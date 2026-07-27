using Microsoft.AspNetCore.SignalR;

namespace Yukti.Api;

/// <summary>
/// FR-RT-01: clients join a group keyed by FlowRunId — JoinRun is the
/// only way in, so a client can never receive another run's events; there
/// is no "subscribe to everything" method on this hub at all.
///
/// FR-RT-03: horizontal scaling of multiple Yukti.Api instances needs a
/// Redis-backed SignalR backplane (AddStackExchangeRedis, wired in
/// Program.cs against the dedicated "yukti-redis" container) so an event
/// raised on worker A reaches a client connected via worker B.
/// </summary>
public sealed class RunProgressHub : Hub
{
    public static string GroupNameFor(Guid flowRunId) => $"run-{flowRunId}";

    // No trailing CancellationToken parameter, deliberately: SignalR only
    // auto-injects one for *streaming* hub methods. On a regular
    // invocation it counts as a client-supplied argument, so a client
    // calling JoinRun(runId) fails the arity check with
    // "Invocation provides 1 argument(s) but target expects 2" — found
    // live, testing this hub from a real browser client for the first
    // time (YUKTI003's trailing-CancellationToken rule doesn't apply to
    // hub methods for exactly this reason).
    public Task JoinRun(Guid flowRunId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupNameFor(flowRunId), Context.ConnectionAborted);

    public Task LeaveRun(Guid flowRunId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupNameFor(flowRunId), Context.ConnectionAborted);
}
