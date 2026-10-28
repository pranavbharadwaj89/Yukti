using Microsoft.Playwright;
using Yukti.Domain.Execution;
using Yukti.Domain.UiVocabulary;

namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Web UI Testing Module Spec, WEB-T3: translates the engine-neutral
/// Yukti.Domain.UiVocabulary.Locator into real Playwright calls — the one place that
/// translation happens, per WEB-T1's own rule ("the Web module translates vocabulary types
/// into Playwright calls; that translation lives entirely inside the module"). Not yet wired
/// into WebModule's existing actions (WEB-T4's job) — this is the resolution algorithm itself,
/// proven standalone against a real Playwright page, the same way FixtureApp is proven standalone
/// before any module drives it.
/// </summary>
public static class PlaywrightLocatorResolver
{
    /// <summary>Succeeded false means every strategy (primary and every fallback) failed to
    /// resolve within the timeout; FailureDetail then names every strategy tried and how many
    /// elements each one matched. A non-null Info means the PRIMARY failed but a fallback
    /// succeeded — this is the "flagged pass," never silent, per the spec's own design.</summary>
    public sealed record Outcome(bool Succeeded, ILocator? Resolved, LocatorResolutionInfo? Info, string? FailureDetail)
    {
        public static Outcome Success(ILocator resolved, LocatorResolutionInfo? info = null) => new(true, resolved, info, null);
        public static Outcome Failure(string detail) => new(false, null, null, detail);
    }

    public static async Task<Outcome> ResolveAsync(IPage page, Locator locator, float timeoutMs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Within chains uniformly: every strategy attempt below (primary AND every fallback) is
        // built relative to this SAME resolved parent scope, not just the primary's own attempt.
        ILocator? scope = null;
        if (locator.Within is not null)
        {
            var parentOutcome = await ResolveAsync(page, locator.Within, timeoutMs, ct);
            if (!parentOutcome.Succeeded)
                return Outcome.Failure($"Parent locator did not resolve: {parentOutcome.FailureDetail}");
            scope = parentOutcome.Resolved;
        }

        var specsInOrder = new List<LocatorSpec> { locator.Primary };
        specsInOrder.AddRange(locator.Fallbacks);

        var attempts = new List<string>();
        for (var i = 0; i < specsInOrder.Count; i++)
        {
            var spec = specsInOrder[i];
            var isPrimary = i == 0;

            ILocator built;
            try
            {
                built = Build(scope, page, spec);
            }
            catch (ArgumentException ex)
            {
                attempts.Add($"{spec.Strategy}:'{spec.Value}' — {ex.Message}");
                continue;
            }

            var targeted = locator.Nth is int n ? built.Nth(n) : built;

            try
            {
                await targeted.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = timeoutMs });

                if (isPrimary)
                    return Outcome.Success(targeted);

                var primaryFailureReason = attempts.Count > 0 ? attempts[0] : "primary locator did not resolve";
                return Outcome.Success(targeted, new LocatorResolutionInfo(spec.Strategy, primaryFailureReason, locator.Description));
            }
            // ILocator.WaitForAsync throws the BCL System.TimeoutException directly on timeout —
            // unlike ClickAsync/WaitForSelectorAsync (WebModule's own WithSelectorNotFound only
            // ever sees PlaywrightException from those), confirmed empirically against a real
            // Playwright page while building this. Catching both here, not just PlaywrightException.
            catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
            {
                var matchCount = await SafeCount(built, ct);
                var nthNote = locator.Nth is int nth && matchCount <= nth ? $" (Nth={nth} is out of range for {matchCount} match(es))" : "";
                attempts.Add($"{spec.Strategy}:'{spec.Value}' matched {matchCount} element(s){nthNote} — {FirstLine(ex.Message)}");
            }
        }

        var entryName = locator.Description ?? "(no description)";
        return Outcome.Failure($"No strategy resolved locator '{entryName}'. Tried: {string.Join(" | ", attempts)}");
    }

    private static async Task<int> SafeCount(ILocator built, CancellationToken ct)
    {
        try { return await built.CountAsync(); }
        catch (PlaywrightException) { return 0; }
    }

    private static string FirstLine(string message) => message.Split('\n')[0].Trim();

    private static ILocator Build(ILocator? scope, IPage page, LocatorSpec spec) => spec.Strategy switch
    {
        LocatorStrategy.TestId => scope is null ? page.GetByTestId(spec.Value) : scope.GetByTestId(spec.Value),
        LocatorStrategy.Role => BuildRole(scope, page, spec),
        LocatorStrategy.Label => scope is null ? page.GetByLabel(spec.Value) : scope.GetByLabel(spec.Value),
        LocatorStrategy.Text => scope is null ? page.GetByText(spec.Value) : scope.GetByText(spec.Value),
        LocatorStrategy.Placeholder => scope is null ? page.GetByPlaceholder(spec.Value) : scope.GetByPlaceholder(spec.Value),
        LocatorStrategy.Css => scope is null ? page.Locator(spec.Value) : scope.Locator(spec.Value),
        LocatorStrategy.XPath => scope is null ? page.Locator("xpath=" + spec.Value) : scope.Locator("xpath=" + spec.Value),
        _ => throw new ArgumentException(
            $"'{spec.Strategy}' is a Mobile locator strategy and is not valid for the Web module. " +
            "Supported here: TestId, Role, Label, Text, Placeholder, Css, XPath."),
    };

    private static ILocator BuildRole(ILocator? scope, IPage page, LocatorSpec spec)
    {
        if (!Enum.TryParse<AriaRole>(spec.Value, ignoreCase: true, out var role))
            throw new ArgumentException($"'{spec.Value}' is not a recognized ARIA role.");

        return scope is null
            ? page.GetByRole(role, new PageGetByRoleOptions { Name = spec.Name })
            : scope.GetByRole(role, new LocatorGetByRoleOptions { Name = spec.Name });
    }
}
