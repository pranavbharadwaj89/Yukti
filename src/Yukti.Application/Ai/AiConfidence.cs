namespace Yukti.Application.Ai;

/// <summary>
/// D5-T6: confidence is a defined, documented formula — never a literal, and never asked of the
/// model (token-economy spec §R8: that would cost tokens, and models are notoriously miscalibrated
/// about their own correctness anyway). Instead it's a deterministic function of how many attempts
/// <see cref="IAiPlanningEngine.PlanAsync{TOutput}"/> needed: succeeding on the first attempt means
/// nothing contradicted the request; needing a retry means the first attempt was objectively wrong
/// about something concrete (a hallucinated module, a missing param, an unresolved variable) — real
/// evidence, not a vibe, so it costs real confidence. <see cref="FullConfidence"/> is deliberately
/// short of 1.0: AI output here is always a Draft a human reviews, never a certainty (CLAUDE.md
/// principle 3), so nothing from this pipeline claims full certainty even on a clean first pass.
/// Pure math over an already-known integer — computing it makes zero provider calls.
/// </summary>
public static class AiConfidence
{
    public const double FullConfidence = 0.9;
    public const double RetryPenaltyPerAttempt = 0.3;
    public const double MinConfidence = 0.3;

    /// <param name="attempt">1-based — the attempt number that actually succeeded.</param>
    public static double FromAttempt(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Attempt is 1-based; it must be at least 1.");
        }

        var confidence = FullConfidence - (attempt - 1) * RetryPenaltyPerAttempt;
        return Math.Clamp(confidence, MinConfidence, FullConfidence);
    }
}
