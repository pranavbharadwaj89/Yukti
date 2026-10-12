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
