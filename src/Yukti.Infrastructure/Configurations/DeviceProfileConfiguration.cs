using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.Json;

namespace Yukti.Infrastructure.Configurations;

public sealed class DeviceProfileConfiguration : IEntityTypeConfiguration<DeviceProfile>
{
    public void Configure(EntityTypeBuilder<DeviceProfile> builder)
    {
        builder.ToTable("device_profiles");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasConversion(id => id.Value, v => new DeviceProfileId(v));

        builder.Property(p => p.TenantId).HasConversion(id => id.Value, v => new TenantId(v));
        builder.Property(p => p.Name).IsRequired();
        builder.Property(p => p.Platform).HasConversion<string>();
        builder.Property(p => p.PlatformVersion);
        builder.Property(p => p.DeviceName);
        builder.Property(p => p.App);
        builder.Property(p => p.AppiumUrl).IsRequired();
        builder.Property(p => p.EndpointKind).HasConversion<string>();
        builder.Property(p => p.FarmCredentialRef);
        builder.Property(p => p.Reset).HasConversion<string>();
        builder.Property(p => p.SessionTimeout);
        builder.Property(p => p.Version);

        // Admin-configured escape hatch, same shape as RequestTemplate's Headers — raw jsonb,
        // never a fixed dictionary shape, because Appium's capability surface varies by driver.
        builder.Property(p => p.ExtraCapabilities)
            .HasConversion(JsonValueConverters.Dictionary)
            .HasColumnName("extra_capabilities")
            .HasColumnType("jsonb")
            .Metadata.SetValueComparer(JsonValueConverters.DictionaryComparer);

        // FR-DB-03: tenant_id leads every composite index on a multi-tenant table. Unique per
        // tenant -- "a flow references a profile by name" only works if the name is unambiguous.
        builder.HasIndex(p => new { p.TenantId, p.Name }).IsUnique();
    }
}
