# Yukti — Mobile Testing Module Spec

Appium-based, native and hybrid apps · Oct 27, 2026

> **Deferral note resolved 2026-11-07:** owner confirmed, explicitly (AskUserQuestion), that a
> real design partner has asked for mobile testing — unblocking this spec's own first Open
> Question ("Blocking everything — you") before any task started. Logged as a SCOPE GUARD
> refusal first (`plan/STANDUP.md`, 2026-11-01), then un-blocked once the owner answered.

## Scope and how to use this spec

**For Sutradhar.** Self-contained spec for a **new** module. Work tasks in order. Standard autonomy policy applies.

**Governing rule:** a new `IAutomationModule` implementation, registered like any other. Nothing in the contract, engine, registry, UI or report changes. The schema-driven editor must render mobile steps with **zero** UI changes — if it can't, the design is wrong.

### Read this before starting: the deferral note

This is the largest of the four modules and the only one with real physical infrastructure attached. It is also the one most likely to be wanted by only some teams.

**If no design partner has asked for mobile testing, do not build this yet.** The module contract means it can be added whenever it's genuinely wanted, at no architectural cost for waiting. Building it speculatively spends 2–3 weeks that the hosting path or the other three modules would spend better.

This spec exists so that *when* someone asks, the work is already thought through — not as a prompt to start.

### Hard dependencies

- [x] **Blocking: WEB-T1**, the shared UI vocabulary from the Web module spec. This module implements that same vocabulary against Appium. If the vocabulary still contains Playwright concepts, this module cannot be built — that's exactly why the Web spec enforces engine-neutrality in CI.
  **Verified 2026-11-07, before MOB-T1 started:** `Yukti.Domain.UiVocabulary` has zero Playwright
  references (one grep hit, a comment explicitly disclaiming them) and `Yukti.Domain.csproj` has
  zero `PackageReference`s at all — structurally impossible for a Playwright type to leak in, not
  just conventionally avoided. `LocatorStrategy` already carries every mobile case the domain
  model below needs (`AccessibilityId`, `ResourceId`, `IosPredicate`, `IosClassChain`,
  `ClassName`) alongside the shared `Text`/`XPath`. No fix needed in the Web module first.
- [x] **Blocking: WEB-T2**, the Object Repository. Mobile locators live in the same repository, so a single entry can hold both web and mobile strategies for the same logical element.
  Exists (`Yukti.Domain.UiVocabulary.ObjectRepository`, closed as part of the Web module). Not yet
  exercised by Mobile — that's MOB-T4's job (locators), not MOB-T1's.
