using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Json;

namespace Yukti.Infrastructure.Configurations;

public sealed class FlowRunConfiguration : IEntityTypeConfiguration<FlowRun>
{
    public void Configure(EntityTypeBuilder<FlowRun> builder)
    {
        builder.ToTable("flow_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasConversion(id => id.Value, v => new FlowRunId(v));

        // Pins to the exact published FlowId, never FlowFamilyId (§9.3's
        // invariant) — deliberately no FK/navigation to Flow here, matching
        // FR-DOM-15's cross-aggregate rule (repositories, not aggregate
        // references, mediate any cross-aggregate lookup).
        builder.Property(r => r.FlowId).HasConversion(id => id.Value, v => new FlowId(v));
        builder.Property(r => r.Status).HasConversion<string>();
        builder.Property(r => r.Trigger).HasConversion<string>();
        builder.Property(r => r.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(r => r.StartedAt);
        builder.Property(r => r.FinishedAt);

        builder.Property(r => r.Variables)
            .HasConversion(JsonValueConverters.Dictionary)
            .HasColumnName("variables")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

        builder.OwnsMany(r => r.Results, results =>
        {
            results.ToTable("step_results");
            results.WithOwner().HasForeignKey("FlowRunId");
            results.HasKey(s => s.Id);
            results.Property(s => s.Id).HasConversion(id => id.Value, v => new StepResultId(v));
            results.Property(s => s.SourceStepId).HasConversion(id => id.Value, v => new FlowStepId(v));
            results.Property(s => s.StepName).IsRequired();
            results.Property(s => s.Module).HasConversion(k => k.Value, v => ModuleKind.Custom(v));
            results.Property(s => s.Action).IsRequired();
            results.Property(s => s.Status).HasConversion<string>();
            results.Property(s => s.Duration);
            results.Property(s => s.Message);
            results.Property(s => s.Error);

            // StepResult.Data is genuinely polymorphic per module (a string,
            // a parsed JSON body, a nested object, or null) — no fixed
            // shape, so it's the one column stored as raw jsonb with no
            // dictionary assumption.
            results.Property(s => s.Data)
                .HasConversion(JsonValueConverters.NullableObject)
                .HasColumnName("data")
                .HasColumnType("jsonb");

            // AiAttribution has no independent identity or query pattern of
            // its own in this codebase (FR-DOM-12: present iff AI touched
            // the step) — one nullable jsonb column, not a child table.
            results.Property(s => s.AiAttribution)
                .HasConversion(
                    attr => attr == null ? null : JsonSerializer.Serialize(attr, (JsonSerializerOptions?)null),
                    json => json == null ? null : JsonSerializer.Deserialize<AiAttribution>(json, (JsonSerializerOptions?)null))
                .HasColumnName("ai_attribution")
                .HasColumnType("jsonb");

            results.OwnsMany(s => s.RetryHistory, attempts =>
            {
                attempts.ToTable("retry_attempts");
                attempts.WithOwner().HasForeignKey("StepResultId");
                attempts.HasKey(a => a.Id);
                attempts.Property(a => a.Id).HasConversion(id => id.Value, v => new RetryAttemptId(v));
                attempts.Property(a => a.AttemptNumber);
                attempts.Property(a => a.Status).HasConversion<string>();
                attempts.Property(a => a.Duration);
                attempts.Property(a => a.Error);
                attempts.Property(a => a.AttemptedAt);
            });
        });
    }
}
