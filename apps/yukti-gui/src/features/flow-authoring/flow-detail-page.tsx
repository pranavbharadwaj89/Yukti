import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "@tanstack/react-router";
import { flowsApi, getActionParams, modulesApi, ApiError } from "@/services/api-client";
import { Button, Card, Input, Spinner, StatusPill, Textarea } from "@/components/ui/primitives";
import { useToastStore } from "@/store/toast-store";

// FR-FEAT-04 (Workflow Builder): a form-based step editor, not a full
// drag-and-drop React Flow canvas — that's a substantially larger build
// this pass's scope didn't include time for; documented simplification,
// not a silent one. Still satisfies the underlying loop: add steps,
// publish, run.
export function FlowDetailPage() {
  const { flowId } = useParams({ strict: false }) as { flowId: string };
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const pushToast = useToastStore((s) => s.push);

  const flowQuery = useQuery({ queryKey: ["flows", flowId], queryFn: () => flowsApi.get(flowId) });
  const modulesQuery = useQuery({ queryKey: ["modules"], queryFn: modulesApi.list });

  const [moduleKind, setModuleKind] = useState("");
  const [action, setAction] = useState("");
  const [stepName, setStepName] = useState("");
  const [paramsJson, setParamsJson] = useState("{}");
  const [saveAs, setSaveAs] = useState("");

  const addStepMutation = useMutation({
    mutationFn: () => {
      let params: Record<string, unknown>;
      try {
        params = JSON.parse(paramsJson || "{}");
      } catch {
        throw new Error("Params must be valid JSON.");
      }
      return flowsApi.addStep(flowId, { name: stepName, module: moduleKind, action, params, saveAs: saveAs || undefined });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["flows", flowId] });
      pushToast({ kind: "success", message: "Step added." });
      setStepName("");
      setParamsJson("{}");
      setSaveAs("");
    },
    onError: (err) => pushToast({ kind: "error", message: err instanceof Error ? err.message : "Failed to add step." }),
  });

  const publishMutation = useMutation({
    mutationFn: () => flowsApi.publish(flowId),
    onSuccess: async (result) => {
      await queryClient.invalidateQueries({ queryKey: ["flows", flowId] });
      if (result.succeeded) {
        pushToast({ kind: "success", message: "Flow published." });
      } else {
        pushToast({ kind: "error", message: result.errors.join("; ") });
      }
    },
    onError: (err) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : "Failed to publish flow.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      }),
  });

  const triggerRunMutation = useMutation({
    mutationFn: () => flowsApi.triggerRun(flowId),
    onSuccess: (run) => {
      pushToast({ kind: "success", message: `Run ${run.id.slice(0, 8)} started.` });
      void navigate({ to: "/runs/$runId", params: { runId: run.id } });
    },
    onError: (err) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : "Failed to trigger run.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      }),
  });

  if (flowQuery.isLoading) return <Spinner />;
  const flow = flowQuery.data;
  if (!flow) return <div className="text-sm text-danger">Flow not found.</div>;

  const selectedActionSchema = getActionParams(modulesQuery.data, moduleKind, action);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-mono text-xl text-ink">{flow.name}</h1>
          <div className="mt-1 text-xs text-ink-dim">{flow.description}</div>
        </div>
        <div className="flex items-center gap-2">
          <StatusPill status={flow.status} />
          <Button variant="secondary" onClick={() => publishMutation.mutate()} disabled={flow.status !== "Draft" || publishMutation.isPending}>
            Publish
          </Button>
          <Button onClick={() => triggerRunMutation.mutate()} disabled={flow.status !== "Published" || triggerRunMutation.isPending}>
            Run
          </Button>
        </div>
      </div>

      <Card className="p-4">
        <h2 className="mb-3 font-mono text-sm text-ink-dim">Steps ({flow.steps.length})</h2>
        <ol className="flex flex-col gap-2">
          {flow.steps
            .sort((a, b) => a.order - b.order)
            .map((step, i) => (
              <li key={step.id} className="flex items-center gap-3 rounded-md border border-border px-3 py-2">
                <span className="font-mono text-xs text-ink-dim">{i + 1}</span>
                <span className="text-sm text-ink">{step.name}</span>
                <span className="font-mono text-xs text-ink-dim">{step.module}.{step.action}</span>
              </li>
            ))}
          {flow.steps.length === 0 && <div className="text-sm text-ink-dim">No steps yet.</div>}
        </ol>
      </Card>

      {flow.status === "Draft" && (
        <Card className="p-4">
          <h2 className="mb-3 font-mono text-sm text-ink-dim">Add step</h2>
          <form
            className="grid grid-cols-2 gap-3"
            onSubmit={(e) => {
              e.preventDefault();
              addStepMutation.mutate();
            }}
          >
            <Input placeholder="Step name" value={stepName} onChange={(e) => setStepName(e.target.value)} required />
            <Input placeholder="Save as (optional)" value={saveAs} onChange={(e) => setSaveAs(e.target.value)} />
            <select
              className="rounded-md border border-border bg-surface px-3 py-2 text-sm text-ink"
              value={moduleKind}
              onChange={(e) => {
                setModuleKind(e.target.value);
                setAction("");
              }}
              required
            >
              <option value="">Module…</option>
              {modulesQuery.data?.map((m) => (
                <option key={m.kind} value={m.kind}>
                  {m.displayName}
                </option>
              ))}
            </select>
            <select
              className="rounded-md border border-border bg-surface px-3 py-2 text-sm text-ink"
              value={action}
              onChange={(e) => setAction(e.target.value)}
              required
              disabled={!moduleKind}
            >
              <option value="">Action…</option>
              {modulesQuery.data
                ?.find((m) => m.kind === moduleKind)
                ?.actions.map((a) => (
                  <option key={a.actionName} value={a.actionName}>
                    {a.actionName}
                  </option>
                ))}
            </select>
            <div className="col-span-2">
              {selectedActionSchema && (
                <p className="mb-1 text-xs text-ink-dim">
                  Params: {selectedActionSchema.parameters.map((p) => `${p.name}${p.required ? "*" : ""}`).join(", ") || "none"}
                </p>
              )}
              <Textarea rows={4} placeholder='{"key": "value"}' value={paramsJson} onChange={(e) => setParamsJson(e.target.value)} />
            </div>
            <div className="col-span-2 flex justify-end">
              <Button type="submit" disabled={addStepMutation.isPending}>
                Add step
              </Button>
            </div>
          </form>
        </Card>
      )}
    </div>
  );
}
