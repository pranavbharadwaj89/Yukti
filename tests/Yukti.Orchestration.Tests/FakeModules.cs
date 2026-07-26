using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Stands in for a real target-system module (e.g. ApiModule making an
/// authenticated HTTP call): resolves a credential and uses it, exactly
/// like a real integration would, but — being well-behaved — never returns
/// or logs the resolved value itself. FR-LOG-04's test asserts the secret
/// never appears anywhere in the log stream despite this real resolve call
/// happening mid-FlowRun.
/// </summary>
public sealed class SecretTouchingModule : IAutomationModule
{
    public static readonly ModuleKind Kind = ModuleKind.Custom("secret-touching");
    ModuleKind IAutomationModule.Kind => Kind;
    public string ContractVersion => "1.0.0";

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema { ActionName = "call", Description = "Resolves a credential and 'calls' a target system with it.", Parameters = Array.Empty<ParamSpec>() }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public async Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        var secret = await ctx.Credentials.ResolveAsync("target-system-api-key", ct);
        // Real usage: secret goes into an Authorization header, never into
        // the returned message/data — that boundary is exactly what this
        // module exists to model for the FR-LOG-04 test.
        var authorized = secret is not null;
        return StepOutcome.Passed(authorized ? "Authenticated call succeeded" : "No credential configured");
    }
}

/// <summary>Fails on its first invocation, then passes — the minimal shape
/// RetryFlakeHandler needs to classify a step as flaky (FR-LOG-02).</summary>
public sealed class FlakyModule : IAutomationModule
{
    public static readonly ModuleKind Kind = ModuleKind.Custom("flaky");
    ModuleKind IAutomationModule.Kind => Kind;
    public string ContractVersion => "1.0.0";

    private int _calls;

    public IReadOnlyList<ActionSchema> GetSupportedActions() => new[]
    {
        new ActionSchema { ActionName = "flake", Description = "Fails once, then passes.", Parameters = Array.Empty<ParamSpec>() }
    };

    public Task Setup(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task Teardown(ExecutionContext ctx, CancellationToken ct) => Task.CompletedTask;

    public Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        _calls++;
        return Task.FromResult(_calls == 1
            ? StepOutcome.Failed("transient failure")
            : StepOutcome.Passed("succeeded on retry"));
    }
}
