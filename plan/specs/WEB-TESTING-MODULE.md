# Yukti — Web UI Testing Module Spec

Selenium-class capability on Playwright · Oct 27, 2026

## Scope and how to use this spec

**For Sutradhar.** Self-contained spec for the Web module. Work tasks in order. A task is done only when its ✔ command passes and every checkbox is met. Standard autonomy policy applies.

**Governing rule:** nothing here changes `IAutomationModule`, the engine, the registry, the schema-driven UI, the report model or the CLI. Every capability is a new action, parameter or assertion on the existing Web module, plus one new shared layer (WEB-T1) and one new artifact kind (WEB-T2). If any task appears to need module-specific code above the module, **stop and escalate**.

### The engine decision — locked, do not revisit

This module stays on **Playwright**. It does not move to Selenium WebDriver.

"Selenium-class" in this spec means Selenium's **capability set** — locator strategies, implicit and explicit waits, rich actions, the Page Object Model — not the Selenium library. Playwright provides all of it, with auto-waiting that produces materially lower flake rates, and the Day 6 SSRF guard (CDP-level interception, after the proven Chromium loopback-proxy finding) depends on Playwright. Rebuilding on WebDriver would cost weeks and raise flake rates.

Record this as **ADR-00NN** before starting: context, decision, consequences, alternatives, revisit-if.

### What exists today

| Capability | State |
| --- | --- |
| `navigate`, `click`, `fill`, `select`, `waitFor`, `assertText`, `assertVisible`, `screenshot` | Working, tested |
| Typed `SelectorNotFound` error | Working |
| SSRF guard via CDP interception | Working (Day 6, T5) |
| Locators | Raw strings embedded in steps — the main gap |

### What this spec adds

A shared UI vocabulary layer; the Object Repository; seven locator strategies with fallbacks; explicit wait conditions; the full action set (hover, drag, keyboard, frames, windows, dialogs, upload); the full assertion set; browser and context configuration; storage-state reuse.

### Dependencies

- [ ] **None blocking.** This module can start immediately.
- [ ] WEB-T1 (the shared vocabulary) and WEB-T2 (the Object Repository) are **prerequisites for the Mobile module**. Build them to be engine-neutral from the start, because the Mobile spec assumes them.

## Domain model

```csharp
// ── The shared UI vocabulary (WEB-T1) ──
// ENGINE-NEUTRAL. No Playwright type may appear in this layer.
// The Mobile module implements the same vocabulary against Appium.

public sealed record Locator(
    LocatorSpec            Primary,
    IReadOnlyList<LocatorSpec> Fallbacks,
    string?                Description,
    Locator?               Within,      // chaining: find inside a parent
    int?                   Nth);        // when several match

public sealed record LocatorSpec(LocatorStrategy Strategy, string Value, string? Name);

public enum LocatorStrategy {
    // Web
    TestId, Role, Label, Text, Placeholder, Css, XPath,
    // Mobile — declared now so the enum never changes in the Mobile phase
    AccessibilityId, ResourceId, IosPredicate, IosClassChain, ClassName
}

public abstract record WaitCondition;
public sealed record Visible(Locator L)                          : WaitCondition;
public sealed record Hidden(Locator L)                           : WaitCondition;
public sealed record Enabled(Locator L)                          : WaitCondition;
public sealed record Disabled(Locator L)                         : WaitCondition;
public sealed record TextEquals(Locator L, string Expected)      : WaitCondition;
public sealed record TextContains(Locator L, string Fragment)    : WaitCondition;
public sealed record AttributeEquals(Locator L, string Attr, string Expected) : WaitCondition;
public sealed record CountEquals(Locator L, int Expected)        : WaitCondition;
public sealed record UrlEquals(string Expected)                  : WaitCondition;
public sealed record UrlContains(string Fragment)                : WaitCondition;
public sealed record TitleContains(string Fragment)               : WaitCondition;
public sealed record NetworkIdle(TimeSpan Quiet)                 : WaitCondition;
public sealed record CustomPredicate(string Script)              : WaitCondition;  // P1

// ── The Object Repository (WEB-T2) ──
public sealed record ObjectRepository(
    RepositoryId Id, string Name, int Version, RepositoryStatus Status,
    TenantId TenantId,
    IReadOnlyList<RepositoryPage> Pages);

public sealed record RepositoryPage(string Name, IReadOnlyDictionary<string, Locator> Elements);

// A step references "Checkout page › submitButton" OR carries an inline Locator
public abstract record LocatorRef;
public sealed record RepositoryRef(string Page, string Element) : LocatorRef;
public sealed record InlineLocator(Locator L)                   : LocatorRef;
```

