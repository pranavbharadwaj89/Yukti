# Module Specs — Index

These specs are reverse-engineered from the actual codebase — the TypeScript
prototype (`yukti-automation.zip` → `AutomationModule` interface,
`src/modules/*`) and the .NET port (`yukti-platform` → `IAutomationModule`,
`src/Yukti.Infrastructure.InMemory/Modules/*`). Nothing here is aspirational;
every claim is traceable to a specific file. Where the .NET port doesn't
exist yet, the spec says so explicitly and describes the TS behavior as the
reference implementation to port against.

## Shared contract

Every module — built-in or third-party — implements one interface, so the
Flow Engine, the report, and (per FR-ORCH-7) the GUI's flow-authoring surface
all treat every module identically:

**TS (prototype)** — `AutomationModule` (`src/core/types.ts`):
```ts
interface AutomationModule {
  kind: ModuleKind;
  setup?(ctx: RunContext): Promise<void>;
  run(action: string, params: Record<string, unknown>, ctx: RunContext): Promise<Omit<StepResult, "step"|"module"|"action"|"durationMs">>;
  teardown?(ctx: RunContext): Promise<void>;
}
```

**C# (real, Volume 1 Part III §18)** — `IAutomationModule` (`Yukti.Contracts/IAutomationModule.cs`):
```csharp
public interface IAutomationModule
{
    ModuleKind Kind { get; }
    string ContractVersion { get; }
    IReadOnlyList<ActionSchema> GetSupportedActions();
    Task Setup(ExecutionContext ctx, CancellationToken ct);
    Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct);
    Task Teardown(ExecutionContext ctx, CancellationToken ct);
}
```

The C# contract is strictly stronger than the TS one: it adds
`ContractVersion` and `GetSupportedActions()` (self-describing, so the GUI
needs zero hardcoded per-module knowledge — this is what makes "Module
Parity" a testable property rather than a design goal, per the code comment
in `IAutomationModule.cs`), and `ExecutionContext` deliberately withholds
repository/command access from modules — they get `RunId`, `Variables`,
`ICredentialResolver`, and a `CancellationToken`, nothing else
(`ExecutionContext.cs`).

`ModuleKind` (`Yukti.Domain/ModulePlugin/ModuleKind.cs`) is an open value
type, not a closed enum — `Api`, `Web`, `Mobile`, `DesktopUi`, `Logs`, `Ai`
are named static instances, and `ModuleKind.Custom(value)` keeps the type
open for marketplace modules, by explicit design (Architecture Principle
20.3: every module treated identically, including ones that don't exist
yet).

## Status at a glance

| Module | TS prototype | .NET port | Backing tech | Trust tier |
|---|---|---|---|---|
| [API](./api.md) | working | `ApiModule.cs` | `HttpClient` / `fetch` | BuiltIn |
| [Logs](./logs.md) | working | `LogsModule.cs` | Regex + z-score | BuiltIn |
| [Web](./web.md) | working (needs Playwright install) | not ported | Playwright | BuiltIn (planned) |
| [Mobile](./mobile.md) | working (needs Appium server) | not ported | Appium / WebdriverIO | BuiltIn (planned) |
| [UI (Desktop)](./ui-desktop.md) | working (needs a display) | not ported | nut.js | BuiltIn (planned) |
| [AI](./ai.md) | working (needs `ANTHROPIC_API_KEY`) | not ported | Claude API | BuiltIn (planned) |

Only Api and Logs have been carried into the formal Volume 1 domain model so
far — both are direct ports, confirmed by comments in the C# source itself
("Direct port of the original TS prototype's ApiModule/LogModule"). Web,
Mobile, UI, and AI exist only as working TS code; porting them is
mechanical (same pattern Api/Logs followed) but not yet done.

## What's structurally deferred for every module, not just one

- **Sandboxed execution**: `ModuleDispatcher` (`Yukti.Orchestration/ModuleDispatcher.cs`)
  dispatches every module in-process regardless of `TrustTier`. The
  trust-tiered `InProcessExecutionStrategy` vs `SandboxedExecutionStrategy`
  split (Volume 1 Part III §18.5) is specified but not implemented — it
  only matters once Community-tier (marketplace) modules exist.
- **Per-step retry configuration**: `FlowEngine` applies one uniform
  `RetryPolicy` (`MaxAttempts: 2, InitialBackoff: 200ms, x2 multiplier`) to
  every step of every module; per-step-configurable retry is a documented
  follow-up (`FlowEngine.cs` comment).
