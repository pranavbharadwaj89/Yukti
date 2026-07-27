using Microsoft.AspNetCore.SignalR;

namespace Yukti.Api;

/// <summary>
/// FR-RT-01: clients join a group keyed by FlowRunId — JoinRun is the
/// only way in, so a client can never receive another run's events; there
/// is no "subscribe to everything" method on this hub at all.
///
/// FR-RT-03: horizontal scaling of multiple Yukti.Api instances needs a
/// Redis-backed SignalR backplane (AddStackExchangeRedis) so an event
/// raised on worker A reaches a client connected via worker B — not
/// deployed in this environment, the same documented category of gap as
/// the Redis-backed rate limiter and distributed trigger lock elsewhere
/// in this repo. Single-instance operation (this environment's actual
/// deployment shape) works correctly without it.
/// </summary>
public sealed class RunProgressHub : Hub
{
    public static string GroupNameFor(Guid flowRunId) => $"run-{flowRunId}";

    public Task JoinRun(Guid flowRunId, CancellationToken ct) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupNameFor(flowRunId), ct);

    public Task LeaveRun(Guid flowRunId, CancellationToken ct) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameFor(flowRunId), ct);
}
