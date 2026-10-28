# Yukti — API Testing Module Spec

REST Assured-class capability · Oct 27, 2026

## Scope and how to use this spec

**For Sutradhar.** This is one of four module specs. It is self-contained: everything needed to build the API module to REST Assured-class capability is here. Work tasks in the order given. A task is done only when its ✔ command passes and every checkbox is demonstrably met. Standard autonomy policy applies — Builder, Verifier, Reviewer, evidence in PRs.

**The governing rule for every task here:** nothing in this spec changes `IAutomationModule`, the flow engine, the registry, the schema-driven UI, the report model or the CLI. Every capability is a new action, a new parameter, or a new assertion kind on the existing API module. If a task appears to require module-specific code anywhere above the module, **stop and escalate** — the design is wrong.

### What exists today

| Capability | State |
| --- | --- |
| `request` action: method, URL, headers, body, timeout | Working, tested (`ApiModuleAssertionTests`) |
| Assertions: status, path equals, path contains, path exists | Working |
| Header redaction of sensitive values | Working (Day 6, T3a) |
| SSRF outbound guard | Working (Day 6, T5) — applies to every request this module makes |

### What this spec adds

Path and query parameters, form and multipart bodies, cookies, the full authentication set including OAuth 2.0, JSONPath and XPath extraction into variables, an expanded assertion set, JSON Schema validation, and reusable request templates.

### Dependencies

- [ ] **Blocking:** the `{{secrets}}` resolution pipeline (deferred from Day 6, scheduled in v0.2). Authentication tasks API-T4 and API-T5 cannot be completed without it — credentials must never be typed into a step. Tasks T1–T3 and T6–T9 have no such dependency and can proceed first.
- [ ] Non-blocking: the Object Repository (Web module spec) is **not** required here. Request templates (API-T9) are the API equivalent and are specified independently.

### The mapping that validates this design

REST Assured's structure maps onto Yukti's step shape with no translation loss, which is a useful check that the model is right:

| REST Assured | Yukti |
| --- | --- |
| `given()` — headers, auth, params, body | Step parameters |
| `when()` — method and endpoint | The `request` action's method and URL |
| `then()` — assertions | The step's assertions list |
| `extract().path()` | `saveAs` plus a JSONPath extraction (API-T6) |

## Domain model

Build these types first. Everything after depends on them. Shapes are illustrative, not literal — match the codebase's existing conventions for naming and nullability.

```csharp
// The full request definition a step carries
public sealed record ApiRequestSpec(
    HttpMethodKind   Method,
    string           UrlTemplate,        // may contain {pathParam} placeholders
    IReadOnlyDictionary<string, string>? PathParams,
    IReadOnlyList<QueryParam>?           QueryParams,
    IReadOnlyDictionary<string, string>? Headers,
    RequestBody?     Body,
    AuthSpec?        Auth,
    IReadOnlyList<CookieSpec>? Cookies,
    TimeSpan?        Timeout,
    RedirectPolicy   Redirects);          // Follow | None | FollowSameHost

// Bodies are a closed union — never a loose string plus a content-type guess
public abstract record RequestBody;
public sealed record JsonBody(string Json)                     : RequestBody;
public sealed record TextBody(string Text, string ContentType) : RequestBody;
public sealed record FormBody(IReadOnlyDictionary<string,string> Fields) : RequestBody;
public sealed record MultipartBody(IReadOnlyList<MultipartPart> Parts)   : RequestBody;
public sealed record NoBody                                     : RequestBody;

// Auth is a closed union — adding a scheme is a new case, never a flag combination
public abstract record AuthSpec;
public sealed record BasicAuth(SecretRef User, SecretRef Password)        : AuthSpec;
public sealed record BearerAuth(SecretRef Token)                          : AuthSpec;
public sealed record ApiKeyAuth(string Name, SecretRef Value, ApiKeyIn In): AuthSpec;
public sealed record OAuthClientCredentials(
    string TokenUrl, SecretRef ClientId, SecretRef ClientSecret,
    IReadOnlyList<string>? Scopes)                                        : AuthSpec;

// Extraction: run after a response, writes into flow variables
public sealed record ExtractionSpec(
    string     SaveAs,
    ExtractSource Source,      // Body | Header | Status | ResponseTime | Cookie
    string?    Path,            // JSONPath or XPath, when Source is Body
    bool       Required);       // true (default) → missing path fails the step

// Assertions: a closed hierarchy, matching the existing pattern
public abstract record ApiAssertion;
public sealed record StatusEquals(int Expected)                     : ApiAssertion;
public sealed record StatusIn(IReadOnlyList<int> Expected)          : ApiAssertion;
public sealed record JsonPathEquals(string Path, object? Expected)  : ApiAssertion;
public sealed record JsonPathContains(string Path, object Fragment) : ApiAssertion;
public sealed record JsonPathExists(string Path)                    : ApiAssertion;
public sealed record JsonPathCount(string Path, int Expected)       : ApiAssertion;
public sealed record HeaderEquals(string Name, string Expected)     : ApiAssertion;
public sealed record HeaderContains(string Name, string Fragment)   : ApiAssertion;
public sealed record ResponseTimeUnder(TimeSpan Max)                : ApiAssertion;
public sealed record BodyMatchesPattern(string Regex)               : ApiAssertion;
public sealed record MatchesJsonSchema(string SchemaRefOrInline)    : ApiAssertion;
```

