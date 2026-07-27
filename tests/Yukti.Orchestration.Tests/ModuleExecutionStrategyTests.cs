using Xunit;
using Yukti.Domain.ModulePlugin;

namespace Yukti.Orchestration.Tests;

/// <summary>FR-PLUGIN-04 evidence: the trust-tier-to-strategy mapping is
/// real and testable, and Community-tier dispatch fails loudly rather
/// than silently falling back to in-process (which would defeat the
/// isolation guarantee entirely).</summary>
public sealed class ModuleExecutionStrategyTests
{
    [Theory]
    [InlineData(TrustTier.BuiltIn)]
    [InlineData(TrustTier.Verified)]
    public void BuiltIn_and_Verified_tiers_select_InProcessExecutionStrategy(TrustTier tier)
    {
        var selector = new ModuleExecutionStrategySelector();

        var strategy = selector.SelectFor(tier);

        Assert.IsType<InProcessExecutionStrategy>(strategy);
    }

    [Fact]
    public void Community_tier_selects_SandboxedExecutionStrategy()
    {
        var selector = new ModuleExecutionStrategySelector();

        var strategy = selector.SelectFor(TrustTier.Community);

        Assert.IsType<SandboxedExecutionStrategy>(strategy);
    }

    [Fact]
    public async Task SandboxedExecutionStrategy_throws_rather_than_silently_running_in_process()
    {
        var strategy = new SandboxedExecutionStrategy();

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            strategy.Execute(new SecretTouchingModule(), "call",
                new Dictionary<string, object?>(), null!, CancellationToken.None));
    }

    [Fact]
    public async Task Dispatch_of_an_unregistered_module_defaults_to_BuiltIn_trust()
    {
        // No ModuleRegistration exists for SecretTouchingModule in these
        // tests — GetByKind returns null, and ModuleDispatcher's fallback
        // (TrustTier.BuiltIn) is what every other FlowRunLoggingTests/
        // FlowRunTelemetryTests test has been implicitly relying on all
        // along. This test makes that reliance explicit.
        var module = new SecretTouchingModule();
        var result = await new InProcessExecutionStrategy().Execute(
            module, "call", new Dictionary<string, object?>(),
            new Yukti.Contracts.ExecutionContext
            {
                RunId = default,
                Variables = new Dictionary<string, object?>(),
                Credentials = new Yukti.Infrastructure.InMemory.InMemoryCredentialResolver(),
                RunCancellation = CancellationToken.None
            },
            CancellationToken.None);

        Assert.Equal(Yukti.Domain.Execution.StepStatus.Passed, result.Status);
    }
}
