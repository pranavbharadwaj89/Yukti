using Xunit;
using Yukti.Domain.ModulePlugin;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T7: mirrors the Web module's own assertion set
/// (assertText/assertVisible/assertHidden/assertEnabled/assertChecked/assertCount/
/// assertAttribute) plus the mobile-specific ones (assertAppInstalled/assertCurrentActivity/
/// assertOrientation/assertKeyboardShown). No live Appium server is available in this
/// environment (the same constraint MOB-T1/T3/T4/T5/T6's own tests document), so none of these
/// actions' actual comparison logic against a real element is exercisable end-to-end here --
/// the same gap WebModule's own equivalent comparisons would have if Playwright weren't able to
/// run headless in this environment either. What IS provable without one: every new action's
/// schema shape, matching the checklist's own list exactly.
/// </summary>
public sealed class MobileAssertionTests
{
    [Fact]
    public void GetSupportedActions_exposes_every_assertion_the_checklist_names()
    {
        var actions = new MobileModule().GetSupportedActions().ToDictionary(a => a.ActionName);

        string[] expected =
        {
            // Mirrors the Web module's own set.
            "assertText", "assertVisible", "assertHidden", "assertEnabled", "assertChecked",
            "assertCount", "assertAttribute",
            // Mobile-specific.
            "assertAppInstalled", "assertCurrentActivity", "assertOrientation", "assertKeyboardShown",
        };

        foreach (var name in expected)
            Assert.True(actions.ContainsKey(name), $"Missing action schema: {name}");
    }

    [Fact]
    public void AssertText_mirrors_the_Web_modules_own_equals_contains_matches_shape()
    {
        var action = Single("assertText");
        AssertParam(action, "selector", required: true);
        AssertParam(action, "expected", required: true);
        AssertParam(action, "mode", required: false);
        AssertParam(action, "exact", required: false);
    }

    [Fact]
    public void AssertAttribute_mirrors_the_Web_modules_own_shape()
    {
        var action = Single("assertAttribute");
        AssertParam(action, "selector", required: true);
        AssertParam(action, "attr", required: true);
        AssertParam(action, "expected", required: true);
        AssertParam(action, "mode", required: false);
    }

    [Fact]
    public void AssertChecked_defaults_to_asserting_checked_true()
    {
        var action = Single("assertChecked");
        var param = AssertParam(action, "checked", required: false);
        Assert.Equal(true, param.DefaultValue);
    }

    [Fact]
    public void AssertCount_takes_a_selector_and_a_numeric_expected_count()
    {
        var action = Single("assertCount");
        AssertParam(action, "selector", required: true);
        var expected = AssertParam(action, "expected", required: true);
        Assert.Equal(ParamType.Number, expected.Type);
    }

    [Fact]
    public void AssertAppInstalled_takes_a_bundleId_cross_platform()
    {
        var action = Single("assertAppInstalled");
        AssertParam(action, "bundleId", required: true);
    }

    [Fact]
    public void AssertCurrentActivity_is_documented_as_Android_only()
    {
        var action = Single("assertCurrentActivity");
        AssertParam(action, "expected", required: true);
        Assert.Contains("Android", action.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssertOrientation_takes_a_Portrait_or_Landscape_expectation()
    {
        var action = Single("assertOrientation");
        AssertParam(action, "expected", required: true);
    }

    [Fact]
    public void AssertKeyboardShown_defaults_to_asserting_shown_true()
    {
        var action = Single("assertKeyboardShown");
        var param = AssertParam(action, "shown", required: false);
        Assert.Equal(true, param.DefaultValue);
    }

    [Fact]
    public void Every_assertion_param_has_a_non_empty_description()
    {
        foreach (var action in new MobileModule().GetSupportedActions())
        {
            if (!action.ActionName.StartsWith("assert", StringComparison.Ordinal))
                continue;
            foreach (var param in action.Parameters)
            {
                Assert.False(string.IsNullOrWhiteSpace(param.Description),
                    $"{action.ActionName}.{param.Name} is missing a Description.");
            }
        }
    }

    private static ActionSchema Single(string actionName) =>
        new MobileModule().GetSupportedActions().Single(a => a.ActionName == actionName);

    private static ParamSpec AssertParam(ActionSchema action, string paramName, bool required)
    {
        var param = Assert.Single(action.Parameters, p => p.Name == paramName);
        Assert.Equal(required, param.Required);
        return param;
    }
}
