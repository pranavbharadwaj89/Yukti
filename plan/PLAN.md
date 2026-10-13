# Yukti v0.1 — 7-Day Plan (v2, existing .NET codebase)

Repo: `d:\Yukti\Yukti` · Owner: you · Orchestrator: Sutradhar · Window: Mon 12 Oct – Sun 18 Oct 2026

Goal: **prove and complete** v0.1 on the existing codebase. A box is ticked ONLY when its ✔ command passes.
Every task follows INSPECT first: done → add proof test · partial → complete · missing → build.
`<Tests>` = the repo's existing test projects; adapt filter names to what exists and record them in GAP-MATRIX.md.

---

## Day 0 — Sun 11 Oct (tonight, owner)
- [x] D0-T1 Pause the 6-hourly cloud routine that pushes to `main`
- [x] D0-T2 Commit SUTRADHAR.md (root, also as CLAUDE.md) and plan/PLAN.md on a branch `chore/sutradhar-plan`; merge it
  ✔ merged via commit `b5b2188` ("Merge chore/sutradhar-plan: bootstrap Sutradhar plan (D0-T2)")
- [x] D0-T3 Enable branch protection on `main` (PR required)
  ✔ owner created a repository ruleset; confirmed live via `GET /repos/.../rules/branches/main` → `pull_request`, `deletion`, `non_fast_forward` all active; `GET /repos/.../branches/main` → `protected: true`

## Day 1 — Mon 12 Oct · Audit, stabilize, gap matrix
- [x] D1-T1 Baseline: build and tests from a clean clone; record failures in STANDUP
  ✔ `dotnet build -warnaserror` and `dotnet test` results recorded (pass OR listed failures) — recorded: strict build FAILED (5× CA1416, DesktopUiModule), plain build/tests/frontend build all green
- [x] D1-T2 Make the baseline green (fix broken build/tests only; no features)
  ✔ `dotnet build -warnaserror && dotnet test` → all green; `npm run build` green
- [x] D1-T3 `plan/GAP-MATRIX.md`: for each acceptance item P0-1…P0-8 + the frozen features (Vault, RLS, RBAC, audit, marketplace UI): status Exists / Partial / Missing, evidence (file paths, tests), and naming differences vs the blueprint vocabulary
  ✔ file exists, every row has evidence; owner-approval gate explicitly waived by the owner (2026-10-12,
  low-risk/reversible naming call) rather than formally approved — see GAP-MATRIX.md's "Resolution" note
- [x] D1-T4 CI baseline in GitHub Actions: build, test, `dotnet format --verify-no-changes`, frontend lint/test/build on every PR
  ✔ workflow green on a PR — run 38267715485 on PR #3 (`feature/D1-T4-ci-baseline`), fixed once (Node 20→24
  for jsdom's engines floor) after the first attempt failed, second run green on both jobs
- [x] D1-T5 Reconcile Days 2–6 below against GAP-MATRIX: mark tasks already proven as `ALREADY DONE (evidence)`; add missing work under DISCOVERED
  ✔ full reconciliation table in plan/GAP-MATRIX.md, verified against `dotnet test --list-tests` + repo
  greps, not just Day-1 prose. 2 Day-1 claims corrected (P0-4 WebModule/LogsModule tests, P0-6 SignalR
  live-run test both did not exist). 1 AI-vendor question flagged under DISCOVERED for the owner
  (OpenAI vs Anthropic) rather than decided unilaterally.
- **Exit check:** green baseline + CI + approved GAP-MATRIX. No feature code merged today.

## Day 2 — Tue 13 Oct · Module contract, flow engine, CLI (P0-1, P0-2, P0-3, P0-5)
- [x] D2-T1 `IAutomationModule` contract + registry; `GET /api/v1/modules` returns all actions with schemas and non-empty descriptions; test registers a synthetic module with no other code change
  ✔ `dotnet test --filter "FullyQualifiedName~ModuleRegistry"` → 6/6 passing (`ModuleRegistryTests.cs`,
  real route recorded: `GET {apiV1}/modules`, not `/modules/actions` as this line assumed — corrected).
  DISCOVERED + fixed in the same PR: the new "every action/param needs a Description" validation this
  task required would have broken startup — WebModule/LogsModule/MobileModule/DesktopUiModule had 25
  production ParamSpecs with no Description at all. Backfilled all 25 with real descriptions before
  adding the validation, not after — confirmed via full suite: 77/77 (was 71/71).
