#!/usr/bin/env bash
#
# run-ui-audit.sh — pipeline stage: UI audit (Brevitas)
#
# WHERE THIS GOES IN THE PIPELINE
#   code -> unit tests -> deploy -> regression tests -> [ THIS SCRIPT ]
#
# WHAT IT DOES
#   Points the Brevitas UI auditor at an already-deployed build, writes a
#   report, and exits with a code the pipeline can act on. It reads the app;
#   it never writes to it and never changes application behaviour.
#
# WHAT IT NEEDS FROM YOU
#   Two environment variables (see --help). Everything else has a default.
#
# EXIT CODES
#   0  pass, or warnings only
#   1  hard fail  (a UI regression that policy says must block)
#   2  could not run  (app unreachable, bad config, login failed, timeout)
#   3  skipped  (an upstream stage did not pass; no audit was attempted)
#
#   0/1/2/3 are deliberately distinct. "The audit broke" (2) and "the UI
#   regressed" (1) are different problems and must not be treated alike.
#
# SAFE BY DEFAULT
#   Report-only unless you explicitly set BREVITAS_REPORT_ONLY=false, so
#   adding this to the pipeline cannot start failing builds on day one.
#
# Run `./run-ui-audit.sh --help` for the full variable list.
# Run `./run-ui-audit.sh --check` to validate wiring without auditing.
#
# Portable bash: works on Linux CI runners and macOS. No GNU-only flags.

set -uo pipefail   # NOT -e: exit codes from the audit are handled explicitly

readonly SCRIPT_NAME="$(basename "$0")"

readonly EXIT_PASS=0
readonly EXIT_FAIL=1
readonly EXIT_ERROR=2
readonly EXIT_SKIPPED=3

# ─────────────────────────────────────────────────────────────────────────────
# Defaults. Override any of these with an environment variable.
# ─────────────────────────────────────────────────────────────────────────────
: "${BREVITAS_TARGET_URL:=}"
: "${BREVITAS_UPSTREAM_DEPLOY:=}"
: "${BREVITAS_UPSTREAM_REGRESSION:=}"
: "${BREVITAS_REGRESSION_MODE:=skip}"        # skip | optional | required
: "${BREVITAS_BUILD_ID:=}"
: "${BREVITAS_HEALTH_PATH:=/health}"
: "${BREVITAS_HEALTH_TIMEOUT:=180}"          # seconds
: "${BREVITAS_REPORTS_DIR:=.brevitas/reports}"
: "${BREVITAS_REPORT_ONLY:=true}"            # true = never exit 1
: "${BREVITAS_VERSION:=v1.0.0}"              # pinned engine tag
: "${BREVITAS_REPO:=https://github.com/REPLACE-ME/brevitas}"
: "${BREVITAS_PYTHON:=}"
: "${BREVITAS_FLOWS:=}"                      # optional: comma-separated flow ids
: "${BREVITAS_SKIP_HEALTHCHECK:=false}"

CHECK_ONLY=false

# ─────────────────────────────────────────────────────────────────────────────
# Output helpers
# ─────────────────────────────────────────────────────────────────────────────
log()  { printf '[ui-audit] %s\n' "$*"; }
warn() { printf '[ui-audit] WARNING: %s\n' "$*" >&2; }
err()  { printf '[ui-audit] ERROR: %s\n' "$*" >&2; }

