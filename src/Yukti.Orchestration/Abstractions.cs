using Yukti.Contracts;
using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration;

/// <summary>The runtime map of registered IAutomationModule instances. (Volume 1 Part I §5.3)</summary>
public interface IModuleRegistry
{
    void Register(IAutomationModule module);
    IAutomationModule? Resolve(ModuleKind kind);
    IReadOnlyList<IAutomationModule> All { get; }
}

/// <summary>
/// The only component that branches on trust tier (deferred in this
/// session's scope — direct in-process dispatch only). Resolves the target
/// module and invokes it. (Volume 1 Part I §5.3, Part III §18.5)
/// </summary>
public interface IModuleDispatcher
{
    Task<StepOutcome> Dispatch(ModuleKind module, string action, IReadOnlyDictionary<string, object?> parameters,
        ExecutionContext ctx, CancellationToken ct);
}

/// <summary>Resolves {{vars.x.y}} interpolation and `when` condition expressions. (Volume 1 Part I §5.3, Part III §19.4/19.6)</summary>
public interface IVariableStore
{
    IReadOnlyDictionary<string, object?> Interpolate(IReadOnlyDictionary<string, object?> parameters, IReadOnlyDictionary<string, object?> vars);
    bool EvaluateCondition(string? whenCondition, IReadOnlyDictionary<string, object?> vars);
}

public sealed record RetryPolicy(int MaxAttempts, TimeSpan InitialBackoff, double BackoffMultiplier)
{
    public static RetryPolicy None => new(MaxAttempts: 1, InitialBackoff: TimeSpan.Zero, BackoffMultiplier: 1.0);
}

public sealed record RetryOutcome(StepOutcome FinalOutcome, IReadOnlyList<RetryAttempt> Attempts);

/// <summary>Implements FR-ORCH-5's retry/backoff and flake-vs-genuine-failure classification. (Volume 1 Part III §19.5)</summary>
public interface IRetryFlakeHandler
{
    Task<RetryOutcome> ExecuteWithRetry(Func<CancellationToken, Task<StepOutcome>> action, RetryPolicy policy, CancellationToken ct);
}