- [ ] **Blocking: the `{{secrets}}` pipeline**, for device-farm credentials.
  The real mechanism is `Yukti.Contracts.ICredentialResolver` (already used by `GeminiProvider`
  and proven in `FakeModules.cs`'s own FR-LOG-04 fixture) — the spec's own "SecretRef" under its
  real name; no `SecretRef` type exists anywhere in this codebase. MOB-T1 uses it for
  `farmCredentialRef`; see that task's own closure note.
- [ ] **Blocking: an Appium server**, run by the customer. Yukti does not bundle one at P0.
  Not available in this environment. MOB-T1 was built and tested without one (owner's own call:
  "build against mocks for now") — see MOB-T1's closure note for exactly what that does and
  doesn't prove.
- [ ] **For iOS only: a Mac.** This is Apple's constraint, not Yukti's. Android works anywhere.
  Kept explicitly open per the owner's own instruction — see `plan/OPEN-QUESTIONS.md`.

### What this module is, in one sentence

Yukti becomes an **Appium client**: it speaks W3C WebDriver over HTTP to an Appium server that the customer runs, which in turn drives UiAutomator2 on Android or XCUITest on iOS. Yukti never touches a device directly, and never needs the app's source code modified.

## Architecture — where Yukti sits

[embedded content: Appium's three tiers · Yukti builds only the top one]

### What this means practically

**Yukti builds tier 1 only.** That's a genuinely smaller job than it first appears: no device drivers, no platform SDKs, no app instrumentation. The customer already has, or sets up, the Appium server — and most teams doing mobile testing already have one.

**The app under test is unmodified.** Appium delegates to Apple's and Google's own test frameworks, so Yukti tests the exact `.apk` or `.ipa` that ships. No SDK injection, no special build.

**The same wire protocol as the web.** Appium extends W3C WebDriver, which is why the shared UI vocabulary works across both: a `click` with a locator means the same thing, and only the translation differs. That's the whole reason the Web spec insists its vocabulary layer stays engine-neutral.

### Where the hard parts actually are

Not in the code. The code is a WebDriver client, which is well-trodden. The difficulty is:

| Hard part | Why |
| --- | --- |
| **Device availability** | A flow needs a device that exists, is awake, and isn't in use. The failure mode when one isn't available must be clear, not a bare timeout |
| **iOS needs a Mac** | Non-negotiable, Apple's rule. It shapes CI and anyone's self-hosting plan |
| **Emulator startup time** | Cold-starting an Android emulator takes minutes. Session reuse matters far more here than on the web |
| **App state between tests** | A phone app keeps state. Without reset, test two inherits test one's mess — the mobile equivalent of the transaction-rollback problem in the DB module |
| **Flakiness** | Mobile automation is inherently flakier than web. The 2% target may need an honest, separately-stated mobile number |

## Domain model

> **Built 2026-11-07, MOB-T1:** `Yukti.Domain.MobileTesting.DeviceProfile` (+ `MobilePlatform`,
> `ResetStrategy`, `DeviceProfileId`) matches this shape, with `FarmCredentialRef: string?` in
> place of `SecretRef? FarmCredential` (same mechanism, real name — see above). `Gesture` and
> `DeviceCommand` are not built yet — MOB-T5/MOB-T6's own job.
>
> **MOB-T2, 2026-11-08:** `DeviceProfile` is now the real, persisted `AggregateRoot<DeviceProfileId>`
> this section originally described — admin-configured, tenant-scoped, versioned, looked-up-by-name,
> with its own repository, summary query, "test profile" admin action and publish validation. Also
> added `DeviceEndpointKind { Local, SelfHosted, Farm }`, this task's own "records whether its
> endpoint is local, self-hosted or a named device farm" field, not yet in the shape below when
> MOB-T1 wrote it.

```csharp
// Admin-configured, tenant-scoped. A flow references a profile by name,
// never a raw capability blob — the same principle as DB connections.
public sealed record DeviceProfile(
    DeviceProfileId Id,
    string          Name,             // "pixel-7-android-14"
    MobilePlatform  Platform,         // Android | Ios
    string          PlatformVersion,
    string?         DeviceName,
    AppSource       App,              // path, URL, or an already-installed bundle id
    AppiumEndpoint  Endpoint,         // local, self-hosted, or a device farm
    SecretRef?      FarmCredential,
    ResetStrategy   Reset,            // Full | Fast | None — see MOB-T3
    TimeSpan        SessionTimeout,
    TenantId        TenantId,
    IReadOnlyDictionary<string,string>? ExtraCapabilities);  // escape hatch

// Reuses LocatorStrategy from the shared vocabulary (WEB-T1).
// The mobile cases were declared there deliberately, so this enum never changes:
//   AccessibilityId, ResourceId, IosPredicate, IosClassChain, ClassName, XPath, Text

public abstract record Gesture;
public sealed record Tap(LocatorRef Target)                              : Gesture;
public sealed record DoubleTap(LocatorRef Target)                        : Gesture;
public sealed record LongPress(LocatorRef Target, TimeSpan Duration)     : Gesture;
public sealed record Swipe(SwipeDirection Direction, LocatorRef? Within, double Distance) : Gesture;
public sealed record SwipeBetween(Point From, Point To, TimeSpan Duration): Gesture;
public sealed record ScrollTo(LocatorRef Target, LocatorRef? Container)  : Gesture;
public sealed record Pinch(LocatorRef Target, double Scale)              : Gesture;
public sealed record DragAndDropMobile(LocatorRef From, LocatorRef To)   : Gesture;

public abstract record DeviceCommand;
public sealed record Rotate(Orientation To)                  : DeviceCommand;
public sealed record ToggleWifi(bool On)                     : DeviceCommand;
public sealed record ToggleAirplaneMode(bool On)             : DeviceCommand;
public sealed record PressDeviceKey(DeviceKey Key)           : DeviceCommand;  // Back | Home | Enter
public sealed record HideKeyboard()                          : DeviceCommand;
public sealed record OpenNotifications()                     : DeviceCommand;
public sealed record BackgroundApp(TimeSpan Duration)        : DeviceCommand;
public sealed record HandlePermissionDialog(PermissionAction Action) : DeviceCommand;
public sealed record InstallApp(AppSource App)               : DeviceCommand;
public sealed record RemoveApp(string BundleId)              : DeviceCommand;
public sealed record ResetApp()                              : DeviceCommand;
public sealed record SwitchContext(AppContext Context)       : DeviceCommand;  // Native | Webview
```

### One design note worth stating

`ExtraCapabilities` exists as an escape hatch because Appium's capability surface is large, evolves, and varies by driver. Without it, every new Appium feature would need a Yukti release. With it, a team can set anything the driver supports.

It is **admin-configured only**, never settable from a step — the same reasoning as the DB module's connection design. A capability can point a session at a different server or a different app, so it's a configuration decision, not a test-authoring one.

## Tasks — client, profiles, sessions, locators

### MOB-T1 — Appium client (P0)

**Closed 2026-11-07 — a completion pass on an already-existing module, not a from-scratch
build.** `src/Yukti.Infrastructure.InMemory/Modules/MobileModule.cs` already existed (a direct
port of an earlier TS prototype, predating this spec), already wired into the real app
(`Program.cs`: DI singleton, module registry, seeded `ModuleRegistration` at startup alongside
Api/Web/Logs), already depending on `Appium.WebDriver`. It had zero test coverage anywhere, and
was missing three of this task's own checklist items — fixed below without touching the parts
that already worked (session reuse by `FlowRunId`, guaranteed teardown).

- [x] A W3C WebDriver client speaking JSON over HTTP to an Appium server. Use a maintained .NET Appium client package rather than hand-rolling the protocol — **ADR and approval required** for the dependency.
  Already present (`Appium.WebDriver`, `AppiumDriver`/`AndroidDriver`/`IOSDriver`). Upgraded
  6.0.1 → 8.2.0 this task, owner's explicit choice (ADR-0009) — verified by a clean build against
  the *unchanged* existing code before any new code was added.
- [x] Session creation from a `DeviceProfile`, session reuse within a flow, guaranteed teardown even when a step throws.
  Session creation now builds a real `DeviceProfile` (see Domain model above) from the same
  `ctx.Variables["mobile"]` config bag as before, rather than passing a raw dictionary straight
  to `AppiumOptions`. Reuse-by-`FlowRunId` and `Teardown` already existed and are unchanged.
  "From a `DeviceProfile`" here means the *shape*, not yet "looked up by name from a persisted,
  admin-configured store" — that's explicitly MOB-T2's job (below), not this task's.
- [x] Connection failures are **specific**: "no Appium server at `<endpoint>`", "no device matching profile `pixel-7-android-14`", "app not found at `<path>`". Never a bare timeout — a bare timeout on a mobile setup costs a tester an hour of guessing.
  Two reliably-distinguishable categories built and tested for real: a genuine connection
  failure (socket/HTTP/timeout anywhere in the exception chain) reports "No Appium server
  reachable at `<url>`..."; anything else reports the real Appium error plus the profile name
  and endpoint. The finer "no device"/"app not found" sub-categories need a real Appium server's
  actual error text to distinguish reliably from each other without guessing at message
  patterns — not available in this environment, flagged in `plan/OPEN-QUESTIONS.md`, not
  invented without one. Either way: never a bare timeout, always the real detail.
- [x] Appium-side logs are captured and attached to the step result on failure, because the real cause is usually in the driver's log rather than the response.
  Built: a `Run()` failure attempts `driver.Manage().Logs.GetLog("logcat")` and attaches it to
  the `StepOutcome`'s `Data` — best-effort, same reasoning as `WebModule`'s own
  failure-screenshot capture (a log type a given driver/platform doesn't support must never turn
  a real step failure into a different, more confusing failure about logging). Not exercised
  against a real device (none available); the capture call itself is proven not to throw and to
  degrade gracefully when it fails.
- [x] Farm credentials via `SecretRef`, with the standard sentinel leak test.
  Built via the existing `ICredentialResolver` pipeline under its real name (`FarmCredentialRef`
  config key, resolved through `ctx.Credentials.ResolveAsync`, never taken as a literal value).
  Sentinel leak test: proves the credential reference name *and* the resolved secret value are
  both absent from every message `MobileModule` produces, mirroring this codebase's existing
  `FlowRunLoggingTests.Credential_value_never_appears_anywhere_in_the_log_stream`.

✔ `dotnet test --filter "FullyQualifiedName~AppiumClient"`
→ 8/8 passing, all new (`MobileAppiumClientTests.cs`) — the module had no tests before this task.

Time-box: 5 h.

### MOB-T2 — Device profiles (P0)

**Closed 2026-11-08.** `DeviceProfile` (MOB-T1's bare record) is now an `AggregateRoot<DeviceProfileId>`
with private setters, a visible `Version` counter, and `Create`/`Update` factory methods — the same
"disposable, editable resource" shape `RequestTemplate` already has. Persisted via EF
(`device_profiles` table, `EfDeviceProfileRepository`/`EfDeviceProfileSummaryQuery`) and an
in-memory counterpart for tests/CLI.

- [x] Admin-configured, tenant-scoped, versioned, exactly like DB connections. A flow references a profile **by name**.
  `FlowEngine.ResolveDeviceProfile` resolves a flow's declared `mobile.profileName` against the
  current `DeviceProfile` (by name + tenant) on every run, merging the profile's fields into
  `MobileModule.BuildProfile`'s own config-bag shape — the flow's own literal fields always win
  per key, same merge precedence `ResolveRequestTemplate` already uses for `templateId`. Unresolved
  name -> `DeviceProfileResolutionException`, thrown before any step dispatches (Setup is
  flow-scoped, not step-scoped). `CreateDeviceProfileCommand`/`Update`/`Delete` handlers + a
  `/api/v1/device-profiles` CRUD group enforce `Permission.DeviceProfileManage`.
- [x] Admin UI with a "test profile" action that opens a session, takes a screenshot and tears down, so a misconfiguration is caught at setup rather than in a failing test run.
  `IDeviceProfileTester`/`AppiumDeviceProfileTester` (Infrastructure.InMemory, reuses
  `MobileModule.ClassifySessionFailure` for the same specific-not-bare-timeout failure shape) +
  `POST /device-profiles/{id}/test-connection` + a real page at `/device-profiles`
  (`apps/yukti-gui`) with a "Test connection" row action rendering the result/screenshot. Not
  exercised against a real Appium server (none available in this environment, same MOB-T1
  constraint) — proven against a genuinely unreachable endpoint instead.
- [x] Publish validation: a flow referencing a missing profile fails with the existing 422-naming-the-step behaviour.
  **Honest deviation, not silently approximated:** a device profile reference is flow-scoped
  (Flow's declared `mobile` variable), not step-scoped the way `templateId` is — there is no one
  step to "name" the way the spec's own phrase literally reads. `Flow.Publish`'s new
  `IDeviceProfileExistsChecker` check uses the exact same `FlowPublishError`/422 shape as the
  `templateId`/`RepositoryRef` checks beside it, with field `"variables.mobile.profileName"`
  instead of `"steps[i]..."`, since that is genuinely where the reference lives. Still purely
  shape-based — no `step.Module == ModuleKind.Mobile` branch anywhere (Architecture Principle
  20.3 held, verified by grep same as MOB-T1).
- [x] Tenant isolation, tested at the API and the raw-SQL level.
  `device_profiles` got the real Postgres RLS policy from day one of its own migration
  (`ENABLE`/`FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy + `yukti_app` GRANT) — the
  `object_repositories`/WEB-T2 pattern, not `request_templates`' gap (that migration's own comment
  flags why). Three new tests in `TenantIsolationTests.cs`, same shape as the `object_repositories`
  ones: cross-tenant read via the real repository, a raw unparameterized SQL count proving the
  database itself (not just the C# `WHERE`) refuses the row, and a rejected illegally-tagged write
  (`42501 insufficient_privilege`). All three run against the real local Postgres and pass.
- [x] A profile records whether its endpoint is local, self-hosted or a named device farm, because error messages and retry behaviour differ.
  `DeviceEndpointKind { Local, SelfHosted, Farm }` on the aggregate, persisted and surfaced in the
  admin UI. Metadata only so far — `ClassifySessionFailure`'s own message doesn't yet branch on it
  (no real farm to test against in this environment); the field exists and is wired through end to
  end, ready for that to be added without a persistence change.

✔ `dotnet test --filter "FullyQualifiedName~DeviceProfile"` → 18/18 passing (13 orchestration-level
+ 5 domain), plus 3/3 new `TenantIsolationTests` cases against the real DB (not covered by that
filter; run separately, see above). Full solution: `dotnet build -warnaserror` clean; full suite
green (one `WebWaitTests` timing flake, confirmed pre-existing/unrelated — passes in isolation).
`npm run lint`/`tsc --noEmit`/`vitest run` all clean on the frontend.
Next: MOB-T3 (Session lifecycle and app state) — reset strategies, teardown-on-every-path,
session timeout, session reuse across steps; `DeviceProfile.Reset` already exists from MOB-T2,
not yet actually honored by `MobileModule.Teardown`.

Time-box: 3 h.

### MOB-T3 — Session lifecycle and app state (P0)

The mobile equivalent of the DB module's transaction problem: without this, test two inherits test one's mess.

**Closed 2026-11-08.**

- [x] Three reset strategies, set per profile: **Full** (reinstall the app — slowest, cleanest), **Fast** (clear app data between flows), **None** (reuse state deliberately, for flows that chain).
  Mapped onto Appium's own W3C `noReset`/`fullReset` capabilities (`MobileModule.ResetCapabilitiesFor`,
  public and pure, same "direct unit testing without a live server" reasoning as
  `ClassifySessionFailure`) rather than a hand-rolled uninstall/clear-data sequence this module
  would otherwise have to reimplement and keep correct per platform. Applied in `Setup()` before
  any `ExtraCapabilities` override, so the escape hatch can still take precedence if a team ever
  needs to.
- [x] Default is **Fast**. A flow can override it.
  `DeviceProfile.Create`'s own `reset` parameter already defaulted to `Fast` (MOB-T2); the real gap
  closed here was that `MobileModule.BuildProfile`'s literal-config path **hardcoded** `Fast`/5
  minutes regardless of what a resolved, persisted profile said — `FlowEngine.ResolveDeviceProfile`
  was silently dropping both fields on the floor. Fixed by adding `"reset"`/`"sessionTimeoutSeconds"`
  as two more config-bag keys both the resolver and a flow author can set, same shape as every
  other key in that bag.
- [x] A session is torn down on flow completion, failure **and** cancellation. A leaked session holds a device hostage, which on a shared device farm blocks a whole team.
  Already true, structurally, before this task: `FlowEngine.Execute`'s `try`/`finally` calls
  `Teardown` for every used module regardless of how the `try` exits (return, exception,
  cancellation) — `GracefulShutdownTests.cs` already proves this generically, and it applies to
  `MobileModule` the same as any other `IAutomationModule`. Nothing Mobile-specific to add here.
- [x] Session reuse across steps within one flow is automatic; emulator cold-start cost is paid once.
  Already true, structurally, before this task: sessions are keyed by `FlowRunId` in a
  `ConcurrentDictionary`, one `Setup()` call per flow run regardless of step count. Not exercisable
  end-to-end in this environment (`Setup()` always fails before storing a session without a live
  Appium server — the same wall MOB-T1 hit), so this is a structural/code-reading confirmation, not
  a passing integration test; flagged, not invented around.
  **MOB-T3's own real addition here:** a session now also carries its creation time and the
  profile's `SessionTimeout`, so a stale session can be told apart from a healthy one (see next item).
- [x] A configurable session timeout, after which the session is force-released and the run fails with a clear message.
  `Run()` checks `MobileSession.IsExpired` before dispatching any action; an expired session is
  `Quit()`-ed, removed, and the step fails naming the exact timeout — never a bare timeout message,
  same MOB-T1 standard. `TimeProvider` (optional constructor param, defaults to `TimeProvider.System`)
  makes this clock-injectable; every existing construction site (`new MobileModule()`) keeps
  compiling and behaving identically. The force-release path itself isn't exercisable end-to-end
  without a live session (same wall as above) — the timeout *arithmetic* is proven directly instead,
  via `BuildProfile`'s parsing (see the gate below).
- [ ] A test proves isolation: flow A writes state, flow B with Fast reset sees a clean app.
  **Not provable in this environment, flagged rather than invented:** real app-state isolation is
  a property of a real device/emulator honoring Appium's reset capabilities, not of this module's
  own code — there is no live Appium server/device here to write state to in the first place (same
  constraint this spec's own "Infrastructure reality" section names explicitly). What *is* proven:
  the correct capability is sent for each strategy (`ResetCapabilitiesFor`'s own test). Left open
  for whoever has a real device farm or emulator to run `demo/mobile_login.json` against it.

✔ `dotnet test --filter "FullyQualifiedName~MobileSession"` → 18/18 passing (new), plus the existing
8 `MobileAppiumClientTests` re-verified unbroken (`FullyQualifiedName~Mobile` → 23/23). Full solution:
`dotnet build -warnaserror` clean (0 warnings); full suite green.

Time-box: 4 h.

### MOB-T4 — Mobile locators (P0)

- [ ] Implements the **shared vocabulary's** `Locator` type against Appium. No new locator concept is invented here; if one seems necessary, the vocabulary needs extending for both engines, which is an escalation.
- [ ] Strategies: `AccessibilityId` (**preferred** — maps to `contentDescription` on Android and `accessibilityIdentifier` on iOS), `ResourceId` (Android), `IosPredicate`, `IosClassChain`, `ClassName`, `Text`, `XPath`.
- [ ] **XPath carries an explicit warning** in the UI: on mobile it is genuinely slow, because the driver serializes the whole UI tree for each query. This is not a style preference; it's a measurable performance cliff.
- [ ] Fallback resolution works identically to the web: a fallback success is a **flagged pass** naming which fallback and why the primary failed.
- [ ] Chaining via `Within` and `Nth` selection, same semantics as web.
- [ ] One Object Repository entry can hold **both** web and mobile strategies for the same logical element, so a team testing a web app and its mobile counterpart maintains one definition.
- [ ] Failure diagnostics include a screenshot **and** the UI hierarchy XML, which is the mobile equivalent of the DOM snapshot.

✔ `dotnet test --filter "FullyQualifiedName~MobileLocator"`

Time-box: 4 h.

## Tasks — gestures, device control, assertions

### MOB-T5 — Gestures (P0)

- [ ] Every `Gesture` type from the domain model, implemented via the W3C Actions API.
- [ ] `Swipe` supports a direction with a distance, scoped to an element or the screen, and an explicit coordinate pair for the cases direction can't express.
- [ ] `ScrollTo` scrolls until the target is visible, with a bounded maximum number of attempts so an element that will never appear fails in seconds rather than scrolling forever.
- [ ] Gestures honour the shared vocabulary's wait conditions — `tap` waits for the element to be tappable, mirroring Playwright's actionability checks on the web.
- [ ] A gesture on a missing element fails with the locator diagnostics, a screenshot, and the UI hierarchy.
- [ ] Coordinate-based gestures are **relative** to the element or screen, not absolute pixels, so a flow isn't tied to one screen size.

✔ `dotnet test --filter "FullyQualifiedName~MobileGesture"`

Time-box: 5 h. The largest task in this module.

### MOB-T6 — Device control (P0)

- [ ] Every `DeviceCommand` from the domain model.
- [ ] `HandlePermissionDialog` deals with OS-level permission prompts (location, camera, notifications), which are otherwise the most common cause of a mobile test mysteriously stalling.
- [ ] `ToggleWifi` and `ToggleAirplaneMode` enable offline-behaviour testing, a genuinely valuable mobile-only capability.
- [ ] Commands unsupported on a platform fail clearly — "toggling airplane mode is not supported on iOS simulators" — rather than silently doing nothing. A silent no-op makes a test pass while proving nothing.
- [ ] `SwitchContext` between native and webview, for hybrid apps, with a test against a hybrid fixture app.

✔ `dotnet test --filter "FullyQualifiedName~DeviceCommand"`

Time-box: 4 h.

### MOB-T7 — Assertions (P0)

- [ ] Mirrors the Web module's set: `assertText`, `assertVisible`, `assertHidden`, `assertEnabled`, `assertChecked`, `assertCount`, `assertAttribute`.
- [ ] Mobile-specific: `assertAppInstalled`, `assertCurrentActivity` (Android), `assertOrientation`, `assertKeyboardShown`.
- [ ] Every failure reports expected, actual, the locator, a screenshot and the UI hierarchy.
- [ ] Soft assertions (P1), consistent with the other modules.

✔ `dotnet test --filter "FullyQualifiedName~MobileAssertion"`

Time-box: 3 h.

### MOB-T8 — Mobile web and hybrid (P1)

- [ ] Automating a website in a mobile browser (Safari on iOS, Chrome on Android) via Appium.
- [ ] A webview inside a native app, with context switching.
- [ ] **Worth noting honestly:** for pure mobile-web testing, Playwright's device emulation (Web module, WEB-T8) is faster, cheaper and less flaky. Appium's mobile-web support matters when you need a *real* device or a webview inside a native app. The documentation should say so, so teams don't reach for the heavier tool by default.

## Infrastructure reality

This section exists because mobile testing fails in the environment far more often than in the code.

### What a customer must provide

| Need | Android | iOS |
| --- | --- | --- |
| Appium server | Any OS | Any OS, but see below |
| Device or emulator | Emulator on any OS, or a real device | Simulator **on a Mac**, or a real device via a Mac |
| Platform tooling | Android SDK, UiAutomator2 driver | Xcode, XCUITest driver |
| Alternative | A cloud device farm removes all of the above | Same |

**The Mac requirement for iOS is absolute.** Apple requires Xcode, which runs only on macOS. Yukti cannot work around it. State it plainly in the documentation and in the device-profile UI when a user selects iOS, so nobody discovers it after an hour of configuration.

### Device farms

- [ ] P0 support for connecting to a cloud device farm — BrowserStack, Sauce Labs, LambdaTest — by pointing a profile's endpoint at it with a `SecretRef` credential.
- [ ] Farms are the **recommended** path for most teams: no Mac, no emulator management, real devices, parallel runs.
- [ ] Farm-specific capabilities go through `ExtraCapabilities`, so supporting a new farm needs no code change.
- [ ] Farm errors are surfaced verbatim alongside Yukti's own message, since a quota or concurrency error from the farm is the actual cause and paraphrasing it hides the fix.

### CI

- [ ] Android emulator tests can run in CI on a Linux runner. Expect several minutes of cold start; budget for it rather than being surprised.
- [ ] iOS tests in CI need a macOS runner, which is materially more expensive. Recommend farms for iOS CI instead.
- [ ] Mobile tests go in a **separate CI job** from the main suite, so their slower, flakier nature never blocks a PR on unrelated work.
- [ ] A mobile test that can't get a device is reported as **infrastructure-unavailable**, visibly distinct from a test failure. Conflating the two destroys trust in the suite faster than anything else.

### Flake expectations — stated honestly

Mobile automation is inherently flakier than web: emulator timing, OS animations, permission dialogs, device state. The web module's under-2% target is **not** realistic here at the start.

- [ ] Measure the mobile flake rate separately and publish it as its own number.
- [ ] Set the initial target at **under 5%**, and work it down.
- [ ] Report mobile flake separately from web in dashboards. One blended number hides which half has a problem, and that's the number people actually need.

## Rules, sequence and gate

### Rules Sutradhar must not break

- **Never invent a mobile-only locator concept.** Implement the shared vocabulary. Extending it means extending it for both engines, which is an escalation.
- **Never let a step set capabilities.** Admin-configured profiles only.
- **Never leave a session leaked.** Teardown on success, failure and cancellation. A leaked session holds a device hostage.
- **Never report an unavailable device as a test failure.** Infrastructure-unavailable is its own, visibly distinct outcome.
- **Never silently no-op an unsupported command.** Fail with a message naming the platform limitation.
- **Never blend mobile flake into the web number.**

### Escalation triggers specific to this module

1. The shared vocabulary can't express a needed mobile concept — stop; extending it affects the Web module too.
2. Any new dependency, including the Appium client package.
3. iOS work is requested and no Mac is available — this is a hard stop, not a workaround.
4. Mobile flake exceeds 10% after MOB-T5 — something structural is wrong; report rather than papering over it with retries.
5. A device farm's behaviour requires module-specific branching — that breaks module parity.

### Sequence

| Order | Task | Time-box |
| --- | --- | --- |
| 1 | MOB-T1 Appium client | 5 h |
| 2 | MOB-T2 Device profiles | 3 h |
| 3 | MOB-T3 Session lifecycle and app state | 4 h |
| 4 | MOB-T4 Mobile locators | 4 h |
| 5 | MOB-T5 Gestures | 5 h |
| 6 | MOB-T6 Device control | 4 h |
| 7 | MOB-T7 Assertions | 3 h |
| 8 | MOB-T8 Mobile web and hybrid (P1) | 4 h |

Roughly **28 hours of P0 work**, about 4 working days — plus environment setup time, which is frequently larger than the coding and should be budgeted honestly.

### Module gate

```
dotnet test --filter "FullyQualifiedName~Mobile|FullyQualifiedName~Appium"
dotnet test --filter "FullyQualifiedName~ModuleParity"      # contract untouched
yukti run demo/mobile_login.json                             # Android emulator
yukti run demo/mobile_login.json --profile ios-sim           # iOS, if a Mac exists
python scripts/flake_run.py demo/mobile_login.json -n 30     # under 5%
```

`demo/mobile_login.json`: **launch the app → log in with test credentials → handle the permission dialog → swipe to a list item → assert its text → rotate → assert it's still visible.** That exercises locators, gestures, device control and assertions in one flow, and it's the smallest flow that proves the module genuinely works rather than compiles.

### Metrics

| Metric | Target |
| --- | --- |
| The demo flow passes on Android | Required |
| The same flow passes on iOS with no flow changes | Required if a Mac is available |
| Mobile flake rate | Under 5%, reported separately from web |
| Leaked sessions after a full test run | 0 |
| Module-specific code above the module | 0 |
| One repository entry serving both web and mobile | Proven by a test |

## Open questions and risks

### Open questions

- [x] **Blocking everything — you:** has any design partner actually asked for mobile testing? If not, **don't build this yet.** The contract means nothing is lost by waiting, and 2–3 weeks is a lot to spend speculatively.
  **Answered 2026-11-07:** yes (owner, via AskUserQuestion).
- [ ] **Blocking iOS — you:** is a Mac available, or will iOS go through a device farm? Without either, iOS is impossible and the module is Android-only.
  Kept explicitly open per the owner's own instruction — see `plan/OPEN-QUESTIONS.md`.
- [x] **Blocking MOB-T1 — you:** approval for the Appium .NET client dependency.
  **Answered 2026-11-07:** approved, and approved upgrading the already-existing 6.0.1 pin to
  8.2.0 (ADR-0009).
- [ ] **Blocking the gate — you:** is there a real app (`.apk` or `.ipa`) to test against? A demo app proves the plumbing; a real app finds the problems.
- [ ] **Non-blocking — you:** local emulators, or go straight to a device farm? Farms cost money but remove nearly all the environment pain, and for most teams they're the right answer.
  Owner's own steer: simulator/emulator for now, not real devices — see `plan/OPEN-QUESTIONS.md`.
- [x] **Non-blocking — Sutradhar:** confirm WEB-T1's vocabulary is genuinely engine-neutral before starting. If a Playwright type has leaked in, that's a blocker and must be fixed in the Web module first.
  **Verified 2026-11-07** — see the Hard dependencies section above. No fix needed.

### Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| **Built speculatively and never used** | **Medium–high** | High | The deferral note. Build it when asked, not before |
| The shared vocabulary turns out not to be engine-neutral | Medium | **High** | Verified before starting; CI check in the Web module; the fake-engine test |
| Environment setup consumes more time than the code | **High** | Medium | Budgeted explicitly; device farms recommended as the default path |
| Mobile flake undermines trust in the whole suite | Medium | High | Separate job, separate number, infrastructure-unavailable as its own outcome |
| Leaked sessions block a shared device farm for a whole team | Medium | High | Teardown on every path, session timeouts, tested explicitly |
| iOS proves impossible for a given customer | Medium | Medium | Stated plainly up front; Android-only is a legitimate, documented configuration |
| Appium's API shifts between major versions | Low | Medium | `ExtraCapabilities` escape hatch; a maintained client package rather than hand-rolled protocol |
