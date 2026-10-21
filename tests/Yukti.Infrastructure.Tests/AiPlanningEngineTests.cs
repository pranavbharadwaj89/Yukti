using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Yukti.Application.Ai;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>
/// D5-T3: the planning pipeline's own orchestration — guardrail →
/// render prompt → call provider → validate → retry-with-errors →
/// record cost per attempt → return. Analyst/Coder don't exist yet
/// (D5-T4/T5), so this exercises the engine with a synthetic capability and
/// a hand-rolled validator, the same spirit as D2-T1's synthetic module:
/// proving the pipeline's own contract, not a real capability's business
/// rules. Also covers the token-economy spec
/// (<c>plan/specs/SARVAGHNA-TOKEN-ECONOMY.md</c>) §R1/R3/R4/R5 guarantees:
/// zero-cost rejections before the provider, capped retry-error text, a
/// cost record per attempt (not per call), and no uncapped assembled
/// prompt.
/// </summary>
public sealed class AiPlanningEngineTests
{
    private sealed record TestOutput(string Value);

    private sealed class SequencedProvider : IAiProvider
    {
        private readonly Queue<ProviderResponse> _responses;
        public List<StructuredGenerationRequest> Requests { get; } = new();
        public string Id => "seq-stub";

        public SequencedProvider(params ProviderResponse[] responses) => _responses = new Queue<ProviderResponse>(responses);

        public Task<ProviderResponse> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    /// <summary>A provider that never returns — every call throws, the same contract
    /// <c>GeminiProvider</c>/<c>OpenAiProvider</c> already guarantee for a 429/503/timeout.</summary>
    private sealed class FailingProvider : IAiProvider
    {
        public List<StructuredGenerationRequest> Requests { get; } = new();
        public string Id => "failing-stub";

        public Task<ProviderResponse> GenerateStructuredAsync(StructuredGenerationRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            throw new AiUnavailableException("Simulated provider outage.");
        }
    }

    private sealed class FixedPromptLoader : IPromptLoader
    {
        private readonly string _template;
        public FixedPromptLoader(string template) => _template = template;
        public Task<string> LoadAsync(string capability, string version, CancellationToken ct) => Task.FromResult(_template);
    }

    private sealed class RecordingCostRecorder : IAiCostRecorder
    {
        public List<AiCostRecordData> Records { get; } = new();
        public Task RecordAsync(AiCostRecordData record, CancellationToken ct) { Records.Add(record); return Task.CompletedTask; }
    }

    private sealed class FixedCapabilityOptions : IAiCapabilityOptionsProvider
    {
        private readonly AiCapabilityOptions _options;
        public int MaxValidationRetries { get; }

        public FixedCapabilityOptions(AiCapabilityOptions? options = null, int maxValidationRetries = 1)
        {
            _options = options ?? AiCapabilityOptions.Default;
            MaxValidationRetries = maxValidationRetries;
        }

        public AiCapabilityOptions GetFor(string capability) => _options;
    }

    /// <summary>D5-T9: this file's engine tests are about the generation loop, not the budget
    /// check itself (that's <c>AiBudgetGuardTests</c>) — always-allowed, never-near-cap, so every
    /// existing scenario here keeps meaning exactly what it did before the guard existed.</summary>
    private sealed class AlwaysAllowedBudgetGuard : IAiBudgetGuard
    {
        public Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct) =>
            Task.FromResult(new AiBudgetCheck(Allowed: true, NearCap: false, SpentUsd: 0m, ProjectedUsd: 0m, CapUsd: 1000m));
    }

    /// <summary>DISC-14: lets a test dictate exactly which refusal shape the guard returns, to
    /// prove the engine raises the *right* exception type for which constraint failed.</summary>
    private sealed class FixedResultBudgetGuard : IAiBudgetGuard
    {
        private readonly AiBudgetCheck _result;
        public FixedResultBudgetGuard(AiBudgetCheck result) => _result = result;
        public Task<AiBudgetCheck> CheckAsync(TenantId tenantId, string provider, int estimatedInputTokens, int maxOutputTokens, CancellationToken ct) =>
            Task.FromResult(_result);
    }

