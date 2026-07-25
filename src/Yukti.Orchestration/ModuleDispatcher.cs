using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration;

/// <summary>
/// In-process dispatch only, for this session's scope. The trust-tiered
/// selection between InProcessExecutionStrategy and SandboxedExecutionStrategy
/// (Volume 1 Part III §18.5) is deferred to a follow-up pass once
/// marketplace/Community-tier modules are in scope.
/// </summary>
public sealed class ModuleDispatcher : IModuleDispatcher
{
    private readonly IModuleRegistry _registry;

    public ModuleDispatcher(IModuleRegistry registry) => _registry = registry;

    public async Task<StepOutcome> Dispatch(ModuleKind module, string action,
        IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        var target = _registry.Resolve(module)
            ?? throw new InvalidOperationException($"No module registered for kind '{module}'.");

        if (!target.GetSupportedActions().Any(a => a.ActionName == action))
            return StepOutcome.Failed($"Module '{module}' does not support action '{action}'.");

        return await target.Run(action, parameters, ctx, ct);
    }
}
