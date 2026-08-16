import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Copy } from "lucide-react";
import { triggersApi, flowsApi, ApiError } from "@/services/api-client";
import { Button, Dialog, Input, Spinner } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { DataTable, type Column } from "@/components/ui/data-table";
import { useToastStore } from "@/store/toast-store";
import type { TriggerResponse } from "@/services/types";

const KIND_OPTIONS = [
  { value: "Cron", label: "Cron" },
  { value: "Webhook", label: "Webhook" },
  { value: "FileWatch", label: "File watch" },
];

async function copyToClipboard(value: string, pushToast: ReturnType<typeof useToastStore.getState>["push"]) {
  try {
    await navigator.clipboard.writeText(value);
    pushToast({ kind: "success", message: "Copied to clipboard." });
  } catch {
    pushToast({ kind: "error", message: "Couldn't copy — copy it manually instead." });
  }
}

function CopyButton({ value }: { value: string }) {
  const pushToast = useToastStore((s) => s.push);
  return (
    <button
      type="button"
      onClick={() => copyToClipboard(value, pushToast)}
      aria-label="Copy webhook path"
      className="text-ink-dim hover:text-accent"
    >
      <Copy size={14} />
    </button>
  );
}

// Scheduler surface for TriggerDefinition (Yukti.Domain/Scheduling) — no
// Delete here since ITriggerRepository doesn't expose one; Enable/Disable
// is the real, supported lifecycle the domain gives us.
export function SchedulerPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const [createdWebhook, setCreatedWebhook] = useState<TriggerResponse | null>(null);
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);
  const triggersQuery = useQuery({ queryKey: ["triggers"], queryFn: triggersApi.list });
  const flowsQuery = useQuery({ queryKey: ["flows"], queryFn: flowsApi.list });

  function flowName(flowId: string): string {
    return flowsQuery.data?.find((f) => f.flowId.value === flowId)?.name ?? flowId.slice(0, 8);
  }

  const toggleMutation = useMutation({
    mutationFn: (trigger: TriggerResponse) =>
      trigger.isEnabled ? triggersApi.disable(trigger.id) : triggersApi.enable(trigger.id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["triggers"] }),
    onError: (err) =>
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to update trigger." }),
  });

  const columns: Column<TriggerResponse>[] = [
    { key: "flow", header: "Flow", render: (t) => <span className="text-ink">{flowName(t.flowId)}</span> },
    { key: "kind", header: "Kind", render: (t) => <span className="font-mono text-body-sm text-ink-dim">{t.kind}</span> },
    {
      key: "detail",
      header: "Detail",
      render: (t) => (
        <span className="inline-flex items-center gap-1.5 font-mono text-body-sm text-ink-dim">
          {t.cronExpression ?? t.webhookPath ?? t.watchPath ?? "—"}
          {t.kind === "Webhook" && t.webhookPath && <CopyButton value={t.webhookPath} />}
        </span>
      ),
    },
    { key: "lastFired", header: "Last fired", render: (t) => <span className="text-ink-dim">{t.lastFiredAt ?? "never"}</span> },
    {
      key: "enabled",
      header: "Enabled",
      render: (t) => (
        <Button variant={t.isEnabled ? "secondary" : "primary"} onClick={() => toggleMutation.mutate(t)}>
          {t.isEnabled ? "Disable" : "Enable"}
        </Button>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 text-ink">Scheduler</h1>
        <Button onClick={() => setCreateOpen(true)}>New trigger</Button>
      </div>

      <DataTable
        columns={columns}
        rows={triggersQuery.data ?? []}
        rowKey={(t) => t.id}
        loading={triggersQuery.isLoading}
        emptyTitle="No triggers yet"
        emptyDescription="Create one to schedule a flow run."
        pageSize={10}
      />

      <CreateTriggerDialog
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onWebhookCreated={(trigger) => setCreatedWebhook(trigger)}
      />
      <WebhookCreatedDialog trigger={createdWebhook} onClose={() => setCreatedWebhook(null)} />
    </div>
  );
}

function WebhookCreatedDialog({ trigger, onClose }: { trigger: TriggerResponse | null; onClose: () => void }) {
  const pushToast = useToastStore((s) => s.push);
  if (!trigger || !trigger.webhookPath) return null;
  return (
    <Dialog open onClose={onClose} title="Webhook trigger created">
      <p className="mb-3 text-body-sm text-ink-dim">
        Send a POST request to this path to fire the flow. It won't be shown again on this page's create form —
        copy it now.
      </p>
      <div className="flex items-center gap-2 rounded-md border border-border bg-surface-2 px-3 py-2">
        <code className="flex-1 overflow-x-auto whitespace-nowrap font-mono text-body-sm text-ink">{trigger.webhookPath}</code>
        <Button variant="secondary" onClick={() => copyToClipboard(trigger.webhookPath!, pushToast)}>
          <Copy size={14} /> Copy
        </Button>
      </div>
      <div className="mt-5 flex justify-end">
        <Button onClick={onClose}>Done</Button>
      </div>
    </Dialog>
  );
}

function CreateTriggerDialog({
  open,
  onClose,
  onWebhookCreated,
}: {
  open: boolean;
  onClose: () => void;
  onWebhookCreated: (trigger: TriggerResponse) => void;
}) {
  const [flowId, setFlowId] = useState("");
  const [kind, setKind] = useState<"Cron" | "Webhook" | "FileWatch">("Cron");
  const [cronExpression, setCronExpression] = useState("");
  const [webhookSecret, setWebhookSecret] = useState("");
  const [watchPath, setWatchPath] = useState("");
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);
  const flowsQuery = useQuery({ queryKey: ["flows"], queryFn: flowsApi.list, enabled: open });

  const createMutation = useMutation({
    mutationFn: () =>
      triggersApi.create(flowId, {
        kind,
        cronExpression: kind === "Cron" ? cronExpression : undefined,
        webhookSecret: kind === "Webhook" ? webhookSecret || undefined : undefined,
        watchPath: kind === "FileWatch" ? watchPath : undefined,
      }),
    onSuccess: async ({ triggerId }) => {
      await queryClient.invalidateQueries({ queryKey: ["triggers"] });
      pushToast({ kind: "success", message: "Trigger created." });
      if (kind === "Webhook") {
        const triggers = await triggersApi.list();
        const created = triggers.find((t) => t.id === triggerId);
        if (created) onWebhookCreated(created);
      }
      setFlowId("");
      setCronExpression("");
      setWebhookSecret("");
      setWatchPath("");
      onClose();
    },
    onError: (err) =>
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to create trigger." }),
  });

  return (
    <Dialog open={open} onClose={onClose} title="New trigger">
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          createMutation.mutate();
        }}
      >
        <Select
          value={flowId}
          placeholder="Flow…"
          options={(flowsQuery.data ?? []).map((f) => ({ value: f.flowId.value, label: f.name }))}
          onChange={setFlowId}
        />
        <Select value={kind} options={KIND_OPTIONS} onChange={(v) => setKind(v as typeof kind)} />
        {kind === "Cron" && (
          <Input placeholder="Cron expression (e.g. 0 * * * *)" value={cronExpression} onChange={(e) => setCronExpression(e.target.value)} required />
        )}
        {kind === "Webhook" && (
          <Input placeholder="Signing secret (optional)" value={webhookSecret} onChange={(e) => setWebhookSecret(e.target.value)} />
        )}
        {kind === "FileWatch" && (
          <Input placeholder="Watch path" value={watchPath} onChange={(e) => setWatchPath(e.target.value)} required />
        )}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={!flowId || createMutation.isPending}>
            {createMutation.isPending ? <Spinner /> : "Create"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
