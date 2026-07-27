using Microsoft.Extensions.Logging;
using Yukti.Application.Abstractions;
using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration;

/// <summary>
/// FR-PLUGIN-04: routes every dispatch through the trust-tiered strategy
/// (InProcessExecutionStrategy for Built-in/Verified, SandboxedExecutionStrategy
/// for Community) matching the module's registered TrustTier, instead of
/// always calling target.Run directly. Trust tier is looked up with
/// tenantId: null (the global/built-in registration scope) — every module
/// registered in this codebase today is Built-in, so this is correct as
/// far as it's exercised; genuinely tenant-scoped Community-tier module
/// resolution is part of the same Q-01-blocked follow-up as
/// SandboxedExecutionStrategy itself (see its doc comment), not a gap
/// introduced here.
/// </summary>
public sealed class ModuleDispatcher : IModuleDispatcher
{
    private readonly IModuleRegistry _registry;
    private readonly IModuleRegistrationRepository _registrations;
    private readonly IModuleExecutionStrategySelector _strategySelector;
    private readonly ILogger<ModuleDispatcher> _logger;

    public ModuleDispatcher(IModuleRegistry registry, IModuleRegistrationRepository registrations,
        IModuleExecutionStrategySelector strategySelector, ILogger<ModuleDispatcher> logger)
    {
        _registry = registry;
        _registrations = registrations;
        _strategySelector = strategySelector;
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

        var registration = await _registrations.GetByKind(module, tenantId: null, ct);
        var trust = registration?.Trust ?? TrustTier.BuiltIn;
        var strategy = _strategySelector.SelectFor(trust);

        return await strategy.Execute(target, action, parameters, ctx, ct);
    }
}
