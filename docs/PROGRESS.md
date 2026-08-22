# Yukti — Progress Summary

Overall: **32 / 43 tracked items done** (Backend 10/12, Frontend 13/17, Infra 7/9, AI 2/5).
Live, auto-detected breakdown: [docs/TRACKER.html](TRACKER.html) — regenerate with
`node tools/tracker/generate-tracker.mjs` from the repo root; a local pre-commit hook
keeps it current automatically on every commit.

## What shipped in this working session

### Live build tracker (git-driven, not hand-maintained)
- `docs/TRACKER.html` is generated from real repo state — file existence and content
  checks defined in `tools/tracker/checks.json` — not a checklist someone edits by hand.
- `tools/tracker/generate-tracker.mjs` runs the checks and regenerates the page.
- A local `.git/hooks/pre-commit` hook regenerates and stages it on every commit;
  `tools/tracker/pre-commit.sample` is the committed, copyable version so any other
  clone (including this one on a different machine) can adopt the same hook.

### Reports page: per-flow drill-down + audit tie-in
- New backend: `GET /api/v1/flow-reports` (per-flow pass/fail summary, grouped from the
  existing `FlowReportReadModel`) and `GET /api/v1/flow-reports/{flowId}/runs` (individual
  run history) — same `ReportView` permission as `/trends` and `/audit-entries`.
- Reports page now lists flows below the tenant-wide trend cards; expanding a row shows
  run history plus the `TriggerFlowRunCommand`/`CancelFlowRunCommand` audit entries whose
  metadata matches that flow (the audit tie-in), fetched lazily the same way the Audit
  page's row-expand already works.
- 3 new tests (`reports-page.test.tsx`); full frontend suite and full solution build
  both green.

### Config-driven deployment mode + appsettings.json convention
- `Deployment:SelfHosted` (`Program.cs`) was already read from config and gates FileWatch
  trigger creation, but nothing established an actual config-file convention — everything
  only ever came from ambient, undocumented env vars.
- Added `src/Yukti.Api/appsettings.json` (base, safe defaults, no secrets) and
  `appsettings.Development.json` (EF Core SQL command logging locally).
- `ConnectionStrings` stays out of both files on purpose — `Program.cs` already requires
  those via `dotnet user-secrets` (local) or real environment variables (deployed), and
  throws with that exact guidance if they're missing. Never commit a connection string.

## What's still open

| Area | Item | Status |
|---|---|---|
| Backend | Webhook receiver endpoint (fires the trigger) | blocked — `WebhookPath` is generated and stored, nothing listens on it yet |
| Backend | OpenAPI / Swagger document | planned — `types.ts` is hand-written against the live contract instead |
| Frontend | Full WCAG audit (contrast, screen reader pass) | planned — only in-use components have had an accessibility pass so far |
| Frontend | Test coverage: API Studio, Flow Authoring, Projects | planned — zero coverage on these feature areas |
| Frontend | Module marketplace UI | planned — no view/install UI for third-party modules |
| Frontend | E2E suite (Playwright) | planned |
| Infra | CI/CD pipeline | planned — no workflow files in the repo yet |
| Infra | Containerization (Dockerfile / compose) | planned |
| AI | LLM-assisted test/assertion generation | planned — no LLM integration exists yet |
| AI | Self-healing selectors / auto-repair | planned |
| AI | AI-powered flow suggestions | planned |

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
