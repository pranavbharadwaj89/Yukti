using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Configurations;

public sealed class ModuleRegistrationConfiguration : IEntityTypeConfiguration<ModuleRegistration>
{
    public void Configure(EntityTypeBuilder<ModuleRegistration> builder)
    {
        builder.ToTable("module_registrations");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasConversion(id => id.Value, v => new ModuleRegistrationId(v));

        // ModuleKind is an open value type (FR-DOM-09), not a closed enum —
        // Custom() round-trips any built-in or marketplace kind identically.
        builder.Property(m => m.Kind).HasConversion(k => k.Value, v => ModuleKind.Custom(v));
        builder.Property(m => m.DisplayName).IsRequired();
        builder.Property(m => m.Trust).HasConversion<string>();
        builder.Property(m => m.ContractVersion).IsRequired();
        builder.Property(m => m.IsActive);
        builder.Property(m => m.TenantId).HasConversion(
            id => id == null ? (Guid?)null : id.Value.Value,
            v => v == null ? (TenantId?)null : new TenantId(v.Value));

        builder.OwnsMany(m => m.Actions, actions =>
        {
            actions.ToTable("module_action_entries");
            actions.WithOwner().HasForeignKey("ModuleRegistrationId");
            actions.HasKey(a => a.Id);
            actions.Property(a => a.Id).HasConversion(id => id.Value, v => new ModuleActionEntryId(v));
            actions.Property(a => a.ActionName).IsRequired();
            actions.Property(a => a.IsDeprecated);
            actions.Property(a => a.DeprecationNotice);

            // ActionSchema (ParamSpec list, descriptions) is read-mostly
            // module-authoring metadata, not something queried relationally
            // — one jsonb column round-trips the whole record.
            actions.Property(a => a.Schema)
                .HasConversion(
                    schema => JsonSerializer.Serialize(schema, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<ActionSchema>(json, (JsonSerializerOptions?)null)!)
                .HasColumnName("schema")
                .HasColumnType("jsonb");
        });
    }
}
