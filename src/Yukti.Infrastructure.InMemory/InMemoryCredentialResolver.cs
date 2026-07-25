using Yukti.Contracts;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// Demo-grade stand-in for the real Vault-backed resolver (Volume 1 Part
/// III §18.4, Volume 4 Part IV §14-15). Resolves against a simple in-memory
/// map — real implementation swaps this for a Vault client with zero
/// changes to any module or the FlowEngine, since both depend only on
/// ICredentialResolver.
/// </summary>
public sealed class InMemoryCredentialResolver : ICredentialResolver
{
    private readonly Dictionary<string, string> _secrets;

    public InMemoryCredentialResolver(Dictionary<string, string>? secrets = null) =>
        _secrets = secrets ?? new Dictionary<string, string>();

    public Task<string?> ResolveAsync(string credentialReference, CancellationToken ct) =>
        Task.FromResult(_secrets.GetValueOrDefault(credentialReference));
}
