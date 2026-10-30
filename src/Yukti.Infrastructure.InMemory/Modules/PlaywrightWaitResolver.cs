using System.Diagnostics;
using Microsoft.Playwright;
using Yukti.Domain.UiVocabulary;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Web UI Testing Module Spec, WEB-T5: evaluates one Yukti.Domain.UiVocabulary.WaitCondition
/// (WEB-T1) against a real Playwright page -- the explicit-wait equivalent, expressed as data
/// instead of a hand-written polling loop. A timeout here always reports which condition never
/// became true, what the actual state last seen was, and how long it waited -- "timed out after
/// 30s" with no further detail is explicitly called out as an incomplete implementation by the
/// spec itself.
/// </summary>
public static class PlaywrightWaitResolver
{
    public sealed record Result(bool Succeeded, string ConditionDescription, string LastObservedState, TimeSpan Elapsed)
    {
        public string FailureDetail =>
            $"Timed out after {Elapsed.TotalMilliseconds:F0}ms waiting for: {ConditionDescription}. Last observed: {LastObservedState}.";
    }

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static async Task<Result> EvaluateAsync(IPage page, WaitCondition condition, float timeoutMs, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(timeoutMs);
        var stopwatch = Stopwatch.StartNew();

        switch (condition)
        {
            case Visible c:
                return await ResolveThenWaitState(page, c.L, WaitForSelectorState.Visible, "visible", stopwatch, timeout, ct);
            case Hidden c:
                return await ResolveThenWaitState(page, c.L, WaitForSelectorState.Hidden, "hidden", stopwatch, timeout, ct);
            case Enabled c:
                return await PollLocator(page, c.L, "enabled",
                    async target => (await target.IsEnabledAsync()) ? (true, "enabled") : (false, "disabled"),
                    stopwatch, timeout, ct);
            case Disabled c:
                return await PollLocator(page, c.L, "disabled",
                    async target => (await target.IsEnabledAsync()) ? (false, "enabled") : (true, "disabled"),
                    stopwatch, timeout, ct);
            case TextEquals c:
                return await PollLocator(page, c.L, $"text to equal '{c.Expected}'",
                    async target => { var text = (await target.TextContentAsync())?.Trim() ?? ""; return (text == c.Expected, $"text was '{text}'"); },
                    stopwatch, timeout, ct);
            case TextContains c:
                return await PollLocator(page, c.L, $"text to contain '{c.Fragment}'",
                    async target => { var text = (await target.TextContentAsync())?.Trim() ?? ""; return (text.Contains(c.Fragment), $"text was '{text}'"); },
                    stopwatch, timeout, ct);
            case AttributeEquals c:
                return await PollLocator(page, c.L, $"attribute '{c.Attr}' to equal '{c.Expected}'",
                    async target => { var actual = await target.GetAttributeAsync(c.Attr); return (actual == c.Expected, $"attribute '{c.Attr}' was '{actual ?? "(absent)"}'"); },
                    stopwatch, timeout, ct);
            case CountEquals c:
                return await EvaluateCountEquals(page, c, stopwatch, timeout, ct);
            case UrlEquals c:
                return await PollPage(page, $"url to equal '{c.Expected}'",
                    () => Task.FromResult((page.Url == c.Expected, $"url was '{page.Url}'")),
                    stopwatch, timeout, ct);
            case UrlContains c:
                return await PollPage(page, $"url to contain '{c.Fragment}'",
                    () => Task.FromResult((page.Url.Contains(c.Fragment), $"url was '{page.Url}'")),
                    stopwatch, timeout, ct);
            case TitleContains c:
                return await PollPage(page, $"title to contain '{c.Fragment}'",
                    async () => { var title = await page.TitleAsync(); return (title.Contains(c.Fragment), $"title was '{title}'"); },
                    stopwatch, timeout, ct);
            case NetworkIdle c:
                return await EvaluateNetworkIdle(page, c, stopwatch, timeout, ct);
            case CustomPredicate c:
                return await EvaluateCustomPredicate(page, c, stopwatch, timeout, ct);
            default:
                throw new ArgumentOutOfRangeException(nameof(condition), $"Unhandled WaitCondition type '{condition.GetType().Name}'.");
        }
    }

