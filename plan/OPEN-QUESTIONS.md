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

WEB-T1 (shared vocabulary) closed 2026-10-29. WEB-T2 (Object Repository) next.

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
5. 🔵 Before WEB-T2: investigating whether the existing artifact-storage machinery (the thing
   `Flow`/`ObjectRepository`'s `RequestTemplate` precedent should reuse) supports a second
   artifact kind cleanly, or needs storage work first. Will report findings before starting T2,
   not asking you anything yet.

---

## How to use this doc

Answer inline (quote the number, e.g. "Web-2: here's the suite ..."), or just tell me "proceed
on all defaults" and I'll keep moving without re-asking. I'll update this file as items resolve
and add a new module's section when its spec arrives.