usage() {
  cat <<'HELPTEXT'
run-ui-audit.sh — run the Brevitas UI audit against a deployed build.

USAGE
  ./run-ui-audit.sh [options]

OPTIONS
  --url <url>            Deployed build to audit. Overrides BREVITAS_TARGET_URL.
  --flow <id>            Audit only this flow. Repeatable.
  --check                Validate configuration and connectivity, then exit.
                         Does not run the audit. Use this to verify wiring.
  --gate                 Allow a hard fail to exit 1 (same as
                         BREVITAS_REPORT_ONLY=false).
  -h, --help             This text.

REQUIRED ENVIRONMENT
  BREVITAS_TARGET_URL          URL of the build that was just deployed.
                               e.g. https://staging.example.com
  BREVITAS_UPSTREAM_DEPLOY     success | failed | skipped
                               The result of your deploy stage.

OPTIONAL ENVIRONMENT
  BREVITAS_UPSTREAM_REGRESSION success | failed | skipped
                               The result of your regression stage. Only
                               consulted when BREVITAS_REGRESSION_MODE is
                               'optional' or 'required'.

  BREVITAS_REGRESSION_MODE     skip (default) | optional | required
                               skip      - audit regardless of regression
                               optional  - audit anyway, but flag the report
                               required  - skip the audit unless regression passed
                               Set this to 'required' once the regression
                               stage is reliable. It is the only change needed.

  BREVITAS_REPORT_ONLY         true (default) | false
                               true  - always exit 0 on findings
                               false - a hard fail exits 1 and blocks the build

  BREVITAS_BUILD_ID            Defaults to the short git SHA, else a timestamp.
  BREVITAS_HEALTH_PATH         Default /health. Set to "" to skip the poll.
  BREVITAS_HEALTH_TIMEOUT      Seconds to wait for health. Default 180.
  BREVITAS_REPORTS_DIR         Default .brevitas/reports
  BREVITAS_VERSION             Brevitas engine tag to install. Default v1.0.0.
  BREVITAS_PYTHON              Python interpreter. Autodetected if unset.
  BREVITAS_USERNAME            App login, if the audit scenario needs one.
  BREVITAS_PASSWORD            Use your CI's secret store. Never commit these.

EXIT CODES
  0 pass/warn   1 hard fail   2 could not run   3 skipped by gate

EXAMPLES
  # Verify wiring before trusting it
  BREVITAS_TARGET_URL=https://staging.example.com \
  BREVITAS_UPSTREAM_DEPLOY=success \
    ./run-ui-audit.sh --check

  # Normal pipeline invocation
  BREVITAS_TARGET_URL=https://staging.example.com \
  BREVITAS_UPSTREAM_DEPLOY=success \
  BREVITAS_UPSTREAM_REGRESSION=success \
    ./run-ui-audit.sh
HELPTEXT
}

