namespace Yukti.Orchestration;

/// <summary>
/// Web UI Testing Module Spec, WEB-T2: thrown by FlowEngine when a step's
/// <c>RepositoryRef</c> doesn't resolve to an existing page/element at run time (deleted or
/// renamed after publish, or published without a checker). Derives from
/// VariableResolutionException for the same reason RequestTemplateResolutionException does: an
/// unresolved reference fails the step, never silently proceeds, and FlowEngine's existing
/// single catch clause already handles it.
/// </summary>
public sealed class RepositoryEntryResolutionException : VariableResolutionException
{
    public RepositoryEntryResolutionException(string page, string element)
        : base($"repositoryRef:{page}.{element}", $"Repository element '{page}.{element}' could not be resolved.")
    {
    }
}
