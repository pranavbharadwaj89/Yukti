# SARVAGHNA (सर्वज्ञ) — Autonomous Agent Specification

```
                          ┌───────────────────────────┐
                          │   SARVAGHNA (सर्वज्ञ)     │
                          │   Autonomous Git Agent    │
                          │   for @pranavbharadwaj89  │
                          └─────────────┬─────────────┘
                                        │
           ┌────────────────────────────┼────────────────────────────┐
           │                            │                            │
 ┌─────────▼──────────┐       ┌─────────▼──────────┐       ┌─────────▼──────────┐
 │ Local Git Faculty  │       │  Knowledge & Spec  │       │  GitHub CI Faculty │
 │ pre-commit/pre-push│       │  Volumes 0 - 5     │       │  Action & Reviewer │
 │ native interception│       │  GOALS.md Ingestion│       │  PR Intelligence   │
 └────────────────────┘       └────────────────────┘       └────────────────────┘
```

## 1. Identity & Persona

* **Name:** SARVAGHNA (सर्वज्ञ — *The Omniscient One*)
* **Role:** Autonomous Quality, Security & Architecture Guardian
* **Affiliation:** Dedicated Agent for [`pranavbharadwaj89`](https://github.com/pranavbharadwaj89)
* **Tone:** Intelligent, discerning, high-signal, pragmatic, and constructive.

---

## 2. Core Sensibility Manifesto

A dumb linter blindly counts lines and flags words. **SARVAGHNA is an agent with sensibility:**

1. **Context-Aware Semantic Perception**:
   SARVAGHNA reads commit messages, PR descriptions, and the active goals in [`GOALS.md`](./GOALS.md) before analyzing code. It evaluates code in the context of what the developer is trying to achieve.

2. **Differentiated Rigor**:
   * **Production Core (`src/Yukti.Domain`, `src/Yukti.Orchestration`)**: Maximum architectural rigor. Zero tolerance for unhandled exceptions or layer pollution.
   * **Application & API (`src/Yukti.Api`, `src/Yukti.Application`)**: Rigor on validation, DTO boundaries, and cancellation tokens.
   * **Tests (`tests/`)**: Lenient on mocking and setup brevity; strict on assertion clarity.
   * **Documentation (`docs/`)**: Informational; recognizes placeholders (`...`, `<token>`) without false alarms.

3. **Autonomous Evolution via Rules & Skills**:
   SARVAGHNA’s intelligence is not hardcoded. Whenever Pranav adds:
   * A rule in `tools/sarvaghna/rules/custom/`
   * A skill in `tools/sarvaghna/skills/`
   * An objective in `tools/sarvaghna/GOALS.md`
   SARVAGHNA dynamically absorbs it into its reasoning loop on the very next run.

---

## 3. The 5-Agent Specialist Crew

Inside SARVAGHNA sits a specialized crew of 5 autonomous agents collaborating to analyze specifications, generate tests, execute suites, and score quality:

```
                            ┌───────────────────────────────────┐
                            │      ORCHESTRATOR (#9B8FE0)       │
                            │   "The boss — routes & decides"   │
                            └─────────────────┬─────────────────┘
                                              │
               ┌──────────────────────────────┼──────────────────────────────┐
               │                              │                              │
     ┌─────────▼─────────┐          ┌─────────▼─────────┐          ┌─────────▼─────────┐
     │  ANALYST (#1D9E75)│          │   CODER (#378ADD) │          │  TESTER (#EF9F27) │
     │  Reads docs and   │          │ Writes and runs   │          │ Runs tests and    │
     │  extracts meaning │          │ real code         │          │ reports results   │
     └───────────────────┘          └───────────────────┘          └─────────┬─────────┘
                                                                             │
                                                                   ┌─────────▼─────────┐
                                                                   │ REVIEWER (#D85A30)│
                                                                   │ Scores quality &  │
                                                                   │ gives feedback    │
                                                                   └───────────────────┘
```

| Agent | Color | Tagline | Capabilities |
| :--- | :--- | :--- | :--- |
| **Orchestrator** | `#9B8FE0` | *The boss — routes and decides* | Decomposes high-level goals into subtasks, routes tasks to specialists, coordinates retry & error recovery loops. |
| **Analyst** | `#1D9E75` | *Reads docs and extracts meaning* | Ingests PRDs, Jira tickets, and architecture specs (`New Text Document.txt`), extracts testable acceptance criteria, flags ambiguities. |
| **Coder** | `#378ADD` | *Writes and runs real code* | Synthesizes Playwright / API / .NET test code, generates resilient selectors (`data-testid`, `aria-label`), auto-fixes syntax errors. |
| **Tester** | `#EF9F27` | *Runs tests and reports results* | Triggers `dotnet test` / Playwright / vitest, parses test telemetry, writes plain-English failure summaries, diagnoses flakiness. |
| **Reviewer** | `#D85A30` | *Scores quality and gives feedback* | Scores test coverage against requirements, audits zero-dependency domain constraints, detects secrets, delivers actionable code reviews. |

Run the full crew anytime:
```bash
node sarvaghna/sarvaghna.mjs --crew
```

## 3a. The Git Agent — merge-triggered, not part of the crew above

`sarvaghna/agents/git-agent.mjs` is a separate automation, deliberately **not** registered in the crew roster above and with **no CLI flag, no npm script, and no manual dispatch trigger**. Per the owner's own instruction: *"it will only have to be active whenever there is a code merged."*

It drives a real Chromium browser (via `apps/yukti-gui`'s own `@playwright/test`) against the real running frontend: signs in as the seeded admin, visits every route `src/app/router.tsx` serves, runs a real-browser WCAG 2.1 A/AA scan (`axe-core` — the same tag set `e2e/ai-generation.spec.ts` already uses) plus HTTP-status and console-error checks, and screenshots every page as evidence. It reports through Sarvaghna's own existing finding/decision/formatter pipeline.

The only thing that ever invokes it is `sarvaghna/run-git-agent.mjs`, and the only thing that calls *that* is the `git-agent-ui-audit` job in `.github/workflows/sarvaghna.yml`, gated `if: github.event_name == 'push'` — i.e. it runs if and only if a merge lands on `main`. There is intentionally no other way to trigger it.

**Sarvaghna has sole authority over UI-audit-on-merge.** A separate, standalone pipeline for this (a Python trigger agent + bash entrypoint + its own GitHub Actions workflow, handed over as a template named "Brevitas") was proposed and partially built, then deliberately retired — its PR (#86) was closed without merging, per the owner's own decision: *"keep the latest and make it as a GIT agent in Sarvaghna... Sarvaghna has complete authority towards it and the decision."* Nothing from that template runs in this repo; only the part of it worth keeping was ported in, below.

**Diff-aware triggering**, ported from that template's `decide.py`/`context.py` (`sarvaghna/agents/git-agent-decide.mjs`, called from `run-git-agent.mjs` before any browser launches):
- `run-git-agent.mjs` diffs `github.event.before`..`HEAD` (passed through as `GITHUB_EVENT_BEFORE`; the job's checkout uses `fetch-depth: 0` so that diff is actually available) to get the files this merge touched.
- A merge that touches nothing under `apps/yukti-gui/` skips the audit entirely (`SkipReason.NO_UI_CHANGES`) — a backend-only or docs-only merge can't have changed the rendered UI, so there's nothing to re-confirm.
- Otherwise, changed files are mapped onto the specific routes they could affect (`ROUTE_TRIGGERS`, keyed off `src/app/router.tsx`'s own imports) and only those routes are audited — not always all 14. A file that matches no route's trigger, or a change to a shared file (layout, login page, the router itself, global store, generated API types), audits every route rather than guessing a narrower blast radius; an unavailable diff (shallow clone, first commit) does the same, and is never read as "nothing changed".
- What wasn't ported from the template, and why: its upstream deploy/regression-status gating has no equivalent here (this job builds and serves the frontend itself; there's no separate deploy stage to consult), and its baseline-gating has no equivalent either (the Git Agent has no screenshot/finding baseline to diff against — axe-core's own impact tiering is already its escalation path).

15 tests in `sarvaghna/agents/git-agent-decide.test.mjs` cover this (skip/audit boundaries, route selection, shared-file and unmapped-file fallback, force, null/empty diff).

