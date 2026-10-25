# Quickstart

Clone to a first passing run, in one sitting, with nothing more than Docker installed.
Target: under 10 minutes.

This page is written to be followed literally, one command at a time. If something here
doesn't work exactly as written, that's a real documentation bug — not something to work
around quietly.

## 0. Prerequisites

- Docker and Docker Compose (`docker compose version` should print something).
- That's it. No .NET SDK, no Node, no local Postgres/Redis/Vault — the stack brings its own.

## 1. Clone and start the stack

```bash
git clone <this repository's URL>
cd Yukti
```

```bash quickstart
docker compose up -d
```

This starts five containers: Postgres, Redis, a dev-mode Vault, the API, and the background
worker. The API applies its own database migrations and seeds starter data on first boot —
there is no separate setup step.

## 2. Wait for the API to come up

```bash quickstart
./scripts/wait-healthy.sh yukti-api
curl -f http://localhost:8080/health
```

The first command polls the `api` container's own Docker healthcheck until it reports
`healthy` (up to 90 seconds on a cold pull — building the image and running migrations takes
most of that). The second is the same check the Day 6 release gate itself runs — a `200` with
`{"status":"healthy"}` means the API, Postgres, Redis and Vault are all genuinely reachable
from inside the container, not just that the process started.

## 3. Start the frontend

The frontend isn't part of `docker-compose.yml` on purpose — it's meant to run with
hot-reload during development, not as a built container for local use.

```bash
cd apps/yukti-gui
npm install
npm run dev
```

Open **http://localhost:5173** in a browser.

## 4. Log in

Two accounts are seeded automatically on first start:

| Email | Password | Role | Tenant |
|---|---|---|---|
| `admin@yukti.local` | `ChangeMe123!` | Administrator | tenant A (the bootstrap tenant) |
| `demo@yukti.local` | `ChangeMe123!` | Flow Author | tenant B (a separate tenant, proving tenant isolation isn't just a database test) |

Log in as either — `admin@yukti.local` is the better starting point since it can see
everything. **Change this password before any real deployment**; it's a documented,
intentionally public dev-only default, not a secret.

## 5. Look at the sample flow

The Flows page already has one flow waiting: **"Sample: API health check"**. It's a single
step — an API request to Yukti's own `/health` endpoint, asserting a `200` — seeded for
exactly this moment, so there's something real to look at before building anything yourself.

Open it, click **Run**, and watch it pass. This proves the whole chain — browser → API →
Postgres → the real `FlowEngine` → back to the browser over the live run view — works, before
you've written a single step of your own.

## 6. Build your own first flow

This is the thing the sample flow was seeded to lead into. From the Flows page:

1. **Create flow** — give it a name.
2. **Add step** — module `API`, action `request`. Set the URL to
   `http://localhost:8080/health`, method `GET`, and add an assertion: `status` equals `200`.
3. **Publish** the flow (Draft → Published; a published flow is immutable, future edits create
   a new version).
4. **Run** it, and watch the result — pass/fail per step, with timing.

That's the complete loop: create → add a step → publish → run → see a result. Every other
module (Web, Logs, Mobile) and every other feature (AI-assisted generation, triage, reports)
builds on exactly this.

## 7. The CLI, if you'd rather not use a browser at all

`demo/sample_flow.json` is the CLI-runnable form of the exact flow step 5 seeded into the
browser — same check, same assertion, run against the same compose stack's exposed port:

```bash quickstart
dotnet run --project src/Yukti.Cli -- run demo/sample_flow.json --verbose
```

For a flow that's entirely self-contained (no API/Postgres needed at all — it doesn't target
the live app):

```bash
dotnet run --project src/Yukti.Cli -- run demo/api_chain.json --junit out.xml --verbose
```

Both run a flow file directly, in-process, against the real `FlowEngine`. See `docs/cli.md`
for the full command reference, and `README.md`'s demo flow table for what else is in `demo/`.

## If something doesn't work

- `docker compose logs api` — the API's own startup log, including migration output.
- `docker compose ps` — confirms which containers report healthy.
- This page is the one D7-T4's own time-to-first-flow trial is measured against. If a step
  here didn't work as written for you, that's the finding the trial exists to surface — it
  isn't you doing something wrong.