# ─────────────────────────────────────────────────────────────────────────────
# Stub report writer
#
# Every run must leave a report behind, including runs that skip or error.
# Without this, "audit skipped" and "audit never ran" look identical in the
# pipeline, and nobody can tell which happened.
# ─────────────────────────────────────────────────────────────────────────────
write_stub_report() {
  local verdict="$1" reason="$2" dir
  dir="${BREVITAS_REPORTS_DIR}/${BREVITAS_BUILD_ID}"
  mkdir -p "$dir" 2>/dev/null || return 0

  cat > "${dir}/report.json" <<EOF
{
  "verdict": "${verdict}",
  "reason": "${reason}",
  "project": "$(basename "$(pwd)")",
  "build_id": "${BREVITAS_BUILD_ID}",
  "target_url": "${BREVITAS_TARGET_URL}",
  "upstream": {
    "deploy": "${BREVITAS_UPSTREAM_DEPLOY:-unset}",
    "regression": "${BREVITAS_UPSTREAM_REGRESSION:-unset}",
    "regression_mode": "${BREVITAS_REGRESSION_MODE}"
  },
  "findings": [],
  "generated_at": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "generated_by": "run-ui-audit.sh"
}
EOF

  cat > "${dir}/report.md" <<EOF
### UI Audit — ${verdict}

${reason}

| | |
|---|---|
| Build | \`${BREVITAS_BUILD_ID}\` |
| Target | ${BREVITAS_TARGET_URL:-(not set)} |
| Deploy | ${BREVITAS_UPSTREAM_DEPLOY:-unset} |
| Regression | ${BREVITAS_UPSTREAM_REGRESSION:-unset} (mode: ${BREVITAS_REGRESSION_MODE}) |

No audit was performed, so this is **not** a clean pass.
EOF

  rm -f "${BREVITAS_REPORTS_DIR}/latest" 2>/dev/null || true
  ln -s "${BREVITAS_BUILD_ID}" "${BREVITAS_REPORTS_DIR}/latest" 2>/dev/null || true

  log "Stub report written: ${dir}/report.md"
  printf 'BREVITAS_REPORT=%s/report.md\n' "$(cd "$dir" && pwd)"
}

die_error() {
  err "$1"
  [ -n "${2:-}" ] && printf '\n  %s\n\n' "$2" >&2
  write_stub_report "ERROR" "$1"
  exit "$EXIT_ERROR"
}

# ─────────────────────────────────────────────────────────────────────────────
# Arguments
# ─────────────────────────────────────────────────────────────────────────────
FLOW_ARGS=()
while [ $# -gt 0 ]; do
  case "$1" in
    --url)   [ $# -ge 2 ] || die_error "--url needs a value"
             BREVITAS_TARGET_URL="$2"; shift 2 ;;
    --flow)  [ $# -ge 2 ] || die_error "--flow needs a value"
             FLOW_ARGS+=(--flow "$2"); shift 2 ;;
    --check) CHECK_ONLY=true; shift ;;
    --gate)  BREVITAS_REPORT_ONLY=false; shift ;;
    -h|--help) usage; exit 0 ;;
    *) err "Unknown option: $1"; echo; usage >&2; exit "$EXIT_ERROR" ;;
  esac
done

if [ -n "$BREVITAS_FLOWS" ] && [ ${#FLOW_ARGS[@]} -eq 0 ]; then
  OLD_IFS="$IFS"; IFS=','
  for f in $BREVITAS_FLOWS; do
    [ -n "$f" ] && FLOW_ARGS+=(--flow "$f")
  done
  IFS="$OLD_IFS"
fi

# ─────────────────────────────────────────────────────────────────────────────
# Build id
# ─────────────────────────────────────────────────────────────────────────────
if [ -z "$BREVITAS_BUILD_ID" ]; then
  if command -v git >/dev/null 2>&1 && git rev-parse --short HEAD >/dev/null 2>&1; then
    BREVITAS_BUILD_ID="$(git rev-parse --short HEAD)"
  else
    BREVITAS_BUILD_ID="$(date -u +%Y%m%d-%H%M%S)"
  fi
fi

log "──────────────────────────────────────────────────────────"
log "UI Audit (Brevitas)"
log "build       : ${BREVITAS_BUILD_ID}"
log "target      : ${BREVITAS_TARGET_URL:-(not set)}"
log "deploy      : ${BREVITAS_UPSTREAM_DEPLOY:-(not set)}"
log "regression  : ${BREVITAS_UPSTREAM_REGRESSION:-(not set)}  mode=${BREVITAS_REGRESSION_MODE}"
log "report-only : ${BREVITAS_REPORT_ONLY}"
log "──────────────────────────────────────────────────────────"

# ─────────────────────────────────────────────────────────────────────────────
# Validate inputs. Fail with the fix, not just the complaint.
# ─────────────────────────────────────────────────────────────────────────────
if [ -z "$BREVITAS_TARGET_URL" ]; then
  die_error "BREVITAS_TARGET_URL is not set." \
    "Set it to the URL of the build your deploy stage just produced, e.g.
  export BREVITAS_TARGET_URL=https://staging.example.com
If the app runs on the pipeline agent itself, use its local URL, e.g.
  export BREVITAS_TARGET_URL=http://localhost:3000"
fi

case "$BREVITAS_TARGET_URL" in
  http://*|https://*) ;;
  *) die_error "BREVITAS_TARGET_URL must start with http:// or https:// (got: ${BREVITAS_TARGET_URL})" ;;
esac

if [ -z "$BREVITAS_UPSTREAM_DEPLOY" ]; then
  die_error "BREVITAS_UPSTREAM_DEPLOY is not set." \
    "Set it from the result of your deploy stage: success, failed or skipped.
  Jenkins  : BREVITAS_UPSTREAM_DEPLOY=\$([ \"\$DEPLOY_OK\" = true ] && echo success || echo failed)
  GitLab   : BREVITAS_UPSTREAM_DEPLOY=success   # in a job that 'needs' deploy
  GitHub   : BREVITAS_UPSTREAM_DEPLOY=\${{ needs.deploy.result }}
This script will not assume the deploy succeeded."
fi

case "$BREVITAS_UPSTREAM_DEPLOY" in
  success|failed|skipped) ;;
  *) die_error "BREVITAS_UPSTREAM_DEPLOY must be success, failed or skipped (got: ${BREVITAS_UPSTREAM_DEPLOY})" \
       "An unrecognised value is not treated as success." ;;
esac

if [ -n "$BREVITAS_UPSTREAM_REGRESSION" ]; then
  case "$BREVITAS_UPSTREAM_REGRESSION" in
    success|failed|skipped) ;;
    *) die_error "BREVITAS_UPSTREAM_REGRESSION must be success, failed or skipped (got: ${BREVITAS_UPSTREAM_REGRESSION})" ;;
  esac
fi

case "$BREVITAS_REGRESSION_MODE" in
  skip|optional|required) ;;
  *) die_error "BREVITAS_REGRESSION_MODE must be skip, optional or required (got: ${BREVITAS_REGRESSION_MODE})" ;;
esac

# ─────────────────────────────────────────────────────────────────────────────
# Gate: should the audit run at all?
# ─────────────────────────────────────────────────────────────────────────────
REGRESSION_BANNER=""

if [ "$BREVITAS_UPSTREAM_DEPLOY" != "success" ]; then
  log "SKIPPED — deploy did not succeed (${BREVITAS_UPSTREAM_DEPLOY})."
  log "There is no deployed build to audit. This is not a UI problem."
  write_stub_report "SKIPPED" "Deploy stage reported '${BREVITAS_UPSTREAM_DEPLOY}'; no build to audit."
  exit "$EXIT_SKIPPED"
fi

case "$BREVITAS_REGRESSION_MODE" in
  skip)
    log "Gate: regression not consulted (mode=skip)."
    ;;
  optional)
    if [ "${BREVITAS_UPSTREAM_REGRESSION:-unset}" = "success" ]; then
      log "Gate: regression passed."
    else
      REGRESSION_BANNER="Regression did not pass upstream (${BREVITAS_UPSTREAM_REGRESSION:-unset}). UI findings may reflect broken functionality rather than UI design."
      warn "$REGRESSION_BANNER"
    fi
    ;;
  required)
    if [ "${BREVITAS_UPSTREAM_REGRESSION:-unset}" != "success" ]; then
      log "SKIPPED — regression is required but reported '${BREVITAS_UPSTREAM_REGRESSION:-unset}'."
      write_stub_report "SKIPPED" "Regression stage is required (mode=required) but reported '${BREVITAS_UPSTREAM_REGRESSION:-unset}'."
      exit "$EXIT_SKIPPED"
    fi
    log "Gate: regression passed (required)."
    ;;
esac

# ─────────────────────────────────────────────────────────────────────────────
# This app's audit config. Checked before installing anything: it is the
# most common first-run problem and costs nothing to verify.
# ─────────────────────────────────────────────────────────────────────────────
if [ ! -d ".brevitas" ]; then
  die_error "No .brevitas/ configuration found in $(pwd)." \
    "The UI audit needs this app's own config: which flows to audit, how to
log in, and the committed baselines. If the repo already has one, run this
script from the repo root. If it does not, create it once with
  brevitas init
and commit the .brevitas/ folder."
fi

# ─────────────────────────────────────────────────────────────────────────────
# Locate Brevitas
# ─────────────────────────────────────────────────────────────────────────────
PY=""
if [ -n "$BREVITAS_PYTHON" ]; then
  PY="$BREVITAS_PYTHON"
elif command -v python3 >/dev/null 2>&1; then
  PY="python3"
elif command -v python >/dev/null 2>&1; then
  PY="python"
fi

BREVITAS_CMD=""
if command -v brevitas >/dev/null 2>&1; then
  BREVITAS_CMD="brevitas"
  log "Using brevitas already on PATH: $(command -v brevitas)"
elif [ -x ".venv/bin/brevitas" ]; then
  BREVITAS_CMD=".venv/bin/brevitas"
  log "Using brevitas from local .venv"
else
  [ -n "$PY" ] || die_error "No python3 found, and brevitas is not on PATH." \
    "Install Python 3.11+ on the pipeline agent, or pre-install brevitas and
put it on PATH, or set BREVITAS_PYTHON to an interpreter."

  case "$BREVITAS_REPO" in
    *REPLACE-ME*) die_error "BREVITAS_REPO still points at the placeholder." \
      "Set BREVITAS_REPO to the Brevitas git URL, or pre-install brevitas on
the agent so this script finds it on PATH." ;;
  esac

  log "Installing brevitas ${BREVITAS_VERSION} ..."
  if ! "$PY" -m pip install --quiet --disable-pip-version-check \
        "git+${BREVITAS_REPO}@${BREVITAS_VERSION}" 2>&1 | tail -5; then
    die_error "Could not install brevitas ${BREVITAS_VERSION} from ${BREVITAS_REPO}." \
      "Check the agent can reach that repo, and that the tag exists."
  fi
  command -v brevitas >/dev/null 2>&1 \
    && BREVITAS_CMD="brevitas" \
    || BREVITAS_CMD="$PY -m brevitas"
fi

if ! $BREVITAS_CMD --version >/dev/null 2>&1; then
  die_error "brevitas was found but does not run." \
    "Try: $BREVITAS_CMD --version"
fi
log "Engine: $($BREVITAS_CMD --version 2>&1 | head -1)"

# ─────────────────────────────────────────────────────────────────────────────
# Health poll — never audit an app that is not up yet
# ─────────────────────────────────────────────────────────────────────────────
if [ "$BREVITAS_SKIP_HEALTHCHECK" != "true" ] && [ -n "$BREVITAS_HEALTH_PATH" ]; then
  command -v curl >/dev/null 2>&1 || die_error "curl not found; needed for the health check." \
    "Install curl, or set BREVITAS_SKIP_HEALTHCHECK=true to bypass it."

  HEALTH_URL="${BREVITAS_TARGET_URL%/}${BREVITAS_HEALTH_PATH}"
  log "Waiting for ${HEALTH_URL} (up to ${BREVITAS_HEALTH_TIMEOUT}s) ..."

  ELAPSED=0
  HEALTHY=false
  while [ "$ELAPSED" -lt "$BREVITAS_HEALTH_TIMEOUT" ]; do
    if curl -fsS --max-time 10 "$HEALTH_URL" >/dev/null 2>&1; then
      HEALTHY=true
      log "App healthy after ${ELAPSED}s."
      break
    fi
    sleep 5
    ELAPSED=$((ELAPSED + 5))
  done

  if [ "$HEALTHY" != true ]; then
    die_error "App never became healthy at ${HEALTH_URL} after ${BREVITAS_HEALTH_TIMEOUT}s." \
      "The audit was not run, so this is not a UI finding. Check that the
deploy really finished, that the URL is reachable from this agent, and that
${BREVITAS_HEALTH_PATH} is the right health path (set BREVITAS_HEALTH_PATH,
or BREVITAS_HEALTH_PATH='' to skip the poll)."
  fi
else
  log "Health check skipped."
fi

# ─────────────────────────────────────────────────────────────────────────────
# --check stops here
# ─────────────────────────────────────────────────────────────────────────────
if [ "$CHECK_ONLY" = true ]; then
  log "──────────────────────────────────────────────────────────"
  log "CHECK PASSED. Config valid, engine present, app reachable."
  log "Remove --check to run the audit."
  exit "$EXIT_PASS"
fi

# ─────────────────────────────────────────────────────────────────────────────
# Run the audit
# ─────────────────────────────────────────────────────────────────────────────
mkdir -p "$BREVITAS_REPORTS_DIR"

AUDIT_ARGS=(stage
  --out "$BREVITAS_REPORTS_DIR"
  --format "html,md,json,sarif,junit"
)
[ ${#FLOW_ARGS[@]} -gt 0 ] && AUDIT_ARGS+=("${FLOW_ARGS[@]}")

# The engine reads the stage contract from the environment, so export rather
# than passing flags. Keeps one source of truth for the contract.
export BREVITAS_TARGET_URL
export BREVITAS_BUILD_ID
export BREVITAS_UPSTREAM_DEPLOY
export BREVITAS_UPSTREAM_REGRESSION
export BREVITAS_REGRESSION_MODE
export BREVITAS_REPORT_BANNER="$REGRESSION_BANNER"

log "Running audit ..."
log ""

$BREVITAS_CMD "${AUDIT_ARGS[@]}"
AUDIT_EXIT=$?

log ""
log "──────────────────────────────────────────────────────────"

REPORT_DIR="${BREVITAS_REPORTS_DIR}/${BREVITAS_BUILD_ID}"
[ -d "$REPORT_DIR" ] || REPORT_DIR="${BREVITAS_REPORTS_DIR}/latest"

case "$AUDIT_EXIT" in
  0) log "UI AUDIT: PASS (no blocking findings)" ;;
  1) log "UI AUDIT: FAIL (a blocking UI regression was found)" ;;
  2) err "UI AUDIT: ERROR — the audit could not complete."
     err "This is not a UI finding. Something stopped the audit from running." ;;
  3) log "UI AUDIT: SKIPPED by the engine gate." ;;
  *) err "UI AUDIT: unexpected exit code ${AUDIT_EXIT} from the engine."
     AUDIT_EXIT="$EXIT_ERROR" ;;
esac

if [ -d "$REPORT_DIR" ]; then
  log "Report: ${REPORT_DIR}"
  [ -f "${REPORT_DIR}/report.md" ] && printf 'BREVITAS_REPORT=%s/report.md\n' "$(cd "$REPORT_DIR" && pwd)"
else
  warn "No report directory was produced. Report this as a Brevitas bug."
fi

# ─────────────────────────────────────────────────────────────────────────────
# Report-only: findings do not block. Errors still surface.
# ─────────────────────────────────────────────────────────────────────────────
if [ "$BREVITAS_REPORT_ONLY" = "true" ] && [ "$AUDIT_EXIT" = "1" ]; then
  log ""
  log "REPORT-ONLY mode: not failing the build on this finding."
  log "Set BREVITAS_REPORT_ONLY=false (or pass --gate) to let it block."
  exit "$EXIT_PASS"
fi

exit "$AUDIT_EXIT"