- [x] D2-T2 Variable interpolation `{{vars.a.b}}`, `saveAs`, publish-time check that referenced vars are produced earlier
  ✔ `dotnet test --filter "FullyQualifiedName~Variable"` → 11/11 passing (`VariableStoreTests.cs`, new —
  no prior coverage existed under any name). Real bug found + fixed: a missing `{{vars.x.y}}` path
  silently became `null` (whole-match) or an empty string (embedded in a larger string) — now throws
  `VariableResolutionException` naming the path, caught by `FlowEngine` and turned into a proper
  `StepResult.Failed` that respects `ContinueOnFailure` like any other failure. Publish-time check that
  a referenced var was produced by an *earlier* step is D2-T5's job (`Publish validation`), not this one
  — this task proves the runtime resolution semantics only.
- [x] D2-T3 Flow engine: ordered steps, fail-fast, `ContinueOnFailure`, `when` → Skipped, retry policy, flaky classification with retry history
  ✔ `dotnet test --filter "FullyQualifiedName~FlowEngine|FullyQualifiedName~RetryFlake"` → 7/7 passing
  (`FlowEngineSemanticsTests.cs`, new). Real bug found + fixed in `RetryFlakeHandler`: the final attempt
  — whether it succeeded or failed — was being added to `RetryHistory` alongside the genuine retries,
  so `MaxAttempts:1` wrongly produced 1 history entry for a never-retried single try (spec wants 0), and
  3-of-3 failures wrongly produced 3 entries instead of the 2 real retries (spec wants 2, final error on
  the result itself). Fixed: only attempts that are followed by another attempt go into history.
- [x] D2-T4 Per-step durable commit of StepResult; test kills execution after step 2 and finds steps 1–2 persisted
  ✔ ALREADY DONE (evidence), filter adapted per SUTRADHAR §2.8: `dotnet test --filter "FullyQualifiedName~GracefulShutdown"`
  → `GracefulShutdownTests.Step_in_flight_when_shutdown_is_requested_still_commits_its_result`, passing
