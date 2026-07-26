using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Yukti.Application.Abstractions;
using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Infrastructure.InMemory;

/// <summary>
/// Real PBKDF2-HMAC-SHA256, 100,000 iterations, 128-bit random salt —
/// genuine password hashing, not a demo stand-in (unlike the "InMemory"
/// naming elsewhere in this project, which always means non-durable
/// storage, never fake cryptography). Format: "iterations.saltB64.hashB64".
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;

        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>
/// Real HMAC-SHA256-signed JWTs (FR-AUTH-02: 15-minute expiry; FR-AUTH-03:
/// RoleVersion claim, never embedded permissions). The signing key is
/// generated once at process startup — a documented, temporary
/// secret-management shortcut identical in spirit to
/// InMemoryCredentialResolver: real deployments need a persisted/rotatable
/// key (Vault or similar), or every restart invalidates every outstanding
/// token. Swapping that in requires no change to this class's public
/// surface, only how the key bytes are sourced in the composition root.
/// </summary>
public sealed class JwtTokenService : IJwtTokenService
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(byte[] signingKey) =>
        _signingCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey), SecurityAlgorithms.HmacSha256);

    public AccessToken IssueAccessToken(User user, IReadOnlyList<Role> roles)
    {
        var expiresAt = DateTimeOffset.UtcNow.Add(AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.Value.ToString()),
            new("tenant", user.TenantId.Value.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
        };
        // One claim per assigned role: "roleId:version" — carries FR-AUTH-03's
        // RoleVersion, even though PermissionChecker never trusts it for the
        // authorization decision itself (see Role.cs).
        claims.AddRange(roles.Select(r => new Claim("role", $"{r.Id.Value}:{r.Version}")));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new AccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

/// <summary>
/// Rotation with reuse detection (FR-AUTH-02): Consume marks the presented
/// token used and returns its owner exactly once; any later Consume call
/// with the same token — a legitimate double-submit race or a stolen/replayed
/// token — fails. Non-durable (in-memory) — a real deployment persists this
/// the same way FlowRun state itself will once Yukti.Infrastructure lands.
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private sealed record Entry(UserId UserId, bool Used);
    private readonly ConcurrentDictionary<string, Entry> _tokens = new();

    public Task<string> Issue(UserId userId, CancellationToken ct)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _tokens[Hash(token)] = new Entry(userId, Used: false);
        return Task.FromResult(token);
    }

    public Task<UserId?> Consume(string refreshToken, CancellationToken ct)
    {
        var key = Hash(refreshToken);
        if (!_tokens.TryGetValue(key, out var entry) || entry.Used)
            return Task.FromResult<UserId?>(null);

        _tokens[key] = entry with { Used = true };
        return Task.FromResult<UserId?>(entry.UserId);
    }

    private static string Hash(string token) =>
        Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
