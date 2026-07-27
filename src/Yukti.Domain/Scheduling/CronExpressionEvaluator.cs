using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Scheduling;

/// <summary>
/// Minimal standard 5-field cron ("minute hour day month weekday")
/// evaluator — no third-party dependency, matching this project's
/// zero-dependency-for-pure-logic convention. Supports '*', exact values,
/// comma lists, ranges ("a-b"), and step ("*/n"); deliberately does not
/// support named months/weekdays, '?', 'L', or 'W' — a documented,
/// intentional subset, not an oversight.
/// </summary>
public static class CronExpressionEvaluator
{
    public static void Validate(string cronExpression) => Parse(cronExpression);

    public static bool Matches(string cronExpression, DateTimeOffset at)
    {
        var fields = Parse(cronExpression);
        return fields.Minute.Contains(at.Minute)
            && fields.Hour.Contains(at.Hour)
            && fields.Day.Contains(at.Day)
            && fields.Month.Contains(at.Month)
            && fields.Weekday.Contains((int)at.DayOfWeek);
    }

    /// <summary>
    /// FR-SCHED-05: every minute-aligned tick strictly after the later of
    /// (lastFiredAt, now - catchUpWindow) up to and including 'now' that
    /// matches the expression — ticks that fall outside the catch-up
    /// window are silently skipped, never queued, so a long outage doesn't
    /// flood-fire every missed minute at once.
    /// </summary>
    public static IReadOnlyList<DateTimeOffset> GetMissedTicks(
        string cronExpression, DateTimeOffset? lastFiredAt, DateTimeOffset now, TimeSpan catchUpWindow)
    {
        var earliestAllowed = now - catchUpWindow;
        var from = lastFiredAt is { } last && last > earliestAllowed ? last : earliestAllowed;

        var cursor = TruncateToMinute(from).AddMinutes(1);
        var nowTruncated = TruncateToMinute(now);

        var result = new List<DateTimeOffset>();
        while (cursor <= nowTruncated)
        {
            if (Matches(cronExpression, cursor))
                result.Add(cursor);
            cursor = cursor.AddMinutes(1);
        }
        return result;
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);

    private sealed record ParsedFields(
        IReadOnlySet<int> Minute, IReadOnlySet<int> Hour, IReadOnlySet<int> Day,
        IReadOnlySet<int> Month, IReadOnlySet<int> Weekday);

    private static ParsedFields Parse(string cronExpression)
    {
        var parts = (cronExpression ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5)
            throw new DomainException($"Cron expression '{cronExpression}' must have exactly 5 fields (minute hour day month weekday).");

        return new ParsedFields(
            ParseField(parts[0], 0, 59, "minute"),
            ParseField(parts[1], 0, 23, "hour"),
            ParseField(parts[2], 1, 31, "day"),
            ParseField(parts[3], 1, 12, "month"),
            ParseField(parts[4], 0, 6, "weekday"));
    }

    private static IReadOnlySet<int> ParseField(string field, int min, int max, string fieldName)
    {
        var values = new HashSet<int>();
        foreach (var segment in field.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var (rangePart, step) = SplitStep(segment, fieldName);
            var (rangeStart, rangeEnd) = rangePart == "*"
                ? (min, max)
                : ParseRange(rangePart, min, max, fieldName);

            for (var v = rangeStart; v <= rangeEnd; v += step)
                values.Add(v);
        }

        if (values.Count == 0)
            throw new DomainException($"Cron {fieldName} field '{field}' produced no valid values.");
        return values;
    }

    private static (string RangePart, int Step) SplitStep(string segment, string fieldName)
    {
        var stepSplit = segment.Split('/', 2);
        if (stepSplit.Length == 1)
            return (stepSplit[0], 1);

        if (!int.TryParse(stepSplit[1], out var step) || step <= 0)
            throw new DomainException($"Cron {fieldName} field has an invalid step in '{segment}'.");
        return (stepSplit[0], step);
    }

    private static (int Start, int End) ParseRange(string rangePart, int min, int max, string fieldName)
    {
        var rangeSplit = rangePart.Split('-', 2);
        if (rangeSplit.Length == 1)
        {
            if (!int.TryParse(rangeSplit[0], out var exact) || exact < min || exact > max)
                throw new DomainException($"Cron {fieldName} field has an out-of-range value '{rangePart}' (expected {min}-{max}).");
            return (exact, exact);
        }

        if (!int.TryParse(rangeSplit[0], out var start) || !int.TryParse(rangeSplit[1], out var end)
            || start < min || end > max || start > end)
            throw new DomainException($"Cron {fieldName} field has an invalid range '{rangePart}' (expected {min}-{max}).");
        return (start, end);
    }
}
