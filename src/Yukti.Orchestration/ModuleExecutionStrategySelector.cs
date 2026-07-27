using Yukti.Domain.ModulePlugin;

namespace Yukti.Orchestration;

/// <summary>FR-PLUGIN-04's trust-tier-to-strategy mapping, as its own
/// testable unit — Built-in/Verified run in-process, Community is
/// sandboxed (see SandboxedExecutionStrategy for why that one still
/// throws).</summary>
public interface IModuleExecutionStrategySelector
{
    IModuleExecutionStrategy SelectFor(TrustTier trust);
}

public sealed class ModuleExecutionStrategySelector : IModuleExecutionStrategySelector
{
    private static readonly InProcessExecutionStrategy InProcess = new();
    private static readonly SandboxedExecutionStrategy Sandboxed = new();

    public IModuleExecutionStrategy SelectFor(TrustTier trust) => trust switch
    {
        TrustTier.BuiltIn or TrustTier.Verified => InProcess,
        TrustTier.Community => Sandboxed,
        _ => throw new ArgumentOutOfRangeException(nameof(trust), trust, "Unknown TrustTier.")
    };
}
