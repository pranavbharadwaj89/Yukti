using Yukti.Domain.IdentityAccess;

namespace Yukti.Application.Abstractions;

/// <summary>
/// FR-TENANT-01/FR-DB-02 fallout, fixed here: login and self-registration's
/// duplicate-email check both need to find a user "by email, before any
/// tenant context exists" — that's the whole point, tenant context comes
/// FROM this lookup's result. EfUserRepository.GetByEmail's own comment
/// already documents this as deliberately unfiltered at the C# query
/// level — but users' RLS policy is strict (no "IS NULL OR" permissive
/// branch like roles/module_registrations have for genuinely global
/// rows), so a real, non-owner, RLS-enforced connection with no tenant
/// context set returns zero rows regardless of what the C# WHERE clause
/// asks for. This interface is the one, narrow, explicitly-named
/// exception: a lookup that must see across tenants because tenant isn't
/// known yet, backed by a BYPASSRLS connection — never used for anything
/// past this single pre-authentication step.
/// </summary>
public interface IAuthBypassUserLookup
{
    Task<User?> GetByEmail(string email, CancellationToken ct);
}
