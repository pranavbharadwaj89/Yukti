# API Module

`ModuleKind.Api` ("api")

## Status

Implemented in both the TS prototype (`src/modules/api/ApiModule.ts`) and
the .NET port (`Yukti.Infrastructure.InMemory/Modules/ApiModule.cs`,
`ContractVersion = "1.0.0"`). The C# comment states this is a "Direct port
of the original TS prototype's ApiModule to the formal IAutomationModule
contract."

## Purpose

Fires real HTTP requests and (in the TS version) runs declarative
assertions against status code and JSON response body, exposing the parsed
body so later flow steps can chain off it via `{{vars.<saveAs>.<field>}}`.

## Lifecycle

- `Setup` / `Teardown`: no-ops in both implementations (stateless — a
  single static `HttpClient` in C#, `fetch` per-call in TS).

## Actions

### `request`

The only supported action in both implementations. Any other action name
returns `Failed("Unknown api action '<action>'. Supported: 'request'.")`.

| Param | Type | Required | Default | TS | C# |
|---|---|---|---|---|---|
| `url` | string | yes | - | done | done |
| `method` | string | no | `GET` | done (`GET\|POST\|PUT\|PATCH\|DELETE`) | done (any string, upper-cased) |
| `headers` | object | no | `{}` | done | done (flat string->string; `Content-Type` routed onto `Content.Headers`, other content headers fall back to `Content.Headers` if `HttpRequestMessage.Headers` rejects them) |
| `queryParams` | object | no | `{}` | not present | done (flat string->string, merged onto the URL's existing query string, last value wins on duplicate keys) |
| `body` | any | no | - | done (JSON-serialized) | done (object/array -> `application/json`; plain string -> `text/plain`; an explicit `Content-Type` header always wins) |
| `timeoutMs` | number | no | `10000` | done (via `AbortController`) | done (via a linked `CancellationTokenSource.CancelAfter`, since the module's shared static `HttpClient` can't safely take a per-request `.Timeout` under concurrent flows) |
| `assert` | array | no | `[]` | done (see below) | done — full `Assertion` hierarchy (`Yukti.Domain.Assertions`), `type`-discriminated on the wire (see below) |
| `expectedStatus` | number | no | - | not present (TS uses `assert: [{status}]` instead) | done — kept as backward-compatible shorthand, translated into an implicit `{type:"status"}` assertion prepended to the `assert` list |

**Assertion wire shape (C#)** — `type`-discriminated JSON objects, mapped
1:1 onto `Yukti.Domain.Assertions.Assertion`'s four record types via
`AssertionParamMapper`:

```json
{ "assert": [
  { "type": "status", "expectedStatus": 200 },
  { "type": "pathEquals", "path": "data.id", "equals": 42 },
  { "type": "pathContains", "path": "data.items", "contains": "abc" },
  { "type": "pathExists", "path": "data.token" }
]}
```

`path` is dot-notation (with optional `[index]` array segments, e.g.
`items[0].id`) into the parsed JSON response body, evaluated by
`JsonPathEvaluator` (a minimal evaluator — no full JSONPath `$`/wildcards/
filters). All assertions in the array are checked via `AssertionEvaluator`;
every failure is collected and joined with `; ` into a single `error`
string — it does not fail fast on the first bad assertion, matching the TS
prototype's `checkAssertion` behavior. An unknown `type` or a malformed
entry raises a clean `StepOutcome.Failed` rather than an unhandled
exception.

## Response handling

Both implementations:
1. Send the request.
2. Read the body as text.
3. Attempt `JSON.parse` / `JsonSerializer.Deserialize<Dictionary<string,object?>>`;
   on failure, keep the raw text as `data` instead (never throws on
   non-JSON bodies).

## Output (`StepOutcome` / `StepResult`)

`Data` (C#) is now a richer object rather than the bare parsed body:

```json
{
  "status": 200,
  "headers": { "Content-Type": "application/json; charset=utf-8", "...": "..." },
  "body": "<parsed body (object/array/primitive) or raw text if not JSON>",
  "durationMs": 42,
  "assertionResults": [
    { "description": "status == 200", "passed": true, "error": null },
    { "description": "data.id equals 42", "passed": false, "error": "Path 'data.id' expected 42, got 41" }
  ]
}
```

`data.body` holds exactly what `Data` used to hold at the top level pre-this-change — anything chaining off a step's raw response body via
`{{vars.<saveAs>.<field>}}` needs an extra `.body` segment now
(`{{vars.<saveAs>.body.<field>}}`).

- **Success**: `status: passed`, `message: "<METHOD> <url> -> <statusCode>"`,
  `data` as above, with every `assertionResults[].passed == true` (or no
  assertions at all).
- **Assertion/status failure**: `status: failed`, `error` is every failing
  assertion's message joined with `; ` (not fail-fast), `data` still fully
  populated (so a failing step's response is still inspectable/chainable in
  reports).
- **Network/transport error** (DNS failure, non-timeout abort): `status: failed`,
  `error: <exception message>`, no `data`.
- **Timeout**: `status: failed`, `error: "Request timed out after <timeoutMs>ms"`,
  no `data`.

## Known constraints

- No retry/backoff of its own — retries are the Flow Engine's job
  (`RetryFlakeHandler`, uniform policy across all modules).
- No auth helpers (bearer token, OAuth) beyond raw `headers`.
- No response size limit — the whole body is buffered into memory.
- `queryParams` is a flat object — duplicate keys (`?tag=a&tag=b`) can't be
  represented; the last value for a given key wins.
- `body` supports JSON (object/array) and plain text only — no multipart or
  `application/x-www-form-urlencoded` support yet.
- `JsonPathEvaluator`'s `path` syntax is dotted segments plus `[index]` array
  access only — no JSONPath `$`, wildcards, or filter expressions.
