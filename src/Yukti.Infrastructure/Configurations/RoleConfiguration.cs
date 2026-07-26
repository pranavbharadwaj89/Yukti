using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasConversion(id => id.Value, v => new RoleId(v));

        builder.Property(r => r.Name).IsRequired();
        builder.Property(r => r.TenantId).HasConversion(
            id => id == null ? (Guid?)null : id.Value.Value,
            v => v == null ? (TenantId?)null : new TenantId(v.Value));
        builder.Property(r => r.Version);

        // Permission is a closed enum (FR-AUTHZ-01) — stored as a jsonb
        // array of strings rather than Cockroach's native enum/array types,
        // so adding a Permission value never requires a schema migration.
        var permissionsComparer = new ValueComparer<IReadOnlySet<Permission>>(
            (a, b) => a!.SetEquals(b!),
            s => s.Aggregate(0, (hash, p) => HashCode.Combine(hash, p)),
            s => (IReadOnlySet<Permission>)s.ToHashSet());

        builder.Property(r => r.Permissions)
            .HasConversion(
                perms => JsonSerializer.Serialize(perms.Select(p => p.ToString()), (JsonSerializerOptions?)null),
                json => (IReadOnlySet<Permission>)(JsonSerializer.Deserialize<List<string>>(json, (JsonSerializerOptions?)null) ?? new())
                    .Select(Enum.Parse<Permission>).ToHashSet())
            .HasColumnName("permissions")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(permissionsComparer);
    }
}
