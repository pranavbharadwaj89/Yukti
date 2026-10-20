using System.Globalization;
using Microsoft.Extensions.Configuration;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Ai;

/// <summary>D5-T9/§R6: <c>Ai:Budget:WeeklyCapUsd</c> (default matches <c>plan/AI-BUDGET.md</c>, $30) and <c>Ai:Budget:WarnThreshold</c> (default 0.8 — 80%).</summary>
public sealed class ConfigurationAiBudgetOptionsProvider : IAiBudgetOptionsProvider
{
    private const decimal DefaultWeeklyCapUsd = 30.00m;
    private const double DefaultWarnThreshold = 0.8;

    private readonly IConfiguration _config;

    public ConfigurationAiBudgetOptionsProvider(IConfiguration config) => _config = config;

    public decimal WeeklyCapUsd =>
        decimal.TryParse(_config["Ai:Budget:WeeklyCapUsd"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : DefaultWeeklyCapUsd;

    public double WarnThreshold =>
        double.TryParse(_config["Ai:Budget:WarnThreshold"], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : DefaultWarnThreshold;
}
