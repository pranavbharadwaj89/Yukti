import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "@tanstack/react-router";
import { flowsApi, ApiError } from "@/services/api-client";
import { flowStatusLabel } from "@/services/types";
import { Button, Card, Dialog, Input, StatusPill } from "@/components/ui/primitives";
import { useToastStore } from "@/store/toast-store";

// FR-FEAT-02 (Project Explorer) is [NEW-DOMAIN], blocked on Q-01 — this is
// a flat flow list instead, the closest available today.
export function FlowsPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const flowsQuery = useQuery({ queryKey: ["flows"], queryFn: flowsApi.list });

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="font-mono text-xl text-ink">Flows</h1>
        <Button onClick={() => setCreateOpen(true)}>New flow</Button>
      </div>

      <Card>
        {(flowsQuery.data ?? []).length === 0 && (
          <div className="p-6 text-sm text-ink-dim">No flows yet — create one to get started.</div>
        )}
        <ul>
          {(flowsQuery.data ?? []).map((f) => (
            <FlowRow key={f.flowId.value} flowId={f.flowId.value} name={f.name} status={flowStatusLabel(f.status)} />
          ))}
        </ul>
      </Card>

      <CreateFlowDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </div>
  );
}

function FlowRow({ flowId, name, status }: { flowId: string; name: string; status: string }) {
  const navigate = useNavigate();
  return (
    <li
      className="flex cursor-pointer items-center justify-between border-b border-border px-4 py-3 last:border-b-0 hover:bg-surface-2"
      onClick={() => void navigate({ to: "/flows/$flowId", params: { flowId } })}
    >
      <span className="text-sm text-ink">{name}</span>
      <StatusPill status={status} />
    </li>
  );
}

function CreateFlowDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const pushToast = useToastStore((s) => s.push);

  const createMutation = useMutation({
    mutationFn: () => flowsApi.create(name, description || undefined),
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