- [x] D2-T5 Publish validation: unregistered action → 422 RFC 7807 naming the step; Published flows immutable (edit = new version)
  ✔ `dotnet test --filter "FullyQualifiedName~Publish"` → 10/10 passing (`FlowPublishValidationTests.cs`,
  new). Partial before today: resolver pattern, step ordering, Draft-only-editable, `CreateNewVersion`
  all already existed and needed only proving. Genuinely built: structured `{field, code, message}`
  errors (was a plain `string` list), the `missing_required_param` check (didn't exist), `PublishedAt`
  timestamp (didn't exist), and the API publish endpoint actually returning 422 (it unconditionally
  returned 200 OK before, even on a failed publish — confirmed via source read, not assumed).
- [x] D2-T6 CLI (`yukti run <flow.json> --junit out.xml`), exit 0/1, runs without the API server
  ✔ `dotnet run --project src/Yukti.Cli -- run demo/api_chain.json --var baseUrl=<stub> --junit out.xml`
  → exit 0, valid XML, 3 testcases, 0 failures. New project `src/Yukti.Cli`, `System.CommandLine`
  2.0.12 (ADR-0002), packaged as both a .NET tool (`PackAsTool`) and a self-contained single-file exe
  from one project (owner's call — both now, not deferred to Day 6). 8/8 process-level integration
  tests passing (`tests/Yukti.Cli.Tests`) covering all 6 scenarios the spec names plus secret
  redaction: pass/fail/unregistered-action/bad-JSON/missing-file/flaky(via the engine's own real retry
  policy against a stub that fails once)/secret-never-leaks/timeout. Reuses the exact same
  `PublishFlowCommandHandler`/`Flow.Publish`/`FlowEngine` the real API uses — no parallel validator or
  engine. 3 real bugs found and fixed along the way (not specific to the CLI — all 3 affected the API
  path equally, just never previously exercised by an end-to-end chained flow): (1) `ApiModule`'s
  `StepOutcome.Data` was an anonymous object, not a `Dictionary<string,object?>` — `VariableStore` can
  only walk dictionaries, so `{{vars.step.body.field}}`-style chaining silently failed past the first
  hop for every API step in the whole system; (2) `StepResult.Duration` was hardcoded to
  `TimeSpan.Zero` in `FlowEngine` even though the dispatch stopwatch was already being measured, just
  never applied — every report's timing data was meaningless; (3) `Flow.DeclaredVariables` existed as
  a persisted property (even has an EF column) with no method to ever set it and no check in
  `Flow.Publish` — meaning a flow could never declare "this variable comes from outside" and have
  publish validation respect it; added `Flow.DeclareVariable(name, default)`, wired into `Publish`'s
  `availableBindings` seeding, and into the flow-file schema's `declaredVariables` field.
- **Exit check:** chained 3-step API flow runs from the CLI with JUnit output.

## Day 3 — Tue 13 Oct · Cross-module proof (P0-4)
**Superseded 2026-10-13 by the owner's "Continuity Spec: Day 3 to Production" doc** — renumbers this
day around a real local fixture app instead of a public site, and folds the old D3-T4 "unified report"
into D3-T4's own exit criteria rather than keeping it as a separate task. Old D3-T1 (API module) is not
re-listed as its own task — it's `ALREADY DONE` (10/10 passing, `ApiModuleAssertionTests.cs`, confirmed
2026-10-12) and gets exercised again for free inside the new D3-T4's UC-1 chain, so nothing regresses by
dropping its task ID.
- [x] D3-T1 `demo/fixtures/FixtureApp`: small ASP.NET minimal-API app (tests/demos only) — `/login` page
  with known `data-testid` attrs, sets a session, redirects to `/orders`; orders page shows a generated
  order ID; `GET /api/orders/{id}` → `{id, status:"confirmed"}`; every request logged to
  `demo/fixtures/app.log` (timestamp, level, order ID), `?fail=1` logs an ERROR for that order; starts on
  a random port inside tests (no internet dependency, no third-party-site flakiness)
  ✔ `dotnet test --filter "FullyQualifiedName~FixtureApp"` → 6/6 passing (`FixtureAppTests.cs`, new
  project `demo/fixtures/FixtureApp`). Hosted in-process via `WebApplication`/Kestrel bound to port 0 —
  no subprocess, no separate runtime resolution at all. Session via ASP.NET Core's built-in `ISession`;
  logging is a terminal middleware writing one line per request after the response is known.
- [x] D3-T2 Web module proof: navigate, click, fill, select, waitFor, assertText (contains + exact),
  assertVisible, screenshot — all against FixtureApp; missing selector → typed `SelectorNotFound` naming
  the selector and the step; screenshots stored with the step result
  ✔ `dotnet test --filter "FullyQualifiedName~WebModule"` → 5/5 passing (`WebModuleTests.cs`, new). Added
  the two missing actions (`select`, `assertVisible`) to `WebModule.cs`; added a typed
  `SelectorNotFoundException` + `WithSelectorNotFound` helper that converts Playwright's own
  `PlaywrightException` (its real public exception — `Microsoft.Playwright.TimeoutException` doesn't
  exist publicly) into a message naming the selector, wired through `click`/`fill`/`assertText`/
  `waitForSelector`; fixed `screenshot`'s `Data` from an anonymous object to a `Dictionary<string,
  object?>` so `{{vars.step.path}}`-style chaining works; `FixtureAppHost.cs` gained a select dropdown and
  a hidden element for the new assertions to exercise. Full solution suite also re-run clean: 124/124
  (17+8+8+91 across Analyzers/Cli/Infrastructure/Orchestration test projects).
- [x] D3-T3 Logs module proof: `checkRules` tested for pattern, severity, max count, and time window;
  matching lines included in the step result; a missing log file fails the step clearly, never passes
  ✔ `dotnet test --filter "FullyQualifiedName~LogsModule"` → 6/6 passing (`LogsModuleTests.cs`, new),
  against a real `app.log` written by FixtureApp. `LogsModule` gained `logFile` (missing file → step
  fails naming the path, previously read as "0 lines, all rules passed"), per-rule `severity`,
  `since`/`until` window, and matched lines returned in the step data (as dictionaries, so `{{vars}}`
  chaining works). Module stays stateless — `ModulePluginContractTests` (FR-PLUGIN-07) still green.
  FixtureApp now writes each log line in `Response.OnStarting` (before the client gets any byte), which
  removed the `Task.Delay(50)` race from `FixtureAppTests` and made UC-1's Logs step race-free.
- [x] D3-T4 UC-1 flow (`demo/uc1_flow.json`): Web login → read the order ID from the page, `saveAs: order`
  → API `GET /api/orders/{{vars.order.id}}` asserting `status` is `confirmed` → Logs check: no ERROR
  lines for that order ID in the run's time window. Runs from the CLI with **no CLI code changes** (Day
  2's leading indicator). With `?fail=1`, the Logs step fails, exit code 1, JUnit names the Logs step.
  ✔ `yukti run demo/uc1_flow.json --junit out.xml` → exit 0, 7/7 passed, one JUnit report.
  `demo/uc1_flow_fail.json` (adds `?fail=1`) → exit 1, JUnit `<testcase name="No ERROR logged for this
  order" classname="yukti.logs.checkRules">` with the offending ERROR line in the failure body. Zero
  `src/Yukti.Cli` changes. Needed one module addition: `web.getText` (no action returned page text as
  data). The order id is read as `{{vars.order.text}}`. "Run's time window" is enforced by the order id
  itself (FixtureApp generates a fresh one per login), not a timestamp — see DISCOVERED.
- [x] D3-T5 Flake run: UC-1 × 50
  ✔ flake rate < 2%, recorded in STANDUP; every flaky occurrence's cause written down
  → 50/50 passed, 0 failed, 0 flaky (0%), 156s total. Cross-checked: all 50 CLI summaries and all 50
  JUnit `<testsuite>` elements report 7 tests / 0 failures. No flaky occurrences, so no causes to record.
- **Exit check:** `yukti run demo/uc1_flow.json --junit out.xml` exits 0 on `main`, flake rate < 2%.
- **Exit check:** UC-1 passes via CLI with one unified report.

## Day 4 — Thu 15 Oct · API testing UI + live runs (P0-6)
- [x] D4-T1 Generated TS types from the backend OpenAPI document wired into the frontend build
  ✔ `npm run generate:api && npm run typecheck && git diff --exit-code src/api/generated` → all pass.
  `openapi-typescript` generates `src/api/generated/schema.ts` from the live `swagger.json`; every
  hand-written request-body type in `services/api-client.ts` now comes from `components["schemas"]`
  (`satisfies`/intersection so required fields stay enforced). New CI job `api-types-drift`: builds the
  backend, migrates a throwaway Postgres, boots the API, regenerates, fails on `git diff`. Verified the
  whole migrate→boot→generate sequence locally against a fresh database before writing the workflow.
  Scope found and NOT expanded into: every endpoint's Swagger entry is a bare `200: OK` with no response
  schema (no `.Produces<T>()` anywhere), so response types stay hand-written in `services/types.ts` —
  see DISCOVERED. `generated/` is gitignored from hand-edits via its own header comment + an
  `.oxlintrc.json` ignore pattern, not from version control (it IS committed, so drift is visible in PRs).
- [x] D4-T2 Flow list + editor using `SchemaDrivenParamEditor`; ESLint rule bans module-kind branching
  ✔ Filter adapted → `schema-driven-param-editor.test.tsx` (vitest filters by file path; kebab-case is this
  repo's convention, so the file isn't literally named `SchemaDrivenParamEditor`) — 6/6 passing, includes
  the synthetic-module (D2-T1's `SyntheticXyzModule`, mirrored exactly) render test: required fields
  marked, the Number param's real default pre-filled without being asked, the Object param rendered as a
  JSON textarea, and an unrecognized param type falling back to a JSON field with a visible warning.
  `ParamFields` (flow-authoring's and the Tests tabs' shared editor since before this task — exported as
  `SchemaDrivenParamEditor` too now) already had zero module-kind branching; gained default-prefill and
  the unknown-type fallback. The one real violation found: `module-test-form.tsx`'s Mobile device-config
  panel was `moduleKind === "mobile"`. Moved into `src/features/shared/module-extras.ts` (`MODULE_EXTRAS`,
  a plain object keyed by module kind — the one sanctioned place a module kind is a literal) and the
  panel itself into `testing/mobile-pre-run-panel.tsx`; `module-test-form.tsx` now does a lookup, not a
  comparison. New ESLint rule `no-module-kind-branching` (`eslint-rules/`, wired into `npm run lint`
  alongside oxlint — oxlint can't run custom JS rules) flags `moduleKind === "..."` and `switch
  (moduleKind)` outside that lookup; proven with a real `RuleTester` suite (4 invalid cases including a
  planted violation, 4 valid including the registry lookup itself) — 4/4 passing.
  ✔ `npm run lint && npm test -- schema-driven-param-editor` → both pass; `npm test` → 210/210 overall.
- [x] D4-T3 API request builder + response viewer (status, timing, JSON tree) — reuse existing components
  ✔ ALREADY DONE (evidence), filter adapted per SUTRADHAR §2.8: `npm test -- api-studio-page` →
  `ApiStudioPage > renders the explorer tree and request designer panes`, passing
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
- [x] D6-T3 Vault proof: secrets resolved from Vault at runtime, never logged; credential rotation audited
  ✔ ALREADY DONE (evidence): `dotnet test --filter "FullyQualifiedName~Vault"` → 4/4 passing in
  `VaultCredentialResolverTests.cs`, against a real local Vault dev server (built 2026-10-11)
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
- [ ] DISC-1 Engine-provided run metadata (`{{vars.run.startedAt}}`): lets a Logs step bound its time
  window to the current run without per-run state in the module (FR-PLUGIN-07 forbids instance state,
  so the module can't record it itself). Needs `Flow.Publish` to treat `run.*` as an available binding.
  Found during D3-T3/T4; UC-1 is correct today because it scopes by a per-run order id.
- [ ] DISC-2 CLI JUnit failure body prints nested params via `ToString()`
  (`rules=System.Collections.Generic.List`1[System.Object]`). Render them as JSON. Found in D3-T4;
  deferred because D3-T4 forbids CLI code changes.
- [ ] DISC-3 No `Yukti.Api` minimal-API endpoint declares `.Produces<T>()`, so Swagger has zero response
  schemas (every endpoint is a bare `200: OK`) — `openapi-typescript` can only generate real types for
  request bodies. Response types in `apps/yukti-gui/src/services/types.ts` stay hand-written until this
  is done. Found during D4-T1; scoped out of it deliberately (it's ~20 endpoints in `Program.cs`, purely
  additive metadata, no behavior change, but a separate task from "wire the frontend to generated types").
- [ ] DISC-4 `MobilePreRunPanel`'s form fields (`testing/mobile-pre-run-panel.tsx`) use a `<label>` that
  isn't associated with its `<input>` (no `htmlFor`/`id`, not nested) — fails `getByLabelText`, invisible
  to a screen reader. Pre-existing (moved verbatim out of `module-test-form.tsx` during D4-T2, not
  introduced by it); belongs with the Day 4 WCAG/axe pass, not fixed here to stay in scope.
