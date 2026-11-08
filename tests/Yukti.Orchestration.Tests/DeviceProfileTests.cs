using Xunit;
using Yukti.Domain.MobileTesting;
using Yukti.Domain.SharedKernel;

namespace Yukti.Orchestration.Tests;

/// <summary>Mobile Testing Module Spec, MOB-T2: DeviceProfile's own invariants, the same
/// "disposable, editable resource" shape RequestTemplate already has (a visible Version counter,
/// no publish step).</summary>
public sealed class DeviceProfileTests
{
    [Fact]
    public void Create_rejects_an_empty_name()
    {
        Assert.Throws<DomainException>(() => DeviceProfile.Create(
            "", TenantId.New(), MobilePlatform.Android, null, "pixel-7", null, "http://localhost:4723",
            DeviceEndpointKind.Local, null));
    }

    [Fact]
    public void Create_rejects_an_empty_appium_url()
    {
        Assert.Throws<DomainException>(() => DeviceProfile.Create(
            "pixel-7-android-14", TenantId.New(), MobilePlatform.Android, null, "pixel-7", null, "   ",
            DeviceEndpointKind.Local, null));
    }

    [Fact]
    public void Create_defaults_to_Fast_reset_and_a_five_minute_session_timeout()
    {
        var profile = DeviceProfile.Create(
            "pixel-7-android-14", TenantId.New(), MobilePlatform.Android, null, "pixel-7", null, "http://localhost:4723",
            DeviceEndpointKind.Local, null);

        Assert.Equal(ResetStrategy.Fast, profile.Reset);
        Assert.Equal(TimeSpan.FromMinutes(5), profile.SessionTimeout);
        Assert.Equal(1, profile.Version);
        Assert.Empty(profile.ExtraCapabilities);
    }

    [Fact]
    public void Update_increments_the_visible_version_counter()
    {
        var profile = DeviceProfile.Create(
            "pixel-7-android-14", TenantId.New(), MobilePlatform.Android, null, "pixel-7", null, "http://localhost:4723",
            DeviceEndpointKind.Local, null);

        profile.Update(
            "pixel-7-android-14", MobilePlatform.Android, "14", "pixel-7", "/path/to.apk", "http://localhost:4724",
            DeviceEndpointKind.SelfHosted, "farm-key", ResetStrategy.Full, TimeSpan.FromMinutes(10), null);

        Assert.Equal(2, profile.Version);
        Assert.Equal("http://localhost:4724", profile.AppiumUrl);
        Assert.Equal(DeviceEndpointKind.SelfHosted, profile.EndpointKind);
        Assert.Equal(ResetStrategy.Full, profile.Reset);
    }

    [Fact]
    public void Update_rejects_an_empty_name_leaving_the_profile_unchanged()
    {
        var profile = DeviceProfile.Create(
            "pixel-7-android-14", TenantId.New(), MobilePlatform.Android, null, "pixel-7", null, "http://localhost:4723",
            DeviceEndpointKind.Local, null);

        Assert.Throws<DomainException>(() => profile.Update(
            "", MobilePlatform.Android, null, "pixel-7", null, "http://localhost:4723",
            DeviceEndpointKind.Local, null, ResetStrategy.Fast, TimeSpan.FromMinutes(5), null));

        Assert.Equal("pixel-7-android-14", profile.Name);
        Assert.Equal(1, profile.Version); // rejected before any field, including Version, was touched
    }
}