    private static ProviderResponse Response(string json, int inputTokens = 10, int outputTokens = 20) =>
        new(json, JsonNode.Parse(json), inputTokens, outputTokens, TimeSpan.FromMilliseconds(5));

    private static AiValidationOutcome<TestOutput> AlwaysValid(JsonNode? node) =>
        AiValidationOutcome<TestOutput>.Valid(new TestOutput(node!["value"]!.GetValue<string>()));

    /// <summary>Adapts a pure, synchronous validator to <see cref="AiPlanningRequest{T}"/>'s async
    /// <c>Validate</c> shape — the engine's contract went async for Coder's real need (calling
    /// <c>Flow.Publish</c>), but most of this test file's validators are deliberately pure.</summary>
    private static Func<JsonNode?, CancellationToken, Task<AiValidationOutcome<T>>> Sync<T>(Func<JsonNode?, AiValidationOutcome<T>> validate) =>
        (node, _) => Task.FromResult(validate(node));

    private static AiPlanningEngine BuildEngine(
        IAiProvider provider, IPromptLoader promptLoader, IAiCostRecorder costs,
        IAiCapabilityOptionsProvider? capabilityOptions = null, IAiBudgetGuard? budgetGuard = null) =>
        new(provider, promptLoader, costs, capabilityOptions ?? new FixedCapabilityOptions(),
            budgetGuard ?? new AlwaysAllowedBudgetGuard(), NullLogger<AiPlanningEngine>.Instance);

    [Fact]
    public async Task Happy_path_calls_the_provider_once_validates_records_cost_and_returns_the_output()
    {
        var provider = new SequencedProvider(Response(@"{""value"":""ok""}", inputTokens: 37, outputTokens: 41));
        var costs = new RecordingCostRecorder();
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs);

