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
