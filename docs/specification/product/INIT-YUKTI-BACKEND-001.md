# INIT-YUKTI-BACKEND-001 — Backend core: domain model, orchestration engine, cross-cutting platform services, API & data layer

| Field | Value |
|-------|-------|
| Initiative | `INIT-YUKTI-BACKEND-001` |
| Source spec | Yukti Architecture Bible, Volume 1 — Backend Architecture (Parts I–VI, Sections 1–40) |
| Source digest | Volume 1, ~39,000 words, 40 sections, 6 Parts — see References |
| Repo | `yukti-platform` (primary) |
| Format basis | Adapted from `INIT-ABHILEKH-002` spec structure (SDD, FR/AC/evidence pattern) at requester's direction |
| Date | 2026-07-25 |
| Status | Draft — dev review required before feasibility |
| Dependency INIT | None (Volume 1 is the foundational backend layer; Volumes 2–5 depend on this, not vice versa) |

## Overview

Yukti's backend is a unified automation-orchestration platform: one domain model, one execution engine, and one plugin contract serving six automation capability domains (API, Web, Mobile, Desktop UI, Logs, AI) as structural peers — no module is privileged over another anywhere in the codebase. This initiative specifies the backend core: the DDD domain model (three aggregates — `Flow`, `FlowRun`, `ModuleRegistration`), the orchestration engine that executes flows with crash-durable incremental commits, the plugin contract third parties build against, and the cross-cutting services (auth, multi-tenancy, audit, observability) and API/data layer that make it a deployable, secure, multi-tenant product.

**This PRD's job:** make every architectural decision in Volume 1 into a testable FR with explicit acceptance criteria, so an AI coding agent can implement directly against it with no re-derivation of intent and no concept silently dropped.

## As-built baseline (do not re-implement)

A first coding pass on this initiative already exists and is real, building, and running — this table exists specifically so an implementing agent does not re-derive what's already correct.

| Area | Status | Evidence |
|------|--------|----------|
| `Yukti.Domain` — all 3 aggregates, entities, value objects, domain events, strongly-typed IDs | **Live** | `src/Yukti.Domain/**`; builds clean, zero external deps |
| `Yukti.Contracts` — `IAutomationModule`, `ExecutionContext`, `StepOutcome`, `ICredentialResolver` | **Live** | `src/Yukti.Contracts/**` |
| `Yukti.Application` — repository interfaces, `IUnitOfWork(Factory)`, command handlers (CreateFlow, AddFlowStep, PublishFlow, RegisterModule, TriggerFlowRun, CancelFlowRun) | **Live** | `src/Yukti.Application/**` |
| `Yukti.Orchestration` — `FlowEngine` with incremental per-step commits, `ModuleDispatcher`, `VariableStore`, `RetryFlakeHandler`, `ModuleRegistry` | **Live** | `src/Yukti.Orchestration/**`; verified via real end-to-end run (live HTTP chaining + log rule engine) |
| Two real built-in modules: `ApiModule` (HttpClient), `LogsModule` (regex rule engine + anomaly detection) | **Live** | `src/Yukti.Infrastructure.InMemory/Modules/**` |
| Real PostgreSQL/EF Core `Yukti.Infrastructure` | **Absent** — in-memory stand-in only | `src/Yukti.Infrastructure.InMemory/**` implements the *real, permanent* repository/UoW interfaces with non-durable storage |
| REST API (`Yukti.Api`) | **Absent** | — |
| SignalR real-time layer | **Absent** | — |
| Authentication / Authorization / RBAC | **Absent** | `User` entity exists in Domain; no auth pipeline |
| Multi-tenancy enforcement (RLS, 3-layer defense) | **Absent** — `TenantId` threaded through Domain only | — |
| Audit pipeline (`AuditableCommandHandler`) | **Absent** | — |
| Structured logging / OpenTelemetry | **Absent** | — |
| Scheduler (cron/webhook/file-watch triggers) | **Absent** | — |
| Workflow Engine (multi-flow DAG orchestration) | **Absent** | — |
| Event Bus Tier 2 (durable outbox relay) | **Absent** — Tier 1 in-process dispatch only, non-durable | `InMemoryDomainEventDispatcher` |
| Trust-tiered / sandboxed module execution | **Absent** — in-process dispatch only, no Community-tier sandbox | `ModuleDispatcher` |

## Functional requirements

FR IDs are namespaced by subsystem to stay legible at this scale (94 FRs across 16 subsystems). Every row traces to a specific Volume 1 section, restated here in Abhilekh's terse, acceptance-testable style rather than Volume 1's original prose.

