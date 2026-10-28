# Open questions — across every Module Depth implementation

Living doc, not a log — updated in place as items resolve, new modules add their own. One place
to review every outstanding decision across the current specs instead of being asked piecemeal.
Each item names where it came from and what happens if you say nothing.

Legend:
- 🔴 **Blocking** — real work is stalled or will ship incomplete until you answer.
- 🟡 **Defaulted** — I picked a reasonable default and I'm proceeding on it. Says what to tell me if you want the other answer.
- 🔵 **Sutradhar's own** — mine to investigate/decide, listed here only for visibility, not asking you anything.

---

## API Testing Module (`plan/specs/API-TESTING-MODULE.md`)

Module gate: **green** (PR #73). P0-complete. Everything below is P1 polish.

1. 🔴 **Postman import test is blocked on a real collection.** Can't produce the gap report your
   work order asked for until you share one. *Needs: a Postman Collection v2.1 export.*
2. 🟡 **Soft-assertion evaluation order.** Your spec's test (b) — "mixed hard and soft, hard
   fails first → evaluation stops there" — implies hard-assertion failure should stop evaluation.
   The current `AssertionEvaluator` never fails fast; it always evaluates every assertion in the
   array and collects every result (the existing, already-tested T7 design). Building test (b)
   literally means changing that for hard assertions while keeping soft ones non-stopping — a
   real behavior change, not just a new flag. *Default if you don't weigh in: I'll build it as a
   genuine behavior change (hard assertions stop evaluation at the first hard failure; soft ones
   never do), since that's what your spec's test explicitly asks for — but flagging it here since
   it changes existing, tested behavior rather than purely adding something new.*
3. 🔵 DISC-20 (Request Templates frontend page) — known, scoped, not yet built. Not a question,
   just listed here so it's visible alongside everything else rather than only in `PLAN.md`.

---

## Web UI Testing Module (`plan/specs/WEB-TESTING-MODULE.md`)

WEB-T1 (shared vocabulary), WEB-T2 (Object Repository), WEB-T3 (locator resolution), WEB-T4
(migrate the existing module) closed 2026-10-29. WEB-T5 (waits) next.

1. 🟡 **"Confirm Playwright stays" (the spec's own blocking question).** Treating this as already
   settled, not re-asking: `CLAUDE.md` §4 already names `Microsoft.Playwright (.NET)` as the web
   automation stack, and ADR-0001 already decided "stay on .NET" generally. Recorded formally as
   `ADR-0008` rather than escalating back to you. *Tell me if you actually want WebDriver
   reconsidered — I'll treat silence as confirmation.*
2. 🔴 **Gate 2 needs a real Selenium or Playwright suite (10+ tests) to port.** This is the only
   honest measure of "Selenium-class" per the spec's own words — an invented suite proves nothing.
   Not needed until WEB-T8 is done (roughly 4 tasks away), but the earlier you can point me at one
   (yours, open-source, anything real), the less likely this stalls the module right at the finish
   line. *Needs: a real test suite to port, or a decision to skip/weaken Gate 2.*
3. 🟡 **`executeScript` (WEB-T6) — build now or defer?** Defaulting to the spec's own
   recommendation: **defer**. It's marked P1, it's the one genuinely dangerous action (arbitrary
   JS in the page context, a possible SSRF bypass via in-page `fetch`), and the spec explicitly
   says "ship the module without it first and see whether anyone actually needs it." *Tell me if
   you want it built in this pass anyway.*
4. 🟡 **Firefox and WebKit (WEB-T8) — actually needed, or Chromium enough for now?** Defaulting to
   building the config surface so a flow *can* request any of the three, but only exercising
   Chromium in CI/mutation checks for now — each extra engine run in CI is real time and a real
   flake surface, and nothing today asks for cross-browser coverage. *Tell me if Firefox/WebKit
   need real CI coverage now, not just a config option.*
5. 🔵 **Resolved, not asked:** the existing artifact-storage machinery reuses cleanly for a
   second artifact kind — `ObjectRepository` uses the exact same EF patterns (`OwnsMany`/jsonb,
   `FamilyId`+`Version`+`Status`+`TenantId`) `Flow` already does. No storage work was needed
   first. Built as WEB-T2, 2026-10-29.
6. 🟡 **`RepositoryRef` has no `RepositoryId`.** The spec's own domain model defines
   `RepositoryRef(Page, Element)` — a step names "Checkout › submitButton," never which Object
   Repository artifact that page lives in. *Default, already built:* resolution and the
   existence check both search every repository the tenant owns for a matching page/element,
   succeeding on the first match. This matters once a tenant has more than one repository with
   overlapping page names — ambiguous today by design, not by oversight. *Tell me if you'd
   rather `RepositoryRef` carry an explicit `RepositoryId` (a real, if small, deviation from the
   spec's own listed domain model) — I haven't done that without checking first.*
7. 🔵 DISC-22/DISC-23 (`plan/PLAN.md`) — two real, pre-existing gaps found while building WEB-T2:
   `RequestTemplate` has no DB-level RLS at all (app-level `TenantId` filter only), and API-T9's
   `IRequestTemplateExistsChecker` was never actually wired into the real publish command handler
   despite being built and tested. The second is already fixed (same change that wired WEB-T2's
   own checker in). The first is a real security-posture question for you, not something I fixed
   silently on a different module's table — see DISC-22 for the two ways to close it.
8. 🟡 **`Text` strategy has no exact/partial flag.** WEB-T3's own checklist asks for "Text (exact
   and partial)," but `LocatorSpec` (WEB-T1, already shipped) has no such flag — only
   `Strategy`/`Value`/`Name`. *Default, already built:* `Text` always uses Playwright's own
   default (case-insensitive substring/partial match); exact-text matching isn't independently
   selectable. *Tell me if you want `LocatorSpec` to carry an `Exact: bool?` (a small addition to
   an already-shipped WEB-T1 type) rather than leaving `Text` partial-only.*
9. 🔵 **Not executed, flagged:** WEB-T4's 50-run flake benchmark (`scripts/flake_run.py`) needs a
   Python interpreter, which isn't available in this environment (`python3`/`python` both fail to
   launch). Everything else on the task's own checklist ran and passed for real. *Needs: either a
   Python interpreter in this environment, or you run the benchmark yourself and report the
   number back.*

---

## How to use this doc

Answer inline (quote the number, e.g. "Web-2: here's the suite ..."), or just tell me "proceed
on all defaults" and I'll keep moving without re-asking. I'll update this file as items resolve
and add a new module's section when its spec arrives.