### Three rules about these types

1. **`SecretRef` is never a string.** It's a reference the secrets pipeline resolves at run time. A plain string credential must be impossible to pass, so the compiler prevents the mistake rather than a review catching it.
2. **Closed unions, not flags.** `RequestBody` and `AuthSpec` are discriminated unions so an invalid combination can't be constructed. This matches the existing `Assertion` pattern in the codebase.
3. **Every type is serializable to and from the flow JSON**, and every one appears in the module's `ActionSchema` with a non-empty description, so the schema-driven UI renders it without any UI change.

## Tasks — request building and authentication

### API-T1 — Domain model and schema (P0)

- [ ] Every type from the domain model section exists, with `mypy`-equivalent strictness: nullable reference types on, no `object` where a union will do.
- [ ] `supported_actions()` returns the `request` action with the full parameter schema for all new fields. Every parameter and every union case has a non-empty description.
- [ ] A round-trip test: serialize a fully-populated `ApiRequestSpec` to flow JSON and back, and assert equality. This catches schema drift before it reaches the UI.
- [ ] The existing `ApiModuleAssertionTests` still pass unchanged.

✔ `dotnet test --filter "FullyQualifiedName~ApiModel"`

Time-box: 2 h.

### API-T2 — Path and query parameters (P0) — ✅ done, merged

- [x] A URL template `/orders/{orderId}/items` with `PathParams: { orderId: "123" }` resolves to `/orders/123/items`.
- [x] Path parameter values are URL-encoded. A value containing `/` or `?` cannot break out of its segment — test this explicitly with `../` and `?x=1`.
- [x] A template placeholder with no matching parameter fails the step before the request is sent, naming the placeholder. It is never sent literally.
- [x] A supplied parameter with no matching placeholder is also an error, since it's almost always a typo.
- [x] Query parameters are appended and properly encoded, including repeated keys (`?tag=a&tag=b`) and empty values.
- [x] Both support `{{vars.…}}` interpolation, resolved before encoding. (Resolved by `FlowEngine`/`VariableStore` before dispatch — `ApiModule` itself never re-implements interpolation; proven by `PathParams_and_queryParams_both_receive_already_resolved_vars_values`.)

✔ `dotnet test --filter "FullyQualifiedName~ApiParameters"`

Time-box: 2 h.

### API-T3 — Body types and cookies (P0) — ✅ done, merged

- [x] `JsonBody` sets `Content-Type: application/json` automatically unless a header overrides it.
- [x] `FormBody` encodes as `application/x-www-form-urlencoded`. (Triggered by a flat object body + an explicit `Content-Type: application/x-www-form-urlencoded` header — see docs/specs/modules/api.md.)
- [x] `MultipartBody` supports text parts and file parts. A file part reads from a path the flow supplies; a missing file fails the step with the path named. (Deliberately scoped to a configured directory, never an arbitrary filesystem path — a documented, narrower reading of "a path the flow supplies" than the spec's own illustrative wording, to avoid a real file-disclosure hole. See docs/specs/modules/api.md's "Multipart file parts" section.)
- [x] Cookies set on a step are sent. A **cookie jar** persists cookies across steps within one flow run, so a login step's session cookie reaches the next step automatically.
- [x] The cookie jar is scoped to the flow run only — never shared between runs or tenants. A test proves two concurrent runs don't see each other's cookies. (Found and fixed a real pre-existing bug along the way: `SocketsHttpHandler.UseCookies` defaulted to true with no explicit container, so the shared static `HttpClient` was silently using ONE implicit cookie store across every run that ever hit the same host — a real cross-run leak that predates this task.)
- [x] Redirect policy is honoured, and **every redirect hop passes through the SSRF guard** (Day 6, T5). A redirect to a private address is blocked. Test this specifically.

