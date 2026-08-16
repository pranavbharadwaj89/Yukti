using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.ProjectManagement;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Json;

namespace Yukti.Infrastructure.Configurations;

public sealed class TestEnvironmentConfiguration : IEntityTypeConfiguration<TestEnvironment>
{
    public void Configure(EntityTypeBuilder<TestEnvironment> builder)
    {
        builder.ToTable("test_environments");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasConversion(id => id.Value, v => new TestEnvironmentId(v));

        builder.Property(e => e.ProjectId).HasConversion(id => id.Value, v => new ProjectId(v));
        builder.Property(e => e.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(e => e.Name).IsRequired();

        // Same jsonb-dictionary convention as ApiRequest.Headers
        // (ApiCollectionConfiguration.cs) — no fixed variable schema by
        // design, mirrors TriggerFlowRunCommand.VariableOverrides' shape.
        builder.Property(e => e.Variables)
            .HasConversion(JsonValueConverters.Dictionary)
            .HasColumnName("variables")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

        // FR-DB-03: tenant_id leads; project_id is the actual filter FE list queries use.
        builder.HasIndex(e => new { e.TenantId, e.ProjectId });
    }
}
