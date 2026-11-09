namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Mobile Testing Module Spec, MOB-T6: "Commands unsupported on a platform fail clearly...
/// rather than silently doing nothing." Thrown before any command attempt, naming the real
/// reason from <see cref="AppiumDeviceCommandExecutor.IsSupported"/> -- never a bare "not
/// supported".
/// </summary>
public sealed class MobileDeviceCommandNotSupportedException : Exception
{
    public MobileDeviceCommandNotSupportedException(string reason) : base(reason)
    {
    }
}
