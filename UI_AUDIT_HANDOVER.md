# Adding the UI Audit stage to the pipeline

One script, one stage, two environment variables. Should take about ten
minutes. You don't need to know anything about how the audit works.

## What it is

`run-ui-audit.sh` points a UI auditor (Brevitas) at a build that has already
been deployed, writes a report, and exits with a code you can act on.

It **reads** the app. It never writes to it, never changes application
behaviour, and never modifies your pipeline's other stages.

## Where it goes

```
code → unit tests → deploy → regression tests → [ run-ui-audit.sh ]
```

Last stage. It needs a deployed, reachable build, so it cannot run before
deploy.

## Install

Already dropped in this repo at `scripts/run-ui-audit.sh`:

```bash
chmod +x scripts/run-ui-audit.sh
git add scripts/run-ui-audit.sh
git commit -m "Add UI audit pipeline stage"
```

## The two variables it needs

| Variable | Value |
|---|---|
| `BREVITAS_TARGET_URL` | URL of the build that was just deployed |
| `BREVITAS_UPSTREAM_DEPLOY` | `success`, `failed` or `skipped` — your deploy stage's result |

That's the required set. The script refuses to assume the deploy succeeded,
because auditing a failed deploy produces nonsense findings that look like
real ones.

Pass the regression result too if you have it:

| Variable | Value |
|---|---|
| `BREVITAS_UPSTREAM_REGRESSION` | `success`, `failed` or `skipped` |

## Verify the wiring first

Before you trust it in a real run:

```bash
BREVITAS_TARGET_URL=https://staging.example.com \
BREVITAS_UPSTREAM_DEPLOY=success \
  ./scripts/run-ui-audit.sh --check
```

`--check` validates the config, confirms the engine is installed, and polls
the app until it answers — then stops without auditing. If that passes, the
real run will reach the audit.

## Pipeline snippets

**Jenkins (declarative)**

```groovy
stage('UI Audit') {
  when { expression { true } }   // runs even if regression failed; see note
  environment {
    BREVITAS_TARGET_URL      = "${STAGING_URL}"
    BREVITAS_UPSTREAM_DEPLOY = "${env.DEPLOY_RESULT}"
    BREVITAS_USERNAME        = credentials('brevitas-user')
    BREVITAS_PASSWORD        = credentials('brevitas-pass')
  }
  steps {
    sh './scripts/run-ui-audit.sh'
  }
  post {
    always {
      archiveArtifacts artifacts: '.brevitas/reports/**', allowEmptyArchive: true
    }
  }
}
```

**GitLab CI**

```yaml
ui-audit:
  stage: ui-audit
  needs: [deploy]
  when: always
  variables:
    BREVITAS_TARGET_URL: $STAGING_URL
    BREVITAS_UPSTREAM_DEPLOY: success
  script:
    - ./scripts/run-ui-audit.sh
  artifacts:
    when: always
    paths: [.brevitas/reports/]
    expire_in: 30 days
```

**Plain shell / any other runner**

```bash
export BREVITAS_TARGET_URL="$STAGING_URL"
export BREVITAS_UPSTREAM_DEPLOY=success
export BREVITAS_UPSTREAM_REGRESSION=success
./scripts/run-ui-audit.sh
```

**This repo's own GitHub Actions wiring** is `.github/workflows/ui-audit.yml`
plus the Python agent in `agent/` (`context.py`/`decide.py`/`run.py`), not the
bash script directly — see "Status in this repo" below for exactly what that
wiring does and does not do yet.

## Two things to get right

**Let the stage run even when an upstream stage failed.** That's the
`when: always` / `expression { true }` above. The script applies its own gate
and writes a `SKIPPED` report explaining why it didn't audit. If you guard
the stage so it never starts, you get no report, and "audit skipped" becomes
indistinguishable from "audit never ran".

**Always archive `.brevitas/reports/`, including on failure.** The report for
a failed run is the one someone needs to look at.

## Exit codes

| Code | Meaning | What to do |
|---|---|---|
| 0 | Pass, or warnings only | Nothing |
| 1 | A blocking UI regression | Read the report |
| 2 | The audit could not run | Infrastructure or config problem, not a UI problem |
| 3 | Skipped — an upstream stage didn't pass | Nothing; expected |

`1` and `2` are deliberately different. "The UI regressed" and "the audit
broke" need different people looking at them, so please don't collapse them
into a generic failure in your pipeline's reporting.

## It can't fail your build yet

The script is **report-only by default**: a UI finding is reported but exits
0. Leave it that way for a week or two of real runs so we can see how noisy
it is against this app before it gates anything.

When we're ready, set `BREVITAS_REPORT_ONLY=false` (or pass `--gate`). No
other change.

Exit code 2 still surfaces in report-only mode — a broken audit should be
visible immediately rather than sitting silent for a fortnight.

## Finding the report