### Domain model (`FR-DOM`) — Volume 1 Part II, §6–14

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-DOM-01 | Five bounded contexts (Flow Authoring, Execution, Module & Plugin, Reporting & Audit, Identity & Access) exist as separate namespaces; AI is a cross-cutting capability, not a sixth context | §7.1, §7.5 | Namespace inspection shows 5 context folders; no `AiAssistance` namespace exists | inspection |
| FR-DOM-02 | `Flow.AddStep` throws unless `Status == Draft` | §9.2 | Attempt on Published flow → `DomainException` | unit |
| FR-DOM-03 | `Flow.Publish` rejects if any step's `(Module, Action)` fails resolver lookup | §9.2, §9.5 | Unresolved action → `FlowPublishResult.Succeeded == false` with explicit error string | unit |
| FR-DOM-04 | `Flow.Publish` rejects if any step references a `{{vars.x}}` binding no prior step declares via `SaveAs` | §9.2 | Forward/undeclared reference → publish failure naming the undeclared variable | unit |
| FR-DOM-05 | Editing a Published flow is impossible; only `CreateNewVersion` (new `FlowId`, shared `FamilyId`, `Version+1`) is available | §9.2 | Attempted step-add on Published flow throws; new version has correct `FamilyId`/`Version` | unit |
| FR-DOM-06 | `FlowRun.RecordStepResult` throws once `Status` is terminal (`Passed`/`Failed`/`Cancelled`) | §9.3 | Post-terminal call → `DomainException` | unit |
| FR-DOM-07 | `FlowRun` references `FlowId` (exact version), never `FlowFamilyId` | §9.3 | Type signature check; a `FlowRun` for family-version 2 is unaffected by a later edit to a new draft version 3 | unit + inspection |
| FR-DOM-08 | `ModuleRegistration.Trust` is immutable after construction; re-certification requires a new registration instance | §9.4 | No setter/mutator exists on `Trust`; attempt to mutate is a compile error | inspection |
| FR-DOM-09 | `ModuleKind` is an open value object (static instances + `Custom()` factory), never a closed C# `enum` | §11.2 | `ModuleKind.Custom("marketplace-thing")` succeeds; uppercase/whitespace input throws `DomainException` | unit |
| FR-DOM-10 | `Assertion` is a closed discriminated union (`Status`/`PathEquals`/`PathContains`/`PathExists`); no flat nullable-field design | §11.6 | Each variant's constructor requires all its fields; no way to construct an ambiguous partial assertion | inspection |
| FR-DOM-11 | `VariableExpression` parses `{{vars.x.y}}` references at construction time, not resolution time | §11.7 | `ReferencedPaths` populated immediately on `new VariableExpression(...)`, before any execution occurs | unit |
| FR-DOM-12 | `StepResult.AiAttribution` is non-null if and only if AI materially altered that step's outcome | §11.5 | Self-heal path sets it; every other path leaves it `null` | unit |
| FR-DOM-13 | Every domain entity/aggregate identifier is a distinct strongly-typed wrapper; no raw `Guid` in any public method signature | §10.8 | Static analyzer rule (see FR-STD-01) rejects raw `Guid` parameters on public domain APIs | static analysis |
| FR-DOM-14 | `StepResult` denormalizes `StepName`/`Module`/`Action` rather than joining live from `FlowStep` | §10.3.1 | A report for a run against Flow v1 shows v1's step names even after Flow v2 renames those steps | unit |
| FR-DOM-15 | Cross-aggregate checks (e.g., `Flow.Publish`'s module-resolution check) go through an injected port interface (`IModuleActionResolver`), never a direct reference to another aggregate or its repository | §9.5 | `Flow` has no constructor or field referencing `ModuleRegistration` or any repository type | inspection |
| FR-DOM-16 | Domain event catalog is closed and past-tense named: `FlowPublished`, `FlowArchived`, `FlowRunStarted`, `StepCompleted`, `StepSelfHealed`, `FlowRunCompleted`, `FlowRunFlakeDetected`, `ModuleRegistered`, `ModuleDeprecatedAction` | §12.2–12.4 | Event type inventory matches exactly; no additional undocumented event types | inspection |
| FR-DOM-17 | `RetryAttempt` records are retained on `StepResult` distinct from the final outcome, enabling flake computation without re-scanning execution logs | §10.4 | A step that fails once then passes has `RetryHistory.Count == 1` and final `Status == Passed` | unit |

### Repository & Unit of Work (`FR-REPO`) — Volume 1 Part III, §15–16

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-REPO-01 | Exactly one repository interface per aggregate root; no generic `IRepository<T>` | §15.2–15.3 | `IFlowRepository`, `IFlowRunRepository`, `IModuleRegistrationRepository` exist; no generic repo type in the solution | inspection |
| FR-REPO-02 | No repository exposes `GetAll()`, `Find(predicate)`, or pagination — those are query-side concerns | §15.2 | Interface inspection confirms absence | inspection |
| FR-REPO-03 | `Yukti.Domain` has zero project references to any persistence technology | §15.5 | `.csproj` inspection — zero `PackageReference`/`ProjectReference` beyond BCL | inspection |
| FR-REPO-04 | `FlowEngine` commits **once per step**, not once per run — a crash mid-run loses at most the one step in flight | §16.7 (the mid-project fix) | Fault-injection test: kill execution after step 2 of 4 → steps 1–2 durably queryable afterward | integration |
| FR-REPO-05 | Every step's `IUnitOfWork.Commit()` also flushes that step's raised domain events in the same logical operation (outbox pattern) | §16.3–16.4 | A crash between state-write and event-dispatch never occurs in isolation — both succeed or the whole step's commit is retried | integration |
| FR-REPO-06 | Every repository query filtering by tenant does so at the query itself, never via a post-fetch check in a calling service | §15.6, §26.3 | Query inspection — `WHERE tenant_id = @tenantId` (or equivalent) present in every multi-tenant query | inspection |

### Plugin architecture (`FR-PLUGIN`) — Volume 1 Part III, §17–18

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-PLUGIN-01 | `IAutomationModule` exposes exactly: `Kind`, `ContractVersion`, `GetSupportedActions()`, `Setup`, `Run`, `Teardown` | §18.2 | Interface diff against spec — no additional members | inspection |
| FR-PLUGIN-02 | `ExecutionContext` given to a module contains only `RunId`, `Variables`, `Credentials`, `RunCancellation` — no repository, no command dispatcher, no reference to `Flow`/`FlowRun` aggregates | §18.3 | Type inspection of `ExecutionContext` | inspection |
| FR-PLUGIN-03 | `ICredentialResolver.ResolveAsync` returns only credentials explicitly wired into the invoking step's parameters; no enumeration capability | §18.4 | A module cannot list available credentials; only named lookups succeed | unit |
| FR-PLUGIN-04 | Built-in/Verified-tier modules execute in-process; Community-tier modules execute in an isolated sandboxed process over a narrow RPC boundary | §18.5 | Trust-tier-to-strategy mapping test; a Community-tier module cannot access in-process memory of the host | integration (deferred — sandbox not yet built, see Open Questions) |
| FR-PLUGIN-05 | `ContractVersion` changes are additive-only for minor/patch; a major bump is required to add a required method or remove any method | §18.7 | CI check diffing `IAutomationModule`'s public surface against the last major-tagged version | static analysis |
| FR-PLUGIN-06 | `GetSupportedActions()` is the **only** data source the flow-authoring UI needs to render a module's action picker and parameter editor — zero module-specific frontend code exists anywhere | §18.6 | A synthetic, previously-unknown module registers and its actions render correctly with no code change (mirrors Volume 2 §19.5's module-parity test) | integration |
| FR-PLUGIN-07 | Built-in modules are registered with Singleton DI lifetime and hold zero mutable instance state | §17.5 | Concurrent execution stress test — 500 simultaneous `FlowRun`s against the same module instance produce no cross-run data leakage | integration |

