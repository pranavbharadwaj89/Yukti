# Yukti v0.1 — Standup Log

Append-only. New entries go at the bottom.

---

## 2026-10-11 ~22:00 IST — Day 0
Done:        D0-T1 (paused cloud routine trig_01768W9Jm3uZvZhcbHktj8Ky), D0-T2 (SUTRADHAR.md/CLAUDE.md/plan/PLAN.md/plan/STANDUP.md merged via `chore/sutradhar-plan`, commit b5b2188)
Blocked:     D0-T3 — branch protection PUT returned `403 Resource not accessible by personal access token`; the owner's PAT lacks Administration permission. Needs either a PAT with that scope, or the owner adding the rule manually (repo → Settings → Branches → Add rule → `main` → Require PR before merging).
Discovered:  none yet
Scope guards: none yet
Exit check:  on track
Next:        Day 1 baseline (done below) and GAP-MATRIX (D1-T3)

## 2026-10-11 ~22:20 IST — Day 1
Done:        D1-T1 (true baseline recorded — see below), D1-T2 (baseline made green)
In progress: D1-T3 GAP-MATRIX.md (next)
Blocked:     D0-T3 carried over (see above, still needs owner action)
Discovered:  D1-T2 found a real strict-build break: `dotnet build -warnaserror` failed with 5× CA1416
             ("This call site is reachable on all platforms... DesktopUiModule... only supported on
             'windows'") in src/Yukti.Api/Program.cs — the plain `dotnet build` used all week never
             surfaced this because it only warns, not errors. Fixed by guarding every DesktopUiModule
             DI-registration/resolution site with `OperatingSystem.IsWindows()`, matching the module's
             own documented Windows-only nature (Volume 1 Part III) — not a suppression, a genuine
             platform guard. Verified: `dotnet build -warnaserror` -> 0 Warnings, 0 Errors;
             `dotnet test` -> 71/71 passed; `npm run build` (apps/yukti-gui) -> green.
Scope guards: none
Exit check:  on track (no feature code merged yet, per Day 1's own rule — this fix is baseline
             stabilization, not a feature)
Next:        D1-T3 GAP-MATRIX.md — audit each P0 item + frozen feature against the real codebase

## 2026-10-11 ~22:45 IST — Day 1 (cont'd)
Done:        D1-T3 plan/GAP-MATRIX.md written with real evidence for all 8 P0 items + 5 frozen
             features (Exists/Partial/Missing + file paths + test names for each). NOT ticked in
             PLAN.md yet — its own acceptance bar requires owner approval, which hasn't happened.
In progress: none
Blocked:     D0-T3 (branch protection, carried); D1-T4 CI baseline and D1-T5 reconciliation both
             depend on the owner reviewing/approving GAP-MATRIX.md first, per D1-T3's own rule and
             SUTRADHAR.md §5 step 2 ("never pull a later task while today has unchecked P0 work" —
             here the blocker is approval, not more build work).
Discovered:  Real gaps found, not assumed from the architecture spec: no CLI project exists at all
             (P0-5); no cross-module/UC-1-style integration test exists (P0-4); AI flow generation
             doesn't validate against registered actions or retry on bad output, and its confidence
             score is a hardcoded literal (P0-7); no explicit AI-off toggle or CI job exists, only
             an implicit fallback-when-no-key (P0-8); no synthetic-module registry proof exists
             (P0-1); retry/flaky logic is real production code (RetryFlakeHandler.cs) but has no
             test proving multi-attempt retry + flaky classification (P0-3); the Marketplace UI
             (frozen feature) is a real, accessible, but entirely backend-disconnected static demo
             — "install" toggles local React state only, never touches ModuleRegistration.
Scope guards: none
Exit check:  at risk, not failed — Day 1's own exit check ("no feature code merged today") still
             holds (D1-T2's fix was baseline stabilization, not a feature). But D1-T4 (CI baseline)
             and D1-T5 (reconciliation) are both gated on owner approval of GAP-MATRIX.md, which
             can't happen inside this session. Flagging now rather than guessing the approval and
             proceeding into Day 2 on an unconfirmed gap list.
Next:        Owner reviews plan/GAP-MATRIX.md (esp. the P0-1..P0-8 naming inference and the
             /ai/flows/suggest vs /flows/generate naming question) and approves or corrects it.
             Once approved: D1-T4 (CI baseline workflow) and D1-T5 (reconcile Days 2-6), then Day 2.

## 2026-10-12 ~20:15 IST — Day 0 close-out
Done:        D0-T3 closed — owner created a GitHub repository ruleset on `main` (not classic branch
             protection) with `pull_request`, `deletion`, `non_fast_forward` rules. First attempt had
             an empty branch target (`conditions.ref_name.include: []`), so the ruleset existed but
             enforced nothing (`GET /rules/branches/main` returned `[]`) — owner added "Include default
             branch" as the target and re-saved. Re-verified: `GET /rules/branches/main` now lists all
             3 rules active, `GET /branches/main` → `protected: true`.
In progress: none
Blocked:     none open
Discovered:  none
Scope guards: none
Exit check:  Day 0 fully closed (D0-T1, D0-T2, D0-T3 all done)
Next:        Merge `feature/D1-T1-T2-baseline-stabilization` and `feature/D1-T3-gap-matrix` into `main`
             (owner — PAT still lacks `pull_request` write scope, confirmed via 403 on `POST /pulls`,
             but with the ruleset active the owner can now merge via the GitHub UI directly, or open
             the compare/merge page themselves). Then owner approval of GAP-MATRIX.md naming questions
             unblocks D1-T4 (CI baseline) and D1-T5 (reconciliation), then Day 2.

## 2026-10-12 ~20:45 IST — Day 1 (cont'd)
Done:        Both open branches merged into `main` via the GitHub UI (owner) — confirmed live,
             `main` HEAD is `9e1a798`. GAP-MATRIX.md naming questions decided unilaterally rather than
             waiting on owner sign-off (owner pushed back on the approval gate as unnecessary for a
             low-risk, reversible call): kept the inferred P0-1..P0-8 mapping, kept `/ai/flows/suggest`
             as-is (not renamed to `/flows/generate`). D1-T4 (CI baseline) done: added
             `dotnet format --verify-no-changes` + switched the existing build step to `-warnaserror`
             + new frontend job (npm ci, lint, test, build) to .github/workflows/ci.yml. Pre-existing
             whitespace-only formatting debt (AssertionEvaluator/DesktopUiModule/LogsModule/
             MobileModule/WebModule.cs) fixed first via `dotnet format whitespace` so the new gate
             started green, not red. First PR run (commit 4144a11) failed: frontend `Test` step threw
             `webidl.util.markAsUncloneable is not a function` on every test file — real root cause,
             not flaky: jsdom@30.x declares `engines.node: "^22.22.2 || ^24.15.0 || >=26.0.0"`, workflow
             pinned Node 20. Fixed (commit 15de599) by bumping to Node 24, matching the version tests
             already passed under locally. Re-ran: both jobs green
             (https://github.com/pranavbharadwaj89/Yukti/actions/runs/38267715485 superseded by the
             Node-24 rerun, also success).
In progress: none
Blocked:     none
Discovered:  The frontend job had literally never run in CI before today — this is the first time
             D1-T4 CI baseline existed at all. The Node 20 vs jsdom 30 floor mismatch could not have
             been caught any earlier.
Scope guards: none
Exit check:  on track
Next:        D1-T5 — reconcile Days 2-6 against GAP-MATRIX.md (mark already-proven tasks
             `ALREADY DONE (evidence)`, add missing work under DISCOVERED), then start Day 2
             (module contract, variable interpolation, flow engine, CLI).

## 2026-10-12 ~21:00 IST — Day 1 close-out (D1-T5)
Done:        D1-T5 reconciliation complete, verified against real evidence (`dotnet test --list-tests`
             + repo greps/globs for every Day 2-6 filter name), not just restated from Day-1 prose.
             Ticked as ALREADY DONE (evidence): D3-T1 ApiModule (10/10 passing, no adaptation needed),
             D6-T3 Vault (4/4 passing, no adaptation needed), D2-T4 Durability (filter adapted to
             `GracefulShutdown`, re-confirmed 1/1 passing just now), D4-T3 RequestBuilder (filter
             adapted to `api-studio-page`, re-confirmed 2/2 passing just now).
Discovered:  Two Day-1 GAP-MATRIX claims were wrong and are now corrected in the file itself: (1)
             claimed WebModule/LogsModule "each with dedicated passing tests" — false, zero dedicated
             tests exist for either, confirmed by `dotnet test --list-tests` and a repo-wide glob for
             `*WebModule*Test*.cs`/`*LogsModule*Test*.cs` (no files). (2) claimed SignalR live-run
             "already built and tested" — false, the implementation is real
             (services/signalr.ts, RunProgressHub.cs) but zero test files reference reconnect/signalr/
             runprogress on either frontend or backend. Also found: D3-T4's named filter (`Report`)
             technically matches one test but it's unrelated (idempotent event-consumer redelivery,
             not a unified status model) — a false-positive match, flagged explicitly so it's never
             mistaken for done. D4-T1's `generate:api`/`typecheck` npm scripts don't exist at all.
             D5-T1's `IAiProvider` abstraction doesn't exist — only a concrete OpenAI integration
             (`OpenAiAssistantService.cs`), no Anthropic anywhere. This is a vendor decision, not a
             rename, so it was NOT decided unilaterally — flagged under GAP-MATRIX.md's DISCOVERED
             section with 3 options for the owner, blocking Day 5 start (not Day 2-4, which don't
             touch AI).
Scope guards: none
Exit check:  Day 1 fully closed — D1-T1 through D1-T5 all done, all evidence-based, 2 prior overclaims
             caught and corrected rather than carried forward silently.
Next:        Day 2 — D2-T1 (module registry + synthetic-module proof), the first unchecked task with
             no upstream dependency. D2-T4 is already done (see above). AI vendor question (DISCOVERED
             #1) needs an owner answer before Day 5, not blocking Day 2 start.

## 2026-10-12 ~21:30 IST — Day 2 begins (per owner's "Yukti — Day 2 Spec" doc)
Done:        D2-T1 (synthetic-module proof, P0-1). Real actions route found: `GET {apiV1}/modules`
             (Program.cs:1106) — the task line's `/modules/actions` was never real, corrected in
             PLAN.md. Added `tests/Yukti.Orchestration.Tests/ModuleRegistryTests.cs`: a
             `SyntheticXyzModule` defined only in the test project (`ModuleKind.Custom("synthetic-xyz")`,
             action `doSomethingNovel`, 3 params matching the spec exactly) proves (a) it resolves
             through the plain in-process `ModuleRegistry` with zero other code change, (b) its full
             schema — descriptions included — is exactly what `GET /api/v1/modules` would serialize,
             (c) a flow step actually dispatches to its `Run` with interpolated params through the real
             `FlowEngine`, (d) no file under `src/` contains the word "synthetic" (source-scan negative),
             (e)+(f) registering an action/param with a missing Description now throws `DomainException`
             naming the action/param.
Discovered:  Adding (e)/(f)'s validation to `ModuleRegistration.RegisterAction` would have broken real
             app startup: `WebModule`, `LogsModule`, `MobileModule`, `DesktopUiModule` had 25 production
             `ParamSpec`s with no `Description` at all (only `ApiModule` was fully documented). Not a
             pre-existing bug anyone had filed — found by deliberately checking before wiring the
             validation in, not after. Fixed by writing real descriptions for all 25 (file-by-file,
             reviewed against each action's actual behavior), confirmed via source grep (0 missing
             across all 5 built-ins) before adding the throw.
Scope guards: none — the description backfill is in-scope: D2-T1's own negative-test criterion cannot
             exist safely without it, so it's a precondition, not expansion.
Exit check:  on track. `dotnet build -warnaserror` → 0/0. `dotnet test` → 77/77 (was 71/71, +6 new).
Next:        D2-T2 — variable interpolation proof (P0-3 per the Day-2 spec's corrected mapping).

Done:        D2-T2 (variable interpolation proof, P0-3). No prior coverage existed under any name
             (confirmed via `dotnet test --list-tests` + grep — matches GAP-MATRIX). Added
             `tests/Yukti.Orchestration.Tests/VariableStoreTests.cs`: 11 tests covering top-level +
             nested resolution, type preservation (`{{vars.x.count}}` stays an `int`, not `"42"`),
             a genuinely-null value vs. a missing path, no-template passthrough, concurrent-run
             isolation, `saveAs` ordering through the real `FlowEngine` (visible to later steps, never
             earlier), and the missing-path failure mode end-to-end (fails only that step, respects
             `ContinueOnFailure`/fail-fast like any other failure).
Discovered:  A real bug, found by writing the test the spec asked for, not assumed: a `{{vars.x.y}}`
             reference to a path no earlier step produced silently resolved to `null` (whole-parameter
             match) or substituted an empty string (embedded in a larger string) — never an error,
             contradicting the spec's explicit requirement and `FlowEngine.cs`'s "exceptions only for
             the exceptional" convention (SUTRADHAR §7) being violated by silence instead. Fixed
             minimally: `VariableStore` now throws a new `VariableResolutionException` naming the exact
             path on an unresolved reference (distinguished from a path that resolves to a legitimate
             `null` value, which is not an error); `FlowEngine.Execute` catches that one specific type
             at the interpolation call site and turns it into a normal `StepResult.Failed`, going
             through the identical fail-fast/`ContinueOnFailure` branch every other step failure already
             uses — not a special case. Added `StepResult.Failed(...)` as a static factory, mirroring
             the existing `StepResult.Skipped(...)`.
Scope guards: none — the fix is the minimal one the proof test demanded, not a redesign; no ADR needed
             per SUTRADHAR §5 step 8 ("a proof test exposes a real bug → minimal fix, noted here").
Exit check:  on track. `dotnet build -warnaserror` → 0/0. `dotnet test` → 88/88 (was 77/77, +11 new).
Next:        D2-T3 — flow engine semantics proof (retry/flaky with exact history-entry counts,
             fail-fast, ContinueOnFailure, when→Skipped), per the Day-2 spec's detailed acceptance list.

Decided:     D2-T6 CLI packaging (the spec's own blocking open question) — owner wants **both** a .NET
             tool (`dotnet tool install`) and a self-contained single-file exe built now, not deferring
             the self-contained build to Day 6 as the spec itself recommended. Noted for when D2-T6
             starts: `Yukti.Cli.csproj` needs `PackAsTool` + a separate publish profile
             (`RuntimeIdentifier`, `PublishSingleFile`, `SelfContained`) producing both artifacts from
             one project.

Done:        D2-T3 (flow engine semantics, P0-3). Added
             `tests/Yukti.Orchestration.Tests/FlowEngineSemanticsTests.cs` (7 tests) via a new
             `ScriptedModule` (test-only, returns a scripted outcome sequence, tracks call count): all 4
             retry/flaky criteria (2-fail-then-pass → Passed+flaky+2 history entries; 3-of-3 fail →
             Failed+2 entries+not flaky; MaxAttempts:1 fail → Failed+0 history; first-try pass → not
             flaky+empty history, including the flake-detected domain event firing exactly once/zero
             times as appropriate) and all 4 fail-fast/ContinueOnFailure/when criteria (default stops
             after step 2 of 4 with 2 results; ContinueOnFailure runs all 4 but still Failed overall;
             a false `when` stays Skipped in its original position with Run never called; Passed+Skipped
             mix with no failures is Passed).
Discovered:  A real, more serious bug than D2-T2's: `RetryFlakeHandler.ExecuteWithRetry` was recording
             the *final* attempt into `RetryHistory` regardless of whether it ultimately succeeded or
             failed — only the success path skipped it (by returning before the `Add` line), the failure
             path didn't. Caught by writing the two tests the spec explicitly asks for
             (`MaxAttempts:1` failing once; `MaxAttempts:3` failing all three) — both would have reported
             the wrong history count (1 instead of 0; 3 instead of 2) against a repo that had never had
             a single test exercise a real retry before today (GAP-MATRIX, D1-T3: the one existing use of
             `RetryPolicy` in a test used `MaxAttempts:1` for its own unrelated reason, so this path was
             never reached). Fixed minimally: an attempt only joins `RetryHistory` if the loop is going
             to retry past it; the attempt that actually produces the returned outcome (pass or fail)
             never does, whether it's the first try or the last.
Scope guards: none — minimal fix to the exact method the proof test targets, no redesign.
Exit check:  on track. `dotnet build -warnaserror` → 0/0. `dotnet test` → 95/95 (was 88/88, +7 new).
Next:        D2-T5 — publish validation + immutable versioning (P0-2). D2-T6 (CLI) after, now unblocked
             on packaging (both .NET tool and self-contained exe, decided above).

Done:        D2-T5 (publish validation + versioning, P0-2). Found more already built than GAP-MATRIX's
             Day-1 pass suggested: `Flow.Publish(IModuleActionResolver)` already enforced Draft-only
             editing, step ordering, and `CreateNewVersion()` (same FamilyId, Version+1) already existed
             and worked correctly — these only needed proof tests, not building. Genuinely missing and
             built: (1) `FlowPublishResult.Errors` was `IReadOnlyList<string>` (plain messages) — changed
             to `IReadOnlyList<FlowPublishError>` with `{Field, Code, Message}`, matching the spec's exact
             `steps[i].action` / `steps[i].params.<name>` / `unresolved_module_action` /
             `unresolved_variable` / `missing_required_param` shape; (2) the `missing_required_param`
             check didn't exist at all — added via a new `IModuleActionResolver.TryGetActionSchema`
             method (implemented in both `ModuleActionResolver` and `EfModuleActionResolver`, the
             in-memory and real EF-backed resolvers) so `Publish` can check each action's `ActionSchema`
             for required params the step's `Params` dict is missing; (3) `Flow.PublishedAt` didn't exist
             — added, set on successful publish; (4) **the real API publish endpoint
             (`POST /api/v1/flows/{id}/publish`) returned 200 OK unconditionally, even when
             `result.Succeeded` was false** — confirmed by reading `Program.cs` directly, not assumed —
             fixed to return 422 `application/problem+json` (`ProblemResults.UnprocessableEntity`, new)
             with the structured errors array and a `correlationId`, matching every other error response
             in the API. `FlowStep.GetReferencedVariablePaths()` replaced with
             `GetReferencedVariableReferences()` so each reference carries its originating
             `params.<name>` or `when` field, not just the bare path.
Discovered:  `FlowStep`'s variable-reference scan only looks at top-level string param values — a
             `{{vars.x}}` template nested inside an object/array param (e.g. a JSON body field) isn't
             caught at publish time, only at runtime via `VariableStore`. Pre-existing, not introduced or
             widened by today's change (same scope as before); out of today's time-box, not silently
             fixed — flagging here rather than either expanding scope or leaving it undiscovered.

             SARVAGHNA's pre-commit hook blocked the first commit attempt (HIGH severity, Volume 5 §5.2 /
             analyzer YUKTI002): my new `TryGetActionSchema` copied an existing
             `.GetAwaiter().GetResult()` blocking pattern already present (unflagged) in both
             `ActionExists` implementations. Rather than bypass the hook, fixed it properly: dropped
             `ActionExists` entirely (it had no callers once `Flow.Publish` switched to the schema-based
             check), made `IModuleActionResolver.TryGetActionSchema` genuinely `async Task<...>` with a
             `CancellationToken`, and threaded that async all the way up through both real
             implementations, `Flow.Publish` itself (now `async Task<FlowPublishResult>`), and
             `PublishFlowCommandHandler.HandleCore` (already async, just needed `await`). `Yukti.Host`
             needed no change — it only calls the command handler, already async at that layer.
Scope guards: none — every change is the minimal shape the task's own stated criteria (plus the hook's
             correctly-enforced async rule) require.
Exit check:  on track. `dotnet build -warnaserror` → 0/0. `dotnet test` → 105/105 (was 95/95, +10 new).
Next:        D2-T6 — the CLI. Packaging decided (both .NET tool + self-contained exe). Posting the CLI
             plan before writing any code, per the spec's own explicit checkpoint.

Done:        D2-T6 (the CLI, P0-5) — **Day 2 is now fully closed, all 6 tasks done.** New project
             `src/Yukti.Cli`: `System.CommandLine` 2.0.12 (ADR-0002, `plan/adr/0002-cli-system-
             commandline.md`), packaged as both a .NET tool (`PackAsTool`) and a self-contained
             single-file exe from one project (`RuntimeIdentifiers` + a `RuntimeIdentifier`-conditioned
             property group) — owner's explicit call, both now rather than deferring the self-contained
             build to Day 6. Verified both really work: `dotnet pack` produced installable nupkgs;
             `dotnet publish -r win-x64` produced a real standalone `Yukti.Cli.exe` (161MB, bundled
             runtime), smoke-tested directly with no system dotnet involved at all — ran the demo flow
             correctly. `CliHost.cs` generalizes Yukti.Host's exact in-memory composition pattern (same
             repos, same seeded admin user/role) from one fixed scenario into a reusable host.
             `FlowLoader.cs` drives flow-file JSON through the identical `CreateFlowCommandHandler`/
             `AddFlowStepCommandHandler`/`PublishFlowCommandHandler` the API uses — no parallel
             validator. `ReportWriters.cs` builds JUnit XML (testsuite/testcase, skipped/failure/flaky
             properties+system-out per the spec's exact mapping) and a JSON report shaped like the API's
             FlowRunResponse/StepResultResponse (can't reference Yukti.Api itself — that would pull in
             ASP.NET Core for a console tool). `tests/Yukti.Cli.Tests`: 8/8 process-level tests running
             the actual built dll as a subprocess (never in-process), covering all 6 scenarios the spec
             names plus secret redaction — pass (exit 0, 3 testcases via `demo/api_chain.json`), failing
             assertion (exit 1), unregistered action (exit 2, step named on stderr), bad JSON (exit 2),
             missing file (exit 2), flaky (exit 0, flaky property set — induced via a stub call-counter
             and the engine's own real default retry policy, not a test-only module, since the CLI's
             fixed process composition root has no way to register one), secret env var never appearing
             in stdout/stderr/JUnit, and whole-run `--timeout` exceeded (exit 3). Day 2 exit check run
             verbatim plus the one necessary addition (`--var baseUrl=<stub>` — the demo flow genuinely
             needs a live server; no public API is safe to hardcode as a default without making the
             check itself internet-dependent and flaky) → exit 0, valid XML, exactly 3 testcases, 0
             failures.
Discovered:  Building the demo flow's real 3-step chain surfaced three genuine, pre-existing bugs —
             none specific to the CLI, all three silently broken on the API path too, just never
             exercised by an actual end-to-end chained flow before today:
             (1) **`ApiModule`'s `StepOutcome.Data` was an anonymous C# object**, not a
             `Dictionary<string,object?>`. `VariableStore`/`ResolvePath` only ever walks
             `IReadOnlyDictionary<string,object?>` — so `{{vars.<step>.body.field}}`-style chaining
             silently failed to resolve past the first hop (`vars.<step>` itself) for every API step
             anywhere in the system. The parsed response `body` itself was already correctly converted
             (`JsonPathEvaluator.ToPlainValue`) — only the outer wrapper was wrong. Fixed:
             `data`/`assertionResults` are now `Dictionary<string, object?>`.
             (2) **`StepResult.Duration` was hardcoded to `TimeSpan.Zero`** in `FlowEngine.Execute`,
             even though `dispatchStopwatch` was already measuring real elapsed time — just for
             telemetry, never applied to the result itself. Every report's timing data (JUnit `time`,
             JSON `durationMs`) was meaningless for every run that ever happened. One-line fix:
             `dispatchStopwatch.Elapsed` instead of `TimeSpan.Zero`.
             (3) **`Flow.DeclaredVariables` existed as a persisted domain property (with a real EF
             column going back through migration history) with no method anywhere to ever set it**, and
             `Flow.Publish` never checked it — so a flow had no way to declare "this variable is
             supplied at trigger time" (the CLI's `--var`, or the API's existing
             `TriggerFlowRunCommand.VariableOverrides` field) and have publish validation treat it as
             available instead of flagging every step that references it as "undeclared variable." Added
             `Flow.DeclareVariable(name, defaultValue)` (Draft-only, like `AddStep`), seeded into
             `Publish`'s `availableBindings`, carried through `CreateNewVersion`, and exposed via the
             flow file's `declaredVariables` field.
             SARVAGHNA's pre-commit hook will be exercised again on this PR (carrying forward the async
             fix discipline from D2-T5) — no new sync-over-async introduced here, confirmed by
             inspection before committing, not by waiting for the hook to catch it.
