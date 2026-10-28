using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.FlowAuthoring;

/// <summary>
/// API Testing Module Spec, API-T9: same cross-aggregate port shape as IModuleActionResolver —
/// Flow.Publish needs to know whether a step's referenced RequestTemplate (a different
/// aggregate) currently exists, without Flow depending on its repository directly. Optional on
/// Flow.Publish (defaults to null): every existing caller that doesn't care about templates
/// keeps compiling and behaving identically; only the real publish command handler, which has a
/// real repository to check against, passes one.
/// </summary>
public interface IRequestTemplateExistsChecker
{
    Task<bool> ExistsAsync(string requestTemplateId, TenantId tenantId, CancellationToken ct);
}
