using System.Text.RegularExpressions;

namespace Yukti.Application.Ai;

/// <summary>
/// D5-T3's input-side guardrail — the cheapest point to enforce a boundary
/// (Volume 3 §2.4's own reasoning): reject an empty description before the
/// provider is ever involved, redact anything that looks like a secret
/// before it leaves this process, and reject outright (never silently
/// truncate — token-economy spec §R1/R4) when there's nothing left worth
/// sending: too long, or the whole thing was a secret. Pure, no I/O, so
/// Analyst/Coder's own tests (D5-T4/T5) can exercise it without a provider.
/// </summary>
public static class AiGuardrailPreCheck
{
    public const int MaxDescriptionLength = 4000;

    // Deliberately simple, pattern-based heuristics — this is a guardrail,
    // not a secret-scanning product. Catches the obvious cases named in
    // the spec ("keys, bearer tokens, anything that looks like a
    // password") without pretending to be exhaustive. An explicit match
    // timeout bounds every pattern against catastrophic backtracking on
    // attacker-controlled input (Sarvaghna review, this PR) — none of
    // these patterns are nested/ambiguous enough to realistically hit it,
    // but a guardrail that can itself be hung by the input it's guarding
    // against defeats the point.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly (string Name, Regex Pattern)[] SecretPatterns =
    [
        ("sk-prefixed secret key", new Regex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled, MatchTimeout)),
        ("Google API key", new Regex(@"AIza[A-Za-z0-9_\-]{35}", RegexOptions.Compiled, MatchTimeout)),
        ("Bearer token", new Regex(@"Bearer\s+[A-Za-z0-9._\-]{10,}", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout)),
        ("password assignment", new Regex(@"password\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase, MatchTimeout)),
    ];

    /// <summary>
    /// Throws <see cref="AiGuardrailRejectedException"/> — never proceeds with a diminished
    /// request — for: an empty/whitespace-only description; a description longer than
    /// <see cref="MaxDescriptionLength"/> (token-economy spec §R1: reject, don't truncate, so
    /// the AI never silently acts on a cut-off request); or a description that, once secrets
    /// are redacted, has nothing left (the whole input WAS the secret). Otherwise returns the
    /// redacted description plus the list of checks that fired, for
    /// <see cref="AiPlanningResult{TOutput}.GuardrailChecks"/>.
    /// </summary>
    public static (string Description, IReadOnlyList<string> ChecksPerformed) Apply(string? rawDescription)
    {
        if (string.IsNullOrWhiteSpace(rawDescription))
        {
            throw new AiGuardrailRejectedException("Description is empty — nothing to send to the AI provider.");
        }

        if (rawDescription.Length > MaxDescriptionLength)
        {
            throw new AiGuardrailRejectedException(
                $"Description is {rawDescription.Length} characters, exceeding the {MaxDescriptionLength}-character " +
                "limit — shorten it and try again. It was not sent to the AI provider.");
        }

        var checks = new List<string>();
        var description = rawDescription;

        foreach (var (name, pattern) in SecretPatterns)
        {
            if (pattern.IsMatch(description))
            {
                description = pattern.Replace(description, "[REDACTED]");
                checks.Add($"redacted: {name}");
            }
        }

        if (checks.Count > 0 && string.IsNullOrWhiteSpace(description.Replace("[REDACTED]", "")))
        {
            throw new AiGuardrailRejectedException(
                "Description contained nothing but secret-like content, which was redacted — there is nothing " +
                "left to send to the AI provider.");
        }

        checks.Add("length and secret-pattern check ran");
        return (description, checks);
    }
}
