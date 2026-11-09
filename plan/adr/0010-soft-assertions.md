# ADR-0010: Soft assertions — a per-step `Soft` flag, not a per-module param

Status: Accepted
Date: 2026-11-09
Owner approval: **approved 2026-11-09**, directly in conversation, after review (PR #98;
Sarvaghna's own two-pass review on the PR raised one non-blocking note — soft vs. catastrophic
failures — addressed in-ADR before approval). Raised from DISC-26 (`plan/PLAN.md`), discovered
while closing MOB-T7 (Mobile Testing Module Spec).

## Context

The Api, Web, and Mobile module specs each list "soft assertions" as a checklist item, every one
phrased the same way — Mobile's own line: *"Soft assertions (P1), consistent with the other
modules."* That phrasing assumes at least one other module already has them. It doesn't:

```
grep -rli "softassert" --include="*.cs" src/ tests/   →  zero hits, anywhere
```

No module, no engine-level concept, nothing. This was discovered, not assumed, while closing
MOB-T7 and confirmed by direct search before writing this ADR.

"Soft assertion" is standard test-automation terminology (TestNG, JUnit's `SoftAssertions`,
Playwright's `expect.soft`): a failed assertion is recorded, but execution continues instead of
aborting immediately. Today, Yukti's only continuation control is `Flow.ContinueOnFailure`
(`src/Yukti.Domain/FlowAuthoring/Flow.cs:42`) — a **flow-level** switch checked once in
`FlowEngine`'s dispatch loop:

```csharp
// src/Yukti.Orchestration/FlowEngine.cs, ~line 442
if (result.Status == StepStatus.Failed && !flow.ContinueOnFailure)
{
    _logger.LogWarning(...);
    break; // fail-fast default (Product Philosophy 4.2)
}
```

Setting `ContinueOnFailure` on the whole flow to get past one expected-to-sometimes-fail
assertion step means every *other* failure in that flow run also stops being fatal — too coarse
for what "soft assertion" actually means in every tool that has it: a property of **one
assertion**, not the whole run.

## Decision

Add a per-step `Soft` flag to `FlowStep`, at the same tier as the existing `SaveAs` and
`WhenCondition` fields — orchestration metadata the engine reads directly, not a module
`ActionSchema` param. A soft-failed step still reports `StepStatus.Failed` (never hidden, per
CLAUDE.md principle 6 — "False `when` = Skipped, never omitted" applies here too: a soft
assertion failure must never be silently dropped from the result). The only behavioral change is
that `FlowEngine` does not stop the run because of it:

```csharp
if (result.Status == StepStatus.Failed && !flow.ContinueOnFailure && !step.Soft)
    break;
```

`FlowRun.Status`'s own failure computation needs **no change** —
`_results.Any(r => r.Status == StepStatus.Failed)` already treats a soft-failed step's `Failed`
status as making the whole run `Failed`. Soft only controls whether execution continues *past*
that step; it never hides that the step failed.

`StepResult` gets the same flag, denormalized, for the same reason `StepName`/`Module`/`Action`
already are: *"a report always reflects exactly what ran"* even if the Draft's step is edited
later. Without it, a report showing steps running normally *after* a `Failed` one looks like a
reporting bug, not an intentional soft assertion.

**`Soft` does not distinguish an assertion mismatch from a catastrophic failure (a crashed
session, a dead driver).** Sarvaghna's own review of this ADR raised exactly this: should a
soft step's failure still be treated as soft when the underlying cause is infrastructure, not
"expected got wrong"? Deliberately yes, not an oversight — `RetryFlakeHandler` and the module's
own `Setup`/`Teardown` guarantees already run unconditionally regardless of `Soft`, so a dead
session surfaces as fast, uniform failures on every subsequent step rather than a hang; `Soft`
only ever means "don't stop because of this one step," never "retry harder" or "assume the
session is still healthy." The cost is a noisier report (several fast failures instead of one),
never a correctness or safety issue, and `RunStatus.Failed` is reached exactly the same either
way. Revisit only if a real case surfaces where this noise materially hides the actual cause.

## Why this shape, not another

**A first-class `FlowStep` field, not a module `ActionSchema` param.** If "soft" were a param
every assertion action declared in its own schema, `FlowEngine` would need to read it out of
`step.Params` by a conventional key name — which means the engine's own behavior depends on every
module remembering to name a param `"soft"` and expose it identically. That's exactly the
module-specific coupling Architecture Principle 20.3 forbids, just moved into param-naming
convention instead of an `if (kind == ...)` branch. A `FlowStep`-level field needs zero
module cooperation: any step, from any module, can be marked soft, the same way any step can
already have a `WhenCondition`.

**Not restricted to assertion-named actions.** Restricting `Soft` to steps whose `Action` starts
with `"assert"` (or some other naming convention) would require `FlowEngine` to pattern-match
action names — again exactly the module-specific branching the architecture forbids, and it would
silently stop working for a module that names an assertion something else. The mechanism stays
generic; the UI and documentation guide authors toward assertion-shaped steps, but nothing in the
engine enforces that restriction.

**`StepStatus.Failed` stays `Failed` — no new `SoftFailed` enum value.** `StepStatus` today is
deliberately small (`Passed, Failed, Skipped`); flakiness and locator-fallback are already
modeled as derived boolean signals alongside `Status` (`StepResult.IsFlaky`,
`UsedLocatorFallback`), not new `Status` values. A `SoftFailed` enum value would duplicate that
pattern unnecessarily, and — more seriously — would require `FlowRun`'s failure computation
(`_results.Any(r => r.Status == StepStatus.Failed)`) to be updated to also check for
`SoftFailed`, or a soft-failed run would silently report `Passed`. Keeping `Status = Failed` plus
a separate `Soft` boolean means the existing failure computation is correct by construction, with
nothing new to remember.

## Consequences

- EF migration: a new `Soft` column on the `flow_steps` table (default `false`), and the
  equivalent on whatever table backs `StepResult`.
- `Flow.AddStep` (and any `UpdateStep`) gains an optional `soft` parameter.
- **`Flow.CreateNewVersion` must copy `Soft` forward explicitly.** This aggregate already has one
  open, unresolved gap in this exact area — DISC-24 (`plan/PLAN.md`) found that
  `ContinueOnFailure` itself is *not* copied forward when a new Draft version is created from a
  Published flow, silently resetting to its default. `Soft` must not repeat that mistake: whatever
  fix DISC-24 lands on (copy forward, or confirm "a new version starts fresh" is intentional) has
  to explicitly include `Soft`, not be an independent decision made twice.
- API DTOs for step create/update, and the generated OpenAPI schema (`npm run generate:api`-style
  regen the frontend already depends on) need the new field.
- The CLI's flow-file JSON schema needs a `soft` field per step, and `FlowLoader` needs to read it.
- Frontend step editor: a checkbox alongside the existing `when`/`saveAs` controls — not a
  per-module schema change, so a newly-registered module gets this for free (Product Principle 2,
  schema-driven UI, holds).
- Report UI: a failed-but-soft step should visually read as "failed, but didn't stop the run" —
  without this, a run that keeps going past a `Failed` step looks like a bug to anyone reading the
  report, not an intentional feature.
- Every module's own spec checklist line ("soft assertions, consistent with the other modules")
  becomes honestly tickable at once, by one engine-level change, instead of three separate,
  inconsistent, module-local reinventions.

## Alternatives considered

| Option | Complexity | Consistency with Architecture Principle 20.3 | Verdict |
|---|---|---|---|
| **A: `FlowStep.Soft` (this decision)** | Low — one field, one condition, no new enum value | Full — no module needs to know this exists | Chosen |
| **B: Per-module `ActionSchema` "soft" param** | Medium — every module must independently add and expose it identically | Violates — engine behavior keyed off a param-name convention | Rejected |
| **C: Restrict to actions named `assert*`** | Low to add, but brittle | Violates — engine pattern-matches action names | Rejected |
| **D: New `StepStatus.SoftFailed` enum value** | Medium — `FlowRun`'s failure computation must also be updated, or a soft-failed run silently reports `Passed` | Neutral on 20.3, but breaks the existing flaky/fallback-as-derived-signal pattern | Rejected |

**B** was the closest runner-up — it's what each spec's own phrasing ("consistent with the other
modules") implicitly assumed would already exist, module by module. Rejected specifically
*because* it would need building three times (Api, Web, Mobile), independently, with no guarantee
the three implementations stay behaviorally identical — the opposite of what "consistent with the
other modules" is actually asking for.

## Revisit if

- A real need emerges for *partial* soft behavior within a single step (e.g., an API step's own
  `assert` array, MOB-T7's sibling concept in `ApiModule`, where `AssertionEvaluator` already
  collects every assertion in one step's array without failing fast *within* that step). That's a
  different, already-solved problem (one step, many assertions, already non-fail-fast inside
  itself) and this ADR's `Soft` flag is orthogonal to it, not a replacement — worth confirming
  explicitly if `ApiModule`'s own array-level behavior should also be called "soft assertions" in
  user-facing documentation, or kept named separately to avoid conflating the two.
- If the frontend step editor turns out to have no single, consistent place to add a generic
  step-level control (unlikely, since `when`/`saveAs` already have one) — would need a different
  UI approach, not a different domain model.
