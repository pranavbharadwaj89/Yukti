using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.Ai;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Configurations;

/// <summary>D5-T9: ai_cost_records — flat columns, not a jsonb blob, since every field here is queried/aggregated (the usage endpoint, the budget guard), unlike StepResult.AiAttribution's one-off "present iff AI touched this step" shape.</summary>
public sealed class AiCostRecordConfiguration : IEntityTypeConfiguration<AiCostRecord>
{
    public void Configure(EntityTypeBuilder<AiCostRecord> builder)
    {
        builder.ToTable("ai_cost_records");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasConversion(id => id.Value, v => new AiCostRecordId(v));

        builder.Property(r => r.TenantId).HasConversion(id => id.Value, v => new TenantId(v)).IsRequired();
        builder.Property(r => r.Capability).IsRequired();
        builder.Property(r => r.Provider).IsRequired();
        builder.Property(r => r.PromptVersion).IsRequired();
        builder.Property(r => r.Attempt).IsRequired();
        builder.Property(r => r.Succeeded).IsRequired();
        builder.Property(r => r.BudgetRefused).IsRequired();
        builder.Property(r => r.InputTokens).IsRequired();
        builder.Property(r => r.OutputTokens).IsRequired();
        builder.Property(r => r.CostUsd).HasColumnType("numeric(12,6)").IsRequired();
        builder.Property(r => r.OccurredAt).IsRequired();

        // The budget guard's own pre-call query (AiBudgetGuard) and the usage endpoint both
        // filter by (TenantId, OccurredAt >= since) — every real read against this table.
        builder.HasIndex(r => new { r.TenantId, r.OccurredAt });
    }
}
