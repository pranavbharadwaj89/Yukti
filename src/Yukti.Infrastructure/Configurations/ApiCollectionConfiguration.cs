using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Json;

namespace Yukti.Infrastructure.Configurations;

public sealed class ApiCollectionConfiguration : IEntityTypeConfiguration<ApiCollection>
{
    public void Configure(EntityTypeBuilder<ApiCollection> builder)
    {
        builder.ToTable("api_collections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasConversion(id => id.Value, v => new ApiCollectionId(v));

        builder.Property(c => c.Name).IsRequired();
        builder.Property(c => c.Description);
        builder.Property(c => c.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(c => c.ProjectId).HasConversion(
            id => id == null ? (Guid?)null : id.Value.Value,
            v => v == null ? (ProjectId?)null : new ProjectId(v.Value));

        // FR-DB-03: tenant_id leads every composite index on a multi-tenant table.
        builder.HasIndex(c => new { c.TenantId, c.Name });

        builder.OwnsMany(c => c.Requests, requests =>
        {
            requests.ToTable("api_requests");
            requests.WithOwner().HasForeignKey("ApiCollectionId");
            requests.HasKey(r => r.Id);
            requests.Property(r => r.Id).HasConversion(id => id.Value, v => new ApiRequestId(v));
            requests.Property(r => r.Name).IsRequired();
            requests.Property(r => r.Method).IsRequired();
            requests.Property(r => r.Url).IsRequired();
            requests.Property(r => r.Order);

            requests.Property(r => r.Headers)
                .HasConversion(JsonValueConverters.Dictionary)
                .HasColumnName("headers")
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

            requests.Property(r => r.QueryParams)
                .HasConversion(JsonValueConverters.Dictionary)
                .HasColumnName("query_params")
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

            // Body/Assertions are genuinely polymorphic (object/array/string/
            // null) — same reasoning as StepResult.Data in FlowRunConfiguration:
            // no fixed dictionary shape, so raw NullableObject jsonb, not
            // JsonValueConverters.Dictionary.
            requests.Property(r => r.Body)
                .HasConversion(JsonValueConverters.NullableObject)
                .HasColumnName("body")
                .HasColumnType("jsonb");

            requests.Property(r => r.Assertions)
                .HasConversion(JsonValueConverters.NullableObject)
                .HasColumnName("assertions")
                .HasColumnType("jsonb");

            requests.HasIndex("ApiCollectionId", nameof(ApiRequest.Order)).IsUnique();
        });
    }
}
