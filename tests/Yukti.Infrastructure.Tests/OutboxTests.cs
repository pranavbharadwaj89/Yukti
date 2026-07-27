using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Domain.Events;
using Yukti.Domain.Execution;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.ReadModels;

namespace Yukti.Infrastructure.Tests;

public sealed class OutboxMessageTests
{
    [Fact]
    public void From_and_Deserialize_round_trips_a_domain_event_exactly()
    {
        var original = new FlowRunCompletedEvent(
            FlowRunId.New(), RunStatus.Passed, PassedCount: 3, FailedCount: 0, SkippedCount: 1,
            TotalDuration: TimeSpan.FromSeconds(12), OccurredAt: DateTimeOffset.UtcNow);

        var message = OutboxMessage.From(original);
        var roundTripped = message.Deserialize();

        Assert.Equal(original, roundTripped);
    }
}

internal sealed class FixedTenantContextAccessor : ITenantContextAccessor
{
    public FixedTenantContextAccessor(TenantId tenantId) => CurrentTenantId = tenantId;
    public TenantId? CurrentTenantId { get; }
}

/// <summary>
/// FR-EVT-02/FR-CQRS-02 live evidence — same environment-dependent,
/// Skip()-if-unconfigured pattern as AuditEntryGrantsTests.
/// </summary>
public sealed class FlowReportProjectionConsumerTests
{
    private static string? GetConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<FlowReportProjectionConsumerTests>()
            .AddEnvironmentVariables()
            .Build();
        return config.GetConnectionString("Yukti");
    }

    [Fact]
    public async Task Handle_is_idempotent_under_redelivery_of_the_same_event()
    {
        var connectionString = GetConnectionString();
        if (connectionString is null)
            return; // Skip: no live DB configured in this environment.

        var options = new DbContextOptionsBuilder<YuktiDbContext>().UseNpgsql(connectionString).Options;

        var tenantId = TenantId.New();

        await using var seed = new YuktiDbContext(options);
        await seed.Database.OpenConnectionAsync();
        await seed.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('app.current_tenant_id', {tenantId.Value.ToString()}, false)");

        var flow = Yukti.Domain.FlowAuthoring.Flow.CreateDraft("Outbox test flow", null, tenantId, UserId.New());
        seed.Add(flow);
        var run = FlowRun.Create(flow.Id, RunTrigger.Api, tenantId);
        run.Start();
        seed.Add(run);
        await seed.SaveChangesAsync();

        var evt = new FlowRunCompletedEvent(run.Id, RunStatus.Passed, 1, 0, 0, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow);

        var consumer = new FlowReportProjectionConsumer(seed);

        // Redeliver the same event twice — simulates the outbox relay's
        // at-least-once guarantee under a crash/retry.
        await consumer.Handle(evt, CancellationToken.None);
        await consumer.Handle(evt, CancellationToken.None);

        var rows = await seed.FlowReports.Where(r => r.FlowRunId == run.Id).ToListAsync();
        Assert.Single(rows); // upsert-by-key, never a duplicate row
    }
}
