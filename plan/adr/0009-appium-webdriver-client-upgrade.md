# ADR-0009: Appium.WebDriver stays as the Mobile module's client, upgraded to 8.2.0

Status: Accepted
Date: 2026-11-07
Owner approval: explicit, this session — approved adding `Appium.WebDriver` as MOB-T1's WebDriver
client dependency (the Mobile Testing Module Spec's own "Blocking MOB-T1" open question), then,
once it came to light that the dependency already existed in this codebase at version `6.0.1`,
explicitly approved upgrading it to `8.2.0` rather than keeping the existing pin.

## Context

`src/Yukti.Infrastructure.InMemory/Modules/MobileModule.cs` already exists — a direct port of an
earlier TS prototype's mobile automation, built before the formal Mobile Testing Module Spec, and
already wired into the real app (`Program.cs`: `AddSingleton<MobileModule>()`, registered in the
module registry, seeded as a real `ModuleRegistration` at startup alongside Api/Web/Logs). It
already depends on `Appium.WebDriver` 6.0.1 (`Yukti.Infrastructure.InMemory.csproj`), the official
.NET client for Appium (built on `Selenium.WebDriver` bindings), which is exactly the "maintained
.NET Appium client package rather than hand-rolling the protocol" MOB-T1's own checklist asks for.

Before this was discovered, 8.2.0 (the current release as of this session, per nuget.org) had
already been researched and presented for approval as if adding a new dependency from scratch.
Once the existing 6.0.1 reference was found, the real decision became upgrade-vs-keep, not
add-vs-don't — a materially different, higher-risk question (a two-major-version jump can carry
breaking API changes the already-working `MobileModule` code depends on).

## Decision

Upgrade `Appium.WebDriver` from `6.0.1` to `8.2.0`.

## Why

- Owner's explicit choice, made with the real tradeoff stated plainly (zero-risk "keep 6.0.1" was
  the recommended default; the owner chose to upgrade anyway).
- Empirically verified, not assumed: `dotnet build` succeeded with zero errors against 8.2.0
  without changing a single line of the existing `MobileModule.cs` logic beyond what MOB-T1 itself
  needed to add — `AppiumOptions`, `AppiumDriver`/`AndroidDriver`/`IOSDriver` construction,
  `AddAdditionalAppiumOption`, `PerformActions`/`ActionSequence`/`PointerInputDevice`, and
  `driver.Manage().Logs.GetLog(...)` (new in this pass, for MOB-T1's log-capture requirement) all
  compile unchanged against 8.2.0.

## Consequences

- `Yukti.Infrastructure.InMemory.csproj`'s `Appium.WebDriver` reference moves to `8.2.0`.
- No code changes were needed purely because of the version bump — confirmed by a clean build
  before any of MOB-T1's own new code was added, isolating "did the upgrade break anything" from
  "does MOB-T1's new code work" as two separate, separately-verified questions.
- Future Appium client upgrades should get the same treatment: build first against the existing
  code unchanged, to separate upgrade risk from whatever feature work prompted looking at the
  version in the first place.

## Alternatives considered

- **Keep 6.0.1.** The zero-risk default; recommended, not chosen. Owner's call to upgrade instead.
- **Hand-roll the W3C WebDriver/Appium protocol directly.** Rejected by the spec itself ("Use a
  maintained .NET Appium client package rather than hand-rolling the protocol") and moot regardless
  — a maintained client already existed in this codebase before this session even started.

## Revisit if

- A future Appium client version breaks `MobileModule`'s existing API usage — re-verify with the
  same "build unchanged first" method before layering new feature work on top of a broken upgrade.