        var result = await engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "registry subset here", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None);

        Assert.Equal("ok", result.Output.Value);
        Assert.False(result.UsedRetry);
        Assert.Equal("coder", result.Capability);
        Assert.Equal("seq-stub", result.Provider);
        Assert.Single(provider.Requests);

        // D5-T6: Attempt/Confidence are computed by the engine itself from the same loop —
        // a first-try success is attempt 1 at AiConfidence.FullConfidence, never a literal.
        Assert.Equal(1, result.Attempt);
        Assert.Equal(AiConfidence.FullConfidence, result.Confidence);

        // §R5: exact token-count accounting — the stub's deterministic counts flow straight
        // through to the one cost record this happy path produces.
        Assert.Single(costs.Records);
        Assert.Equal(1, costs.Records[0].Attempt);
        Assert.True(costs.Records[0].Succeeded);
        Assert.Equal(37, costs.Records[0].InputTokens);
        Assert.Equal(41, costs.Records[0].OutputTokens);
    }

    [Fact]
    public async Task Renders_the_CONTEXT_token_and_sends_the_description_separately_from_the_system_prompt()
    {
        var provider = new SequencedProvider(Response(@"{""value"":""ok""}"));
        var engine = BuildEngine(provider, new FixedPromptLoader("System text.\n{{CONTEXT}}\nMore text."), new RecordingCostRecorder());

        await engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "the user's own description", "REGISTRY-SUBSET-XYZ", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None);

        var sent = provider.Requests[0];
        Assert.Contains("REGISTRY-SUBSET-XYZ", sent.SystemPrompt);
        Assert.DoesNotContain("{{CONTEXT}}", sent.SystemPrompt);
        Assert.Equal("the user's own description", sent.UserContent);
    }

    [Fact]
    public async Task Uses_the_capability_options_providers_MaxOutputTokens_not_a_literal()
    {
        var provider = new SequencedProvider(Response(@"{""value"":""ok""}"));
        var options = new AiCapabilityOptions(MaxInputChars: 4000, MaxOutputTokens: 777, Temperature: 0);
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), new RecordingCostRecorder(),
            new FixedCapabilityOptions(options));

        await engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None);

        Assert.Equal(777, provider.Requests[0].MaxTokens);
    }

    [Fact]
    public async Task A_failed_first_attempt_retries_with_the_errors_fed_into_the_prompt_and_writes_one_cost_row_per_attempt()
    {
        var provider = new SequencedProvider(
            Response(@"{""value"":""bad""}", inputTokens: 10, outputTokens: 5),
            Response(@"{""value"":""good""}", inputTokens: 12, outputTokens: 8));
        var costs = new RecordingCostRecorder();
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs);

        AiValidationOutcome<TestOutput> ValidateOnceThenSucceed(JsonNode? node)
        {
            var value = node!["value"]!.GetValue<string>();
            return value == "bad"
                ? AiValidationOutcome<TestOutput>.Invalid(new[] { "value must not be 'bad'" })
                : AiValidationOutcome<TestOutput>.Valid(new TestOutput(value));
        }

        var result = await engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(ValidateOnceThenSucceed)),
            CancellationToken.None);

        Assert.Equal("good", result.Output.Value);
        Assert.True(result.UsedRetry);
        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains("value must not be 'bad'", provider.Requests[1].SystemPrompt);

        // D5-T6: a success on attempt 2 costs real confidence — proven against the exact
        // documented formula, not just "lower than attempt 1".
        Assert.Equal(2, result.Attempt);
        Assert.Equal(AiConfidence.FromAttempt(2), result.Confidence);
        Assert.True(result.Confidence < AiConfidence.FullConfidence);

        // §R5: a run with one retry writes 2 rows, attempt 1 (failed) and attempt 2 (succeeded)
        // — never one combined row.
        Assert.Equal(2, costs.Records.Count);
        Assert.Equal(1, costs.Records[0].Attempt);
        Assert.False(costs.Records[0].Succeeded);
        Assert.Equal(10, costs.Records[0].InputTokens);
        Assert.Equal(2, costs.Records[1].Attempt);
        Assert.True(costs.Records[1].Succeeded);
        Assert.Equal(12, costs.Records[1].InputTokens);
    }

    [Fact]
    public async Task Two_failed_attempts_raise_AiGenerationFailedException_with_the_second_attempts_errors_and_two_cost_rows()
    {
        var provider = new SequencedProvider(
            Response(@"{""value"":""bad1""}"),
            Response(@"{""value"":""bad2""}"));
        AiValidationOutcome<TestOutput> AlwaysInvalid(JsonNode? node) =>
            AiValidationOutcome<TestOutput>.Invalid(new[] { $"still invalid: {node!["value"]!.GetValue<string>()}" });

        var costs = new RecordingCostRecorder();
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs);

        var ex = await Assert.ThrowsAsync<AiGenerationFailedException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(AlwaysInvalid)),
            CancellationToken.None));

        Assert.Equal(2, provider.Requests.Count);
        Assert.Contains("bad2", ex.ValidationErrors[0]);

        Assert.Equal(2, costs.Records.Count);
        Assert.All(costs.Records, r => Assert.False(r.Succeeded));
        Assert.Equal(new[] { 1, 2 }, costs.Records.Select(r => r.Attempt));
    }

    [Fact]
    public async Task The_retry_count_is_configurable_not_hardcoded_to_one()
    {
        // §R3: "keep it at 1, make it config, not a literal" — proven here by setting it to 2
        // and seeing 3 total attempts (1 original + 2 retries), not 2.
        var provider = new SequencedProvider(
            Response(@"{""value"":""bad1""}"), Response(@"{""value"":""bad2""}"), Response(@"{""value"":""bad3""}"));
        AiValidationOutcome<TestOutput> AlwaysInvalid(JsonNode? node) => AiValidationOutcome<TestOutput>.Invalid(new[] { "nope" });

        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), new RecordingCostRecorder(),
            new FixedCapabilityOptions(maxValidationRetries: 2));

        await Assert.ThrowsAsync<AiGenerationFailedException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(AlwaysInvalid)),
            CancellationToken.None));

        Assert.Equal(3, provider.Requests.Count);
    }

    [Fact]
    public async Task Retry_errors_are_capped_at_20_entries_and_2000_characters()
    {
        // §R3: a pathological validator returning hundreds of errors (or one huge one) must
        // never blow up the size of the very next attempt it's supposed to shrink.
        var manyErrors = Enumerable.Range(1, 100).Select(i => $"error number {i} is quite a long description of what went wrong this time around").ToArray();
        var provider = new SequencedProvider(Response(@"{""value"":""bad""}"), Response(@"{""value"":""good""}"));

        var first = true;
        AiValidationOutcome<TestOutput> FailOnceWithManyErrors(JsonNode? node)
        {
            if (first) { first = false; return AiValidationOutcome<TestOutput>.Invalid(manyErrors); }
            return AiValidationOutcome<TestOutput>.Valid(new TestOutput(node!["value"]!.GetValue<string>()));
        }

        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), new RecordingCostRecorder());

        await engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(FailOnceWithManyErrors)),
            CancellationToken.None);

        var retryPrompt = provider.Requests[1].SystemPrompt;
        Assert.DoesNotContain("error number 21 ", retryPrompt); // only the first 20 of 100 survive the count cap
    }

    [Fact]
    public async Task Retry_error_text_is_truncated_at_2000_characters_even_within_the_first_20_errors()
    {
        var fiveHugeErrors = Enumerable.Range(1, 5).Select(i => new string((char)('a' + i), 600)).ToArray();
        var provider = new SequencedProvider(Response(@"{""value"":""bad""}"), Response(@"{""value"":""good""}"));

        var first = true;
        AiValidationOutcome<TestOutput> FailOnceWithHugeErrors(JsonNode? node)
        {
            if (first) { first = false; return AiValidationOutcome<TestOutput>.Invalid(fiveHugeErrors); }
            return AiValidationOutcome<TestOutput>.Valid(new TestOutput(node!["value"]!.GetValue<string>()));
        }

        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), new RecordingCostRecorder());

        await engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(FailOnceWithHugeErrors)),
            CancellationToken.None);

        var retryPrompt = provider.Requests[1].SystemPrompt;
        var errorMarkerIndex = retryPrompt.IndexOf("Fix them and return corrected output:\n", StringComparison.Ordinal)
            + "Fix them and return corrected output:\n".Length;
        var errorSection = retryPrompt[errorMarkerIndex..];

        Assert.Equal(2000, errorSection.Length);
        Assert.DoesNotContain(new string((char)('a' + 5), 600), retryPrompt); // the 5th huge error never fully arrives
    }

    [Fact]
    public async Task An_empty_description_is_rejected_before_the_provider_is_ever_called_and_no_cost_is_recorded()
    {
        var provider = new SequencedProvider();
        var costs = new RecordingCostRecorder();
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs);

        await Assert.ThrowsAsync<AiGuardrailRejectedException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "", "ctx", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None));

        Assert.Empty(provider.Requests);
        Assert.Empty(costs.Records);
    }

    [Fact]
    public async Task A_request_with_no_registered_modules_is_rejected_before_the_provider_is_ever_called()
    {
        // §R1: "no modules registered" is a zero-cost guardrail reject, just like an empty
        // description — an empty/whitespace PromptContext means there's nothing for the AI to
        // build on, so the request never reaches the provider.
        var provider = new SequencedProvider();
        var costs = new RecordingCostRecorder();
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs);

        var ex = await Assert.ThrowsAsync<AiGuardrailRejectedException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "   ", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None));

        Assert.Contains("no modules are registered", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.Requests);
        Assert.Empty(costs.Records);
    }

    [Fact]
    public async Task An_assembled_prompt_over_the_capabilitys_MaxInputChars_is_rejected_before_the_provider_is_ever_called()
    {
        // §R4: "A prompt over MaxInputChars after context assembly fails fast... it never
        // silently truncates the user's request." The cap is on the *assembled* prompt
        // (template + substituted context + description), not the raw description alone.
        var provider = new SequencedProvider();
        var costs = new RecordingCostRecorder();
        var tinyCap = new AiCapabilityOptions(MaxInputChars: 50, MaxOutputTokens: 100, Temperature: 0);
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs,
            new FixedCapabilityOptions(tinyCap));

        var hugeContext = new string('x', 500);
        var ex = await Assert.ThrowsAsync<AiGuardrailRejectedException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", hugeContext, TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None));

        Assert.Contains("50", ex.Message);
        Assert.Empty(provider.Requests);
        Assert.Empty(costs.Records);
    }

    [Fact]
    public async Task A_missing_prompt_file_raises_PromptNotFoundException_not_a_provider_call()
    {
        var provider = new SequencedProvider();
        var engine = BuildEngine(provider, new FilePromptLoaderThatAlwaysMisses(), new RecordingCostRecorder());

        await Assert.ThrowsAsync<PromptNotFoundException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("ghost-capability", "v1", "do the thing", "ctx", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None));

        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task A_cost_cap_refusal_raises_AiBudgetExceededException_not_the_quota_one()
    {
        // DISC-14: QuotaExceeded false — this is the pre-existing §R6 dollar-cap refusal, unchanged.
        var provider = new SequencedProvider();
        var costs = new RecordingCostRecorder();
        var guard = new FixedResultBudgetGuard(new AiBudgetCheck(
            Allowed: false, NearCap: true, SpentUsd: 29m, ProjectedUsd: 2m, CapUsd: 30m, QuotaExceeded: false));
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs, budgetGuard: guard);

        await Assert.ThrowsAsync<AiBudgetExceededException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None));

        Assert.Empty(provider.Requests);
        Assert.Single(costs.Records); // the zero-cost budget-refusal row still gets written
        Assert.True(costs.Records[0].BudgetRefused);
    }

    [Fact]
    public async Task A_request_count_quota_refusal_raises_AiRequestQuotaExceededException_not_the_dollar_one()
    {
        // DISC-14: the real-world case — a free-tier key's daily request ceiling hit while cost is
        // nowhere near the dollar cap; the engine must raise the quota-specific exception, not the
        // generic budget one, so callers (and the owner reading the error) see the real cause.
        var provider = new SequencedProvider();
        var costs = new RecordingCostRecorder();
        var guard = new FixedResultBudgetGuard(new AiBudgetCheck(
            Allowed: false, NearCap: false, SpentUsd: 0.03m, ProjectedUsd: 0m, CapUsd: 30m,
            QuotaExceeded: true, RequestsToday: 20, DailyRequestQuota: 20));
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs, budgetGuard: guard);

        var ex = await Assert.ThrowsAsync<AiRequestQuotaExceededException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None));

        Assert.Contains("20 of 20", ex.Message);
        Assert.Empty(provider.Requests);
        Assert.Single(costs.Records);
        Assert.True(costs.Records[0].BudgetRefused);
    }

    [Fact]
    public async Task A_provider_failure_still_writes_a_cost_and_quota_record_before_the_exception_propagates()
    {
        // The real bug this guards against: a 429/503/timeout from the real provider used to
        // vanish from both ledgers entirely (GenerateStructuredAsync throwing skipped the
        // RecordAsync call below it) — discovered the hard way when the budget guard kept letting
        // real calls through against an already-exhausted free-tier quota, because its own local
        // count had never seen any of the failed attempts that actually happened.
        var provider = new FailingProvider();
        var costs = new RecordingCostRecorder();
        var engine = BuildEngine(provider, new FixedPromptLoader("Instructions.\n{{CONTEXT}}"), costs);

        await Assert.ThrowsAsync<AiUnavailableException>(() => engine.PlanAsync(
            new AiPlanningRequest<TestOutput>("coder", "v1", "do the thing", "ctx", TenantId.New(), Sync(AlwaysValid)),
            CancellationToken.None));

        Assert.Single(provider.Requests); // the call genuinely reached the provider once
        Assert.Single(costs.Records); // ...and that real attempt is on the ledger, not lost
        Assert.False(costs.Records[0].Succeeded);
        Assert.False(costs.Records[0].BudgetRefused); // distinct from a pre-emptive refusal — this one really tried
        Assert.Equal(0, costs.Records[0].InputTokens); // unknown on a failed call — 0, never guessed
        Assert.Equal(0, costs.Records[0].OutputTokens);
    }

    private sealed class FilePromptLoaderThatAlwaysMisses : IPromptLoader
    {
        public Task<string> LoadAsync(string capability, string version, CancellationToken ct) =>
            throw new PromptNotFoundException(capability, version);
    }
}
