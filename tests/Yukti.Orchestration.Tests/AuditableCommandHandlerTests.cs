using Xunit;
using Yukti.Application.Abstractions;
using Yukti.Application.Auditing;
using Yukti.Domain.Auditing;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Orchestration.Tests;

/// <summary>Records every AuditEntry appended, for direct assertion — the
/// same role CapturingLoggerProvider plays for logging.</summary>
internal sealed class CapturingAuditRepository : IAuditRepository
{
    public List<AuditEntry> Entries { get; } = new();

    public Task Append(AuditEntry entry, CancellationToken ct)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class FixedTenantContextAccessor : ITenantContextAccessor
{
    public FixedTenantContextAccessor(TenantId? tenantId) => CurrentTenantId = tenantId;
    public TenantId? CurrentTenantId { get; }
}

public sealed record SampleCommand(string Name, [property: SensitiveValue] string Secret) : ICommand<int>;

public sealed class SampleCommandHandler : AuditableCommandHandler<SampleCommand, int>
{
    private readonly bool _shouldFail;
    public SampleCommandHandler(IAuditRepository audit, ITenantContextAccessor tenantAccessor, IUnitOfWorkFactory uowFactory, bool shouldFail = false)
        : base(audit, tenantAccessor, uowFactory) => _shouldFail = shouldFail;

    protected override Task<int> HandleCore(SampleCommand command, CancellationToken ct) =>
        _shouldFail ? throw new InvalidOperationException("deliberate failure") : Task.FromResult(command.Name.Length);
}

public sealed class AuditableCommandHandlerTests
{
    [Fact]
    public void AuditMetadataBuilder_redacts_SensitiveValue_properties()
    {
        var metadata = AuditMetadataBuilder.Capture(new SampleCommand("alice", "super-secret-password"));

        Assert.Equal("alice", metadata["Name"]);
        Assert.Equal("***REDACTED***", metadata["Secret"]);
    }

    [Fact]
    public async Task Successful_HandleCore_appends_one_succeeded_audit_entry()
    {
        var audit = new CapturingAuditRepository();
        var tenantId = TenantId.New();
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var handler = new SampleCommandHandler(audit, new FixedTenantContextAccessor(tenantId), uowFactory);

        var result = await handler.Handle(new SampleCommand("alice", "secret"), CancellationToken.None);

        Assert.Equal(5, result);
        var entry = Assert.Single(audit.Entries);
        Assert.True(entry.Succeeded);
        Assert.Null(entry.FailureReason);
        Assert.Equal(nameof(SampleCommand), entry.CommandType);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal("***REDACTED***", entry.Metadata["Secret"]);
    }

    [Fact]
    public async Task Failing_HandleCore_still_appends_a_failed_audit_entry_and_rethrows()
    {
        var audit = new CapturingAuditRepository();
        var uowFactory = new InMemoryUnitOfWorkFactory(new InMemoryDomainEventDispatcher());
        var handler = new SampleCommandHandler(audit, new FixedTenantContextAccessor(TenantId.New()), uowFactory, shouldFail: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new SampleCommand("alice", "secret"), CancellationToken.None));

        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("deliberate failure", entry.FailureReason);
    }
}
