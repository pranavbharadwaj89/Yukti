using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.ProjectManagement;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasConversion(id => id.Value, v => new ProjectId(v));

        builder.Property(p => p.Name).IsRequired();
        builder.Property(p => p.Description);
        builder.Property(p => p.TenantId).HasConversion(id => id.Value, v => new TenantId(v));

        // FR-DB-03: tenant_id leads every composite index on a multi-tenant table.
        builder.HasIndex(p => new { p.TenantId, p.Name });
    }
}