### Execution engine (`FR-EXEC`) — Volume 1 Part III, §19

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-EXEC-01 | Steps execute in `Order` sequence | §19.2 | Steps dispatch in ascending `Order`, verified via dispatch-order capture in a test double | unit |
| FR-EXEC-02 | Default is fail-fast: first `Failed` step halts the run unless `Flow.ContinueOnFailure == true` | §19.2, §19.7 | Default flow halts after first failure; `ContinueOnFailure` flow runs every step regardless | unit (already verified live in `Yukti.Host` demo) |
| FR-EXEC-03 | A step whose `when` condition evaluates falsy is recorded as `Skipped`, never omitted from `Results` | §19.6 | `FlowRun.Results.Count` equals `Flow.Steps.Count` even with skipped steps present | unit |
| FR-EXEC-04 | A step's `SaveAs` binding is available to every subsequent step's `{{vars.x.y}}` interpolation within the same run | §19.4 | Chained two-step flow: step 2's resolved parameter matches step 1's actual output field | unit (already verified live) |
| FR-EXEC-05 | Retry attempts use the configured `RetryPolicy` (max attempts, initial backoff, multiplier); a step passing after ≥1 failed attempt is flagged flaky, never reported as a clean pass | §19.5 | `RetryHistory` non-empty + final `Status == Passed` on a step that fails once then succeeds | unit |
| FR-EXEC-06 | `CancellationToken` is checked between every step dispatch, honoring `CancelFlowRunCommand` promptly | §19.2, Part II §13.3 | Cancellation requested mid-run halts before the next step dispatches, not merely at run end | integration |
| FR-EXEC-07 | Orchestrator step-dispatch overhead (excluding module execution time) stays under 50ms at p95 | §19.4, NFR-PERF-1 | Load-test benchmark; see FR-PERF-01 | performance |

### Workflow engine (`FR-WORKFLOW`) — Volume 1 Part III, §20

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-WORKFLOW-01 | `WorkflowDefinition` expresses a DAG of `FlowId` nodes with `DependsOn` edges | §20.3 | A 3-node linear DAG and a 2-parallel-then-join DAG both construct without error | unit |
| FR-WORKFLOW-02 | Each node triggers via the exact same `TriggerFlowRunCommand` any standalone flow uses — zero workflow-specific execution path | §20.4 | Code inspection — `WorkflowEngine` calls `ICommandHandler<TriggerFlowRunCommand,_>`, introduces no parallel dispatch mechanism | inspection |
| FR-WORKFLOW-03 | A node's `ContinueWorkflowOnFailure` flag is independent of that node's underlying Flow's own `ContinueOnFailure` | §20.3 | A node with `ContinueWorkflowOnFailure=true` but a fail-fast Flow still lets the *workflow* proceed to dependents even though that *flow run* halted internally | unit |
| Scope note | Volume 1 §20.5 explicitly scopes Workflow Engine as **not** a Must-priority GA requirement | §20.5 | — | — |