### How a locator resolves

[embedded content: Locator resolution · fallbacks turn silent decay into a visible warning]

The flagged middle path is the point of the whole design. A step that succeeds via a fallback **passes and is flagged**, with the report saying which fallback was used and why the primary failed. That turns silent locator decay — the thing that quietly kills automation suites — into a visible, trackable signal. It's the same honesty rule already applied to flaky steps.

It also produces exactly the data self-healing will need later: a locator with recorded fallback usage, and the `AiAttribution` shape already exists to carry a proposed fix.

## Tasks — foundations

### WEB-T1 — The shared UI vocabulary (P0)

The layer everything else rests on, and the one the Mobile module will reuse.

- [x] Every type from the domain model exists in a new engine-neutral assembly or namespace.
  **2026-10-29:** `Yukti.Domain.UiVocabulary` (`Locator`, `LocatorSpec`, `LocatorStrategy`,
  `WaitCondition` + all 13 subtypes) — a namespace inside the existing `Yukti.Domain` project, not
  a new project, since `Yukti.Domain.csproj` already has zero `PackageReference`s and already
  "references nothing else in the solution" (its own `RootNamespace` comment) — placing the
  vocabulary there makes "no Playwright type" a structural guarantee, not just a convention.
- [x] **Hard rule, reviewer-enforced:** no Playwright type, no `IPage`, no `ILocator`, no browser-specific concept appears in this layer. A dependency check in CI fails the build if the vocabulary project references Playwright at all.
  **2026-10-29:** `UiVocabularyTests.Yukti_Domain_never_references_Playwright` asserts
  `typeof(Locator).Assembly.GetReferencedAssemblies()` contains nothing named `Playwright`; this
  test runs inside `dotnet test` in CI's existing `build-and-test` job, so a future PR that adds a
  Playwright reference to `Yukti.Domain` fails CI the same session it's added.
