using Microsoft.EntityFrameworkCore;
using Yukti.Domain.Execution;
using Yukti.Infrastructure;
using Yukti.Infrastructure.ReadModels;

namespace Yukti.Api;

/// <summary>
/// FR-CQRS-03: recomputes TrendAggregateReadModel per tenant on a fixed
/// cadence — never per-event (that would just be FR-CQRS-02's pattern
/// again with extra steps). Cadence is configurable via
/// TrendAggregates:RecomputeInterval; defaults to 5 minutes.
/// </summary>
public sealed class TrendAggregateBatchJob : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TrendAggregateBatchJob> _logger;
    private readonly TimeSpan _interval;

    public TrendAggregateBatchJob(IServiceScopeFactory scopeFactory, ILogger<TrendAggregateBatchJob> logger, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = configuration.GetValue<TimeSpan?>("TrendAggregates:RecomputeInterval") ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        do
        {
            try
            {
                await RecomputeOnce(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Trend aggregate batch job failed: {Error}", ex.Message);
            }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RecomputeOnce(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<YuktiDbContext>();

        var windowStart = DateTimeOffset.UtcNow.AddHours(-24);
        var now = DateTimeOffset.UtcNow;

        // FlowReportReadModel (FlowReportProjectionConsumer's projection)
        // is the batch source, not flow_runs directly — reading from an
        // already-eventually-consistent Tier 2 projection for a
        // batch-computed aggregate is the correct layering (FR-CQRS-03
        // depends on FR-CQRS-02, not on raw aggregate state).
        //
        // Flake rate is NOT computed here: FlowRunFlakeDetectedEvent
        // (Yukti.Domain.Events) doesn't carry TenantId, so there is no
        // correct way to scope it per tenant from the outbox alone yet —
        // reporting a wrong number would be worse than reporting none.
        // Documented gap, closed once that event carries TenantId.
        var byTenant = await context.FlowReports
            .Where(r => r.OccurredAt >= windowStart)
            .GroupBy(r => r.TenantId)
            .Select(g => new
            {
                TenantId = g.Key,
                Total = g.Count(),
                Passed = g.Count(r => r.FinalStatus == RunStatus.Passed),
            })
            .ToListAsync(ct);

        foreach (var tenant in byTenant)
        {
            var existing = await context.Set<TrendAggregateReadModel>().FindAsync(new object[] { tenant.TenantId }, ct);
            if (existing is not null)
                context.Remove(existing);

            context.Add(new TrendAggregateReadModel
            {
                TenantId = tenant.TenantId,
                TotalRunsLast24h = tenant.Total,
                PassRateLast24h = tenant.Total == 0 ? 0 : (double)tenant.Passed / tenant.Total,
                FlakeRateLast24h = 0, // see comment above — not yet computable per-tenant
                LastUpdatedAt = now,
            });
        }

        if (byTenant.Count > 0)
            await context.SaveChangesAsync(ct);

        _logger.LogInformation("Trend aggregates recomputed for {TenantCount} tenant(s)", byTenant.Count);
    }
}
