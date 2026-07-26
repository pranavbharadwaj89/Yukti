using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.FlowAuthoring;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Json;

namespace Yukti.Infrastructure.Configurations;

public sealed class FlowConfiguration : IEntityTypeConfiguration<Flow>
{
    public void Configure(EntityTypeBuilder<Flow> builder)
    {
        builder.ToTable("flows");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasConversion(id => id.Value, v => new FlowId(v));

        builder.Property(f => f.FamilyId).HasConversion(id => id.Value, v => new FlowFamilyId(v));
        builder.Property(f => f.Version);
        builder.Property(f => f.Name).IsRequired();
        builder.Property(f => f.Description);
        builder.Property(f => f.Status).HasConversion<string>();
        builder.Property(f => f.ContinueOnFailure);
        builder.Property(f => f.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(f => f.CreatedBy).HasConversion(id => id.Value, v => new UserId(v));

        // FlowId + Version together identify one immutable published
        // version (§9.2's core invariant) — a family can have many rows,
        // one per version, never mutated once Published.
        builder.HasIndex(f => new { f.FamilyId, f.Version });

        builder.Property(f => f.DeclaredVariables)
            .HasConversion(JsonValueConverters.Dictionary)
            .HasColumnName("declared_variables")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

        // No explicit SetPropertyAccessMode needed — Steps is a get-only
        // property (no setter), so EF Core automatically falls back to the
        // _steps backing field by convention.
        builder.OwnsMany(f => f.Steps, steps =>
        {
            steps.ToTable("flow_steps");
            steps.WithOwner().HasForeignKey("FlowId");
            steps.HasKey(s => s.Id);
            steps.Property(s => s.Id).HasConversion(id => id.Value, v => new FlowStepId(v));
            steps.Property(s => s.Name).IsRequired();
            steps.Property(s => s.Module).HasConversion(k => k.Value, v => ModuleKind.Custom(v));
            steps.Property(s => s.Action).IsRequired();
            steps.Property(s => s.SaveAs);
            steps.Property(s => s.WhenCondition).HasColumnName("when_condition");
            steps.Property(s => s.Order);

            steps.Property(s => s.Params)
                .HasConversion(JsonValueConverters.Dictionary)
                .HasColumnName("params")
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

            // Step order is part of the Flow's consistency boundary
            // (FlowStep.cs's own doc comment) — enforced here as a unique
            // composite index, not just an in-memory List<T> ordering.
            steps.HasIndex("FlowId", nameof(FlowStep.Order)).IsUnique();
        });
    }
}
