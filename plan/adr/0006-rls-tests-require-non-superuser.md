# ADR-0006: RLS tests fail loudly when run as a superuser, instead of passing meaninglessly

Status: Accepted
Date: 2026-10-25
Owner directive: explicit — "a false pass is worse than a red test" (Day 6 close-out, part B).

## Context

`TenantIsolationTests.GetConnectionString()` falls back from `ConnectionStrings:YuktiRuntime` to
`ConnectionStrings:Yukti` when the former isn't configured:

```csharp
return config.GetConnectionString("YuktiRuntime") ?? config.GetConnectionString("Yukti");
```

`ConnectionStrings:Yukti` authenticates as `postgres`, the database's own bootstrap superuser.
PostgreSQL superusers **unconditionally bypass row-level security**, regardless of policy
configuration. This is not a bug in the RLS policies — `relrowsecurity`/`relforcerowsecurity` are
both `true` on every affected table, and the policies themselves are correct. It is a property of
the role the connection authenticates as.

**`FORCE ROW LEVEL SECURITY` does not close this gap.** `FORCE` changes which connections a
policy applies to *by ownership* — without it, the table *owner's own* connection is exempt from
RLS even as a non-superuser; `FORCE` removes that owner-specific exemption. It says nothing about
superusers, who are exempt from RLS entirely, at a layer above ownership, and no table-level
setting can change that. These are two different exemption mechanisms with no overlap: `FORCE`
closes the owner hole; nothing at the table level closes the superuser hole.

The practical consequence found during D6-T6/D7-T1: on a machine where only
`ConnectionStrings:Yukti` is configured (the common case — `YuktiRuntime` requires a second,
separately-provisioned role and credential), every `TenantIsolationTests` test connects as the
superuser. Before this ADR's fix, that meant:

- Three of four tests failed with assertion errors that read like genuine RLS bugs (a cross-tenant
  row was visible; an illegal write was not rejected) — correct output for the wrong reason, easy
  to misdiagnose as "RLS is broken" when it was actually "this connection cannot observe RLS at
  all."
- **One test passed** — not because tenant isolation held, but because the superuser connection
  happened to produce the expected result by coincidence of that test's specific assertion shape.
  A passing security test that isn't actually exercising the protection is not evidence; it's
  decoration (the same standard the Day 6 spec's own mutation-check rule exists to enforce). This
  is strictly worse than the three honest failures: a false pass hides the gap instead of
  surfacing it.

## Decision

`TenantIsolationTests.OpenSessionAs` — the one method every test in the file already routes
through to establish its database session — now asserts
`SELECT current_setting('is_superuser')` is `'off'` immediately after opening the connection, before any
tenant context is established or any query runs. If it is `'on'`, the fixture throws
immediately:

> "RLS tests cannot run as a superuser — RLS is bypassed by design and these tests would pass
> meaninglessly. Point ConnectionStrings:YuktiRuntime at a non-superuser role."

This makes the entire fixture fail loudly and uniformly (all four tests, same message) rather
than producing a mix of misleading failures and a meaningless pass. The fix for a developer
hitting this is exactly what the message says: configure `ConnectionStrings:YuktiRuntime` to the
non-superuser `yukti_app` role (created by `AddYuktiAppRuntimeRole` for exactly this purpose), not
to weaken or route around the check.

## Consequences

- Every future run of this fixture either proves RLS for real or fails with an unambiguous,
  actionable message — never a silent false pass.
- This guard is local to `TenantIsolationTests`; it does not change `AddRowLevelSecurity`,
  `AddYuktiAppRuntimeRole`, or any production code. Reading security code and adding a proof
  against it is permitted under the Day 6 spec's autonomy rules; this ADR records the latter.
- The same class of trap — "the fallback connection happens to be powerful enough to make the
  test meaningless" — is worth checking for in any future live-database test that depends on a
  specific role's restrictions, not just this one file.
- Converting D6-T1 (and D6-T4, which shares the same credential dependency) to Testcontainers —
  provisioning a throwaway Postgres, creating `yukti_app` itself, and connecting as that role by
  construction — would remove this entire class of misconfiguration rather than just detecting it.
  Tracked as a DISCOVERED item, v0.2, not built now.
