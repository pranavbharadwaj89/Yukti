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
| `headers` | object | no | `{}` | done | not implemented in C# |
| `body` | any | no | - | done (JSON-serialized) | not implemented in C# |
| `timeoutMs` | number | no | `10000` | done (via `AbortController`) | no timeout in C# |
| `assert` | array | no | `[]` | done (see below) | replaced by `expectedStatus` (single status-code check only) |
| `expectedStatus` | number | no | - | not present (TS uses `assert: [{status}]` instead) | done |

**Divergence to close when porting further**: the C# port currently only
checks a single `expectedStatus`. The TS `assert` array supports three
richer assertion shapes not yet in C#:

```ts
type Assertion =
  | { status: number }
  | { path: string; equals: unknown }
  | { path: string; contains: unknown }
  | { path: string; exists: true };
```

`path` is dot-notation into the parsed JSON body (`getByPath`). All
assertions in the array are checked; every failure is collected and
joined with `; ` into a single `error` string (TS `checkAssertion`,
`ApiModule.ts` lines 25-42, 73-81) — it does not fail fast on the first
bad assertion.

## Response handling

Both implementations:
1. Send the request.
2. Read the body as text.
3. Attempt `JSON.parse` / `JsonSerializer.Deserialize<Dictionary<string,object?>>`;
   on failure, keep the raw text as `data` instead (never throws on
   non-JSON bodies).

## Output (`StepOutcome` / `StepResult`)

- **Success**: `status: passed`, `message: "<METHOD> <url> -> <statusCode>"`,
  `data: <parsed body or raw text>`.
- **Assertion/status failure**: `status: failed`, `error` describing the
  mismatch(es), `data` still populated with the parsed body (so a failing
  step's response is still inspectable/chainable in reports).
- **Network/transport error** (DNS failure, timeout, abort): `status: failed`,
  `error: <exception message>`, no `data`.

## Known constraints

- No retry/backoff of its own — retries are the Flow Engine's job
  (`RetryFlakeHandler`, uniform policy across all modules).
- No auth helpers (bearer token, OAuth) beyond raw `headers` (TS only).
- No response size limit — the whole body is buffered into memory.
