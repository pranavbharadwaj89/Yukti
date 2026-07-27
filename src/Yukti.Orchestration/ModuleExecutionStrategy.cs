using Yukti.Contracts;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration;

/// <summary>
/// FR-PLUGIN-04 (Volume 1 Part III §18.5): the seam between "dispatch a
/// module action" and "how that action actually runs" — in-process for
/// trusted code, isolated for untrusted code. Kept separate from
/// IAutomationModule.Run itself so a module's own code never knows or
/// cares which strategy invoked it.
/// </summary>
public interface IModuleExecutionStrategy
{
    Task<StepOutcome> Execute(IAutomationModule module, string action,
        IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct);
}

/// <summary>Built-in/Verified-tier modules — direct in-process call, exactly
/// what every module invocation did before trust tiers existed.</summary>
public sealed class InProcessExecutionStrategy : IModuleExecutionStrategy
{
    public Task<StepOutcome> Execute(IAutomationModule module, string action,
        IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
        module.Run(action, parameters, ctx, ct);
}

/// <summary>
/// Community-tier modules — isolated sandboxed process over a narrow RPC
/// boundary, per FR-PLUGIN-04. NOT YET IMPLEMENTED: which sandbox
/// technology (Kubernetes Job-per-invocation vs. gVisor vs. Firecracker
/// microVM) is an open spec question (Q-01 in
/// docs/specification/product/INIT-YUKTI-BACKEND-001.md's "Spec questions"
/// section) requiring PM/tech-lead confirmation before this can be built —
/// the PRD's own FR-PLUGIN-04 row marks its evidence type "integration
/// (deferred — sandbox not yet built, see Open Questions)". This class
/// exists so trust-tier-to-strategy *selection* (ModuleExecutionStrategySelector)
/// is real and testable today, with a clear, loud failure at the one seam
/// that genuinely can't be built yet — never a silent in-process fallback
/// that would defeat the isolation guarantee Community-tier exists for.
/// </summary>
public sealed class SandboxedExecutionStrategy : IModuleExecutionStrategy
{
    public Task<StepOutcome> Execute(IAutomationModule module, string action,
        IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct) =>
        throw new NotSupportedException(
            "Community-tier sandboxed module execution is not yet implemented — blocked on Q-01 " +
            "(sandbox technology: Kubernetes Job-per-invocation vs. gVisor vs. Firecracker microVM), " +
            "an open spec question in INIT-YUKTI-BACKEND-001.md requiring PM/tech-lead confirmation. " +
            "Register Community-tier modules only once this strategy has a real implementation.");
}
