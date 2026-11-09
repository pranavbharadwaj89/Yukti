using Xunit;
using Yukti.Domain.MobileTesting;
using Yukti.Infrastructure.InMemory.Modules;

namespace Yukti.Orchestration.Tests;

/// <summary>
/// Mobile Testing Module Spec, MOB-T6: every device-level command, and the spec's own explicit
/// bar -- "commands unsupported on a platform fail clearly... a silent no-op makes a test pass
/// while proving nothing." No live Appium server is available in this environment (the same
/// constraint MOB-T1/T3/T4/T5's own tests document), so <c>AppiumDeviceCommandExecutor</c>'s
/// driver-bound half isn't exercisable end-to-end here. What IS provable without one: every
/// <see cref="DeviceCommand"/> type constructs, and <see cref="AppiumDeviceCommandExecutor.IsSupported"/>
/// -- the actual decision behind every platform-support failure -- for every command/platform
/// pair the spec's own checklist names.
/// </summary>
public sealed class MobileDeviceCommandTests
{
    [Fact]
    public void Every_DeviceCommand_type_from_the_domain_model_constructs()
    {
        DeviceCommand[] commands =
        {
            new Rotate(Orientation.Portrait),
            new ToggleWifi(true),
            new ToggleAirplaneMode(true),
            new PressDeviceKey(DeviceKey.Back),
            new HideKeyboard(),
            new OpenNotifications(),
            new BackgroundApp(TimeSpan.FromSeconds(5)),
            new HandlePermissionDialog(PermissionAction.Allow),
            new InstallApp(new AppSource("/path/app.apk")),
            new RemoveApp("com.example.app"),
            new ResetApp(),
            new SwitchContext(MobileContext.Native),
        };

        Assert.Equal(12, commands.Length);
        Assert.All(commands, c => Assert.IsAssignableFrom<DeviceCommand>(c));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToggleWifi_is_Android_only(bool on)
    {
        var (androidSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new ToggleWifi(on), MobilePlatform.Android);
        var (iosSupported, iosReason) = AppiumDeviceCommandExecutor.IsSupported(new ToggleWifi(on), MobilePlatform.Ios);

        Assert.True(androidSupported);
        Assert.False(iosSupported);
        Assert.Contains("iOS", iosReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToggleAirplaneMode_is_Android_only(bool on)
    {
        var (androidSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new ToggleAirplaneMode(on), MobilePlatform.Android);
        var (iosSupported, iosReason) = AppiumDeviceCommandExecutor.IsSupported(new ToggleAirplaneMode(on), MobilePlatform.Ios);

        Assert.True(androidSupported);
        Assert.False(iosSupported);
        Assert.Contains("iOS", iosReason);
    }

    [Theory]
    [InlineData(DeviceKey.Back)]
    [InlineData(DeviceKey.Enter)]
    public void PressDeviceKey_Back_and_Enter_have_no_iOS_equivalent(DeviceKey key)
    {
        var (androidSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new PressDeviceKey(key), MobilePlatform.Android);
        var (iosSupported, iosReason) = AppiumDeviceCommandExecutor.IsSupported(new PressDeviceKey(key), MobilePlatform.Ios);

        Assert.True(androidSupported);
        Assert.False(iosSupported);
        Assert.False(string.IsNullOrWhiteSpace(iosReason));
    }

    [Fact]
    public void PressDeviceKey_Home_works_on_both_platforms()
    {
        var (androidSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new PressDeviceKey(DeviceKey.Home), MobilePlatform.Android);
        var (iosSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new PressDeviceKey(DeviceKey.Home), MobilePlatform.Ios);

        Assert.True(androidSupported);
        Assert.True(iosSupported);
    }

    [Fact]
    public void OpenNotifications_is_Android_only()
    {
        var (androidSupported, _) = AppiumDeviceCommandExecutor.IsSupported(new OpenNotifications(), MobilePlatform.Android);
        var (iosSupported, iosReason) = AppiumDeviceCommandExecutor.IsSupported(new OpenNotifications(), MobilePlatform.Ios);

        Assert.True(androidSupported);
        Assert.False(iosSupported);
        Assert.Contains("iOS", iosReason);
    }

    [Theory]
    [MemberData(nameof(CrossPlatformCommands))]
    public void Every_cross_platform_command_is_supported_on_both_platforms(DeviceCommand command)
    {
        var (androidSupported, _) = AppiumDeviceCommandExecutor.IsSupported(command, MobilePlatform.Android);
        var (iosSupported, _) = AppiumDeviceCommandExecutor.IsSupported(command, MobilePlatform.Ios);

        Assert.True(androidSupported);
        Assert.True(iosSupported);
    }

    public static TheoryData<DeviceCommand> CrossPlatformCommands() => new()
    {
        new Rotate(Orientation.Landscape),
        new HideKeyboard(),
        new BackgroundApp(TimeSpan.FromSeconds(1)),
        new HandlePermissionDialog(PermissionAction.Deny),
        new InstallApp(new AppSource("https://example.com/app.ipa")),
        new RemoveApp("com.example.app"),
        new ResetApp(),
        new SwitchContext(MobileContext.Native),
        new SwitchContext(MobileContext.Webview),
    };

    [Fact]
    public void Every_not_supported_reason_is_non_empty_never_a_bare_false()
    {
        // MOB-T6: "never a bare timeout" (MOB-T1's own standard) applies equally here -- a
        // platform-unsupported failure must always name why, never just "unsupported".
        var unsupportedCases = new (DeviceCommand Command, MobilePlatform Platform)[]
        {
            (new ToggleWifi(true), MobilePlatform.Ios),
            (new ToggleAirplaneMode(false), MobilePlatform.Ios),
            (new PressDeviceKey(DeviceKey.Back), MobilePlatform.Ios),
            (new PressDeviceKey(DeviceKey.Enter), MobilePlatform.Ios),
            (new OpenNotifications(), MobilePlatform.Ios),
        };

        foreach (var (command, platform) in unsupportedCases)
        {
            var (supported, reason) = AppiumDeviceCommandExecutor.IsSupported(command, platform);
            Assert.False(supported);
            Assert.False(string.IsNullOrWhiteSpace(reason), $"{command} on {platform} has no reason.");
        }
    }

    [Fact]
    public void GetSupportedActions_exposes_every_device_command_action()
    {
        var actions = new MobileModule().GetSupportedActions().ToDictionary(a => a.ActionName);

        string[] expected =
        {
            "rotate", "toggleWifi", "toggleAirplaneMode", "pressDeviceKey", "hideKeyboard",
            "openNotifications", "backgroundApp", "handlePermissionDialog", "installApp",
            "removeApp", "resetApp", "switchContext",
        };

        foreach (var name in expected)
            Assert.True(actions.ContainsKey(name), $"Missing action schema: {name}");

        Assert.Empty(actions["hideKeyboard"].Parameters);
        Assert.Empty(actions["openNotifications"].Parameters);

        var resetApp = actions["resetApp"];
        var appIdParam = Assert.Single(resetApp.Parameters, p => p.Name == "appId");
        Assert.True(appIdParam.Required);
    }

    [Fact]
    public void Every_device_command_param_has_a_non_empty_description()
    {
        foreach (var action in new MobileModule().GetSupportedActions())
        {
            foreach (var param in action.Parameters)
            {
                Assert.False(string.IsNullOrWhiteSpace(param.Description),
                    $"{action.ActionName}.{param.Name} is missing a Description.");
            }
        }
    }
}