### Scheduler (`FR-SCHED`) — Volume 1 Part III, §21

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-SCHED-01 | `TriggerDefinition` supports `Cron`, `Webhook`, `FileWatch` kinds | §21.2 | Three trigger kinds constructible; `FileWatch` scoped to self-hosted deployments only | unit |
| FR-SCHED-02 | Every trigger kind converges on the identical `TriggerFlowRunCommand` — a scheduled run and an API-triggered run are indistinguishable downstream except for the recorded `RunTrigger` value | §21.3 | `RunTrigger` is the only observed difference in resulting `FlowRun` state | unit |
| FR-SCHED-03 | Cron firing across horizontally-scaled workers uses a distributed lock keyed per `(TriggerId, tick-window)` to prevent duplicate firing | §21.4 | Multi-instance test: N scheduler instances, one trigger fires exactly once per tick | integration |
| FR-SCHED-04 | Webhook paths are unguessable and optionally signature-verified against a per-trigger shared secret | §21.5 | Path is a generated high-entropy token; signature mismatch → request rejected | unit |
| FR-SCHED-05 | On Scheduler restart, missed cron ticks within a bounded catch-up window fire; ticks outside that window do not flood-fire | §21.8 | Simulated downtime + restart → missed ticks within window fire once each; ticks beyond window are skipped, not queued | integration |

### Event bus (`FR-EVT`) — Volume 1 Part III, §22

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-EVT-01 | Two-tier design: Tier 1 synchronous in-process dispatch; Tier 2 durable, outbox-backed, at-least-once relay | §22.2 | Component inspection; Tier 2 backed by the same outbox table FR-REPO-05 populates | inspection |
| FR-EVT-02 | Every Tier-2-subscribed consumer is idempotent (upsert-by-key, never blind insert) | §22.6, §12.7 | Duplicate redelivery of the same event produces no duplicate report/audit rows | integration |
| FR-EVT-03 | SignalR live-progress subscribes Tier 1 only; audit and report-projection consumers subscribe both tiers | §22.5 | Consumer registration table matches; a Tier-1-only outage does not lose audit data (Tier 2 catches it) | inspection |

### CQRS / read models (`FR-CQRS`) — Volume 1 Part III, §23

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-CQRS-01 | `FlowSummaryReadModel` is updated synchronously, same transaction as the aggregate write (read-your-own-writes) | §23.4 | Publishing a flow then immediately listing flows shows the new status with zero delay | integration |
| FR-CQRS-02 | `FlowReportReadModel` is eventually consistent via Tier-2 event projection | §23.4, §23.6 | Explicitly documented staleness bound; no code path assumes synchronous consistency for this model | inspection |
| FR-CQRS-03 | `TrendAggregateReadModel` is recomputed by a scheduled batch job (not per-event), with staleness surfaced as a "last updated" timestamp wherever displayed | §23.5–23.6 | Batch job cadence configurable; consuming query returns the batch timestamp alongside data | integration |

### Authentication (`FR-AUTH`) — Volume 1 Part IV, §24

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-AUTH-01 | Three mechanisms supported: JWT session, API key, OIDC/SAML SSO | §24.2 | Each mechanism independently authenticates a request | integration |
| FR-AUTH-02 | Access tokens expire at 15 minutes; refresh tokens rotate on use | §24.5 | Token issued at T is rejected at T+16min; refresh invalidates the prior refresh token | unit |
| FR-AUTH-03 | JWT carries a `RoleVersion` claim, never embedded permissions | §24.6 | Token payload inspection — no permission list present | inspection |
| FR-AUTH-04 | API keys are stored as salted hashes; the raw secret is never retrievable after creation | §24.4 | DB inspection — no reversible storage of key material | inspection |
| FR-AUTH-05 | Authentication failure returns a uniform, non-distinguishing error externally; full detail logged internally only | §24.8 | External response identical for "user not found" vs "wrong password"; internal log differentiates | unit |

### Authorization (`FR-AUTHZ`) — Volume 1 Part IV, §25

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-AUTHZ-01 | Closed `Permission` enum; three baseline roles: Flow Author, Flow Runner, Administrator | §25.1–25.2 | Role↔permission mapping matches spec table exactly; Flow Runner has `FlowExecute`+`ReportView` only | unit |
| FR-AUTHZ-02 | Every command handler calls `EnsurePermission` as its first meaningful statement | §25.3, §13.6 | Static/code-review check across every `ICommandHandler` implementation | static analysis + review |
| FR-AUTHZ-03 | A self-heal or other AI-originated action executes under the *triggering identity's* existing permission scope — never elevated | §25.6 | AI-triggered credential resolution fails identically to how the original triggering user's own request would fail for the same resource | unit |
| FR-AUTHZ-04 | `RoleVersion` mismatch between token and current stored value forces a fresh permission lookup within one request | §25.5, §24.6 | Role revoked mid-session → next request from an already-issued token is denied, not honored until natural expiry | integration |

