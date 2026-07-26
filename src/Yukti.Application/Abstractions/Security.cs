using Yukti.Domain.IdentityAccess;
using Yukti.Domain.SharedKernel;

namespace Yukti.Application.Abstractions;

/// <summary>
/// Raised when an authenticated identity lacks the required permission for
/// a command (FR-AUTHZ-02). Distinct from DomainException — this is an
/// authorization-layer concern, not a domain invariant violation, so it
/// deliberately does not live in Yukti.Domain (which has zero knowledge of
/// permissions at all — see Role's placement in IdentityAccess, not
/// SharedKernel).
/// </summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

/// <summary>Hashing is infrastructure's job (PBKDF2/bcrypt/etc.) — the domain and application layers only ever see the resulting opaque hash string (FR-AUTH-04's "never retrievable" guarantee starts here).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

/// <summary>Issues signed JWTs carrying a RoleVersion-bearing claim set (FR-AUTH-03) — see Role.cs for why authorization never actually trusts the claim for the permission decision itself.</summary>
public interface IJwtTokenService
{
    AccessToken IssueAccessToken(User user, IReadOnlyList<Role> roles);
}

/// <summary>Refresh tokens rotate on every use — Consume both validates and invalidates the presented token atomically, so replay of an already-used refresh token always fails (FR-AUTH-02).</summary>
public interface IRefreshTokenStore
{
    Task<string> Issue(UserId userId, CancellationToken ct);
    Task<UserId?> Consume(string refreshToken, CancellationToken ct);
}
