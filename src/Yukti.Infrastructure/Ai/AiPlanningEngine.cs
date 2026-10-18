using System.Linq;
using Microsoft.Extensions.Logging;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>
/// D5-T3: the concrete <see cref="IAiPlanningEngine"/> — guardrail
/// pre-check → render the versioned prompt → call the provider → validate
/// the output (configurable retries, default 1, errors fed back into the
/// prompt) → record cost per attempt → return. See the interface's own doc
/// comment for how this maps to, and deliberately narrows, the blueprint's
/// seven-stage version.
///
/// A rendered prompt has exactly one template token, <c>{{CONTEXT}}</c> —
/// the caller's own registry subset, substituted verbatim. The user's
/// description is never folded into the template text; it goes to the
/// provider as its own user-role content (<see
/// cref="StructuredGenerationRequest.UserContent"/>), the same separation
/// D5-T1's provider contract already keeps.
///
/// Token-economy spec (<c>plan/specs/SARVAGHNA-TOKEN-ECONOMY.md</c>) §R1/R3/R4/R5 are enforced
/// here: nothing reaches <see cref="IAiProvider"/> for an empty description, a description over
/// the guardrail's own length cap, a description that was only a redacted secret, an empty
/// registry context ("no modules registered"), or an assembled prompt over the capability's
/// configured <see cref="AiCapabilityOptions.MaxInputChars"/> — all typed, zero-cost rejections.
/// Every attempt that *does* reach the provider writes its own cost record (§R5), and the retry
/// prompt's error list is capped so a pathological validator can't blow up the next attempt's
/// own size (§R3).
/// </summary>
public sealed class AiPlanningEngine : IAiPlanningEngine
{
    private const string ContextToken = "{{CONTEXT}}";
    private const int MaxRetryErrors = 20;
    private const int MaxRetryErrorsChars = 2000;

    private readonly IAiProvider _provider;
    private readonly IPromptLoader _promptLoader;
    private readonly IAiCostRecorder _costRecorder;
    private readonly IAiCapabilityOptionsProvider _capabilityOptions;
    private readonly ILogger<AiPlanningEngine> _logger;

    public AiPlanningEngine(
        IAiProvider provider,
        IPromptLoader promptLoader,
        IAiCostRecorder costRecorder,
        IAiCapabilityOptionsProvider capabilityOptions,
        ILogger<AiPlanningEngine> logger)
    {
        _provider = provider;
        _promptLoader = promptLoader;
        _costRecorder = costRecorder;
        _capabilityOptions = capabilityOptions;
        _logger = logger;
    }

    public async Task<AiPlanningResult<TOutput>> PlanAsync<TOutput>(AiPlanningRequest<TOutput> request, CancellationToken ct)
    {
        var (description, guardrailChecks) = AiGuardrailPreCheck.Apply(request.Description);

        if (string.IsNullOrWhiteSpace(request.PromptContext))
        {
            throw new AiGuardrailRejectedException(
                $"No modules are registered for capability '{request.Capability}' — nothing for the AI to work with.");
        }

        var options = _capabilityOptions.GetFor(request.Capability);

        var template = await _promptLoader.LoadAsync(request.Capability, request.PromptVersion, ct);
        var basePrompt = template.Replace(ContextToken, request.PromptContext);

        var assembledLength = basePrompt.Length + description.Length;
        if (assembledLength > options.MaxInputChars)
        {
            throw new AiGuardrailRejectedException(
                $"Assembled prompt for capability '{request.Capability}' is {assembledLength} characters, " +
                $"exceeding its configured cap of {options.MaxInputChars} — the request was not sent.");
        }

        var maxRetries = _capabilityOptions.MaxValidationRetries;
        var currentPrompt = basePrompt;
        IReadOnlyList<string> lastErrors = Array.Empty<string>();

        for (var attempt = 1; attempt <= maxRetries + 1; attempt++)
        {
            var response = await _provider.GenerateStructuredAsync(
                new StructuredGenerationRequest(request.Capability, currentPrompt, description, JsonSchema: "", options.MaxOutputTokens, request.Timeout),
                ct);
            var outcome = await request.Validate(response.Parsed, ct);

            await _costRecorder.RecordAsync(
                new AiCostRecordData(request.Capability, _provider.Id, request.PromptVersion, attempt, outcome.IsValid,
                    response.InputTokens, response.OutputTokens, response.Latency, DateTimeOffset.UtcNow),
                ct);

            if (outcome.IsValid)
            {
                return new AiPlanningResult<TOutput>(
                    outcome.Output!, request.Capability, request.PromptVersion, _provider.Id,
                    response.InputTokens, response.OutputTokens, response.Latency, guardrailChecks,
                    UsedRetry: attempt > 1, Attempt: attempt, Confidence: AiConfidence.FromAttempt(attempt));
            }

            lastErrors = outcome.Errors;
            if (attempt > maxRetries)
            {
                break;
            }

            _logger.LogWarning(
                "AI generation for capability {Capability} failed validation on attempt {Attempt} ({ErrorCount} errors) — retrying with the errors fed back",
                request.Capability, attempt, outcome.Errors.Count);

            currentPrompt = basePrompt
                + "\n\n---\nYour previous attempt failed validation with these errors. Fix them and return corrected output:\n"
                + FormatRetryErrors(outcome.Errors);
        }

        throw new AiGenerationFailedException(lastErrors);
    }

    /// <summary>
    /// Token-economy spec §R3: the retry prompt only ever grows by the error list itself, capped
    /// at <see cref="MaxRetryErrors"/> errors and <see cref="MaxRetryErrorsChars"/> characters — a
    /// validator that returns hundreds of errors (or one enormous one) can't blow up the size of
    /// the very next attempt it's supposed to shrink.
    /// </summary>
    private static string FormatRetryErrors(IReadOnlyList<string> errors)
    {
        var text = string.Join("\n", errors.Take(MaxRetryErrors).Select(e => $"- {e}"));
        return text.Length > MaxRetryErrorsChars ? text[..MaxRetryErrorsChars] : text;
    }
}
