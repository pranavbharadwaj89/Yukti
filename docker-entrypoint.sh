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

exec dotnet "$@"
