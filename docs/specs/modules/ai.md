# AI Module

`ModuleKind.Ai` ("ai")

## Status

TS prototype only (`src/modules/ai/AiModule.ts`). No .NET port exists yet.
The C# `StepOutcome` type already carries an `AiAttribution` field
(`Yukti.Contracts/IAutomationModule.cs`) and `FlowEngine` threads it
through (`retryOutcome.FinalOutcome.AiAttribution` in `FlowEngine.cs`),
so the domain model has already reserved a slot for AI-generated results
even though the module producing them isn't ported yet.

## Purpose

This module is categorically different from the other five: it does not
click, tap, request, or read logs itself — it writes and repairs the
automations that the other modules run, via the Claude API. Three concrete
jobs, one per action:

1. `generateFlow` — turn a plain-English description into a runnable Flow (JSON).
2. `triageLogs` — summarize raw log lines into a likely root cause.
3. `healSelector` — given a broken CSS selector, its original intent, and
   current page HTML, suggest a replacement selector.

## Dependencies

- `@anthropic-ai/sdk`.
- `ANTHROPIC_API_KEY` environment variable (or `apiKey` constructor
  option) — required for every action; there is no offline/mock mode.

## Constructor options

| Option | Type | Default |
|---|---|---|
| `apiKey` | string | `process.env.ANTHROPIC_API_KEY` |
| `model` | string | `"claude-sonnet-4-5"` |

## Lifecycle

No `setup()`/`teardown()` — the Anthropic client is constructed once in
the module constructor and reused across calls.

## Actions

### `generateFlow`

| Param | Type | Required | Default |
|---|---|---|---|
| `description` | string | yes | - |
| `availableModules` | string[] | no | `["api","logs","web","mobile","ui"]` (note: excludes `"ai"` itself — the model is never asked to generate a flow step that recursively calls the AI module) |

Sends a system prompt constraining the model to emit only JSON matching
the `Flow` TypeScript type, restricted to the given `availableModules`
list, `max_tokens: 2000`. Response text is `JSON.parse`'d.

- Pass: `message: "Generated flow \"<name>\" with <N> steps"`, `data: <parsed Flow>`.
- Fail: if the model's output isn't valid JSON, `error: "Model did not return valid Flow JSON"`, `data: {raw: <the actual text>}` (the raw text is preserved rather than discarded, so a human can see what actually came back).

Also exposed directly via the CLI: `yukti generate <description...> [-o <path>]`
(`src/cli.ts`) — calls this action outside of any Flow/Orchestrator context
(`ctx: {vars: {}, results: []}`) and writes the resulting JSON straight to
disk.

### `triageLogs`

| Param | Type | Required |
|---|---|---|
| `logText` | string | yes |

System prompt casts the model as "an SRE triaging logs," constrained to
return JSON only:
`{severity: "info"|"warn"|"error"|"critical", likelyRootCause, affectedComponent, suggestedNextStep}`.
`max_tokens: 1000`.

- Pass: `message: "Triaged as <severity>: <likelyRootCause>"`, `data: <parsed triage object>`.
- Fail: `error: "Model did not return valid triage JSON"`, `data: {raw}`.

### `healSelector`

| Param | Type | Required |
|---|---|---|
| `brokenSelector` | string | yes |
| `intent` | string | yes |
| `pageHtml` | string | yes (truncated to first 8000 chars before sending) |

System prompt: given a broken CSS selector after a UI change, plus intent
and current HTML, return JSON only:
`{newSelector: string, confidence: number, reasoning: string}`.
`max_tokens: 300`.

- Pass: `message: "Suggested selector: <newSelector> (confidence <confidence>)"`, `data: <parsed heal object>`.
- Fail: `error: "Model did not return valid healing JSON"`, `data: {raw}`.

Any other action name: `{status: "failed", error: "Unknown ai action \"<action>\". Supported: generateFlow, triageLogs, healSelector."}`.

## Cross-cutting behavior

All three actions share one pattern: constrain the model via a strict
system prompt to emit JSON only, then `try { JSON.parse(text) }` — on
failure, report a typed error but preserve the raw model output in
`data.raw` rather than losing it. This is the module's only defense
against model non-compliance; there is no JSON-schema-validated retry, no
function/tool-calling enforcement, and no repair loop if the first
response is malformed.

## Known constraints

- No retry/self-repair if the model returns malformed JSON — it fails the
  step outright on the first bad response (the Flow Engine's uniform retry
  policy would re-invoke the whole action, including a fresh, potentially
  equally-malformed, model call).
- No cost/token tracking or budget enforcement at the module level — this
  is presumably meant to be handled by the platform-level "Provider and
  Cost Infrastructure" (Volume 3 Part 4), which doesn't exist in this repo
  yet.
- `healSelector`'s 8000-char HTML truncation is a blunt token-budget
  guard, not a structural one (e.g. no DOM-relevant-subtree extraction).
- Single hardcoded model default (`claude-sonnet-4-5`) with no per-action
  model override (e.g. a cheaper/faster model for `healSelector`'s small
  300-token job vs. a stronger one for `generateFlow`).
