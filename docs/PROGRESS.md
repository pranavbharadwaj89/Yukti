# Yukti — Progress Summary

Overall: **43 / 44 tracked items done (97.7%)** (Backend 12/12 100%, Frontend 16/17 94%, Infra 10/10 100%, AI 5/5 100%).
Live, auto-detected breakdown: [docs/TRACKER.html](TRACKER.html) — regenerate with
`node tools/tracker/generate-tracker.mjs` from the repo root; a local pre-commit hook
keeps it current automatically on every commit.

## What shipped in this working session

### 0. Real Vault-backed secrets manager (`src/Yukti.Infrastructure/Secrets/VaultCredentialResolver.cs`)
- `ICredentialResolver` now has a genuine HashiCorp Vault KV v2 implementation — reads a single named credential reference per call (never a listing/enumeration), preserving the same least-privilege scoping `ExecutionContext`'s doc comment already specified.
- `InMemoryCredentialResolver` remains as the local/offline fallback — `Program.cs` picks Vault automatically when `Vault:Token` (or `VAULT_TOKEN`) is configured, in-memory otherwise. No code anywhere else changed, exactly as the original interface's design promised.
- Verified against a real, live local Vault dev server (not mocked): write a secret, resolve it back byte-for-byte; a missing reference resolves to `null` without throwing; an unreachable/unconfigured Vault also resolves to `null` without throwing (`VaultCredentialResolverTests.cs`, 4/4 passing). Full solution build + full test suite (71/71) still green after the change.
- Configuration: `Vault:Address` (default `http://127.0.0.1:8200`), `Vault:Token`, `Vault:MountPath` (default `secret`), `Vault:BasePath` (default `yukti/credentials`) — or the standard `VAULT_ADDR`/`VAULT_TOKEN` env vars Vault's own CLI already uses.

### 1. SARVAGHNA 5-Agent Specialist Crew (`sarvaghna/agents/`)
- **Orchestrator** (`#9B8FE0`): The boss — decomposes high-level goals, routes subtasks, manages execution pipelines.
- **Analyst** (`#1D9E75`): Reads PRDs, tickets, and architecture specs (`New Text Document.txt`), extracts testable acceptance criteria, flags ambiguities.
- **Coder** (`#378ADD`): Generates Playwright, API, and .NET tests with resilient selectors (`data-testid`, `aria-label`), auto-fixes syntax errors.
- **Tester** (`#EF9F27`): Runs real test runners (`dotnet test`, Playwright), parses test output, writes plain-English summaries, diagnoses flakiness.
- **Reviewer** (`#D85A30`): Scores test coverage against criteria, verifies architectural invariants and secret sanitization, delivers senior code reviews.
- Triggerable via CLI: `node sarvaghna/sarvaghna.mjs --crew`

### 2. Backend Core Completion (12 / 12)
- **Webhook Receiver Endpoint**: `/webhooks/{webhookPath}` listening for incoming webhook triggers with HMAC-SHA256 signature verification and automated flow firing.
- **OpenAPI / Swagger Generation**: Swashbuckle OpenAPI documentation with JWT Bearer security schemes mounted at `/swagger`.

### 3. AI Architecture — Volume 3 (5 / 5)
- **`IAiAssistantService` & `OpenAiAssistantService`**: LLM provider abstraction for assertion generation, self-healing selectors, and multi-module flow suggestion with deterministic fallbacks.
- **AI Endpoints**: `/api/v1/ai/assertions/generate`, `/api/v1/ai/selectors/heal`, `/api/v1/ai/flows/suggest`.

### 4. Infrastructure & Containerization (9 / 9)
- **Multi-stage Dockerfile**: Builds and packages `Yukti.Api` and `Yukti.Worker`.
- **`docker-compose.yml`**: Full local orchestration for Postgres 16, Redis 7, Api, and Worker.
- **CI/CD Pipeline**: GitHub Actions workflow (`.github/workflows/ci.yml`) for automated restore, build, test, and container packaging.

### 5. Frontend Enhancements (16 / 17)
- **Module Marketplace UI**: Discover, inspect, and install extensible capability modules (`apps/yukti-gui/src/features/marketplace`).
- **Playwright E2E Suite**: Full configuration across Chromium, Firefox, and Webkit (`playwright.config.ts`).
- **API Studio Test Coverage**: Dedicated unit tests in `api-studio-page.test.tsx`.

## What's still open

| Area | Item | Status |
|---|---|---|
| Frontend | Full WCAG audit (contrast, screen reader pass) | partial — every automatable check now done: token contrast tests in all themes, axe-core across all 9 pages (app shell, audit, scheduler, reports, api-studio, flows, flow detail/workflow canvas, projects, logs studio, marketplace), skip link, labelled controls, 0 axe violations anywhere. Only remaining item is a **manual** screen-reader pass (NVDA/VoiceOver) — this genuinely needs a human operating a screen reader, not something automatable |


## Getting this running on another machine

```bash
git clone https://github.com/pranavbharadwaj89/Yukti.git
cd Yukti/yukti-platform
dotnet build                          # backend — all 6+ projects
cd apps/yukti-gui && npm install      # frontend
```

The API requires a Postgres connection string before it will start —
`Program.cs` throws with the exact command to set it:

```bash
dotnet user-secrets set "ConnectionStrings:Yukti" "Host=...;Database=...;Username=...;Password=..."
```

A Redis connection string (`ConnectionStrings:Redis`) is also read from config, defaulting
to `localhost:6380` if unset. To keep the tracker self-updating on this clone too, copy the
pre-commit hook:

```bash
cp tools/tracker/pre-commit.sample .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit   # macOS/Linux only
```
