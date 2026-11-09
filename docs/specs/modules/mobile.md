# Mobile Module

`ModuleKind.Mobile` ("mobile")

## Status

Real .NET implementation, built against `Appium.WebDriver` 8.2.0
(`src/Yukti.Infrastructure.InMemory/Modules/MobileModule.cs`). All 7 P0 tasks in
`plan/specs/MOBILE-TESTING-MODULE.md` (MOB-T1 through MOB-T7 — client, device profiles, session
lifecycle, locators, gestures, device control, assertions) are closed. This file previously
described the original TypeScript prototype (`webdriverio`-based) and had not been updated
through that entire build-out — rewritten here to match the real module, not the prototype that
preceded it.

## Purpose

Mobile UI automation (iOS/Android) via Appium — taps, gestures, device-level commands, and
assertions against a real device/emulator/simulator or a device farm (BrowserStack App Automate,
Sauce Labs) reachable via the same Appium protocol.

## Dependencies

- `Appium.WebDriver` (NuGet), upgraded 6.0.1 → 8.2.0 (ADR-0009).
- Requires a running Appium server plus an attached device/emulator/simulator. This module does
  not start Appium itself; it only connects to a URL (`appiumUrl`).

## Configuration

A flow's `mobile` variable (a literal config bag) or a persisted, admin-configured `DeviceProfile`
looked up by name (`FlowEngine.ResolveDeviceProfile`, MOB-T2):

| Key | Required | Notes |
|---|---|---|
| `platformName` | yes | `"iOS"` or `"Android"`. |
| `deviceName` | yes | |
| `app` | no | Path or bundle id. Omit entirely for mobile-web testing (see below). |
| `appiumUrl` | no | Defaults to `http://localhost:4723`. |
| `farmCredentialRef` | no | Resolved via the real `{{secrets}}`/`ICredentialResolver` pipeline, never a literal key. |
| `reset` | no | `Full` \| `Fast` (default) \| `None` — maps to Appium's own `fullReset`/`noReset` capabilities (MOB-T3). |
| `sessionTimeoutSeconds` | no | Default 300. |
| *(any other key)* | no | Passed through as-is to Appium's capabilities (`AddAdditionalAppiumOption`) — this is also how `browserName` (mobile web, see below) reaches Appium, with no dedicated field. |

## Lifecycle

- `Setup()`: builds a typed `DeviceProfile` from the config bag, opens an `AndroidDriver`/
  `IOSDriver` session. Connection failures are specific (`ClassifySessionFailure`) — never a bare
  timeout.
- `Run()`: force-releases a session that has exceeded its `sessionTimeoutSeconds` before
  dispatching any action against it (MOB-T3).
- `Teardown()`: `driver.Quit()`, guaranteed even when a step throws (`FlowEngine`'s own
  `try`/`finally`).

## Actions

**Element interaction** — `selector` accepts a plain string, an inline locator object
(`{primary, fallbacks, description, within, nth}`), or an Object Repository reference; every
strategy in the shared `LocatorStrategy` vocabulary except the Web-only ones (`Css`, `TestId`,
`Role`, `Label`, `Placeholder`) is supported (`AppiumLocatorResolver`, MOB-T4):

| Action | Params | Behavior |
|---|---|---|
| `tap` | `selector` | Taps via the W3C Actions API (MOB-T5). |
| `type` | `selector`, `value` | Sets the element's value. |
| `doubleTap` | `selector` | |
| `longPress` | `selector`, `durationMs` (default 1000) | |
| `swipe` | `direction`+`distance`+`within` (relative, screen-size independent) **or** legacy `fromX/fromY/toX/toY` (absolute pixels) | |
| `swipeBetween` | `fromX/fromY/toX/toY` (relative, 0-1), `within`, `durationMs` | The explicit-coordinate escape hatch a direction can't express. |
| `scrollTo` | `selector`, `container`, `direction` (default Down), `maxAttempts` (default 10) | Bounded — fails in seconds, never scrolls forever. |
| `pinch` | `selector`, `scale` (> 1 zoom in, < 1 zoom out) | Real two-finger multi-touch (MOB-T5). |
| `dragAndDrop` | `from`, `to` | |

