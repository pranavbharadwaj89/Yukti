import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor, act } from "@testing-library/react";
import { useLiveRunProgress } from "./index";
import { runsApi } from "@/services/api-client";
import { ensureRunProgressConnected, getRunProgressConnection, subscribeToRunProgressLifecycle } from "@/services/signalr";
import type { FlowRunResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  runsApi: { get: vi.fn() },
}));

// A minimal fake matching the real @microsoft/signalr HubConnection shape
// this hook calls directly — on/off/invoke. Reconnecting/reconnected/close
// no longer go through the connection at all (DISC-6): the real client has
// no off() for those three, so hooks/index.ts now goes through
// subscribeToRunProgressLifecycle instead, which this file fakes below with
// a real removable Set, matching signalr.ts's own pattern.
function createFakeConnection() {
  const handlers: Record<string, Array<() => void>> = { StepCompleted: [], FlowRunCompleted: [] };
  return {
    on: vi.fn((event: string, cb: () => void) => {
      (handlers[event] ??= []).push(cb);
    }),
    off: vi.fn((event: string, cb: () => void) => {
      handlers[event] = (handlers[event] ?? []).filter((h) => h !== cb);
    }),
    invoke: vi.fn().mockResolvedValue(undefined),
  };
}

type FakeConnection = ReturnType<typeof createFakeConnection>;
let fakeConnection: FakeConnection;

type LifecycleListener = { onReconnecting?: () => void; onReconnected?: () => void; onClose?: () => void };
let lifecycleListeners: Set<LifecycleListener>;

vi.mock("@/services/signalr", () => ({
  getRunProgressConnection: vi.fn(),
  ensureRunProgressConnected: vi.fn(),
  subscribeToRunProgressLifecycle: vi.fn(),
}));

const triggerReconnecting = () => lifecycleListeners.forEach((l) => l.onReconnecting?.());
const triggerReconnected = () => lifecycleListeners.forEach((l) => l.onReconnected?.());
const triggerClose = () => lifecycleListeners.forEach((l) => l.onClose?.());

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
    lifecycleListeners = new Set();
    vi.mocked(getRunProgressConnection).mockReturnValue(fakeConnection as never);
    vi.mocked(ensureRunProgressConnected).mockResolvedValue(fakeConnection as never);
    vi.mocked(subscribeToRunProgressLifecycle).mockImplementation((listener: LifecycleListener) => {
      lifecycleListeners.add(listener);
      return () => lifecycleListeners.delete(listener);
    });
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

    act(() => triggerReconnecting());
    expect(result.current.connectionState).toBe("reconnecting");
    expect(result.current.error).toBeNull(); // never shown as a run failure

    act(() => triggerReconnected());
    expect(result.current.connectionState).toBe("connected");
    await waitFor(() => expect(runsApi.get).toHaveBeenCalledWith("run-1")); // FR-RT-02: re-syncs on reconnect
  });

  it("falls back to polling every 5s when SignalR gives up entirely (onclose), with a distinct message from reconnecting", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { result } = renderHook(() => useLiveRunProgress("run-1"));
    await waitFor(() => expect(result.current.run).not.toBeNull());
    vi.mocked(runsApi.get).mockClear();

    act(() => triggerClose());
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

  it("unsubscribes its lifecycle listener on unmount, so no stale handler can leak across mounts (DISC-6)", async () => {
    // Before this fix, onreconnecting/onreconnected/onclose were registered
    // directly on the shared connection, which has no off() for them — every
    // mount left one more unremovable handler behind forever. Now cleanup
    // actually calls the unsubscribe this hook got back, which is provable
    // here because the fake's listener Set size must drop back to 0.
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { result, unmount } = renderHook(() => useLiveRunProgress("run-1"));
    await waitFor(() => expect(result.current.run).not.toBeNull());
    expect(lifecycleListeners.size).toBe(1);

    unmount();
    expect(lifecycleListeners.size).toBe(0);

    vi.mocked(runsApi.get).mockClear();
    act(() => {
      triggerClose();
      triggerReconnecting();
      triggerReconnected();
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

    act(() => triggerClose());
    expect(result.current.connectionState).toBe("polling");

    act(() => triggerReconnected());
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
