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

- `Setup` / `Teardown`: no-ops in the TS implementation. In C# (API-T3),
  `Setup` creates a fresh per-`FlowRunId` cookie jar and `Teardown` removes
  it — the only per-run state this module holds, kept off the module
  instance itself (a static `ConditionalWeakTable`, same shape as the
  shared `HttpClient`'s own), so `ModulePluginContractTests`'
  zero-reflected-instance-fields check still holds.

## Actions

### `request`

The only supported action in both implementations. Any other action name
returns `Failed("Unknown api action '<action>'. Supported: 'request'.")`.

| Param | Type | Required | Default | TS | C# |
|---|---|---|---|---|---|
| `url` | string | yes | - | done | done |
| `method` | string | no | `GET` | done (`GET\|POST\|PUT\|PATCH\|DELETE`) | done (any string, upper-cased) |
| `headers` | object | no | `{}` | done | done (flat string->string; `Content-Type` routed onto `Content.Headers`, other content headers fall back to `Content.Headers` if `HttpRequestMessage.Headers` rejects them) |
| `pathParams` | object | no | `{}` | not present | done (API-T2) — resolves `{placeholder}` segments in `url`; every value is `Uri.EscapeDataString`-encoded so it can never add a path segment or query string of its own |
| `queryParams` | object | no | `{}` | not present | done — a scalar value replaces the URL's own value(s) for that key (last-value-wins); an array value (API-T2) appends the key once per entry, in order, so repeated keys (`?tag=a&tag=b`) and empty values (`?tag=`) are both expressible |
| `body` | any | no | - | done (JSON-serialized) | done — object/array -> `application/json`, plain string -> `text/plain` (an explicit `Content-Type` header always wins for either). API-T3 adds `{parts:[...]}` -> `multipart/form-data` (shape-triggered, header ignored — see below) and a flat object + a `Content-Type: application/x-www-form-urlencoded` header -> form-encoded. |
| `cookies` | object | no | `{}` | not present | done (API-T3) — name->value, merged into the flow run's own cookie jar (see Lifecycle); sent on this request and remembered for later steps in the same run |
| `redirectPolicy` | string | no | `follow` | not present | done (API-T3) — `follow` \| `none` \| `sameHost`. Implemented as a manual redirect loop (`SocketsHttpHandler.AllowAutoRedirect = false`), so every hop re-enters `ApiModule`'s own `ConnectCallback` and thus the SSRF guard, and so cookies/headers are correctly re-applied per hop. |
| `timeoutMs` | number | no | `10000` | done (via `AbortController`) | done (via a linked `CancellationTokenSource.CancelAfter`, since the module's shared static `HttpClient` can't safely take a per-request `.Timeout` under concurrent flows) — the budget covers the whole request including any redirects it follows |
| `assert` | array | no | `[]` | done (see below) | done — full `Assertion` hierarchy (`Yukti.Domain.Assertions`), `type`-discriminated on the wire (see below) |
| `expectedStatus` | number | no | - | not present (TS uses `assert: [{status}]` instead) | done — kept as backward-compatible shorthand, translated into an implicit `{type:"status"}` assertion prepended to the `assert` list |
| `extract` | array | no | `[]` | not present | done (API-T6) — see "Extraction" below |

**Assertion wire shape (C#)** — `type`-discriminated JSON objects, mapped
1:1 onto `Yukti.Domain.Assertions.Assertion`'s record types via
`AssertionParamMapper`:

```json
{ "assert": [
  { "type": "status", "expectedStatus": 200 },
  { "type": "statusIn", "expectedStatuses": [200, 201, 202] },
  { "type": "pathEquals", "path": "data.id", "equals": 42 },
  { "type": "pathContains", "path": "data.items", "contains": "abc" },
  { "type": "pathExists", "path": "data.token" },
  { "type": "pathCount", "path": "data.items", "expectedCount": 3 },
  { "type": "headerExists", "header": "X-Request-Id" },
  { "type": "headerEquals", "header": "Content-Type", "equals": "application/json" },
  { "type": "headerContains", "header": "Content-Disposition", "contains": "report.pdf" },
  { "type": "cookieExists", "cookie": "session" },
  { "type": "schema", "schema": { "type": "object", "required": ["id"], "properties": { "id": { "type": "number" } } } },
  { "type": "responseTimeUnder", "maxMs": 2000 },
  { "type": "bodyMatchesPattern", "pattern": "^order-\\d+$" }
]}
```

API Testing Module Spec, API-T7 additions: `statusIn` (status in an allowed set), `pathCount`
(an array's length, or 1 for a present scalar/object), `headerEquals`/`headerContains` (exact/
substring header value match, as distinct from `headerExists`'s presence-only check),
`responseTimeUnder` (the request's own measured duration, excluding Yukti's overhead) and
`bodyMatchesPattern` (a regex against the raw response text — a 2s match timeout and a clean
failure on an invalid pattern, never an unhandled exception).

`path` is dot-notation (with optional `[index]` array segments, e.g.
`items[0].id`) into the parsed JSON response body, evaluated by
`JsonPathEvaluator` (a minimal evaluator — no full JSONPath `$`/wildcards/
filters). `header`/`cookie` are matched case-insensitively against the
response's headers and `Set-Cookie` cookie names. `schema` (API-T8, ADR-0007)
is a real JSON Schema (draft 2020-12) validated by `JsonSchema.Net`, either
inline as a JSON object, or a string naming a reusable schema artifact
loaded from `ApiSchemaStore` (see "Schema validation" below) — every
violation is listed with its own instance path, not just the first. All
assertions in the array are checked via `AssertionEvaluator`; every failure
is collected and joined with `; ` into a single `error` string — it does
not fail fast on the first bad assertion, matching the TS prototype's
`checkAssertion` behavior. An unknown `type` or a malformed entry raises a
clean `StepOutcome.Failed` rather than an unhandled exception.

## Schema validation (API-T8, ADR-0007)

`JsonSchemaValidator` wraps `JsonSchema.Net` (draft 2020-12) — `$ref`,
`oneOf`/`anyOf`/`allOf`/`not`, `pattern`, `format`, and numeric/string
length bounds all work, unlike the retired validation path. Evaluated with
`OutputFormat.List`; every leaf result with errors becomes one `<path>:
<message>` entry (`$` for the root), joined with `; ` into the assertion's
single `error` string — the same multi-failure join convention as
`assertionResults` and every other collected-failures case in this module.

A `schema` value that's a JSON object is used inline. A `schema` value
that's a *string* names a schema file in `ApiSchemaStore`'s configured
directory (`YUKTI_API_SCHEMAS_DIR` env var, default `<app base dir>/api-
schemas`, looked up as `<name>.json`) — the reusable-artifact half of
API-T8. Resolved at `assert`-parse time (`AssertionParamMapper`), so an
unknown reference fails the step naming it, same as a missing multipart
file. Like `ApiFileStorage` (API-T3), this directory is shared across
tenants, not yet scoped to a run or tenant — there's no upload mechanism to
either module to hang that scoping from. Treat it as operator-managed
fixtures, a real documented gap, not a security boundary.

`MinimalJsonSchemaValidator` (the hand-rolled subset) is still in the
codebase but no longer used by any assertion — left in place rather than
deleted, since removing working, documented, tested code that isn't
actually in the way is unnecessary churn.

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

## Extraction (API-T6)

`extract` is an array of `{saveAs, source, path?, xpath?, required?}`, evaluated
**before** `assert` (so a bad extraction — almost always the author's own typo —
is diagnosed on its own terms rather than buried under unrelated assertion
failures from the same response). Every entry writes into this step's own
`data.extracted.<saveAs>`:

```json
{ "extract": [
  { "saveAs": "orderId", "source": "body", "path": "data.orders[0].id" },
  { "saveAs": "requestId", "source": "header", "path": "X-Request-Id" },
  { "saveAs": "code", "source": "status" },
  { "saveAs": "elapsed", "source": "responseTime" },
  { "saveAs": "sessionCookie", "source": "cookie", "path": "session" },
  { "saveAs": "legacyId", "source": "body", "xpath": "/order/id" },
  { "saveAs": "optional", "source": "body", "path": "data.maybe", "required": false }
]}
```

Reached from a later step via `{{vars.<step>.extracted.<saveAs>}}` —
**deliberately not** a flat top-level `{{vars.<saveAs>}}`. This is a narrower
reading than the spec's own illustrative wording, chosen because the
existing publish-time validator (`Flow.cs`) only ever checks a
`{{vars.x...}}` reference's *first* segment against a step's own declared
`SaveAs` name; nesting under that satisfies "reuse the existing validator,
never write a second one" with zero engine or domain change, exactly as
required.

- `source: body` (the default) evaluates `path` as a dotted
  JSONPath-subset path via `JsonPathEvaluator` (same evaluator `assert`
  uses), or `xpath` via `System.Xml.XPath` against the raw response text
  when the response is XML. `path` and `xpath` are mutually exclusive.
- `source: header` / `cookie` match `path` case-insensitively against the
  response's headers / the cookie jar (after this response's own
  `Set-Cookie` has already been folded in).
- `source: status` / `responseTime` need no `path` at all.
- A required (`required` defaults to `true`) extraction that finds nothing
  fails the step, naming the `saveAs` and the `source:path`/`xpath`
  locator, plus a body excerpt truncated to 300 characters — never a silent
  empty write. `required: false` writes `null` instead.
- A value keeps its parsed type (`JsonPathEvaluator.ToPlainValue` — number,
  string, bool, null, object, array), so `{{vars.<step>.extracted.order.customer.name}}`
  works for an extracted object exactly like the body itself does.
- A malformed XML document or a non-matching XPath expression are both
  "not found" — never an unhandled exception.

## Multipart file parts (API-T3)

A `body.parts` entry with a `fileName` field reads that file from
`ApiFileStorage`'s configured directory (`YUKTI_API_FILES_DIR` env var,
default `<app base dir>/api-files`) — **never an arbitrary filesystem
path**. Only the bare file name survives (`Path.GetFileName`), so a value
like `../../secrets.txt` can't escape that directory; a missing file fails
the step naming the file, before any request is sent. This directory is
intentionally not per-run or per-tenant like `ScreenshotStorage`'s own
directories: those are written by the system after a run starts (a RunId
already exists to scope them by), but a multipart test fixture must exist
*before* the run that references it even starts, and there is no
upload-to-this-module mechanism yet to stage one per-run. Until one
exists, treat this directory as shared, operator-managed test fixtures,
not tenant data — a real gap, not a hidden one.

## Known constraints

- No retry/backoff of its own — retries are the Flow Engine's job
  (`RetryFlakeHandler`, uniform policy across all modules).
- No auth helpers (bearer token, OAuth) beyond raw `headers` (API-T4/T5 —
  blocked on the `{{secrets}}` pipeline, deferred to v0.2).
- No response size limit — the whole body is buffered into memory.
- The multipart file-parts directory is shared across tenants (see above)
  — not yet a security boundary, just a fixtures folder.
- `JsonPathEvaluator`'s `path` syntax is dotted segments plus `[index]` array
  access only — no JSONPath `$`, wildcards, or filter expressions. (The
  `schema` assertion's own JSON Schema validation no longer shares this
  limitation — see "Schema validation" above.)
- The named-schema-reference directory (`ApiSchemaStore`) is shared across
  tenants, same gap as the multipart file-parts directory above.
