# Spec — Sarvaghna Token Economy ("spend only when it earns it")

> **How to use:** save this file as `plan/specs/SARVAGHNA-TOKEN-ECONOMY.md` in the Yukti repo, then paste the
> **VS Code prompt** block below into the VS Code agent chat. Everything after the prompt is the spec it follows.

---

## VS Code prompt (paste this)

```text
You are Sutradhar. Follow CLAUDE.md exactly (work loop, branch + PR, never push to main, tick only after ✔ passes).

Read plan/specs/SARVAGHNA-TOKEN-ECONOMY.md before anything else. It is a cross-cutting constraint on every
remaining Day 5 task, not a new day. Apply it as follows:

1. Close D5-T3 first: branch feature/D5-T3-planning-pipeline (if not already), run its ✔ command and the full
   suite, commit, push the branch, open the PR. Before opening the PR, check D5-T3 against spec sections
   R1–R5 and fix any gap inside the same PR. Paste the ✔ output.
2. Then work D5-T4 → D5-T9 in order. For each task, the "Token rules for this task" section of the spec is
   part of that task's definition of done. Add the listed tests to the task's ✔ filter.
3. Do NOT start Phase B (the Node CI agent) until the owner approves DISC-TOKEN-1 in plan/PLAN.md
   `## DISCOVERED`. Add that entry now (wording in spec §Phase B), then ask the owner.
4. Zero live provider calls in any test. Live Gemini calls are allowed ONLY in D5-T8 eval runs and the final
   Day 5 acceptance run, and only after printing the pre-run cost estimate (R9).
5. Measure before you optimise: record the baseline numbers in spec §Baseline into plan/AI-BUDGET.md
   before changing any prompt or limit. If a number can't be measured without a live call, say so — never
   invent it.
6. Ask the owner before: adding any NuGet/npm package (ADR required), changing the $30/wk cap, adding a
   response cache backed by Redis (R12 is P1 — ask first).

