namespace Yukti.Orchestration;

/// <summary>
/// API Testing Module Spec, API-T9: thrown by FlowEngine when a step's `templateId` doesn't
/// resolve to an existing RequestTemplate at run time (deleted after publish, or publish ran
/// without a template checker — see IRequestTemplateExistsChecker's own doc comment). Derives
/// from VariableResolutionException for the same reason SecretResolutionException does: this is
/// the same "unresolved reference, fail the step, never silently proceed" shape, so FlowEngine's
/// existing single catch clause already handles it.
/// </summary>
public sealed class RequestTemplateResolutionException : VariableResolutionException
{
    public RequestTemplateResolutionException(string templateId)
        : base($"templateId:{templateId}", $"Request template '{templateId}' could not be resolved.")
    {
    }
}