✔ `dotnet test --filter "FullyQualifiedName~ApiBody|FullyQualifiedName~ApiCookie"`

Time-box: 3 h.

### API-T4 — Basic, Bearer and API key auth (P0, needs secrets pipeline)

- [ ] All three schemes work, with credentials resolved from `SecretRef` at request time.
- [ ] `ApiKeyAuth` supports placement in a header, a query parameter or a cookie.
- [ ] **Leak test:** a sentinel credential is used in each scheme; the sentinel appears in no report, no JUnit file, no log, no error message and no audit entry. This reuses Day 6's sentinel scan.
- [ ] A credential that fails to resolve (missing secret) fails the step with the secret's *name*, never its value, and never falls back to sending nothing.
- [ ] **Mutation check:** disable the redaction path, show the leak test goes red, restore, show it green. Evidence in the PR.

✔ `dotnet test --filter "FullyQualifiedName~ApiAuth"`

Time-box: 3 h.

### API-T5 — OAuth 2.0 client credentials (P0, needs secrets pipeline)

The single biggest current gap. Without it, every authenticated API suite needs a manual token step that expires and breaks.

- [ ] Yukti fetches the token itself from `TokenUrl` using the client ID and secret, and attaches it as a bearer token.
- [ ] The token is **cached for its lifetime** (from the response's `expires_in`, minus a safety margin of 60 seconds) and reused across steps and across flows within the same tenant and configuration.
- [ ] Expiry triggers an automatic refresh, transparently to the flow.
- [ ] A token fetch failure fails the step with the provider's error description included, so a misconfigured client is diagnosable.
- [ ] The token value never appears in any output, same scan as T4.
- [ ] The cache key includes the tenant, so two tenants with the same token URL never share a token. **This is a tenant-isolation concern — test it explicitly.**
- [ ] A **custom auth step** escape hatch exists: call an endpoint, extract a value with T6's extraction, use it via `{{vars}}` in later steps. This covers schemes Yukti doesn't implement.

✔ `dotnet test --filter "FullyQualifiedName~OAuth"`

Time-box: 4 h.

## Tasks — extraction, assertions, templates

### API-T6 — Extraction into variables (P0) — ✅ done, merged

This is what makes multi-step API flows possible. REST Assured's `extract().path()`.

- [x] JSONPath extraction from the response body: `$.data.orders[0].id` saved as a named variable, usable by any later step via `{{vars.name}}`. (Addressed as `{{vars.<step>.extracted.name}}`, nested under the step's own `saveAs` rather than a flat top-level name — see docs/specs/modules/api.md's "Extraction" section for why this satisfies the validator-reuse rule below with zero engine change.)
- [x] Extraction from a **header**, the **status code**, the **response time** and a **cookie**, not just the body.
- [x] Several extractions per step, each with its own `saveAs`.
- [x] A path that matches nothing **fails the step** by default, naming the path and showing a truncated response body. It never writes an empty variable silently — that produces a confusing failure three steps later instead of a clear one here.
- [x] `Required: false` allows an optional extraction that writes null when absent, for genuinely optional fields.
- [x] XPath extraction for XML responses, same rules.
- [x] Extracted values keep their type: a JSON number stays a number, an object stays an object, so a later step can reference `{{vars.order.customer.name}}`.
- [x] Publish-time validation: a step referencing `{{vars.x}}` where no earlier step saves `x` is rejected with the existing 422 behaviour. **Reuse the existing validator — do not write a second one.** (Zero new validation code: `Flow.cs`'s existing check only inspects a `{{vars.x...}}` reference's first segment against declared `SaveAs` names, which the nested-under-SaveAs addressing above already satisfies untouched.)

✔ `dotnet test --filter "FullyQualifiedName~ApiExtraction"`

Time-box: 3 h.

### API-T7 — The expanded assertion set (P0)

- [ ] Every assertion type from the domain model works, with the existing four unchanged in behaviour so current flows keep passing.
- [ ] Each failure reports **expected, actual, and the path or header involved**, plus a truncated response body. An assertion failure a tester can't diagnose from the report is a failed assertion implementation.
- [ ] `ResponseTimeUnder` measures the full request-to-response time and excludes Yukti's own overhead, so it means what a user thinks it means.
- [ ] Numeric comparison handles JSON number types sensibly: `1` equals `1.0`, and a string `"1"` does **not** equal a number `1`. Test both explicitly — loose equality here causes false passes.
- [ ] **Soft assertions (P1):** an assertion marked non-fatal collects its failure and lets the step continue, reporting every failure together. The report model already supports multiple failures per step, so this is cheap and genuinely useful.

✔ `dotnet test --filter "FullyQualifiedName~ApiAssertion"`

Time-box: 3 h.

### API-T8 — JSON Schema validation (P0) — ✅ done, merged

- [x] `MatchesJsonSchema` validates the whole response against a JSON Schema (draft 2020-12 or whichever the chosen library supports — record the choice in the PR). (The existing `schema` assertion type — spec's illustrative `MatchesJsonSchema` name not literally introduced, per the fixed-vocabulary rule.) Library: `JsonSchema.Net` 9.5.0 — ADR-0007.
- [x] A schema can be inline in the step, or a reference to a stored, reusable schema artifact. (A string `schema` value names a file in `ApiSchemaStore`'s configured directory — same shared-directory pattern as API-T3's `ApiFileStorage`, documented as a known, not-yet-tenant-scoped gap.)
- [x] A validation failure lists **every** violation with its path, not just the first.
- [x] One schema assertion replaces many field assertions, and catches shape changes a field-by-field test would miss — that's the reason it's P0 rather than a nicety.
- [x] Adding the schema library needs an ADR and your approval, per the dependency rule. (Asked via AskUserQuestion; owner chose JsonSchema.Net over extending the in-tree validator — ADR-0007.)

✔ `dotnet test --filter "FullyQualifiedName~JsonSchema"`

Time-box: 2 h.

### API-T9 — Request templates (P1) — ⏸️ deferred to v0.2, see plan/PLAN.md DISC-19

The API equivalent of the Web module's Object Repository, and the same maintenance argument applies.

**Escalated, not built:** "updates every flow that uses it, with no flow edits" needs real runtime
template resolution, which requires module-specific code above `ApiModule` itself (`FlowEngine` or
a new `ExecutionContext` capability) — exactly what this spec's own governing rule says to stop
and escalate for. No existing precedent to mirror either: `ApiCollection`/`ApiRequest` are a
separate, non-publishing "API Studio" context with no runtime resolution into `Flow`/`FlowStep`,
and the Web module's own "Object Repository" doesn't exist in the code at all. Asked the owner via
AskUserQuestion; chose to defer to v0.2 over approving the engine change or a weaker publish-time-
only version. Full reasoning in `plan/PLAN.md`'s DISC-19.

- [ ] A **request template** is a versioned, tenant-scoped artifact holding a base URL, common headers, an auth spec and a default timeout.
- [ ] A step references a template and overrides any field. Changing the template updates every flow that uses it, with no flow edits — proven with three flows and one base-URL change.
- [ ] Publish validation fails naming the step when a referenced template is missing.
- [ ] The UI shows which flows use a template, so the blast radius is visible before editing.

✔ `dotnet test --filter "FullyQualifiedName~RequestTemplate"`

Time-box: 3 h.

### API-T10 — Importers (P1)

Direct adoption levers. Both were identified in the original blueprint as switching-cost reducers.

- [ ] **OpenAPI import:** read a specification and generate flow steps for selected endpoints, with parameters and a schema assertion pre-filled.
- [ ] **Postman collection import:** convert a v2.1 collection into API steps, including auth, headers and body. Variables map to Yukti variables.
- [ ] Anything that can't be converted is reported clearly rather than silently dropped. A partial import that looks complete is worse than a refused one.

✔ `dotnet test --filter "FullyQualifiedName~ApiImport"`

Time-box: 4 h.

## Safety and escalation

### Rules Sutradhar must not break

- **Never send a credential as a plain string.** `SecretRef` only. If a task seems to need a raw credential, the secrets pipeline isn't ready and the task is blocked, not worked around.
- **Never bypass the SSRF guard.** Every request, every redirect hop, every OAuth token fetch goes through it. A "just for testing" bypass is an escalation, not a shortcut.
- **Never weaken the redaction tests** to make a feature pass. A leaked sentinel is a release blocker.
- **Never write a second validator.** Publish validation, variable resolution and schema checking each have exactly one implementation already. Reuse them.
- **Never let an extraction fail silently.** A missing path fails the step. This rule exists because the alternative produces bugs that surface three steps later.

### Escalation triggers specific to this module

1. A task requires changes to the SSRF guard, the secrets pipeline, or any auth, RBAC, RLS or audit code.
2. A new NuGet dependency is needed (JSON Schema library, OAuth helper) — ADR plus approval first.
3. The OAuth token cache would need to be shared across tenants to work — stop; that's a tenant-isolation violation and the design needs rethinking.
4. A leak test fails and the fix isn't obvious within one attempt.
5. Any task appears to need module-specific code in the engine, UI or report.

## Sequence, gate and metrics

| Order | Task | Needs secrets pipeline? | Time-box |
| --- | --- | --- | --- |
| 1 | API-T1 Domain model and schema | No | 2 h |
| 2 | API-T2 Path and query parameters | No | 2 h |
| 3 | API-T3 Bodies and cookies | No | 3 h |
| 4 | API-T6 Extraction | No | 3 h |
| 5 | API-T7 Assertions | No | 3 h |
| 6 | API-T8 JSON Schema | No | 2 h |
| 7 | API-T4 Basic, Bearer, API key | **Yes** | 3 h |
| 8 | API-T5 OAuth client credentials | **Yes** | 4 h |
| 9 | API-T9 Request templates (P1) | No | 3 h |
| 10 | API-T10 Importers (P1) | No | 4 h |

Non-auth tasks come first deliberately, so the module is usable even if the secrets pipeline slips. Roughly **22 hours of P0 work**, about 3 working days.

### Module gate

All of these must pass before the API module is considered complete:

```
dotnet test --filter "FullyQualifiedName~Api"          # every task's tests
yukti run demo/api_oauth_chain.json                     # see below
dotnet test --filter "FullyQualifiedName~SecretLeak"   # no credential in any output
dotnet test --filter "FullyQualifiedName~ModuleParity" # contract untouched
```

`demo/api_oauth_chain.json` is the acceptance flow and must exercise the whole module in one go: **fetch an OAuth token → create a resource with a JSON body → extract its ID with JSONPath → fetch it back using that ID as a path parameter → assert status, a JSONPath value, the response time, and the whole body against a JSON Schema.** If that flow passes, the module is REST Assured-class in practice, not just on paper.

### Metrics

| Metric | Target |
| --- | --- |
| Credential sentinels found in any output | 0 |
| The acceptance flow passes | Yes |
| Existing API flows still pass unchanged | 100% |
| A real Postman collection imports with a listed, accurate gap report | P1 gate |
| Step dispatch overhead | Still under 50 ms p95 |

## Open questions and risks

### Open questions

- [ ] **Blocking T4 and T5 — you:** is the `{{secrets}}` pipeline scheduled before or after this module? If after, T4 and T5 are blocked and the module ships without authentication, which limits it severely.
- [ ] **Blocking T8 — you:** which JSON Schema library? Any addition needs an ADR. `JsonSchema.Net` is the common .NET choice.
- [ ] **Non-blocking — you:** do your team's APIs use OAuth client credentials, or a different flow (authorization code, implicit)? The spec builds client credentials because it's the machine-to-machine standard; another flow would change T5.
- [ ] **Non-blocking — you:** is a real Postman collection available to test T10's importer against? Without one, the importer is built against assumptions.

### Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| A credential leaks into a report or log | Medium | **Severe** | Sentinel scans on every auth task, mutation-checked; redaction is tested, not assumed |
| The OAuth token cache leaks across tenants | Low | **Severe** | Tenant in the cache key, tested explicitly as a tenant-isolation concern |
| Silent extraction failures produce confusing downstream errors | Medium | Medium | Extraction fails loudly by default; `Required: false` is explicit opt-in |
| Loose numeric comparison causes false passes | Medium | High | Explicit type-strictness tests in T7. A false pass is worse than a failure |
| Scope grows toward matching REST Assured's full API surface | Medium | Medium | The acceptance flow is the measure, not feature count. P1 items stay P1 |
