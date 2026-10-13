import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { flowsApi, getActionParams, modulesApi, runsApi, ApiError } from "@/services/api-client";
import type { FlowRunResponse } from "@/services/types";
import { Button, Card, Spinner, StatusPill } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { useToastStore } from "@/store/toast-store";
import { useSelectedEnvironmentVariables } from "@/hooks";
import { ParamFields, buildParamsFromFields, type FieldValue } from "@/features/shared/param-fields";
import { MODULE_EXTRAS } from "@/features/shared/module-extras";

// Each Tests tab (Web/Mobile/API) is a standalone single-action test runner
// against its module's real plugin — no dependency on the Flows builder UI.
// Under the hood it still uses the same flow endpoints (Yukti has no
// separate "run one action" API), but that's invisible here: create,
// add the one step, publish, run, and poll the result inline. The
// throwaway flow this creates is named so it's identifiable in the Flows
// list, not hidden.

function isTerminal(status: string) {
  return status === "Passed" || status === "Failed" || status === "Cancelled";
}

function isPathData(data: unknown): data is { path: string } {
  return typeof data === "object" && data !== null && typeof (data as Record<string, unknown>).path === "string";
}

function isMatchData(data: unknown): data is { x: number; y: number; confidence?: number } {
  return (
    typeof data === "object" &&
    data !== null &&
    typeof (data as Record<string, unknown>).x === "number" &&
    typeof (data as Record<string, unknown>).y === "number"
  );
}

export function ModuleTestForm({ moduleKind, title }: { moduleKind: string; title: string }) {
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);
  const modulesQuery = useQuery({ queryKey: ["modules"], queryFn: modulesApi.list });

  const [action, setAction] = useState("");
  const [fieldValues, setFieldValues] = useState<Record<string, FieldValue>>({});
  const [runId, setRunId] = useState<string | null>(null);
  const extra = MODULE_EXTRAS[moduleKind];
  const [extraConfig, setExtraConfig] = useState<unknown>(extra?.emptyConfig);
  const environmentVariables = useSelectedEnvironmentVariables();

  const module = modulesQuery.data?.find((m) => m.kind === moduleKind);
  const selectedActionSchema = getActionParams(modulesQuery.data, moduleKind, action);

  const runQuery = useQuery({
    queryKey: ["runs", runId],
    queryFn: () => runsApi.get(runId!),
    enabled: !!runId,
    refetchInterval: (query) => (query.state.data && isTerminal(query.state.data.status) ? false : 1000),
  });

  const runMutation = useMutation({
    mutationFn: async () => {
      const params = buildParamsFromFields(selectedActionSchema, fieldValues);
      const { flowId } = await flowsApi.create(`${title} — ${new Date().toLocaleString()}`, `Ad-hoc ${moduleKind}.${action} test run from the Tests tab.`);
      await flowsApi.addStep(flowId, { name: action, module: moduleKind, action, params });
      const publishResult = await flowsApi.publish(flowId);
      if (!publishResult.succeeded) throw new Error(publishResult.errors.join("; "));
      // Environment variables are the base (e.g. a Project's saved Mobile
      // device config); the form's own pre-run panel — if filled — takes
      // priority, since a manual override at run time is more specific
      // than the saved default.
      const manualOverride = extra?.buildVariableOverrides(extraConfig);
      const variableOverrides =
        environmentVariables || manualOverride ? { ...environmentVariables, ...manualOverride } : undefined;
      return flowsApi.triggerRun(flowId, variableOverrides);
    },
    onSuccess: (run: FlowRunResponse) => {
      setRunId(run.id);
      void queryClient.invalidateQueries({ queryKey: ["flows"] });
    },
    onError: (err) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : err instanceof Error ? err.message : "Failed to run test.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      }),
  });

  const run = runQuery.data;

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-h1 text-ink">{title}</h1>

      {extra && <extra.PreRunPanel config={extraConfig} onChange={setExtraConfig} />}

      <Card className="flex flex-col gap-3 p-4">
        <Select
          value={action}
          placeholder="Action…"
          disabled={!module}
          options={(module?.actions ?? []).map((a) => ({ value: a.actionName, label: a.actionName }))}
          onChange={(a) => {
            setAction(a);
            setFieldValues({});
            setRunId(null);
          }}
        />

        {selectedActionSchema && (
          <ParamFields schema={selectedActionSchema} values={fieldValues} onChange={setFieldValues} />
        )}

        <div className="flex justify-end">
          <Button onClick={() => runMutation.mutate()} disabled={!action || runMutation.isPending}>
            {runMutation.isPending ? <Spinner /> : "Run test"}
          </Button>
        </div>
      </Card>

      {run && (
        <Card className="p-4">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="font-mono text-sm text-ink-dim">Run {run.id.slice(0, 8)}</h2>
            <StatusPill status={run.status} />
          </div>
          <ol className="flex flex-col gap-2">
            {run.results.map((r) => (
              <li key={r.id} className="rounded-md border border-border p-3">
                <div className="mb-1 flex items-center justify-between">
                  <span className="text-sm text-ink">
                    {r.stepName} — <span className="font-mono text-ink-dim">{r.module}.{r.action}</span>
                  </span>
                  <StatusPill status={r.status} />
                </div>
                {(r.message || r.error) && (
                  <p className={`text-body-sm ${r.error ? "text-danger" : "text-ink-dim"}`}>{r.error ?? r.message}</p>
                )}
                {isPathData(r.data) && <p className="font-mono text-body-sm text-ink-dim">Saved: {r.data.path}</p>}
                {isMatchData(r.data) && (
                  <p className="font-mono text-body-sm text-ink-dim">
                    Match at ({r.data.x}, {r.data.y}){r.data.confidence !== undefined ? ` — confidence ${r.data.confidence}` : ""}
                  </p>
                )}
              </li>
            ))}
            {run.results.length === 0 && <div className="text-sm text-ink-dim">Waiting for result…</div>}
          </ol>
        </Card>
      )}
    </div>
  );
}
