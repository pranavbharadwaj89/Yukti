import * as signalR from "@microsoft/signalr";
import { useAuthStore } from "@/store/auth-store";

// FR-RT-01/03: one hub connection, group-scoped per FlowRunId by the server
// (RunProgressHub.JoinRun). FR-RT-02: callers must always do a REST
// catch-up fetch on connect/reconnect before trusting pushed events — this
// module only owns the transport, not that reconciliation logic (see
// useLiveRunProgress in hooks/index.ts).
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000";

let connection: signalR.HubConnection | null = null;
let startPromise: Promise<void> | null = null;

/**
 * Starts the shared connection at most once, even when several components
 * mount at the same time. Found live: two callers each checking
 * `state === "Disconnected"` both raced into start(), and the loser tried
 * to invoke on a still-connecting connection — "Cannot send data if the
 * connection is not in the 'Connected' State". Awaiting one shared promise
 * makes every caller wait for the same start rather than starting again.
 */
export async function ensureRunProgressConnected(): Promise<signalR.HubConnection> {
  const conn = getRunProgressConnection();
  if (conn.state === signalR.HubConnectionState.Connected) return conn;
  if (!startPromise) {
    startPromise = conn.start().finally(() => {
      startPromise = null;
    });
  }
  await startPromise;
  return conn;
}

export function getRunProgressConnection(): signalR.HubConnection {
  if (connection) return connection;
  connection = new signalR.HubConnectionBuilder()
    .withUrl(`${BASE_URL}/hubs/run-progress`, {
      accessTokenFactory: () => useAuthStore.getState().accessToken ?? "",
      // Found live: SignalR's default withCredentials:true sends the
      // negotiate request in "include" credentials mode, which a
      // cross-origin (different port = different origin) CORS response
      // must answer with Access-Control-Allow-Credentials — this app
      // never sends cookies at all (bearer token only), so there's
      // nothing to include; disabling it is the correct fix, not a
      // workaround for a missing server header.
      withCredentials: false,
    })
    .withAutomaticReconnect()
    .build();
  return connection;
}
