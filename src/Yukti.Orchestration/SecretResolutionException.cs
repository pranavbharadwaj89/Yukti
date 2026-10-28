namespace Yukti.Orchestration;

/// <summary>
/// API Testing Module Spec, API-T4: thrown by VariableStore when a {{secrets.name}} reference
/// can't be resolved — the credential doesn't exist in Vault, or Vault itself is unreachable.
/// Names the secret by its reference NAME only, never its value, and never falls back to sending
/// nothing — the step simply fails, naming what's missing.
/// </summary>
public sealed class SecretResolutionException : VariableResolutionException
{
    public SecretResolutionException(string secretName)
        : base($"secrets.{secretName}", $"Secret '{secretName}' could not be resolved. Check it exists and the credential resolver is configured correctly.")
    {
    }
}
