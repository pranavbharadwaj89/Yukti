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