### Multi-tenancy (`FR-TENANT`) — Volume 1 Part IV, §26

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-TENANT-01 | Three independent enforcement layers: repository query filter, database row-level security, application-service assertion | §26.3 | Disabling any one layer in a test harness — the remaining two still block cross-tenant reads | integration |
| FR-TENANT-02 | `TenantId` is sourced only from `ITenantContextAccessor`, populated only from the authenticated principal — never a header, query param, or client-supplied value | §26.4 | Code review + static check: no code path constructs `TenantId` from `HttpContext.Request.Headers` or similar | static analysis + review |
| FR-TENANT-03 | Pooled multi-tenancy is the hosted default; dedicated-instance is available for the regulated-industry segment | §26.1, §26.6 | Deployment configuration supports both modes from the identical codebase | inspection |

### Audit (`FR-AUDIT`) — Volume 1 Part IV, §27

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-AUDIT-01 | Every command handler inherits `AuditableCommandHandler` unless explicitly, visibly exempted with justification | §27.2 | New command added without inheriting the base class fails a CI check unless an exemption comment is present | static analysis |
| FR-AUDIT-02 | Fields marked `[SensitiveValue]` are automatically excluded from derived audit metadata | §27.4 | A command with a credential-shaped field produces an audit entry with that field redacted, not present | unit |
| FR-AUDIT-03 | The `audit_entries` table grants `INSERT`/`SELECT` only to the application DB role — no `UPDATE`/`DELETE` grant exists | §27.5 | Direct SQL `UPDATE`/`DELETE` against the table fails with a permissions error even from the app's own connection | integration |
| FR-AUDIT-04 | Audit retention is ≥1 year in the hot/queryable tier; older entries archive, never delete | §27.6 | No `DeleteAuditEntryCommand` exists anywhere in the command catalog | inspection |

### Logging (`FR-LOG`) — Volume 1 Part IV, §28

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-LOG-01 | Every log call uses structured templates; string-interpolated log messages are prohibited | §28.2 | Roslyn analyzer rejects `$"..."` passed to any `ILogger` method | static analysis |
| FR-LOG-02 | Log levels conform to the six-value vocabulary: Trace/Debug/Information/Warning/Error/Critical, each with the documented meaning | §28.3 | Log-level usage audit spot-checks match the documented semantics (e.g., a flaky-but-passing step logs Warning, not Error) | review |
| FR-LOG-03 | Every log statement within a `FlowRun`'s execution scope automatically carries `FlowRunId` via `BeginScope`, with no call site manually threading it | §28.4 | Log inspection — every line within an execution scope carries the identifier without explicit per-call passing | integration |
| FR-LOG-04 | Credential values, full request/response bodies from target systems, and full page HTML/screenshots are never logged at any level including Trace | §28.5 | Adversarial log-scraping test finds zero instances of known secret patterns in log output | integration |

### Observability (`FR-OBS`) — Volume 1 Part IV, §29

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-OBS-01 | Every `FlowRun` is instrumented as one OpenTelemetry root trace, with each step as a child span | §29.3 | Trace inspection for a 3-step run shows exactly one root span + 3 child spans | integration |
| FR-OBS-02 | Named metrics exist: `step.dispatch.duration`, `flow.run.flake_detected`, `flow.run.completed`, `orchestration.concurrent_executions`, `ai.request.duration`, `ai.request.timeout` | §29.4 | Metric inventory matches; each populated under realistic load | integration |
| FR-OBS-03 | Alerting fires on symptom metrics with defined early-warning margin below the hard NFR threshold, not only at actual breach | §29.6, Volume 4 §7.3 | Alert rule configuration inspected against documented margins | inspection |

### REST API (`FR-API`) — Volume 1 Part V, §30

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-API-01 | Every endpoint maps 1:1 to a cataloged command or query — no endpoint exists with undocumented backing logic | §30.2 | Endpoint-to-handler mapping table matches the command/query catalog exactly | inspection |
| FR-API-02 | `TriggerFlowRunCommand`-backed endpoint honors an `Idempotency-Key` header; a retried request with the same key returns the original result, never a second execution | §30.4 | Duplicate request with identical key produces exactly one `FlowRun` | integration |
| FR-API-03 | Errors follow RFC 7807 with a `correlationId` field tying back to the full internal trace | §30.5 | Error response schema validated; `correlationId` resolves to a real trace in the observability backend | integration |
| FR-API-04 | Rate limiting is tenant-scoped, using the same Redis-backed sliding-window mechanism as elsewhere in the platform | §30.7 | One tenant's burst traffic does not degrade another tenant's request success rate | integration |
| FR-API-05 | A major API version bump serves both old and new versions concurrently for a minimum 90-day deprecation window | §30.6, cross-referenced Vol 5 §6.4 | Both `/api/v1` and `/api/v2` respond correctly during the overlap window | integration |

