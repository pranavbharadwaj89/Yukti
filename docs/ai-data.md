# What Yukti sends to its AI provider

D5-T2 requirement: this document states exactly what leaves the process
when AI is enabled, and exactly what never does — so a security reviewer
can assess the risk without reading the prompt-building code themselves.
It covers both AI integrations that exist in this codebase today; keep
both sections current as the Sarvagna planning pipeline (D5-T3 onward)
adds a third.

## 1. The existing capability endpoints (`IAiAssistantService`)

`POST /api/v1/ai/assertions/generate`, `/ai/selectors/heal`, `/ai/flows/suggest`.
Each sends **only the single request's own fields** — nothing pulled from
the database, no other steps, no other users' data:

| Endpoint | Sent | Never sent |
| --- | --- | --- |
| `assertions/generate` | HTTP method, URL, observed status code, the one response body it was asked to generate assertions for | Request/response headers (sent only if the caller explicitly passed them), credentials, any other step's data |
| `selectors/heal` | The failed CSS/text selector, its target tag name, the error message, one DOM snippet | The full page, cookies, session state, screenshots |
| `flows/suggest` | The plain-English description, an optional target base URL | Flow history, run results, other flows, tenant/user identity |

Every prompt is built from exactly these fields (see
`src/Yukti.Infrastructure/Ai/OpenAiAssistantService.cs`) — there is no
step that enriches the prompt with anything else before the HTTP call.

## 2. The Sarvagna planning pipeline (D5-T3 onward)

Analyst and Coder's only context is the **module registry**: registered
module kinds, their action names, parameter schemas, and descriptions —
the same data `GET /api/v1/modules` already returns publicly to any
authenticated user. Plus the user's own plain-English description typed
into the generation panel.

**Never sent:** flow history, run results, screenshots, log contents,
credentials, other tenants' or other users' data, page content from a
running test. This is a deliberate week-1 scope choice (see the Day 5
spec's "Context, kept deliberately small") — no retrieval, no embeddings,
no vector store, so there is no path for anything beyond the registry and
the user's own typed text to ever reach the provider.

The planning pipeline's guardrail pre-check (D5-T3) additionally redacts
anything that looks like a secret, API key, or bearer token from the
description before it is sent, and rejects an empty or over-4,000-character
description outright rather than forwarding it.

## 3. When AI is disabled (`Ai:Enabled=false`)

Zero outbound calls to any AI provider are possible — not "none happen to
fire," but none are possible, by construction: `OpenAiAssistantService`
and `GeminiProvider` (the real Sarvagna planning-pipeline provider, per
ADR-0004 — not `OpenAiProvider`, kept in the codebase but currently
unused for lack of an OpenAI key; the only two classes in this codebase
capable of making an outbound AI call) are never constructed at all. Every AI
endpoint returns `503 Service Unavailable` naming AI as disabled for this
deployment. Proven in CI by the `ai-off` job
(`.github/workflows/ci.yml`), which boots the real API with AI disabled —
even with a (deliberately unusable) key configured — and asserts every AI
endpoint's response.
