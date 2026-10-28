# API importers (CLI)

API Testing Module Spec, API-T10. This is CLI tooling, not module runtime behavior — it generates
an ordinary flow file (`FlowFileDto`, the same external contract `yukti run` already consumes
unchanged) from an external API specification. Neither importer touches `IAutomationModule`, the
flow engine, the registry, the schema-driven UI, the report model, or the CLI's existing `run`
command.

## Usage

```
yukti import openapi <spec.json> -o <flow.json> [--operation <operationId> ...]
yukti import postman <collection.json> -o <flow.json>
```

Both print any `Warnings` to stderr (one `warning: ...` line each) and write the generated flow
file only if the document produced at least one step. A document that produces zero usable steps
is a refused import (exit 1, no file written) — never a flow file with zero steps, and never a
flow file that looks complete but silently dropped something the source document asked for.

## OpenAPI (`OpenApiImporter`)

- **JSON only** — no YAML, no Swagger 2.0. A real, documented scope-down (same convention as
  `JsonPathEvaluator`/`MinimalJsonSchemaValidator`'s own hand-rolled subsets) rather than adding a
  new NuGet dependency this task doesn't strictly need; YAML OpenAPI documents can be converted to
  JSON with any standard tool first.
- One `api`/`request` step per selected operation (`--operation` filters by `operationId`;
  omitted, every operation in `paths` is imported).
- `in: path` / `in: query` / `in: header` parameters become `{{vars.<name>}}` placeholders in
  `pathParams` / `queryParams` / `headers` — a flow author fills in real values via `--var` at run
  time, or an earlier step's `saveAs`.
- The first 2xx response's `content."application/json".schema` becomes a pre-filled
  `{type: "schema", schema: ...}` assertion.
- A `requestBody` is **never fabricated** — OpenAPI only describes its *shape*, never a real
  value, so `body` is left unset and the operation is named in a warning instead.
- `in: cookie` parameters have no first-class step param yet — named in a warning, skipped.

## Postman Collection v2.1 (`PostmanImporter`)

- Only `info.schema` containing `"v2.1"` is accepted.
- Folders (nested `item` arrays) are flattened recursively; a flattened step's name is prefixed
  with its folder path (`"Orders folder / List orders"`).
- Postman's own `{{name}}` template syntax maps directly onto Yukti's `{{vars.name}}` — this
  substitution *is* "variables map to Yukti variables," the spec's own checklist wording.
  Postman's dynamic variables (`{{$guid}}`, `{{$timestamp}}`, ...) have no Yukti equivalent and
  are left as literal text, with a warning naming each one. The collection's own top-level
  `variable[]` entries, plus every `{{name}}` reference actually used, become the generated
  flow's `declaredVariables` (names only — values come from `--var` at run time, same as every
  other flow file).
- Postman's `:id` path-param syntax becomes `{id}` (`ApiModule`'s own `pathParams` placeholder
  syntax, API-T2) plus a matching `{{vars.id}}` entry.
- A structured `url.query[]` array is rebuilt into `queryParams` (honoring `disabled: true`); a
  bare-string URL keeps its query string embedded as-is (`ApiModule`'s own `url` param already
  parses an embedded `?...` query string itself, API-T2) — either way, nothing is dropped.
- `body.mode: "raw"` with JSON content is converted literally (a real advantage over OpenAPI,
  which only ever has a schema, never an example value). Any other body mode (`formdata`,
  `urlencoded`, `graphql`, `file`, ...) has no Yukti equivalent yet — named in a warning, skipped.
- `auth.type: "bearer"` becomes a literal `Authorization: Bearer {{vars.<name>}}` header.
  `auth.type: "basic"` and anything else is named in a warning (Basic specifically notes the
  `{{secrets}}` pipeline is blocked, v0.2 — there is nowhere safe to put a real credential yet).
- An item's `event[]` (pre-request/test scripts) has no Yukti equivalent — named in a warning;
  the request itself still imports.

## Known constraints

- Neither importer resolves `$ref` (OpenAPI) or collection-level `auth` inherited from a parent
  folder (Postman) — both are out of this task's scope, not silently mishandled: an operation/
  request that depends on either imports with whatever's directly on it and nothing more.
- Multipart (`body.mode: "formdata"`) import is not supported — API-T3's own multipart body
  shape (`{parts:[...]}`) would need the importer to also stage referenced files into
  `ApiFileStorage`, which neither importer does.
