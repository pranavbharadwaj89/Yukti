using System.Security.Cryptography;
using Yukti.Domain.SharedKernel;

namespace Yukti.Domain.Scheduling;

/// <summary>FR-SCHED-01: the three supported trigger kinds.</summary>
public enum TriggerKind { Cron, Webhook, FileWatch }

/// <summary>
/// FR-SCHED (Volume 1 Part III §21): a trigger is purely "when/how a
/// FlowRun gets started" — it holds no execution logic of its own. Every
/// kind converges on TriggerFlowRunCommand (FR-SCHED-02); this aggregate's
/// only job is deciding *whether* now is the moment to issue that command,
/// via ShouldFireForCron/webhook signature verification below, and
/// recording that it did.
/// </summary>
public sealed class TriggerDefinition : AggregateRoot<TriggerId>
{
    public FlowId FlowId { get; }
    public TenantId TenantId { get; }
    // The trigger fires TriggerFlowRunCommand "as" whoever registered it —
    // FR-AUTHZ-02's EnsurePermission needs a real UserId to check
    // FlowExecute against, and there's no human present at fire time to
    // supply one.
    public UserId RegisteredBy { get; }
    public TriggerKind Kind { get; }
    public bool IsEnabled { get; private set; }
    public DateTimeOffset? LastFiredAt { get; private set; }

    // Cron
    public string? CronExpression { get; }

    // Webhook — FR-SCHED-04: Path is a generated high-entropy token (not a
    // guessable slug), Secret is an optional shared secret for HMAC
    // signature verification. Both null for non-Webhook triggers.
    public string? WebhookPath { get; }
    public string? WebhookSecret { get; }

    // FileWatch
    public string? WatchPath { get; }

    private TriggerDefinition(TriggerId id, FlowId flowId, TenantId tenantId, UserId registeredBy, TriggerKind kind,
        string? cronExpression, string? webhookPath, string? webhookSecret, string? watchPath)
        : base(id)
    {
        FlowId = flowId;
        TenantId = tenantId;
        RegisteredBy = registeredBy;
        Kind = kind;
        IsEnabled = true;
        CronExpression = cronExpression;
        WebhookPath = webhookPath;
        WebhookSecret = webhookSecret;
        WatchPath = watchPath;
    }

    public static TriggerDefinition CreateCron(FlowId flowId, TenantId tenantId, UserId registeredBy, string cronExpression)
    {
        CronExpressionEvaluator.Validate(cronExpression); // throws DomainException if malformed
        return new TriggerDefinition(TriggerId.New(), flowId, tenantId, registeredBy, TriggerKind.Cron,
            cronExpression, webhookPath: null, webhookSecret: null, watchPath: null);
    }

    /// <summary>Generates its own unguessable path — callers never choose one, closing off the
    /// obvious "reuse a predictable slug" mistake at the type level.</summary>
    public static TriggerDefinition CreateWebhook(FlowId flowId, TenantId tenantId, UserId registeredBy, string? signingSecret)
    {
        var path = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        return new TriggerDefinition(TriggerId.New(), flowId, tenantId, registeredBy, TriggerKind.Webhook,
            cronExpression: null, webhookPath: path, webhookSecret: signingSecret, watchPath: null);
    }

    /// <summary>FR-SCHED-01: "FileWatch scoped to self-hosted deployments only" — the caller
    /// (Application layer, which knows the deployment mode) must assert that explicitly; this
    /// constructor cannot infer it, but refuses to proceed silently if told it's not self-hosted.</summary>
    public static TriggerDefinition CreateFileWatch(FlowId flowId, TenantId tenantId, UserId registeredBy, string watchPath, bool isSelfHostedDeployment)
    {
        if (!isSelfHostedDeployment)
            throw new DomainException("FileWatch triggers are only available in self-hosted deployments.");
        if (string.IsNullOrWhiteSpace(watchPath))
            throw new DomainException("FileWatch trigger requires a non-empty watch path.");
        return new TriggerDefinition(TriggerId.New(), flowId, tenantId, registeredBy, TriggerKind.FileWatch,
            cronExpression: null, webhookPath: null, webhookSecret: null, watchPath: watchPath);
    }

    public void Disable() => IsEnabled = false;
    public void Enable() => IsEnabled = true;

    public void RecordFired(DateTimeOffset firedAt) => LastFiredAt = firedAt;
}
