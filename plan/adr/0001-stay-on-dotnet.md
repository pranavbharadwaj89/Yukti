# ADR-0001: Stay on the existing .NET/C# + React codebase

Status: Accepted
Date: 2026-10-12
Owner approval: explicit — the owner's "v2" operating manual (`CLAUDE.md`) directly supersedes
the prior plan on this point.

## Context

The original build plan ("v1") assumed a greenfield rewrite: a brand-new Python/FastAPI
project, started from `mkdir yukti`, with an explicit non-goals list that included "Vault /
multi-tenancy / RBAC / marketplace / .NET port." That plan was written without having looked
at the actual repository.

The real repository at `d:\Yukti\Yukti` already existed, and already had a working,
substantial .NET 8/C# backend (`Yukti.Domain` through `Yukti.Host`) and a React/Vite/
TypeScript frontend — not a sketch, but real aggregates, a real flow engine with incremental
per-step commits for crash durability, two real built-in modules, and (per `plan/GAP-MATRIX.md`,
Day 1's audit) real, already-exercised Vault integration, PostgreSQL row-level-security
multi-tenancy, RBAC, and an audit trail. Starting over in Python would have thrown all of
that away to re-build the same thing from zero, in a different language, for no functional
gain — and would have directly contradicted several of v1's own "frozen, don't touch" items,
which only make sense if the .NET codebase they're defined against keeps existing.

## Decision

Yukti stays on .NET 8/C# (ASP.NET Core, EF Core, PostgreSQL, Redis, SignalR, Vault) for the
backend and React/Vite/TypeScript for the frontend — the stack the existing repository was
already built on. There is no new repository and no Python rewrite. `CLAUDE.md` v2 ("SUTRADHAR
— Operating Manual") is the operating manual going forward; v1 and any older chat context
about Python, FastAPI, `mkdir yukti`, or the v1 non-goals list is void.

The week's work (`plan/PLAN.md`) is scoped as **complete, verify, and harden** the existing
codebase against the v0.1 acceptance criteria — not rewrite it.

## Why

- The existing code was real and substantially further along than a Python rewrite could reach
  in the same time: a working domain model, flow engine, two built-in modules, and three
  enterprise features (Vault, RLS, RBAC) with evidence, confirmed by Day 1's own audit before
  this decision was made.
- v1's own non-goals list ("Vault / multi-tenancy / RBAC / marketplace / .NET port") only makes
  sense as "don't build these *yet*" against a codebase that already has them stubbed in — it
  was written in ignorance of the real repo, not as a deliberate scope cut against it.
- Rewriting in Python would mean re-proving every one of those already-working features from
  scratch, with less of the week left to do it in, for a stack with no demonstrated advantage
  over the one already running.

## Consequences

- Every reference to Python/FastAPI/`mkdir yukti` in prior chat context or planning documents
  is void; `CLAUDE.md` v2 is the one source of truth for scope and process.
- Project names stay whatever the existing solution already uses (`Yukti.*`), not the
  architecture blueprint's `Nexus.*` naming — matching `CLAUDE.md` §4's explicit instruction not
  to rename projects to match the blueprint.
- All subsequent work (Days 1–7) is phrased as "complete, verify, harden" against existing code,
  never "build from scratch" — reflected throughout `plan/PLAN.md`'s task list and the day-by-day
  `plan/STANDUP.md` log.
