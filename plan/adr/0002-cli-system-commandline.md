# ADR-0002: System.CommandLine for the Yukti CLI

Status: Accepted
Date: 2026-10-13
Owner approval: pre-approved — the owner specified this library directly when requesting D2-T6.

## Context

D2-T6 requires a new CLI project (`src/Yukti.Cli`) that runs a flow file
in-process, with no API server. SUTRADHAR.md §4 requires an ADR before
adding a new NuGet/npm dependency.

## Decision

Use `System.CommandLine` 2.0.12 (the current stable release) for argument
parsing, subcommands (`yukti run <flow.json>`), and exit-code handling.

## Why

- It's Microsoft's own command-line library, actively maintained, with a
  stable 2.0 API (migrated off the long-running 2.0.0-betaN series).
- Built-in support for everything the spec's CLI surface needs natively:
  repeatable options (`--var key=value`, arity `ZeroOrMore` by default for
  array-typed options), async actions returning an `int` exit code,
  and `ProcessTerminationTimeout` — Ctrl+C/SIGTERM is wired to the same
  `CancellationToken` passed to the action automatically, which is exactly
  the cancellation path `--timeout` also uses.
- No alternative was seriously considered — hand-rolling argument parsing
  for ~6 options plus a subcommand would reimplement a smaller, buggier
  version of what this library already does correctly.

## Consequences

- `Yukti.Cli.csproj` takes one new PackageReference. No other project in
  the solution references it — the CLI is a leaf node.
- `PackAsTool` + a `RuntimeIdentifiers`-conditioned self-contained/
  single-file publish profile both live in the same `.csproj` (the owner
  asked for both artifacts now, not the self-contained build deferred to
  Day 6 as the spec itself had suggested).
- `RollForward: LatestMajor` is set so the framework-dependent build (the
  .NET tool path) runs on whatever later major runtime a user actually has
  installed, rather than demanding an exact 8.0.x match — found necessary
  on the dev machine itself, which has only a .NET 10 runtime installed
  alongside the .NET 8 SDK.
