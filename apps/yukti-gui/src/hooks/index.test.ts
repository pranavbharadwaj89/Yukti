import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { useLiveRunProgress } from "./index";
import { runsApi } from "@/services/api-client";
import { ensureRunProgressConnected, getRunProgressConnection } from "@/services/signalr";
import type { FlowRunResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  runsApi: { get: vi.fn() },
}));

// A minimal fake matching the real @microsoft/signalr HubConnection shape
// this hook actually calls — real enough to drive the hook's logic, with
// test-only trigger* methods standing in for events a real server/network
// would raise. The real client has no off-counterpart for
// onreconnecting/onreconnected/onclose (confirmed against its .d.ts), so
// neither does this fake — matching that real constraint is the point.
function createFakeConnection() {
  const handlers: Record<string, Array<() => void>> = { StepCompleted: [], FlowRunCompleted: [] };
  const reconnecting: Array<() => void> = [];
  const reconnected: Array<() => void> = [];
  const closed: Array<() => void> = [];
  return {
    on: vi.fn((event: string, cb: () => void) => {
      (handlers[event] ??= []).push(cb);
    }),
    off: vi.fn((event: string, cb: () => void) => {
      handlers[event] = (handlers[event] ?? []).filter((h) => h !== cb);
    }),
    onreconnecting: vi.fn((cb: () => void) => reconnecting.push(cb)),
    onreconnected: vi.fn((cb: () => void) => reconnected.push(cb)),
    onclose: vi.fn((cb: () => void) => closed.push(cb)),
    invoke: vi.fn().mockResolvedValue(undefined),
    triggerReconnecting: () => reconnecting.forEach((h) => h()),
    triggerReconnected: () => reconnected.forEach((h) => h()),
    triggerClose: () => closed.forEach((h) => h()),
  };
}

type FakeConnection = ReturnType<typeof createFakeConnection>;
let fakeConnection: FakeConnection;

vi.mock("@/services/signalr", () => ({
  getRunProgressConnection: vi.fn(),
  ensureRunProgressConnected: vi.fn(),
}));

const run: FlowRunResponse = {
  id: "run-1",
  flowId: "flow-1",
  status: "Running",
  trigger: "Api",
  startedAt: new Date().toISOString(),
  results: [],
};

describe("useLiveRunProgress — D4-T4 connection state", () => {
  beforeEach(() => {
    vi.mocked(runsApi.get).mockResolvedValue(run);
    fakeConnection = createFakeConnection();
    vi.mocked(getRunProgressConnection).mockReturnValue(fakeConnection as never);
    vi.mocked(ensureRunProgressConnected).mockResolvedValue(fakeConnection as never);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("starts connected and never shows reconnecting/polling before anything has happened", async () => {
    const { result } = renderHook(() => useLiveRunProgress("run-1"));
    await waitFor(() => expect(result.current.run).not.toBeNull());
    expect(result.current.connectionState).toBe("connected");
  });

  it("shows reconnecting (amber, not failed) while SignalR is between retries, and re-syncs via REST on reconnected", async () => {
    const { result } = renderHook(() => useLiveRunProgress("run-1"));
    await waitFor(() => expect(result.current.run).not.toBeNull());
    vi.mocked(runsApi.get).mockClear();

    act(() => fakeConnection.triggerReconnecting());
    expect(result.current.connectionState).toBe("reconnecting");
    expect(result.current.error).toBeNull(); // never shown as a run failure

    act(() => fakeConnection.triggerReconnected());
    expect(result.current.connectionState).toBe("connected");
    await waitFor(() => expect(runsApi.get).toHaveBeenCalledWith("run-1")); // FR-RT-02: re-syncs on reconnect
  });

  it("falls back to polling every 5s when SignalR gives up entirely (onclose), with a distinct message from reconnecting", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { result } = renderHook(() => useLiveRunProgress("run-1"));
    await waitFor(() => expect(result.current.run).not.toBeNull());
    vi.mocked(runsApi.get).mockClear();

    act(() => fakeConnection.triggerClose());
    expect(result.current.connectionState).toBe("polling");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(runsApi.get).toHaveBeenCalledTimes(1);

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(runsApi.get).toHaveBeenCalledTimes(2);
  });

  it("falls back to polling immediately if the initial connection/JoinRun attempt fails outright, not just on a later disconnect", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.mocked(ensureRunProgressConnected).mockRejectedValue(new Error("negotiate failed"));
    const { result } = renderHook(() => useLiveRunProgress("run-1"));

    await waitFor(() => expect(result.current.connectionState).toBe("polling"));
    vi.mocked(runsApi.get).mockClear();

    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000);
    });
    expect(runsApi.get).toHaveBeenCalledTimes(1);
  });

  it("a stale onclose/onreconnecting/onreconnected firing after unmount does nothing — no surviving interval, no state update", async () => {
    // SARVAGHNA caught this in review: since the real SignalR client has no
    // off() for these three, a stale handler stays registered on the
    // shared connection forever. Before the cancelled-guard fix, a stale
    // onclose firing after unmount called startPolling(), which created a
    // brand-new setInterval this already-torn-down closure could never
    // clear again — an unbounded background poll for a page the user left.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { result, unmount } = renderHook(() => useLiveRunProgress("run-1"));
    await waitFor(() => expect(result.current.run).not.toBeNull());

    unmount();
    vi.mocked(runsApi.get).mockClear();

    act(() => {
      fakeConnection.triggerClose();
      fakeConnection.triggerReconnecting();
      fakeConnection.triggerReconnected();
    });

    await act(async () => {
      await vi.advanceTimersByTimeAsync(20000); // several poll intervals' worth, if one had started
    });
    expect(runsApi.get).not.toHaveBeenCalled();
  });

  it("stops polling and returns to connected once a real reconnect succeeds, without a second update path left running", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { result } = renderHook(() => useLiveRunProgress("run-1"));
    await waitFor(() => expect(result.current.run).not.toBeNull());

    act(() => fakeConnection.triggerClose());
    expect(result.current.connectionState).toBe("polling");

    act(() => fakeConnection.triggerReconnected());
    expect(result.current.connectionState).toBe("connected");

    vi.mocked(runsApi.get).mockClear();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(10000);
    });
    // The old poll interval must actually be cleared — not just ignored —
    // or two update paths (poll + live) would both be calling runsApi.get.
    expect(runsApi.get).not.toHaveBeenCalled();
  });
});
