import { useEffect, useRef, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useAuthStore } from "@/store/auth-store";
import { useProjectStore } from "@/store/project-store";
import { ensureRunProgressConnected, getRunProgressConnection, subscribeToRunProgressLifecycle } from "@/services/signalr";
import { runsApi, environmentsApi } from "@/services/api-client";
import type { FlowRunResponse } from "@/services/types";

// FR-AUTHZ-02/FR-SEC-02: UX-convenience-only gating — real enforcement is
// always server-side (EnsurePermission on every command handler). This
// hook decodes the same JWT the API client attaches, never a separate
// client-maintained permission cache.
export function usePermission(): { hasAnyRole: boolean } {
  const user = useAuthStore((s) => s.user);
  return { hasAnyRole: !!user };
}

// D4-T4: what the UI shows for the live connection, distinct from `run`/
// `error` (the run's own data) — "reconnecting" must never render as if
// the run itself failed. "polling" is SignalR's automatic reconnect
// giving up entirely (onclose, not onreconnecting), falling back to REST
// every 5s rather than leaving the view silently stale forever.
export type LiveConnectionState = "connected" | "reconnecting" | "polling";
const POLL_INTERVAL_MS = 5000;

// FR-RT-01/02: joins the run's SignalR group, but the source of truth on
// mount/reconnect is always the REST catch-up fetch — pushed events only
// update state *after* that fetch resolves, never assumed to be complete
// on their own (a client that only trusted pushes could miss everything
// that happened before it subscribed).
export function useLiveRunProgress(runId: string | undefined) {
  const [run, setRun] = useState<FlowRunResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [connectionState, setConnectionState] = useState<LiveConnectionState>("connected");
  const runRef = useRef(run);
  runRef.current = run;

  useEffect(() => {
    if (!runId) return;
    let cancelled = false;
    let pollTimer: ReturnType<typeof setInterval> | undefined;
    const connection = getRunProgressConnection();

    const catchUp = async () => {
      try {
        const fresh = await runsApi.get(runId);
        if (!cancelled) setRun(fresh);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : "Failed to load run.");
      }
    };

    // SignalR's automatic reconnect retries a few times, then gives up for
    // good (onclose) — there's no further event telling us it's back, so
    // REST polling is the only way left to stay current. Idempotent: a
    // second onclose (or the initial JoinRun failure below) while already
    // polling is a no-op, not a second interval.
    const startPolling = () => {
      if (pollTimer) return;
      setConnectionState("polling");
      pollTimer = setInterval(() => void catchUp(), POLL_INTERVAL_MS);
    };
    const stopPolling = () => {
      if (!pollTimer) return;
      clearInterval(pollTimer);
      pollTimer = undefined;
    };

    const onStepCompleted = () => {
      // Server pushes the event; we re-fetch rather than hand-patch
      // client state, so a missed field never silently diverges.
      void catchUp();
    };
    const onRunCompleted = () => void catchUp();
    // DISC-6: these three go through subscribeToRunProgressLifecycle, not
    // connection.onreconnecting/onreconnected/onclose directly — that gives
    // this effect's cleanup a real unsubscribe, so a stale handler can no
    // longer fire after unmount at all (see signalr.ts).
    const onReconnecting = () => setConnectionState("reconnecting");
    const onReconnected = () => {
      stopPolling(); // shouldn't be polling at this point, but never leave two update paths running
      setConnectionState("connected");
      void catchUp(); // FR-RT-02: reconnect always re-syncs first
    };
    const onClose = () => startPolling();

    connection.on("StepCompleted", onStepCompleted);
    connection.on("FlowRunCompleted", onRunCompleted);
    const unsubscribeLifecycle = subscribeToRunProgressLifecycle({ onReconnecting, onReconnected, onClose });

    // Catch-up is unconditional and comes first — a client that saw
    // JoinRun fail should still show the run's current state via REST.
    // Live pushes are a strictly additive enhancement on top of that
    // baseline, never a prerequisite for it (this is what a client that
    // "only trusted pushes" — the failure mode FR-RT-02 exists to
    // prevent — would get wrong).
    void catchUp();
    (async () => {
      await ensureRunProgressConnected();
      await connection.invoke("JoinRun", runId);
    })().catch((e) => {
      // Live updates unavailable from the start — same fallback as losing
      // an established connection, not just a console warning nobody sees.
      console.warn("Live run updates unavailable, falling back to REST-only:", e);
      if (!cancelled) startPolling();
    });

    return () => {
      cancelled = true;
      stopPolling();
      connection.off("StepCompleted", onStepCompleted);
      connection.off("FlowRunCompleted", onRunCompleted);
      unsubscribeLifecycle();
    };
  }, [runId]);

  return { run, error, connectionState };
}

// The selected TestEnvironment's Variables, ready to pass straight through
// as flowsApi.triggerRun's variableOverrides — same object shape by design
// (TestEnvironment.cs's doc comment), so no per-call-site translation is
// needed. Returns undefined when nothing is selected, matching
// triggerRun's optional param.
export function useSelectedEnvironmentVariables(): Record<string, unknown> | undefined {
  const selectedProjectId = useProjectStore((s) => s.selectedProjectId);
  const selectedEnvironmentId = useProjectStore((s) => s.selectedEnvironmentId);
  const environmentsQuery = useQuery({
    queryKey: ["environments", selectedProjectId],
    queryFn: () => environmentsApi.list(selectedProjectId!),
    enabled: !!selectedProjectId,
  });
  return environmentsQuery.data?.find((e) => e.id === selectedEnvironmentId)?.variables;
}