### Real-time (`FR-RT`) — Volume 1 Part V, §31

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-RT-01 | `RunProgressHub` scopes clients to a SignalR group keyed by `FlowRunId`; a client never receives another run's events | §31.3 | Two concurrent runs, two subscribed clients — each receives only its own run's events | integration |
| FR-RT-02 | On reconnect, the client always performs a full REST catch-up fetch before trusting further pushed events — never assumes nothing was missed | §31.4 | Simulated disconnect during a run — reconnect reflects every step completed during the gap, not just state as of reconnect moment | integration |
| FR-RT-03 | Horizontal scaling of `nexus-orchestration`-equivalent workers uses a Redis-backed SignalR backplane | §31.5 | Multi-instance test — an event raised on worker A reaches a client connected via worker B | integration |

### Database (`FR-DB`) — Volume 1 Part V, §32

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-DB-01 | Schema tables map 1:1 to the domain model: `flows`, `flow_steps`, `flow_runs`, `step_results`, `retry_attempts`, `module_registrations`, `module_action_entries`, `users`, `roles`, `user_roles`, `audit_entries` | §32.2–32.5 | Schema diff against spec table list | inspection |
| FR-DB-02 | Row-level security policy exists on every multi-tenant table, keyed to `current_setting('app.current_tenant_id')` | §32.7 | RLS policy inventory matches table list; a session without the setting returns zero rows | integration |
| FR-DB-03 | Every composite index on a multi-tenant table leads with `tenant_id` | §32.6 | Index definition inspection | inspection |
| FR-DB-04 | Schema migrations run as a distinct Job gating application rollout; never auto-applied on app startup | §32.8, Vol 1 §37.5 | Deployment pipeline inspection — migration Job precedes app Deployment update | inspection |
| FR-DB-05 | Every migration remains backward-compatible with the immediately-prior application version for one full deployment cycle | §37.5 | Old app version smoke-tests successfully against post-migration schema | integration |

### Deployment, performance, scalability (`FR-OPS`) — Volume 1 Part VI, §37–39

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-OPS-01 | API layer and Orchestration engine are separate deployable containers/processes, independently scalable | §4.4 (Part I, cross-referenced), §37 | Two distinct Deployments in the reference topology; scaling one leaves the other's replica count unaffected | inspection |
| FR-OPS-02 | Graceful shutdown drains in-flight `FlowRun` execution to the next step boundary before process exit | §37.6 | SIGTERM during step execution — current step completes and commits before the process exits | integration |
| FR-OPS-03 | 500 concurrent flow executions sustained without step-dispatch latency exceeding its budget (NFR-SCALE-1) | §38.8, §39.2 | Load test at 500 concurrent executions; p95 dispatch latency stays under 50ms | performance |
| FR-OPS-04 | `flow_runs`/`step_results` partitioning activates automatically at a monitored row-count threshold (5M), with headroom before the 10M target | §39.6, §13.5 (Vol 4) | Alert fires at threshold; partitioning activation requires no schema change, only an operational action | inspection |

### Coding standards (`FR-STD`) — Volume 1 Part VI, §40

| ID | Requirement | Source | Acceptance criteria | Evidence type |
|----|-------------|--------|---------------------|----------------|
| FR-STD-01 | Strongly-typed IDs, dependency-direction, structured-logging-only rules are enforced by static analyzer, not review alone | §40.2 | CI fails on any violation, verified with a deliberately-introduced violation in a test branch | static analysis |
| FR-STD-02 | Every async I/O method accepts and honors a trailing `CancellationToken` | §40.5 | Analyzer flags any I/O method missing the parameter | static analysis |
| FR-STD-03 | No `.Result`/`.Wait()` synchronous-over-async calls anywhere in the codebase | §40.5 | Analyzer rule active; zero violations in current codebase | static analysis |

## Negative and failure paths

| FR | Condition | Required behavior | Evidence |
|----|-----------|-------------------|----------|
| FR-DOM-02/05 | Attempt to edit a Published Flow | `DomainException`, no state change | unit |
| FR-DOM-03/04 | Publish with unresolved module or undeclared variable | `FlowPublishResult.Succeeded=false` with itemized errors, no partial publish | unit |
| FR-REPO-04 | Process crash mid-`FlowRun` execution | At most one step's result lost; all prior steps durably queryable | integration (fault injection) |
| FR-PLUGIN-03 | Module attempts to access an unwired credential | Resolution returns null/fails; no enumeration path exists | unit |
| FR-AUTH-02 | Expired access token presented | 401, refresh flow required | integration |
| FR-AUTHZ-02 | Command issued without required permission | 403 before any aggregate is loaded | unit |
| FR-AUTHZ-04 | Token used after role revoked mid-session | Denied on next request, not honored until natural token expiry | integration |
| FR-TENANT-01 | One isolation layer bypassed (simulated bug) | Remaining two layers still block cross-tenant access | integration (chaos) |
| FR-AUDIT-03 | Direct `UPDATE`/`DELETE` attempted against `audit_entries` | Database-level permission error | integration |
| FR-API-02 | Duplicate `TriggerFlowRunCommand` request with same idempotency key | Second request returns first result; no second execution | integration |
| FR-RT-02 | Client disconnects mid-run, reconnects | Full state reconciliation, no fabricated "replay," no silently stale view | integration |
| FR-SCHED-03 | Two scheduler instances, one cron trigger, same tick | Fires exactly once, not twice | integration |
| FR-DB-04 | App deploy attempted before migration Job completes | Blocked by pipeline gate | inspection |

