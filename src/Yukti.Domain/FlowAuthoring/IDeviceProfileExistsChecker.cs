using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.FlowAuthoring;

/// <summary>
/// Mobile Testing Module Spec, MOB-T2: same cross-aggregate port shape as
/// IRequestTemplateExistsChecker — Flow.Publish needs to know whether a flow's declared
/// "mobile" config currently names an existing DeviceProfile (a different aggregate), without
/// Flow depending on its repository directly. Checked by name, not id — "a flow references a
/// profile by name, never a raw capability blob" is the spec's own governing rule for how a
/// profile is referenced at all, unlike RequestTemplate (referenced by templateId).
/// Optional on Flow.Publish (defaults to null): every existing caller that doesn't care about
/// device profiles keeps compiling and behaving identically; only the real publish command
/// handler, which has a real repository to check against, passes one.
/// </summary>
public interface IDeviceProfileExistsChecker
{
    Task<bool> ExistsAsync(string profileName, TenantId tenantId, CancellationToken ct);
}
