using Yukti.Domain.Execution;
using Yukti.Domain.ModulePlugin;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Events;

// Every domain event is immutable, past-tense named, and carries only the
// data its known consumers actually need. (Volume 1 Part II §12)

public sealed record FlowPublishedEvent(
    FlowId FlowId, FlowFamilyId FamilyId, int Version, TenantId TenantId, UserId PublishedBy, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record FlowArchivedEvent(
    FlowId FlowId, TenantId TenantId, UserId ArchivedBy, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record FlowRunStartedEvent(
    FlowRunId RunId, FlowId FlowId, RunTrigger Trigger, TenantId TenantId, DateTimeOffset OccurredAt
) : IDomainEvent;

/// <summary>
/// The single highest-frequency event in the system — one per step, per
/// run. Direct data source for SignalR's live progress streaming; carries
/// the full StepResult since the GUI needs the complete outcome immediately.
/// </summary>
public sealed record StepCompletedEvent(
    FlowRunId RunId, StepResult Result, DateTimeOffset OccurredAt
) : IDomainEvent;

/// <summary>
/// Raised in addition to (never instead of) StepCompletedEvent whenever a
/// step's AiAttribution is non-null (FR-AI-3).
/// </summary>
public sealed record StepSelfHealedEvent(
    FlowRunId RunId, StepResultId ResultId, AiAttribution Attribution, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record FlowRunCompletedEvent(
    FlowRunId RunId, RunStatus FinalStatus, int PassedCount, int FailedCount, int SkippedCount,
    TimeSpan TotalDuration, DateTimeOffset OccurredAt
) : IDomainEvent;

/// <summary>Feeds the platform flake-rate metric (NFR-REL-2) as a first-class, independently queryable event.</summary>
public sealed record FlowRunFlakeDetectedEvent(
    FlowRunId RunId, StepResultId ResultId, int RetryCount, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record ModuleRegisteredEvent(
    ModuleRegistrationId RegistrationId, ModuleKind Kind, TrustTier Trust, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record ModuleDeprecatedActionEvent(
    ModuleRegistrationId RegistrationId, string ActionName, string Notice, DateTimeOffset OccurredAt
) : IDomainEvent;

// Identity & Access context events (Volume 1 Part IV §25).

public sealed record UserRegisteredEvent(
    UserId UserId, string Email, TenantId TenantId, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record UserRoleAssignedEvent(
    UserId UserId, RoleId RoleId, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record RoleCreatedEvent(
    RoleId RoleId, string Name, TenantId? TenantId, int Version, DateTimeOffset OccurredAt
) : IDomainEvent;

/// <summary>Feeds FR-AUTHZ-04's mid-session revocation guarantee — a consumer could invalidate cached tokens/sessions on sight of this event, though PermissionChecker's always-live-lookup design doesn't itself depend on that.</summary>
public sealed record RolePermissionsUpdatedEvent(
    RoleId RoleId, int NewVersion, DateTimeOffset OccurredAt
) : IDomainEvent;
