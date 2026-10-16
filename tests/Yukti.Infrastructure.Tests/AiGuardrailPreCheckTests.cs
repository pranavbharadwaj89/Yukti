using Xunit;
using Yukti.Application.Ai;

namespace Yukti.Infrastructure.Tests;

/// <summary>D5-T3: the input-side guardrail, in isolation — pure logic, no provider, no I/O.</summary>
public sealed class AiGuardrailPreCheckTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_an_empty_or_whitespace_only_description(string? description)
    {
        Assert.Throws<AiGuardrailRejectedException>(() => AiGuardrailPreCheck.Apply(description));
    }

    [Fact]
    public void Redacts_an_sk_prefixed_secret_key()
    {
        var (result, checks) = AiGuardrailPreCheck.Apply("use key sk-abcdefghijklmnopqrstuvwxyz123456 to log in");
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz123456", result);
        Assert.Contains("[REDACTED]", result);
        Assert.Contains(checks, c => c.Contains("sk-prefixed secret key"));
    }

    [Fact]
    public void Redacts_a_bearer_token()
    {
        var (result, checks) = AiGuardrailPreCheck.Apply("call the API with Bearer abc123def456ghi789 in the header");
        Assert.DoesNotContain("abc123def456ghi789", result);
        Assert.Contains(checks, c => c.Contains("Bearer token"));
    }

    [Fact]
    public void Redacts_a_password_assignment()
    {
        var (result, checks) = AiGuardrailPreCheck.Apply("login with password=SuperSecret123");
        Assert.DoesNotContain("SuperSecret123", result);
        Assert.Contains(checks, c => c.Contains("password"));
    }

    [Fact]
    public void A_description_with_no_secrets_passes_through_unredacted()
    {
        var (result, checks) = AiGuardrailPreCheck.Apply("check that the login page works");
        Assert.Equal("check that the login page works", result);
        Assert.DoesNotContain(checks, c => c.StartsWith("redacted:"));
    }

    [Fact]
    public void Rejects_a_description_longer_than_4000_characters_instead_of_truncating_it()
    {
        // Token-economy spec §R1/R4: a prompt over the cap fails fast, it never silently
        // truncates — the AI must never act on a cut-off version of the user's own request.
        var longDescription = new string('a', 5000);
        Assert.Throws<AiGuardrailRejectedException>(() => AiGuardrailPreCheck.Apply(longDescription));
    }

    [Fact]
    public void Rejects_a_description_that_is_nothing_but_a_redacted_secret()
    {
        // Token-economy spec §R1: "input that is only redacted secrets" is its own zero-cost
        // reject — there's nothing meaningful left to send once the secret is stripped out.
        Assert.Throws<AiGuardrailRejectedException>(() =>
            AiGuardrailPreCheck.Apply("sk-abcdefghijklmnopqrstuvwxyz123456"));
    }

    [Fact]
    public void A_description_that_is_mostly_text_with_one_embedded_secret_is_redacted_not_rejected()
    {
        var (result, checks) = AiGuardrailPreCheck.Apply("log in using sk-abcdefghijklmnopqrstuvwxyz123456 and check the orders page");
        Assert.Contains("[REDACTED]", result);
        Assert.Contains("check the orders page", result);
        Assert.Contains(checks, c => c.Contains("sk-prefixed secret key"));
    }
}