Scope guards: All three fixes above are minimal, targeted corrections to existing code the CLI's own
             demo flow genuinely needed to work — not a FlowEngine/RetryFlakeHandler redesign (both
             still explicitly protected), not scope creep. `Flow.DeclareVariable` is new surface, but
             it's filling in a already-scaffolded, already-persisted property that had no way to be set
             — completing an existing gap, not adding a new concept.
Exit check:  **Day 2 fully closed.** `dotnet build -warnaserror` → 0/0. `dotnet test` → 113/113 (was
             105/105, +8 new — Yukti.Cli.Tests). Day 2's own exit check command passes exactly as
             specified (plus the one noted, necessary `--var` addition).
Next:        Day 3 — API/Web/Logs module proofs + the UC-1 cross-module flow (P0-4). D3-T1 (API module)
             already ALREADY DONE per D1-T5's reconciliation; start at D3-T2 (Web module, currently zero
             dedicated tests despite the real Playwright implementation).

## 2026-10-13 ~00:30 IST — Day 3 begins (per owner's "Continuity Spec: Day 3 to Production" doc)
Done:        D3-T1 (new numbering — `demo/fixtures/FixtureApp`). The Continuity Spec renumbers Day 3
             around a real local fixture app instead of a public site, folding the old D3-T4 "unified
             report" task into D3-T4's own UC-1 exit criteria; `plan/PLAN.md` updated to match (old D3-T1
             "API module" kept as evidence, not re-listed as its own task — it's already proven and gets
             exercised again for free inside UC-1's API step). New project `demo/fixtures/FixtureApp`:
             `/login` (known `data-testid`s) → `POST /login` sets an `ISession`-backed order id, 302s to
             `/orders` → `/orders` shows the order id → `GET /api/orders/{id}` returns
             `{id, status:"confirmed"}`. Every request logged to a configurable path (default
             `demo/fixtures/app.log`) via a terminal middleware: timestamp, level, order id; `?fail=1`
             forces that line to ERROR. Hosted in-process on port 0 — `WebApplication`/Kestrel inside the
             same test process, no subprocess, no apphost/runtime-resolution step at all (sidesteps this
             machine's .NET-8-SDK-but-only-.NET-10-runtime quirk entirely for the test path).
Discovered:  Found while reading WebModule.cs/LogsModule.cs ahead of D3-T2/D3-T3 (not yet fixed, noted
             for those tasks): (1) WebModule is missing two actions the spec explicitly lists —
             `select` and `assertVisible` — only navigate/click/fill/assertText/screenshot/
             waitForSelector exist today. (2) A missing/not-found selector currently falls through to a
             bare `catch (Exception ex)` producing Playwright's own generic timeout message, not a
             named, typed "selector not found" error — the spec's D3-T2 bar wants the selector and step
             named explicitly. (3) `screenshot`'s `StepOutcome.Data` is an anonymous C# object (`new
             { path }`) — the identical bug class D2-T6 found and fixed in ApiModule; same fix needed
             here for consistency/chainability. (4) `LogsModule.checkRules` only ever reads an inline
             `logText` parameter — there is no way to point it at a real file on disk at all, and a
             missing/absent `logText` silently defaults to an empty string (0 lines scanned, "all rules
             within threshold" — PASSES), which is exactly backwards from the spec's explicit D3-T3
             requirement ("a missing log file fails the step clearly; it isn't treated as passing").
             Also no time-window filtering exists on `checkRules` at all (only `detectAnomalies` buckets
             by time, a different action). All four are real build items for D3-T2/D3-T3, not just
             proof-writing.
Scope guards: none
Exit check:  on track. `dotnet build -warnaserror` → 0/0. `dotnet test` → 119/119 (was 113/113, +6 new).
Next:        D3-T2 — Web module proof. Add `select`/`assertVisible` actions, a typed selector-not-found
             error, and the Data-shape fix, then prove all 8 actions against FixtureApp.

## 2026-10-13 01:29 IST — Day 3 (cont.)
Done:        D3-T2 Web module proof. Added the two missing WebModule actions — `select` (SelectOptionAsync)
             and `assertVisible` (IsVisibleAsync, never throws — missing and hidden both read as
             not-visible). Added a typed `SelectorNotFoundException` + `WithSelectorNotFound` helper
             wrapping every locator-based action (`click`, `fill`, `assertText`, `waitForSelector`) so a
             missing selector always fails with a message naming the selector, instead of a bare
             Playwright exception. Fixed `screenshot`'s `StepOutcome.Data` from an anonymous object to a
             `Dictionary<string, object?>` (anonymous objects silently don't support the
             `{{vars.step.field}}` variable-chaining path used everywhere else — same class of bug as
             D2-T6's ApiModule fix). Extended `FixtureAppHost.cs`'s login/orders HTML with a `login-region`
             `<select>` and a `hidden-field` (`display:none`) element so `select`/`assertVisible` have a
             real target to prove against.
Discovered:  `Microsoft.Playwright.TimeoutException` does not exist as a public type — it's internal to
             `Microsoft.Playwright.Core`. The real public exception every Playwright call can throw is
             `Microsoft.Playwright.PlaywrightException`. Confirmed by extracting type names directly from
             the installed DLL's bytes after a first guess failed to compile. Also: this machine's
             Playwright browser binaries weren't installed yet (`chromium_headless_shell` missing) — a
             one-time `playwright.ps1 install chromium` was needed before any WebModule test could run at
             all; not a code issue, just environment setup that will be needed on any fresh machine/CI
             runner using this module.
Scope guards: none
Exit check:  on track. `dotnet build -warnaserror` (tests/Yukti.Orchestration.Tests) → 0 warnings, 0
             errors. `dotnet test --filter "FullyQualifiedName~WebModule"` → 5/5 passing. Full solution
             `dotnet test` → 124/124 (17 Analyzers + 8 Cli + 8 Infrastructure + 91 Orchestration; was
             119/119, +5 new WebModule tests).
Next:        D3-T3 — Logs module proof. Add `logFile` param support to `LogsModule.checkRules`, make a
             missing log file fail the step clearly instead of silently passing with 0 matched lines, add
             time-window filtering (start/end), then prove pattern/severity/max-count/time-window against
             FixtureApp's real `app.log`.

## 2026-10-13 09:32 IST — Day 3 (close)
Done:        D3-T3, D3-T4, D3-T5 — Day 3 complete.
             D3-T3: LogsModule.checkRules gained logFile (missing file now FAILS naming the path — it
             previously read as "0 lines, all rules passed"), per-rule severity, since/until window, and
             matched lines in the step data. 6/6 LogsModuleTests against a real FixtureApp app.log.
             D3-T4: demo/uc1_flow.json runs Web login → web.getText (saveAs order) → API GET
             /api/orders/{{vars.order.text}} asserting status=confirmed → Logs no-ERROR check. Zero
             src/Yukti.Cli changes. Exit 0, 7/7. demo/uc1_flow_fail.json (?fail=1) → exit 1, JUnit
             names "No ERROR logged for this order" (logs.checkRules) with the ERROR line in the body.
             D3-T5: UC-1 × 50 → 50 passed, 0 failed, 0 flaky. Flake rate 0% (target < 2%).
             Also: applied SARVAGHNA's two PR #14 review notes (SelectorNotFoundException keeps the
             Playwright inner exception; assertVisible checks the CancellationToken).
Discovered:  1. No WebModule action returned page text as data — added web.getText ({text}).
             2. FixtureApp logged AFTER sending the response; FixtureAppTests papered over it with
                Task.Delay(50), and UC-1's Logs step could have raced the API step's log line (false
                pass on the ?fail=1 variant). Moved the write to Response.OnStarting; delays removed.
             3. A run-scoped time window can't live in LogsModule: FR-PLUGIN-07's contract test forbids
                instance state. Did not move it to a static to dodge the test. UC-1 is scoped by its
                per-login order id instead. Engine-level {{vars.run.startedAt}} → DISC-1.
             4. CLI JUnit failure body prints nested params via ToString() → DISC-2 (D3-T4 forbids CLI
                changes, so deferred).
             5. demo/fixtures/app.log was not gitignored — fixed (plus out.xml).
Scope guards: none
Exit check:  on track. dotnet build -warnaserror → 0/0. dotnet format --verify-no-changes → clean.
             dotnet test → 131/131 (17 Analyzers + 8 Cli + 8 Infrastructure + 98 Orchestration).
             `yukti run demo/uc1_flow.json --junit out.xml` → exit 0 locally; re-run on main after merge.
Next:        Day 4 D4-T1 — API testing UI + live runs (P0-6).

## 2026-10-14 09:23 IST — Day 4 (cont.)
Done:        D4-T6 Variable autocomplete from earlier steps' saveAs. Found in-progress, uncommitted
             in the working tree at session start (`variable-autocomplete-input.tsx`/`.test.tsx`
             untracked, `primitives.tsx`/`flow-detail-page.tsx`/`param-fields.tsx` modified) — not
             done yet: unticked in PLAN.md, GAP-MATRIX still said "Needs build", nothing committed,
             and the test run that was supposed to prove it hadn't actually executed. Completed it:
             a real ARIA 1.2 combobox (`VariableAutocompleteInput`) wired into every String param in
             `ParamFields`, suggesting only earlier steps' `saveAs` names (mirrors `Flow.Publish`'s
             own `availableBindings` rule), going one level deeper into the last run's real field
             shapes once one exists, and flagging unresolved references with `Flow.Publish`'s exact
             wording. Found and fixed a real bug while proving it: `getCandidates` required an exact
             match on the text before `.`, so suggestions never appeared for a still-partially-typed
             binding name even though the binding-name dropdown already resolved it uniquely — now
             resolves via `startsWith` the same way the binding step does.
Discovered:  1. `npm test -- autocomplete` (vitest's default `forks` pool) cannot start a worker on
             this machine at all — `[vitest-pool-runner]: Timeout waiting for worker to respond`,
             reproduced twice, 0 tests ever ran, silently reads as "no tests" rather than a failure.
             `--pool=threads` runs clean. Environment-specific (Windows), not a project bug, but the
             task's own `✔` command as written in PLAN.md will hang the same way for anyone else on
             this machine/profile — worth a `vitest.config.ts` default-pool fix as its own task
             rather than silently patched here as a side effect of D4-T6.
             2. `plan/STANDUP.md` had zero entries for D4-T1 through D4-T5 despite all five being
             ticked `[x]` in PLAN.md with detailed evidence — the required per-session append (§5)
             was skipped for whatever session(s) did that work. Not re-litigating or backfilling
             those five from the PLAN.md evidence after the fact (I wasn't there for them and
             reconstructing a session log after the fact would just be a plausible-sounding guess,
             not a true record) — flagging the gap here so it isn't silently normalized going
             forward; every session from here on appends its own entry regardless of how small.
Scope guards: none
Exit check:  on track. `npx vitest run --pool=threads` → 235/235 (was 227/227, +8 new). `npm run
             typecheck` and `npm run lint` clean. `npm run build` succeeds.
Next:        D4-T7 is not a real task — PLAN.md's Day 4 only defines D4-T1 through D4-T6; "D4-T7"
             exists only inside DISC-5's prose, which is itself about a Playwright e2e harness that
             doesn't exist in this repo yet (`playwright.config.ts` present, no `test:e2e` script, no
             `e2e/` directory) — a real multi-day undertaking (real backend+DB+frontend running
             together, fixtures, trace/screenshot capture), not something to wave through as already
             done. Awaiting the owner's actual Day 5 spec content and a decision on D4-T7's scope
             before proceeding.

## 2026-10-14 10:52 IST — Day 5 (open)
Done:        Received the owner's real Day 5 spec ("Yukti — Day 5 Spec: Sarvagna, the AI Layer").
             Superseded PLAN.md's old 7-task Day 5 skeleton with its T0-T9 breakdown (old->new mapping
             recorded inline in PLAN.md, same pattern Day 3 used). Found and resolved two real
             contradictions before writing any code — both logged as DISC-7/DISC-8:
             - AI vendor: GAP-MATRIX flagged this Day 1 as "blocks Day 5 start"; CLAUDE.md said
               Anthropic, the only real code is OpenAI-only, and the new spec silently assumed OpenAI
               without flagging it as a decision. Asked the owner directly instead of picking either
               way myself. Answer: keep OpenAI, add the abstraction. Recorded as
               `plan/adr/0003-ai-vendor-openai.md`; fixed CLAUDE.md/SUTRADHAR.md's §4 line to match
               (both said Anthropic, now say OpenAI).
             - "Day 4 autonomy policy" (Builder/Verifier/Reviewer, auto-merge, `SUTRADHAR.md §9`) the
               spec assumes is live: doesn't exist. SUTRADHAR.md has 8 sections, no §9, nothing about
               agent roles. Not building a speculative multi-agent framework to satisfy an assumption
               that was never actually met — proceeding under the model already working this session
               (build, real tests, commit, push, PR, merge via `gh`, STANDUP every step).
             D5-T1 done: `IAiProvider` abstraction (`src/Yukti.Application/Ai/IAiProvider.cs`) +
             `OpenAiProvider` (new, Infrastructure/Ai, key via `ICredentialResolver`/Vault, not
             config/env) + `StubAiProvider` (Infrastructure.InMemory/Ai). Deliberately a new, separate
             abstraction from the pre-existing `IAiAssistantService`/`OpenAiAssistantService` — those
             keep serving their three existing endpoints unchanged, nothing regressed.
Discovered:  1. (DISC-7, DISC-8 above.)
             2. My own first cut of D5-T1's "no OpenAI type outside the provider implementation"
             source-check test was too literal — it flagged my own explanatory doc comments in
             `IAiProvider.cs` and `OrchestrationTelemetry.cs` for mentioning the vendor by name in
             prose, not for any actual code coupling. Fixed by rewording those two comments instead of
             loosening the check — kept it a plain substring ban, consistent with this repo's existing
             `synthetic`-word-ban test (`ModuleRegistryTests.cs`), not something identifier-aware.
             3. `OrchestrationTelemetry.AiRequestDuration`/`AiRequestTimeout` already existed,
             declared since FR-OBS-02 but never emitted by anything (comment said so explicitly) — D5-T1
             is the first thing to actually populate them, via one new project reference
             (`Yukti.Infrastructure` -> `Yukti.Orchestration`, one-way, no cycle).
Scope guards: none
Exit check:  on track so far. D5-T1 real tests passing (see commit); full-suite `dotnet build
             -warnaserror`/`dotnet test` re-run before merge, not just the filtered subset.
Next:        D5-T2 (AI-off switch — never cut) once D5-T1 is merged; D5-T4/T5/T8 stay blocked until
             the owner hands over the actual OpenAI key + week's budget cap (asked, not yet received).

## 2026-10-14 11:40 IST — Day 5 (cont.)
Done:        D5-T1 merged to `main` — PR #23 (squashed), feature branch deleted. Went through three
             rounds of this repo's own Sarvaghna commit-hook review before the push was clean, each
             round catching something real, each fixed before the next push (not argued with or
             suppressed):
             1. Undisposed `HttpResponseMessage`; `choices[0]` indexed without checking the array
             wasn't empty (IndexOutOfRangeException on a content-filter refusal or malformed payload).
             2. Caller-initiated cancellation (the flow run itself stopping, not our own 15s deadline)
             was falling into the generic catch and getting wrapped into `AiUnavailableException`
             instead of propagating as `OperationCanceledException` — a caller couldn't tell "this was
             cancelled" from "the AI provider is down." Also: the outer `choices` lookup itself
             (not just `choices[0]`) could throw `KeyNotFoundException` on a payload missing the
             property entirely.
             3. The response body read used the caller's raw token instead of `timeoutCts.Token`, so a
             stalled body stream on an otherwise-successful response wouldn't respect
             `EffectiveTimeout` at all — only the header round-trip did. Restructured to one try/catch
             covering the whole exchange under the same deadline. Also hardened `StubAiProvider` to a
             `ConcurrentDictionary` against a future shared-instance registration.
             Final push: clean review, no further findings. 11/11 tests passing throughout every round.
Discovered:  None new — all caught by Sarvaghna's own review, logged above rather than separately.
Scope guards: none
Exit check:  on track. D5-T1 fully merged with real tests, not just locally green.
Next:        D5-T2 — the AI switch actually switches (never cut). Still waiting on the owner's OpenAI
             key + week's budget cap for D5-T4/T5/T8.

## 2026-10-14 11:55 IST — Day 5 (cont.)
Done:        D5-T2 merged to `main` — PR #25 (squashed). `Ai:Enabled` (default true) beats a configured
             key: false means `AiDisabledAssistantService` (new, holds no HttpClient, references no
             provider type) is registered instead of `OpenAiAssistantService`, and `IAiProvider` isn't
             registered at all — so zero outbound AI calls are possible by construction, not by
             convention. `AiDisabledException` -> 503 RFC 7807 via the existing global exception
             middleware, same pattern as `ForbiddenException`/`DomainException` — all three AI
             endpoints get this for free. `docs/ai-data.md` written, stating exactly what's sent today
             and what Sarvagna's pipeline will send later (registry context only).
             Insisted on the real `ai-off` CI job going green on this PR's own run before merging —
             not just local tests — per the task's own `✔` command. It took three pushes to get there,
             and both failures were real, not flaky:
             1. First run: 6 WebModuleTests failed — the job ran `dotnet test` without installing
             Playwright browsers first (copied `api-types-drift`'s job structure, which never runs
             `dotnet test` at all, so it never needed to).
             2. Second run: the AI-endpoint checks themselves never ran — `/auth/login` 500'd with
             `RedisConnectionException`. Found the real cause: Program.cs's rate limiter needs a real
             `IConnectionMultiplexer` for *any* rate-limited endpoint to respond, login included, and
             no CI job in this repo had ever hit a rate-limited endpoint before this one — `postgres`
             was the only service container anyone had needed so far. Added a `redis:7` service on
             port 6380, matching Program.cs's own default. Third run: green, all 5 checks.
Discovered:  No CI job before this one had ever exercised a rate-limited endpoint (login or otherwise)
             against the real API — the gap existed before D5-T2, this task just happened to be the
             first thing to call a rate-limited endpoint from CI and expose it. Worth keeping in mind
             for D6-T6 (CI gates a PR on a Yukti flow) and D7-T4 (`docker compose up` from clean clone)
             — either could hit the same missing-Redis issue in a different form if they also assume
             only Postgres is needed.
Scope guards: none
Exit check:  on track. D5-T1 and D5-T2 both merged with real, green CI evidence — not assumed.
Next:        D5-T3 — the planning pipeline (guardrail pre-check -> registry context -> versioned prompt
             -> provider call -> schema validation -> cost record). Still no OpenAI key/budget cap from
             the owner yet, so D5-T4/T5/T8 (the ones that need a live call) stay blocked; D5-T3's own
             skeleton and guardrail logic don't need one and can proceed.

## 2026-10-14 13:41 IST — Day 5 (cont.)
Done:        Owner said directly: "I don't have OpenAI Key. I have gemini API key." ADR-0003's vendor
             call (same day, a few hours earlier) assumed a working OpenAI key existed somewhere -
             checked again just now, it never did, for OpenAiAssistantService or anything else.
             Found the Gemini integration is already real and working in this exact repo:
             `sarvaghna/core/gemini-client.mjs`, the Sarvaghna crew's own caller (confirmed its
             `GEMINI_API_KEY` sits in `sarvaghna/.env.local`, gitignored - checked the var name only,
             never the value). Superseded ADR-0003 with `plan/adr/0004-ai-vendor-gemini.md`: built
             `GeminiProvider : IAiProvider` mirroring that script's exact request shape (generateContent
             REST endpoint, key in the query string, JSON-mode structured output) but through
             `ICredentialResolver`/Vault instead of a raw env read, and `AiUnavailableException` instead
             of that script's {ok, reason} style. `OpenAiProvider` stays in the codebase, unused but not
             deleted - a one-line DI change back if an OpenAI key ever shows up. `Program.cs` now
             registers `GeminiProvider` as the real provider. CLAUDE.md/SUTRADHAR.md/docs/ai-data.md
             updated to match (second time today for the AI vendor line - DISC-7 closed, both rounds).
             Merged: PR #27, all 5 CI checks green.
Discovered:  Real, reproduced bug while testing both providers side by side: OpenAiProviderTests' and
             GeminiProviderTests' timeout tests each attach a MeterListener to the same process-wide
             static OrchestrationTelemetry.Meter and assert an exact measurement count. Run in parallel
             (xUnit's default across test classes), each listener saw both tests' increments -
             `Assert.Single() Failure: The collection contained 2 matching items`, reproduced directly,
             not theorized from reading the code. Fixed with a shared xUnit collection
             (`AiProviderTelemetryCollection`) so the two classes serialize relative to each other without
             slowing down anything else in the assembly. Worth remembering for any future test that
             listens on this same static Meter.
Scope guards: none
Exit check:  on track. Real vendor, real key path, real tests - nothing assumed.
Next:        Still need the actual Gemini key value + the week's spend cap from the owner before
             D5-T4/T5/T8 (Analyst, Coder, eval harness) can start - asked, not yet received. D5-T3's own
             skeleton/guardrail work doesn't need the key and can proceed meanwhile.

## 2026-10-16 ~12:30 IST — Day 5 (cont.)
Done:        Owner handed down `plan/specs/SARVAGHNA-TOKEN-ECONOMY.md` — a cross-cutting constraint
             on every remaining Day 5 task (token accounting, zero-cost guardrails, bounded spend
             per call), not a new day. Saved to `plan/specs/`. Per its own instructions: closed
             D5-T3 first, checking the in-progress (uncommitted at session start) planning pipeline
             against §R1-R5 and fixing every real gap in the same pass rather than deferring:
             - §R1: `AiGuardrailPreCheck` now *rejects* (never truncates) a description over 4,000
               chars, and rejects outright when a description is nothing but a redacted secret
               (previously: silently truncated / let a secret-only string through). The engine now
               also rejects an empty/whitespace `PromptContext` ("no modules registered") before
               ever calling the provider — all guardrail rejects write zero cost records, proven.
             - §R3: the validation-retry ceiling moved from a hardcoded 2-attempt structure to
               `Ai:MaxValidationRetries` (config, default 1); the retry prompt's error list is
               capped at 20 entries and 2,000 characters so a pathological validator can't blow up
               the next attempt's own size.
             - §R4: new `IAiCapabilityOptionsProvider`/`ConfigurationAiCapabilityOptionsProvider`
               reads per-capability `MaxInputChars`/`MaxOutputTokens`/`Temperature` from
               `Ai:Capabilities:<name>` config; Analyst (6000/1024/0) and Coder (12000/2048/0)
               defaults seeded in `appsettings.json` straight from the spec's table. An assembled
               prompt (template + context + description) over the cap now fails fast instead of
               the old flat 4,000-char truncate. `AiPlanningRequest.MaxTokens`'s own literal
               default (2000) was removed — the engine sources it from capability options instead.
               A source-check test (same pattern as D5-T1's OpenAI-name ban) proves no `.cs` file
               outside `AiCapabilityOptions.cs` assigns a `MaxOutputTokens` literal.
             - §R5: cost recording changed from one row per `PlanAsync` call to one row per
               provider *attempt* — a run that retries once now writes 2 `AiCostRecordData` rows
               (`Attempt`, `Succeeded`), not 1, each with its own exact token count. Proven directly
               against the stub provider's deterministic counts, not estimated.
             Avoided adding a NuGet package for config binding (`Microsoft.Extensions.Configuration.
             Binder` isn't referenced anywhere in `Yukti.Infrastructure`) — per CLAUDE.md §4 and the
             spec's own instruction #6, that needs an ADR + owner approval, not a one-off import for
             three `GetValue<T>` calls; read config with plain indexers + manual `TryParse`, the
             same style `GeminiProvider`/`FilePromptLoader` already use.
             Deliberately NOT built yet, logged rather than silently skipped: R2's Analyst/Coder
             prompt-assembly tests (no real fixtures exist until D5-T4/T5) and R7's full
             reflection-based "no live call in any test" guard (nothing to guard yet with zero real
             capabilities wired up) — both tracked against D5-T4 onward per the spec's own per-task
             table, not dropped.
             Added `DISC-TOKEN-1` (Phase B, the Node CI agent token economy) to PLAN.md
             `## DISCOVERED`, unchecked — **not started, needs owner approval** per the spec's own
             §Phase B / Q1 gate. Recorded the one baseline metric measurable without a live call
             (0 live Gemini calls in `dotnet test`, confirmed) into `plan/AI-BUDGET.md`; the
             Sarvaghna-CI-ledger-dependent rows are marked "not measured" rather than invented,
             since `sarvaghna/ledger/` doesn't exist until DISC-TOKEN-1 is approved and built.
Discovered:  None new beyond what's logged above (the config-binder-package avoidance and the
             R2/R7 scoping decisions).
