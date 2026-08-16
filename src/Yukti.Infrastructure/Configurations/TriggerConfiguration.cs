using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.Scheduling;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Configurations;

public sealed class TriggerConfiguration : IEntityTypeConfiguration<TriggerDefinition>
{
    public void Configure(EntityTypeBuilder<TriggerDefinition> builder)
    {
        builder.ToTable("triggers");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasConversion(id => id.Value, v => new TriggerId(v));

        builder.Property(t => t.FlowId).HasConversion(id => id.Value, v => new FlowId(v));
        builder.Property(t => t.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(t => t.RegisteredBy).HasConversion(id => id.Value, v => new UserId(v));
        builder.Property(t => t.Kind).HasConversion<string>();
        builder.Property(t => t.IsEnabled);
        builder.Property(t => t.LastFiredAt);
        builder.Property(t => t.CronExpression);
        builder.Property(t => t.WebhookPath);
        builder.Property(t => t.WebhookSecret);
        builder.Property(t => t.WatchPath);

        // FR-DB-03: tenant_id leads; webhook_path needs its own lookup index
        // for GetByWebhookPath's incoming-request-routing hot path.
        builder.HasIndex(t => new { t.TenantId, t.FlowId });
        builder.HasIndex(t => t.WebhookPath);
    }
}
