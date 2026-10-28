# ADR-0007: `schema` assertion validation moves from the hand-rolled subset to JsonSchema.Net

Status: Accepted
Date: 2026-10-28
Owner directive: explicit — asked via AskUserQuestion for API-T6, chose "Add JsonSchema.Net" over
extending the existing hand-rolled validator in-tree.

## Context

API Testing Module Spec, API-T8 asks for the `schema` assertion to validate a whole response
against a real JSON Schema, listing **every** violation with its path, not just the first — the
whole point being that one schema assertion replaces many brittle field-by-field assertions and
catches shape changes they'd miss.

The existing `MinimalJsonSchemaValidator` (API's own doc comment: "a deliberately small subset —
`type`, `required`, `properties`, `items`, `enum` only... not a full JSON Schema implementation")
is fail-fast (returns the *first* violation only) and has no `$ref`, `oneOf`/`anyOf`/`allOf`/`not`,
`pattern`/`format`, or numeric/string length bounds. Extending it in-tree to cover that ground
correctly — `$ref` resolution in particular — is exactly the kind of edge-case-laden correctness
work a mature library already got right, repeatedly, across years of real-world schemas; a
hand-rolled version risks silent, subtly-wrong validation (a false pass on a schema violation is
worse than the assertion not existing at all).

Per CLAUDE.md §4, adding a NuGet dependency needs an ADR and owner approval. Asked directly; the
owner chose JsonSchema.Net over the no-new-dependency alternative.

## Decision

Add `JsonSchema.Net` version `9.5.0` (the spec's own suggested library; latest stable at the time
of this ADR) as a `PackageReference` on `Yukti.Infrastructure.InMemory` — the project `ApiModule`/
`AssertionEvaluator` already live in. Draft 2020-12 support (the library's default). `MinimalJsonSchemaValidator` is **not deleted**
(no other assertion type uses it, but removing a working, documented, tested piece of code that
isn't actually in the way is unnecessary churn) — the `schema` assertion's evaluation path in
`AssertionEvaluator` switches to `JsonSchema.Net`, collecting every violation's path and message
into one joined error string rather than stopping at the first.

A `schema` value that's a JSON object is used inline, as before. A `schema` value that's a
*string* is treated as a name, loaded from a new `ApiSchemaStore` (a single configured directory,
`YUKTI_API_SCHEMAS_DIR`) — the reusable-schema-artifact half of API-T8's checklist, built the same
way `ApiFileStorage` (API-T3) and `FilePromptLoader` already handle a flow-author-named,
file-backed resource in this codebase: not tenant- or run-scoped (there's no such mechanism to
hang it from yet), documented as a known, narrower-than-illustrative gap rather than hidden.

## Consequences

- `schema` assertion failures now name every violated path, not just the first — the actual
  acceptance bar for API-T8.
- One new NuGet dependency. MIT licensed, no transitive package of note beyond
  `JsonPointer.Net`/`JsonSchema.Net`'s own small dependency set.
- `$ref`, `oneOf`/`anyOf`/`allOf`/`not`, `pattern`, `format`, and numeric/string bounds all become
  available for free — the "Known constraints" list in docs/specs/modules/api.md shrinks
  accordingly for this one assertion type (it still applies to `JsonPathEvaluator`'s own dotted
  `path` syntax elsewhere, which this ADR does not touch).
- The named-schema-reference store is shared across tenants, exactly like `ApiFileStorage` already
  is — a real, documented gap, not a security boundary, until a per-run or per-tenant upload
  mechanism exists for either.
