namespace Yukti.Orchestration;

/// <summary>
/// Thrown by VariableStore when a {{vars.x.y}} reference can't be resolved
/// against the run's current variable scope. FlowEngine catches this
/// specifically and turns it into a named StepResult.Failed — a missing
/// reference must never silently become an empty string or null
/// (Volume 1 Part III §19.4).
///
/// Not sealed: API Testing Module Spec, API-T4's SecretResolutionException
/// derives from this one (a {{secrets.name}} reference that fails to
/// resolve is the same "unresolved reference, fail the step, never a
/// silent empty value" shape as a {{vars...}} miss) so FlowEngine's
/// existing single catch clause already handles both, never a second one.
/// </summary>
public class VariableResolutionException : Exception
{
    public string Path { get; }

    public VariableResolutionException(string path)
        : base($"Unresolved variable reference '{{{{{path}}}}}': no earlier step has produced this value.")
    {
        Path = path;
    }

    protected VariableResolutionException(string path, string message) : base(message)
    {
        Path = path;
    }
}
