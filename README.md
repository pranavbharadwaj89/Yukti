# Yukti — AI-powered unified test-automation platform

Yukti authors, publishes, and runs automated test **flows** against web UIs, HTTP APIs, log
files, mobile apps, and desktop UIs through one module contract, one engine, one unified
report format — with optional AI assistance to draft a flow from a plain-English description.
Everything here is real, running code: a .NET 8 backend, a PostgreSQL database with
row-level-security multi-tenancy, a React frontend, a standalone CLI, and a CI pipeline that
exercises all of it, including the full v0.1 acceptance suite, on every PR.

## Quickstart (Docker)

The fastest way to see it run — brings up Postgres, Redis, the API, and the background worker:

```bash
docker compose up
```

The API applies its own EF Core migrations and seeds a bootstrap admin on first start — no
separate setup step. Once `docker compose ps` shows `api` healthy:

- API: `http://localhost:8080` (Swagger UI at `/swagger`, health check at `/health`)
- Bootstrap login: `admin@yukti.local` / `ChangeMe123!` (change this before any real deployment)

Then run the frontend separately (not in `docker-compose.yml` — it's meant to run with
hot-reload during development):

```bash
cd apps/yukti-gui
npm install
npm run dev     # http://localhost:5173, talking to the API on :8080
```

## Quickstart (local, no Docker)

Needs a local PostgreSQL 16+ and Redis reachable from `ConnectionStrings:Yukti` /
`ConnectionStrings:Redis` (set via `dotnet user-secrets` in Development, or environment
variables / a real secret store otherwise — never hardcoded):

```bash
dotnet user-secrets set "ConnectionStrings:Yukti" "Host=localhost;Port=5432;Database=yukti;Username=postgres;Password=..." --project src/Yukti.Api

dotnet build
dotnet run --project src/Yukti.Api      # applies migrations + seeds on startup, serves :5000

cd apps/yukti-gui && npm install && npm run dev   # :5173
```

## The CLI — no server, no database

`Yukti.Cli` runs a flow file directly, in-process, against the real `FlowEngine` — no API
server and no Postgres needed for flows that don't target the live app themselves:

```bash
dotnet run --project src/Yukti.Cli -- run demo/api_chain.json --junit out.xml --verbose
```

`run <flow-file> [--junit <path>] [--json <path>] [--var key=value] [--ai off|on] [--timeout <seconds>] [--verbose]`

## Demo flows

`demo/` has a few ready-to-run flows:

| File | What it proves | Needs running |
|---|---|---|
| `api_chain.json` | Two chained HTTP calls, the second built from the first's `saveAs` output. | Nothing — self-contained. |
| `uc1_flow.json` | A real cross-module flow: Web login → API confirms the order → Logs module checks no `ERROR` was logged for it. | `dotnet run --project demo/fixtures/FixtureApp`. |
| `self_test.json` | **Yukti testing itself.** Drives the real `apps/yukti-gui` frontend through login → create a flow → add a step → publish → run → confirm the run passed — the same sequence a human would click through, done by Yukti's own Web module. | The real API (`:5000`) and the real frontend dev server (`:5173`). |

```bash
dotnet run --project src/Yukti.Cli -- run demo/self_test.json --verbose
```

## Project structure

```
Yukti.sln
src/
  Yukti.Domain/                  Aggregates, entities, value objects, domain events.
                                  Zero external dependencies, by design.
  Yukti.Contracts/                IAutomationModule — the plugin interface every module
                                  (built-in or third-party) implements.
  Yukti.Application/              Repository interfaces, Unit of Work, command handlers.
  Yukti.Orchestration/            FlowEngine — the execution loop, incremental per-step
                                  commits for crash durability, retry/flaky handling.
  Yukti.Infrastructure/           Real EF Core + PostgreSQL repositories, RLS, Vault
                                  credential resolution, OpenTelemetry.
  Yukti.Infrastructure.InMemory/  The five built-in modules: Api, Logs, Web (Playwright),
                                  Mobile (Appium), DesktopUi (Windows-only).
  Yukti.Api/                      ASP.NET Core REST API + SignalR hub (live run progress).
  Yukti.Worker/                   Scheduler, outbox relay, trend-aggregation batch job —
                                  split out of Yukti.Api so HTTP serving never blocks on them.
  Yukti.Cli/                      Standalone flow runner — no server, no database.
  Yukti.Host/                     Original console smoke-test wiring (superseded by the
                                  real API/CLI above; kept for the original end-to-end demo).
apps/
  yukti-gui/                      React 19 + Vite + TypeScript frontend: flow authoring,
                                  API Studio, live run console, AI generation panel, audit,
                                  reporting, module marketplace.
tests/                            xUnit test projects, one per src/ project with tests, plus
                                  CLI process-level integration tests.
demo/                             Ready-to-run flow files (see above) and the FixtureApp
                                  target `uc1_flow.json` needs.
plan/                             PLAN.md (the week's task list), STANDUP.md (session log),
                                  GAP-MATRIX.md (Day 1 audit), adr/ (architecture decisions).
```

## What's real, what's intentionally out of scope for v0.1

Real and tested: the module contract and five built-in modules, the flow engine (variables,
retry/flaky, fail-fast/`ContinueOnFailure`/`when`, crash durability), publish-time validation,
a unified report (JUnit + JSON, CLI and API agree on shape), the CLI, the API-testing UI and
live SignalR run console, AI flow generation (Gemini-backed, with a documented, tested
AI-off path that registers zero AI-provider client), JWT auth + RBAC, PostgreSQL row-level-
security multi-tenancy, an audit trail, and a CI pipeline (`build-and-test`, `frontend`,
`ai-off`, `e2e`, `api-types-drift`, `docker-compose-smoke`, `uc1-flow-gate`) that proves all of
it on every PR, not just locally.

Deliberately out of scope this release — see `plan/PLAN.md` §3 "Not this week": new modules
beyond the five built-in ones, marketplace publishing/certification, community-module
sandboxing, selector self-healing, a RAG/vector store, Kubernetes/Helm production rollout, and
new OpenTelemetry instrumentation beyond what already exists.

## Architecture decisions

See `plan/adr/` — notably `0001-stay-on-dotnet.md` (why this is .NET/React, not a from-scratch
rewrite) and `0004-ai-vendor-gemini.md` (why Gemini, not OpenAI, is the real AI provider).

## Where this fits in the full architecture

This implements the v0.1 acceptance criteria from the full six-volume architecture
specification this project was designed against: Volume 1's module contract, flow engine, and
repository/Unit-of-Work patterns; Volume 1 Part IV's auth/multi-tenancy/audit; Volume 1 Part V's
REST API and SignalR layer; Volume 3's AI capability engine (flow generation); and Volume 2's
frontend. Not yet built: the module marketplace's publishing/certification backend, community-
tier module sandboxing, and selector self-healing — tracked as v0.2+ scope.
