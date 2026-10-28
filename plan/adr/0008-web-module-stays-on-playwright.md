# ADR-0008: Web module stays on Playwright, not Selenium WebDriver

Status: Accepted
Date: 2026-10-29
Owner approval: the Web UI Testing Module Spec itself states this decision is "locked, do not
revisit" and frames it as already made; recorded here per the spec's own instruction ("Record
this as ADR-00NN before starting") to make it a durable, numbered decision rather than only a
paragraph in a spec doc. Consistent with — not a new decision on top of — `CLAUDE.md` §4's
existing tech-stack line ("Web automation: `Microsoft.Playwright` (.NET)") and ADR-0001 (stay on
.NET).

## Context

The Web UI Testing Module Spec ("Selenium-class capability on Playwright," Oct 27 2026) asks the
Web module to reach Selenium's capability set: locator strategies, implicit/explicit waits, a
rich action set, and a Page Object Model equivalent (the Object Repository). The existing
`WebModule` (`src/Yukti.Infrastructure.InMemory/Modules/WebModule.cs`) already runs on
`Microsoft.Playwright`, including a working SSRF guard built on Playwright's CDP-level route
interception (D6-T5) — empirically the only approach found that reliably intercepts loopback
requests, since Chromium's native `--proxy-server`/bypass-list did not.

## Decision

The Web module stays on Playwright. "Selenium-class" means Selenium's capability set, not the
Selenium library or WebDriver protocol. No rewrite onto Selenium WebDriver.

## Why

- Playwright already provides every capability this spec asks for (locators, explicit waits,
  the full action set) with auto-waiting that produces materially lower flake rates than
  WebDriver's own polling model.
- The existing SSRF guard (D6-T5) is built directly on Playwright's `IBrowserContext.RouteAsync`
  CDP-level interception. Moving to WebDriver would require re-solving that guard from zero, with
  no guarantee an equivalent interception point exists in WebDriver at all.
- Rebuilding the existing, working, tested `WebModule` on a different engine would cost weeks for
  no capability gain and would directly raise flake rates — the opposite of this spec's own goal.

## Consequences

- Every Web UI Testing Module task (WEB-T1–T8) is additive on top of the existing Playwright-based
  `WebModule`: new actions, parameters, and assertions on the existing module, plus one new
  engine-neutral shared layer (`Yukti.Domain.UiVocabulary`, WEB-T1) and one new artifact kind (the
  Object Repository, WEB-T2). None of it changes `IAutomationModule`, the engine, the registry,
  the schema-driven UI, the report model, or the CLI.
- The shared vocabulary layer (WEB-T1) must stay engine-neutral regardless of this decision — it
  is reused as-is by the (not-yet-built) Mobile module against Appium, a different engine
  entirely. Staying on Playwright for Web does not change that requirement.

## Alternatives considered

- **Move to Selenium WebDriver.** Rejected: throws away a working, SSRF-guarded implementation
  for a library change with no new capability, and WebDriver's lack of a known CDP-equivalent
  interception point puts the existing SSRF guarantee at risk.
- **Support both engines behind an abstraction.** Not considered seriously — no requirement in
  this spec or any other calls for running the same flow against two different browser engines,
  and building that abstraction now would be exactly the kind of premature generalization the
  project avoids elsewhere.

## Revisit if

- A real requirement emerges for a capability Playwright cannot provide and WebDriver can (none
  identified in this spec).
- The SSRF guard's CDP-interception approach is found to have a bypass that only an engine change
  could fix (none found; the one documented residual gap — a true DNS-rebinding race between this
  guard's check and Chromium's own subsequent resolution — is not engine-specific and would not be
  closed by moving to WebDriver either).