## Out of scope for this initiative

- Frontend (Volume 2), AI capability engines (Volume 3), infrastructure/deployment tooling beyond what Volume 1 §37 names directly (full Volume 4), engineering process/CI details (Volume 5) — each gets its own SDD-format PRD per this same template, on request
- Trust-tiered sandboxed execution's actual sandbox runtime implementation (Kubernetes Job/gVisor specifics) — Volume 1 specifies the contract (FR-PLUGIN-04); the concrete sandbox technology is Volume 4's domain
- Marketplace certification workflow (Volume 0 §17) — this PRD covers the `TrustTier` domain concept only, not the review/publishing pipeline
- Real vector-store/RAG infrastructure — Volume 3's domain entirely

## Cross-layer contracts

| Contract ID | Provider | Consumer | Entry point | Input shape | Output shape | Invariants | Errors | Compatibility | Contract-test location |
|---|---|---|---|---|---|---|---|---|---|
| CTR-01 | `Yukti.Domain` | `Yukti.Application` | Aggregate public methods (`Flow.Publish`, `FlowRun.RecordStepResult`, etc.) | Primitive + value-object params | Domain entities, `FlowPublishResult`-style result objects | Invariants enforced in-aggregate; never bypassable from Application | `DomainException` for programmer-error-class violations; typed results for expected-outcome rejections | Additive-only on result types; breaking change requires ADR | `Yukti.Domain.Tests` |
| CTR-02 | `Yukti.Contracts` | Every `IAutomationModule` implementation (built-in + marketplace) | `IAutomationModule.Run` | `ExecutionContext` + params dict | `StepOutcome` | `ExecutionContext` never exposes repository/command access | `StepOutcome.Failed` for expected failures; unhandled exceptions caught by dispatcher | Semantic versioning per FR-PLUGIN-05 | Contract-test suite run against every registered module, including third-party |
| CTR-03 | `Yukti.Orchestration` (`FlowEngine`) | `Yukti.Application` (`IUnitOfWorkFactory`, `IFlowRunRepository`) | `CommitRun` internal method | In-memory `FlowRun` aggregate state | Persisted state + dispatched events | Exactly one commit per step, no batching | Commit failure surfaces as run-level infrastructure error, not silently swallowed | N/A — internal contract | Integration test per FR-REPO-04 |
| CTR-04 | `Yukti.Api` | `Yukti.Application` (command/query dispatch) | Every controller action | HTTP request | HTTP response (JSON, or RFC 7807 on error) | Controllers contain zero business logic — pure protocol translation | RFC 7807 uniform error shape | `/api/v{n}` path-segment versioning, 90-day min deprecation window | API integration test suite |
| CTR-05 | `Yukti.Orchestration` | SignalR Hub (`Yukti.Api`) | `StepCompletedEvent`/`FlowRunCompletedEvent` via Event Bus Tier 1 | Domain event payload | Pushed hub message, group-scoped by `FlowRunId` | Group scoping mandatory; no tenant-wide broadcast | Connection drop → client-driven REST catch-up, not server-side replay | N/A | SignalR integration test |
| CTR-06 | Identity provider (external OIDC, or internal password store) | `Yukti.Api` (Authentication) | Login / token refresh endpoints | Credentials or refresh token | JWT access token + `HttpOnly` refresh cookie | `RoleVersion` claim present; no permission list embedded | 401 uniform failure, no user-enumeration signal | OIDC standard compliance | Auth integration test suite |

## Non-functional requirements

| Area | Requirement | Acceptance / evidence |
|------|-------------|------------------------|
| Performance | Step-dispatch p95 < 50ms excluding module execution (NFR-PERF-1) | Load test, tracked as a release gate (Volume 5 §10) |
| Performance | Flow CRUD p95 < 200ms (NFR-PERF-2) | Load test |
| Scalability | 500 concurrent flow executions (NFR-SCALE-1) | Load test at target concurrency |
| Scalability | 10M flow-run records supported via partitioning, activated at 5M-row threshold | Schema + operational runbook inspection |
| Reliability | 99.9% availability for the core execution path (NFR-REL-1) | SLO tracking per Volume 4 §7 |
| Reliability | Platform flake rate < 0.5% (NFR-REL-2) | Computed metric, release-gated |
| Security | Every security-relevant guarantee enforced at ≥2 independent layers (defense in depth) | Chaos test disabling one layer at a time |
| Security | No mandatory outbound internet dependency for core (non-AI) functionality (NFR-SEC-4) | Air-gapped deployment smoke test |
| Observability | One OTel trace per `FlowRun`; step-dispatch time cleanly separable from module-execution time in trace structure | Trace inspection |
| Compliance | Audit retention ≥1 year hot tier, no purge capability in the command catalog | Inspection |

## Assumptions