Last line of stdout, every run:

```
BREVITAS_REPORT=/path/to/.brevitas/reports/<build>/report.md
```

Grep for `BREVITAS_REPORT=` if you want to link it from a build summary or
post it to a channel. The folder also holds `report.html` (the readable one),
plus SARIF and JUnit XML if your dashboard consumes either.

## Secrets

If the audited flows need a login, pass `BREVITAS_USERNAME` and
`BREVITAS_PASSWORD` from the pipeline's secret store. Never commit them. The
script never prints them.

## One-time setup — not yet done in this repo

The script and the agent both expect a `.brevitas/` folder committed in the
repo root: `.brevitas/config.yml`, `.brevitas/flows/*.yml`, and
`.brevitas/baselines/*.json`. **That folder does not exist in Yukti yet.**
Until it does:
- `agent.run` (the GitHub Actions path) exits 2 immediately — its own `main()`
  checks for `.brevitas/` first and refuses to guess a config.
- `run-ui-audit.sh` (the bash path) exits 2 at the same check, with the same
  reasoning.

Creating it is deliberately not done here — per the original instructions
for this handover, the baselines need to be captured deliberately, not
generated on a whim, so this needs you before it's real. See "Status in this
repo" below for the exact list.

Also: the script installs the audit engine itself on first run, pinned to a
version tag. If the pipeline agent can't reach the Brevitas git repo, set
`BREVITAS_REPO`, or pre-install `brevitas` on the agent and the script will
find it on `PATH`.

## Other variables

`./scripts/run-ui-audit.sh --help` lists all of them. The ones you're most
likely to need:

| Variable | Default | Notes |
|---|---|---|
| `BREVITAS_HEALTH_PATH` | `/health` | Set to the app's real health endpoint, or `""` to skip the poll |
| `BREVITAS_HEALTH_TIMEOUT` | `180` | Seconds to wait for the app to come up |
| `BREVITAS_REGRESSION_MODE` | `skip` | `skip` / `optional` / `required` — see below |
| `BREVITAS_BUILD_ID` | git short SHA | Names the report folder |

## When regression becomes reliable

Today `BREVITAS_REGRESSION_MODE` defaults to `skip`, so the audit runs
whether or not regression passed. Once the regression suite is trustworthy,
set it to `required` and the audit will skip itself (exit 3) when regression
hasn't passed. That's the only change needed — no edit to the script.

## Status in this repo (added 2026-10-31)

What exists now:
- `scripts/run-ui-audit.sh` — committed, executable, syntax-checked.
- `agent/__init__.py`, `agent/context.py`, `agent/decide.py`, `agent/run.py`
  — committed.
- `agent/test_decide.py` — committed and passing (24/24, pure unit tests, no
  repo/network/filesystem needed).
- `.github/workflows/ui-audit.yml` — committed, **manual dispatch only for
  now**. `push`/`pull_request` are commented out on purpose: Sarvaghna's own
  review caught that enabling them today would put a permanently failing
  "UI Audit" check on every future PR and push to `main`, since every
  automatic run would hit gap #2 below and exit 2. Uncomment both once
  `.brevitas/` is real and Deploy actually starts the app.

What does **not** work yet, and needs you specifically:
1. **No real Brevitas engine wired in.** `BREVITAS_REPO` in the script, and
   the commented-out pip install line in `ui-audit.yml`, both still say
   `REPLACE-ME`/are commented out. Point them at the real Brevitas repo and
   tag.
2. **No `.brevitas/` config.** No `config.yml`, no `flows/`, no `baselines/`.
   Both entrypoints exit 2 immediately until this exists — correctly, not as
   a bug. Needs your deliberate input, not a generated stand-in.
3. **The workflow's own Deploy step is a placeholder.** `echo "TODO: docker
   compose up -d --build"` — it doesn't actually start Yukti. Yukti already
   has a real `docker compose up -d --build` + `scripts/wait-healthy.sh`
   pattern (see `ci.yml`'s `docker-compose-smoke` job) that this step should
   probably call instead, once the rest of this is real.
4. **This overlaps with work already done this session.** A different UI
   auditor ("the Git Agent", inside `sarvaghna/agents/git-agent.mjs` +
   `sarvaghna/run-git-agent.mjs`) already exists, already runs on every merge
   to `main` via `.github/workflows/sarvaghna.yml`'s `git-agent-ui-audit` job,
   and already audits Yukti's real frontend (axe-core WCAG scan + HTTP/console
   checks per route, via Playwright). Once this file exists, **two separate
   "audit the UI on merge" GitHub Actions jobs will run on every merge to
   `main`** until one of them is retired or they're reconciled. Flagging this
   plainly rather than deciding it myself: keep both, retire the Git Agent, or
   fold the Git Agent's axe-core/Playwright checks into Brevitas's flow
   config once `.brevitas/` exists — that's a call for you to make, not me.