**Device control** (MOB-T6 — every command checked against unsupported-platform cases first,
never a silent no-op):

| Action | Params | Platform |
|---|---|---|
| `rotate` | `to` (Portrait/Landscape) | Both |
| `toggleWifi` | `on` | Android only |
| `toggleAirplaneMode` | `on` | Android only |
| `pressDeviceKey` | `key` (Back/Home/Enter) | Home: both; Back/Enter: Android only |
| `hideKeyboard` | — | Both |
| `openNotifications` | — | Android only |
| `backgroundApp` | `durationMs` | Both |
| `handlePermissionDialog` | `action` (Allow/Deny) | Both |
| `installApp` | `location` (path or URL) | Both |
| `removeApp` | `bundleId` | Both |
| `resetApp` | `appId` | Both (terminate + reactivate — Appium itself deprecated a dedicated reset command) |
| `switchContext` | `context` (Native/Webview) | Both |

**Assertions** (MOB-T7 — every failure attaches a screenshot and the UI hierarchy XML):

| Action | Mirrors Web module's | Params |
|---|---|---|
| `assertText` | `assertText` | `selector`, `expected`, `mode`, `exact` |
| `assertAttribute` | `assertAttribute` | `selector`, `attr`, `expected`, `mode` |
| `assertVisible` / `assertHidden` | `assertVisible` / `assertHidden` | `selector` |
| `assertEnabled` | `assertEnabled` | `selector` |
| `assertChecked` | `assertChecked` | `selector`, `checked` |
| `assertCount` | `assertCount` | `selector`, `expected` |
| `assertAppInstalled` | *(mobile-specific)* | `bundleId` |
| `assertCurrentActivity` | *(mobile-specific, Android only)* | `expected` |
| `assertOrientation` | *(mobile-specific)* | `expected` |
| `assertKeyboardShown` | *(mobile-specific)* | `shown` |

Any other action: `StepOutcome.Failed("Unknown mobile action \"<action>\".")`.

## Mobile web and hybrid apps (MOB-T8, P1)

**Automating a website in a mobile browser:** set `browserName` (e.g. `"Chrome"` on Android,
`"Safari"` on iOS) in the `mobile` config instead of `app` — Appium's own standard recipe for
mobile-web testing. No dedicated field exists for this; it reaches Appium through the same
pass-through `ExtraCapabilities` mechanism any unrecognized config key already uses.

**A webview inside a native app:** use `switchContext` (`context: "Webview"`) once the app has
navigated into one. `AppiumDeviceCommandExecutor.IsSupported` already treats this as supported on
both platforms — nothing mobile-web-specific needed on top of the existing device-control action.

**Worth stating plainly, not glossed over:** for *pure* mobile-web testing — a website, no native
app involved at all — the Web module's own device emulation (Playwright, WEB-T8) is faster,
cheaper, and less flaky than going through Appium and a real/simulated device. Reach for this
module's mobile-web path when you genuinely need a *real* device, or a webview embedded inside a
native app Playwright can't drive at all. Don't reach for the heavier tool by default.

## What's real and tested vs. what's wired in but unverified in this environment

Every pure/testable piece named above — the locator-strategy mapping, gesture coordinate math,
the bounded scroll-attempts loop, the platform-support decision matrix, config-bag parsing — has
a real xUnit test and no live Appium server dependency. Every driver-bound call (the actual
tap/swipe/assertion/device-command execution against a real session) is built and wired in but
**not exercised end-to-end in this environment** — no Appium server or device/emulator/simulator
is available here. See `plan/OPEN-QUESTIONS.md`'s Mobile section for the full, itemized list of
what that gap actually covers and what it would take to close each one.
