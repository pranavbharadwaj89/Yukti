# Web Module

`ModuleKind.Web` ("web")

## Status

TS prototype only (`src/modules/web/WebModule.ts`). No .NET port exists
yet — `Yukti.Infrastructure.InMemory/Modules/` contains only `ApiModule.cs`
and `LogsModule.cs`. Porting this module means implementing
`IAutomationModule` against a .NET browser-automation library (Playwright
has a first-class .NET binding, `Microsoft.Playwright`, so the underlying
tech can carry over — the port is an interface-shape exercise, not a new
tool choice).

## Purpose

Browser-based web UI automation via Playwright (Chromium). Drives one
`Browser` -> `BrowserContext` -> `Page` for the lifetime of a flow so
sequential steps (navigate -> fill -> click -> assertText) share page
state, the way a human drives one tab through a journey.

## Dependencies

- `playwright` — a peer dependency, not bundled. Requires
  `npm install playwright && npx playwright install chromium` before use
  (stated explicitly in both the module's own doc-comment and the repo
  README).

## Lifecycle

- `setup()`: dynamically imports `playwright`, launches Chromium
  (`headless` from constructor option, default `true`), opens one context
  and one page. If `setup()` was never called, every `run()` call fails
  immediately with `"WebModule not set up — call setup() first"`.
- `teardown()`: closes the context, then the browser.

## Constructor options

| Option | Type | Default |
|---|---|---|
| `headless` | boolean | `true` |

## Actions

All actions operate on the single shared `Page`.

| Action | Params | Behavior |
|---|---|---|
| `navigate` | `url: string` | `page.goto(url, { waitUntil: "domcontentloaded" })` |
| `click` | `selector: string`, `timeoutMs?: number` (default 5000) | `page.locator(selector).click({timeout})` |
| `fill` | `selector: string`, `value: string` | `page.locator(selector).fill(value)` |
| `assertText` | `selector: string`, `expected: string`, `mode?: "contains"` | Reads `locator.textContent()`, trims it; exact match unless `mode === "contains"` |
| `screenshot` | `path?: string` (default `screenshot-<timestamp>.png`), `fullPage?: boolean` | `page.screenshot({path, fullPage})`; returns `data: {path}` |
| `waitForSelector` | `selector: string`, `timeoutMs?: number` (default 10000) | `page.waitForSelector(selector, {timeout})` |

Any other action: `{status: "failed", error: "Unknown web action \"<action>\""}`.

## Output shape

- Every action returns `{status: "passed", message: "<human-readable summary>"}`
  on success (`screenshot` additionally returns `data: {path}`).
- Any thrown error (Playwright timeout, selector not found, navigation
  failure) is caught and converted to `{status: "failed", error: <message>}`
  — the module never throws out of `run()`.

## Known constraints

- Single page per flow run — no multi-tab/multi-context support, no
  explicit viewport/device-emulation params exposed through actions.
- No built-in retry on flaky selectors (relies on the Flow Engine's
  uniform retry policy once ported to .NET).
- `assertText` only supports exact-match or substring-contains; no regex
  mode.
- Screenshots write to local disk with no upload/artifact-store
  integration — a gap the platform-level artifact storage (Volume 1/4)
  would need to close once ported.
