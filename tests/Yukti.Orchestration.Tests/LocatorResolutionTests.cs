using Microsoft.Playwright;
using Xunit;
using Yukti.Domain.UiVocabulary;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Web UI Testing Module Spec, WEB-T3: proves PlaywrightLocatorResolver directly against a real
/// headless Chromium page — standalone, the same way FixtureAppTests proves FixtureApp before
/// any module drives it. WEB-T4 (not yet built) is what wires this into WebModule's real
/// actions; this file only proves the resolution algorithm itself.
/// </summary>
public sealed class LocatorResolutionTests : IAsyncLifetime
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    private const string Html = """
        <!doctype html>
        <html>
        <body>
          <div id="panel">
            <button data-testid="submit-btn">Go</button>
          </div>
          <label for="username">Username</label>
          <input id="username" placeholder="Enter your username" />
          <p>Welcome back, friend</p>
          <ul>
            <li class="item">Item 1</li>
            <li class="item">Item 2</li>
            <li class="item">Item 3</li>
          </ul>
        </body>
        </html>
        """;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        _page = await _browser.NewPageAsync();
        await _page.SetContentAsync(Html);
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    private static Locator Simple(LocatorStrategy strategy, string value, string? name = null, string? description = "a locator") =>
        new(new LocatorSpec(strategy, value, name), Array.Empty<LocatorSpec>(), description, null, null);

    [Theory]
    [InlineData(LocatorStrategy.TestId, "submit-btn", null)]
    [InlineData(LocatorStrategy.Role, "button", null)] // accessible name computed from "Go"
    [InlineData(LocatorStrategy.Label, "Username", null)]
    [InlineData(LocatorStrategy.Text, "Welcome back", null)] // partial match
    [InlineData(LocatorStrategy.Placeholder, "Enter your username", null)]
    [InlineData(LocatorStrategy.Css, "#panel button", null)]
    [InlineData(LocatorStrategy.XPath, "//button[@data-testid='submit-btn']", null)]
    public async Task Every_web_strategy_resolves_its_element(LocatorStrategy strategy, string value, string? name)
    {
        var locator = Simple(strategy, value, name);

        var outcome = await PlaywrightLocatorResolver.ResolveAsync(_page, locator, timeoutMs: 1000, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.FailureDetail);
        Assert.Null(outcome.Info); // primary resolved directly -- never flagged
    }

    [Fact]
    public async Task Within_chains_into_a_parent_element_instead_of_complex_XPath()
    {
        // Equivalent, unchained XPath would be "//div[@id='panel']//button[@data-testid='submit-btn']" --
        // chaining replaces that with two simple locators.
        var panel = Simple(LocatorStrategy.Css, "#panel");
        var button = new Locator(new LocatorSpec(LocatorStrategy.TestId, "submit-btn", null), Array.Empty<LocatorSpec>(), "submit button", panel, null);

        var outcome = await PlaywrightLocatorResolver.ResolveAsync(_page, button, timeoutMs: 1000, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.FailureDetail);
        Assert.Equal("Go", await outcome.Resolved!.TextContentAsync());
    }

    [Fact]
    public async Task Nth_selects_among_several_matches()
    {
        var locator = new Locator(new LocatorSpec(LocatorStrategy.Css, "li.item", null), Array.Empty<LocatorSpec>(), "second item", null, Nth: 1);

        var outcome = await PlaywrightLocatorResolver.ResolveAsync(_page, locator, timeoutMs: 1000, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.FailureDetail);
        Assert.Equal("Item 2", await outcome.Resolved!.TextContentAsync());
    }

    [Fact]
    public async Task Out_of_range_Nth_fails_clearly_naming_the_real_match_count()
    {
        var locator = new Locator(new LocatorSpec(LocatorStrategy.Css, "li.item", null), Array.Empty<LocatorSpec>(), "tenth item", null, Nth: 9);

        var outcome = await PlaywrightLocatorResolver.ResolveAsync(_page, locator, timeoutMs: 300, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("matched 3 element(s)", outcome.FailureDetail);
        Assert.Contains("Nth=9 is out of range", outcome.FailureDetail);
    }

    [Fact]
    public async Task Mutation_check_a_broken_primary_falls_back_and_is_flagged_never_silently()
    {
        var primary = new LocatorSpec(LocatorStrategy.Css, "#does-not-exist", null); // broken on purpose
        var fallback = new LocatorSpec(LocatorStrategy.TestId, "submit-btn", null);
        var locator = new Locator(primary, new[] { fallback }, "submit button", null, null);

        var outcome = await PlaywrightLocatorResolver.ResolveAsync(_page, locator, timeoutMs: 300, CancellationToken.None);

        Assert.True(outcome.Succeeded);
        Assert.NotNull(outcome.Info); // flagged -- never a silent pass
        Assert.Equal(LocatorStrategy.TestId, outcome.Info!.StrategyUsed);
        Assert.Contains("#does-not-exist", outcome.Info.PrimaryFailureReason);
        Assert.Equal("submit button", outcome.Info.Description);
    }

    [Fact]
    public async Task Mutation_check_every_strategy_broken_fails_cleanly_with_full_diagnostics()
    {
        var primary = new LocatorSpec(LocatorStrategy.Css, "#does-not-exist", null);
        var fallback1 = new LocatorSpec(LocatorStrategy.TestId, "also-missing", null);
        var fallback2 = new LocatorSpec(LocatorStrategy.Text, "nothing like this on the page", null);
        var locator = new Locator(primary, new[] { fallback1, fallback2 }, "a locator with no real match", null, null);

        var outcome = await PlaywrightLocatorResolver.ResolveAsync(_page, locator, timeoutMs: 300, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Null(outcome.Info);
        // Every strategy tried, named, with its own match count -- never just "not found".
        Assert.Contains("Css:'#does-not-exist' matched 0 element(s)", outcome.FailureDetail);
        Assert.Contains("TestId:'also-missing' matched 0 element(s)", outcome.FailureDetail);
        Assert.Contains("Text:'nothing like this on the page' matched 0 element(s)", outcome.FailureDetail);
    }

    [Fact]
    public async Task A_mobile_only_strategy_fails_with_a_clear_reason_instead_of_throwing()
    {
        var locator = Simple(LocatorStrategy.AccessibilityId, "some-id");

        var outcome = await PlaywrightLocatorResolver.ResolveAsync(_page, locator, timeoutMs: 300, CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Contains("Mobile locator strategy", outcome.FailureDetail);
    }
}
