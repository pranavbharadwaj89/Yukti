# AI spend budget — Week of Mon 12 Oct 2026

Set by Sutradhar, 2026-10-14, using the Day 5 spec's own suggested
default rather than waiting on the owner to pick a number — this is a
soft, software-enforced cap (D5-T9), not a real charge; change it anytime
by editing this file and the owner's own Gemini billing console if a
harder limit is wanted there too.

| | |
| --- | --- |
| **Week total** | $30.00 |
| **Day 5's share** | $15.00 |
| **Provider** | Gemini (`gemini-3.8-flash`, per ADR-0004) |
| **Spent so far** | $0.00 (no live call has been made yet — D5-T1/T2 used `StubAiProvider`/no provider at all) |

## Enforcement (D5-T9, not yet built)

- 80% of the week's cap → log a warning, keep working.
- 100% → `POST /api/v1/ai/*` returns 503 "AI budget reached"; the rest of
  Yukti keeps working. Enforced in software against `ai_cost_records`, not
  a hope that nobody goes over.
- The eval harness (D5-T8) and the final acceptance run are the only
  things expected to spend anything today — D5-T3/T4/T5's own tests run
  against `StubAiProvider`, zero cost.

## Credential path

The Gemini key itself is **not** in this file, or in any file in this
repo — it goes through Vault (`ai-gemini-api-key` reference,
`ICredentialResolver`), same as every other credential. Sutradhar reads
the value directly from `sarvaghna/.env.local` (already present on this
machine, gitignored) when a live call is actually needed, and writes it
into a local Vault dev server — the owner does not need to paste it again.
