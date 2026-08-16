import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "@tanstack/react-router";
import { flowsApi, getActionParams, modulesApi, ApiError } from "@/services/api-client";
import { Button, Card, Dialog, Input, Spinner, StatusPill } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { useToastStore } from "@/store/toast-store";
import { WorkflowCanvas } from "@/features/flow-authoring/workflow-canvas";
import { ParamFields, buildParamsFromFields, type FieldValue } from "@/features/shared/param-fields";

// FR-FEAT-04 (Workflow Builder): a real React Flow canvas (WorkflowCanvas)
// visualizes the step sequence and drives "add step" via its trailing "+"
// node. Drag-to-reorder and delete are still out of scope — FlowStepResponse
// only carries a flat `order`, and flowsApi has no reorder/delete/update
// endpoint yet — documented, not silent.
export function FlowDetailPage() {
  const { flowId } = useParams({ strict: false }) as { flowId: string };
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const pushToast = useToastStore((s) => s.push);

  const flowQuery = useQuery({ queryKey: ["flows", flowId], queryFn: () => flowsApi.get(flowId) });
  const modulesQuery = useQuery({ queryKey: ["modules"], queryFn: modulesApi.list });

  const [addStepOpen, setAddStepOpen] = useState(false);
  const [selectedStepId, setSelectedStepId] = useState<string | null>(null);
  const [moduleKind, setModuleKind] = useState("");
  const [action, setAction] = useState("");
  const [stepName, setStepName] = useState("");
  const [fieldValues, setFieldValues] = useState<Record<string, FieldValue>>({});
  const [saveAs, setSaveAs] = useState("");

  const addStepMutation = useMutation({
    mutationFn: () => {
      const params = buildParamsFromFields(selectedActionSchema, fieldValues);
      return flowsApi.addStep(flowId, { name: stepName, module: moduleKind, action, params, saveAs: saveAs || undefined });
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["flows", flowId] });
      pushToast({ kind: "success", message: "Step added." });
      setStepName("");
      setFieldValues({});
      setSaveAs("");
      setAddStepOpen(false);
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
        <div className="mb-3 flex items-center justify-between">
          <h2 className="font-mono text-sm text-ink-dim">Steps ({flow.steps.length})</h2>
          {flow.status === "Draft" && (
            <Button variant="secondary" onClick={() => setAddStepOpen(true)}>
              Add step
            </Button>
          )}
        </div>
        <WorkflowCanvas
          steps={flow.steps}
          canAddStep={flow.status === "Draft"}
          onAddStep={() => setAddStepOpen(true)}
          selectedStepId={selectedStepId}
          onSelectStep={setSelectedStepId}
        />
      </Card>

      {selectedStepId &&
        (() => {
          const step = flow.steps.find((s) => s.id === selectedStepId);
          if (!step) return null;
          return (
            <Card className="p-4">
              <h2 className="mb-3 font-mono text-sm text-ink-dim">
                {step.name} — {step.module}.{step.action}
              </h2>
              <dl className="grid grid-cols-[120px_1fr] gap-y-2 text-sm">
                <dt className="text-ink-dim">Params</dt>
                <dd>
                  <pre className="whitespace-pre-wrap rounded-md bg-surface-2 p-2 font-mono text-xs text-ink">
                    {JSON.stringify(step.params, null, 2)}
                  </pre>
                </dd>
                <dt className="text-ink-dim">Save as</dt>
                <dd className="font-mono text-xs text-ink">{step.saveAs ?? "—"}</dd>
                <dt className="text-ink-dim">When</dt>
                <dd className="font-mono text-xs text-ink">{step.when ?? "—"}</dd>
              </dl>
            </Card>
          );
        })()}

      <Dialog open={addStepOpen} onClose={() => setAddStepOpen(false)} title="Add step">
        <form
          className="grid grid-cols-2 gap-3"
          onSubmit={(e) => {
            e.preventDefault();
            addStepMutation.mutate();
          }}
        >
          <Input placeholder="Step name" value={stepName} onChange={(e) => setStepName(e.target.value)} required />
          <Input placeholder="Save as (optional)" value={saveAs} onChange={(e) => setSaveAs(e.target.value)} />
          <Select
            value={moduleKind}
            placeholder="Module…"
            options={(modulesQuery.data ?? []).map((m) => ({ value: m.kind, label: m.displayName }))}
            onChange={(kind) => {
              setModuleKind(kind);
              setAction("");
              setFieldValues({});
            }}
          />
          <Select
            value={action}
            placeholder="Action…"
            disabled={!moduleKind}
            options={(modulesQuery.data?.find((m) => m.kind === moduleKind)?.actions ?? []).map((a) => ({
              value: a.actionName,
              label: a.actionName,
            }))}
            onChange={(a) => {
              setAction(a);
              setFieldValues({});
            }}
          />
          <div className="col-span-2">
            {selectedActionSchema && (
              <ParamFields schema={selectedActionSchema} values={fieldValues} onChange={setFieldValues} />
            )}
          </div>
          <div className="col-span-2 flex justify-end">
            <Button type="submit" disabled={addStepMutation.isPending}>
              Add step
            </Button>
          </div>
        </form>
      </Dialog>
    </div>
  );
}
