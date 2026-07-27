using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.Auditing;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Json;

namespace Yukti.Infrastructure.Configurations;

/// <summary>
/// FR-DB-01/FR-AUDIT: audit_entries. Metadata is genuinely per-command-type
/// polymorphic (whatever properties that command's record declares), so it
/// maps to jsonb the same way StepResult.Data does, not a fixed column set.
/// </summary>
public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasConversion(id => id.Value, v => new AuditEntryId(v));
        builder.Property(e => e.CommandType).IsRequired();

        // FR-DB-02: nullable — see AuditEntry's own doc comment for why
        // (self-registration, process-startup seeding). Same conversion
        // shape as ModuleRegistration.TenantId.
        builder.Property(e => e.TenantId).HasConversion(
            id => id == null ? (Guid?)null : id.Value.Value,
            v => v == null ? (TenantId?)null : new TenantId(v.Value));

        builder.Property(e => e.Succeeded).IsRequired();
        builder.Property(e => e.FailureReason);
        builder.Property(e => e.OccurredAt).IsRequired();

        builder.Property(e => e.Metadata)
            .HasConversion(JsonValueConverters.Dictionary)
            .HasColumnName("metadata")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);
    }
}
