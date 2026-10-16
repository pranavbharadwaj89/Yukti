namespace Yukti.Application.Ai;

/// <summary>
/// Token-economy spec (<c>plan/specs/SARVAGHNA-TOKEN-ECONOMY.md</c> §R4): per-capability caps,
/// in config, never a literal scattered through the pipeline. <see cref="Default"/> is the one
/// place a fallback number is allowed to exist as a literal — the source-check test
/// (<c>AiCapabilityOptionsTests.No_production_source_outside_this_file_assigns_a_MaxOutputTokens_literal</c>)
/// enforces that nothing else does.
/// </summary>
public sealed record AiCapabilityOptions(int MaxInputChars, int MaxOutputTokens, double Temperature)
{
    public static readonly AiCapabilityOptions Default = new(MaxInputChars: 4000, MaxOutputTokens: 2000, Temperature: 0);
}

/// <summary>
/// Resolves a capability's token caps from configuration (<c>Ai:Capabilities:&lt;name&gt;</c>) and
/// the pipeline-wide validation-retry ceiling (<c>Ai:MaxValidationRetries</c>, R3) — both fall back to
/// <see cref="AiCapabilityOptions.Default"/> / 1 respectively when unconfigured, so a capability that
/// hasn't been given its own section (or a fresh test double) still gets a sane, finite cap rather
/// than an unbounded call.
/// </summary>
public interface IAiCapabilityOptionsProvider
{
    AiCapabilityOptions GetFor(string capability);

    int MaxValidationRetries { get; }
}
