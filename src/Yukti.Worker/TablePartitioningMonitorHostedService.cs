using Microsoft.EntityFrameworkCore;
using Yukti.Infrastructure;

namespace Yukti.Worker;

/// <summary>
/// FR-OPS-04: watches flow_runs/step_results row counts and logs a
/// structured alert once either crosses the monitored 5M threshold, with
/// the remaining headroom to the 10M target so an operator can see how
/// much runway is left, not just that the threshold fired. The alerting
/// mechanism itself is a structured ERROR-level log line — the same
/// "real, demonstrable stand-in for a production alerting pipeline"
/// pattern OpenTelemetry's console exporter already uses elsewhere in this
/// codebase; wiring this log line to PagerDuty/Slack/etc. is an
/// infrastructure concern, not an application-code one.
///
/// Once alerted, activating partitioning is a single operational action —
/// `ALTER INDEX flow_runs@"IX_flow_runs_TenantId_StartedAt" PARTITION BY
/// LIST ("TenantId") (...)` (and the equivalent on step_results' TenantId
/// index) — because both tables already carry a TenantId-leading index
/// (see FlowRunConfiguration's doc comments). No further schema change is
/// needed at alert time; this job only ever reads.
/// </summary>
public sealed class TablePartitioningMonitorHostedService : BackgroundService
{
    private const long AlertThreshold = 5_000_000;
    private const long PartitioningTarget = 10_000_000;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TablePartitioningMonitorHostedService> _logger;

    public TablePartitioningMonitorHostedService(IServiceScopeFactory scopeFactory, ILogger<TablePartitioningMonitorHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await CheckOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Partitioning threshold check failed: {Error}", ex.Message);
            }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YuktiDbContext>();

        var flowRunCount = await context.FlowRuns.LongCountAsync(ct);
        var stepResultCount = await context.Database
            .SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM step_results")
            .SingleAsync(ct);

        CheckTable("flow_runs", flowRunCount);
        CheckTable("step_results", stepResultCount);
    }

    private void CheckTable(string tableName, long rowCount)
    {
        if (rowCount >= AlertThreshold)
        {
            _logger.LogError(
                "PARTITIONING THRESHOLD ALERT: table {Table} has {RowCount} rows, at or past the monitored threshold of {Threshold}; headroom to the {Target} partitioning target is {Headroom} rows. Activate partitioning now via ALTER INDEX ... PARTITION BY LIST (\"TenantId\") (...) — no schema change required, a TenantId-leading index already exists.",
                tableName, rowCount, AlertThreshold, PartitioningTarget, PartitioningTarget - rowCount);
        }
        else
        {
            _logger.LogInformation("{Table} row count: {RowCount} (alert threshold {Threshold})", tableName, rowCount, AlertThreshold);
        }
    }
}
