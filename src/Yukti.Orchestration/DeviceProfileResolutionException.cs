namespace Yukti.Orchestration;

/// <summary>
/// Mobile Testing Module Spec, MOB-T2: thrown by FlowEngine when a flow's declared "mobile"
/// config names a `profileName` that doesn't resolve to an existing DeviceProfile at run time
/// (deleted after publish, or publish ran without a profile checker — see
/// IDeviceProfileExistsChecker's own doc comment). Derives from VariableResolutionException for
/// the same reason RequestTemplateResolutionException does: an unresolved reference fails the
/// run, it never silently proceeds with stale or partial config.
///
/// Unlike RequestTemplateResolutionException (thrown and caught per-step, turned into a Failed
/// StepResult), this is thrown from Setup() resolution, before any step runs — it propagates out
/// of FlowEngine.Execute uncaught, the same way MobileModule.Setup()'s own ArgumentException for
/// missing "platformName" already does (Setup failures were never wrapped in a per-module
/// try/catch; see the comment on FlowEngine's Setup loop).
/// </summary>
public sealed class DeviceProfileResolutionException : VariableResolutionException
{
    public DeviceProfileResolutionException(string profileName)
        : base($"profileName:{profileName}", $"Device profile '{profileName}' could not be resolved.")
    {
    }
}
