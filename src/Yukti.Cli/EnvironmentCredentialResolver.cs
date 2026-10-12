using Yukti.Contracts;

namespace Yukti.Cli;

/// <summary>
/// The CLI's credential source: `YUKTI_SECRET_&lt;NAME&gt;` environment
/// variables, per the spec. The API path uses Vault
/// (VaultCredentialResolver) — this is a different ICredentialResolver
/// implementation behind the same port, exactly how the contract is meant
/// to be extended (Volume 1 Part III §18.4), not a special case in the
/// engine or any module.
/// </summary>
public sealed class EnvironmentCredentialResolver : ICredentialResolver
{
    public Task<string?> ResolveAsync(string credentialReference, CancellationToken ct) =>
        Task.FromResult(Environment.GetEnvironmentVariable($"YUKTI_SECRET_{credentialReference.ToUpperInvariant()}"));
}
