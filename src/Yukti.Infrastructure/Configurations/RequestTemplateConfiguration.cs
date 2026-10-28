using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.ApiTesting;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Json;

namespace Yukti.Infrastructure.Configurations;

public sealed class RequestTemplateConfiguration : IEntityTypeConfiguration<RequestTemplate>
{
    public void Configure(EntityTypeBuilder<RequestTemplate> builder)
    {
        builder.ToTable("request_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasConversion(id => id.Value, v => new RequestTemplateId(v));

        builder.Property(t => t.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(t => t.Name).IsRequired();
        builder.Property(t => t.BaseUrl);
        builder.Property(t => t.DefaultTimeoutMs);
        builder.Property(t => t.Version);

        builder.Property(t => t.Headers)
            .HasConversion(JsonValueConverters.Dictionary)
            .HasColumnName("headers")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

        // Auth is genuinely polymorphic (basic/bearer/apiKey/oauth2ClientCredentials all have
        // different fields) -- same reasoning as ApiRequest.Assertions: raw NullableObject
        // jsonb, not a fixed dictionary shape.
        builder.Property(t => t.Auth)
            .HasConversion(JsonValueConverters.NullableObject)
            .HasColumnName("auth")
            .HasColumnType("jsonb");

        // FR-DB-03: tenant_id leads every composite index on a multi-tenant table.
        builder.HasIndex(t => new { t.TenantId, t.Name });
    }
}
