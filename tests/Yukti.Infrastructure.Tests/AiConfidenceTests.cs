using Xunit;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>D5-T6/§R8: confidence is a defined, documented formula, never a literal, and never asked of the model — proven here with no <see cref="IAiProvider"/> involved at all, since the formula is pure math over an already-known attempt count.</summary>
public sealed class AiConfidenceTests
{
    [Fact]
    public void A_first_attempt_success_gets_FullConfidence()
    {
        Assert.Equal(AiConfidence.FullConfidence, AiConfidence.FromAttempt(1));
    }

    [Fact]
    public void Each_retry_reduces_confidence_by_the_documented_penalty()
    {
        Assert.Equal(AiConfidence.FullConfidence - AiConfidence.RetryPenaltyPerAttempt, AiConfidence.FromAttempt(2));
    }

    [Fact]
    public void Confidence_never_drops_below_MinConfidence_no_matter_how_many_attempts()
    {
        Assert.Equal(AiConfidence.MinConfidence, AiConfidence.FromAttempt(100));
    }

    [Fact]
    public void An_attempt_below_1_is_rejected_its_1_based()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AiConfidence.FromAttempt(0));
    }

    [Fact]
    public void FullConfidence_is_deliberately_short_of_1_AI_output_is_always_a_Draft_never_a_certainty()
    {
        Assert.True(AiConfidence.FullConfidence < 1.0);
    }
}
