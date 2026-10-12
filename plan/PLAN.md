# Yukti v0.1 — 7-Day Plan (v2, existing .NET codebase)

Repo: `d:\Yukti\Yukti` · Owner: you · Orchestrator: Sutradhar · Window: Mon 12 Oct – Sun 18 Oct 2026

Goal: **prove and complete** v0.1 on the existing codebase. A box is ticked ONLY when its ✔ command passes.
Every task follows INSPECT first: done → add proof test · partial → complete · missing → build.
`<Tests>` = the repo's existing test projects; adapt filter names to what exists and record them in GAP-MATRIX.md.

---

## Day 0 — Sun 11 Oct (tonight, owner)
- [x] D0-T1 Pause the 6-hourly cloud routine that pushes to `main`
- [ ] D0-T2 Commit SUTRADHAR.md (root, also as CLAUDE.md) and plan/PLAN.md on a branch `chore/sutradhar-plan`; merge it
- [x] D0-T3 Enable branch protection on `main` (PR required)
  ✔ owner created a repository ruleset; confirmed live via `GET /repos/.../rules/branches/main` → `pull_request`, `deletion`, `non_fast_forward` all active; `GET /repos/.../branches/main` → `protected: true`

## Day 1 — Mon 12 Oct · Audit, stabilize, gap matrix
- [x] D1-T1 Baseline: build and tests from a clean clone; record failures in STANDUP
  ✔ `dotnet build -warnaserror` and `dotnet test` results recorded (pass OR listed failures) — recorded: strict build FAILED (5× CA1416, DesktopUiModule), plain build/tests/frontend build all green
- [x] D1-T2 Make the baseline green (fix broken build/tests only; no features)
  ✔ `dotnet build -warnaserror && dotnet test` → all green; `npm run build` green
- [ ] D1-T3 `plan/GAP-MATRIX.md`: for each acceptance item P0-1…P0-8 + the frozen features (Vault, RLS, RBAC, audit, marketplace UI): status Exists / Partial / Missing, evidence (file paths, tests), and naming differences vs the blueprint vocabulary
  ✔ file exists; every row has evidence; owner approves
- [ ] D1-T4 CI baseline in GitHub Actions: build, test, `dotnet format --verify-no-changes`, frontend lint/test/build on every PR
  ✔ workflow green on a PR
- [ ] D1-T5 Reconcile Days 2–6 below against GAP-MATRIX: mark tasks already proven as `ALREADY DONE (evidence)`; add missing work under DISCOVERED
  ✔ owner approves the reconciled plan
- **Exit check:** green baseline + CI + approved GAP-MATRIX. No feature code merged today.

## Day 2 — Tue 13 Oct · Module contract, flow engine, CLI (P0-1, P0-2, P0-3, P0-5)
- [ ] D2-T1 `IAutomationModule` contract + registry; `GET /api/v1/modules/actions` returns all actions with schemas and non-empty descriptions; test registers a synthetic module with no other code change
  ✔ `dotnet test --filter "FullyQualifiedName~ModuleRegistry"`
- [ ] D2-T2 Variable interpolation `{{vars.a.b}}`, `saveAs`, publish-time check that referenced vars are produced earlier
  ✔ `dotnet test --filter "FullyQualifiedName~Variable"`
- [ ] D2-T3 Flow engine: ordered steps, fail-fast, `ContinueOnFailure`, `when` → Skipped, retry policy, flaky classification with retry history
  ✔ `dotnet test --filter "FullyQualifiedName~FlowEngine"`
- [ ] D2-T4 Per-step durable commit of StepResult; test kills execution after step 2 and finds steps 1–2 persisted
  ✔ `dotnet test --filter "FullyQualifiedName~Durability"` (Testcontainers Postgres)
- [ ] D2-T5 Publish validation: unregistered action → 422 RFC 7807 naming the step; Published flows immutable (edit = new version)
  ✔ `dotnet test --filter "FullyQualifiedName~Publish"`
- [ ] D2-T6 CLI (`yukti run <flow.json> --junit out.xml`), exit 0/1, runs without the API server
  ✔ `dotnet run --project <Cli> -- run demo/api_chain.json --junit out.xml` → exit 0, valid XML
- **Exit check:** chained 3-step API flow runs from the CLI with JUnit output.

## Day 3 — Wed 14 Oct · API, Web, Logs modules + UC-1 (P0-4)
- [ ] D3-T1 API module: request (GET/POST/PUT/PATCH/DELETE, headers, body, timeout) + status / path equals / contains / exists assertions
  ✔ `dotnet test --filter "FullyQualifiedName~ApiModule"`
- [ ] D3-T2 Web module on Microsoft.Playwright: navigate, click, fill, select, waitFor, assertText, assertVisible, screenshot; typed SelectorNotFound
  ✔ `dotnet test --filter "FullyQualifiedName~WebModule"`
- [ ] D3-T3 Logs module: checkRules (pattern, severity, maxOccurrences, optional time window)
  ✔ `dotnet test --filter "FullyQualifiedName~LogsModule"`
- [ ] D3-T4 Unified report: one status model across modules, JSON + JUnit
  ✔ `dotnet test --filter "FullyQualifiedName~Report"`
