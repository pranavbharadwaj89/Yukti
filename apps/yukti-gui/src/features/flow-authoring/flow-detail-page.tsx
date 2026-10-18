import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams } from "@tanstack/react-router";
import { flowsApi, flowReportsApi, getActionParams, modulesApi, runsApi, ApiError } from "@/services/api-client";
import { Button, Card, Dialog, Input, Spinner, StatusPill } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { useToastStore } from "@/store/toast-store";
import { WorkflowCanvas } from "@/features/flow-authoring/workflow-canvas";
import { ParamFields, buildParamsFromFields, type FieldValue } from "@/features/shared/param-fields";
import { AiAssistedBadge } from "@/features/ai-generation/ai-assisted-badge";

// D4-T6: real output fields from the flow's last run, e.g. {order: ["id",
// "status"]} — an opportunistic enhancement over plain saveAs-name
// suggestions. Flattens one level of nesting past each top-level key;
// arrays and anything past that depth are offered as the array/object
// field itself, not guessed into further paths no step may ever produce
// the same shape twice for.
function flattenResultFields(data: unknown, prefix = "", depth = 0): string[] {
  if (depth > 2 || data === null || typeof data !== "object" || Array.isArray(data)) {
    return prefix ? [prefix] : [];
  }
  const paths: string[] = [];
  for (const [key, value] of Object.entries(data as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (value !== null && typeof value === "object" && !Array.isArray(value)) {
      paths.push(...flattenResultFields(value, path, depth + 1));
    } else {
      paths.push(path);
    }
  }
  return paths;
}

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

  // D4-T6: earlier steps' saveAs names (what {{vars. can ever legally
  // reference, matching Flow.Publish's own rule exactly) plus, when a
  // previous run exists, that run's real field shapes per binding.
  const reportsQuery = useQuery({
    queryKey: ["flow-reports", flowId, "runs"],
    queryFn: () => flowReportsApi.runsByFlow(flowId),
    enabled: !!flowId,
  });
  const latestRunId = [...(reportsQuery.data ?? [])].sort(
    (a, b) => new Date(b.occurredAt).getTime() - new Date(a.occurredAt).getTime(),
  )[0]?.flowRunId;
  const latestRunQuery = useQuery({
    queryKey: ["runs", latestRunId, "autocomplete-fields"],
    queryFn: () => runsApi.get(latestRunId!),
    enabled: !!latestRunId,
  });

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
  const availableBindings = flow.steps.map((s) => s.saveAs).filter((s): s is string => !!s);
  const fieldsByBinding: Record<string, string[]> = {};
  if (latestRunQuery.data) {
    const stepNameToSaveAs = new Map(flow.steps.filter((s) => s.saveAs).map((s) => [s.name, s.saveAs as string]));
    for (const result of latestRunQuery.data.results) {
      const bound = stepNameToSaveAs.get(result.stepName);
      if (bound && result.data !== undefined) fieldsByBinding[bound] = flattenResultFields(result.data);
    }
  }

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

      {(() => {
        // D5-T7 (CLAUDE.md principle 3): AI proposes, human disposes — say so up front on any
        // flow containing AI-generated steps, not only once a step happens to be selected.
        const aiStep = flow.steps.find((s) => s.aiAttribution);
        if (!aiStep?.aiAttribution) return null;
        return (
          <Card className="flex flex-wrap items-center gap-2 p-3">
            <AiAssistedBadge attribution={aiStep.aiAttribution} />
            <p className="text-sm text-ink-dim">
              {flow.status === "Draft"
                ? "This draft was generated by AI. Review every step before publishing."
                : "This flow's steps were generated by AI and reviewed before publishing."}
            </p>
          </Card>
        );
      })()}

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
              <h2 className="mb-3 flex flex-wrap items-center gap-2 font-mono text-sm text-ink-dim">
                {step.name} — {step.module}.{step.action}
                {step.aiAttribution && <AiAssistedBadge attribution={step.aiAttribution} />}
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
              <ParamFields
                schema={selectedActionSchema}
                values={fieldValues}
                onChange={setFieldValues}
                stepName={stepName || "this step"}
                availableBindings={availableBindings}
                fieldsByBinding={fieldsByBinding}
              />
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
