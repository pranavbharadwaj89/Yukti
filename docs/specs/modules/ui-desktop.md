# UI (Desktop) Module

`ModuleKind.DesktopUi` ("ui")

## Status

TS prototype only (`src/modules/ui/UiModule.ts`). No .NET port exists yet.
Note: in C#, `ModuleKind.DesktopUi` maps to the wire value `"ui"` — the
property name differs from the value deliberately, since `Ui` alone would
be ambiguous with the general concept; `DesktopUi` disambiguates from Web
UI and Mobile UI, which are separate module kinds.

## Purpose

The layer below Web and Mobile: raw mouse, keyboard, and screen-pixel
control for automating native desktop apps, legacy Win32/Electron UIs, or
any application with no DOM or accessibility tree to hook into. Backed by
`nut.js` (`@nut-tree-fork/nut-js`).

Key primitive: image-based matching (`findImage`) — instead of a CSS
selector, you supply a reference screenshot of a button/icon and the
module locates that region on the live screen. This is the fallback of
last resort for kiosk apps, games, or legacy software with zero automation
hooks.

## Dependencies

- `@nut-tree-fork/nut-js` — dynamically imported per action call (not
  imported at module load time), so the dependency is only required at the
  moment an action actually runs.
- Requires a real display. The module doc-comment states explicitly:
  "won't run headless in CI" — this is the one module in the platform that
  cannot run in a typical CI runner without a virtual framebuffer (e.g.
  Xvfb) or a GUI-attached agent.

## Constructor options

| Option | Type | Default |
|---|---|---|
| `matchConfidence` | number (0-1) | `0.9` |

Used only by `findImage` as the minimum confidence for a positive match.

## Lifecycle

No `setup()`/`teardown()` implemented — nut.js primitives (`mouse`,
`keyboard`, `screen`) are imported and used directly inside `run()` on
every call; there is no persistent session/handle to open or close.

## Actions

| Action | Params | Behavior |
|---|---|---|
| `click` | `x: number, y: number` | Moves mouse to `(x,y)` via `straightTo(Point)`, then `mouse.leftClick()` |
| `typeText` | `text: string` | `keyboard.type(text)` |
| `pressKey` | `key: string` (must match a `Key` enum member name from nut.js) | `keyboard.pressKey(Key[key])` then immediately `releaseKey` |
| `findImage` | `imagePath: string` | Sets `screen.config.confidence = matchConfidence`, loads the reference image, calls `screen.find(target)`, catching a not-found result as `null` rather than throwing |
| `screenshot` | `path?: string` (default `ui-screenshot-<timestamp>.png`) | `screen.capture(path)`; returns `data: {path}` |

Any other action: `{status: "failed", error: "Unknown ui action \"<action>\""}`.

## Output shape

- `click`, `typeText`, `pressKey`: `{status: "passed", message}` only, no
  `data`.
- `findImage`: on match, `{status: "passed", message: "Found image at (x, y)", data: <region>}`;
  on no match, `{status: "failed", error: "Image <path> not found on screen (confidence <matchConfidence>)"}`
  — this is the only action in the module that treats "not found" as a
  normal failure outcome rather than an exception.
- `screenshot`: `{status: "passed", data: {path}}`.
- Any thrown error (including a `pressKey` with an invalid `key` name,
  which would throw indexing into `Key`) is caught at the top of `run()`
  and returned as `{status: "failed", error: <message>}`.

## Known constraints

- Absolute-coordinate clicking only (`x, y`) — no element/selector
  abstraction, so any UI resize or DPI-scaling change breaks recorded
  coordinates. `findImage` is the module's own mitigation for this, not a
  built-in coordinate-independence guarantee.
- `pressKey` takes a single key name per call — no explicit modifier-combo
  helper (e.g. Ctrl+C) beyond manually sequencing `pressKey`/`typeText`
  calls across steps.
- Cannot run in standard headless CI without a virtual display — this
  constrains where this module's flows can execute in a pipeline.
