using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using Yukti.Domain.Execution;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.UiVocabulary;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Mobile Testing Module Spec, MOB-T4: translates the engine-neutral
/// Yukti.Domain.UiVocabulary.Locator into real Appium/Selenium calls — the Mobile module's own
/// counterpart to PlaywrightLocatorResolver (WEB-T3), same algorithm shape: try the primary
/// strategy, then every fallback in order, each attempt scoped to the same resolved <see
/// cref="Locator.Within"/> parent; a fallback succeeding is always a flagged pass
/// (<see cref="LocatorResolutionInfo"/>), never silent.
///
/// Selenium has no async wait-for-element primitive the way Playwright's ILocator.WaitForAsync
/// does — <see cref="PollForMatches"/> polls FindElements on a short interval until the timeout,
/// the direct equivalent without adding a new dependency (WebDriverWait/Selenium.Support isn't
/// referenced anywhere in this codebase; adding it would need its own ADR for one polling loop
/// this already does by hand).
///
/// No live Appium server is available in this environment (the same constraint MOB-T1/T2/T3's
/// own tests document), so <see cref="ResolveAsync"/> itself is implemented and wired in but not
/// exercisable end-to-end here — <see cref="Build"/>, the pure strategy-to-<see cref="By"/>
/// mapping, needs no live driver at all and is what MobileLocatorTests actually proves.
/// </summary>
public static class AppiumLocatorResolver
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Same shape as PlaywrightLocatorResolver.Outcome: Succeeded false means every
    /// strategy (primary and every fallback) failed to resolve within the timeout; FailureDetail
    /// then names every strategy tried and how many elements each one matched. A non-null Info
    /// means the PRIMARY failed but a fallback succeeded.</summary>
    public sealed record Outcome(bool Succeeded, IWebElement? Resolved, LocatorResolutionInfo? Info, string? FailureDetail)
    {
        public static Outcome Success(IWebElement resolved, LocatorResolutionInfo? info = null) => new(true, resolved, info, null);
        public static Outcome Failure(string detail) => new(false, null, null, detail);
    }

    public static async Task<Outcome> ResolveAsync(AppiumDriver driver, Locator locator, MobilePlatform platform, TimeSpan timeout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Within chains uniformly: every strategy attempt below (primary AND every fallback) is
        // built relative to this SAME resolved parent scope, not just the primary's own attempt.
        IWebElement? scope = null;
        if (locator.Within is not null)
        {
            var parentOutcome = await ResolveAsync(driver, locator.Within, platform, timeout, ct);
            if (!parentOutcome.Succeeded)
                return Outcome.Failure($"Parent locator did not resolve: {parentOutcome.FailureDetail}");
            scope = parentOutcome.Resolved;
        }

        var specsInOrder = new List<LocatorSpec> { locator.Primary };
        specsInOrder.AddRange(locator.Fallbacks);

        // One overall deadline for the whole primary+fallback chain, not `timeout` per attempt --
        // otherwise a locator with several fallbacks could take specsInOrder.Count * timeout in
        // the worst case instead of the caller's configured budget.
        var deadline = DateTime.UtcNow + timeout;

        var attempts = new List<string>();
        for (var i = 0; i < specsInOrder.Count; i++)
        {
            var spec = specsInOrder[i];
            var isPrimary = i == 0;

            By by;
            try
            {
                by = Build(spec, platform);
            }
            catch (ArgumentException ex)
            {
                attempts.Add($"{spec.Strategy}:'{spec.Value}' — {ex.Message}");
                continue;
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                attempts.Add($"{spec.Strategy}:'{spec.Value}' — not attempted, overall timeout already elapsed");
                continue;
            }

            var matches = await PollForMatches(driver, scope, by, remaining, ct);
            var target = locator.Nth is int n ? (n >= 0 && n < matches.Count ? matches[n] : null) : matches.FirstOrDefault();

            if (target is not null)
            {
                if (isPrimary)
                    return Outcome.Success(target);

                var primaryFailureReason = attempts.Count > 0 ? attempts[0] : "primary locator did not resolve";
                return Outcome.Success(target, new LocatorResolutionInfo(spec.Strategy, primaryFailureReason, locator.Description));
            }

            var nthNote = locator.Nth is int nth && matches.Count <= nth ? $" (Nth={nth} is out of range for {matches.Count} match(es))" : "";
            attempts.Add($"{spec.Strategy}:'{spec.Value}' matched {matches.Count} element(s){nthNote}");
        }

        var entryName = locator.Description ?? "(no description)";
        return Outcome.Failure($"No strategy resolved locator '{entryName}'. Tried: {string.Join(" | ", attempts)}");
    }

    private static async Task<IReadOnlyList<IWebElement>> PollForMatches(AppiumDriver driver, IWebElement? scope, By by, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            IReadOnlyCollection<IWebElement> found;
            try
            {
                found = scope is null ? driver.FindElements(by) : scope.FindElements(by);
            }
            catch (WebDriverException)
            {
                found = Array.Empty<IWebElement>(); // a malformed-for-this-driver selector matches nothing, not a crash
            }

            if (found.Count > 0)
                return found.ToList();
            if (DateTime.UtcNow >= deadline)
                return Array.Empty<IWebElement>();

            await Task.Delay(PollInterval, ct);
        }
    }

    /// <summary>
    /// Mobile Testing Module Spec, MOB-T4's own strategy list, each mapped onto the real Appium
    /// client call that already exists for it — never a hand-rolled protocol detail:
    /// AccessibilityId (preferred — maps to contentDescription on Android, accessibilityIdentifier
    /// on iOS), ResourceId (Android, via the same MobileBy.Id compatibility mapping MOB-T1's
    /// original ResolveElement already relied on for a plain non-XPath selector), IosPredicate,
    /// IosClassChain, ClassName, XPath, and Text (platform-aware — no single Appium capability
    /// expresses "visible text" identically on both platforms, so this picks each platform's own
    /// idiomatic match). Pure -- no live driver needed, directly unit-testable.
    /// </summary>
    public static By Build(LocatorSpec spec, MobilePlatform platform) => spec.Strategy switch
    {
        LocatorStrategy.AccessibilityId => MobileBy.AccessibilityId(spec.Value),
        LocatorStrategy.ResourceId => MobileBy.Id(spec.Value),
        LocatorStrategy.IosPredicate => MobileBy.IosNSPredicate(spec.Value),
        LocatorStrategy.IosClassChain => MobileBy.IosClassChain(spec.Value),
        LocatorStrategy.ClassName => By.ClassName(spec.Value),
        LocatorStrategy.XPath => By.XPath(spec.Value),
        LocatorStrategy.Text => BuildText(spec.Value, platform),
        _ => throw new ArgumentException(
            $"'{spec.Strategy}' is a Web locator strategy and is not valid for the Mobile module. " +
            "Supported here: AccessibilityId, ResourceId, IosPredicate, IosClassChain, ClassName, Text, XPath."),
    };

    private static By BuildText(string value, MobilePlatform platform) => platform == MobilePlatform.Ios
        ? MobileBy.IosNSPredicate($"label == \"{EscapeForPredicate(value)}\" OR name == \"{EscapeForPredicate(value)}\" OR value == \"{EscapeForPredicate(value)}\"")
        : MobileBy.AndroidUIAutomator($"new UiSelector().text(\"{EscapeForUiSelector(value)}\")");

    private static string EscapeForPredicate(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string EscapeForUiSelector(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
