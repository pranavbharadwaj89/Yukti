using Microsoft.AspNetCore.SignalR;
using Yukti.Domain.Events;
using Yukti.Infrastructure.InMemory;

namespace Yukti.Api;

/// <summary>
/// FR-EVT-03: subscribes Tier 1 ONLY (InMemoryDomainEventDispatcher's
/// synchronous in-process pub/sub) — never the durable outbox. Live
/// progress is inherently best-effort/ephemeral; if this subscription
/// misses an event (a Tier-1-only outage), FR-RT-02's REST catch-up fetch
/// on reconnect is what makes the client whole again, not redelivery from
/// Tier 2. Runs once at startup, for the process's lifetime — there's
/// nothing to poll, so this isn't a BackgroundService, just a subscriber.
/// </summary>
public static class RunProgressBridge
{
    public static void Wire(InMemoryDomainEventDispatcher dispatcher, IHubContext<RunProgressHub> hub)
    {
        dispatcher.Subscribe<StepCompletedEvent>(evt =>
            _ = hub.Clients.Group(RunProgressHub.GroupNameFor(evt.RunId.Value)).SendAsync("stepCompleted", evt));

        dispatcher.Subscribe<FlowRunCompletedEvent>(evt =>
            _ = hub.Clients.Group(RunProgressHub.GroupNameFor(evt.RunId.Value)).SendAsync("runCompleted", evt));
    }
}
