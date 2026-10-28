using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.SharedKernel;
using Yukti.Domain.UiVocabulary;

namespace Yukti.Infrastructure.Configurations;

public sealed class ObjectRepositoryConfiguration : IEntityTypeConfiguration<ObjectRepository>
{
    public void Configure(EntityTypeBuilder<ObjectRepository> builder)
    {
        builder.ToTable("object_repositories");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasConversion(id => id.Value, v => new RepositoryId(v));

        builder.Property(r => r.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(r => r.Name).IsRequired();
        builder.Property(r => r.Version);
        builder.Property(r => r.Status).HasConversion<string>();

        // Pages/Elements are a fixed, strongly-typed shape (Locator/LocatorSpec, neither
        // polymorphic) -- a dedicated JsonSerializer round-trip, same "one jsonb column" shape
        // FlowConfiguration.cs already uses for StepResult.AiAttribution, not the generic
        // NullableObject/Dictionary converters those use for genuinely dynamic JSON (Auth,
        // Params) that must survive JsonElement normalization on read.
        builder.Property(r => r.Pages)
            .HasConversion(
                pages => JsonSerializer.Serialize(pages, (JsonSerializerOptions?)null),
                json => JsonSerializer.Deserialize<List<RepositoryPage>>(json, (JsonSerializerOptions?)null) ?? new())
            .HasColumnName("pages")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(new ValueComparer<IReadOnlyList<RepositoryPage>>(
                (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
                p => JsonSerializer.Serialize(p, (JsonSerializerOptions?)null).GetHashCode(),
                p => JsonSerializer.Deserialize<List<RepositoryPage>>(JsonSerializer.Serialize(p, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null) ?? new()));

        // FR-DB-03: tenant_id leads every composite index on a multi-tenant table.
        builder.HasIndex(r => new { r.TenantId, r.Name });
    }
}