- [ ] D3-T5 `demo/uc1_flow.json`: Web login (local fixture page or the-internet.herokuapp.com) → saveAs → API call using it → Logs check on `demo/fixtures/app.log`
  ✔ CLI run of uc1_flow.json → Passed
- [ ] D3-T6 Flake run UC-1 ×50, rate recorded in STANDUP
  ✔ flake rate < 2%
- **Exit check:** UC-1 passes via CLI with one unified report.

## Day 4 — Thu 15 Oct · API testing UI + live runs (P0-6)
- [ ] D4-T1 Generated TS types from the backend OpenAPI document wired into the frontend build
  ✔ `npm run generate:api && npm run typecheck`
- [ ] D4-T2 Flow list + editor using `SchemaDrivenParamEditor`; ESLint rule bans module-kind branching
  ✔ `npm test -- SchemaDrivenParamEditor` (includes synthetic-module render test)
- [ ] D4-T3 API request builder + response viewer (status, timing, JSON tree) — reuse existing components
  ✔ `npm test -- RequestBuilder`
- [ ] D4-T4 Trigger run + live progress via SignalR; refetch run state on reconnect
  ✔ `npm test -- LiveRun` + backend hub test
- [ ] D4-T5 Marketplace UI and new editor coexist; marketplace-installed module actions render in the editor
  ✔ `npm test -- ModulePicker`
- [ ] D4-T6 Variable autocomplete from earlier steps' saveAs
  ✔ `npm test -- autocomplete`
- **Exit check:** author → publish → run an API flow fully in the browser, no hand-typed JSON.

## Day 5 — Fri 16 Oct · AI layer — Sutradhar runtime (P0-7, P0-8)
- [ ] D5-T1 `IAiProvider` (Anthropic + Stub), 15s timeout, typed `AiUnavailableException`; API key resolved from Vault
  ✔ `dotnet test --filter "FullyQualifiedName~AiProvider"`
- [ ] D5-T2 Planning pipeline: guardrail pre-check (PII redaction, size limit) → context (module registry) → versioned prompt → call → JSON-schema validation → cost record
  ✔ `dotnet test --filter "FullyQualifiedName~Planning"`
- [ ] D5-T3 Analyst prompt v1 → ParsedIntent (with ambiguities → clarifying question)
  ✔ `dotnet test --filter "FullyQualifiedName~Analyst"`
- [ ] D5-T4 Coder prompt v1 → Draft Flow with registered actions only, one retry with error feedback; `POST /api/v1/flows/generate`
  ✔ `dotnet test --filter "FullyQualifiedName~Coder"`
- [ ] D5-T5 UI generation panel, AI-assisted badge + confidence, clarifying-question flow
  ✔ `npm test -- AiGenerationPanel`
- [ ] D5-T6 AI-off CI job: AI disabled, no outbound LLM call, all non-AI tests green
  ✔ CI job `ai-off` green
- [ ] D5-T7 Eval set: 10 prompts + scorer
  ✔ ≥ 8/10 structurally valid drafts
- **Exit check:** English → Draft → edit → publish → run; AI-off green.

## Day 6 — Sat 17 Oct · Verify the enterprise features + hardening
- [ ] D6-T1 RLS proof: tenant A cannot read/write tenant B flows, runs, results (API and direct DB with tenant session)
  ✔ `dotnet test --filter "FullyQualifiedName~TenantIsolation"`
- [ ] D6-T2 RBAC proof: for every write command, an authorized-success and unauthorized-403 test (Vol 1 §25.8)
  ✔ `dotnet test --filter "FullyQualifiedName~Authorization"`
- [ ] D6-T3 Vault proof: secrets resolved from Vault at runtime, never logged; credential rotation audited
  ✔ `dotnet test --filter "FullyQualifiedName~Vault"`
- [ ] D6-T4 Audit proof: publish, run trigger, role change each produce one immutable audit entry
  ✔ `dotnet test --filter "FullyQualifiedName~Audit"`
- [ ] D6-T5 Run history + report pages (pass/fail/skip/flaky, screenshots, errors)
  ✔ `npm test -- RunHistory Report`
- [ ] D6-T6 CI gates a PR on `yukti run demo/uc1_flow.json`; `docker compose up` works from clean clone
  ✔ workflow green; `/health` returns 200
- **Exit check:** security features proven by tests, not assumed; CI gates on a Yukti flow.

## Day 7 — Sun 18 Oct · Buffer, self-test, release
- [ ] D7-T1 Close all BLOCKED / carried tasks
- [ ] D7-T2 Yukti tests itself: Web flow that authors and runs a flow in the Yukti UI
  ✔ CLI run of `demo/self_test.json` → Passed
- [ ] D7-T3 Time-to-first-flow trial with a teammate (< 30 min), result in STANDUP
- [ ] D7-T4 README, quickstart, demo script; ADR-0001 (stay on .NET) committed
- [ ] D7-T5 Decide the cloud routine's future (retire, or resume in branch+PR mode per SUTRADHAR.md §6)
- [ ] D7-T6 P1 stretch: Reviewer failure triage
- [ ] D7-T7 Tag `v0.1.0`; retro in STANDUP
- **Exit check:** every P0 box ticked or ALREADY DONE with evidence; `v0.1.0` tagged.

---

## DISCOVERED
<!-- Sutradhar adds new tasks here only -->
