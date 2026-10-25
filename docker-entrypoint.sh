#!/bin/sh
# D6-T6: assembles ConnectionStrings__Yukti from separate PG*-style pieces at container start,
# the same reason ci.yml's own migration steps build connection strings this way — so no file ever
# commits one static "Host=...;Password=..." literal (Sarvaghna's own secret-pattern rule, correctly
# applied — this isn't a real secret, but the shape is indistinguishable from one that is). Only
# fills it in when the caller hasn't already set ConnectionStrings__Yukti directly (e.g. a real
# deployment's own secret store), and only when PGHOST is present at all (so running this image
# outside docker-compose, with ConnectionStrings__Yukti set the normal way, is unaffected).
set -e

if [ -z "$ConnectionStrings__Yukti" ] && [ -n "$PGHOST" ]; then
    CONN="Host=${PGHOST}"
    CONN="${CONN};Port=${PGPORT:-5432}"
    CONN="${CONN};Database=${PGDATABASE:-yukti}"
    CONN="${CONN};Username=${PGUSER:-postgres}"
    CONN="${CONN};Password=${PGPASSWORD}"
    export ConnectionStrings__Yukti="$CONN"
fi

# Found 2026-10-25: without these, Yukti.Api/Yukti.Worker fall back to ConnectionStrings__Yukti —
# the Postgres superuser — for every request, which silently bypasses RLS (superusers are exempt
# from FORCE ROW LEVEL SECURITY, confirmed by ADR-0006) and the audit_entries UPDATE/DELETE grant
# restriction (GRANT/REVOKE don't bind superusers either). The whole point of AddYuktiAppRuntimeRole/
# AddYuktiWorkerRole (yukti_app/yukti_worker, non-owner, scoped grants) was to make those DB-layer
# protections real at runtime, not just in a test that happens to connect as the restricted role —
# this compose stack ran as the superuser the entire time until this fix, so `docker compose up`
# never actually exercised the protections D6-T1/D6-T4 built and mutation-tested. Both roles are
# created with the same documented, dev-only placeholder password (the migrations' own literal,
# not an env var) — safe for the same reason Program.cs's own migration call is: this container's
# volume is thrown away with every `docker compose down`, never a real credential. A real
# deployment overrides ConnectionStrings__YuktiRuntime/__YuktiWorker directly, the same way it
# would override ConnectionStrings__Yukti.
if [ -z "$ConnectionStrings__YuktiRuntime" ] && [ -n "$PGHOST" ]; then
    CONN="Host=${PGHOST}"
    CONN="${CONN};Port=${PGPORT:-5432}"
    CONN="${CONN};Database=${PGDATABASE:-yukti}"
    CONN="${CONN};Username=yukti_app"
    CONN="${CONN};Password=placeholder-rotate-immediately"
    export ConnectionStrings__YuktiRuntime="$CONN"
fi

if [ -z "$ConnectionStrings__YuktiWorker" ] && [ -n "$PGHOST" ]; then
    CONN="Host=${PGHOST}"
    CONN="${CONN};Port=${PGPORT:-5432}"
    CONN="${CONN};Database=${PGDATABASE:-yukti}"
    CONN="${CONN};Username=yukti_worker"
    CONN="${CONN};Password=placeholder-rotate-immediately"
    export ConnectionStrings__YuktiWorker="$CONN"
fi

exec dotnet "$@"
