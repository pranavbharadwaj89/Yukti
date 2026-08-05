import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { flowsApi, getActionParams, modulesApi, runsApi, ApiError } from "@/services/api-client";
import type { FlowRunResponse } from "@/services/types";
import { Button, Card, Input, Spinner, StatusPill, Textarea } from "@/components/ui/primitives";
import { Checkbox, Select } from "@/components/ui/form-controls";
import { useToastStore } from "@/store/toast-store";

// Each Tests tab (Web/Mobile/API) is a standalone single-action test runner
// against its module's real plugin — no dependency on the Flows builder UI.
// Under the hood it still uses the same flow endpoints (Yukti has no
// separate "run one action" API), but that's invisible here: create,
// add the one step, publish, run, and poll the result inline. The
// throwaway flow this creates is named so it's identifiable in the Flows
// list, not hidden.

type FieldValue = string | boolean;

function buildParams(schema: ReturnType<typeof getActionParams>, values: Record<string, FieldValue>) {
  if (!schema) return {};
  const params: Record<string, unknown> = {};
  for (const p of schema.parameters) {
    const raw = values[p.name];
    if (raw === undefined || raw === "") continue;
    switch (p.type) {
      case "Number": {
        const n = Number(raw);
        if (!Number.isNaN(n)) params[p.name] = n;
        break;
      }
      case "Boolean":
        params[p.name] = raw === true || raw === "true";
        break;
      case "Object":
      case "Array":
        try {
          params[p.name] = JSON.parse(String(raw));
        } catch {
          throw new Error(`"${p.name}" must be valid JSON.`);
        }
        break;
      default:
        params[p.name] = String(raw);
    }
  }
  return params;
}

function isTerminal(status: string) {
  return status === "Passed" || status === "Failed" || status === "Cancelled";
}

export function ModuleTestForm({ moduleKind, title }: { moduleKind: string; title: string }) {
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);
  const modulesQuery = useQuery({ queryKey: ["modules"], queryFn: modulesApi.list });

  const [action, setAction] = useState("");
  const [fieldValues, setFieldValues] = useState<Record<string, FieldValue>>({});
  const [runId, setRunId] = useState<string | null>(null);

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
      const params = buildParams(selectedActionSchema, fieldValues);
      const { flowId } = await flowsApi.create(`${title} — ${new Date().toLocaleString()}`, `Ad-hoc ${moduleKind}.${action} test run from the Tests tab.`);
      await flowsApi.addStep(flowId, { name: action, module: moduleKind, action, params });
      const publishResult = await flowsApi.publish(flowId);
      if (!publishResult.succeeded) throw new Error(publishResult.errors.join("; "));
      return flowsApi.triggerRun(flowId);
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
          <div className="flex flex-col gap-3">
            {selectedActionSchema.parameters.map((p) => (
              <div key={p.name} className="flex flex-col gap-1">
                <label className="text-body-sm text-ink-dim">
                  {p.name}
                  {p.required ? "*" : ""}
                  {p.description && <span className="ml-2 text-caption text-ink-dim/70">{p.description}</span>}
                </label>
                {p.type === "Boolean" ? (
                  <Checkbox
                    checked={fieldValues[p.name] === true}
                    onChange={(checked) => setFieldValues((v) => ({ ...v, [p.name]: checked }))}
                  />
                ) : p.type === "Object" || p.type === "Array" ? (
                  <Textarea
                    rows={3}
                    placeholder={p.type === "Array" ? "[...]" : "{...}"}
                    value={(fieldValues[p.name] as string) ?? ""}
                    onChange={(e) => setFieldValues((v) => ({ ...v, [p.name]: e.target.value }))}
                  />
                ) : (
                  <Input
                    type={p.type === "Number" ? "number" : "text"}
                    value={(fieldValues[p.name] as string) ?? ""}
                    onChange={(e) => setFieldValues((v) => ({ ...v, [p.name]: e.target.value }))}
                  />
                )}
              </div>
            ))}
          </div>
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
              </li>
            ))}
            {run.results.length === 0 && <div className="text-sm text-ink-dim">Waiting for result…</div>}
          </ol>
        </Card>
      )}
    </div>
  );
}
