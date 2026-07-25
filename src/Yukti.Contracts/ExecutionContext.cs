using Yukti.Domain.SharedKernel;

namespace Yukti.Contracts;

/// <summary>
/// Scoped per flow-run and per module, resolving only the specific named
/// credentials a given FlowStep's parameters reference — a module cannot
/// enumerate or access any credential beyond what the flow author
/// explicitly wired into that step. (Volume 1 Part III §18.4)
/// </summary>
public interface ICredentialResolver
{
    Task<string?> ResolveAsync(string credentialReference, CancellationToken ct);
}

/// <summary>
/// What a module is given, and — deliberately — what it is NOT given: no
/// reference to the Flow/FlowRun aggregates, no repository, no ability to
/// issue commands or queries. Modules receive exactly the narrow slice of
/// context needed to do their job and report a result. (Volume 1 Part III §18.3)
/// </summary>
public sealed class ExecutionContext
{
    public required FlowRunId RunId { get; init; }
    public required IReadOnlyDictionary<string, object?> Variables { get; init; }
    public required ICredentialResolver Credentials { get; init; }
    public required CancellationToken RunCancellation { get; init; }
}
