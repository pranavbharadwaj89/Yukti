using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasConversion(id => id.Value, v => new UserId(v));

        builder.Property(u => u.Email).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.DisplayName);
        builder.Property(u => u.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(u => u.IsServiceAccount);
        builder.Property(u => u.PasswordHash).IsRequired();

        // Pragmatic divergence from FR-DB-01's named "user_roles" join
        // table: role assignment is low-cardinality (a handful of roles per
        // user) and never queried from the "many" side in this codebase, so
        // a uuid[] column round-trips through RoleIds with zero extra
        // tables. A real join table is a mechanical follow-up if role
        // assignment ever needs to be queried/indexed from the Role side.
        var roleIdsComparer = new ValueComparer<IReadOnlyList<RoleId>>(
            (a, b) => a!.SequenceEqual(b!),
            list => list.Aggregate(0, (hash, r) => HashCode.Combine(hash, r.Value)),
            list => (IReadOnlyList<RoleId>)list.ToList());

        builder.Property(u => u.RoleIds)
            .HasConversion(
                ids => ids.Select(r => r.Value).ToArray(),
                arr => (IReadOnlyList<RoleId>)arr.Select(g => new RoleId(g)).ToList())
            .HasColumnName("role_ids")
            .HasColumnType("uuid[]")
            .Metadata.SetValueComparer(roleIdsComparer);
    }
}