| ID | Assumption | Evidence | Status |
|----|------------|----------|--------|
| A-01 | `.NET 8` and PostgreSQL remain the locked backend technology choices for the duration of this initiative | Volume 0 §21.2, §21.8 | confirmed |
| A-02 | The in-memory Infrastructure implementation (`Yukti.Infrastructure.InMemory`) is explicitly temporary and will be replaced by a real EF Core/PostgreSQL implementation satisfying the identical `Yukti.Application` interfaces, with zero changes required to Domain/Application/Orchestration | README.md, this session | confirmed |
| A-03 | Sandboxed Community-tier module execution technology (container-per-invocation vs. gVisor vs. other) is not yet chosen | Volume 1 §18.5, Volume 4 §3.2/§3.8 | open — see Q-01 |
| A-04 | Workflow Engine (multi-flow DAG) remains a Should-priority, not Must-priority, GA capability | Volume 1 §20.5 | confirmed |

## Spec questions (ambiguities — need PM or tech-lead confirmation before feasibility)

| ID | Lane | Question | Blocking | Default if deferred | Status |
|----|------|----------|----------|----------------------|--------|
| Q-01 | Infra | Sandbox technology for Community-tier module execution — Kubernetes Job-per-invocation vs. gVisor vs. Firecracker microVM? | No | Kubernetes Job-per-invocation (simplest to operate; matches Volume 4 §3.2's `yukti-sandbox` namespace design) — confirm in technical review | open |
| Q-02 | Backend | Is per-step configurable `RetryPolicy` (vs. this session's single default policy for all steps) required for GA, or can it wait? | No | Default policy for GA; per-step config is additive later, no breaking change required | open |
| Q-03 | Backend | Real `Yukti.Infrastructure` (EF Core) — build against a fresh PostgreSQL schema, or does Volume 1 §32's schema need adjustment based on what the in-memory implementation revealed? | Yes — blocks real persistence work | No changes anticipated; confirm during technical review before EF Core build begins | open |

## Draft check summary

| Check | Status | Findings |
|-------|--------|----------|
| D1 — Source spec traceable | PASS | Every FR cites a specific Volume 1 section |
| D2 — Complete coverage, no concept dropped | PASS | All 6 Parts (§1–40) represented; Part I (System Foundations, C4 diagrams) intentionally excluded from FR-level treatment as it's architectural context, not testable behavior — see note below |
| D3 — As-built alignment | PASS | Baseline table reflects the actual, currently-building `yukti-platform` codebase, not just the doc |
| D4 — Observable acceptance criteria | PASS | Every FR has a concrete AC + evidence type |
| D5 — Negative/failure paths | PASS | 13 rows covering the highest-risk failure modes across every subsystem |
| D6 — Assumptions/open questions | PASS | 4 assumptions, 3 non-blocking-except-one open questions |
| D7 — Cross-layer contracts | PASS | 6 contracts covering Domain→Application, Contracts→Modules, Orchestration→Application, Api→Application, Orchestration→SignalR, external IdP→Api |
| D8 — NFR applicability | PASS | Performance, scalability, reliability, security, observability, compliance all represented |
| D9 — Dependency order | PASS | No upstream dependency; this is the foundational initiative |
| D10 — Zero unresolved blockers | PARTIAL | Q-03 blocks real EF Core Infrastructure work specifically; does not block continued Domain/Application/Orchestration work |
| D11 — Output completeness | PASS | Header, overview, baseline, FRs, negative paths, out-of-scope, contracts, NFRs, assumptions, questions, this checklist |

**Draft verdict:** PASS (with Q-03 flagged as blocking specifically for real-database work, not for continued work on already-in-scope layers)

## Note on Part I (System Foundations) scoping

Volume 1 Sections 1–5 (Executive Summary, System Context, C4 Context/Container/Component diagrams) are architectural narrative and visual documentation, not directly testable behavior — they inform *why* the FRs above are shaped the way they are (e.g., FR-OPS-01's container split traces to §4.4) but are cited as *sources*, not restated as their own FR rows, consistent with this PRD's goal of testable acceptance criteria over architectural prose restatement.

## References

- Source: Yukti Architecture Bible, Volume 1 — Backend Architecture (Parts I–VI, all 40 sections)
- Format basis: `INIT-ABHILEKH-002` (Dual-profile configuration releases spec), used as structural template only — no content from that initiative appears here
- As-built code: `yukti-platform` repository, commit `6dd3b66` ("feat(domain,orchestration): implement backend core per Volume 1")
- Related upstream: Volume 0 (Product Blueprint) — business objectives and NFR origin; Volume 5 §2–3 (PR guidelines, code review checklist) — the review process this PRD's FRs will be checked against once implementation PRs open

```yaml
handoff:
  contract: sdd-yukti/v1
  stage: backend-core-feasibility
  outcome: findings
  artifact:
    path: docs/specification/product/INIT-YUKTI-BACKEND-001.md
    fr_count: 94
  blockers:
    - Q-03
  signals:
    source_volume: 1
    source_sections: 40
    as_built_commit: 6dd3b66
    open_questions: [Q-01, Q-02, Q-03]
    next_candidates:
      - INIT-YUKTI-FRONTEND-001 (Volume 2)
      - INIT-YUKTI-AI-001 (Volume 3)
      - INIT-YUKTI-INFRA-001 (Volume 4)
  human_checkpoint: true
  external_action: false
```
