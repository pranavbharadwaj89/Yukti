using System.Globalization;
using Microsoft.Extensions.Configuration;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>D5-T9/§R6: <c>Ai:Budget:WeeklyCapUsd</c> (default matches <c>plan/AI-BUDGET.md</c>, $30) and
/// <c>Ai:Budget:WarnThreshold</c> (default 0.8 — 80%). DISC-14: <c>Ai:Budget:DailyRequestQuota</c>
/// defaults to unbounded (<see cref="int.MaxValue"/>) — only set it where the configured key
/// actually has a provider-side request-count ceiling (today: 20, this project's free-tier
/// Gemini key).</summary>
public sealed class ConfigurationAiBudgetOptionsProvider : IAiBudgetOptionsProvider
{
    private const decimal DefaultWeeklyCapUsd = 30.00m;
    private const double DefaultWarnThreshold = 0.8;
    private const int DefaultDailyRequestQuota = int.MaxValue;

    private readonly IConfiguration _config;

    public ConfigurationAiBudgetOptionsProvider(IConfiguration config) => _config = config;

    public decimal WeeklyCapUsd =>
        decimal.TryParse(_config["Ai:Budget:WeeklyCapUsd"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : DefaultWeeklyCapUsd;

    public double WarnThreshold =>
        double.TryParse(_config["Ai:Budget:WarnThreshold"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : DefaultWarnThreshold;

    public int DailyRequestQuota =>
        int.TryParse(_config["Ai:Budget:DailyRequestQuota"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : DefaultDailyRequestQuota;
}
