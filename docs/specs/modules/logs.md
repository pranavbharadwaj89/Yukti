# Logs Module

`ModuleKind.Logs` ("logs")

## Status

Implemented in both the TS prototype (`src/modules/logs/LogModule.ts`) and
the .NET port (`Yukti.Infrastructure.InMemory/Modules/LogsModule.cs`,
`ContractVersion = "1.0.0"`). The C# comment states this is a "Direct port
of the original TS prototype's LogModule to the formal IAutomationModule
contract."

## Purpose

Two independent capabilities over plain-text log content:

1. A declarative regex rule engine ("fail the build if ERROR appears more
   than N times") — used as a CI quality gate.
2. A lightweight statistical anomaly detector over per-minute error rates
   (z-score style: flag any minute whose error rate exceeds
   `mean + N*stdDev`).

## Lifecycle

- `Setup` / `Teardown`: no-ops in both implementations (fully stateless per
  call).

## Input handling

- **TS**: accepts either `path` (reads file via `readFile`) or `text`
  (`ParseParams`) — `path` wins if both given, falls through to `text`,
  else empty string. Lines split on `/\r?\n/`, empty lines filtered out.
- **C#**: accepts only `logText` (string) directly — no file-path
  parameter. Lines split on `'\n'` with `RemoveEmptyEntries`. This is a
  divergence: the C# port dropped the TS `path` option; a flow author
  targeting the .NET runtime must read the file into a variable upstream
  (e.g. via a future filesystem/artifact module) and pass it as `logText`.

## Actions

### `checkRules`

| Param | Type | Required | TS | C# |
|---|---|---|---|---|
| `path` / `text` | string | one of the two | present | n/a (C# has `logText` only) |
| `logText` | string | yes | n/a | present |
| `rules` | array of `{name, pattern, severity, maxAllowed?}` | yes | present | present (`severity` accepted but unused in C# logic) |

Per rule: builds a `Regex` from `pattern`, counts matching lines
(`hitLines`), records `{rule, severity(TS only), count, sampleLines/samples: first 3 matches}`
into a `matches` array regardless of pass/fail. A rule violates if
`hits.length > (rule.maxAllowed ?? 0)` — i.e. the default is zero
tolerance: any single match fails the step unless `maxAllowed` is set
explicitly. Violations across all rules are joined with `; ` into one
`error` string — every rule is still evaluated even after an earlier one
fails (no fail-fast within the action).

- Pass: `message: "<N> lines scanned, all rules within threshold"`, `data: {matches, linesScanned}`.
- Fail: `error: "rule '<name>' (<severity>): <count> matches, max allowed <maxAllowed>; ..."`, same `data` shape.

### `detectAnomalies`

| Param | Type | Required | Default | TS | C# |
|---|---|---|---|---|---|
| `path` / `text` | string | one of the two | - | present | n/a |
| `logText` | string | yes | - | n/a | present |
| `timestampPattern` | string (regex, 1 capture group) | no | `^\[?(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2})` | present | not implemented in C# (hardcoded pattern only) |
| `stdDevThreshold` | number | no | `2` | present | present (default `2.0`) |

Algorithm (identical in both):
1. Bucket every line by the captured timestamp prefix (minute-granularity,
   given the default pattern's `\d{2}:\d{2}` cutoff); lines that don't
   match the timestamp pattern go in an `"unknown"` bucket.
2. Per bucket, count `total` lines and `errors` (lines matching
   `/error|fatal|exception/i`).
3. Compute the error-rate mean and standard deviation across buckets (not
   across lines).
4. `threshold = mean + stdDevThreshold * stdDev`.
5. A bucket is anomalous if its own error rate exceeds `threshold` and
   `threshold > 0` (guards against flagging everything when the log is
   uniformly clean and stdDev collapses to 0).

- Pass: `message: "Error rate stable across <N> buckets (mean <mean%>)"`, `data: {mean, stdDev, threshold, anomalousBuckets: [], bucketsScanned}`.
- Fail: result listing bucket count that exceeded threshold, `data.anomalousBuckets: [{bucket, errorRate, total, errors}, ...]`.

Any other action name: `Failed("Unknown logs action '<action>'. Supported: checkRules, detectAnomalies.")`.

## Known constraints

- Anomaly detection has no minimum-sample-size guard — a log with very few
  buckets can produce a noisy/meaningless stdDev.
- Regex patterns are user-supplied and compiled directly (`new Regex(pattern)`)
  — no timeout or complexity guard against catastrophic backtracking
  (ReDoS) in either implementation.
- `severity` on a rule is informational only in both implementations; it
  does not change pass/fail behavior (only `maxAllowed` does).
