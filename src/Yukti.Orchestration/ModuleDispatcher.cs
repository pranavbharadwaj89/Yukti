using Microsoft.Extensions.Logging;
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
    private readonly ILogger<ModuleDispatcher> _logger;

    public ModuleDispatcher(IModuleRegistry registry, ILogger<ModuleDispatcher> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<StepOutcome> Dispatch(ModuleKind module, string action,
        IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct)
    {
        // FR-LOG-04: only parameter *names* are ever logged, never values —
        // a step's params can legitimately carry credential references or
        // request bodies, neither of which belongs in a log line.
        _logger.LogDebug("Dispatching {Module}.{Action} with parameters [{ParamNames}]",
            module, action, string.Join(", ", parameters.Keys));

        var target = _registry.Resolve(module);
        if (target is null)
        {
            _logger.LogError("No module registered for kind {Module}", module);
            throw new InvalidOperationException($"No module registered for kind '{module}'.");
        }

        if (!target.GetSupportedActions().Any(a => a.ActionName == action))
        {
            _logger.LogError("Module {Module} does not support action {Action}", module, action);
            return StepOutcome.Failed($"Module '{module}' does not support action '{action}'.");
        }

        return await target.Run(action, parameters, ctx, ct);
    }
}