Report at the end of each task: what changed, ✔ output, tokens spent (from ai_cost_records or "0 — stub").
```

---

## Problem statement

Sarvaghna calls Gemini in two places, and neither is careful with tokens yet:

- **CI agent** (`sarvaghna/sarvaghna.mjs`) runs a semantic review on **every PR and every push to `main`**
  (so most changes are reviewed twice), sends up to **35,000 chars** of diff including lockfiles/generated files,
  and the **daily digest** runs the full `--crew` (Analyst + Coder + Reviewer, `maxOutputTokens: 8192` each)
  even on days with no commits. Nothing records how many tokens any of this used.
- **Sarvagna (.NET AI layer, D5-T3…T9)** is about to add Analyst + Coder calls with schema retries, a
  10-prompt eval harness run up to 3 prompt versions, and a cost guard that doesn't exist yet.

The week's soft cap is **$30 ($15 for Day 5)** (`plan/AI-BUDGET.md`). Without guardrails, retries, double
CI runs and idle digests burn budget that should go to the eval harness — the only thing that proves P0-7.

## Goals

1. **Every token is accounted for.** 100% of provider calls (.NET and Node, incl. retries and failures) write a
   record with real `usageMetadata` token counts — no estimates in the ledger.
2. **No spend where nothing is learned.** Zero AI calls for: docs/plan-only diffs, push-to-`main` after a reviewed
   PR, no-change digest days, guardrail rejects, and every unit/integration test.
3. **Bounded spend per action.** Each capability has an input-size cap and an output-token cap; a generation
   (Analyst + Coder incl. one retry each) can never exceed its configured ceiling.
4. **≥ 50% fewer tokens per PR inspection** vs. the measured baseline, with no loss of findings on the
   regression set (§Success metrics).
5. **Budget can't be overrun by accident.** The guard refuses a call *before* sending it if the worst-case
   cost would cross the cap.

## Non-goals

- **Switching model/vendor.** Gemini `gemini-3.8-flash` stays (ADR-0004). No "escalate to a bigger model" path.
- **RAG / vector store / embeddings** to shrink context — refused by CLAUDE.md scope guard.
- **Semantic (similarity) caching.** Only exact-match caching, and only as P1.
- **Changing D5 acceptance criteria.** This spec adds constraints; it never relaxes a ✔ command, the
  ≥ 8/10 eval gate, or the "never cut" status of D5-T6 and D5-T8.
- **Prompt quality work for its own sake.** Prompts get shorter only where tests prove output is unchanged.

## User stories

**Owner (pays the bill)**
- As the owner, I want to see tokens and cost per capability for the week so that I know where budget goes.
- As the owner, I want AI calls to be skipped when they can't add anything so that budget is saved for evals.
- As the owner, I want a hard stop before the cap so that I'm never surprised by a bill.

**Developer opening a PR**
- As a developer, I want docs-only PRs to get the rule checks without an AI review so that CI is faster and free.
- As a developer, I want a re-run of the same commit to reuse the earlier review so that I don't pay twice.

**Flow author using generation (D5-T7)**
- As a flow author, I want an empty or oversized request rejected instantly with a clear message, without
  waiting on (or paying for) a model call.
- As a flow author, I want "AI budget reached" to say so plainly while the rest of Yukti keeps working.

---

## Part A — .NET Sarvagna (D5-T3 … D5-T9) · P0 unless marked

### R1 · Zero-cost short-circuits before the provider (D5-T3)
Guardrail rejects never reach `IAiProvider`: empty/whitespace input, input > 4,000 chars, input that is only
redacted secrets, and "no modules registered".
- [ ] Each case returns a typed result with a plain-English reason.
- [ ] Test asserts the stub provider's call count is **0** for every case.
- [ ] A cost record is **not** written for a rejected request (nothing was spent).

### R2 · Minimal context (D5-T3/T4/T5)
- **Analyst** receives a compact registry summary only: module `kind`, action names, one-line description.
  No `ParamSpec` detail, no examples.
- **Coder** receives full `ActionSchema` **only for the candidate modules** in `ParsedIntent`, not the whole registry.
- Context is serialised as minified JSON, stable key order (so hashes and caching work).
- [ ] Test: Coder prompt for a single-module intent contains no other module's schema.
- [ ] Test: Analyst prompt contains no `ParamSpec` fields.
- [ ] Prompt size (chars) for each fixture is asserted ≤ the configured cap (R4) — fails loudly if a change bloats it.

### R3 · Retries that don't resend the world (D5-T3/T5)
- Max **1** schema/validation retry per call (already in the plan — keep it at 1, make it config, not a literal).
- The retry prompt = original prompt + **only** the validation errors (capped at 20 errors / 2,000 chars).
- **No retry** on: `AiUnavailableException` from budget/disabled, HTTP 4xx (except 429), or guardrail rejects.
  Transport retry on 429/5xx/timeout: max 1, with backoff; counts against the same budget.
- [ ] Test: a second validation failure produces `AiGenerationFailedException` and exactly 2 provider calls, never 3.
- [ ] Test: retry prompt contains the error list and is ≤ original length + 2,000 chars.

### R4 · Per-capability caps in config, not code
`Ai:Capabilities:<name>:MaxInputChars`, `MaxOutputTokens`, `Temperature`. Defaults (tunable, not facts):

| Capability | MaxInputChars | MaxOutputTokens | Temperature |
|---|---|---|---|
| Analyst | 6,000 | 1,024 | 0 |
| Coder | 12,000 | 2,048 | 0 |

- [ ] A prompt over `MaxInputChars` after context assembly fails fast (typed failure, 0 provider calls) — it never silently truncates the user's request.
- [ ] No `MaxOutputTokens` literal outside configuration (source-check test, like the existing OpenAI one).

### R5 · Real token accounting on every call (D5-T3 hook, D5-T9 table)
- `GeminiProvider.cs` already reads `usageMetadata` (≈ line 153) — make sure `InputTokens` / `OutputTokens`
  reach `ProviderResponse` and the cost record, including on failed/retried attempts.
- Every call — success, schema failure, retry, timeout — writes one `ai_cost_records` row: capability, provider,
  model, prompt version, input/output tokens, cost, tenant, `attempt` (1 or 2), outcome.
- Cost = tokens × price table from config (`Ai:Pricing:<model>:InputPer1M/OutputPer1M`), not hardcoded.
- [ ] Test: a run with one retry writes **2** rows with `attempt` 1 and 2.
- [ ] Test: the stub provider reports deterministic token counts so cost math is asserted exactly.

### R6 · Budget guard checks worst case *before* calling (D5-T9)
- Before each call: `projected = estimatedInputTokens (chars ÷ 4) + MaxOutputTokens`, priced at the model's rates.
- If `spent + projected > cap` → 503 RFC 7807 "AI budget reached", **no provider call**.
- 80% → warning log (structured template) + `GET /api/v1/ai/usage` shows `warning: true`.
- Cap is read from config; default matches `plan/AI-BUDGET.md` ($30/week).
- [ ] Test: at 99.9% spent, a call whose projection crosses the cap is refused with 0 provider calls.
- [ ] Test: AI-off still returns the "AI disabled" 503, not "budget reached" (switch wins over budget).

### R7 · Tests never spend (all D5 tasks)
- Unit/integration tests use `StubAiProvider` + recorded fixtures only.
- [ ] A test-assembly guard fails the run if `GeminiProvider` is resolved in any test without an explicit `[Trait("Live","true")]`.
- [ ] CI never sets `Live=true`.

### R8 · Confidence & attribution carry the cost context (D5-T6 — never cut)
- `AiAttribution` keeps model + prompt version (already required). Add nothing that costs tokens:
  confidence is computed from validation results/retry count, **never** by asking the model "how confident are you".
- [ ] Test: computing confidence makes 0 provider calls.

### R9 · Eval harness spends deliberately (D5-T8 — never cut)
- `eval flow-generation --dry-run` prints prompt count × projected tokens × price and exits without calling.
- A real run prints the same estimate first and refuses if it would cross the remaining budget.
- **Early stop:** once failures make ≥ 8/10 impossible (3 structural failures), stop that version's run and report.
- Results are stored per prompt-version + timestamp (as planned); re-running an **unchanged** prompt version
  with an unchanged registry hash reuses stored results unless `--force`.
- Max 3 prompt versions (as planned), then escalate to the owner.
- [ ] Test (stub): early stop triggers after the 3rd structural failure and makes no further calls.
- [ ] Test: unchanged version + registry hash → 0 provider calls without `--force`.

### R10 · Usage endpoint the owner can read (D5-T9)
`GET /api/v1/ai/usage` (admin): week-to-date tokens and cost by capability, % of cap, retry share, refusals by reason.
- [ ] Test: non-admin → 403; admin sees per-capability totals matching seeded `ai_cost_records`.

### R11 · Generation UI doesn't double-submit (D5-T7)
- "Suggest a flow" is disabled while a request is in flight; identical text re-submitted within 60 s reuses the
  last Draft instead of calling again (client-side, no server cache needed).
- Client-side guardrails (empty, > 4,000 chars) block before the request is sent.
- [ ] `npm test -- AiGenerationPanel`: double-click sends 1 request; empty input sends 0.

### R12 · Exact-match response cache — **P1, ask owner first**
Key = hash(capability, prompt version, model, normalised input, registry hash, tenant). Redis, TTL 24 h.
Cache hits are recorded in `ai_cost_records` with 0 tokens and `outcome = cache_hit`. Never shared across tenants.

### Token rules per remaining task (definition-of-done add-ons)

| Task | Must also satisfy | Add to ✔ filter |
|---|---|---|
| D5-T3 (close-out) | R1, R2 (assembly), R3, R4, R5 hook | `~Planning` (already) |
| D5-T4 Analyst | R2 (Analyst), R4 | `~Analyst` |
| D5-T5 Coder | R2 (Coder), R3, R4 | `~Coder` |
| D5-T6 Confidence/Attribution | R8 | `~Confidence\|~Attribution` |
| D5-T7 Generation UI | R11 | `npm test -- AiGenerationPanel` |
| D5-T8 Eval harness | R9 | `eval flow-generation --dry-run` |
| D5-T9 Cost + budget | R5 (table), R6, R7, R10 | `~AiCost\|~Budget` |

---

## Part B — Node CI agent (`sarvaghna/`) · needs owner approval

Add to `plan/PLAN.md` under `## DISCOVERED` (do not start until approved):