- [ ] The Web module translates vocabulary types into Playwright calls. That translation lives entirely inside the module.
  **Not yet** — `WebModule` still takes raw selector strings (WEB-T4's job, after WEB-T2/T3).
- [x] A design test: a stub "fake engine" implements the vocabulary with no browser. If it compiles and runs, the layer is genuinely neutral. This is the cheapest possible proof and it catches leakage early.
  **2026-10-29:** `UiVocabularyTests.FakeEngine` (test-only, not shipped in `Yukti.Domain` —
  WEB-T1 doesn't ask for a production engine abstraction, and building one now would be scope
  WEB-T3 owns) pattern-matches every `WaitCondition` case into a description string with zero
  Playwright/browser types in its signature or body; 13 theory cases, one per subtype.

✔ `dotnet test --filter "FullyQualifiedName~UiVocabulary"` → 17/17 passed. CI dependency check:
folded into the same test (see above) rather than a separate CI script step, consistent with this
repo's existing test-based-proof style (`ModulePluginContractTests`, `SecretRedactionTests`, etc.)
over ad-hoc shell checks.

Time-box: 3 h. **Closed 2026-10-29.**

### WEB-T2 — Object Repository artifact (P0)

- [ ] A first-class versioned artifact: Draft and Published states, tenant-scoped, stored and validated like a Flow. Reuse the existing artifact machinery; do not invent a parallel one.
- [ ] An entry holds a primary locator, an ordered fallback list, and a human-readable description. Entries are grouped into pages or screens — the Page Object Model grouping, without the code.
- [ ] Publishing a **flow** validates that every referenced repository entry exists, failing with the existing 422-naming-the-step behaviour. Reuse the existing publish validator.
- [ ] Publishing a **repository** validates that every entry has a primary locator and a non-empty description.
- [ ] Editing an entry affects every flow referencing it with **no flow edits**. Prove with three flows and one rename — this is the module's headline gate.
- [ ] Tenant isolation holds: a repository in tenant A is invisible and unreferenceable from tenant B. Test at the API and the raw-SQL level, as in Day 6's T1.

✔ `dotnet test --filter "FullyQualifiedName~ObjectRepository"`

Time-box: 5 h.

### WEB-T3 — Locator strategies and resolution (P0)

- [ ] All seven web strategies work: `TestId`, `Role` (with accessible name), `Label`, `Text` (exact and partial), `Placeholder`, `Css`, `XPath`.
- [ ] **Chaining** via `Within`: find inside a parent element. This replaces most complex XPath, and a test should demonstrate that by rewriting one.
- [ ] `Nth` selects among several matches; out-of-range fails clearly rather than silently picking nothing.
- [ ] Fallback resolution works as in the diagram, in order, each within the step's timeout budget.
- [ ] A fallback success produces a **flagged pass** carrying: which fallback, why the primary failed, and the entry's identity. Surfaced in the report and in the JUnit output as a property.
- [ ] Total failure reports every strategy tried, how many elements each matched, and a screenshot.
- [ ] **Mutation check:** break a primary locator in a test fixture, confirm the flagged-pass path fires; break all of them, confirm a clean failure with full diagnostics.

✔ `dotnet test --filter "FullyQualifiedName~LocatorResolution"`

Time-box: 4 h.

### WEB-T4 — Migrate the existing module (P0)

- [ ] Every existing Web action accepts a `LocatorRef` instead of a raw selector string.
- [ ] **Existing flows with inline selectors keep working.** Migration to the repository is opt-in, never forced. A test runs a pre-migration flow definition unchanged.
- [ ] All existing Web tests pass with no behavioural change.
- [ ] UC-1 and the self-test flow still pass; the 50-run flake rate stays under 2%.

✔ `dotnet test --filter "FullyQualifiedName~WebModule"` and `yukti run demo/uc1_flow.json`

Time-box: 3 h.

## Tasks — capability

### WEB-T5 — Waits (P0)

Selenium's implicit and explicit waits, expressed as data.

- [ ] **Flow-level default timeout**, overridable per step: the implicit-wait equivalent.
- [ ] **`waitFor` with conditions**: every `WaitCondition` from the domain model, the explicit-wait equivalent.
- [ ] A timeout reports **which condition never became true, what the actual state was, and how long it waited**. "Timed out after 30s" with no further detail is an incomplete implementation.
- [ ] **No `sleep` action exists.** This is deliberate: fixed sleeps are the single largest cause of slow, flaky suites. If someone genuinely needs one, they use `CustomPredicate`. Adding a `sleep` action requires an ADR and your approval.
- [ ] Playwright's built-in auto-waiting is preserved on every action — these conditions are for waiting on things you are *not* about to interact with.
- [ ] `NetworkIdle` waits for a quiet period with no in-flight requests, which is the reliable way to wait for an asynchronous page to settle.

✔ `dotnet test --filter "FullyQualifiedName~WebWait"`

Time-box: 3 h.

### WEB-T6 — The full action set (P0 unless marked)

| Action | Notes |
| --- | --- |
| `navigate`, `back`, `forward`, `reload` | Navigation |
| `click` | Modifiers (ctrl, shift, alt), button (left, right, middle), click count |
| `doubleClick`, `rightClick` |  |
| `fill`, `clear` | Set a value directly |
| `type` | Keystroke by keystroke, for inputs that react to each key |
| `press` | Keys and combinations: `Enter`, `Control+A`, `Tab` |
| `select` | By value, label or index; multi-select supported |
| `check`, `uncheck` | Checkboxes and radios, idempotent |
| `hover` |  |
| `dragAndDrop` | Source and target locators |
| `scrollIntoView`, `scrollBy` |  |
| `uploadFile` | One or several files |
| `switchToFrame`, `switchToMainFrame` | iframes |
| `switchToWindow`, `closeWindow` | Tabs and popups, by index or title |
| `handleDialog` | Accept or dismiss alerts, confirms and prompts, with optional text. Set **before** the action that triggers it |
| `screenshot` | Full page or a single element |
| `executeScript` | **P1.** JavaScript escape hatch — see the safety section; it is gated |

- [ ] Every action takes a `LocatorRef` where it targets an element.
- [ ] Every action honours the step timeout and reports failures with locator diagnostics plus a screenshot.
- [ ] `handleDialog` is tested for all three dialog types, since an unhandled dialog blocks a browser indefinitely.

✔ `dotnet test --filter "FullyQualifiedName~WebAction"`

Time-box: 6 h. The largest task in this module.

### WEB-T7 — Assertions (P0)

- [ ] `assertText` (equals, contains, matches a pattern), `assertValue`, `assertAttribute`, `assertVisible`, `assertHidden`, `assertEnabled`, `assertDisabled`, `assertChecked`, `assertCount`, `assertUrl`, `assertTitle`.
- [ ] Every failure reports **expected, actual, the locator used, and a screenshot**.
- [ ] Text assertions normalize whitespace by default, with an exact-match option. Unnormalized text comparison is a classic source of false failures.
- [ ] **Soft assertions (P1):** a non-fatal assertion collects its failure and lets the step continue, reporting all failures together. The report model already supports several failures per step.

✔ `dotnet test --filter "FullyQualifiedName~WebAssertion"`

Time-box: 3 h.

### WEB-T8 — Browser and context (P0)

- [ ] Browser engine per flow: Chromium, Firefox, WebKit. Default Chromium.
- [ ] Viewport size, device emulation presets, user agent, locale, timezone, colour scheme.
- [ ] **Storage-state reuse:** a flow can save its logged-in state and later flows can start from it, skipping login. For a 200-flow suite this is the difference between minutes and hours.
- [ ] Storage state is tenant-scoped, stored like any other artifact, and **scanned for secrets** before storage — a session token in a saved state is a credential.
- [ ] Trace and video capture on failure, attached to the report as artifacts.
- [ ] **The SSRF guard applies unchanged.** A flow cannot navigate to a private address in hosted mode, including via redirect. Re-run Day 6's T5 adversarial set against the new action surface — especially `navigate` and any new window-opening path.

✔ `dotnet test --filter "FullyQualifiedName~BrowserContext|FullyQualifiedName~OutboundGuard"`

Time-box: 4 h.

## Safety and escalation

### `executeScript` — the one genuinely dangerous action

It can do anything a page can do, which makes it both the useful escape hatch and the biggest risk in this module.

- [ ] P1, not P0. Ship the module without it first and see whether anyone actually needs it.
- [ ] When built: the script runs in the page context, **never** with access to Yukti's own internals.
- [ ] Any network call it makes still passes through the SSRF guard — verify this specifically, because an in-page `fetch` is an obvious bypass route.
- [ ] Every use is logged with the script text, so an audit can see what ran.
- [ ] A tenant-level setting can disable it entirely, for organizations that don't want an arbitrary-code action available at all.

### Rules Sutradhar must not break

- **Never put a Playwright type in the vocabulary layer.** The Mobile module depends on this holding. CI enforces it.
- **Never add a `sleep` action.** ADR and approval required.
- **Never weaken the SSRF guard** to make an action work. Escalate instead.
- **Never let a fallback succeed silently.** A flagged pass is the whole design.
- **Never break an existing flow.** Inline selectors keep working; repository adoption is opt-in.
- **Never write a second publish validator** for repository references.

### Escalation triggers specific to this module

1. Any task requires changing the SSRF guard, auth, RBAC, RLS, Vault or audit code.
2. The vocabulary layer can't stay engine-neutral without an awkward abstraction — stop and discuss, because the Mobile module's whole plan rests on it.
3. A new dependency is needed.
4. The flake rate rises above 2% after any task.
5. Migration (WEB-T4) would break existing flows.

## Sequence, gate and metrics

| Order | Task | Time-box | Blocks |
| --- | --- | --- | --- |
| 1 | WEB-T1 Shared vocabulary | 3 h | Everything, including Mobile |
| 2 | WEB-T2 Object Repository | 5 h | T3, T4 |
| 3 | WEB-T3 Locator strategies and resolution | 4 h | T4 |
| 4 | WEB-T4 Migrate existing module | 3 h | T5–T8 |
| 5 | WEB-T5 Waits | 3 h |  |
| 6 | WEB-T6 Full action set | 6 h |  |
| 7 | WEB-T7 Assertions | 3 h |  |
| 8 | WEB-T8 Browser and context | 4 h |  |

Roughly **31 hours of P0 work**, about 4 working days.

### The two gates

**Gate 1 — maintenance leverage (after WEB-T4).** Three flows reference one repository entry. Change that entry's primary locator. All three now use the new locator, **with zero flow edits**. Proven by an automated test, not a manual check.

**Gate 2 — suite portability (after WEB-T8).** Port a **real** existing Selenium or Playwright suite of at least 10 tests into Yukti flows, with no capability gaps. Every gap found is recorded; zero gaps means the module is genuinely Selenium-class.

This second gate is the only honest measure of "works like Selenium", and it needs a real suite. An invented one proves nothing, because it will only ever use the capabilities this spec already lists.

```
dotnet test --filter "FullyQualifiedName~Web|FullyQualifiedName~ObjectRepository"
dotnet test --filter "FullyQualifiedName~ModuleParity"     # contract untouched
dotnet test --filter "FullyQualifiedName~OutboundGuard"    # SSRF still holds
yukti run demo/uc1_flow.json                                # unchanged, passes
python scripts/flake_run.py demo/uc1_flow.json -n 50        # under 2%
```

### Metrics

| Metric | Target |
| --- | --- |
| Repository edit updates all referencing flows, zero flow edits | Gate 1 |
| Real 10+ test suite ports with no gaps | Gate 2 |
| Flake rate after full capability | Still under 2% |
| Playwright types in the vocabulary layer | 0 (CI-enforced) |
| Existing flows broken by migration | 0 |
| Fallback usage rate | Tracked, not targeted — a rise means the app is outpacing its locators |

## Open questions and risks

### Open questions

- [ ] **Blocking the ADR — you:** confirm Playwright stays. Everything in this spec assumes it, and the SSRF guard depends on it.
- [ ] **Blocking Gate 2 — you:** is there a real Selenium or Playwright suite available to port? Without one, "Selenium-class" remains an assertion rather than a measured result.
- [ ] **Non-blocking — you:** should `executeScript` be built at all in this pass, or deferred until someone asks? My recommendation is defer.
- [ ] **Non-blocking — Sutradhar:** does the existing artifact storage support a second artifact kind cleanly, or does WEB-T2 need storage work first? Report this before starting T2.
- [ ] **Non-blocking — you:** are Firefox and WebKit actually needed, or is Chromium enough for now? Each extra engine adds CI time and a flake surface.

### Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| The vocabulary layer leaks Playwright concepts | **Medium** | **High** | CI dependency check; the fake-engine test; reviewer rejection. The Mobile module is unbuildable if this fails |
| The Object Repository design is wrong and later tasks inherit it | Medium | High | It's second, with a concrete gate right after, before the capability tasks build on it |
| Flake rate rises as the action surface grows | Medium | Medium | No `sleep`; auto-waiting preserved; 50-run benchmark after every task |
| Migration breaks existing flows | Low | High | Inline selectors keep working; pre-migration flow definitions tested unchanged |
| `executeScript` becomes an SSRF bypass | Low | **Severe** | P1, gated, guard verified specifically against in-page `fetch`, tenant-disableable |
| Scope grows toward matching Selenium's entire API | Medium | Medium | Gate 2 is the measure, not feature count |