Scope guards: Part B (Node CI agent token economy) not started — owner approval pending (DISC-TOKEN-1).
Exit check:  on track. `dotnet build -warnaserror` → 0/0. `dotnet test` → 183/184 (was ~162/163
             before today's new AI tests; the 1 failure is the pre-existing Docker/Testcontainers
             gap on this machine, unrelated to AI, confirmed by checking `docker info` directly).
             `dotnet format --verify-no-changes` → clean. D5-T3's own filter
             (`~Planning|~AiGuardrail|~AiCapabilityOptions|~FilePromptLoader`) → 30/30 passing.
Next:        Push `feature/D5-T3-planning-pipeline`, open the PR, then D5-T4 (Analyst) — applying
             §R2 (Analyst-specific minimal context) and §R4 as part of that task's own definition of
             done, per the spec's token-rules table. Asking the owner now about DISC-TOKEN-1
             (Phase B approval) and Q4 (confirm Gemini per-1M-token pricing from the billing
             console, blocking D5-T9's cost accuracy) before going further than D5-T4/T5/T6.

## 2026-10-16 ~13:15 IST — Day 5 (cont.)
Done:        D5-T3 PR opened (#30), plus a same-PR follow-up fixing a Sarvaghna review finding
             (secret-detection regexes had no match timeout — ReDoS hardening, not a blocker but
             cheap and real). Owner answered both outstanding questions: DISC-TOKEN-1 (Phase B)
             **approved** — recorded in PLAN.md, scheduled after D5-T7/T8 per the spec's own
             §Timeline rather than jumped ahead of the AI-layer work; Gemini pricing (Q4) deferred
             to right before D5-T9, which is the first task that actually needs it.
             D5-T4 (Analyst) done on a stacked branch (`feature/D5-T4-analyst`, based on
             `feature/D5-T3-planning-pipeline` since D5-T3 isn't merged yet). `ParsedIntent`/
             `IAnalyst` in Application; `Analyst` in Infrastructure (needs `IModuleRegistry`,
             which only Infrastructure can see without creating a circular project reference —
             `Yukti.Orchestration` already depends on `Yukti.Application`). Applied §R2/§R4 as
             part of D5-T4 itself, not deferred: the registry context sent to the provider is kind
             + action name + one-line description only, proven to exclude every `ParamSpec` field
             even against a fixture module that has them; minified JSON with a stable key order
             (byte-identical across repeat calls, proven); assembled-prompt-size-within-cap proven
             against Analyst's configured `MaxInputChars` (6000, seeded in `appsettings.json`
             during D5-T3). Added the real `Sarvagna/Prompts/analyst.v1.md` and wired it to
             actually deploy (`Content` item in `Yukti.Api.csproj` copying the repo-root
             `Sarvagna/Prompts` folder to the API's output directory) — D5-T3 never needed this
             since its own tests only ever used a fixture `IPromptLoader` double.
Discovered:  1. `Yukti.Infrastructure.Tests` had no project reference to
             `Yukti.Infrastructure.InMemory` at all (where `StubAiProvider` lives) — added it;
             additive, not a layering change (Infrastructure.InMemory is a sibling implementation
             project, same category as the Infrastructure→Orchestration reference D5-T1 added).
             2. `StubAiProvider` had no way to inspect what was actually sent to it — every prior
             AI test either didn't need to (D5-T1/T2 test the provider contract itself) or used a
             hand-rolled fake with its own `Requests` list (D5-T3's `AiPlanningEngineTests`). Added
             `LastRequest` (additive, all 4 existing `StubAiProviderTests` still pass unchanged) so
             Analyst's (and later Coder's) own §R2 prompt-shape tests have something real to assert
             against instead of trusting the implementation by inspection alone.
Scope guards: none
Exit check:  on track. `dotnet build -warnaserror` → 0/0. D5-T4 filter → 8/8. Full suite →
             191/192 (was 183/184 before D5-T3's regex-timeout follow-up and D5-T4's 8 new tests;
             the 1 failure is the same pre-existing Docker/Testcontainers gap on this machine,
             confirmed earlier via `docker info`, unrelated to AI work).
             `dotnet format --verify-no-changes` → clean.
Next:        Push `feature/D5-T4-analyst`, open its PR (based on `feature/D5-T3-planning-pipeline`,
             stacked since D5-T3 hasn't merged), then D5-T5 (Coder) — applying §R2 (Coder gets full
             `ActionSchema` for only the candidate modules, not the whole registry) and §R3 (the
             shared retry-cap machinery D5-T3 already built) as part of that task's own definition
             of done.
