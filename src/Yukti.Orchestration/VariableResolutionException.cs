namespace Yukti.Orchestration;

/// <summary>
/// Thrown by VariableStore when a {{vars.x.y}} reference can't be resolved
/// against the run's current variable scope. FlowEngine catches this
/// specifically and turns it into a named StepResult.Failed — a missing
/// reference must never silently become an empty string or null
/// (Volume 1 Part III §19.4).
/// </summary>
public sealed class VariableResolutionException : Exception
{
    public string Path { get; }

    public VariableResolutionException(string path)
        : base($"Unresolved variable reference '{{{{{path}}}}}': no earlier step has produced this value.")
    {
        Path = path;
    }
}
