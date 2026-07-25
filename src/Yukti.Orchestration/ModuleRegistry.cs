using System.Collections.Concurrent;
using Yukti.Contracts;
using Yukti.Domain.ModulePlugin;

namespace Yukti.Orchestration;

public sealed class ModuleRegistry : IModuleRegistry
{
    private readonly ConcurrentDictionary<string, IAutomationModule> _modules = new();

    public void Register(IAutomationModule module) => _modules[module.Kind.Value] = module;

    public IAutomationModule? Resolve(ModuleKind kind) =>
        _modules.TryGetValue(kind.Value, out var module) ? module : null;

    public IReadOnlyList<IAutomationModule> All => _modules.Values.ToList().AsReadOnly();
}
