# ADR-0005: A real Playwright e2e harness, with a fixture AI provider for determinism

Status: Accepted
Date: 2026-10-18
Owner approval: explicit — asked directly whether to scope D5-T7 to component-level tests (the
D4-T4 precedent) or build the real e2e harness; the owner chose "Build the real e2e harness now."

## Context

D5-T7's acceptance command includes `npm run test:e2e -- ai-generation`, and the Day 5 exit check
asks for English → draft → publish → run proven end to end. No e2e harness existed (DISC-5, open
since Day 4): `apps/yukti-gui/playwright.config.ts` was present and imported `@playwright/test`,
but that package was never installed, there was no `test:e2e` script and no `e2e/` directory — the
config could never have run.

Two constraints shape the harness:

1. **Zero live AI calls in any test** (token-economy spec §R7, CLAUDE.md principle 4). An e2e run
   boots the real API process, so the usual in-test `StubAiProvider.SetFixture` isn't reachable —
   the test and the API are separate processes.
2. **This development machine has no Docker**, so the stack (Postgres + Redis + API + Vite) can't
   be brought up locally. CI can: every existing integration job already runs Postgres/Redis as
   GitHub Actions service containers.

## Decision

1. Add `@playwright/test` **1.63.0** (pinned exactly — the newest release at least two weeks old
   on 2026-10-18) as a devDependency of `apps/yukti-gui`, plus `"test:e2e"`. Chromium only by
   default; the config keeps its existing project list, the script selects Chromium.
2. Add an `Ai:Provider` switch to `Program.cs`. Default (unset) stays `GeminiProvider` — nothing
   changes for any real deployment. `Ai:Provider=fixture` registers the existing, already-tested
   `StubAiProvider`, loaded at startup with one canned response per capability from
   `Ai:FixtureDirectory/<capability>.json`. Startup logs a warning naming it, and it still sits
   *inside* the `Ai:Enabled` check, so the D5-T2 switch keeps winning. No new provider class.
3. Accessibility is checked in the browser with the already-installed `axe-core` (injected via
   `page.addScriptTag`) — no `@axe-core/playwright` dependency.
4. A new `e2e` CI job: Postgres + Redis services, migrate, boot the real API with the fixture
   provider, and run `npm run test:e2e`. Traces/screenshots are uploaded on failure.

## Consequences

- The e2e suite proves wiring the unit tests can't: the HTTP endpoint, DI composition, EF
  persistence of `flow_steps.ai_attribution`, prompt files actually deployed next to the API
  binary (D5-T4's `Content` item, never exercised by a real process before), the browser UI, and a
  real run of the generated flow.
- It does **not** prove model quality — the fixture answers are fixed. That is D5-T8's job (the
  eval harness), which is the only place live calls are allowed.
- The ambiguous-request path is not covered in e2e: one fixture per capability means the Analyst
  always answers the same way. It's covered at the component level (`ai-generation-panel.test.tsx`)
  and in `GenerateFlowCommandHandlerTests`. Varying fixtures per request is a deliberate non-goal.
- The harness can only be run where Docker (or equivalent Postgres/Redis) is available — CI today,
  not this development machine.
