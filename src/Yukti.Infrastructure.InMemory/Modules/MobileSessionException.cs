namespace Yukti.Infrastructure.InMemory.Modules;

/// <summary>
/// Mobile Testing Module Spec, MOB-T1: "Connection failures are specific... Never a bare
/// timeout — a bare timeout on a mobile setup costs a tester an hour of guessing." This is
/// MobileModule's typed signal for every session-creation failure, always naming the device
/// profile and the Appium endpoint, and always carrying the real underlying error (Appium's
/// own message, or the raw socket/HTTP failure) rather than hiding it behind a generic
/// wrapper — see MobileModule.ClassifySessionFailure for how the message is chosen.
/// </summary>
public sealed class MobileSessionException : Exception
{
    public MobileSessionException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
