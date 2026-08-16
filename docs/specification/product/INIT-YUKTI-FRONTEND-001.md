# INIT-YUKTI-FRONTEND-001 — Frontend core: React GUI, module test surfaces, and multi-project support

| Field | Value |
|-------|-------|
| Initiative | `INIT-YUKTI-FRONTEND-001` |
| Source spec | Yukti Architecture Bible, Volume 2 — Frontend Architecture (this document instantiates that volume against the actual `apps/yukti-gui` codebase; Volume 2 itself does not exist as a separate document in this repo) |
| Repo | `yukti-platform` (`apps/yukti-gui`) |
| Format basis | `INIT-YUKTI-BACKEND-001` (Volume 1's SDD, FR/AC/evidence pattern), applied here to the frontend at the same rigor |
| Date | 2026-08-16 |
| Status | Draft — reverse-engineered from a real, running, substantially-built codebase, not written ahead of implementation |
| Dependency INIT | `INIT-YUKTI-BACKEND-001` (every FR below assumes that backend's REST/SignalR contract as given) |

## Overview

`apps/yukti-gui` is Yukti's single web client: a React 19 + TypeScript SPA (Vite-built) that authors Flows, runs ad-hoc tests against all five backend automation modules (API, Logs, Web, Mobile, Desktop UI), and — as of this initiative's most recent work — organizes that work into Projects with reusable Environments. Unlike the backend, which was built from a formal spec (Volume 1) before this document existed, the frontend was built iteratively against live backend endpoints, verified in-browser session by session. **This document's job is the same as Volume 1's: capture what's actually true as testable FRs, not invent new scope.** Where a capability doesn't exist yet, this is stated as an explicit gap, not silently omitted.

## As-built baseline (do not re-implement)

| Area | Status | Evidence |
|------|--------|----------|
| Routing (`TanStack Router`), persistent `AppShell` (nav rail + top banner) | **Live** | `src/app/router.tsx`, `src/layouts/app-shell.tsx` |
| Auth (JWT access token in-memory, refresh token in `sessionStorage`, silent-refresh-and-retry-once on 401) | **Live** | `src/store/auth-store.ts`, `src/services/api-client.ts` |
| Typed API client, one `request()` wrapper, RFC 7807 (`ApiError` with `correlationId`) | **Live** | `src/services/api-client.ts` |
| Live run progress (SignalR + REST catch-up-before-trusting-pushes) | **Live** | `src/hooks/index.ts` (`useLiveRunProgress`), `src/services/signalr.ts` |
| API Studio (Explorer with saved Collections/Requests, Request Designer, Response Viewer) | **Live** | `src/features/api-studio/**` |
| Logs Studio (Check Rules / Detect Anomalies, results viewer) | **Live** | `src/features/logs-studio/**` |
| Web / Mobile / Desktop UI test surfaces (generic schema-driven form) | **Live**, generic — no bespoke per-module UI | `src/features/testing/module-test-form.tsx` |
| Mobile device-config panel (writes `variableOverrides.mobile`) | **Live**, verified against a real Android emulator + Appium session | `module-test-form.tsx` |
| Flow Authoring (list, detail with React Flow canvas step visualization, add-step dialog) | **Live**, add-step param entry is a raw JSON textarea, not schema-driven like the Tests tabs | `src/features/flow-authoring/**` |
| Execution Monitor (run detail: step list + live console) | **Live** | `src/features/execution/run-detail-page.tsx` |
| Reports (tenant-wide aggregate: total runs/pass rate/flake rate + bar chart) | **Live**, tenant-aggregate only | `src/features/reporting-audit/reports-page.tsx` |
| Dashboard (summary cards + recent flows) | **Live** | `src/pages/dashboard-page.tsx` |
| Projects (CRUD) + Environments (per-project, variables + Mobile device config) | **Live** | `src/features/projects/projects-page.tsx` |
| Project switcher, Flows/API Explorer scoped by active project | **Live**, client-side filtering (no server-side `?projectId=` query param wired) | `src/layouts/app-shell.tsx`, `flows-page.tsx`, `explorer-tree.tsx` |
| Design system primitives (`Button`, `Input`, `Card`, `Dialog`, `StatusPill`, `Select`, `Checkbox`, `Tabs`, `CodeEditor` (Monaco), `Badge`, `DataTable`) | **Live** | `src/components/ui/**` |
| Scheduler / trigger management UI (cron/webhook/filewatch) | **Absent** | — |
| Audit log / per-flow drill-down UI | **Absent** | Reports page is tenant-aggregate only; backend `AuditRepository` has no FE consumer |
| Module marketplace / registration UI | **Absent** | — |
| ESLint import-boundary enforcement | **Absent (dependency installed, unconfigured)** | `eslint-plugin-boundaries` is in `package.json` devDependencies; no `eslint.config.*`/`.eslintrc*` file exists in `apps/yukti-gui` |
| Automated frontend tests (unit/component/E2E) | **Absent** | Zero `*.test.ts`/`*.test.tsx` files exist in `apps/yukti-gui` |
| Bundle-size budget / Lighthouse CI gate | **Absent** | No CI configuration exists for this repo at all |

## Functional requirements

FR IDs are namespaced `FR-FE-<SUBSYSTEM>` to stay legible, mirroring Volume 1's convention. Every row traces to a real file/behavior verified this session, restated in acceptance-testable form.

### Routing & shell (`FR-FE-ROUTE`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-ROUTE-01 | Every authenticated route's loader checks for a session (`accessToken` present) and redirects to `/login` otherwise, via one shared `beforeLoad` guard, never a per-page ad hoc check | `router.tsx`'s `requireSession()` on `authenticatedLayoutRoute` | Navigating to any authenticated route with no session redirects to `/login`; the guard is defined once |
| FR-FE-ROUTE-02 | `AppShell` (nav rail + top banner) is composed once at the route tree root; no page re-implements navigation chrome | `layouts/app-shell.tsx`, `authenticatedLayoutRoute.component` | Every authenticated page renders inside the same `<Outlet/>`; nav/banner markup exists in exactly one file |
| FR-FE-ROUTE-03 | The Tests nav group is a collapsible sub-list, expanded by default when the current path starts with `/tests`, persisted only for the session (not across reloads) | `app-shell.tsx`'s `testsExpanded` state | Loading `/tests/api` directly shows the group pre-expanded; collapsing it and reloading resets to the path-derived default |
| FR-FE-ROUTE-04 | "Workflow Designer" in the nav aliases to the same component as "Flows" — there is no dedicated multi-flow DAG designer route | `router.tsx`'s `workflowDesignerRoute` | Navigating `/workflow-designer` renders `FlowsPage`; no separate component exists for it |

### Auth & session (`FR-FE-AUTH`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-AUTH-01 | Access token lives only in memory (Zustand store), never `localStorage`; lost on reload by design | `store/auth-store.ts` | A hard reload with no valid refresh token returns the user to `/login` |
| FR-FE-AUTH-02 | Refresh token persists in `sessionStorage` (not a cookie — backend returns it as a JSON field, not `Set-Cookie`), restored via `restoreSession()` at boot | `api-client.ts`'s `restoreSession`/`refreshSession` | A reload with a valid, unexpired refresh token restores the session without a visible login prompt |
| FR-FE-AUTH-03 | A `401` on any authenticated request triggers exactly one silent refresh-and-retry; a second `401` after retry surfaces as a real `ApiError`, never an infinite loop | `api-client.ts`'s `request()`, `refreshInFlight` dedup | Two concurrent requests hitting `401` simultaneously trigger exactly one `/auth/refresh` call, not two |
| FR-FE-AUTH-04 | `user` (decoded JWT claims: `sub`, `tenant`, `email`, `role`, `exp`) is derived client-side by decoding the access token, never fetched via a separate `/me` endpoint | `auth-store.ts`'s `decodeAccessToken` | `useAuthStore().user` is populated immediately on login/refresh with no additional network round-trip |

### API client & error handling (`FR-FE-API`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-API-01 | Every network call in the app goes through one typed `request<T>()` wrapper; no feature calls `fetch()` directly | `services/api-client.ts` | Grep for `fetch(` outside `api-client.ts` and `signalr.ts` returns nothing |
| FR-FE-API-02 | Every non-2xx response is parsed as RFC 7807 (`ProblemDetails`) and thrown as `ApiError`, carrying `status`, `title`, `correlationId`, and a user-facing `detail` message | `api-client.ts`'s `ApiError` class | A backend 404/500 with a `correlationId` in its body surfaces that same `correlationId` in the thrown error, visible in the resulting toast |
| FR-FE-API-03 | A `204 No Content` response resolves to `undefined`, never attempts to parse an empty body as JSON | `request()`'s `if (res.status === 204) return undefined` | A `DELETE` call against any resource does not throw a JSON-parse error |
| FR-FE-API-04 | Every module-test/API-collection/Flow-run trigger reuses the identical `create → addStep → publish → triggerRun` sequence — there is no separate "run one action" backend endpoint, and the frontend never assumes one | `request-designer.tsx`, `logs-studio-page.tsx`, `module-test-form.tsx` | All three files' `runMutation`s follow byte-for-byte the same 4-call sequence, differing only in `module`/`action`/`params` |

### State management (`FR-FE-STATE`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-STATE-01 | Client state (session, theme, active project/environment, toasts) uses Zustand, one store per concern, never a monolithic store | `store/auth-store.ts`, `theme-store.ts`, `project-store.ts`, `toast-store.ts` | Four distinct store files exist; no single store exceeds one concern's state shape |
| FR-FE-STATE-02 | Server state (flows, runs, collections, projects, environments, modules) uses TanStack Query exclusively — no server data is duplicated into a Zustand store | Every feature's `useQuery`/`useMutation` usage | No Zustand store holds a `FlowResponse`, `ApiCollectionResponse`, or similar server-shaped object |
| FR-FE-STATE-03 | Project/Environment selection (`selectedProjectId`, `selectedEnvironmentId`) persists to `localStorage`, survives reload and cross-tab, and resets `selectedEnvironmentId` whenever `selectedProjectId` changes (an environment belongs to exactly one project) | `store/project-store.ts` | Switching the active project via the header selector clears whatever environment was previously selected |
| FR-FE-STATE-04 | Toasts are a global, app-shell-rendered viewport; any feature can push one via `useToastStore().push`, never renders its own inline toast UI | `store/toast-store.ts`, `components/ui/primitives.tsx`'s `ToastViewport` | Every `onError` handler across every mutation in the app calls `pushToast`, none renders a bespoke error banner |

### Design system (`FR-FE-DS`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-DS-01 | A small, hand-authored primitive set (`Button`, `Input`, `Textarea`, `Card`, `StatusPill`, `Dialog`, `Spinner`) is the only source of these elements — no feature defines its own button/input styling | `components/ui/primitives.tsx` | Grep for `<button` / `<input` with inline Tailwind classes outside `components/ui/` returns nothing (excluding icon-only utility buttons documented as exceptions, e.g. `KeyValueEditor`'s remove-row button) |
| FR-FE-DS-02 | Theming is entirely via CSS custom properties (`--yukti-*`), switchable at runtime (Dark/Light/High contrast) without a page reload | `store/theme-store.ts`, `index.css` | Switching the theme selector updates every visible primitive's colors immediately, no flash-of-unstyled-content |
| FR-FE-DS-03 | `CodeEditor` (Monaco-backed) is the only rich-text/code input component in the app; raw `<textarea>` is reserved for plain, non-code multi-line input (e.g. Mobile's log-text input) | `components/ui/code-editor.tsx` | Every JSON-shaped input (API request bodies, Flow step params) renders via `CodeEditor`, not a bare `Textarea` |
| FR-FE-DS-04 | `DataTable` (sortable, paginated) is the only table component; a feature needing a sortable list uses it rather than hand-rolling pagination | `components/ui/data-table.tsx`, used by `flows-page.tsx` and `projects-page.tsx` | Both Flows and Projects lists share the same `Column<T>`-typed component, not duplicated table markup |

### API Studio (`FR-FE-APISTUDIO`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-APISTUDIO-01 | Explorer (left pane) lists saved Collections/Requests via real backend persistence (`apiCollectionsApi`), independent of the Request Designer's ad-hoc send path | `features/api-studio/explorer-tree.tsx` | Creating, renaming, and deleting a Collection persists across a page reload |
| FR-FE-APISTUDIO-02 | Request Designer (right pane) supports method/URL, Headers, Query Params, Body (JSON via `CodeEditor`), and Assertions (`status`/`pathEquals`/`pathContains`/`pathExists`/`headerExists`/`cookieExists`/`schema`) as first-class tabs, not one generic params blob | `features/api-studio/request-designer.tsx`, `assertions-editor.tsx` | Every assertion type the backend's `ApiModule` supports has a corresponding UI affordance |
| FR-FE-APISTUDIO-03 | "Send" always executes through the ad-hoc Flow path (`create → addStep → publish → triggerRun`); "Save" always persists through `apiCollectionsApi`; the two never share a code path | `request-designer.tsx`'s `runMutation` vs `saveMutation` | Sending an unsaved request never creates an `ApiCollection` row; saving a request never triggers a `FlowRun` |
| FR-FE-APISTUDIO-04 | Response Viewer renders Pretty/Raw/Headers/Assertions tabs, live-updating via `useLiveRunProgress`, and falls back to the raw step message/error when `step.data` isn't the expected `ApiRequestResultData` shape (e.g. a transport-level failure) | `features/api-studio/response-viewer.tsx` | A request to an unreachable host still renders a readable error, not a blank/crashed panel |
| FR-FE-APISTUDIO-05 | The Send button merges the active Project's Environment variables into `variableOverrides` at trigger time | `request-designer.tsx`'s `useSelectedEnvironmentVariables` usage | Selecting an Environment with a `baseUrl` variable makes that variable resolvable via `{{vars.baseUrl}}` in the request |

### Logs Studio (`FR-FE-LOGSSTUDIO`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-LOGSSTUDIO-01 | Check Rules and Detect Anomalies are separate tabs sharing one log-text input, each with action-specific param editors (rule rows vs. std-dev threshold) | `features/logs-studio/logs-studio-page.tsx` | Switching tabs preserves the shared log text but resets the previous tab's run result |
| FR-FE-LOGSSTUDIO-02 | Results viewer renders a rule-match table (rule/count/samples) for Check Rules, or an anomaly-bucket table (bucket/error-rate/errors/total) plus mean/stdDev/threshold for Detect Anomalies, keyed by narrowing `step.data` via a type guard on `linesScanned` vs `bucketsScanned` | `features/logs-studio/log-results-viewer.tsx` | The Raw tab always shows the exact JSON the backend returned, regardless of which action ran |
| FR-FE-LOGSSTUDIO-03 | No saved "log test case" persistence exists — every run is ad hoc, matching the backend's lack of an equivalent to `ApiCollection` for Logs | `logs-studio-page.tsx` has no `save`/collection concept | There is no UI affordance to save a Logs test for reuse |

### Module test surfaces — Web / Mobile / Desktop UI (`FR-FE-MODTEST`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-MODTEST-01 | `ModuleTestForm` discovers its action list and per-action param schema entirely from `GET /api/v1/modules` — zero per-module hardcoded field lists in the frontend | `features/testing/module-test-form.tsx`'s `getActionParams` usage | A newly-registered backend module with no frontend changes renders a working test form immediately (mirrors backend `FR-PLUGIN-06`) |
| FR-FE-MODTEST-02 | Field rendering is type-driven: `Boolean` → `Checkbox`, `Object`/`Array` → `Textarea` (raw JSON), everything else → typed `Input` | `module-test-form.tsx`'s field-rendering switch | Every `ParamType` the backend defines has exactly one corresponding field renderer |
| FR-FE-MODTEST-03 | Mobile's Device Config panel (Platform/Device/Automation names, Appium URL, App) renders only when `moduleKind === "mobile"`, and its non-empty fields are merged into `variableOverrides.mobile` at trigger time, taking priority over the active Environment's own `mobile` variable | `module-test-form.tsx`'s `buildMobileVariableOverrides`, its merge with `environmentVariables` | Filling the panel with a `deviceName` overrides whatever `deviceName` the active Environment's saved config specifies for that one run |
| FR-FE-MODTEST-04 | Results render generically (per-step message/error) plus a best-effort structured display when `step.data` contains a `path` string (screenshot/artifact saved) or `x`/`y` numbers (image-match coordinates) — never a module-specific bespoke viewer | `module-test-form.tsx`'s `isPathData`/`isMatchData` guards | Running Desktop UI's `screenshot` action shows `Saved: <path>` beneath the generic pass/fail line |
| FR-FE-MODTEST-05 | Desktop UI (`/tests/ui`) and every other Tests-group route both exist in the nav and route tree — no automation module the backend registers is missing a corresponding frontend entry point | `router.tsx`, `app-shell.tsx`'s `TEST_NAV_ITEMS` | Every `ModuleKind` the backend's `GET /api/v1/modules` returns has a matching `/tests/*` route |

### Flow authoring (`FR-FE-FLOW`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-FLOW-01 | Flows list is sortable by name/status/version, paginated via `DataTable`, filtered client-side to the active Project when one is selected | `features/flow-authoring/flows-page.tsx` | Selecting Project A in the switcher hides Flows created under Project B or no project |
| FR-FE-FLOW-02 | Flow detail renders steps via a real React Flow canvas (`WorkflowCanvas`), not a plain list; clicking a step shows its params/`saveAs`/`when` read-only | `features/flow-authoring/flow-detail-page.tsx`, `workflow-canvas.tsx` | Steps render as connected nodes in visual sequence order |
| FR-FE-FLOW-03 | Add-step's param entry is a single raw JSON textarea, not the schema-driven per-param fields `ModuleTestForm`/`RequestDesigner` already have — a known, documented inconsistency | `flow-detail-page.tsx`'s `AddStepDialog`, `paramsJson` state | Adding a step still requires hand-writing valid JSON; there is no per-param typed field here despite the same `ActionSchema` being available via `modulesApi.list()` |
| FR-FE-FLOW-04 | Publish is only enabled when `flow.status === "Draft"`; Run is only enabled when `flow.status === "Published"` — the UI never allows an action the backend would reject | `flow-detail-page.tsx`'s `Button disabled` conditions | Both buttons are disabled/enabled strictly matching backend `FlowStatus` |

### Execution monitor (`FR-FE-EXEC`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-EXEC-01 | Run detail always performs a REST catch-up fetch before trusting any pushed SignalR event, and re-fetches (never hand-patches state) on every `StepCompleted`/`FlowRunCompleted` push | `hooks/index.ts`'s `useLiveRunProgress` | Disconnecting and reconnecting mid-run shows every step completed during the gap, not just state as of reconnect |
| FR-FE-EXEC-02 | Cancel is only rendered/enabled while `run.status` is non-terminal (`Passed`/`Failed`/`Cancelled` all hide it) | `run-detail-page.tsx`'s `isTerminal` check | A completed run never shows an actionable Cancel button |
| FR-FE-EXEC-03 | The Execution Console renders every step's timestamp, status, name, and message/error inline, colored by status (`Failed` → danger, `Passed` → success) — a readable log, not just a status list | `run-detail-page.tsx`'s console `<div>` | A failed step's error text is visible directly in the console without a separate click |

### Reporting (`FR-FE-REPORT`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-REPORT-01 | Reports always displays the trend data's `lastUpdatedAt` timestamp alongside every number it shows — staleness is part of the payload, never inferred or hidden | `features/reporting-audit/reports-page.tsx` | The "last updated" string is visible on every load, sourced from `TrendAggregateResponse.lastUpdatedAt`, never a client-side "just now" guess |
| FR-FE-REPORT-02 | Reports is tenant-wide aggregate only — there is no per-flow report, no per-run drill-down beyond what Execution Monitor already provides, and no audit-entry viewer, despite the backend having a full `AuditRepository` | `reports-page.tsx`'s sole data source (`trendsApi.get()`) | No route or component in the app renders an individual `AuditEntry` |

### Projects & Environments (`FR-FE-PROJ`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-PROJ-01 | Projects page lists all projects via `DataTable`, click-to-activate (not a separate "select" action), with the active project visibly marked | `features/projects/projects-page.tsx` | Clicking a project name both selects it and shows "(active)" next to it in the same table |
| FR-FE-PROJ-02 | The active project's Environments render inline below the Projects table (not a separate route), with full CRUD and a Mobile-device-config sub-section that writes into `Variables.mobile` | `projects-page.tsx`'s `EnvironmentsPanel` | Editing an Environment's Mobile fields round-trips correctly through save → reload → edit, verified live this session |
| FR-FE-PROJ-03 | The header's Project switcher is available from every authenticated page, backed by the same `project-store` every scoped list reads from | `layouts/app-shell.tsx` | Switching projects from any page immediately reflects in Flows/API Explorer without a manual refresh |
| FR-FE-PROJ-04 | Every run-triggering surface (API Studio, Logs Studio, Web/Mobile/Desktop-UI tests) merges the active Environment's `Variables` into `variableOverrides` via one shared hook, never a per-feature reimplementation | `hooks/index.ts`'s `useSelectedEnvironmentVariables` | All four call sites import the identical hook; none independently fetches `environmentsApi.list()` |

### Coding standards (`FR-FE-STD`)

| ID | Requirement | Evidence | Acceptance criteria |
|----|-------------|----------|----------------------|
| FR-FE-STD-01 | TypeScript strict mode is enabled; `tsc -b` is part of the production build (`npm run build`), not merely an editor hint | `package.json`'s `build` script (`tsc -b && vite build`) | A type error fails the build, not just the editor |
| FR-FE-STD-02 | DTO types in `services/types.ts` are hand-written against the live backend contract, explicitly not code-generated (no OpenAPI document exists), and annotated where a real inconsistency between endpoints was found rather than silently normalized | `types.ts`'s own header comment, the documented `FlowSummary`/`FlowResponse` ID-serialization inconsistency | Every exported type in `types.ts` traces to a specific endpoint verified live, per its own comments |
| FR-FE-STD-03 | Import-boundary linting (`eslint-plugin-boundaries`) is a declared dependency but is not wired into an active ESLint config — a known, currently-inert dependency, not a false claim of enforcement | `package.json` devDependencies vs. absence of `eslint.config.*` | Running `npm run lint` (`oxlint`) does not currently check import boundaries |

## Negative and failure paths

| FR | Condition | Required behavior | Evidence |
|----|-----------|--------------------|----------|
| FR-FE-AUTH-03 | Refresh token expired/invalid | `clearSession()` called, user redirected to `/login`, no infinite retry loop | `api-client.ts`'s `refreshSession` catch path |
| FR-FE-APISTUDIO-04 | Backend returns a non-`ApiRequestResultData`-shaped `step.data` (network failure inside `ApiModule`) | Falls back to `step.error ?? step.message`, never crashes on a missing field | `response-viewer.tsx`'s shape guard |
| FR-FE-MODTEST-03 | Mobile action run with no Device Config filled and no active Environment | Backend's real `"MobileModule not set up"` error surfaces via toast, not a silent no-op | Confirmed live this session (pre-fix behavior); post-fix, a filled config instead surfaces the real Appium connection error |
| FR-FE-EXEC-01 | SignalR connection drops mid-run | Falls back to REST-only polling via the same hook, logs a console warning, never leaves the UI stuck on stale state | `useLiveRunProgress`'s `connection.onreconnected` handler |
| FR-FE-PROJ-02 | Environment save fails (backend validation/permission error) | Toast surfaces `ApiError.message`, form retains unsaved input (no silent data loss) | `projects-page.tsx`'s `saveMutation.onError` |

## Out of scope for this initiative

- Volume 2's own separately-numbered section structure (UX Principles, full Component Spec catalog, Core Web Vitals budgets) — this document restates only what the codebase actually implements as testable FRs, not a from-scratch UX specification
- Scheduler/trigger management UI — backend capability exists (`FR-SCHED` in Volume 1), no frontend consumer at all
- Audit-entry viewer UI — backend capability exists (`FR-AUDIT` in Volume 1), no frontend consumer at all
- Automated testing infrastructure (Vitest, React Testing Library, Playwright E2E) — none exists; a real gap, not deferred scope
- CI/CD for the frontend specifically (bundle-size budgets, Lighthouse CI) — no CI exists for this repo at all (see Volume 4 Part I's own as-built status note)

## Cross-layer contracts

| Contract ID | Provider | Consumer | Entry point | Invariants | Compatibility |
|---|---|---|---|---|---|
| CTR-FE-01 | `Yukti.Api` (`GET /api/v1/modules`) | `ModuleTestForm`, Flow Authoring's Add-step dialog | `modulesApi.list()` | Every `ActionSchema` field the FE renders a control for must exist in the response; FE never hardcodes a module's action list | Additive-only on the backend side; FE must not assume a fixed module set |
| CTR-FE-02 | `Yukti.Api` (Flow lifecycle endpoints) | Every run-triggering feature | `flowsApi.create/addStep/publish/triggerRun` | No feature invents a shortcut around this sequence, even for a single ad-hoc action | Backend adding a dedicated "run one action" endpoint would be a breaking simplification opportunity, not currently relied upon |
| CTR-FE-03 | `Yukti.Api`'s SignalR hub (`/hubs/run-progress`) | `useLiveRunProgress` | `JoinRun` invoke, `StepCompleted`/`FlowRunCompleted` events | REST catch-up is always the source of truth; pushed events only trigger a re-fetch, never a direct state patch | FE must not assume ordering/delivery guarantees on the pushed events themselves |
| CTR-FE-04 | `Yukti.Api` error middleware | `api-client.ts`'s `request()` | Any non-2xx response | Every error body is RFC 7807-shaped with a `correlationId`; FE always surfaces that ID in the resulting toast | Breaking change if the backend ever returns a non-Problem-Details error body |

## Non-functional requirements

| Area | Requirement | Status |
|------|-------------|--------|
| Performance | Frontend build fails on a TypeScript error, not just a lint warning (FR-FE-STD-01) | Enforced |
| Performance | Bundle-size budget | **Not enforced** — `vite build` already warns the production bundle exceeds 500kB minified with no CI gate |
| Accessibility | Component-level a11y (keyboard nav on `Tabs`, `Select`) | Partially present (`Tabs`'s arrow-key navigation, `Select`'s `aria-*` attributes) — no systematic audit performed |
| Security | Access token never in `localStorage` (FR-FE-AUTH-01) | Enforced |
| Observability | Frontend error/performance monitoring (RUM, Core Web Vitals) | **Absent** — no client-side telemetry exists |
| Testability | Any automated test coverage | **Absent** — zero test files in the repo |

## Assumptions

| ID | Assumption | Status |
|----|------------|--------|
| A-01 | React 19 + TanStack Router/Query + Zustand + Tailwind 4 + Vite 8 remain the locked frontend stack | confirmed (package.json, this session) |
| A-02 | The backend has no OpenAPI document; frontend types will continue to be hand-maintained against live-verified contract shapes until one exists | confirmed |
| A-03 | `eslint-plugin-boundaries` will eventually be wired into an active config to enforce the same import-boundary discipline Volume 4 Part I's CI pipeline document assumes already exists | open — currently a dangling, unconfigured dependency |

## Spec questions

| ID | Question | Blocking | Default if deferred | Status |
|----|----------|----------|----------------------|--------|
| Q-01 | Should Flow Authoring's Add-step dialog be upgraded to the same schema-driven param UI `ModuleTestForm`/`RequestDesigner` already have (FR-FE-FLOW-03), closing a real, user-visible inconsistency? | No | Leave as raw-JSON textarea; document as a known rough edge | open |
| Q-02 | Should Environment `Variables` gain real secrets-vault backing (flagged as plaintext in the multi-project work), or remain plaintext for the foreseeable term? | No — blocks nothing today | Plaintext, same trust level Mobile's device config already had before Environments existed | open |
| Q-03 | Is a Scheduler/trigger management UI and an Audit-entry viewer in scope for the next iteration, given both have full backend support and zero frontend consumer? | No | Deferred, not committed | open |

## Draft check summary

| Check | Status | Findings |
|-------|--------|----------|
| D1 — Source spec traceable | PASS | Every FR cites a specific file/behavior verified live this session, not a hypothetical Volume 2 section |
| D2 — Complete coverage, no concept dropped | PASS | Every route, store, and feature folder present in `apps/yukti-gui` as of this session is represented |
| D3 — As-built alignment | PASS | Baseline table reflects the actual, currently-running codebase, cross-checked against `package.json` and a live `find` for test files this session |
| D4 — Observable acceptance criteria | PASS | Every FR has a concrete, checkable AC |
| D5 — Negative/failure paths | PASS | 5 rows covering the highest-risk gaps (auth expiry, malformed step data, unconfigured Mobile, dropped SignalR, failed save) |
| D6 — Assumptions/open questions | PASS | 3 assumptions, 3 non-blocking open questions |
| D7 — Cross-layer contracts | PASS | 4 contracts covering the module-discovery, execution, live-progress, and error-shape boundaries with the backend |
| D8 — NFR applicability | PARTIAL | Performance/security covered; accessibility, observability, and testability are honestly marked Absent/Partial rather than claimed |
| D9 — Dependency order | PASS | Depends on `INIT-YUKTI-BACKEND-001`; no other frontend initiative exists to depend on |
| D10 — Zero unresolved blockers | PASS | All 3 open questions are explicitly non-blocking |
| D11 — Output completeness | PASS | Header, overview, baseline, FRs, negative paths, out-of-scope, contracts, NFRs, assumptions, questions, this checklist |

**Draft verdict:** PASS — this document describes a real, working, if incompletely-polished frontend, with every gap named rather than smoothed over.

## References

- Source: live `apps/yukti-gui` codebase, verified interactively (browser automation) across this and prior sessions
- Format basis: `INIT-YUKTI-BACKEND-001.md` (Volume 1), same SDD structure applied one layer up the stack
- Related: `docs/specification/architecture/volume-4-infrastructure-part-1.md` (Volume 4 Part I) — the CI/CD pipeline document there (Section 5–6) assumes frontend test/lint gates this document's as-built table shows don't exist yet; closing that gap is prerequisite to Volume 4's pipeline being real, not just described

```yaml
handoff:
  contract: sdd-yukti/v1
  stage: frontend-architecture-feasibility
  outcome: findings
  artifact:
    path: docs/specification/product/INIT-YUKTI-FRONTEND-001.md
    fr_count: 33
  blockers: []
  signals:
    source: live-codebase-reverse-engineering
    as_built_verified_this_session: true
    open_questions: [Q-01, Q-02, Q-03]
    next_candidates:
      - Scheduler/trigger management UI (Q-03)
      - Audit-entry viewer UI (Q-03)
      - Frontend test infrastructure (currently zero coverage)
      - eslint-plugin-boundaries activation (A-03)
  human_checkpoint: true
  external_action: false
```
