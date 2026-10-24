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
  wireLifecycleDispatch(connection);
  return connection;
}

export interface RunProgressLifecycleListener {
  onReconnecting?: () => void;
  onReconnected?: () => void;
  onClose?: () => void;
}

const lifecycleListeners = new Set<RunProgressLifecycleListener>();
let lifecycleWired = false;

// DISC-6: the real @microsoft/signalr client has no off() for
// onreconnecting/onreconnected/onclose, so a caller (e.g.
// useLiveRunProgress) registering directly on the shared connection would
// leak one more unremovable handler per mount for the lifetime of the page.
// Registering the three real callbacks here exactly once, for the
// connection's lifetime, and fanning each event out to this removable Set
// gives callers a real unsubscribe.
function wireLifecycleDispatch(conn: signalR.HubConnection): void {
  if (lifecycleWired) return;
  lifecycleWired = true;
  conn.onreconnecting(() => {
    for (const listener of lifecycleListeners) listener.onReconnecting?.();
  });
  conn.onreconnected(() => {
    for (const listener of lifecycleListeners) listener.onReconnected?.();
  });
  conn.onclose(() => {
    for (const listener of lifecycleListeners) listener.onClose?.();
  });
}

export function subscribeToRunProgressLifecycle(listener: RunProgressLifecycleListener): () => void {
  lifecycleListeners.add(listener);
  return () => lifecycleListeners.delete(listener);
}
