using System.Reflection;
using Xunit;
using Yukti.Contracts;
using Yukti.Infrastructure.InMemory.Modules;
using ExecutionContext = Yukti.Contracts.ExecutionContext;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Reflection-based "type inspection" evidence for the FR-PLUGIN
/// requirements that are structural properties of the interfaces
/// themselves, not behavior — FR-PLUGIN-01/02/03/07. These fail loudly
/// the moment someone widens IAutomationModule/ExecutionContext/
/// ICredentialResolver's surface, or gives a built-in module mutable
/// instance state, without anyone having to notice in review.
/// </summary>
public sealed class ModulePluginContractTests
{
    [Fact]
    public void IAutomationModule_exposes_exactly_the_six_FR_PLUGIN_01_members()
    {
        var memberNames = typeof(IAutomationModule).GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m is not MethodInfo method || !method.IsSpecialName) // excludes property get_/set_ accessor MethodInfos — Kind/ContractVersion already counted once as PropertyInfo
            .Select(m => m.Name)
            .ToHashSet();

        var expected = new[] { "Kind", "ContractVersion", "GetSupportedActions", "Setup", "Run", "Teardown" };

        Assert.Equal(expected.OrderBy(n => n), memberNames.OrderBy(n => n));
    }

    [Fact]
    public void ExecutionContext_exposes_exactly_the_four_FR_PLUGIN_02_properties()
    {
        var propertyNames = typeof(ExecutionContext).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        var expected = new[] { "RunId", "Variables", "Credentials", "RunCancellation" };

        Assert.Equal(expected.OrderBy(n => n), propertyNames.OrderBy(n => n));
    }

    [Fact]
    public void ICredentialResolver_exposes_only_named_lookup_no_enumeration()
    {
        // FR-PLUGIN-03: "no enumeration capability" — structurally true iff
        // ResolveAsync(string, CancellationToken) is the interface's only
        // member; anything like GetAll()/Keys/an indexer would violate it.
        var members = typeof(ICredentialResolver).GetMembers(BindingFlags.Public | BindingFlags.Instance);

        var member = Assert.Single(members);
        Assert.Equal("ResolveAsync", member.Name);
    }

    [Theory]
    [InlineData(typeof(ApiModule))]
    [InlineData(typeof(LogsModule))]
    public void BuiltIn_modules_hold_zero_mutable_instance_state(Type moduleType)
    {
        // FR-PLUGIN-07: Singleton-safe under concurrent FlowRuns iff there
        // is no per-instance mutable field a concurrent Run() could race
        // on. Static fields (e.g. ApiModule's shared HttpClient) are fine —
        // they're not per-invocation state.
        var instanceFields = moduleType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.Empty(instanceFields);
    }
}
