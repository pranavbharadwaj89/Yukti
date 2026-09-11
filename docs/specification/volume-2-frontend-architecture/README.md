# Volume 2 — Frontend Architecture · Part I — UX Foundations


## Table of Contents

  - [Section 1 — UX Principles](#1-ux-principles)
  - [Section 2 — Design System](#2-design-system)
  - [Section 3 — Theme System](#3-theme-system)
  - [Section 4 — React Architecture](#4-react-architecture)
  - [Section 5 — Folder Structure](#5-folder-structure)
  - [Section 6 — Feature Modules](#6-feature-modules)
  - [Section 7 — Routing](#7-routing)
  - [Section 8 — State Management](#8-state-management)
  - [Section 9 — Component Library](#9-component-library)
  - [Section 10 — Monaco Integration](#10-monaco-integration)
  - [Section 11 — Workflow Designer](#11-workflow-designer)
  - [Section 12 — React Flow Integration](#12-react-flow-integration)
  - [Section 13 — Plugin UI](#13-plugin-ui)
  - [Section 14 — Authentication](#14-authentication)
  - [Section 15 — SignalR Integration](#15-signalr-integration)
  - [Section 16 — Offline Support](#16-offline-support)
  - [Section 17 — Performance](#17-performance)
  - [Section 18 — Accessibility](#18-accessibility)
  - [Section 19 — Testing Strategy](#19-testing-strategy)

---

## 1. UX Principles

### 1.1 Purpose and relationship to Volume 0 and Volume 1

Volume 0 established five personas (Section 10) and a specific usability target — NFR-UX-2: a user experienced with an existing single-domain tool authors a basic flow within 30 minutes of first exposure, with no external training. Volume 1 specified the API and data contracts (Part V) this frontend consumes. This section establishes the UX principles that govern every design and implementation decision in Volumes 2 in full — the same role Volume 0's Product Philosophy (Part I, Section 4) and Volume 1's Architecture Principles (Part IV, Section 20) played for their respective volumes. Every subsequent section in this volume should be checkable against the principles stated here.

### 1.2 Principle 1 — The GUI has no authority the API doesn't already have

Direct UX consequence of the architectural rule established in Volume 1, Part I Section 19.2 and Part V, Section 30.3: the GUI is a client of the REST API, never a privileged shortcut. This means, concretely: every validation the GUI performs client-side (e.g., warning that a flow step references an unresolved variable before publish) must be a *convenience preview* of a check the API will also enforce authoritatively — never the only place a check happens. A user should never be able to do something in the GUI that circumvents server-side authorization (Volume 1, Part IV, Section 25) or domain invariants (Volume 1, Part II, Section 9). This principle protects against a common and serious class of security/consistency bug where a frontend team's convenience validation silently becomes the *only* validation over time.

### 1.3 Principle 2 — Show the system's real state, never a fiction of it

Directly extending Architecture Principle 20.4 (fail loud, fail informatively, Volume 1 Section 20.4) into the UX layer: the GUI never fabricates optimistic state that could diverge from what the backend actually persisted. Where optimistic UI updates are used for responsiveness (Section 8, State Management, will specify exactly where), they are always reconciled against the authoritative server response, and any divergence is visibly corrected, never silently left inconsistent. This is the direct UX expression of Volume 1, Part V Section 31.4's SignalR reconnection design ("never assume no events were missed... re-fetch on reconnect") generalized as a governing principle beyond just the live-progress view.

### 1.4 Principle 3 — Every module is a first-class citizen in the UI, with zero hardcoding

The single most consequential principle in this volume, directly inherited from Volume 0's Module Parity Index (Section 6.3) and Volume 1's repeated architectural enforcement (Architecture Principle 20.3; Part III, Section 18.6's `GetSupportedActions()` auto-surfacing design): **no UI component may contain module-specific branching logic** (e.g., `if (module === 'web') { renderWebSpecificEditor() }`). The flow-step editor renders itself entirely from the `ActionSchema` metadata the backend already exposes (Volume 1, Part II, Section 14.2's `GetAvailableModuleActionsQuery`) — a newly-registered marketplace module must appear, fully editable, with zero frontend code changes, exactly as Volume 0's UC-6 (Persona Raj) requires. This principle is tested directly, not just followed by convention: Section 19 (Testing Strategy) will specify an automated test that registers a synthetic module and asserts the GUI renders it correctly without any module-specific code path existing.

### 1.5 Principle 4 — Progressive disclosure over exhaustive surface area

Directly serving NFR-UX-2's 30-minute time-to-first-flow target: the GUI defaults to the simplest path for common tasks (a Flow Author building a single-module flow sees a clean, minimal editor) while making advanced capability (conditional steps, retry policy tuning, cross-module variable chaining, Workflow-level orchestration per Volume 1 Part III Section 20) available but not front-loaded. This is a direct rejection of "expose every backend capability with equal visual prominence," which would optimize for completeness at the cost of the specific, measured usability target Volume 0 committed to.

### 1.6 Principle 5 — Design for the five personas' five different relationships to the product

Restating Volume 0, Section 10.7's persona-to-interface table as a binding UX constraint: Priya needs an efficient authoring loop, Sofia needs a trustworthy reporting surface she can walk into a leadership review with, Marcus barely touches the GUI at all (his primary surface is the CLI/API, per Volume 1 Part V Section 30), Raj needs the plugin-authoring experience to be self-explanatory from documentation alone, and Elena needs the GUI's own security posture (session handling, no client-side credential exposure) to hold up under scrutiny without needing to be a GUI user herself. A design decision that serves one persona at another's direct expense (e.g., an authoring-optimized interface so dense with power-user features that Sofia's occasional dashboard visits become confusing) should be treated as a conflict requiring explicit resolution, not an acceptable default trade-off absorbed silently.

### 1.7 Principle 6 — Transparency about AI, always

Direct UX carry-through of Volume 0's Product Philosophy 4.4 ("AI proposes, deterministic code disposes, never silently") and Volume 1's `AiAttribution` value object (Part II, Section 11.5): anywhere the GUI displays a result that involved AI assistance — a generated flow, a self-healed selector, an AI-triaged log — the interface visibly and unambiguously marks it as AI-assisted, with the underlying attribution data (confidence score, original vs. suggested value) inspectable, never hidden behind an extra click a user might reasonably not take. This is a hard UX requirement, not a nice-to-have: Section 18 (Accessibility) and Section 10 (Workflow Designer) will both specify concrete rendering treatments that satisfy this principle in their respective surfaces.

### 1.8 Principle 7 — The interface degrades honestly, never pretends full capability

Directly extending Volume 1's AI-optionality architecture (NFR-REL-3) and multi-tenancy/licensing tiering (Volume 0, Section 16) into the UX layer: when a capability is unavailable — AI disabled by tenant configuration, a licensing tier that doesn't include cross-module analytics (Volume 0, Section 16.2), a module at Volume 0's "Preview" maturity level (Section 14.10) — the GUI states this plainly (a disabled control with an explanatory tooltip, not a hidden or broken one) rather than presenting a uniform interface that silently fails or behaves inconsistently depending on invisible backend configuration.

### 1.9 How these principles resolve design disputes

Consistent with the pattern established by Volume 1's Architecture Principles (Section 20.9 there): every subsequent section in this volume that makes a non-obvious design choice should cite the specific principle number here (1.2 through 1.8) that motivated it, rather than justifying from scratch — this is the same internal-consistency discipline Volume 0 and Volume 1 both established, now extended to the frontend.



2. Design System
2.1 Purpose and starting point

This section formalizes Nexus's visual design language into a reusable token system that the Component Library (Section 9) implements. Rather than inventing a visual identity from a blank page, this design system codifies and extends the "instrument panel" aesthetic already prototyped and validated earlier in this project's exploration phase (the Nexus control-deck dashboard) — a deliberate choice consistent with UX Principle 1.3's "every module is a first-class citizen": an engineering-tool aesthetic (dense, precise, technical-monospace-forward) suits a platform whose primary users are engineers (Priya, Marcus, Raj) far better than a generic SaaS-dashboard look borrowed from a consumer product.

2.2 Design rationale — why "instrument panel," not generic SaaS

Three reasons, each traceable to Volume 0: first, Persona positioning (Section 10) — every primary persona except Sofia is a hands-on technical practitioner, and Sofia's own need (Volume 0, UC-5) is for a trustworthy, data-dense reporting view, which an instrument-panel aesthetic serves better than a soft, decorative consumer style. Second, competitive differentiation (Volume 0, Section 9) — the Archetype A/B incumbents largely use conventional, interchangeable SaaS visual language; a distinct, confident technical aesthetic is a small but real differentiator supporting Volume 0's "not shallow, not generic" positioning concern (Section 8.6's credibility-tax risk). Third, and most directly: the aesthetic must communicate precision and control over six genuinely different automation domains simultaneously — a visual language built around clear signal/status states (pass/fail/warn, live/idle, AI-assisted/deterministic) rather than decorative flourish directly serves UX Principle 1.7's "degrade honestly, show real state" requirement.

2.3 Color tokens
Token	Value	Usage
--nexus-bg	
#0c1015	Application background
--nexus-panel	
#12171f	Primary surface/card background
--nexus-panel-2	
#161c25	Secondary/nested surface background
--nexus-line	
#232a35	Default border/divider
--nexus-line-strong	
#333c4a	Emphasized border, focus-adjacent
--nexus-text	
#e7eaee	Primary text
--nexus-muted	
#8892a0	Secondary text, labels
--nexus-dim	
#5b6472	Tertiary text, disabled-state text
--nexus-amber	
#e8a33d	Primary action color, "live"/active signal
--nexus-teal	
#4fb0a5	Secondary signal, AI-assisted indicator, "verified" states
--nexus-red	
#e5534b	Failure/error signal
--nexus-green	
#57ab5a	Pass/success signal

Every color token maps to a specific semantic meaning, never used decoratively — this is a direct, load-bearing design decision, not a stylistic footnote: --nexus-amber always means "primary action or actively live," --nexus-teal always means "AI-related or verified-trust," --nexus-red/--nexus-green always and only mean fail/pass. A component using --nexus-teal for a purpose unrelated to AI or trust verification is a design-system violation, because it would erode the signal-color vocabulary's reliability — a user should be able to infer meaning from color alone, consistently, across all six module surfaces (UX Principle 1.3).

2.4 Typography

Two type families, each with a deliberate, non-interchangeable role:

IBM Plex Mono — used for all data readouts, code/parameter values, identifiers (Flow IDs, correlation IDs per Volume 1 Part V Section 30.5), module/action names, and status labels. The monospace choice is functional, not aesthetic: it makes tabular data (report tables, parameter lists) align predictably and gives technical values (a selector string, a JSON parameter) unambiguous visual distinction from prose.
IBM Plex Sans — used for all prose: descriptions, help text, navigation labels, body copy in dialogs.

A component mixing the two families for the same semantic role (e.g., using Mono for one status label and Sans for another) is a design-system violation, consistent with the same rigor Section 2.3 established for color.

2.5 Spacing and layout grid

An 4px base unit, with a defined scale (4, 8, 12, 16, 20, 24, 32, 40, 56px) — every margin, padding, and gap value in the Component Library (Section 9) is drawn from this scale, never an arbitrary value, to keep visual rhythm consistent across the five distinct product surfaces (flow authoring, reporting dashboard, module marketplace, Workflow Designer, admin/settings).

2.6 Signal states — the vocabulary that spans all six modules

Directly serving UX Principle 1.3: a single, closed set of visual "signal states" is defined once and reused identically regardless of which module a given step or result belongs to — this is the visual-design-system counterpart to Volume 1's StepStatus/RunStatus closed enums (Part II, Section 11.3).

Signal state	Visual treatment	Backing domain concept
Passed	Green dot/badge, --nexus-green	StepStatus.Passed
Failed	Red dot/badge, --nexus-red	StepStatus.Failed
Skipped	Dim/outline badge, --nexus-dim	StepStatus.Skipped
Running/Live	Amber pulsing dot, --nexus-amber	RunStatus.Running
AI-assisted	Teal badge with a distinct icon, always paired with visible confidence %	Non-null AiAttribution (Volume 1, Part II §11.5)
Flaky (passed on retry)	Green badge with an amber outline/asterisk	Non-empty RetryHistory on a Passed StepResult (Volume 1, Part II §10.4)

Every one of these six states is rendered identically wherever it appears — the flow-run detail view, the live-progress stream, the reporting dashboard's trend charts — precisely because the underlying domain concepts they represent (Volume 1) are themselves module-agnostic, closed sets. This table is the single source of truth the Component Library's status-badge component (Section 9) implements against.

2.7 Iconography

A single, consistent icon set (Lucide, chosen for its comprehensive coverage and consistent stroke-width visual language) is used throughout, with a specific, documented icon assigned per ModuleKind (Volume 1, Part II, Section 11.2) — including a defined fallback icon for ModuleKind.Custom marketplace modules that haven't supplied their own icon, since UX Principle 1.3 requires the interface to handle an arbitrary, previously-unknown module gracefully, not just the six built-in ones.

2.8 Motion and animation principles

Motion is used exclusively to communicate state change, never for decorative effect — a step transitioning from "running" to "passed" animates that specific transition; nothing animates on page load or idle for visual flourish alone. Duration and easing values are similarly tokenized (--nexus-motion-fast: 150ms, --nexus-motion-standard: 250ms) and reused consistently. This restraint is a direct expression of UX Principle 1.2's "show real state" — decorative motion that doesn't correspond to an actual state change risks implying activity or change that isn't really happening.

2.9 Design system governance

Consistent with Volume 1, Part VI Section 40.8's ADR-for-deviations discipline: any new color, type treatment, or signal state not defined in this section requires a documented design-system extension proposal, reviewed against Section 2.2's rationale, before implementation — preventing the gradual, uncoordinated visual drift that undermines a design system's core value (predictability) over a project's lifetime.



3. Theme System
3.1 Purpose

This section specifies the technical implementation of Section 2's design tokens as a runtime-switchable theme system — distinct from Section 2's job of defining what the tokens are, this section defines how they're delivered to and consumed by every component in the Component Library (Section 9) and every feature module (Section 6).

3.2 CSS custom properties as the token delivery mechanism

Every token from Section 2.3–2.8 is implemented as a CSS custom property (--nexus-*, following the naming convention already shown throughout Section 2), scoped at the document root and consumed by components exclusively through these properties — never as hardcoded hex/pixel values anywhere in component code. This is a hard, statically-enforced rule (a stylelint rule rejecting hardcoded color/spacing values outside the token-definition file itself), directly analogous in spirit to Volume 1's Roslyn-analyzer-enforced coding standards (Part VI, Section 40.2) — design-system consistency is enforced by tooling, not review vigilance alone.

css
:root[data-theme="dark"] {
  --nexus-bg: #0c1015;
  --nexus-panel: #12171f;
  /* ...full token set from Section 2.3-2.8 */
}

:root[data-theme="light"] {
  --nexus-bg: #f7f8fa;
  --nexus-panel: #ffffff;
  --nexus-text: #1a1f26;
  /* ...a fully-specified light-mode counterpart for every dark-mode token,
     never a partial override — see 3.4 for why partial theming is prohibited */
}
3.3 Dark mode as the default, light mode as a fully-supported alternative

Dark mode (Section 2's instrument-panel aesthetic) is the default theme, consistent with Section 2.2's rationale — but light mode is a fully-specified, equally-complete alternative, not an afterthought or a partial "invert some colors" implementation. This matters for two Volume 0-traceable reasons: accessibility (some users have genuine visual/environmental needs that dark mode doesn't serve, directly relevant to NFR-UX-1's WCAG 2.1 AA requirement, Section 18 of this volume will elaborate) and enterprise environments (some organizations have workplace or brand-consistency policies favoring light interfaces, relevant to the Sofia/Elena personas' more occasional, business-context GUI usage).

3.4 Why partial/override-only theming is prohibited

A tempting shortcut is defining light mode as a small set of overrides layered on top of the dark-mode token set (only redefining --nexus-bg and --nexus-text, for instance, leaving other tokens computed or assumed). This is explicitly prohibited: it inevitably produces subtly broken combinations (a signal-state badge color, Section 2.6, that has acceptable contrast against a dark panel but fails WCAG contrast requirements against a light one) that are difficult to catch without exhaustive per-combination testing. Every theme is a complete, independently-specified token set, verified independently against Section 18's accessibility contrast requirements — more upfront specification work, deliberately accepted in exchange for eliminating an entire class of theme-interaction bug.

3.5 Theme resolution and persistence
typescript
type ThemePreference = 'dark' | 'light' | 'system';

function resolveTheme(preference: ThemePreference): 'dark' | 'light' {
  if (preference === 'system') {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  return preference;
}

A user's theme preference (including system, which follows the OS-level setting) is persisted server-side as part of their user profile (a straightforward extension of the User entity, Volume 1 Part II Section 10.6, or a lightweight associated preferences record — a Volume 1 Part V schema addition, not specified in Volume 1 itself since it was correctly out of that volume's backend-architecture scope, but flagged here as the concrete cross-volume dependency this frontend feature creates) rather than only in browser local storage — ensuring a consistent experience across devices, directly relevant to Persona Priya potentially moving between a work laptop and a secondary machine.

3.6 Tenant-level theme/branding (enterprise consideration)

Directly anticipating Volume 0's Future Vision (Section 23.4, industry-specific editions) and the general enterprise-trust posture (Business Objective 5): the theme system's architecture supports a tenant-level accent-color override (a single additional custom property layer, applied only to --nexus-amber's role specifically, never touching the semantic signal colors in Section 2.6, which must remain universally consistent per that section's own governance rule) for enterprise customers wanting light custom branding. This is explicitly scoped as a narrow, future-facing extension point, not a GA requirement — mentioned here only so the theme system's technical design (3.2's CSS custom-property architecture) doesn't need retrofitting if and when this becomes a real roadmap item.

3.7 High-contrast mode

A third theme variant, high-contrast, is specified as a GA requirement directly serving NFR-UX-1's WCAG 2.1 AA conformance target — not merely a stretch goal. This variant increases contrast ratios beyond the standard dark/light themes' already-AA-compliant baselines (Section 18 specifies exact contrast ratio verification) and simplifies or removes decorative visual treatments (e.g., Section 2.8's motion is further reduced, gradients or subtle background treatments in Section 9's components are flattened) for users with low-vision needs — implemented as a fourth complete token set following the same non-partial-override discipline established in Section 3.4.

3.8 Theme system and the Plugin UI (forward reference)

Section 13 (Plugin UI) will specify that marketplace-sourced module UI extensions (where a Community or Verified-tier module supplies custom rendering for its own action parameters — an extension point Section 13 formalizes) consume the same theme tokens defined in this section, via a constrained, sandboxed CSS custom-property read-only interface — a third-party module's UI contribution cannot introduce its own colors or override the host application's tokens, only consume them, which is the frontend-specific expression of the same trust-boundary discipline Volume 1 established for backend plugin code (Part III, Section 18.4's ExecutionContext boundary, now mirrored here for the frontend equivalent).


____________________________________________________________________________________________________________________________________________________


4. React Architecture
4.1 Purpose and technology baseline

React is locked as the frontend framework per Volume 0, Section 21.3, chosen specifically for its ecosystem fit with Monaco (Section 10) and React Flow (Section 12). This section establishes the architectural patterns governing how React is used — component design philosophy, rendering strategy, and the boundary between this volume's concerns and Volume 1's backend contracts.

4.2 Functional components and hooks exclusively

Nexus's React codebase uses functional components with hooks throughout — no class components anywhere in new code, consistent with modern React idiom and, more specifically, because the Component Library's composability goals (Section 9) and the custom-hook-based data-fetching architecture (Section 8, State Management) both depend on hooks' composition model. This is a static-analysis-enforced rule (an ESLint rule rejecting React.Component subclassing), mirroring Volume 1's Roslyn-analyzer-enforced backend conventions (Part VI, Section 40.2) — the same "enforced by tooling, not review vigilance" discipline extended to the frontend.

4.3 Rendering strategy — client-rendered SPA, no SSR at GA

Nexus's GUI is a client-side-rendered single-page application for GA, not a server-rendered (Next.js-style) application. This is a deliberate scope decision, not an oversight: per UX Principle 1.6 (Volume 2, Section 1.6), the primary personas interacting with the GUI (Priya, Sofia) are authenticated users performing tool-like work behind a login wall — there is no public, unauthenticated, SEO-relevant surface (unlike a marketing site) that would justify SSR's added complexity. This decision is explicitly revisitable if a future public-facing surface (e.g., a public flow-template gallery, extending Volume 0's Plugin Marketplace vision, Section 17) emerges as a real requirement — noted here so a future team doesn't need to rediscover this reasoning from scratch, consistent with this document set's established practice (Volume 1, Part II, Section 6.5).

4.4 Component classification

Every component in the codebase falls into exactly one of four classifications, each with distinct rules about what it may depend on:

Classification	May contain	May depend on	Example
Primitive	Pure presentation, design-token-driven styling only	Design tokens (Volume 2, Section 2) only — no data fetching, no business logic	Badge, Button, StatusDot (Section 9)
Composite	Composition of primitives into a reusable, still-domain-agnostic unit	Primitives only	DataTable, Modal, CodeEditor wrapper (Section 9)
Feature	Domain-aware rendering, data fetching, business logic specific to one bounded context	Composites, primitives, feature-scoped hooks (Section 6)	FlowStepEditor, RunProgressView, ModuleActionPicker
Page	Route-level composition of Feature components, layout only	Feature components, routing (Section 7)	FlowEditorPage, ReportingDashboardPage

This classification is the direct frontend analogue of Volume 1's layered dependency-direction discipline (Part V, Section 36.3's Nexus.Domain → Nexus.Application → ... inward-pointing rule) — Primitives never depend on Composites, Composites never depend on Feature components, enforced by the folder structure and import-boundary rules Section 5 specifies concretely.

4.5 Data fetching — server state is never duplicated into local component state

Directly anticipating Section 8's full State Management specification: any data originating from the REST API (Volume 1, Part V, Section 30) or SignalR (Section 31 there) is treated as server state, managed exclusively through the data-fetching layer specified in Section 8, never copied into useState and manually kept in sync. This is stated here, at the architecture level, because it's a foundational rule the component-classification table in 4.4 depends on — a Feature component reads server state via a hook, it does not "own" a local copy of it.

4.6 Error boundaries — module-scoped, never a single global catch-all

Consistent with UX Principle 1.3 (module parity) and 1.7 (honest degradation): each Feature-level surface that renders module-specific content (the flow-step editor rendering an arbitrary module's action parameters, per Section 9's schema-driven rendering) is wrapped in its own React error boundary, scoped narrowly enough that a rendering failure in one module's custom UI contribution (Section 13, Plugin UI) cannot take down the entire authoring surface — directly mirroring Volume 1's Part III, Section 18.5 backend sandboxing philosophy (a misbehaving module shouldn't compromise the whole system) translated into frontend fault isolation.

tsx
function ModuleActionRenderer({ schema, module }: { schema: ActionSchema; module: ModuleKind }) {
  return (
    <ModuleErrorBoundary moduleKind={module} fallback={<ModuleRenderFailedNotice module={module} />}>
      <SchemaDrivenParamEditor schema={schema} />
    </ModuleErrorBoundary>
  );
}
4.7 TypeScript — strict mode, generated types from the API contract

The entire codebase uses TypeScript in strict mode. Critically, DTO types (Volume 1, Part II, Section 14.8's FlowDetailDto, StepResultDto, and so on) are generated, not hand-written, from Volume 1's API contract (an OpenAPI-spec-driven code generation step, consistent with Volume 1, Part V, Section 30.2's note that the endpoint inventory should be generated from, not independently maintained against, the command/query catalog) — this closes the loop so that a backend DTO shape change is caught as a frontend TypeScript compilation error immediately, rather than as a runtime bug discovered later. This generation step is a mandatory part of the CI pipeline (Volume 5's eventual responsibility to specify the exact tooling), treated as a build-breaking check if the generated types and the actual running API drift.

4.8 Concurrent rendering and Suspense

React's concurrent rendering features (Suspense for data fetching, transitions for non-urgent updates) are used deliberately for exactly two patterns: the initial load of a Feature-level surface (a route transition into the Flow Editor suspends on the initial GetFlowByIdQuery fetch, Section 8) and large, potentially-slow list renders (the reporting dashboard's trend views, Volume 1 Part II Section 14.4's GetPassRateTrendQuery, wrapped in a transition so typing in a date-range filter doesn't block the input's own responsiveness while the new data loads). This is a deliberately narrow, specified usage — not a blanket "wrap everything in Suspense" policy — consistent with this volume's general preference (inherited from Volume 1, Architecture Principle 20.7) for applying complexity only where it earns its cost.

4.9 Relationship to the remaining Part II sections

This section establishes the component and rendering philosophy; Section 5 (Folder Structure) makes the classification in 4.4 concrete as an enforced directory layout; Section 6 (Feature Modules) specifies how bounded-context-aligned features are organized; Section 7 (Routing) specifies the page-level navigation structure; Section 8 (State Management) fully specifies the server-state/client-state split introduced in 4.5.



5. Folder Structure
5.1 Purpose

This section makes Section 4.4's component classification and Volume 2's bounded-context alignment (mirroring Volume 1, Part II Section 7's Flow Authoring/Execution/Module & Plugin/Reporting & Audit/Identity & Access contexts) concrete as an enforced directory layout — the frontend counterpart to Volume 1, Part V, Section 36's package/solution structure.

5.2 Top-level structure
nexus-gui/
├── src/
│   ├── primitives/              # Section 4.4 "Primitive" components — Button, Badge, StatusDot, etc.
│   ├── composites/               # Section 4.4 "Composite" components — DataTable, Modal, CodeEditor
│   ├── design-tokens/             # Volume 2 §2-3 — CSS custom properties, theme definitions
│   │
│   ├── features/                  # Section 6 — one folder per bounded context, mirroring Volume 1 §7
│   │   ├── flow-authoring/
│   │   ├── execution/
│   │   ├── module-plugin/
│   │   ├── reporting-audit/
│   │   └── identity-access/
│   │
│   ├── pages/                     # Section 4.4 "Page" components, one per route (Section 7)
│   │
│   ├── api/                       # Section 4.7's generated DTO types + typed API client
│   │   ├── generated/              # never hand-edited — regenerated from Volume 1's OpenAPI spec
│   │   └── client.ts                # thin wrapper adding auth headers, error normalization
│   │
│   ├── state/                     # Section 8 — server-state hooks (TanStack Query) and
│   │   │                              client-only state stores (Zustand), organized by
│   │   │                              bounded context to mirror features/
│   │   ├── server/
│   │   └── client/
│   │
│   ├── realtime/                   # Section 15 — SignalR connection management
│   │
│   ├── routing/                    # Section 7 — route definitions, guards
│   │
│   ├── plugin-ui/                   # Section 13 — the sandboxed marketplace-module UI
│   │                                  extension host, kept structurally separate from
│   │                                  everything above per the trust-boundary discipline
│   │                                  Section 13 will specify in full
│   │
│   └── app/                        # composition root — App.tsx, providers, theme bootstrapping
│
├── tests/
│   ├── unit/                       # mirrors src/ structure
│   ├── integration/
│   └── e2e/                        # Section 19 — a deliberate, notable case of Nexus's own
│                                       Web module (Volume 0's product itself) being used to
│                                       test the Nexus GUI, elaborated in Section 19
│
└── public/
5.3 Enforced import boundaries

Directly extending Section 4.4's classification rules into a statically-checked structural rule (an ESLint import-boundary plugin, configured per-directory):

primitives/     → may import from: design-tokens/ only
composites/     → may import from: primitives/, design-tokens/
features/*/     → may import from: composites/, primitives/, design-tokens/, api/, state/, realtime/
pages/          → may import from: features/*/, composites/, primitives/, routing/
plugin-ui/      → may import from: design-tokens/ (read-only, per Volume 2 §3.8) — explicitly
                    FORBIDDEN from importing features/ or state/ directly, since a marketplace
                    module's UI extension must interact with the host application only through
                    the constrained extension-point API Section 13 defines, never by reaching
                    into host application internals

A violation of this table is a CI-failing lint error, not a review suggestion — mirroring the enforcement rigor Volume 1 established for its own dependency-direction rule (Part V, Section 36.3; Part VI, Section 40.2).

5.4 Why features/ mirrors Volume 1's bounded contexts exactly

This is a deliberate, non-obvious structural choice worth justifying explicitly: features/flow-authoring/, features/execution/, and so on use the exact same names as Volume 1, Part II, Section 7's bounded contexts, not a frontend-invented alternative grouping (e.g., "authoring," "monitoring," "settings" as a UX-driven taxonomy might suggest). This is a direct application of Volume 1, Part II, Section 6.3's ubiquitous-language discipline extended across the volume boundary — a frontend engineer and a backend engineer discussing "the Execution feature" or "the Execution context" are talking about the same conceptual boundary, reducing the exact cross-team vocabulary drift risk Volume 1 was originally designed to prevent within the backend alone.

5.5 Within a feature folder
features/execution/
├── components/         # Feature-classification components specific to this context
│   ├── RunProgressView.tsx
│   ├── StepResultCard.tsx
│   └── FlowRunTriggerDialog.tsx
├── hooks/               # feature-scoped hooks composing state/server/ queries
│   ├── useFlowRun.ts
│   └── useLiveRunProgress.ts    # composes realtime/ (Section 15) + state/server/
└── index.ts             # the ONLY file other features/pages may import from —
                            everything else in this folder is a private implementation
                            detail, enforced by the same ESLint boundary tooling as 5.3

The index.ts-as-sole-public-surface convention, applied to every feature folder, is what keeps features/* from becoming implicitly, unmanageably interdependent as the codebase grows — directly analogous to Volume 1, Part V, Section 36.3's project-reference discipline, expressed here at the module/folder level since TypeScript/React doesn't have a first-class project-reference mechanism as rigorous as .NET's.

5.6 Shared cross-feature concerns — where they live

A small number of concerns are legitimately needed by multiple features (e.g., the AiAttribution badge rendering from Volume 2, Section 2.6, needed by both execution and reporting-audit). These live in composites/ if purely presentational (Section 4.4's classification), or in a narrowly-scoped features/shared/ folder (an explicit, deliberate exception to 5.4's one-context-per-folder rule, used sparingly and only for genuinely cross-context domain concepts like AI attribution that don't belong to any single bounded context more than another) if they carry feature-level logic.

5.7 Path aliasing

TypeScript path aliases (@primitives/*, @composites/*, @features/*, and so on, mapped 1:1 to the top-level directories in 5.2) are configured project-wide, both for import ergonomics and because the ESLint boundary rules in 5.3 are implemented against these aliases specifically — a relative-path import that circumvents an alias (e.g., ../../../primitives/Button instead of @primitives/Button) is itself flagged as a lint violation, closing a potential loophole around the boundary enforcement.



6. Feature Modules
6.1 Purpose

Section 5.5 established the internal shape of a single feature folder. This section specifies the five feature modules themselves — their scope, their primary components, and, critically, their interaction patterns with each other, since Volume 1's bounded-context Customer/Supplier and Conformist relationships (Part II, Section 7.3) have direct frontend analogues that this section makes explicit.

6.2 features/flow-authoring

Scope: Everything Persona Priya touches to create, edit, and publish flows (Volume 0, Section 10.2; UC-2). Primary components: FlowEditor (the main authoring canvas, composing the Workflow Designer from Section 11), FlowStepEditor (schema-driven parameter editing per UX Principle 1.3), FlowVersionHistory, AiFlowGenerationPanel (the UI for Volume 1's GenerateFlowFromDescriptionCommand, Part II §13.2).

Interaction with module-plugin: Consumes GetAvailableModuleActionsQuery results (via state/server/) to render the module/action picker — this is a read-only, Customer-side relationship mirroring Volume 1's own Customer/Supplier context-map relationship (Part II, Section 7.3) exactly: flow-authoring never mutates module registration data, only reads it.

6.3 features/execution

Scope: Triggering runs, live progress (Persona Priya's authoring-loop feedback and Persona Marcus's CI-adjacent visibility, Volume 0 UC-1/UC-4), and the flow-run detail view. Primary components: RunProgressView (SignalR-driven, Section 15), StepResultCard (rendering Volume 1's StepResult, including the AI-attribution and flake-badge treatments from Volume 2, Section 2.6), FlowRunTriggerDialog, WorkflowRunView (the frontend counterpart to Volume 1, Part III Section 20's Workflow Engine, if and when that capability ships per Volume 0's roadmap prioritization).

Interaction with flow-authoring: Consumes a published Flow's definition (read-only) to know what steps to expect progress for — again a Customer/Supplier relationship, directly mirroring Volume 1's own (Part II, Section 7.3): execution never edits a flow definition, and per UX Principle 1.2, has no client-side ability to do so even if a developer were tempted to add a shortcut.

6.4 features/module-plugin

Scope: Module/marketplace browsing, installation (Volume 0, Section 17.4), and the module registration detail view (serving Persona Raj, Volume 0 Section 10.5). Primary components: ModuleMarketplaceBrowser, ModuleCertificationBadge (rendering Volume 1's TrustTier, Part II §11.9, using the exact tiered visual treatment Volume 0 Section 17.3 specifies), ModuleActionSchemaViewer.

Interaction with flow-authoring: Supplier-side only, as established in 6.2 — this feature never reads or depends on any flow-authoring state.

6.5 features/reporting-audit

Scope: Persona Sofia's primary surface (Volume 0, UC-5) — trend dashboards, drill-down views, audit log export (Persona Elena, UC-7). Primary components: PassRateTrendChart, FlakeRateWidget, FailureClusterDrilldown, AuditLogExporter.

Interaction with execution: Read-only consumer of historical FlowRun/StepResult data via the reporting-specific queries (Volume 1, Part II, Section 14.4) — critically, this feature never subscribes to SignalR live-progress events (Section 15's scope, Volume 1 Part V Section 31.7's explicit boundary that the hub is scoped to actively-watched runs, not general reporting) — Sofia's dashboard is built entirely on the REST/query layer (Section 8), consistent with the eventual-consistency read-model design Volume 1 already specified (Part V, Section 23.4–23.6) and this frontend correctly does not try to work around with a real-time shortcut that would misrepresent the dashboard's actual, documented consistency guarantee.

6.6 features/identity-access

Scope: Login, session management, user/role administration (Volume 1, Part IV, Section 25's baseline roles), and the theme/preferences settings (Volume 2, Section 3.5). Primary components: LoginForm, RoleAssignmentTable, UserProfileSettings, ApiKeyManager (serving Persona Marcus's CI service-account credential needs, Volume 1 Part IV Section 24.4).

Interaction with every other feature: This is the one feature every other feature has an implicit dependency on (via the authentication context established in Section 14), but the relationship is strictly one-directional — identity-access exposes the current user/session/permission state; no other feature ever reaches into identity-access's internal implementation, consuming only its published index.ts surface (Section 5.5) and, more specifically, the shared useAuth()/usePermission() hooks Section 14 will formalize.

6.7 Cross-feature permission checks — mirroring Volume 1's authorization discipline

Directly extending Volume 1, Part IV, Section 25.3's EnsurePermission convention to the frontend: every Feature-level component that renders a state-changing control (a "Publish" button, a "Trigger Run" button, a "Register Module" form) checks the current user's permission (via identity-access's hooks, Section 6.6) before rendering that control as enabled — but, per UX Principle 1.2, this check is understood explicitly as a UX convenience (hiding/disabling controls a user can't use, avoiding a confusing failed-request round-trip) and never as the actual security boundary, which remains entirely server-side (Volume 1, Part IV, Section 25). A frontend permission check that's stale or bypassed results, at worst, in a clean, expected 403 Forbidden API response — never in an actual unauthorized action succeeding.

6.8 Feature module ownership and team structure (forward reference)

Consistent with Volume 1, Part II, Section 7.6's observation about bounded contexts as natural team-ownership units: these five feature modules are designed to be ownable by frontend sub-teams aligned with the same team structure Volume 1 anticipated for the backend bounded contexts — a flow-authoring frontend engineer and a Flow Authoring backend engineer share a bounded context and, per Section 5.4's naming discipline, a vocabulary, even if they sit on nominally separate "frontend" and "backend" teams. This is flagged here as a Volume 5 (Engineering Playbook) consideration, not resolved in this volume, consistent with this document set's practice of tracing decisions to where they actually belong.



7. Routing
7.1 Purpose and technology choice

Client-side routing uses React Router, the de facto standard for React SPAs (Section 4.3's client-rendered-SPA decision) with mature support for the route-level code-splitting and data-loading patterns this section specifies. This section defines the route table, route-level data loading strategy, and the authorization-guard pattern that enforces Volume 1's RBAC model (Part IV, Section 25) at the navigation layer.

7.2 Route table
Route	Page component	Feature module(s)	Required permission (Volume 1, Part IV §25.1)
/login	LoginPage	identity-access	None (public)
/flows	FlowListPage	flow-authoring	FlowCreate OR FlowExecute (view access implied by either)
/flows/new	FlowEditorPage (new-draft mode)	flow-authoring	FlowCreate
/flows/:id/edit	FlowEditorPage	flow-authoring	FlowEdit
/flows/:id/versions	FlowVersionHistoryPage	flow-authoring	ReportView
/runs	FlowRunListPage	execution	ReportView
/runs/:id	FlowRunDetailPage	execution, reporting-audit	ReportView
/reports/trends	TrendDashboardPage	reporting-audit	ReportView
/reports/audit	AuditLogPage	reporting-audit	AuditView
/modules	ModuleMarketplacePage	module-plugin	ReportView (browsing); ModuleRegister for install actions, checked at the component level per Section 6.7
/modules/:id	ModuleDetailPage	module-plugin	ReportView
/admin/users	UserAdminPage	identity-access	UserManage
/admin/roles	RoleAdminPage	identity-access	RoleManage
/settings	UserSettingsPage	identity-access	None (own-profile access)

This table is the direct frontend realization of Volume 1, Part V, Section 30.2's REST endpoint inventory — every route ultimately calls the query/command combination that table specifies; no route introduces a data-access pattern not already accounted for in Volume 1.

7.3 Route guards — layered, mirroring Volume 1's authorization layers
tsx
function RequirePermission({ permission, children }: { permission: Permission; children: React.ReactNode }) {
  const { hasPermission, isLoading } = usePermission(permission);  // features/identity-access, Section 6.6
  if (isLoading) return <RouteLoadingSkeleton />;
  if (!hasPermission) return <Navigate to="/forbidden" replace />;
  return <>{children}</>;
}

// Route definition:
<Route path="/admin/users" element={
  <RequirePermission permission="UserManage">
    <UserAdminPage />
  </RequirePermission>
} />

Consistent with Section 6.7's explicit framing: this guard is a UX-layer convenience (redirecting a user away from a page they'd immediately receive 403 responses on) — the actual authorization enforcement remains Volume 1, Part IV, Section 25's server-side responsibility. A route guard existing does not reduce or replace any server-side check specified there; this is restated here specifically because routing is exactly the kind of layer where a team might be tempted to treat client-side gating as sufficient.

7.4 Route-level data loading

Using React Router's data-loading APIs (loaders), each route pre-fetches its primary data dependency before rendering, integrated with Section 8's server-state layer (TanStack Query) rather than as a separate, competing data-fetching mechanism — the loader initiates the query, and the rendered Page/Feature components then read from the same cached query result via the standard hooks, avoiding a duplicate fetch. This pattern directly supports Section 4.8's Suspense-based loading strategy for route transitions specifically.

7.5 Deep linking and state restoration

Every meaningful UI state that a user might want to bookmark or share — a specific flow-run's detail view, a trend dashboard filtered to a specific module and date range, a specific flow's specific version — is represented in the URL (path parameters or query-string parameters), never held only in ephemeral component state. This is a direct UX Principle 1.2 application ("show real state, never a fiction of it") extended to navigable state specifically: a URL that doesn't fully describe what's on screen is itself a small violation of that principle, since two users navigating to the "same" URL should see the same thing.

7.6 The Workflow Designer and React Flow's canvas state — a deliberate routing exception

Section 11 (Workflow Designer) will specify that the visual canvas's pan/zoom position is not URL-encoded, as a deliberate, narrow exception to Section 7.5's general rule — canvas viewport state is ephemeral, session-local UI convenience, not meaningful application state a user would want to bookmark or share (unlike, say, which specific flow is open, which remains a URL parameter per 7.5). This exception is stated explicitly here to preempt what would otherwise look like an inconsistency in the routing design.

7.7 Not-found and error routes

A catch-all * route renders a NotFoundPage; a top-level error boundary (distinct from Section 4.6's module-scoped boundaries, this one wrapping the entire router) renders a RouteErrorPage for any unhandled rendering exception during route transition — consistent with UX Principle 1.7's "degrade honestly": a broken route never renders a blank white screen, always a clear, actionable error state, mirroring Volume 1, Part V, Section 30.5's RFC 7807 error-format philosophy translated into a full-page UX equivalent, including surfacing the same correlationId for support escalation when the error originated from a failed API call during route loading.

7.8 Navigation and the AI-generated-flow entry point

Volume 0's UC-2 (Priya using AI flow generation) has a specific routing consideration worth calling out: POST /flows/generate (Volume 1, Part V, Section 30.2) does not have its own dedicated route — it's a modal/panel action available from /flows and /flows/new (the AiFlowGenerationPanel from Section 6.2), because generating a flow is a means to arrive at a normal Draft flow, not a distinct application state — once generated, the result is a standard Flow that opens in the exact same FlowEditorPage (/flows/:id/edit) as any human-authored draft, directly reinforcing UX Principle 1.6's transparency requirement in a structural way: there is no separate "AI flow" page implying a fundamentally different, opaque kind of object.



8. State Management
8.1 Purpose and the fundamental split

This section fully specifies the server-state/client-state split introduced conceptually in Section 4.5. This split is the single most important state-management decision in this volume: server state (anything originating from Volume 1's REST API or SignalR) and client state (purely local UI concerns — a dialog's open/closed state, a form's in-progress draft values, canvas pan/zoom per Section 7.6) are managed by two deliberately different tools, never conflated into one general-purpose store.

8.2 Server state — TanStack Query

All data originating from Volume 1's REST API (Part V, Section 30) is managed through TanStack Query (React Query), never manually fetched-and-useState'd. Every query in Volume 1's catalog (Part II, Section 14) has a corresponding typed hook:

typescript
function useFlowById(flowId: FlowId) {
  return useQuery({
    queryKey: ['flows', flowId],
    queryFn: () => apiClient.getFlow(flowId),   // Section 4.7's generated, typed client
    staleTime: 30_000,  // matches this data's read-your-own-writes-adjacent
                          // consistency expectations, see 8.3
  });
}

function usePublishFlow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (flowId: FlowId) => apiClient.publishFlow(flowId),
    onSuccess: (_, flowId) => {
      queryClient.invalidateQueries({ queryKey: ['flows', flowId] });
      queryClient.invalidateQueries({ queryKey: ['flows', 'list'] });  // the list view
                                                                          // must reflect the
                                                                          // new Published status
    },
  });
}
8.3 Cache invalidation strategy — mirroring Volume 1's read-model consistency guarantees

This is the most architecturally significant part of this section, and it is a direct, deliberate mirror of Volume 1, Part V, Section 23.6's explicit per-read-model consistency table — restated here as a frontend cache-configuration table so the two documents' guarantees never silently diverge:

Query category	staleTime	Invalidation trigger	Mirrors (Volume 1)
GetFlowByIdQuery, ListFlowsQuery	Short (30s) + explicit invalidation on mutation	Any flow-authoring mutation for that flow	FlowSummaryReadModel — strongly consistent, Part V §23.6
GetFlowReportQuery, trend queries	Longer (2min) + SignalR-driven invalidation (Section 15) for actively-watched runs	FlowRunCompletedEvent received via SignalR, or explicit refetch	FlowReportReadModel — eventually consistent, Part V §23.6
GetPassRateTrendQuery, GetFlakeRateQuery	Long (10min), no SignalR invalidation	Manual refresh or natural cache expiry	TrendAggregateReadModel — batch-consistent, Part V §23.6 — and per that section's own requirement, the UI displays a "last updated" timestamp sourced from the query response, never implying fresher data than the backend actually guarantees
GetAvailableModuleActionsQuery	Medium (1min)	Manual refresh; rarely changes during a session	ModuleActionReadModel — eventually consistent, Part V §23.6

This table is not an independent frontend design choice — it is a derived table, and any change to Volume 1, Part V, Section 23.6's consistency guarantees requires a corresponding review of this table, flagged here explicitly as a cross-volume maintenance dependency (the same category of obligation Volume 1, Part VI, Section 40.8 established for intra-volume deviations, now extended across the volume boundary).

8.4 Client state — Zustand for cross-component local state, useState for single-component state

Purely local UI state that doesn't need to be shared beyond a single component tree uses plain useState/useReducer. State that needs to be shared across components without prop-drilling (the Workflow Designer's canvas selection state, Section 11; the currently-open dialog stack) uses small, purpose-scoped Zustand stores — never one large, global application-state store, consistent with Architecture Principle 20.7's composable-primitives preference (Volume 1, Section 20.7) applied here: many small, independently-testable stores over one monolithic one.

typescript
interface FlowCanvasStore {
  selectedStepId: FlowStepId | null;
  selectStep: (id: FlowStepId | null) => void;
}

const useFlowCanvasStore = create<FlowCanvasStore>((set) => ({
  selectedStepId: null,
  selectStep: (id) => set({ selectedStepId: id }),
}));
8.5 Why not Redux

Redux (or Redux Toolkit) was evaluated and deliberately not adopted: Nexus's state genuinely splits cleanly into server state (TanStack Query's actual purpose-built domain) and small, scoped client state (Zustand's) — Redux's value proposition (a single normalized store with time-travel debugging, middleware-based side effects) solves problems this architecture doesn't have once the server/client split is respected. Adopting Redux would mean either duplicating server state into Redux (directly violating Section 4.5's "server state is never duplicated" rule) or using Redux only for the client-state slice, at which point it offers no advantage over the lighter-weight Zustand while adding boilerplate — a direct application of the same "don't add complexity that doesn't earn its cost" principle already cited in Section 4.8 and 8.4.

8.6 Optimistic updates — where and how, per UX Principle 1.2

Referencing Volume 2, Section 1.3's principle directly: optimistic updates are used narrowly, only where the operation's success is highly predictable and the UX cost of waiting is meaningful (e.g., toggling a flow's ContinueOnFailure flag in the editor) — and every optimistic update is implemented with TanStack Query's built-in rollback-on-error mechanism, never a manual, ad hoc local-state patch:

typescript
const mutation = useMutation({
  mutationFn: updateFlowFlag,
  onMutate: async (newFlag) => {
    await queryClient.cancelQueries({ queryKey: ['flows', flowId] });
    const previous = queryClient.getQueryData(['flows', flowId]);
    queryClient.setQueryData(['flows', flowId], (old) => ({ ...old, continueOnFailure: newFlag }));
    return { previous };  // used for rollback in onError
  },
  onError: (_err, _newFlag, context) => {
    queryClient.setQueryData(['flows', flowId], context?.previous);  // exact reconciliation,
                                                                        // per UX Principle 1.3 —
                                                                        // never left inconsistent
  },
});

High-risk or slow operations (publish, trigger run, module registration) are explicitly not optimistic — the UI shows a clear pending/loading state and waits for the authoritative server response, consistent with UX Principle 1.7's honest-degradation requirement outweighing raw responsiveness for operations where a rollback would be visually jarring or where the operation's own domain invariants (Volume 1, Part II, Section 9) make the outcome genuinely uncertain until the server responds.

8.7 State management and the Plugin UI trust boundary

Directly extending Section 5.3's import-boundary rule: marketplace module UI extensions (Section 13) have no access to the host application's TanStack Query cache or Zustand stores whatsoever — a sandboxed module's UI contribution receives only the specific, narrow props/callbacks Section 13's extension-point API provides, never a reference to the surrounding application's state management internals. This is the frontend-state-management-specific instance of the same trust-boundary principle Volume 1 established for backend plugin code (Part III, Section 18.3's deliberately narrow ExecutionContext) and Section 5.3 already established for the import graph — restated here because state access is a distinct, easily-overlooked way a boundary could otherwise leak even if imports are correctly restricted.


____________________________________________________________________________________________________________________________________________________



9. Component Library
9.1 Purpose

This section catalogs the Primitive and Composite components (Section 4.4's classification) that implement Section 2's design tokens as actual, reusable React components — the concrete library every Feature module (Section 6) builds on. It also specifies the single most architecturally important component in the entire frontend: the schema-driven parameter editor that makes UX Principle 1.3 (module parity, zero hardcoding) real.

9.2 Primitives catalog
Component	Purpose	Design tokens consumed (Section 2)
Button	Primary/secondary/ghost action triggers	--nexus-amber, motion tokens
Badge	Small status/label chips	Full signal-state palette (Section 2.6)
StatusDot	The pass/fail/skip/running visual vocabulary	Signal-state palette
Input, Select, Checkbox, TextArea	Form primitives	Typography, spacing scale
Tooltip	Contextual help, required for UX Principle 1.7's "explain why disabled" pattern	Typography
Spinner	Loading indication	Motion tokens
Icon	Lucide wrapper with the ModuleKind-to-icon mapping (Section 2.7)	—

Every Primitive is built with zero data-fetching and zero business logic, per Section 4.4's rule — a Badge component knows how to render a signal state given a status prop; it has no knowledge of what a FlowRun is.

9.3 Composites catalog
Component	Purpose	Built from
DataTable	Paginated, sortable tabular display — used by ListFlowsQuery, ListRecentFlowRunsQuery, ListUsersQuery results alike	Badge, StatusDot, pagination primitives
Modal, Drawer	Overlay containers	Button
CodeEditor	Thin Monaco wrapper (full integration detail in Section 10) — deliberately classified as a Composite, not a Feature component, because it has no domain-specific knowledge of its own; it's configured by whatever Feature component uses it	—
TrendChart	Generic time-series chart primitive (built on a charting library, Recharts, chosen for React-idiomatic composition matching this volume's component-classification philosophy) — used by PassRateTrendChart and FlakeRateWidget (Section 6.5) alike, itself knowing nothing about pass rates or flake rates specifically	—
SchemaDrivenParamEditor	The component directly implementing UX Principle 1.3, detailed fully in 9.4	Input, Select, CodeEditor, Tooltip
AiAttributionBadge	The always-visible AI-transparency treatment (UX Principle 1.7)	Badge, Tooltip
9.4 SchemaDrivenParamEditor — the component that makes module parity real

This is the single component every one of Volume 0's six modules' step-parameter editing passes through, with zero module-specific code anywhere in its implementation:

tsx
interface SchemaDrivenParamEditorProps {
  schema: ActionSchema;   // Volume 1, Part II §11.8 — generated type, Section 4.7
  values: Record<string, unknown>;
  onChange: (values: Record<string, unknown>) => void;
}

function SchemaDrivenParamEditor({ schema, values, onChange }: SchemaDrivenParamEditorProps) {
  return (
    <div className="param-editor">
      {schema.parameters.map((param) => (
        <ParamField
          key={param.name}
          spec={param}                        // ParamSpec, Volume 1 Part II §11.8
          value={values[param.name]}
          onChange={(v) => onChange({ ...values, [param.name]: v })}
        />
      ))}
    </div>
  );
}

function ParamField({ spec, value, onChange }: { spec: ParamSpec; value: unknown; onChange: (v: unknown) => void }) {
  // Renders the correct input control purely from spec.type (Volume 1 Part II §11.8's
  // ParamType: String | Number | Boolean | Object | Array) — this switch is the ONLY
  // place in the entire frontend that branches on parameter shape, and critically,
  // it branches on ParamType (a small, closed, backend-defined enum), never on
  // ModuleKind or action name — satisfying UX Principle 1.3 precisely, not just in spirit.
  switch (spec.type) {
    case 'String': return <Input value={value as string} onChange={onChange} required={spec.required} />;
    case 'Number': return <Input type="number" value={value as number} onChange={onChange} required={spec.required} />;
    case 'Boolean': return <Checkbox checked={value as boolean} onChange={onChange} />;
    case 'Object': case 'Array': return <CodeEditor language="json" value={JSON.stringify(value)} onChange={(v) => onChange(JSON.parse(v))} />;
  }
}
9.5 Variable-reference autocomplete within ParamField

Referencing Volume 1, Part II, Section 11.7's VariableExpression and Section 19.6's condition-evaluation design: a String-typed ParamField additionally offers {{vars.x.y}} autocomplete, sourced from the current flow's prior steps' declared saveAs bindings (available client-side from the currently-loaded Flow draft, Section 8's server state) — this is a genuine UX enhancement built on top of the same closed, generic ParamType switch in 9.4, not a special case requiring module-specific knowledge: any String parameter, on any module's action, gets this autocomplete uniformly.

9.6 Component documentation via Storybook

Every Primitive and Composite is documented in Storybook, with stories covering every signal state (Section 2.6) and both themes (Section 3) — this is a build-time-verified requirement (a CI check that every exported component has a corresponding story file), directly serving Volume 1, Part VI Section 40.7's documentation-as-code discipline extended to the frontend: an undocumented component is treated as an incomplete one, not a documentation gap to close later.

9.7 Visual regression testing as a design-system guardrail

Every Storybook story (9.6) is captured as a visual regression test baseline (Section 19, Testing Strategy, will specify the exact tooling) — this is the concrete enforcement mechanism protecting Section 2's design-token governance rule (2.9: no undocumented visual drift) at the component-implementation level specifically, catching the case where a component's behavior is correct but its rendered appearance has silently drifted from the design system's token definitions.

9.8 Accessibility baked into primitives, not bolted onto features

Directly anticipating Section 18 (Accessibility): every Primitive component (9.2) implements its accessibility contract (ARIA roles, keyboard interaction, focus management) once, centrally — a Modal handles focus trapping and Escape-to-close for every feature that uses it, a Select implements full keyboard navigation for every dropdown across the product. This architectural choice means Feature-level components (Section 6) inherit correct accessibility behavior automatically rather than needing to reimplement it per-surface, which is both a development-efficiency win and, more importantly, the only realistic way to achieve NFR-UX-1's WCAG 2.1 AA conformance consistently across five distinct feature modules rather than auditing each one independently for the same underlying concerns.



10. Monaco Integration
10.1 Purpose and where Monaco fits

The Monaco Editor (VS Code's editor component, chosen per Volume 0, Section 21.3's rationale for React's ecosystem depth) serves three distinct roles in Nexus's GUI, each specified separately in this section because they have genuinely different requirements: raw flow-definition editing (10.3), embedded JSON/object-parameter editing (10.4, already referenced in Section 9.4's ParamField), and read-only code/log display (10.5).

10.2 The CodeEditor composite wrapper

All three roles route through the single CodeEditor composite (Section 9.3) rather than each Feature module independently instantiating Monaco — this centralization is what makes Section 10.6's custom language services (autocomplete, validation) available consistently everywhere Monaco appears, and is a direct application of the same "define once, reuse everywhere" discipline Section 2 established for design tokens.

tsx
interface CodeEditorProps {
  language: 'json' | 'yaml' | 'nexus-flow-expression';
  value: string;
  onChange?: (value: string) => void;   // omitted => read-only mode
  height?: string;
  validationSchema?: JSONSchema;         // Section 10.4
}
10.3 Raw flow-definition editing

Directly serving a specific, deliberate authoring-experience decision: alongside the visual Workflow Designer (Section 11), Nexus offers a raw JSON/YAML editing mode for the same underlying Flow definition (Volume 1, Part II, Section 8.2) — a power-user path consistent with UX Principle 1.5 (progressive disclosure: the visual designer is the default, the raw editor is available but not front-loaded). Both editing modes operate on the exact same underlying data structure and the exact same publish-time validation (Volume 1, Part II, Section 9.2's Flow.Publish), switchable without data loss — a flow author can start visually, drop into raw JSON for a complex conditional expression Section 11's canvas doesn't have a dedicated widget for, and switch back, with both views staying synchronized against the same in-memory draft state (Section 8's client-state layer holding the current unsaved draft).

10.4 JSON Schema-driven validation for embedded object/array parameters

Referencing Section 9.4's ParamField handling of Object/Array-typed parameters: the embedded CodeEditor instance is configured with a JSON Schema derived directly from the relevant ActionSchema's parameter definition (Volume 1, Part II, Section 11.8), giving real-time inline validation and autocomplete for structured parameter values — e.g., an API module's assert array parameter (referencing the Assertion discriminated union from Volume 1, Part II, Section 11.6) gets full autocomplete for status/path/equals/contains/exists fields as the user types, generated from the same backend-defined schema, not a hand-maintained frontend copy of it.

typescript
function deriveJsonSchema(paramSpec: ParamSpec): JSONSchema {
  // Maps Volume 1's ParamSpec/ActionSchema shape to a standard JSON Schema document —
  // a pure, deterministic transformation with no module-specific logic, satisfying
  // UX Principle 1.3 at the validation layer specifically, not just the rendering layer
  // Section 9.4 already covers.
}
10.5 Read-only code/log display

Log excerpts (a StepResult's Data field when it contains log content, per Volume 1's Logs module), page HTML snippets shown during self-heal review (Volume 1, Part III, Section 18's RequestSelectorHealCommand flow, surfaced in the AI-attribution detail view per UX Principle 1.6), and raw API response bodies (the API module's StepResult.Data) are all displayed via CodeEditor in read-only mode with appropriate syntax highlighting — chosen over a plain <pre> block specifically for Monaco's built-in features (line numbers, search-within, minimap for long content) that meaningfully aid Persona Priya's debugging workflow when investigating a failed run.

10.6 Custom language service — nexus-flow-expression

The {{vars.x.y}} variable-expression syntax (Volume 1, Part II, Section 11.7) gets a genuine, custom Monaco language service — not merely syntax highlighting via a regex grammar, but a real completion provider and hover provider:

typescript
monaco.languages.registerCompletionItemProvider('nexus-flow-expression', {
  triggerCharacters: ['.'],
  provideCompletionItems(model, position) {
    // Resolves available variable paths from the currently-loaded Flow's prior
    // steps' saveAs bindings (Section 9.5's same data source), offering
    // structured completion (vars.orderInfo.id, vars.orderInfo.status, etc.)
    // derived from the ACTUAL StepResult.Data shape of a representative prior
    // run if one exists (an enhancement beyond static schema knowledge alone —
    // if step 1 is an API module call whose last real execution returned a
    // JSON body with a `repos_url` field, that field is offered as a concrete
    // completion suggestion, not just a generic "any property" placeholder).
  },
});

This "completion informed by real historical execution data" capability is a deliberate, specified UX enhancement directly serving NFR-UX-2's 30-minute time-to-first-flow target — it's substantially easier for Priya to chain step 2's parameters off step 1's actual observed output shape than to guess at an unfamiliar target API's response structure from documentation alone.

10.7 Performance considerations

Monaco is a genuinely heavy dependency (a large JS bundle); per Section 17 (Performance)'s forthcoming code-splitting strategy, the CodeEditor composite is lazy-loaded — the visual Workflow Designer (Section 11), which most authoring sessions use as the primary interface per UX Principle 1.5, does not pay Monaco's bundle-size cost unless a user actually opens the raw-editing mode (10.3) or a parameter field requiring structured JSON input (10.4).

10.8 Accessibility considerations specific to Monaco

Monaco has known, well-documented accessibility limitations relative to native form controls (screen-reader interaction with a canvas-rendered code editor is inherently more complex than with a <textarea>) — Section 18 (Accessibility) will specify Monaco's built-in accessibility mode (accessibilitySupport: 'on') as a mandatory configuration, and, critically, requires that no capability be exclusively available through Monaco with no alternative path — every Object/Array parameter editable via the embedded JSON editor (10.4) must also be constructible through a structured, fully-accessible fallback form for users who cannot effectively use the code-editor surface, directly satisfying NFR-UX-1's WCAG 2.1 AA requirement rather than treating Monaco's limitations as an accepted accessibility gap.



11. Workflow Designer
11.1 Purpose and scope — product/UX specification

This section specifies the Workflow Designer as a product surface: what a user sees, does, and experiences when visually authoring a flow. Section 12 (React Flow Integration) covers the technical implementation built on the React Flow library — this section deliberately stays at the interaction-design level, since the same visual designer experience specified here could, in principle, be rebuilt on a different underlying canvas library without changing anything in this section, while Section 12 would need a full rewrite. Keeping these separate mirrors this volume's general practice of separating "what and why" from "how," the same distinction Volume 1 maintained between its domain model (Part II) and its engineering patterns (Part III).

11.2 What "Workflow Designer" names, precisely

A naming clarification worth stating directly: this section's "Workflow Designer" is the visual canvas for authoring a single Flow (Volume 1, Part II, Section 8.2's ordered step sequence) — it is not, despite the name's echo, primarily about Volume 1, Part III, Section 20's Workflow concept (multi-flow orchestration). The canvas does support authoring a WorkflowDefinition (Volume 1, Part III, Section 20.3) when that capability ships per Volume 0's roadmap, using the same underlying visual grammar (11.3) applied to flow-level nodes instead of step-level nodes — but the primary, GA-critical use case this section specifies is single-flow authoring. This naming tension is called out explicitly to avoid the exact kind of ambiguous-terminology drift Volume 1, Part II, Section 6.3's ubiquitous-language discipline exists to prevent.

11.3 Visual grammar

Each FlowStep (Volume 1, Part II, Section 10.2) renders as a node on the canvas; steps connect via directional edges reflecting execution order (Volume 1, Part II, Section 10.2's explicit Order field — the canvas is the direct visual editor for that ordering, not an independent representation that needs separate reconciliation). A node's visual treatment is driven entirely by the same ModuleKind-to-icon mapping and signal-state palette already established in Section 2.6–2.7 — a Web-module step and an AI-module step are visually distinguished only by their module icon and color accent, never by fundamentally different node shapes or layouts, directly reinforcing UX Principle 1.3's module-parity requirement at the canvas level specifically.

┌─────────────────────┐        ┌─────────────────────┐        ┌─────────────────────┐
│  🌐  Navigate to login  │───────►│  🌐  Fill credentials   │───────►│  🔌  Verify order API   │
│      web.navigate       │        │      web.fill            │        │      api.request          │
└─────────────────────┘        └─────────────────────┘        └─────────────────────┘
11.4 Conditional branches (when) — visual representation

A step with a WhenCondition (Volume 1, Part II, Section 10.2) renders its incoming edge with a distinct dashed treatment and a small condition-summary label, directly visualizing Volume 1, Part III, Section 19.6's skip-vs-execute semantics before a flow is ever run — a flow author can see, at a glance, which steps are unconditional and which are gated, without opening each step's detail panel.

11.5 Variable-flow visualization

When a node is selected, the canvas highlights every other node that either produces a variable this step consumes (via {{vars.x.y}}, Section 10.6's autocomplete data source) or consumes a variable this step produces (via saveAs) — a direct visual answer to "where does this data come from, and where does it go," which is exactly the kind of cross-step relationship that's hard to hold in your head reading a flat JSON list (10.3's raw-editing alternative) but natural to show spatially on a canvas. This is the single strongest UX argument for the visual designer's existence as the default authoring mode (UX Principle 1.5) rather than merely a cosmetic alternative to raw editing.

11.6 Adding a step — the module/action picker

Clicking "add step" opens a picker rendering every available module and action, sourced live from GetAvailableModuleActionsQuery (Volume 1, Part II, Section 14.2; Section 6.2's flow-authoring/module-plugin Customer/Supplier relationship) — this picker is the most direct, visible proof-point of UX Principle 1.3 and Volume 0's UC-6: a newly-registered marketplace module (Persona Raj's scenario) appears in this exact picker with zero frontend changes, ordered and grouped by TrustTier (Volume 1, Part II, Section 11.9) with the same certification badge treatment established in Volume 2, Section 6.4.

11.7 Error and validation surfacing on the canvas

Referencing Volume 1, Part II, Section 9.2's publish-time validation (unresolved module/action references) and Section 10.6's AiAttribution and Volume 1, Section 19.6's skip semantics: validation errors surface directly on the affected node (a red outline plus an inline error summary), not only in a separate error panel — consistent with UX Principle 1.2's "show real state" and Architecture Principle 20.4's fail-informatively philosophy (Volume 1, Section 20.4) carried through to this specific surface. A flow with unresolved references cannot be published (server-enforced, per Volume 1) and the canvas makes the specific reason visible at the specific node responsible, rather than a generic "publish failed" toast.

11.8 Live-run overlay — the canvas doubles as a progress view

During an active FlowRun (Volume 0's UC-1), the same canvas layout (not a separate view) overlays live status per Section 2.6's signal-state treatment on each node, driven by SignalR (Section 15's full specification) — this is a deliberate reuse of the authoring canvas as the live-monitoring surface, rather than building two separate visualizations of the same flow structure, directly serving Architecture Principle 20.7's composable-primitives-over-duplication preference (Volume 1, Section 20.7) applied to the frontend.

11.9 Undo/redo and draft autosave

The canvas maintains a local undo/redo stack (client state, Section 8.4) independent of the server's Flow versioning (Volume 1, Part II, Section 9.2's Draft/Published versioning is a distinct, coarser-grained concept — a new published version, not every individual canvas edit). Draft edits autosave to the server periodically (a PATCH-style incremental update, not shown explicitly in Volume 1's command catalog as a separate command but implementable as repeated AddFlowStepCommand/step-update calls against the same Draft Flow) so that Priya's in-progress work survives a browser crash or accidental tab close — directly serving NFR-UX-2's usability bar by removing "did I lose my work" as a source of authoring friction.

11.10 Relationship to Section 12

Every capability specified in this section — node rendering, edge semantics, the live-run overlay, selection-based variable-flow highlighting — is realized technically using React Flow's specific APIs (custom node/edge types, its selection and viewport state model) in Section 12, which the next section specifies in full implementation detail.



12. React Flow Integration
12.1 Purpose and scope — technical implementation

Where Section 11 specified the Workflow Designer's product behavior, this section specifies its technical realization on React Flow, the library Volume 0, Section 21.3 anticipated by name. This section is the direct implementation counterpart to Section 11's product spec — every subsection here exists to implement one of Section 11's numbered requirements, cited explicitly throughout.

12.2 Custom node types
tsx
const nodeTypes = {
  flowStep: FlowStepNode,
  // Additional node types registered only if/when Volume 1's Workflow Engine
  // (Part III §20) ships, per Section 11.2's naming clarification — a
  // `workflowFlowRef` node type representing a whole Flow as a single node
  // within a multi-flow Workflow canvas.
};

function FlowStepNode({ data, selected }: NodeProps<FlowStepNodeData>) {
  const module = useModuleRegistration(data.moduleKind);  // Section 8's server state
  return (
    <div className={cn('flow-step-node', selected && 'selected', data.validationError && 'error')}>
      <Icon name={module.icon} />                          {/* Section 2.7 */}
      <span className="step-name">{data.name}</span>
      <span className="step-action">{data.moduleKind}.{data.action}</span>
      {data.liveStatus && <StatusDot status={data.liveStatus} />}   {/* Section 11.8 */}
      {data.validationError && <ErrorIndicator message={data.validationError} />}  {/* Section 11.7 */}
    </div>
  );
}

FlowStepNodeData carries exactly the fields Section 11.3, 11.7, and 11.8 require — module/action identity, current validation state, and (during an active run) live status — sourced from a single derived data structure combining the Draft Flow's definition (Section 8's server state) with any active FlowRun's live progress (Section 15), computed once per render rather than each node independently querying multiple data sources.

12.3 Custom edge types — conditional vs. unconditional
tsx
const edgeTypes = { flowEdge: FlowEdge };

function FlowEdge({ data, ...edgeProps }: EdgeProps<FlowEdgeData>) {
  return (
    <BaseEdge
      {...edgeProps}
      style={data.isConditional ? { strokeDasharray: '5,5' } : undefined}  // Section 11.4
    />
  );
}
12.4 Layout algorithm

Node positions are computed automatically on initial load (a directed-graph auto-layout, using a library such as Dagre, laying out steps left-to-right in execution order per FlowStep.Order) and then become user-adjustable — a flow author can drag nodes to a preferred arrangement, with the adjusted layout persisted as presentation metadata separate from the Flow aggregate's actual step-order data (Volume 1, Part II, Section 9.2's Order field remains the single source of truth for execution sequence; canvas position is purely a display concern). This separation is deliberate and directly mirrors Volume 1, Part V, Section 32.2's schema design principle of keeping presentation-adjacent data (like declared_vars) distinct from execution-critical fields — canvas layout here is genuinely presentation-only and never influences execution order.

12.5 Selection state and variable-flow highlighting
tsx
function useVariableFlowHighlight(selectedNodeId: string | null, flow: FlowDetailDto) {
  return useMemo(() => {
    if (!selectedNodeId) return { highlightedNodes: [], highlightedEdges: [] };
    const selectedStep = flow.steps.find((s) => s.id === selectedNodeId);
    // Cross-references selectedStep.params against every other step's saveAs
    // (Volume 1 Part II §10.2) using the same VariableExpression.ReferencedPaths
    // data Section 10.6's autocomplete already computes — reused here, not
    // recomputed independently, per this volume's "define once" discipline.
    return computeVariableFlowGraph(selectedStep, flow.steps);
  }, [selectedNodeId, flow]);
}

This hook directly implements Section 11.5's product requirement, built on React Flow's useReactFlow() selection APIs combined with the derived variable-reference data already established for Monaco's autocomplete (Section 10.6) — a second, deliberate instance of reusing one underlying data computation across two different surfaces (code-editor autocomplete and canvas highlighting) rather than maintaining two parallel implementations of "what variables does this step reference."

12.6 Viewport state — the Section 7.6 routing exception, implemented
tsx
function WorkflowCanvas({ flowId }: { flowId: FlowId }) {
  const viewport = useFlowCanvasStore((s) => s.viewport);  // Zustand, Section 8.4 —
                                                               // NOT URL state, per Section 7.6
  const setViewport = useFlowCanvasStore((s) => s.setViewport);
  return <ReactFlow defaultViewport={viewport} onMoveEnd={(_, vp) => setViewport(vp)} ... />;
}
12.7 Live-run overlay data flow
tsx
function useLiveNodeStatuses(runId: FlowRunId | null): Record<string, StepStatus> {
  const { data: liveEvents } = useLiveRunProgress(runId);  // Section 15's SignalR hook,
                                                               // composed here rather than
                                                               // this component managing its
                                                               // own SignalR subscription directly
  return useMemo(() => Object.fromEntries(
    liveEvents.map((e) => [e.sourceStepId, e.status])
  ), [liveEvents]);
}

This is the concrete implementation of Section 11.8's "canvas doubles as progress view" — critically, WorkflowCanvas (12.6) does not know or care whether runId is null (pure authoring mode) or populated (live-monitoring mode); the same component renders both, with FlowStepNode (12.2) simply receiving liveStatus: undefined in the authoring case, directly realizing Section 11.8's "not a separate view" requirement as an actual, provable code-level fact rather than just a design intention.

12.8 Performance — large flow rendering

React Flow's built-in virtualization (only rendering nodes within or near the current viewport) is enabled by default, but Nexus's specific concern — per Product Philosophy 4.2's "flows should be short, vertical slices" (Volume 0) — is that a well-designed flow should rarely have enough steps for this to matter; a flow with node counts in the hundreds is itself a signal the flow author should consider splitting it (Volume 1, Part III, Section 20's Workflow-level composition) rather than a performance case this canvas needs to heavily optimize for. This is stated explicitly because it's a deliberate scope decision — the canvas is engineered to be smooth for realistic flow sizes (tens of steps), not exhaustively load-tested against pathological thousand-node flows that would themselves represent a violation of the product's own stated design philosophy.

12.9 Testing the canvas

Given React Flow's canvas-based (SVG/DOM-hybrid) rendering, component tests for FlowStepNode and FlowEdge render them in isolation (outside a full ReactFlow provider tree) using React Testing Library, asserting on the data-driven rendering logic in 12.2–12.3 directly; full canvas interaction (drag, connect, select) is covered by the end-to-end test suite (Section 19) rather than attempting brittle unit-level simulation of React Flow's internal drag/gesture handling — a pragmatic testing-layer split consistent with Volume 1, Part VI, Section 40.6's own tiered testing philosophy (fast, isolated unit tests plus a smaller set of slower, higher-fidelity integration/e2e tests) applied here to the frontend's most visually complex component.



13. Plugin UI
13.1 Purpose — the frontend counterpart to Volume 1's Plugin Architecture

Volume 1, Part III, Section 18 specified the backend AutomationModule contract and its trust-tiered execution model. This section specifies the equivalent frontend concern: how a marketplace-installed module can supply custom UI for its own action parameters — beyond what Section 9.4's generic, schema-driven SchemaDrivenParamEditor provides — without compromising the host application's security, design consistency, or stability. This is the most security-sensitive section in this volume, directly analogous in importance to Volume 1, Part III, Section 18's standing as that volume's most consequential section.

13.2 Why custom UI extension is needed at all, given Section 9.4's generic editor

Section 9.4's schema-driven editor handles every ParamType generically and correctly, satisfying UX Principle 1.3's baseline requirement. But some modules benefit from a richer, purpose-built parameter-entry experience beyond generic form fields — a module for a specific SaaS platform's API might want to render a live-searchable resource picker instead of asking a user to paste a raw ID string. This is an enhancement a module may optionally supply, never a requirement — every module works correctly with zero custom UI, using Section 9.4's generic editor as the guaranteed fallback, directly preserving UX Principle 1.3's "zero hardcoding, works for any module" guarantee even for modules that supply no custom UI at all.

13.3 The extension-point contract
typescript
// Published as part of the same Nexus.Contracts-equivalent frontend SDK package
// (mirroring Volume 1, Part V §36.2's decision to publish the backend
// AutomationModule interface as an independently-versioned package)
interface NexusParamEditorExtension {
  paramName: string;               // which specific ParamSpec this extension enhances
  render: (props: ParamEditorExtensionProps) => React.ReactNode;
}

interface ParamEditorExtensionProps {
  value: unknown;
  onChange: (value: unknown) => void;
  theme: Readonly<ThemeTokens>;     // Section 3.8 — read-only token access, nothing more
  // Deliberately absent: any reference to the host application's TanStack Query
  // client, Zustand stores, router, or any other feature module — per Section
  // 8.7's trust-boundary rule, restated and enforced concretely here.
}
13.4 Sandboxed rendering via iframe, not in-process component execution

This is the single most important, and most different-from-the-backend, technical decision in this section: unlike Volume 1, Part III, Section 18.5's in-process execution option for Verified/Built-in-tier backend modules, frontend plugin UI extensions always render inside a sandboxed <iframe>, regardless of the module's TrustTier (Volume 1, Part II, Section 11.9) — there is no "trusted, in-process" tier for frontend code. This asymmetry with the backend's trust-tiered model is deliberate and justified specifically: arbitrary third-party JavaScript executing directly within the host application's own React tree could read authentication tokens, manipulate the DOM outside its intended boundary, or exfiltrate data through means an in-process backend module's narrower ExecutionContext boundary (Volume 1, Part III, Section 18.3) doesn't have to guard against in the same way — a browser's cross-origin iframe sandbox is a fundamentally stronger, browser-enforced isolation primitive than anything achievable with in-process JavaScript execution, and this section adopts the strongest available option universally rather than trying to replicate the backend's tiered trust model in an environment where the strongest isolation is cheap enough to always use.

tsx
function PluginParamEditor({ extension, value, onChange, theme }: PluginParamEditorProps) {
  return (
    <SandboxedIframe
      src={extension.bundleUrl}                    // served from an isolated,
                                                       // separate origin, never the
                                                       // host application's own origin
      sandbox="allow-scripts"                       // explicitly NOT allow-same-origin
      onMessage={(msg) => handlePluginMessage(msg, onChange)}
      postMessage={{ type: 'init', value, theme }}   // theme tokens passed in,
                                                        // read-only, per Section 3.8
    />
  );
}
13.5 Communication protocol — postMessage, strictly typed and validated

All communication between the host application and a sandboxed plugin extension happens via window.postMessage, using a small, closed message-type union (init, valueChanged, resize, error) — every incoming message from the iframe is validated against this schema before being acted upon, and any malformed or unrecognized message is dropped and logged (Section 15's telemetry, extended to cover plugin-UI anomalies) rather than trusted. This mirrors Volume 1, Part III, Section 18.5's gRPC-based sandboxed-module communication design philosophy exactly — a narrow, explicit, validated protocol rather than a broad, implicit trust relationship — translated to the browser's available isolation primitives.

13.6 What a plugin UI extension cannot do

Stated exhaustively, since a security boundary is only as good as its explicitly-enumerated limits: a plugin UI extension cannot navigate the host page, cannot access cookies or localStorage of the host origin (enforced by the cross-origin iframe boundary itself, not merely a convention), cannot make network requests to the Nexus API directly (any data it needs beyond ParamEditorExtensionProps must be explicitly passed in via the init message, per 13.4's deliberately narrow props), and cannot render outside its allocated iframe bounds (no ability to create modals, tooltips, or overlays that escape its container — a real UX constraint module developers must design within, stated here as an accepted trade-off in exchange for the security guarantee).

13.7 Design consistency within the sandbox

Per Section 3.8's forward reference: the theme prop (13.3) gives a plugin extension read-only access to the current theme's design tokens (Section 2–3), so a well-behaved extension can render visually consistent with the host application despite running in an isolated iframe — this is offered as a capability, not enforced, since the sandbox boundary means Nexus cannot force a third party's rendered output to comply with the design system (Section 2.9's governance model applies to first-party code; it cannot reach across the trust boundary this section establishes). The Verified/Community certification review (Volume 0, Section 17.3) is the actual mechanism for catching a poorly-designed or inconsistent plugin UI extension before it's listed, not a runtime technical enforcement.

13.8 Graceful fallback on extension failure

Directly extending Section 4.6's module-scoped error boundary discipline: if a plugin UI extension's iframe fails to load, times out, or posts a malformed error message (13.5), the SchemaDrivenParamEditor's generic rendering (Section 9.4) is used as an automatic fallback — a broken or unavailable custom UI extension never blocks a flow author from configuring that parameter entirely, directly preserving UX Principle 1.7's honest-degradation requirement even in this most security-constrained, third-party-dependent corner of the product.

13.9 Certification tier and UI extension review

Referencing Volume 0, Section 17.3's three-tier certification model: a module's UI extension bundle is reviewed as part of the same Verified/Enterprise-certified review process as its backend code — the sandboxing in 13.4 is defense-in-depth, not a substitute for the marketplace review process, mirroring exactly the relationship Volume 1, Part III, Section 18.5 established between sandboxed execution and certification tier for backend module code (both matter; neither is sufficient alone).


____________________________________________________________________________________________________________________________________________________



14. Authentication
14.1 Purpose — the frontend's role given Volume 1's authentication design

Volume 1, Part IV, Section 24 specified JWT-based sessions, API keys, and SSO/OIDC as Nexus's supported authentication mechanisms, with a deliberate design choice (Section 24.6 there) that permissions are never embedded in the JWT, only a RoleVersion claim. This section specifies how the GUI implements the client side of that design — token handling, session lifecycle, and the useAuth()/usePermission() hooks that every Feature module (Volume 2, Section 6.6–6.7) depends on.

14.2 Token storage — memory-first, never localStorage

Access tokens (Volume 1, Part IV, Section 24.5) are held in memory (a module-scoped variable within features/identity-access, never in localStorage or sessionStorage) — this is a deliberate, non-negotiable security decision: localStorage is readable by any script executing in the page's origin, meaning any successful XSS vulnerability anywhere in the application (including, hypothetically, a compromised third-party dependency) would trivially exfiltrate a localStorage-held token, whereas an in-memory-only token is meaningfully harder to exfiltrate this way. Refresh tokens (Volume 1, Part IV, Section 24.5's rotation-on-use design) are held in an HttpOnly, Secure, SameSite=Strict cookie, set directly by the API Layer's login/refresh response — never readable by JavaScript at all, closing the exfiltration vector entirely for the longer-lived credential.

14.3 Session bootstrap and silent refresh
typescript
function useAuthBootstrap() {
  const [state, setState] = useState<'loading' | 'authenticated' | 'unauthenticated'>('loading');

  useEffect(() => {
    // On app load, no access token exists in memory (14.2) — attempt a silent
    // refresh using the HttpOnly refresh-token cookie, which the browser
    // sends automatically. A successful refresh yields a new in-memory access
    // token; a failure (expired/absent refresh token) means the user must log
    // in interactively.
    refreshAccessToken()
      .then((token) => { setAccessToken(token); setState('authenticated'); })
      .catch(() => setState('unauthenticated'));
  }, []);

  return state;
}

Access tokens are proactively refreshed shortly before their known 15-minute expiry (Volume 1, Part IV, Section 24.5), not only reactively on a 401 response — this keeps Persona Priya's authoring session (Volume 0, UC-2) from ever hitting an expired-token interruption mid-flow-edit, directly serving UX Principle 1.2's "never a fiction of the real state" by keeping the client's authenticated state genuinely, continuously valid rather than allowing it to silently lapse.

14.4 The useAuth() and usePermission() hooks
typescript
function useAuth(): { user: UserSummaryDto | null; isAuthenticated: boolean } {
  // Reads from the identity-access feature's internal state (Section 6.6) —
  // the ONLY public surface other features are permitted to import,
  // per Section 5.5's index.ts convention.
}

function usePermission(permission: Permission): { hasPermission: boolean; isLoading: boolean } {
  const { user } = useAuth();
  const { data: permissions, isLoading } = usePermissionsQuery(user?.id);  // Section 8's
                                                                              // server-state layer,
                                                                              // backed by Volume 1
                                                                              // Part II §14.6's
                                                                              // GetUserPermissionsQuery
  return { hasPermission: permissions?.includes(permission) ?? false, isLoading };
}

Every Feature module's permission-gated rendering (Volume 2, Section 6.7) and every route guard (Section 7.3) consumes exactly these two hooks — never a component's own ad hoc token-parsing or role-checking logic, keeping permission-checking behavior centralized and consistent, mirroring the same centralization discipline Section 9's Component Library established for design tokens.

14.5 RoleVersion cache invalidation on the frontend

Directly extending Volume 1, Part IV, Section 24.6/25.5's RoleVersion mechanism to the frontend cache layer: when a 401/403 response includes a RoleVersion mismatch indicator (a header the API Layer adds specifically for this purpose, an extension of Volume 1's error-response design not detailed in Volume 1 since it's a frontend-consumption concern, flagged here as the concrete cross-volume dependency this creates, consistent with this document set's practice of surfacing such gaps explicitly rather than leaving them implicit), the frontend immediately invalidates its cached usePermission() results (Section 8.2's TanStack Query invalidation) and refetches — ensuring a role change made by an administrator (Volume 1, Part II, Section 13.5's AssignRoleCommand) is reflected in an already-open browser tab within one request round-trip, not only on the affected user's next full page load.

14.6 API key management UI

Serving Persona Marcus's CI service-account needs (Volume 1, Part IV, Section 24.4): the ApiKeyManager component (Volume 2, Section 6.6) displays existing keys by their public prefix only (never the secret value, which per Volume 1's design is never retrievable after initial creation) with creation, revocation, and last-used-timestamp visibility — directly surfacing Volume 1's CredentialRotatedEvent-driven audit trail (Volume 1, Part II, Section 12.5) in a human-readable form, giving Marcus visibility into exactly when and whether his CI credential was recently used, a concrete trust-and-debugging aid for a persona who otherwise barely touches the GUI (Volume 0, Section 10.3).

14.7 SSO/OIDC flow

For enterprise-tier tenants (Volume 1, Part IV, Section 24.7), the login page conditionally renders an SSO-initiated login button (tenant-configuration-driven, resolved before rendering the standard username/password form) that redirects to the customer's IdP and returns via a standard OIDC authorization-code-with-PKCE flow — the PKCE requirement specifically because this is a public (browser-based) client per OAuth2 best practice, with the authorization code exchanged server-side (the API Layer) rather than the frontend ever handling a client secret, keeping no long-lived secret material in browser-reachable code at any point in the flow.

14.8 Logout and session termination

Logout clears the in-memory access token (14.2) and calls a dedicated API endpoint that invalidates the refresh-token cookie server-side (not merely clearing it client-side, since a stolen refresh-token cookie should be immediately unusable, not merely inconvenient to resend) — directly closing the loop with Volume 1, Part IV, Section 24.5's refresh-token-rotation design, ensuring "logout" is a real, server-enforced session termination, not a purely cosmetic client-state reset.

14.9 Cross-tab session consistency

A BroadcastChannel (or storage event fallback for older browsers) synchronizes authentication state across multiple open tabs of the same origin — a logout in one tab immediately reflects as logged-out in every other open tab, and a token refresh in one tab is shared rather than each tab independently and redundantly refreshing — directly serving UX Principle 1.2 (consistent real state) across the multi-tab usage pattern that's genuinely common for Priya's workflow (one tab authoring a flow, another watching its live run, per Volume 2 Section 11.8's canvas-as-progress-view design).



15. SignalR Integration
15.1 Purpose — the frontend client for Volume 1's RunProgressHub

Volume 1, Part V, Section 31 specified the RunProgressHub — group-per-run scoping, the REST-catch-up-plus-SignalR-stream reconnection pattern, and the explicit boundary that this hub serves only actively-watched-run progress, nothing else. This section specifies the frontend connection management, the hooks that Section 6.3 (execution feature) and Section 12.7 (the canvas live-overlay) both consume, and how this integrates with Section 8's server-state layer.

15.2 Connection lifecycle — a single, shared connection
typescript
const signalRConnection = new HubConnectionBuilder()
  .withUrl('/hubs/run-progress', { accessTokenFactory: () => getAccessToken() })  // Section 14.2
  .withAutomaticReconnect({ nextRetryDelayInMilliseconds: exponentialBackoff })
  .build();

A single SignalR connection is established once per authenticated session (not once per component watching a run) and shared across every subscriber via a lightweight connection-manager module in realtime/ (Section 5.2) — multiple components watching different runs simultaneously (e.g., a list view showing several in-progress runs at once) multiplex over this one connection, each joining its own server-side group (Volume 1, Part V, Section 31.3) rather than each opening a redundant separate connection.

15.3 The useLiveRunProgress hook
typescript
function useLiveRunProgress(runId: FlowRunId | null): { events: StepCompletedEventDto[]; status: RunStatus | null } {
  const queryClient = useQueryClient();
  const [events, setEvents] = useState<StepCompletedEventDto[]>([]);

  useEffect(() => {
    if (!runId) return;

    // 1. Catch-up fetch — Section 8's server state, backing Volume 1 Part II
    //    §14.3's GetLiveRunProgressQuery, per the REST-then-stream pattern
    //    Volume 1 Part V §31.4 specifies exactly.
    queryClient.fetchQuery(liveRunProgressQuery(runId)).then((initial) => setEvents(initial.stepResults));

    // 2. Subscribe
    signalRConnection.invoke('SubscribeToRun', runId);
    const handler = (evt: StepCompletedEventDto) => setEvents((prev) => [...prev, evt]);
    signalRConnection.on('StepUpdate', handler);

    return () => {
      signalRConnection.invoke('UnsubscribeFromRun', runId);
      signalRConnection.off('StepUpdate', handler);
    };
  }, [runId]);

  // 3. On reconnect, re-run the catch-up fetch — Section 15.4
  useOnSignalRReconnect(() => { if (runId) queryClient.invalidateQueries(liveRunProgressQuery(runId)); });

  return { events, status: deriveRunStatus(events) };
}

This hook is the single implementation every consumer (Section 6.3's RunProgressView, Section 12.7's canvas overlay) shares — directly avoiding two independently-written, potentially-divergent SignalR subscription implementations for what is conceptually one capability, consistent with this volume's repeated "define once" discipline (Sections 9.4, 10.6, 12.5 all made the same choice for their respective concerns).

15.4 Reconnection handling — implementing Volume 1's explicit warning

Volume 1, Part V, Section 31.4 was explicit: "the client re-subscribes AND re-fetches via GetLiveRunProgressQuery again — never assumes no events were missed during the gap." Section 15.3's useOnSignalRReconnect callback is the literal implementation of that instruction — a connection drop and automatic reconnect (handled by SignalR's built-in withAutomaticReconnect, 15.2) triggers a full state reconciliation via the REST catch-up query, never a bare resume-and-hope. A brief "reconnecting..." indicator (using the design system's signal-state vocabulary, Section 2.6 — specifically an amber, non-error treatment, since a reconnect-in-progress is expected transient behavior, not a failure) is shown during this window, directly serving UX Principle 1.2's "show real state" by never letting the UI silently display now-possibly-stale data without any indication.

This reconciliation is trustworthy for a stronger reason than client-side connection handling alone: Volume 1, Part III, Section 16.7's incremental per-step commit design means the catch-up query reflects genuinely durable backend state, not merely "whatever the server happened to have in memory." A reconnect that spans not just a brief client-side network blip but an actual backend process interruption (an orchestration engine pod restart mid-run, per Volume 1, Part VI, Section 37.6's graceful-shutdown handling) still resolves correctly here — the catch-up fetch returns every step genuinely completed before the interruption, because Volume 1 now guarantees each one was individually committed as it happened, not batched and at risk of being lost together. This frontend section's reconciliation logic required no design change to benefit from that backend guarantee; it was already built to distrust its own assumptions about what it might have missed (per Volume 1, Part V, Section 31.4's original instruction), and simply inherits a stronger, now-provably-accurate answer to "what actually happened" as a result.

15.5 Backplane transparency — the frontend doesn't need to know

Volume 1, Part V, Section 31.5 specified a Redis-backed SignalR backplane for horizontal scaling across multiple nexus-orchestration/API Layer instances. This is entirely transparent to the frontend client — the connection-management code in 15.2–15.3 requires zero awareness of backend scaling topology, which is precisely the point of that backend design decision: the frontend's contract is with the SignalR hub abstraction, not with the specific instances serving it, a direct, concrete instance of clean separation between this volume's concerns and Volume 1's paying off exactly as intended.

15.6 Multiplexing across multiple watched runs
typescript
function useMultiRunLiveStatus(runIds: FlowRunId[]): Record<string, RunStatus> {
  // Used by, e.g., a "runs in progress" widget on the reporting dashboard's
  // real-time-adjacent corner — but NOT the main trend dashboard itself,
  // per Section 6.5's explicit rule that reporting-audit's primary views
  // never depend on SignalR. This hook is scoped to a genuinely different,
  // narrower use case: a small, explicitly real-time widget, not the
  // dashboard's core data.
}

This hook is called out specifically to reinforce, at the implementation level, the boundary Section 6.5 already established at the feature-architecture level — SignalR usage in the reporting surface is narrow and explicitly justified per instance, never a default assumption that "more real-time is always better."

15.7 Error and degraded-connectivity handling

If the SignalR connection fails to establish at all (a restrictive network/proxy blocking WebSocket upgrade, falling back through SignalR's negotiated transport options to long-polling, and ultimately failing even that), useLiveRunProgress degrades to REST-polling at a conservative interval (e.g., every 5 seconds) rather than leaving the live-progress view entirely non-functional — a direct application of UX Principle 1.7's honest-degradation requirement: a user on a restrictive network sees slightly-less-live progress updates with a visible "live updates unavailable, refreshing periodically" notice, never a silently frozen view that looks live but isn't.

15.8 Testing SignalR-dependent components

Given the real-time, connection-stateful nature of this integration, useLiveRunProgress and its consumers are tested against a mocked HubConnection implementation (a test double satisfying the same interface as 15.2's real connection) for unit/component tests, with genuine SignalR connection behavior — including the reconnection-and-reconcile path specified in 15.4 — covered by the end-to-end test suite (Section 19) running against a real backend instance, consistent with the same unit-vs-integration testing tier split already established for the canvas in Section 12.9.



16. Offline Support
16.1 Scoping this section honestly

Consistent with this document set's established practice of stating limitations plainly rather than overclaiming (Volume 1, Part VI, Section 39.9's honest scaling-ceiling disclosure is the direct model this section follows): Nexus is not, and is not being built as, an offline-first application. Every one of its core capabilities — triggering a flow run, viewing live progress, browsing the marketplace, reading reports — fundamentally requires connectivity to the backend, since the backend is where actual automation execution happens (Volume 1, Part I, Section 2.4's system boundary). This section specifies what "offline support" concretely and honestly means for a tool like this: resilience to brief connectivity interruption, and protection against data loss during that interruption — not full offline operation.

16.2 What this section does deliver

Three concrete guarantees, each scoped to a specific, realistic failure mode rather than a general "works offline" claim:

In-progress authoring work survives a connectivity drop (16.3).
The user is always told the truth about connectivity state (16.4), never left guessing whether the app is broken or the network is.
Queued, safe-to-retry mutations are not silently lost during a brief interruption (16.5).
16.3 Draft persistence — local-first for unsaved authoring state

Building on Volume 2, Section 11.9's autosave design: in addition to periodic server-side autosave, the Workflow Designer's in-progress draft state is also persisted to IndexedDB locally, on every meaningful edit, independent of network availability. If a connectivity drop occurs mid-edit, the user can continue editing locally (the canvas remains fully interactive — it's a client-rendered application, per Section 4.3, and editing a Flow draft is a local, in-memory operation until explicitly saved) and the local IndexedDB copy is reconciled against the server the moment connectivity returns:

typescript
async function reconcileDraftOnReconnect(flowId: FlowId) {
  const localDraft = await getLocalDraft(flowId);         // IndexedDB
  const serverDraft = await apiClient.getFlow(flowId);     // Section 8's server state
  if (localDraft.lastEditedAt > serverDraft.updatedAt) {
    await apiClient.syncDraft(flowId, localDraft);          // push local changes
  } else if (serverDraft.updatedAt > localDraft.lastEditedAt) {
    // A rare case (e.g., the same draft edited in a second tab that stayed
    // connected) — surfaced to the user as an explicit conflict choice,
    // never silently overwritten in either direction, per UX Principle 1.2
    showDraftConflictDialog(localDraft, serverDraft);
  }
}

This reconciliation logic deliberately never silently discards either version — a genuine, if rare, conflict is surfaced to the human, consistent with UX Principle 1.2's "never a fiction of the real state" applied specifically to the case where two "real states" (local and server) have diverged and only a human can correctly decide which one wins.

16.4 Connectivity status — always visible, never assumed

A persistent, unobtrusive connectivity indicator (using the design system's signal-state vocabulary, Section 2.6) reflects genuine, actively-monitored connectivity — not merely navigator.onLine (a notoriously unreliable browser API that reports link-layer connectivity, not actual reachability of the Nexus backend specifically), but a real heartbeat check against the API Layer's health endpoint (Volume 1, Part IV, Section 29.7's /health/live) combined with the SignalR connection's own state (Section 15.4's reconnection handling). This directly extends UX Principle 1.7's honest-degradation requirement to connectivity itself: the application never presents a falsely-confident "everything is fine" state when it cannot actually verify that.

16.5 Mutation queuing — narrow, explicit, and only for genuinely safe operations

A small, explicitly-enumerated set of mutations queue locally and retry automatically on reconnect: draft-save operations (16.3) and, per Volume 1, Part V, Section 30.4's idempotency-key design, TriggerFlowRunCommand specifically when issued with an idempotency key already generated client-side before the network drop occurred. No other mutation is queued — publish, module registration, role assignment, and every other state-changing operation in Volume 1's command catalog (Part II, Section 13) fails immediately and visibly on a connectivity drop, requiring explicit user retry once reconnected, rather than being silently queued and executed later at an unpredictable time the user may no longer expect or want. This narrow scope is a deliberate safety decision: queuing an operation like PublishFlowCommand for automatic later execution risks a confusing, delayed side effect the user has forgotten about or no longer intends — the idempotency-key-protected run-trigger case is safe specifically because Volume 1 already designed it to be safely retryable by construction (Volume 1, Part V, Section 30.4), and this section extends that safety property to the offline case rather than inventing a new one.

16.6 What is explicitly not supported

Stated exhaustively, mirroring Section 16.1's honesty discipline: live-run progress (Section 15) does not queue or replay while offline in the sense of the client fabricating or interpolating anything about the gap — but this is a narrower limitation than it might first sound, and worth being precise about now that Volume 1, Part III, Section 16.7 guarantees incremental, per-step backend durability. A user who loses connectivity mid-run-watching sees the connectivity indicator (16.4) and, on reconnect, the SignalR reconciliation (Section 15.4) catches them up to the genuinely complete history of every step that finished during the gap — not an approximation, and not contingent on the backend having stayed up the whole time either, per Section 15.4's updated discussion of that guarantee. What the client specifically does not do is show a locally-simulated "replay" of the gap (an animated catch-up sequence re-playing each missed step's arrival) — the reconciled state simply appears, accurately and completely, as of reconnect. The reporting dashboard, module marketplace, and administration surfaces (Section 6.4–6.6) have no offline behavior at all beyond standard browser caching of already-fetched TanStack Query data (Section 8.2) incidentally remaining visible-but-stale until the connectivity indicator (16.4) makes that staleness explicit — this is existing cache behavior, not a purpose-built offline feature, and is described here only to be precise about what a user should and shouldn't expect.

16.7 Service worker scope — asset caching only, not data

A service worker is used exclusively for static-asset caching (enabling faster reload after a brief connectivity drop, and a smoother initial-load experience generally) — it does not intercept or cache API responses, and does not implement a general offline-data-sync framework (e.g., no background-sync API usage beyond what 16.5's narrow mutation queue requires). This is a deliberate, minimal-scope choice consistent with Section 16.1's honest framing: a broader service-worker-based offline-data architecture would be substantial engineering investment in service of a use case (fully offline operation of a live automation-execution platform) that doesn't coherently exist for this product, and would risk the exact kind of "unified platform" credibility-tax problem Volume 0, Section 8.6 warned about if it created an impression of offline capability the product doesn't and structurally can't fully deliver.

16.8 Self-hosted, air-gapped deployments — a different, already-solved case

Worth distinguishing explicitly from this section's "offline support" scope: Volume 0's air-gapped self-hosted deployment target (Section 19.5; Volume 1, Part VI, Section 37.7) is not an "offline" scenario in this section's sense at all — in that deployment model, the Nexus backend itself runs within the customer's network, so the GUI is fully "online" relative to its own backend even with zero general internet access. This section's offline-support scope is specifically about brief, incidental connectivity loss between an otherwise-online client and its backend, a genuinely different concern from air-gapped deployment, which Volume 1's infrastructure sections already address completely on their own terms.


___________________________________________________________________________________________________________________________________________________



17. Performance
17.1 Purpose and relationship to NFR-UX-2

Volume 0's NFR-UX-2 (30-minute time-to-first-flow) is fundamentally a usability metric, but frontend performance is a direct, measurable contributor to it — a sluggish authoring loop (Section 11's canvas, Section 10's Monaco integration) erodes that budget just as surely as a confusing information architecture would. This section specifies concrete performance budgets and the engineering practices that hold them, mirroring the rigor Volume 1, Part VI, Section 38 established for backend performance.

17.2 Bundle size budgets
Bundle	Budget (gzipped)	Rationale
Initial shell (app bootstrap, routing, identity-access)	150 KB	The only bundle every user pays for on first load, including Persona Sofia's occasional dashboard-only visits — kept lean specifically for her use case, which shouldn't require downloading authoring-surface code she'll never touch
flow-authoring feature chunk (excluding Monaco/React Flow)	200 KB	Route-level code split, loaded only on navigation to /flows/*
Monaco Editor chunk	Separately lazy-loaded, no fixed budget (inherently large)	Per Section 10.7's deferred-load strategy — never part of the initial or authoring-route bundle
React Flow / canvas chunk	Separately lazy-loaded	Same rationale as Monaco — the canvas is substantial and deferred until actually needed
reporting-audit feature chunk	150 KB	Recharts (Section 9.3's TrendChart) is the main contributor; also lazy-loaded per-route

These budgets are enforced in CI via a bundle-size-analysis step that fails the build on regression beyond a small tolerance — mirroring Volume 1, Part VI, Section 38's "performance regression treated with the same severity as a functional bug" principle, applied here to bundle weight specifically.

17.3 Code splitting strategy

Every route (Section 7.2's table) is its own lazy-loaded chunk via React.lazy, and within flow-authoring specifically, Monaco (Section 10.7) and React Flow (Section 12.8's virtualization note) are further split into their own sub-chunks loaded only when the raw-editing mode or the canvas is actually rendered — a user who only ever uses the AI-generation panel (Section 6.2) to create flows and never opens raw JSON editing never pays Monaco's bundle cost at all. This is the concrete implementation of UX Principle 1.5 (progressive disclosure) applied to loading strategy, not just UI layout: the code itself discloses progressively, matching the interface's progressive disclosure.

17.4 Rendering performance — the canvas and large lists

Beyond Section 12.8's React Flow virtualization: DataTable (Section 9.3), used for flow lists, run history, and audit logs alike, virtualizes rows beyond a threshold (e.g., 50 visible rows) regardless of the underlying dataset size, directly relevant to NFR-SCALE-3's 10-million-row backend target (Volume 1, Part V, Section 32.6) — a frontend that rendered every row of a large ListRecentFlowRunsQuery result naively would defeat the backend's own pagination design (Volume 1, Part V, Section 30.4). Pagination (server-side, per that same section) is the primary mitigation; virtualization is the secondary, defense-in-depth layer for within-page rendering smoothness.

17.5 Memoization discipline

React.memo, useMemo, and useCallback are applied deliberately at specific, identified hot paths — the FlowStepNode canvas renderer (Section 12.2, re-rendering potentially many times per second during live-run overlay updates, Section 12.7) and the SchemaDrivenParamEditor (Section 9.4, re-rendering on every keystroke of a sibling field if not properly scoped) — rather than applied reflexively everywhere. Consistent with this volume's repeated "don't add complexity that doesn't earn its cost" discipline (echoed from Volume 1, Architecture Principle 20.7): blanket memoization without profiling data justifying it is treated as a code-review concern (unnecessary complexity, potential for stale-closure bugs) rather than a default good practice.

17.6 Data-fetching performance — avoiding waterfalls

Route loaders (Section 7.4) and TanStack Query's parallel-query capabilities are used deliberately to avoid request waterfalls — a route like /runs/:id (Volume 2, Section 7.2) that needs both the FlowRun detail and its parent Flow's definition fetches both in parallel from the loader, not sequentially (fetch the run, wait, then fetch the flow only once the run response reveals the flowId, where avoidable) — reviewed explicitly for any new route added to Section 7's table.

17.7 Core Web Vitals and real-user monitoring

Standard Core Web Vitals (LCP, INP, CLS) are tracked via real-user monitoring, exported through the same OpenTelemetry pipeline Volume 1, Part IV, Section 29 established for backend observability — frontend and backend performance telemetry converge into one observability system, not two separate ones, directly extending Volume 1's "the platform observing itself" principle (Part IV, Section 29.1) to include the client experience, not just server-side execution. This gives Volume 1's alerting philosophy (Part IV, Section 29.6) a genuinely complete picture: a backend that's fast but a frontend that's slow to render results is still, from Persona Priya's perspective, a slow platform, and this telemetry convergence is what makes that visible to whoever's on call.

17.8 SignalR and canvas update-rate throttling

During a fast-executing flow (many steps completing within a short window), raw per-event canvas re-rendering (Section 12.7) could exceed a smooth frame budget; useLiveRunProgress (Section 15.3) batches rapid-succession events within a short window (e.g., 100ms) before triggering a re-render, trading negligible perceived latency for guaranteed rendering smoothness — a deliberate, narrow performance optimization justified by the specific, identified hot path (fast flows with many steps), not a general debouncing policy applied without cause, consistent with Section 17.5's memoization-discipline reasoning.

17.9 Performance budget as a release gate

Mirroring Volume 1, Part VI, Section 38.8's load-testing release gate: Lighthouse CI (or equivalent) runs against representative pages (the flow editor with a realistic flow loaded, the trend dashboard with representative data volume) as a mandatory CI check before any GA-track release, with specific, numeric thresholds for LCP and INP directly tied to NFR-UX-2's overall usability commitment — a frontend performance regression is a release blocker, not a follow-up ticket, for the same reasons Volume 1 already established for backend performance regressions.



18. Accessibility
18.1 Purpose and the NFR-UX-1 target

Volume 0's NFR-UX-1 requires WCAG 2.1 AA conformance for the flow-authoring GUI — stated there as a hard requirement tied directly to enterprise/public-sector procurement conditions (Volume 0, Section 13.6), not an aspirational goal. This section specifies how that conformance is achieved and verified across every surface this volume has specified, not just the authoring GUI narrowly.

18.2 Conformance scope — every surface, not just flow authoring

While NFR-UX-1's original wording named the authoring GUI specifically, this section extends the same AA conformance bar to every Feature module (Section 6) without exception — the reporting dashboard (Sofia's primary surface), the admin/settings pages, and the login flow are held to the identical standard. Narrowing conformance to only the literal wording of the NFR would create an inconsistent, partially-accessible product, directly contradicting UX Principle 1.6's persona-equity spirit (Section 1.6) — Sofia's occasional but important dashboard visits deserve the same accessibility guarantee Priya's daily authoring sessions get.

18.3 Color contrast — verified per theme, per Section 3's discipline

Directly extending Section 3.4's "no partial theme overrides" rule: every one of the four theme variants (dark, light, high-contrast, and the enterprise-branding extension point from Section 3.6) is independently verified against WCAG AA contrast ratios (4.5:1 for normal text, 3:1 for large text and UI component boundaries) — including, critically, the signal-state palette (Section 2.6): a Failed-red badge's text must meet contrast against its background in every theme, not just the default dark theme it was originally designed against. This verification is automated (axe-core or equivalent, integrated into the Storybook visual-regression pipeline already established in Section 9.7) rather than manually spot-checked, since a design-token change (Section 2.9's governance process) could otherwise silently introduce a contrast regression in a theme nobody happened to manually re-check.

18.4 Keyboard navigation

Every interactive surface is fully keyboard-operable, with the Workflow Designer canvas (Section 11) as the specific, hardest case worth calling out explicitly: node selection, connection creation, and step reordering all have keyboard-accessible equivalents to their mouse-drag interactions (arrow-key node navigation, a keyboard-invoked "connect mode" as an alternative to drag-to-connect, and a step-reordering command accessible via a context menu rather than requiring drag-and-drop) — directly serving the same "no capability exclusively available through an inherently-limited interaction model" principle Section 10.8 already established for Monaco's accessibility fallback, applied here to React Flow's canvas interactions specifically.

18.5 Screen reader support — the canvas's specific challenge

The Workflow Designer canvas (Section 11–12) is, per Section 10.8's Monaco discussion, an inherently harder surface for screen-reader users than standard form-based UI. Nexus's approach: the canvas is one, visually-primary representation of a Flow, but Section 10.3's raw-editing mode and, more specifically, a dedicated structured list view (a third representation, alongside canvas and raw-JSON: an accessible, linear, fully-keyboard-and-screen-reader-native list of steps with the same edit capabilities as the canvas) ensure no capability is canvas-exclusive. This structured list view is not a lesser, read-only fallback — it supports the complete set of authoring operations (add, edit, reorder, delete, connect via explicit "depends on" fields rather than visual edges) as a fully first-class alternative, directly satisfying WCAG's requirement that equivalent functionality be available through an accessible interaction model, not merely that the inaccessible one be somewhat mitigated.

18.6 ARIA live regions for real-time updates

Live-run progress (Section 15) — a core, distinguishing product experience — presents a specific accessibility challenge: visual users perceive step completion through the canvas's signal-state animation (Section 2.8), but this needs an equivalent, non-visual channel. RunProgressView (Section 6.3) includes an ARIA live region (aria-live="polite", chosen over "assertive" specifically to avoid interrupting a screen-reader user's current focus for every single step completion, which would be disruptive for a fast-running flow) announcing step completions and the final run outcome — calibrated to announce meaningfully (step name and status), not so verbosely that rapid multi-step completion becomes an unusable wall of audio.

18.7 AI-attribution accessibility — UX Principle 1.6, made concrete

Directly extending UX Principle 1.6 (Section 1.7) and the AiAttributionBadge component (Section 9.3): the AI-assisted visual treatment (Section 2.6's teal badge) is never conveyed by color alone — every AI-attributed result additionally carries a text label ("AI-assisted") and, where relevant, an icon, satisfying WCAG's general "don't rely on color alone to convey information" success criterion while also serving this volume's own transparency principle simultaneously — a case where accessibility best practice and Nexus's specific product-trust requirement happen to demand the exact same implementation, reinforcing rather than trading off against each other.

18.8 Focus management

Modal dialogs (Modal, Section 9.3) trap focus per Section 9.8's centralized-primitive accessibility contract; route transitions (Section 7) move focus to the new page's primary heading, not silently leaving focus on a now-unmounted element or resetting to the document body; the flow-step editor (Section 9.4), when opened for a newly-added step, moves focus to that step's first editable field — every one of these is a specific, deliberate focus-management decision, not an incidental byproduct of component structure, reviewed as part of the same PR checklist discipline Volume 1, Part VI, Section 40.3 established for backend code, extended here with accessibility-specific items.

18.9 Automated and manual verification

Automated accessibility testing (axe-core, integrated per Section 18.3 into Storybook and, separately, into the end-to-end test suite, Section 19) catches the class of issue that's mechanically detectable (contrast, missing ARIA labels, invalid heading hierarchy) — but is explicitly not treated as sufficient alone: manual testing with actual screen readers (NVDA, JAWS, VoiceOver) against the Workflow Designer's structured-list fallback (18.5) and the live-progress ARIA live region (18.6) specifically is a required part of the release checklist for any change touching those two surfaces, since these are exactly the interaction patterns automated tooling is least reliable at evaluating — a direct, honest acknowledgment that automated accessibility testing has real limits, consistent with this document set's general discipline of not overclaiming what a given mechanism actually verifies.

18.10 Accessibility as a design-system responsibility, restated

This section closes by reinforcing Section 9.8's original claim directly: because every Primitive component (Section 9.2) implements its accessibility contract once, centrally, the majority of this section's requirements are satisfied automatically by any Feature module correctly composing those primitives, rather than needing independent accessibility engineering per surface — the canvas (18.4–18.5) and live-progress announcements (18.6) are the two genuine, irreducible exceptions requiring bespoke accessibility work, precisely because they're the two surfaces in this entire volume that don't reduce to standard form-and-list UI patterns the Component Library's primitives already cover.



19. Testing Strategy
19.1 Purpose — consolidating this volume's testing references

Nearly every prior section in this volume deferred a specific testing obligation to "Section 19." This closing section collects those deferred obligations into one coherent, tiered testing strategy — the frontend counterpart to Volume 1, Part VI, Section 40.6's backend testing conventions, and the section that finally makes UX Principle 1.4 (module parity, zero hardcoding) a provable, continuously-verified property of the shipped product rather than a design intention.

19.2 The four testing tiers
Tier	Scope	Tooling	Runs
Unit	Pure functions, hooks in isolation (mocked dependencies)	Vitest	Every commit
Component	Individual React components rendered in isolation, including Storybook-driven visual regression (Section 9.7)	React Testing Library + Vitest, Chromatic (or equivalent) for visual diffs	Every commit
Integration	Feature modules (Section 6) with real TanStack Query/Zustand wiring, mocked API/SignalR at the network boundary	React Testing Library + MSW (Mock Service Worker)	Every commit
End-to-end	Full application against a real backend instance	Playwright — deliberately Nexus's own Web module, per Section 19.8	Pre-merge (full suite) and post-deploy smoke subset

This tiering mirrors Volume 1, Part VI, Section 40.6's fast-to-slow, isolated-to-integrated progression exactly, adapted to frontend-specific concerns.

19.3 Unit and hook testing

Every custom hook specified across this volume — useAuth, usePermission (Section 14.4), useLiveRunProgress (Section 15.3), useVariableFlowHighlight (Section 12.5) — has dedicated unit tests covering its logic independent of the components that consume it, consistent with the same "test the logic, not the rendering, where they're separable" discipline Volume 1 established for command handlers (Part VI, Section 40.6).

19.4 Component testing and the design-system guardrail

Directly implementing Section 9.7's visual-regression requirement: every Storybook story is captured as a baseline, and any pull request producing a visual diff against that baseline requires explicit reviewer approval before merge — a mechanical safeguard against the exact silent-drift failure mode Section 2.9's design-system governance exists to prevent, now enforced by tooling rather than relying on a reviewer noticing a subtle color or spacing change by eye.

19.5 The module-parity test — proving UX Principle 1.3/1.4, not just following it

This is the single most important test in this volume, existing specifically because UX Principle 1.3 (Section 1.4) made a strong, structural claim — "no UI component may contain module-specific branching logic" — that deserves more than trust:

typescript
describe('Module parity', () => {
  it('renders a completely synthetic, previously-unknown module with zero special-casing', async () => {
    const syntheticModule: ModuleActionDto = {
      moduleKind: 'synthetic-test-module-xyz',   // guaranteed not to match any
                                                    // built-in ModuleKind or any
                                                    // fixture the codebase has
                                                    // ever seen before
      actions: [{
        name: 'doSomethingNovel',
        parameters: [
          { name: 'targetId', type: 'String', required: true },
          { name: 'retryCount', type: 'Number', required: false },
          { name: 'payload', type: 'Object', required: true },
        ],
      }],
    };

    render(<FlowStepEditor availableModules={[syntheticModule]} />);

    // Asserts: the module/action picker (Section 11.6) lists it with the
    // correct fallback icon (Section 2.7); selecting it renders the
    // SchemaDrivenParamEditor (Section 9.4) with correctly-typed fields for
    // all three parameters; the canvas (Section 12.2) renders a FlowStepNode
    // for it without throwing or falling back to an error state.
    await userEvent.click(screen.getByText('synthetic-test-module-xyz'));
    expect(screen.getByLabelText('targetId')).toBeRequired();
    expect(screen.getByLabelText('retryCount')).not.toBeRequired();
    expect(screen.getByTestId('payload-json-editor')).toBeInTheDocument();
  });
});

This test runs in CI on every commit that touches SchemaDrivenParamEditor, FlowStepNode, or the module/action picker — a regression here (someone accidentally introducing a ModuleKind-specific branch, per Section 1.4's prohibition) fails the build immediately, making the module-parity principle a continuously-enforced invariant, not a one-time design review outcome.

19.6 Integration testing with MSW

Feature-module integration tests (Section 19.2's third tier) mock the network boundary with MSW rather than mocking TanStack Query or the API client directly — this ensures the tests exercise the real request/response cycle, cache-invalidation logic (Section 8.3), and error-handling paths (Section 30.5's RFC 7807 format, Volume 1) exactly as they'd behave against the real backend, with only the actual network call intercepted. SignalR is similarly mocked at this tier via a fake HubConnection (Section 15.8), never by stubbing out useLiveRunProgress itself, for the same reason — the integration test should exercise real hook logic against a controlled, fake transport, not bypass the logic entirely.

19.7 Accessibility testing integration

Per Section 18.9: automated axe-core checks run as part of both the Component tier (every Storybook story) and the Integration tier (key Feature-module flows — publishing a flow, triggering a run, navigating the reporting dashboard) — with the manual screen-reader verification Section 18.9 required for the canvas's structured-list fallback (Section 18.5) and the live-progress ARIA live region (Section 18.6) tracked as an explicit, named pre-release checklist item, not something automated tooling is trusted to cover on its own.

19.8 End-to-end testing — Nexus testing itself

The most notable architectural choice in this section: Nexus's end-to-end test suite is built using Nexus's own Web module (Volume 1, Part III, Section 18; the product this entire document set specifies) rather than a separately-adopted E2E framework unrelated to the product itself. Concretely, the E2E suite is a collection of Nexus flows (Volume 1, Part II, Section 8.2's Flow definitions) that drive the Nexus GUI itself as their target — Priya's authoring journey (Volume 0, UC-2), Marcus's CI-triggered run (UC-4), and Sofia's dashboard review (UC-5) are each expressed as an actual Nexus flow, executed by an actual Nexus flow run, asserting on the actual Nexus GUI's rendered state via the Web module's assertion capabilities (Volume 1, Part III, Section 18.2's assertText/waitForSelector actions).

This is a deliberate dogfooding choice with real, direct value beyond novelty: it exercises the Web module's genuine production capability against a real, complex application (not a toy test fixture) on every CI run, meaning a regression in Nexus's own Web automation capability is caught by Nexus testing itself before it would ever reach a customer — the most direct, continuous validation of Volume 0's core product promise (Business Objective 5's trust posture) available. It also means the E2E suite's own reports (Volume 1, Part II, Section 8.5's FlowReport) are genuine Nexus artifacts, viewable in the exact reporting dashboard (Section 6.5) this volume specifies — closing the loop completely.

19.9 E2E suite scope and the smoke/full split

Given genuine E2E test runtime cost, the full E2E suite (covering every persona journey from Volume 0's use-case catalog, Section 11) runs pre-merge on the primary integration branch; a smaller, curated smoke subset (login, create-and-publish-a-basic-flow, trigger-and-view-a-run) runs post-deploy against each environment as a fast release-health check, mirroring Volume 1, Part VI, Section 37's deployment-gating philosophy extended to the frontend's own release process.

19.10 Performance and bundle-size testing integration

Section 17.9's Lighthouse CI and Section 17.2's bundle-size budgets are both integrated into this same CI pipeline as additional, mandatory gates — not a separately-scheduled process — ensuring a single pull request's "ready to merge" state means functionally correct, accessible, visually non-regressed, and performance-budget-compliant simultaneously, never any one of these verified in isolation from the others.

19.11 Testing the Plugin UI sandbox

Directly extending Section 13's trust-boundary design: automated tests specifically verify the sandbox's negative guarantees (Section 13.6) — a test harness loads a deliberately malicious test extension attempting each prohibited action (accessing document.cookie, attempting window.top navigation, attempting a direct fetch to the Nexus API) and asserts every attempt is blocked by the iframe sandbox, not merely assumed to be blocked because the sandbox attribute is set correctly. This is the frontend-testing counterpart to Volume 1, Part VI, Section 40.6's requirement that every Permission have an explicit authorized/unauthorized test pair — a security boundary is only trustworthy once it's been actually, adversarially tested, not just configured correctly by inspection.

19.12 Closing note — Volume 2 complete

This section closes Volume 2 in full: 19 sections across 5 Parts, tracing from UX principles and the design system (Part I) through application architecture (Part II), the core authoring and reporting surfaces (Part III), platform integration (Part IV), and now the quality and testing discipline that keeps all of it correct (this Part). Every design decision in this volume traces back through Volume 0's personas and requirements and Volume 1's API/domain contracts — and, as this session demonstrated directly with the Unit of Work correction, every volume in this set remains genuinely load-bearing for the others: a gap found while writing Volume 2 flowed back into a real fix in Volume 1, exactly the kind of cross-volume consistency this document set's traceability discipline (Volume 0, Section 20.9; Volume 1, Part VI, Section 40.8) was built to make possible, not merely aspire to.


__________________________________________________________________________________________________________________________________________________________________



📘 ADS-003 — AI Architecture Design

Objective

Design an AI subsystem that assists throughout the automation lifecycle without being tightly coupled to a specific model provider.

Chapter 1 – Vision

AI-first Automation Platform



Chapter 2 – AI Responsibilities

Generate tests

Heal locators

Analyze failures

Generate reports

Summarize logs

Suggest optimizations

Natural language execution


Chapter 3 – AI Services

Planner

Generator

Analyzer

Healer

Reporter

Assistant

Knowledge Base


Chapter 4 – Provider Abstraction
OpenAI

Azure OpenAI

Anthropic

Google Gemini

Local Models

Future Providers



Chapter 5 – Prompt Framework

Reusable prompt templates

Context injection

Memory

RAG support

Chapter 6 – Knowledge Layer

Documentation

Requirements

Screenshots

Logs

Historical executions

Best practices


Chapter 7 – Self-Healing

Locator healing

Wait strategy adaptation

Retry recommendations

Fallback execution


Chapter 8 – AI Security

PII masking

Prompt validation

Provider failover

Rate limiting

Audit logs


___________________________________________________________________________________________________________________________________________________________________