```
- [ ] DISC-TOKEN-1 Sarvaghna CI token economy: skip AI on docs/plan-only diffs and on push-to-main,
  skip idle digests, trim diff context, cap output, reuse review per head SHA, log usageMetadata
  to sarvaghna/ledger/. ✔ `npm test --prefix sarvaghna` (new `test` script using built-in `node --test`,
  no new npm package) + a docs-only test PR shows "AI review skipped" and 0 Gemini calls in the ledger.
```

Notes: `sarvaghna/package.json` has no `test` script today and `sarvaghna/ledger/` doesn't exist yet
(it's only listed in `ignoredPaths`) — create both. In CI the ledger is per-run; B5 needs `actions/cache`
keyed by head SHA to survive between runs.

| ID | Change | Where | Acceptance |
|---|---|---|---|
| B1 | **Skip AI** when every changed path matches `docs/**`, `plan/**`, `*.md`, `**/*.html` (rules still run) | `sarvaghna.mjs` before `runSemanticReview` | Docs-only PR → comment says "AI review skipped (docs-only)", ledger shows 0 calls |
| B2 | **No AI on push to `main`** — the PR was already reviewed; run rules only | new `--no-ai` flag in `sarvaghna.mjs`; `sarvaghna.yml` passes it when `github.event_name == 'push'` | Merge commit run makes 0 Gemini calls |
| B3 | **Trim the diff**: drop lockfiles, generated API types, `bin/ obj/ dist/`, binary; order files by rigor tier (Domain/Orchestration → Api/Application → tests → docs); cap **12,000 chars** (was 35,000) | `semantic-reviewer.mjs` | Unit test: lockfile hunks never appear in the prompt; prompt ≤ cap |
| B4 | **Output cap**: semantic review 2,048, crew agents 2,048 (was 4,096 / 8,192) | `sarvaghna.config.json`, agents | Config-driven; no literal in agent files |
| B5 | **Reuse per head SHA**: ledger entry keyed by `headSha + configHash + GOALS.md hash`; same key → reuse result | `sarvaghna/ledger/` + `actions/cache` in `sarvaghna.yml` | Re-running a workflow on the same SHA → 0 calls |
| B6 | **Idle digest**: if no commits on `main` since the last digest, send the non-AI digest only | `sarvaghna-digest.yml` / `generate_report.mjs` | No-change day → 0 calls, email still sent |
| B7 | **Ledger** every call's `usageMetadata` (prompt/candidates/total tokens), model, trigger, SHA | `core/gemini-client.mjs` returns `usage`; callers append to ledger | Ledger lines present for every call; report shows weekly totals |
| B8 | `concurrency: cancel-in-progress` per PR, so superseded pushes don't finish an AI call | `sarvaghna.yml` | Two quick pushes → only the latest run reaches the AI step |
| B9 | `GOALS.md` sent as a compact digest (headings + active goals), not the full file | `semantic-reviewer.mjs` | Prompt contains goals ≤ 1,500 chars |

**Regression set (proves no loss of findings):** pick 5 past PRs with known Sarvaghna findings; before/after
each change, the same CRITICAL/HIGH findings must still appear. Store in `sarvaghna/tests/fixtures/`.

---

## Baseline (measure first, write into `plan/AI-BUDGET.md`)

| Metric | How | Baseline | Target |
|---|---|---|---|
| Tokens per PR inspection (avg of last N runs) | B7 ledger / Gemini console | _measure_ | ≤ 50% of baseline |
| AI calls per merged PR (PR + push) | workflow runs × calls | _measure_ (expect 2) | 1 |
| Digest tokens on a no-change day | ledger | _measure_ | 0 |
| Prompt chars per Analyst / Coder fixture | test output | _measure_ | ≤ R4 caps |
| Live calls in `dotnet test` | R7 guard | must be 0 | 0 |

If a baseline needs a live call to measure, note "not measured — needs live call" and ask the owner.

## Success metrics

**Leading (this week)**
- 100% of provider calls have a cost/ledger record with real token counts.
- 0 live calls in any CI test job; 0 AI calls on docs-only PRs and push-to-`main` runs.
- Generation p95 total tokens (Analyst + Coder incl. retries) ≤ the R4-derived ceiling.
- Eval harness reaches ≥ 8/10 within the Day 5 $15 share.

**Lagging (next 2–4 weeks)**
- Weekly Sarvaghna spend ≤ $30 cap with no budget-reached refusals during normal work.
- Retry share < 20% of Coder calls (a higher rate means the prompt, not the budget, needs work).
- Same CRITICAL/HIGH findings on the regression set as before trimming.

## Open questions

| # | Question | Who | Blocking? |
|---|---|---|---|
| Q1 | Approve DISC-TOKEN-1 (Part B) this week, or park it for v0.2? | Owner | Blocks Part B only |
| Q2 | Approve R12 Redis cache now, or leave as P1? | Owner | No |
| Q3 | Are the R4 default caps acceptable, or should D5-T8 eval results set them? | Owner + Sutradhar after T8 | No |
| Q4 | Gemini price per 1M tokens for `gemini-3.8-flash` for `Ai:Pricing` — confirm from the billing console | Owner | Blocks D5-T9 cost accuracy |
| Q5 | Should the 80% warning also email via the existing `send_email.py` path? | Owner | No |

## Timeline & order

1. **Now:** close D5-T3 PR (with R1–R5 gaps fixed). Add DISC-TOKEN-1. Record baseline.
2. **D5-T4 → D5-T6:** apply R2, R3, R4, R8 as part of each task.
3. **D5-T9 before the first live eval run** — the guard (R6) must exist before D5-T8 spends real money.
   (Plan order is T7 → T8 → T9; build R5/R6 first or run T8 only in `--dry-run` until T9 lands.)
4. **D5-T7, D5-T8** with R11, R9.
5. **Part B** after owner approval, in order B2 → B1 → B7 → B3 → B4 → B6 → B5 → B8 → B9 (cheapest wins first).

**Slip policy:** if time runs short, cut R12, R11's 60-s reuse, B5, B8, B9 — in that order. Never cut R1, R5,
R6, R7, R9 (they protect the budget and the never-cut tasks).
