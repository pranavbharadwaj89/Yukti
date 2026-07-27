using Xunit;
using Yukti.Domain.Scheduling;
using Yukti.Domain.SharedKernel;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Orchestration.Tests;

public sealed class TriggerLockTests
{
    [Fact]
    public async Task TryAcquire_succeeds_exactly_once_for_the_same_trigger_and_tick()
    {
        // FR-SCHED-03's core guarantee, minus the "N instances" part real
        // Redis-backed cross-instance locking would need — this proves the
        // (TriggerId, tick-window) key design actually dedupes.
        var triggerLock = new InMemoryTriggerLock();
        var triggerId = TriggerId.New();
        var tick = DateTimeOffset.UtcNow;

        var first = await triggerLock.TryAcquire(triggerId, tick, CancellationToken.None);
        var second = await triggerLock.TryAcquire(triggerId, tick, CancellationToken.None);

        Assert.True(first);
        Assert.False(second);
    }

    [Fact]
    public async Task TryAcquire_succeeds_independently_for_different_ticks_of_the_same_trigger()
    {
        var triggerLock = new InMemoryTriggerLock();
        var triggerId = TriggerId.New();
        var now = DateTimeOffset.UtcNow;

        var tick1 = await triggerLock.TryAcquire(triggerId, now, CancellationToken.None);
        var tick2 = await triggerLock.TryAcquire(triggerId, now.AddMinutes(1), CancellationToken.None);

        Assert.True(tick1);
        Assert.True(tick2);
    }
}

public sealed class TriggerDefinitionTests
{
    [Fact]
    public void CreateCron_produces_an_enabled_Cron_trigger()
    {
        var trigger = TriggerDefinition.CreateCron(FlowId.New(), TenantId.New(), UserId.New(), "0 9 * * 1");

        Assert.Equal(TriggerKind.Cron, trigger.Kind);
        Assert.True(trigger.IsEnabled);
        Assert.Equal("0 9 * * 1", trigger.CronExpression);
    }

    [Fact]
    public void CreateCron_rejects_a_malformed_expression()
    {
        Assert.Throws<DomainException>(() =>
            TriggerDefinition.CreateCron(FlowId.New(), TenantId.New(), UserId.New(), "not a cron"));
    }

    [Fact]
    public void CreateWebhook_generates_a_high_entropy_unguessable_path()
    {
        var trigger = TriggerDefinition.CreateWebhook(FlowId.New(), TenantId.New(), UserId.New(), signingSecret: "shh");

        Assert.Equal(TriggerKind.Webhook, trigger.Kind);
        Assert.NotNull(trigger.WebhookPath);
        Assert.True(trigger.WebhookPath!.Length >= 64); // 32 random bytes, hex-encoded
    }

    [Fact]
    public void CreateFileWatch_rejects_non_self_hosted_deployments()
    {
        Assert.Throws<DomainException>(() =>
            TriggerDefinition.CreateFileWatch(FlowId.New(), TenantId.New(), UserId.New(), "/var/watch", isSelfHostedDeployment: false));
    }

    [Fact]
    public void CreateFileWatch_succeeds_for_self_hosted_deployments()
    {
        var trigger = TriggerDefinition.CreateFileWatch(FlowId.New(), TenantId.New(), UserId.New(), "/var/watch", isSelfHostedDeployment: true);

        Assert.Equal(TriggerKind.FileWatch, trigger.Kind);
        Assert.Equal("/var/watch", trigger.WatchPath);
    }
}

public sealed class CronExpressionEvaluatorTests
{
    [Fact]
    public void Matches_true_for_an_exact_field_match()
    {
        // "0 9 * * *" — every day at 09:00
        var at = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        Assert.True(CronExpressionEvaluator.Matches("0 9 * * *", at));
    }

    [Fact]
    public void Matches_false_when_the_hour_does_not_match()
    {
        var at = new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero);
        Assert.False(CronExpressionEvaluator.Matches("0 9 * * *", at));
    }

    [Fact]
    public void Validate_throws_on_wrong_field_count()
    {
        Assert.Throws<DomainException>(() => CronExpressionEvaluator.Validate("0 9 * *"));
    }

    [Fact]
    public void GetMissedTicks_fires_once_for_each_missed_minute_within_the_catchup_window()
    {
        // "* * * * *" fires every minute. lastFiredAt 3 minutes ago, now — 3 missed ticks.
        var now = new DateTimeOffset(2026, 1, 5, 9, 5, 0, TimeSpan.Zero);
        var lastFiredAt = now.AddMinutes(-3);

        var ticks = CronExpressionEvaluator.GetMissedTicks("* * * * *", lastFiredAt, now, TimeSpan.FromHours(1));

        Assert.Equal(3, ticks.Count);
    }

    [Fact]
    public void GetMissedTicks_does_not_flood_fire_beyond_the_catchup_window()
    {
        // Outage far longer than the catch-up window — only ticks within
        // the window fire; older ones are skipped, not queued.
        var now = new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero);
        var lastFiredAt = now.AddDays(-10);
        var catchUpWindow = TimeSpan.FromMinutes(15);

        var ticks = CronExpressionEvaluator.GetMissedTicks("* * * * *", lastFiredAt, now, catchUpWindow);

        Assert.Equal(15, ticks.Count); // exactly the window's worth of minutes, not 10 days' worth
    }
}

public sealed class WebhookSignatureVerifierTests
{
    [Fact]
    public void Verify_returns_true_when_no_secret_is_configured()
    {
        Assert.True(WebhookSignatureVerifier.Verify(null, "{}", providedSignatureHex: null));
    }

    [Fact]
    public void Verify_rejects_a_missing_signature_when_a_secret_is_configured()
    {
        Assert.False(WebhookSignatureVerifier.Verify("secret", "{}", providedSignatureHex: null));
    }

    [Fact]
    public void Verify_rejects_a_mismatched_signature()
    {
        Assert.False(WebhookSignatureVerifier.Verify("secret", "{}", providedSignatureHex: "deadbeef"));
    }

    [Fact]
    public void Verify_accepts_a_correctly_computed_signature()
    {
        const string secret = "shared-secret";
        const string body = """{"flowId":"abc"}""";
        var correctSignature = Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(secret), System.Text.Encoding.UTF8.GetBytes(body)));

        Assert.True(WebhookSignatureVerifier.Verify(secret, body, correctSignature));
    }
}
