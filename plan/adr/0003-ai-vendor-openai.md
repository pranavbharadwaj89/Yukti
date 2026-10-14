# ADR-0003: Keep OpenAI as Sarvagna's real AI provider

Status: Accepted
Date: 2026-10-14
Owner approval: explicit — owner chose "Keep OpenAI, add abstraction" when asked directly ahead of Day 5 work.

## Context

`plan/GAP-MATRIX.md` flagged this on Day 1 as blocking Day 5's start: the
plan's own vocabulary (CLAUDE.md §4, PLAN.md's Day 5 skeleton) names
`IAiProvider` with an Anthropic implementation; the actual code
(`src/Yukti.Infrastructure/Ai/OpenAiAssistantService.cs`) is a real, working
OpenAI integration, no abstraction. Three options were on the table:

1. Keep OpenAI, add the `IAiProvider` abstraction around it.
2. Replace OpenAI with Anthropic entirely, per the plan as literally written.
3. Build both behind the abstraction, selectable by config.

The Day 5 spec ("Yukti — Day 5 Spec: Sarvagna, the AI Layer") that triggered
this day's work assumes option 1 without flagging it as a decision — it
treats keeping OpenAI as a non-blocking, already-settled question. It isn't:
GAP-MATRIX never got an answer, and CLAUDE.md §4 still says Anthropic.

## Decision

Option 1. `OpenAiAssistantService` keeps serving its existing three
capability-specific endpoints (`/ai/assertions/generate`,
`/ai/selectors/heal`, `/ai/flows/suggest`) unchanged. A new, separate
`OpenAiProvider : IAiProvider` (`src/Yukti.Infrastructure/Ai/OpenAiProvider.cs`)
is the real implementation of the Day 5 Sarvagna provider abstraction — same
vendor, generic structured-generation contract, Vault-resolved key instead of
config/env.

## Why

- Least work, and nothing already paid for/configured gets thrown away.
- `IAiProvider` is a real abstraction regardless of which vendor sits behind
  it today — switching to Anthropic later (or adding it alongside) is a new
  class implementing one interface, not a rewrite of Analyst/Coder/the
  planning pipeline.
- Replacing a working integration (option 2) or building two providers when
  only one is used in production (option 3) is cost without a corresponding
  benefit this week.

## Consequences

- CLAUDE.md §4's "AI: provider-abstracted `IAiProvider` (Anthropic via its
  HTTP API or official .NET SDK) + a stub provider" line is now wrong and
  needs updating to say OpenAI — tracked as its own small follow-up, not
  silently left contradicting this ADR.
- The AI API key is a new Vault secret (`yukti/credentials/ai-openai-api-key`
  by default, configurable via `Ai:OpenAi:CredentialReference`) — distinct
  from whatever `OpenAI:ApiKey`/`OPENAI_API_KEY` config `OpenAiAssistantService`
  already reads directly; the two are not the same secret until someone
  points them at the same value, which is a deliberate non-goal of this ADR
  (migrating the older service onto Vault too is out of Day 5's scope).
