namespace Yukti.Domain.Assertions;

/// <summary>
/// Closed hierarchy of record types (a discriminated union via inheritance
/// plus pattern matching) rather than one class with nullable fields per
/// possible assertion kind — a PathEqualsAssertion can never be constructed
/// without both a Path and an ExpectedValue, where a flat-fields design
/// would allow an ambiguous, partially-specified assertion to exist.
/// (Volume 1 Part II §11.6)
/// </summary>
public abstract record Assertion;

public sealed record StatusAssertion(int ExpectedStatus) : Assertion;

public sealed record PathEqualsAssertion(string Path, object? ExpectedValue) : Assertion;

public sealed record PathContainsAssertion(string Path, object ExpectedFragment) : Assertion;

public sealed record PathExistsAssertion(string Path) : Assertion;

public sealed record HeaderExistsAssertion(string HeaderName) : Assertion;

public sealed record CookieExistsAssertion(string CookieName) : Assertion;

/// <summary>
/// Schema is a plain CLR object graph (Dictionary/List/primitives — the
/// same normalized shape every other param arrives in, see
/// JsonParamNormalizer), not a raw JSON string. Validated by
/// MinimalJsonSchemaValidator, a deliberately small subset of JSON Schema
/// (type/required/properties/items/enum only) — see that type's own doc
/// comment and docs/specs/modules/api.md's "Known constraints" for what's
/// out of scope.
/// </summary>
public sealed record SchemaValidationAssertion(object Schema) : Assertion;
