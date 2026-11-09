namespace Yukti.Domain.MobileTesting;

/// <summary>
/// Mobile Testing Module Spec, MOB-T6: every device-level (not UI-element-level) command a
/// mobile flow can author, implemented in
/// <c>Yukti.Infrastructure.InMemory.Modules.AppiumDeviceCommandExecutor</c>.
///
/// The spec's own type signature names this enum's values' type <c>AppContext</c>; renamed here
/// to <see cref="MobileContext"/> because <c>System.AppContext</c> (a real BCL static class) is
/// in scope everywhere via the implicit <c>System</c> using, and a same-named enum would make
/// every unqualified <c>AppContext</c> reference ambiguous (CS0104) in any file that also
/// references this namespace -- a necessity, not a style choice, flagged rather than silently
/// different from the spec's own prose.
/// </summary>
public abstract record DeviceCommand;

public sealed record Rotate(Orientation To) : DeviceCommand;

public sealed record ToggleWifi(bool On) : DeviceCommand;

public sealed record ToggleAirplaneMode(bool On) : DeviceCommand;

public sealed record PressDeviceKey(DeviceKey Key) : DeviceCommand;

public sealed record HideKeyboard : DeviceCommand;

public sealed record OpenNotifications : DeviceCommand;

public sealed record BackgroundApp(TimeSpan Duration) : DeviceCommand;

public sealed record HandlePermissionDialog(PermissionAction Action) : DeviceCommand;

public sealed record InstallApp(AppSource App) : DeviceCommand;

public sealed record RemoveApp(string BundleId) : DeviceCommand;

public sealed record ResetApp : DeviceCommand;

public sealed record SwitchContext(MobileContext Context) : DeviceCommand;

public enum Orientation
{
    Portrait,
    Landscape,
}

public enum DeviceKey
{
    Back,
    Home,
    Enter,
}

public enum PermissionAction
{
    Allow,
    Deny,
}

public enum MobileContext
{
    Native,
    Webview,
}

/// <summary>Where <see cref="InstallApp"/> installs from -- a local path on the Appium server's
/// own machine, or a URL the server fetches it from. Appium's own <c>installApp</c> command
/// accepts either, undifferentiated by type -- this stays a single string wrapper rather than
/// inventing a path-vs-url distinction the real command itself doesn't make.</summary>
public sealed record AppSource(string Location);
