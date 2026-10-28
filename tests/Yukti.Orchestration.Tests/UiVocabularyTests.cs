using Xunit;
using Yukti.Domain.UiVocabulary;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T1: proves the shared UI vocabulary layer
/// (Yukti.Domain.UiVocabulary) is genuinely engine-neutral — the hard rule the Mobile module's
/// whole plan depends on holding.
/// </summary>
public sealed class UiVocabularyTests
{
    [Fact]
    public void Yukti_Domain_never_references_Playwright()
    {
        var domainAssembly = typeof(Locator).Assembly;
        Assert.Equal("Yukti.Domain", domainAssembly.GetName().Name);

        var referenced = domainAssembly.GetReferencedAssemblies();
        Assert.DoesNotContain(referenced, a => a.Name!.Contains("Playwright", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Every_strategy_builds_a_resolvable_locator_with_fallbacks()
    {
        var primary = new LocatorSpec(LocatorStrategy.TestId, "submit-button", null);
        var fallback = new LocatorSpec(LocatorStrategy.Role, "button", "Submit");
        var locator = new Locator(primary, new[] { fallback }, "Checkout submit button", Within: null, Nth: null);

        Assert.Equal(LocatorStrategy.TestId, locator.Primary.Strategy);
        Assert.Single(locator.Fallbacks);
        Assert.Equal(LocatorStrategy.Role, locator.Fallbacks[0].Strategy);
    }

    [Fact]
    public void A_locator_chains_within_a_parent()
    {
        var parent = new Locator(new LocatorSpec(LocatorStrategy.Css, "form.checkout", null), Array.Empty<LocatorSpec>(), null, null, null);
        var child = new Locator(new LocatorSpec(LocatorStrategy.TestId, "submit-button", null), Array.Empty<LocatorSpec>(), null, parent, null);

        Assert.Same(parent, child.Within);
    }

    /// <summary>
    /// WEB-T1's own design test: a stub "fake engine" — no browser, no Playwright type anywhere
    /// in its signature or body — that still implements something real against every
    /// <see cref="WaitCondition"/> case. If this compiles and runs, the vocabulary layer carries
    /// enough information to drive a real engine without leaking one.
    /// </summary>
    private sealed class FakeEngine
    {
        public string Describe(WaitCondition condition) => condition switch
        {
            Visible c => $"wait for {Describe(c.L)} to be visible",
            Hidden c => $"wait for {Describe(c.L)} to be hidden",
            Enabled c => $"wait for {Describe(c.L)} to be enabled",
            Disabled c => $"wait for {Describe(c.L)} to be disabled",
            TextEquals c => $"wait for {Describe(c.L)} text to equal '{c.Expected}'",
            TextContains c => $"wait for {Describe(c.L)} text to contain '{c.Fragment}'",
            AttributeEquals c => $"wait for {Describe(c.L)} attribute '{c.Attr}' to equal '{c.Expected}'",
            CountEquals c => $"wait for {Describe(c.L)} count to equal {c.Expected}",
            UrlEquals c => $"wait for url to equal '{c.Expected}'",
            UrlContains c => $"wait for url to contain '{c.Fragment}'",
            TitleContains c => $"wait for title to contain '{c.Fragment}'",
            NetworkIdle c => $"wait for network idle for {c.Quiet.TotalMilliseconds}ms",
            CustomPredicate c => $"wait for script '{c.Script}' to return true",
            _ => throw new ArgumentOutOfRangeException(nameof(condition)),
        };

        private string Describe(Locator locator) =>
            locator.Description ?? $"{locator.Primary.Strategy}:{locator.Primary.Value}";
    }

    [Theory]
    [MemberData(nameof(EveryWaitCondition))]
    public void The_fake_engine_describes_every_wait_condition_with_no_browser(WaitCondition condition)
    {
        var engine = new FakeEngine();
        var description = engine.Describe(condition);
        Assert.False(string.IsNullOrWhiteSpace(description));
    }

    public static IEnumerable<object[]> EveryWaitCondition()
    {
        var locator = new Locator(new LocatorSpec(LocatorStrategy.TestId, "order-id", null), Array.Empty<LocatorSpec>(), "order id", null, null);
        yield return new object[] { new Visible(locator) };
        yield return new object[] { new Hidden(locator) };
        yield return new object[] { new Enabled(locator) };
        yield return new object[] { new Disabled(locator) };
        yield return new object[] { new TextEquals(locator, "confirmed") };
        yield return new object[] { new TextContains(locator, "confirm") };
        yield return new object[] { new AttributeEquals(locator, "aria-busy", "false") };
        yield return new object[] { new CountEquals(locator, 3) };
        yield return new object[] { new UrlEquals("https://example.com/orders") };
        yield return new object[] { new UrlContains("/orders") };
        yield return new object[] { new TitleContains("Orders") };
        yield return new object[] { new NetworkIdle(TimeSpan.FromMilliseconds(500)) };
        yield return new object[] { new CustomPredicate("() => document.readyState === 'complete'") };
    }

    [Fact]
    public void Out_of_range_nth_is_representable_but_not_yet_resolved_here()
    {
        // WEB-T3 resolves Nth against real matches; WEB-T1 only needs the shape to carry it.
        var locator = new Locator(new LocatorSpec(LocatorStrategy.Css, "li.item", null), Array.Empty<LocatorSpec>(), null, null, Nth: 99);
        Assert.Equal(99, locator.Nth);
    }
}
