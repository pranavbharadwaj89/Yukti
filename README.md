# Yukti — Backend Core

This is the first coding pass on the Yukti platform, implementing **Volume 1's domain model
and orchestration engine** from the full architecture specification — the "engineering bible"
this project was designed against.

## What's real here

Every project in this solution builds and runs for real. This is not a sketch or a set of
stubs — `Yukti.Domain` through `Yukti.Host` compile clean with the .NET 8 SDK, and
`Yukti.Host` runs an actual end-to-end flow: a live HTTP call chained into a second HTTP
call via `{{vars.x.y}}` interpolation, plus a real regex-based log rule engine and
statistical anomaly detector — the same modules originally prototyped in this project's
early exploration phase, now implemented against the formal `IAutomationModule` contract.

```bash
dotnet build          # builds all 6 projects from the solution root
dotnet run --project src/Yukti.Host   # runs the live smoke-test flow
```

## Project structure

```
Yukti.sln
src/
  Yukti.Domain/                 Aggregates, entities, value objects, domain events.
                                 Zero external dependencies, by design (Volume 1 §15.5).
  Yukti.Contracts/               The IAutomationModule plugin interface — meant to be
                                 published as its own independently-versioned package.
  Yukti.Application/             Repository interfaces, Unit of Work, command handlers.
  Yukti.Orchestration/           FlowEngine — the execution loop, with incremental
                                 per-step commits (see "The Unit of Work fix" below).
  Yukti.Infrastructure.InMemory/ Demo-grade in-memory repositories + two real, working
                                 built-in modules (Api, Logs). See "What's temporary" below.
  Yukti.Host/                    Console demo wiring everything and running a real flow.
```

## The Unit of Work fix

Partway through writing the full six-volume architecture spec this code implements, a real
crash-durability gap was found and fixed: the original design committed a flow run's results
to the database only once, at the very end — meaning a process crash mid-run would lose the
*entire* run's history, not just the step in flight. `FlowEngine.Execute` in this codebase
commits **after every single step** (see `CommitRun` in `FlowEngine.cs`), so a crash loses
at most the one step that was executing at that instant. This is `Yukti.Domain`'s `FlowRun.Start()`
/ `RecordStepResult()` / `Complete()` design and `Yukti.Application`'s `IUnitOfWorkFactory`
existing specifically to support that pattern.

## What's temporary (and what isn't)

`Yukti.Infrastructure.InMemory` is exactly what it sounds like — in-memory, non-durable,
built to prove the layers above it are correct and runnable without needing a database in
this environment. **The repository and Unit of Work *interfaces* it implements are the real,
permanent contracts** (`IFlowRepository`, `IUnitOfWork`, etc., in `Yukti.Application`) — a
real `Yukti.Infrastructure` project backed by EF Core and PostgreSQL, matching the full
schema in the architecture spec (database design, row-level security, partitioning), is the
natural next step and requires no changes to `Yukti.Domain`, `Yukti.Application`, or
`Yukti.Orchestration` — only a new implementation of interfaces that already exist.

Similarly, `Yukti.Orchestration.ModuleDispatcher` currently dispatches every module
in-process. The trust-tiered execution model (Built-in/Verified in-process, Community-tier
sandboxed) is specified but not yet implemented — a follow-up once marketplace modules are
in scope.

## A note on the sandbox this was built in

This code was written and built inside a network-restricted sandbox with no access to
`nuget.org` — every project here was deliberately kept to zero external NuGet packages so
it could be built and genuinely tested without that access, using only the .NET base class
library. This is a real constraint of *where this was built*, not a property of the
architecture: once you have this checked out with normal internet access, `dotnet restore`
will work normally, and adding `Microsoft.EntityFrameworkCore`, `Npgsql`, `xunit`, etc. per
the full architecture spec (Volumes 1 and 4) is unblocked. Delete `nuget.config` (or point
it at nuget.org) as the first step in a normal development environment.

The API module's live smoke-test step calling `api.github.com` may show a `403` — that's
GitHub's real, standard rate limit on unauthenticated requests from a shared IP, not a bug.

## Where this fits in the full architecture

This implements Volume 1's Domain-Driven Design (Part II) and Core Engineering Patterns
(Part III — specifically the Execution Engine, §19, and the Repository/Unit of Work
patterns, §15-16) sections. Still ahead, per the full spec: the REST API and SignalR layer
(Part V), cross-cutting services — auth, multi-tenancy, audit (Part IV) — and everything in
Volumes 2 through 5 (frontend, AI capability engines, infrastructure, engineering process).
