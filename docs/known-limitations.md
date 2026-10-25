# Known limitations

Plain, honest statements of what v0.1 doesn't do yet — not a roadmap, not a sales page. If a
limitation here feels embarrassing to say directly to a design partner, it's release-blocking, not
a known limitation (the standard Day 7's own gap report uses).

## Secrets in flow steps

v0.1 has no secrets syntax. Credentials placed in flow steps are redacted from reports, logs and
JUnit output, but are stored in the flow definition itself. Do not use v0.1 with production
credentials. Vault-backed `{{secrets}}` arrive in v0.2.
