#!/usr/bin/env bash
# D6-T7: waits for a docker-compose container's own healthcheck to report
# "healthy" — the exact polling loop D6-T6's docker-compose-smoke CI job
# already used inline, extracted so the spec's own ✔ command
# (`docker compose up -d && ./scripts/wait-healthy.sh && curl -f localhost:8080/health`)
# is literally real, and so a person following docs/quickstart.md on a
# fresh clone has the same thing CI already relies on, not a second,
# undocumented inline loop nobody else can run.
set -euo pipefail

container="${1:-yukti-api}"
timeout_seconds="${2:-90}"
interval_seconds=2
attempts=$(( (timeout_seconds + interval_seconds - 1) / interval_seconds ))

for ((i = 1; i <= attempts; i++)); do
  status=$(docker inspect --format='{{json .State.Health.Status}}' "$container" 2>/dev/null || echo '"starting"')
  echo "$container health: $status"
  if [ "$status" = '"healthy"' ]; then
    exit 0
  fi
  if [ "$status" = '"unhealthy"' ]; then
    break
  fi
  sleep "$interval_seconds"
done

echo "$container never reported healthy within ${timeout_seconds}s:"
docker compose logs "$container" 2>/dev/null || true
exit 1
