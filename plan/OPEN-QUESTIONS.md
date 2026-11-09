# Open questions — across every Module Depth implementation

Living doc, not a log — updated in place as items resolve, new modules add their own. One place
to review every outstanding decision across the current specs instead of being asked piecemeal.
Each item names where it came from and what happens if you say nothing.

Legend:
- 🔴 **Blocking** — real work is stalled or will ship incomplete until you answer.
- 🟡 **Defaulted** — I picked a reasonable default and I'm proceeding on it. Says what to tell me if you want the other answer.
- 🔵 **Sutradhar's own** — mine to investigate/decide, listed here only for visibility, not asking you anything.

---

## API Testing Module (`plan/specs/API-TESTING-MODULE.md`)

Module gate: **green** (PR #73). P0-complete. Everything below is P1 polish.

1. 🔴 **Postman import test is blocked on a real collection.** Can't produce the gap report your
   work order asked for until you share one. *Needs: a Postman Collection v2.1 export.*
2. 🟡 **Soft-assertion evaluation order.** Your spec's test (b) — "mixed hard and soft, hard
   fails first → evaluation stops there" — implies hard-assertion failure should stop evaluation.
   The current `AssertionEvaluator` never fails fast; it always evaluates every assertion in the
   array and collects every result (the existing, already-tested T7 design). Building test (b)
   literally means changing that for hard assertions while keeping soft ones non-stopping — a
   real behavior change, not just a new flag. *Default if you don't weigh in: I'll build it as a
   genuine behavior change (hard assertions stop evaluation at the first hard failure; soft ones
   never do), since that's what your spec's test explicitly asks for — but flagging it here since
   it changes existing, tested behavior rather than purely adding something new.*
   **2026-10-29:** the Web module's own WEB-T7 also has a deferred "soft assertions (P1)" item
   with the same corrected semantics. Worth building both from one shared design when either gets
   picked up, not two independent ones — "soft assertion" means the same thing in both modules.
3. 🔵 DISC-20 (Request Templates frontend page) — **closed 2026-10-31**, `/request-templates` in
   `apps/yukti-gui`. See `PLAN.md`'s own closure note for the full build. One real follow-up it
   created, not resolved here: the new `GET /api/v1/request-templates/` endpoint almost certainly
   drifted `schema.ts` again (same mechanism PR #85 already fixed once) — regenerating it needs
   a live API, which needs Docker/Postgres this environment doesn't have. *Needs: either run
   `npm run generate:api` yourself against a locally-running API, or expect `api-types-drift` to
   fail on this PR's first real CI run and let me fix it in a tiny follow-up once that happens.*

---

## Web UI Testing Module (`plan/specs/WEB-TESTING-MODULE.md`)

WEB-T1 (shared vocabulary), WEB-T2 (Object Repository), WEB-T3 (locator resolution), WEB-T4
(migrate the existing module), WEB-T5 (waits), WEB-T6 (full action set), WEB-T7 (assertions),
WEB-T8 (browser and context) all closed 2026-10-29. **That's the whole P0 scope.** Gate 1 is
already proven (WEB-T2's own test). Gate 2 (item 2 below) is the only thing left before this
module is done end to end.

1. 🟡 **"Confirm Playwright stays" (the spec's own blocking question).** Treating this as already
   settled, not re-asking: `CLAUDE.md` §4 already names `Microsoft.Playwright (.NET)` as the web
   automation stack, and ADR-0001 already decided "stay on .NET" generally. Recorded formally as
   `ADR-0008` rather than escalating back to you. *Tell me if you actually want WebDriver
   reconsidered — I'll treat silence as confirmation.*
2. 🔴 **Gate 2 needs a real Selenium or Playwright suite (10+ tests) to port.** This is the only
   honest measure of "Selenium-class" per the spec's own words — an invented suite proves nothing.
   **This is now the only thing blocking the Web UI Testing Module's completion** — WEB-T1 through
   WEB-T8 (all P0) are closed. *Needs: a real test suite to port, or a decision to skip/weaken
   Gate 2.*
3. 🟡 **`executeScript` (WEB-T6) — build now or defer?** Defaulting to the spec's own
   recommendation: **defer**. It's marked P1, it's the one genuinely dangerous action (arbitrary
   JS in the page context, a possible SSRF bypass via in-page `fetch`), and the spec explicitly
   says "ship the module without it first and see whether anyone actually needs it." *Tell me if
   you want it built in this pass anyway.*
4. 🟡 **Firefox and WebKit (WEB-T8) — actually needed, or Chromium enough for now?** Built as
   defaulted: `BrowserConfig.Engine` lets a flow request any of the three, `WebModule.Setup`
   switches on it for real (`IPlaywright.Firefox`/`.Webkit`/`.Chromium`), but only Chromium is
   exercised in `WebBrowserContextTests` and CI — this environment has no proof the other two
   engines' browser binaries are even installed, so claiming "tested" for them would be dishonest.
   *Tell me if Firefox/WebKit need real CI coverage (and binaries) now, not just a config option.*
5. 🔵 **Resolved, not asked:** the existing artifact-storage machinery reuses cleanly for a
   second artifact kind — `ObjectRepository` uses the exact same EF patterns (`OwnsMany`/jsonb,
   `FamilyId`+`Version`+`Status`+`TenantId`) `Flow` already does. No storage work was needed
   first. Built as WEB-T2, 2026-10-29.
6. 🟡 **`RepositoryRef` has no `RepositoryId`.** The spec's own domain model defines
   `RepositoryRef(Page, Element)` — a step names "Checkout › submitButton," never which Object
   Repository artifact that page lives in. *Default, already built:* resolution and the
   existence check both search every repository the tenant owns for a matching page/element,
   succeeding on the first match. This matters once a tenant has more than one repository with
   overlapping page names — ambiguous today by design, not by oversight. *Tell me if you'd
   rather `RepositoryRef` carry an explicit `RepositoryId` (a real, if small, deviation from the
   spec's own listed domain model) — I haven't done that without checking first.*
7. 🔵 DISC-22/DISC-23 (`plan/PLAN.md`) — two real, pre-existing gaps found while building WEB-T2:
   `RequestTemplate` has no DB-level RLS at all (app-level `TenantId` filter only), and API-T9's
   `IRequestTemplateExistsChecker` was never actually wired into the real publish command handler
   despite being built and tested. The second is already fixed (same change that wired WEB-T2's
   own checker in). The first is a real security-posture question for you, not something I fixed
   silently on a different module's table — see DISC-22 for the two ways to close it.
8. 🟡 **`Text` strategy has no exact/partial flag.** WEB-T3's own checklist asks for "Text (exact
   and partial)," but `LocatorSpec` (WEB-T1, already shipped) has no such flag — only
   `Strategy`/`Value`/`Name`. *Default, already built:* `Text` always uses Playwright's own
   default (case-insensitive substring/partial match); exact-text matching isn't independently
   selectable. *Tell me if you want `LocatorSpec` to carry an `Exact: bool?` (a small addition to
   an already-shipped WEB-T1 type) rather than leaving `Text` partial-only.*
9. 🔵 **Not executed, flagged:** WEB-T4's 50-run flake benchmark (`scripts/flake_run.py`) needs a
   Python interpreter, which isn't available in this environment (`python3`/`python` both fail to
   launch). Everything else on the task's own checklist ran and passed for real. *Needs: either a
   Python interpreter in this environment, or you run the benchmark yourself and report the
   number back.*
10. 🟡 **`NetworkIdle`'s quiet period isn't actually tunable.** The domain type (WEB-T1) carries
    a `Quiet: TimeSpan`, but Playwright's own `WaitForLoadStateAsync(NetworkIdle)` — the real
    mechanism `PlaywrightWaitResolver` uses — has a fixed ~500ms threshold with no way to
    configure it. *Default, already built:* `Quiet` is silently ignored; the condition's own
    `timeoutMs` param (from `waitFor`) is used as the overall wait budget instead. *Tell me if
    you want this to actually fail loudly when `Quiet` is set to something other than the
    implicit default, rather than quietly ignoring the field.*
11. 🔵 DISC-24 (`plan/PLAN.md`) — found while building WEB-T5, unrelated to it:
    `Flow.CreateNewVersion` never copied `ContinueOnFailure` forward to the new Draft version (only
    `ProjectId`/`Description` were). A pre-existing, unrelated gap, not fixed here — flagged for
    you to say whether it's intentional or a real bug.
12. 🟡 **`switchToFrame` only takes a plain CSS selector, not a full `LocatorRef`,** and nothing
    the richer Locator-object path resolves (an inline locator object, or an Object Repository
    reference) is frame-aware once you've switched into one — `IFrame` and `IPage` don't share a
    common locator-building interface in Playwright's own public API, so extending
    `PlaywrightLocatorResolver`/`ResolveLocatorTarget` to work against either would need a real
    abstraction layer, not a small patch. *Default, already built:* frame-awareness only covers
    the legacy plain-string selector path. *Tell me if frame-scoped Object-Repository references
    are something you actually need soon — it changes how big a job this is.*
13. 🔵 **Resolved, not asked:** a locator-resolution failure now attaches a screenshot too
    (`WebModule.Run`'s `SelectorNotFoundException` catch, best-effort via `ScreenshotStorage`) —
    closed the same session it was flagged, since it directly matched WEB-T6's own checklist
    wording and was a contained addition.
14. 🔵 **Caught and corrected, not asked:** WEB-T8's storage-state reuse (`saveStorageState`/
    `loadStorageState`) was first built as a DB-backed EF aggregate injected straight into
    `WebModule`'s constructor. `WebModule` is `AddSingleton` in `Program.cs`; anything depending on
    `YuktiDbContext` is `AddDbContext` = Scoped — a real captive-dependency bug that `dotnet ef
    migrations remove` itself flagged ("Cannot consume scoped service... from singleton..."), not
    something a test caught (every test constructs `WebModule` by hand, bypassing DI validation).
    Reverted the DB aggregate/repository/migration entirely and rebuilt storage-state persistence
    as tenant-scoped files (`BrowserStorageStateStorage.cs`), mirroring the already-shipped
    `ScreenshotStorage` pattern — no DI-scoped dependency needed at all. No behavior change from
    what WEB-T8's checklist asks for; flagged here only because it's a real bug that nearly shipped.
15. 🟡 **Storage-state redaction only scans for a literal cookie/field named `password`,** not the
    broader fragment list `SecretRedactor` (used elsewhere for logs) matches against. *Default,
    already built:* narrow, name-exact match — storage state is cookies/origin-storage key-value
    pairs, not free text, so a fragment scan risks false positives on legitimate non-secret values
    that happen to contain a matched substring. *Tell me if you want the broader `SecretRedactor`
    fragment list applied here too.*
16. 🔵 **Not surfaced, flagged:** WEB-T8's trace/video capture-on-failure (`Tracing.StartAsync`/
    `StopAsync`, context-level video recording) writes files to disk via `ScreenshotStorage`'s
    sibling layout, but nothing in `StepResult`/the unified report model carries a path/URL to
    them yet — they're on disk, provably produced (`WebBrowserContextTests` checks for the files),
    but not yet wired into anything a user would actually open from the report UI. Out of WEB-T8's
    own scope (which only asked for capture-on-failure to exist); flagged as a real follow-up gap.

---

## Mobile Testing Module (`plan/specs/MOBILE-TESTING-MODULE.md`)

MOB-T1 (Appium client) closed 2026-11-07, as a completion pass on an already-existing,
already-wired-in (but untested) `MobileModule` — see `plan/PLAN.md`'s own DISC-25 entry for the
full accounting of what existed vs. what MOB-T1 actually added.

1. 🟡 **iOS/Mac — kept open, deliberately, per your own instruction.** The spec's own "Blocking
   iOS" question (Mac available, or a device farm?) was explicitly left unanswered: *"Why this
   should be an environment dependent?? ... I think this should run on the simulator only for
   now ... As of now android would do, for XCode we shall see."* Noted plainly, not glossed over:
   Appium's iOS support (XCUITest) is a real Apple platform constraint, not a Yukti design
   choice — Xcode runs only on macOS, so iOS automation (simulator or real device) needs a Mac
   somewhere in the chain regardless of how Yukti itself is built. That's unrelated to whether
   Yukti's own client code is cross-platform (it is — `MobileModule` doesn't care what OS it
   runs on; `IOSDriver`/`AndroidDriver` are just classes). *Needs: nothing right now — correctly
   left open. Revisit when iOS work is actually requested (the spec's own escalation trigger #3:
   "iOS work is requested and no Mac is available — this is a hard stop, not a workaround").*
2. 🟡 **Simulator/emulator only, not real devices, for now.** Per your own instruction: *"I think
   this should run on the simulator only for now. Thats how we do it."* `MobileModule` itself
   doesn't distinguish emulator/simulator from a real device — that's purely a matter of which
   `deviceName`/capabilities a `DeviceProfile` names — so nothing in MOB-T1 enforces or needs to
   enforce this; it's a policy for which profiles get created later (MOB-T2), not a code
   constraint today.
3. 🔵 **Finer connection-failure categories ("no device matching profile", "app not found at")
   are not distinguishable from a generic "Appium rejected the session" without a real Appium
   server's actual error text to pattern-match against** — none is available in this
   environment (confirmed: no live Appium/device, per your own "build against mocks for now").
   *Default, already built:* the real Appium error message and the profile name are always
   included in the fallback case, so nothing is hidden — it's just not sub-categorized further.
   *Needs: a real Appium server (local, or a device farm trial) to capture real error text
   against, so the classifier's fallback case can be split into the spec's finer categories with
   confidence instead of guessing at message patterns that might not match what Appium actually
   says.*
4. 🔵 DISC-25 (`plan/PLAN.md`) — the pre-existing `MobileModule`/dependency discovery itself:
   `Appium.WebDriver` was already a dependency (6.0.1, upgraded to 8.2.0 this task, ADR-0009),
   and the module was already live in the real app with zero tests. Not a question, just
   flagged here so it's visible alongside everything else.
5. 🔵 **MOB-T3's own "flow A writes state, flow B with Fast reset sees a clean app" isolation
   test couldn't be built — no live Appium server/device in this environment means there's
   nowhere to actually write state to.** What's proven instead: each `ResetStrategy` maps to the
   correct Appium `noReset`/`fullReset` capability (`MobileModule.ResetCapabilitiesFor`, tested
   directly). *Needs: a real Appium server + emulator/device to run `demo/mobile_login.json`
   (or an equivalent two-flow isolation probe) against, the same resource every item above this
   one is also waiting on.*
6. 🔵 **MOB-T4's `AppiumLocatorResolver.ResolveAsync` — the full primary/fallback/`Within`/`Nth`
   orchestration against a real driver — isn't exercised end-to-end here, the same standing
   constraint as items 1-5.** What's proven instead: `Build`, the pure strategy-to-`By` mapping
   every strategy (and every fallback) goes through, is directly unit-tested for all 7 strategies
   plus the Web-only-strategy rejection; the Object Repository integration is proven at the
   orchestration level (`FlowEngine.ResolveLocatorRef`, engine-neutral, exercised with a real
   repository entry and `EchoModule` as a param-echoing probe, no live driver needed). *Needs: the
   same real Appium server + emulator/device as items 1, 3 and 5.*
7. 🔵 **MOB-T5's gestures — the driver-bound half of every `Gesture` (everything in
   `AppiumGestureExecutor` that actually moves a pointer: `Tap`, `DoubleTap`, `LongPress`,
   `SwipeBetween`, `Pinch`, `DragAndDrop`) isn't exercised end-to-end here, the same standing
   constraint as items 1, 3, 5 and 6.** What's proven instead: the full pure math/control-flow
   core these all build on — relative-to-absolute coordinate conversion, direction-to-points
   (including clamping at the edges), the `ScrollTo` bounded-attempts loop (including
   cancellation, so it provably doesn't scroll forever), and pinch-scale validation — plus the
   action schema shape for all 6 new actions. *Needs: the same real Appium server + emulator/
   device as every item above.*
8. 🔵 **MOB-T6's device commands — every driver-bound call in `AppiumDeviceCommandExecutor`
   isn't exercised end-to-end here, the same standing constraint as every item above.** What's
   proven instead: the full `IsSupported` decision matrix for every command/platform pair the
   checklist names (including that a "not supported" case always names a real reason, never a
   bare false). `SwitchContext`'s own checklist line — "with a test against a hybrid fixture
   app" — additionally needs a real hybrid app with an actual webview context to switch into, not
   just a live Appium server; the same resource gap as items 1, 3, 5, 6 and 7, one level further
   (a fixture *app*, not just a server/device).
9. 🔵 **MOB-T7's assertions — none of the 11 new/updated actions' actual comparison logic
   against a real element is exercisable end-to-end here, the same standing constraint as every
   item above.** What's proven instead: every new action's schema shape, matching the
   checklist's own list exactly. Soft assertions (P1, the checklist's own lowest-priority line)
   not built — every P0 item in MOB-T7 took priority first, consistent with the slip policy.
10. 🔵 **MOB-T8's two P1 checklist items were already real before this task touched any
    production code** (`browserName` already flows through `BuildProfile`'s existing
    `ExtraCapabilities` passthrough; `switchContext` to Webview already works, built in MOB-T6) —
    proven by a new test, not re-built. The actual browser launch and webview context switch
    still need a live Appium server to exercise end-to-end, same as every item above.
11. 🟡 **DISC-26 (soft assertions) — not a "needs a live server" gap, a real missing feature
    across every module.** See `plan/PLAN.md`'s DISC-26 for the full accounting; needs an owner
    decision before it's buildable anywhere, Mobile included. Design proposed:
    `plan/adr/0010-soft-assertions.md` (PR #98, **Accepted — owner approved 2026-11-09**).

---

## How to use this doc

Answer inline (quote the number, e.g. "Web-2: here's the suite ..."), or just tell me "proceed
on all defaults" and I'll keep moving without re-asking. I'll update this file as items resolve
and add a new module's section when its spec arrives.
