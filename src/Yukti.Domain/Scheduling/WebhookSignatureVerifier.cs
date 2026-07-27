using System.Security.Cryptography;
using System.Text;

namespace Yukti.Domain.Scheduling;

/// <summary>FR-SCHED-04: HMAC-SHA256 signature verification for webhook
/// triggers that carry a shared secret — a trigger with no configured
/// secret accepts any request (unsigned webhooks remain valid; the path's
/// own high entropy is the security boundary in that case).</summary>
public static class WebhookSignatureVerifier
{
    public static bool Verify(string? sharedSecret, string requestBody, string? providedSignatureHex)
    {
        if (sharedSecret is null)
            return true; // no secret configured — path unguessability is the only guard

        if (string.IsNullOrEmpty(providedSignatureHex))
            return false;

        var expected = Convert.ToHexString(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(sharedSecret), Encoding.UTF8.GetBytes(requestBody)));

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(providedSignatureHex.ToLowerInvariant()));
    }
}
