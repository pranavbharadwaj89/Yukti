# ADR-0004: Gemini, not OpenAI, is Sarvagna's real AI provider

Status: Accepted
Date: 2026-10-14
Owner approval: explicit — owner stated directly "I don't have OpenAI Key. I have gemini API key."
Supersedes: ADR-0003 (2026-10-14, same day — see below)

## Context

ADR-0003 chose OpenAI as the real `IAiProvider` implementation, reasoning
that it was "least work... nothing already paid for/configured gets
thrown away." That reasoning assumed a working OpenAI key existed
somewhere. It doesn't — checked at the time (grep for `OpenAI:ApiKey`/
`OPENAI_API_KEY` across the repo, no Vault secret, no env var) and now
confirmed directly by the owner: there has never been an OpenAI key for
this project, for `OpenAiAssistantService` or anything else.

What the owner does have: a Gemini API key, already used in this exact
repository today — `sarvaghna/core/gemini-client.mjs` is the real,
working integration the Sarvaghna crew (commit-hook review, digest
reports) calls through, local to `sarvaghna/.env.local`
(`GEMINI_API_KEY`, gitignored).

## Decision

`GeminiProvider : IAiProvider` (`Yukti.Infrastructure/Ai/GeminiProvider.cs`)
is the real Sarvagna planning-pipeline provider going forward, registered
in place of `OpenAiProvider` when `Ai:Enabled=true`. Follows the same REST
shape `gemini-client.mjs` already proves works:
`POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}`,
`responseMimeType: "application/json"` for structured output.

`OpenAiProvider` (D5-T1) is **not deleted** — it's a complete, tested,
working `IAiProvider` implementation, just currently unusable for lack of
a key. If an OpenAI key ever exists, switching back is a DI registration
change, not a rewrite. `OpenAiAssistantService`/`IAiAssistantService`
(the three pre-existing capability endpoints — assertions/selector-heal/
flow-suggest) are untouched by this ADR; they were never part of the
Day 5 `IAiProvider` work and were already proven to degrade to their
deterministic heuristic without a key (D1 GAP-MATRIX finding, unchanged).

The API key is resolved through `ICredentialResolver`/Vault
(`ai-gemini-api-key` reference, configurable), the same mechanism
`OpenAiProvider` uses — not read from `sarvaghna/.env.local` directly.
That file belongs to the separate sarvaghna Node.js tooling; Yukti's own
Gemini key is a distinct secret the owner provides for this purpose,
even if it's the same underlying Google account/key value.

## Why

- It's the vendor the owner actually has a working key for, today, not a
  hypothetical. Nothing blocks on waiting for an OpenAI key that was
  never coming.
- The exact request/response shape is already proven in this repo under
  real load (the Sarvaghna crew's own semantic reviews throughout this
  session), not a guess at an untested API.
- `IAiProvider` was built vendor-neutral in D5-T1 specifically so this
  kind of pivot is a new class behind one interface, not a rewrite of the
  planning pipeline, Analyst, or Coder.

## Consequences

- CLAUDE.md/SUTRADHAR.md's AI line (just updated to say OpenAI per
  ADR-0003) needs updating again to say Gemini.
- `docs/ai-data.md`'s "when AI is disabled" section names
  `OpenAiAssistantService`/`OpenAiProvider` as the only classes capable of
  an outbound call — needs `GeminiProvider` added to that list.
- Two vendor ADRs exist for the same day (0003, then 0004 superseding it)
  because the facts changed mid-day, not because the first decision was
  made carelessly — GAP-MATRIX's own vendor question was answered in good
  faith with the information available at the time.
