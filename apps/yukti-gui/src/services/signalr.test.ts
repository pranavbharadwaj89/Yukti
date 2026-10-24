import { describe, it, expect, vi, beforeEach } from "vitest";

// DISC-6: @microsoft/signalr's real HubConnection has no off() for
// onreconnecting/onreconnected/onclose, so this proves the production fix
// directly — wireLifecycleDispatch registers the three real callbacks on
// the connection exactly once, and subscribeToRunProgressLifecycle's
// returned unsubscribe actually drops a listener out of the fan-out.
function createFakeConnection() {
  return {
    onreconnecting: vi.fn(),
    onreconnected: vi.fn(),
    onclose: vi.fn(),
    start: vi.fn().mockResolvedValue(undefined),
    state: "Disconnected",
  };
}

let fakeConnection: ReturnType<typeof createFakeConnection>;

vi.mock("@microsoft/signalr", () => {
  class HubConnectionBuilder {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    build() {
      return fakeConnection;
    }
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: { Connected: "Connected", Disconnected: "Disconnected" },
  };
});

vi.mock("@/store/auth-store", () => ({
  useAuthStore: { getState: () => ({ accessToken: "test-token" }) },
}));

describe("signalr.ts — run-progress lifecycle fan-out", () => {
  beforeEach(() => {
    vi.resetModules();
    fakeConnection = createFakeConnection();
  });

  it("wires onreconnecting/onreconnected/onclose on the shared connection exactly once, and fans each out to every subscriber", async () => {
    const { getRunProgressConnection, subscribeToRunProgressLifecycle } = await import("./signalr");

    getRunProgressConnection();
    getRunProgressConnection(); // same singleton — must not re-wire
    expect(fakeConnection.onreconnecting).toHaveBeenCalledTimes(1);
    expect(fakeConnection.onreconnected).toHaveBeenCalledTimes(1);
    expect(fakeConnection.onclose).toHaveBeenCalledTimes(1);

    const seen: string[] = [];
    subscribeToRunProgressLifecycle({ onReconnecting: () => seen.push("a") });
    subscribeToRunProgressLifecycle({ onReconnecting: () => seen.push("b") });

    const realOnReconnecting = fakeConnection.onreconnecting.mock.calls[0][0] as () => void;
    realOnReconnecting();

    expect(seen).toEqual(["a", "b"]);
  });

  it("unsubscribe actually removes the listener from the fan-out, not just a soft no-op guard", async () => {
    const { getRunProgressConnection, subscribeToRunProgressLifecycle } = await import("./signalr");
    getRunProgressConnection();

    const seen: string[] = [];
    const unsubscribe = subscribeToRunProgressLifecycle({ onClose: () => seen.push("fired") });
    const realOnClose = fakeConnection.onclose.mock.calls[0][0] as () => void;

    realOnClose();
    expect(seen).toEqual(["fired"]);

    unsubscribe();
    realOnClose();
    expect(seen).toEqual(["fired"]); // not called a second time
  });
});