    private static async Task<Result> ResolveThenWaitState(
        IPage page, Locator locator, WaitForSelectorState state, string description, Stopwatch stopwatch, TimeSpan timeout, CancellationToken ct)
    {
        // Playwright's own State=Visible/Hidden is a built-in wait -- no manual polling needed --
        // but the locator itself must resolve first (fallback-aware, via WEB-T3's resolver),
        // using whatever time budget remains.
        var resolveOutcome = await PlaywrightLocatorResolver.ResolveAsync(page, locator, (float)Math.Max(0, timeout.TotalMilliseconds - stopwatch.Elapsed.TotalMilliseconds), ct);
        if (!resolveOutcome.Succeeded)
            return new Result(false, $"{locator.Description ?? "locator"} to become {description}", $"locator never resolved: {resolveOutcome.FailureDetail}", stopwatch.Elapsed);

        var remaining = (float)Math.Max(0, timeout.TotalMilliseconds - stopwatch.Elapsed.TotalMilliseconds);
        try
        {
            await resolveOutcome.Resolved!.WaitForAsync(new LocatorWaitForOptions { State = state, Timeout = remaining });
            return new Result(true, $"{locator.Description ?? "locator"} to become {description}", description, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            var actualState = state == WaitForSelectorState.Visible ? "not visible" : "still visible";
            return new Result(false, $"{locator.Description ?? "locator"} to become {description}", actualState, stopwatch.Elapsed);
        }
    }

    private static async Task<Result> PollLocator(
        IPage page, Locator locator, string conditionDescription,
        Func<ILocator, Task<(bool Matches, string Actual)>> check, Stopwatch stopwatch, TimeSpan timeout, CancellationToken ct)
    {
        var resolveOutcome = await PlaywrightLocatorResolver.ResolveAsync(page, locator, (float)Math.Max(0, timeout.TotalMilliseconds - stopwatch.Elapsed.TotalMilliseconds), ct);
        if (!resolveOutcome.Succeeded)
            return new Result(false, $"{locator.Description ?? "locator"} {conditionDescription}", $"locator never resolved: {resolveOutcome.FailureDetail}", stopwatch.Elapsed);

        var lastActual = "(never evaluated)";
        while (stopwatch.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            var (matches, actual) = await check(resolveOutcome.Resolved!);
            lastActual = actual;
            if (matches)
                return new Result(true, $"{locator.Description ?? "locator"} {conditionDescription}", actual, stopwatch.Elapsed);
            await Task.Delay(PollInterval, ct);
        }
        return new Result(false, $"{locator.Description ?? "locator"} {conditionDescription}", lastActual, stopwatch.Elapsed);
    }

    /// <summary>CountEquals legitimately expects several matches -- the opposite of every other
    /// locator-based condition above, which all operate on "the" one element -- so this builds
    /// the bare locator directly (WEB-T3's own Build, not the single-element ResolveAsync) and
    /// polls CountAsync() without ever going through strict-mode single-match resolution.
    /// Fallbacks don't apply to a count check the way they do to "find one element," so only
    /// the primary strategy is used.</summary>
    private static async Task<Result> EvaluateCountEquals(IPage page, CountEquals condition, Stopwatch stopwatch, TimeSpan timeout, CancellationToken ct)
    {
        ILocator? scope = null;
        if (condition.L.Within is not null)
        {
            var parentOutcome = await PlaywrightLocatorResolver.ResolveAsync(page, condition.L.Within, (float)timeout.TotalMilliseconds, ct);
            if (!parentOutcome.Succeeded)
                return new Result(false, $"{condition.L.Description ?? "locator"} count to equal {condition.Expected}", $"parent locator never resolved: {parentOutcome.FailureDetail}", stopwatch.Elapsed);
            scope = parentOutcome.Resolved;
        }

        var built = PlaywrightLocatorResolver.Build(scope, page, condition.L.Primary);
        var lastActual = "(never evaluated)";
        while (stopwatch.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            var count = await built.CountAsync();
            lastActual = $"count was {count}";
            if (count == condition.Expected)
                return new Result(true, $"{condition.L.Description ?? "locator"} count to equal {condition.Expected}", lastActual, stopwatch.Elapsed);
            await Task.Delay(PollInterval, ct);
        }
        return new Result(false, $"{condition.L.Description ?? "locator"} count to equal {condition.Expected}", lastActual, stopwatch.Elapsed);
    }

    private static async Task<Result> PollPage(
        IPage page, string conditionDescription, Func<Task<(bool Matches, string Actual)>> check, Stopwatch stopwatch, TimeSpan timeout, CancellationToken ct)
    {
        var lastActual = "(never evaluated)";
        while (stopwatch.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            var (matches, actual) = await check();
            lastActual = actual;
            if (matches)
                return new Result(true, conditionDescription, actual, stopwatch.Elapsed);
            await Task.Delay(PollInterval, ct);
        }
        return new Result(false, conditionDescription, lastActual, stopwatch.Elapsed);
    }

    /// <summary>"Waits for a quiet period with no in-flight requests" -- Playwright's own
    /// WaitForLoadStateAsync(NetworkIdle) is the real, built-in mechanism for this, but its
    /// quiet-period threshold is fixed (~500ms) and not configurable; NetworkIdle.Quiet is
    /// honored as the overall timeout here, not a tunable quiet duration. Flagged honestly in
    /// plan/OPEN-QUESTIONS.md rather than pretending full fidelity to the domain record's own
    /// field name.</summary>
    private static async Task<Result> EvaluateNetworkIdle(IPage page, NetworkIdle condition, Stopwatch stopwatch, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = (float)timeout.TotalMilliseconds });
            return new Result(true, "network to go idle", "idle", stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return new Result(false, "network to go idle", "still had in-flight requests", stopwatch.Elapsed);
        }
    }

    private static async Task<Result> EvaluateCustomPredicate(IPage page, CustomPredicate condition, Stopwatch stopwatch, TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            await page.WaitForFunctionAsync(condition.Script, null, new PageWaitForFunctionOptions { Timeout = (float)timeout.TotalMilliseconds });
            return new Result(true, $"script '{condition.Script}' to return true", "true", stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            return new Result(false, $"script '{condition.Script}' to return true", "false (or threw)", stopwatch.Elapsed);
        }
    }
}
