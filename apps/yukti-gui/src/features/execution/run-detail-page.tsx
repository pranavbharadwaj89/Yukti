import { useEffect, useState } from "react";
import { useParams } from "@tanstack/react-router";
import { useMutation } from "@tanstack/react-query";
import { useLiveRunProgress } from "@/hooks";
import { runsApi, screenshotsApi, ApiError } from "@/services/api-client";
import { Button, Card, Spinner, StatusPill } from "@/components/ui/primitives";
import { useToastStore } from "@/store/toast-store";

// D6-T5: a module-agnostic convention, not a branch on module kind (SUTRADHAR §2.1) — any step's
// Data carrying a string "path" is treated as a screenshot reference, whether it came from
// web.screenshot or ui.screenshot (DesktopUiModule). Neither this page nor the API special-cases
// either module by name.
function screenshotFileName(data: unknown): string | null {
  if (typeof data !== "object" || data === null) return null;
  const path = (data as Record<string, unknown>).path;
  return typeof path === "string" ? path : null;
}

function ScreenshotThumbnail({ runId, fileName }: { runId: string; fileName: string }) {
  const [objectUrl, setObjectUrl] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let createdUrl: string | null = null;
    screenshotsApi
      .fetchObjectUrl(runId, fileName)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        createdUrl = url;
        setObjectUrl(url);
      })
      .catch(() => {
        /* best-effort — a step's own pass/fail/error already carries the signal that matters */
      });
    return () => {
      cancelled = true;
      if (createdUrl) URL.revokeObjectURL(createdUrl);
    };
  }, [runId, fileName]);

  if (!objectUrl) return null;
  return <img src={objectUrl} alt={`Screenshot: ${fileName}`} className="mt-2 max-h-48 rounded-md border border-border" />;
}

// FR-FEAT-03/FR-CL-05: Execution Monitor composing live + historical run
// view with a scrolling Execution Console. FR-RT-02's catch-up-before-
// trusting-pushes contract lives in useLiveRunProgress, not here.
export function RunDetailPage() {
  const { runId } = useParams({ strict: false }) as { runId: string };
  const { run, error, connectionState } = useLiveRunProgress(runId);
  const pushToast = useToastStore((s) => s.push);

  const cancelMutation = useMutation({
    mutationFn: () => runsApi.cancel(runId),
    onSuccess: () => pushToast({ kind: "success", message: "Cancellation requested." }),
    onError: (err) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : "Failed to cancel run.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      }),
  });

  if (error) return <div className="text-sm text-danger">{error}</div>;
  if (!run) return <Spinner />;

  const isTerminal = ["Passed", "Failed", "Cancelled"].includes(run.status);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-mono text-lg text-ink">Run {run.id.slice(0, 8)}</h1>
          <div className="mt-1 font-mono text-xs text-ink-dim">trigger: {run.trigger}</div>
        </div>
        <div className="flex items-center gap-2">
          {/* D4-T4: connection health, never conflated with the run's own
              status above — a reconnecting live feed must never read as a
              failed run. aria-live announces the change without the page
              needing focus. */}
          {connectionState === "reconnecting" && (
            <span role="status" aria-live="polite" className="flex items-center gap-1.5 text-xs font-medium text-warning">
              <Spinner className="h-3 w-3" /> Reconnecting…
            </span>
          )}
          {connectionState === "polling" && (
            <span role="status" aria-live="polite" className="text-xs font-medium text-ink-dim">
              Live updates unavailable — refreshing periodically
            </span>
          )}
          <StatusPill status={run.status} />
          {!isTerminal && (
            <Button variant="danger" onClick={() => cancelMutation.mutate()} disabled={cancelMutation.isPending}>
              Cancel
            </Button>
          )}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <Card className="p-4">
          <h2 className="mb-3 font-mono text-sm text-ink-dim">Steps</h2>
          <ol className="flex flex-col gap-2">
            {run.results.map((r) => (
              <li key={r.id} className="flex items-center justify-between rounded-md border border-border px-3 py-2">
                <div>
                  <div className="text-sm text-ink">{r.stepName}</div>
                  <div className="font-mono text-xs text-ink-dim">
                    {r.module}.{r.action} · {r.durationMs}ms
                    {r.isFlaky && <span className="ml-1 text-warning">flaky</span>}
                  </div>
                </div>
                <StatusPill status={r.status} />
              </li>
            ))}
            {run.results.length === 0 && <div className="text-sm text-ink-dim">Waiting for the first step…</div>}
          </ol>
        </Card>

        <Card className="flex flex-col overflow-hidden p-4">
          <h2 className="mb-3 font-mono text-sm text-ink-dim">Execution console</h2>
          <div className="flex-1 overflow-y-auto rounded-md bg-bg p-3 font-mono text-xs">
            {run.results.map((r) => {
              const screenshot = screenshotFileName(r.data);
              return (
                <div key={r.id} className="mb-1">
                  <span className="text-ink-dim">[{new Date(run.startedAt).toLocaleTimeString()}]</span>{" "}
                  <span
                    className={
                      r.status === "Failed" ? "text-danger" : r.status === "Passed" ? "text-success" : "text-ink-dim"
                    }
                  >
                    {r.status.toUpperCase()}
                  </span>{" "}
                  <span className="text-ink">{r.stepName}</span>
                  {r.message && <span className="text-ink-dim"> — {r.message}</span>}
                  {r.error && <div className="pl-4 text-danger">{r.error}</div>}
                  {screenshot && <ScreenshotThumbnail runId={runId} fileName={screenshot} />}
                </div>
              );
            })}
            {!isTerminal && <div className="text-ink-dim">…</div>}
          </div>
        </Card>
      </div>
    </div>
  );
}
