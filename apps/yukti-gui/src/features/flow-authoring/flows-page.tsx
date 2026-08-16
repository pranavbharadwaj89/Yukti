import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { flowsApi, ApiError } from "@/services/api-client";
import { flowStatusLabel, type FlowSummary } from "@/services/types";
import { Button, Dialog, Input, StatusPill } from "@/components/ui/primitives";
import { DataTable, type Column } from "@/components/ui/data-table";
import { useToastStore } from "@/store/toast-store";
import { useProjectStore } from "@/store/project-store";

// FR-FEAT-02 (Project Explorer) is [NEW-DOMAIN], blocked on Q-01 — this is
// a flat flow list instead, the closest available today. Rendered through
// DataTable (UI_Component_Spec.md Part 3 §1) rather than a plain <ul>: gets
// sortable columns and pagination for free once flow counts grow.
export function FlowsPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const navigate = useNavigate();
  const selectedProjectId = useProjectStore((s) => s.selectedProjectId);
  const flowsQuery = useQuery({ queryKey: ["flows"], queryFn: flowsApi.list });
  const visibleFlows = selectedProjectId
    ? (flowsQuery.data ?? []).filter((f) => f.projectId?.value === selectedProjectId)
    : (flowsQuery.data ?? []);

  const columns: Column<FlowSummary>[] = [
    {
      key: "name",
      header: "Name",
      sortable: true,
      sortValue: (f) => f.name.toLowerCase(),
      render: (f) => (
        <button
          type="button"
          className="text-ink hover:text-accent hover:underline"
          onClick={() => void navigate({ to: "/flows/$flowId", params: { flowId: f.flowId.value } })}
        >
          {f.name}
        </button>
      ),
    },
    {
      key: "status",
      header: "Status",
      sortable: true,
      sortValue: (f) => f.status,
      render: (f) => <StatusPill status={flowStatusLabel(f.status)} />,
    },
    {
      key: "version",
      header: "Version",
      align: "right",
      sortable: true,
      sortValue: (f) => f.version,
      render: (f) => <span className="font-mono text-body-sm text-ink-dim">v{f.version}</span>,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 text-ink">Flows</h1>
        <Button onClick={() => setCreateOpen(true)}>New flow</Button>
      </div>

      <DataTable
        columns={columns}
        rows={visibleFlows}
        rowKey={(f) => f.flowId.value}
        loading={flowsQuery.isLoading}
        emptyTitle="No flows yet"
        emptyDescription="Create one to get started."
        pageSize={10}
      />

      <CreateFlowDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </div>
  );
}

function CreateFlowDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const pushToast = useToastStore((s) => s.push);
  const selectedProjectId = useProjectStore((s) => s.selectedProjectId);

  const createMutation = useMutation({
    mutationFn: () => flowsApi.create(name, description || undefined, selectedProjectId ?? undefined),
    onSuccess: async (result) => {
      await queryClient.invalidateQueries({ queryKey: ["flows"] });
      pushToast({ kind: "success", message: `Flow "${name}" created.` });
      setName("");
      setDescription("");
      onClose();
      void navigate({ to: "/flows/$flowId", params: { flowId: result.flowId } });
    },
    onError: (err) => {
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : "Failed to create flow.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      });
    },
  });

  return (
    <Dialog open={open} onClose={onClose} title="New flow">
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          createMutation.mutate();
        }}
      >
        <Input placeholder="Flow name" value={name} onChange={(e) => setName(e.target.value)} required autoFocus />
        <Input placeholder="Description (optional)" value={description} onChange={(e) => setDescription(e.target.value)} />
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={createMutation.isPending}>
            Create
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
