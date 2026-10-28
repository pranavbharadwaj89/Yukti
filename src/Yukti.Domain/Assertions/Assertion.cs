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

/// <summary>API Testing Module Spec, API-T7: status against a fixed allowed set,
/// not just one exact value.</summary>
public sealed record StatusInAssertion(IReadOnlyList<int> ExpectedStatuses) : Assertion;

/// <summary>API-T7: the number of elements at a path (an array's length, or 1 for
/// a present scalar/object), e.g. asserting a list came back with exactly N items.</summary>
public sealed record PathCountAssertion(string Path, int ExpectedCount) : Assertion;

/// <summary>API-T7: exact header value match, as distinct from HeaderExistsAssertion's
/// presence-only check.</summary>
public sealed record HeaderEqualsAssertion(string HeaderName, string ExpectedValue) : Assertion;

/// <summary>API-T7: substring match on a header's value.</summary>
public sealed record HeaderContainsAssertion(string HeaderName, string Fragment) : Assertion;

/// <summary>API-T7: the request's own measured duration, excluding Yukti's overhead
/// (ApiModule measures from just before SendAsync to just after the body is read).</summary>
public sealed record ResponseTimeUnderAssertion(TimeSpan Max) : Assertion;

/// <summary>API-T7: the raw response body text matches a regular expression —
/// for responses that aren't JSON, or a check no path/schema assertion expresses well.</summary>
public sealed record BodyMatchesPatternAssertion(string Pattern) : Assertion;

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
