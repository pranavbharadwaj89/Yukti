# Mobile Module

`ModuleKind.Mobile` ("mobile")

## Status

TS prototype only (`src/modules/mobile/MobileModule.ts`). No .NET port
exists yet.

## Purpose

Mobile UI automation (iOS/Android) via WebdriverIO's Appium service —
taps, typing, swipes, and visibility assertions against a real
device/emulator or a device farm (BrowserStack App Automate, Sauce Labs)
reachable via the same Appium protocol.

## Dependencies

- `webdriverio`, `appium` — peer dependencies, not bundled.
- Requires a running Appium server (`appium &`) plus an attached
  device/emulator/simulator. This module does not start Appium itself; it
  only connects to a URL.

## Constructor

```ts
new MobileModule(capabilities: MobileCapabilities, appiumUrl = "http://localhost:4723")
```

`MobileCapabilities` (required, no defaults — caller must supply):

| Field | Type |
|---|---|
| `platformName` | `"iOS" \| "Android"` |
| `appium:deviceName` | string |
| `appium:app` | string (path or bundle id) |
| `appium:automationName` | `"XCUITest" \| "UiAutomator2"` |
| *(any other key)* | passed through as-is (`[key: string]: unknown`) |

Config is entirely constructor-supplied — there is no per-action override
of capabilities, and no way to switch device/platform mid-flow (one
`MobileModule` instance equals one fixed device target for the whole run).

## Lifecycle

- `setup()`: dynamically imports `webdriverio`, parses `appiumUrl` into
  hostname/port, opens a `remote()` session with the given capabilities.
- `teardown()`: `driver.deleteSession()`.
- If `setup()` wasn't called, every action fails with `"MobileModule not
  set up — call setup() first"`.

## Actions

| Action | Params | Behavior |
|---|---|---|
| `tap` | `selector: string` | `driver.$(selector).click()` |
| `type` | `selector: string`, `value: string` | `driver.$(selector).setValue(value)` |
| `swipe` | `fromX, fromY, toX, toY: number` | Synthesizes a W3C pointer-action gesture: move -> pointerDown -> move (300ms) -> pointerUp |
| `assertVisible` | `selector: string` | `el.isDisplayed()`, swallowing errors as `false` (i.e. "not found" and "found but hidden" both read as not-visible) |

Any other action: `{status: "failed", error: "Unknown mobile action \"<action>\""}`.

## Output shape

Same pattern as every other module: `{status: "passed", message}` on
success; any thrown error is caught and returned as
`{status: "failed", error: <message>}`.

## Known constraints

- No screenshot action (unlike Web and Desktop UI modules) — a gap
  relative to its siblings.
- `assertVisible`'s `.catch(() => false)` means a genuinely broken
  selector and a hidden-but-present element are indistinguishable in the
  result — no differentiated error message.
- Single fixed device/session per module instance; testing across
  multiple devices in one flow needs multiple `MobileModule` registrations
  (not currently supported — the orchestrator's `register()` keys modules
  by `kind`, one module per kind).
