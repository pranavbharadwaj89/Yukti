import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { requestTemplatesApi, ApiError } from "@/services/api-client";
import type { RequestTemplateResponse, RequestTemplateSummaryResponse } from "@/services/types";
import { Button, Card, Dialog, Input, Spinner } from "@/components/ui/primitives";
import { ConfirmDialog } from "@/components/ui/feedback";
import { DataTable, type Column } from "@/components/ui/data-table";
import { useToastStore } from "@/store/toast-store";
import { KeyValueEditor, newKeyValueRow, type KeyValueRow } from "@/features/api-studio/key-value-editor";
import { AuthEditor, authDraftFromWire, authDraftToWire, type AuthDraft } from "./auth-editor";

// DISC-20: API-T9's own "the UI shows which flows use a template, so the blast radius is
// visible before editing" checklist item, finally given a real page. GET
// /api/v1/request-templates/{id}/usages already existed (API-T9) with nothing calling it —
// DataTable's expandedContent (projects-page.tsx doesn't need it; flows-page.tsx's own run
// history panel is the closest precedent) renders it per row, fetched lazily only once a
// row is actually expanded.

function headersToRows(headers: Record<string, unknown>): KeyValueRow[] {
  const entries = Object.entries(headers);
  const rows = entries.map(([key, value]) => ({
    ...newKeyValueRow(),
    key,
    value: typeof value === "string" ? value : JSON.stringify(value),
  }));
  return rows.length > 0 ? rows : [newKeyValueRow()];
}

function rowsToHeaders(rows: KeyValueRow[]): Record<string, unknown> {
  const headers: Record<string, unknown> = {};
  for (const row of rows.filter((r) => r.enabled && r.key.trim() !== "")) {
    headers[row.key] = row.value;
  }
  return headers;
}

export function RequestTemplatesPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<RequestTemplateResponse | null>(null);
  const [deleting, setDeleting] = useState<RequestTemplateSummaryResponse | null>(null);
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);

  const templatesQuery = useQuery({ queryKey: ["request-templates"], queryFn: requestTemplatesApi.list });

  const deleteMutation = useMutation({
    mutationFn: (templateId: string) => requestTemplatesApi.delete(templateId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["request-templates"] });
      pushToast({ kind: "success", message: "Request template deleted." });
      setDeleting(null);
    },
    onError: (err) => {
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to delete request template." });
      setDeleting(null);
    },
  });

  const columns: Column<RequestTemplateSummaryResponse>[] = [
    {
      key: "name",
      header: "Name",
      sortable: true,
      sortValue: (t) => t.name.toLowerCase(),
      render: (t) => <span className="font-medium text-ink">{t.name}</span>,
    },
    { key: "baseUrl", header: "Base URL", render: (t) => <span className="text-ink-dim">{t.baseUrl ?? "—"}</span> },
    { key: "version", header: "Version", align: "right", render: (t) => <span className="text-ink-dim">v{t.version}</span> },
    {
      key: "actions",
      header: "",
      align: "right",
      render: (t) => (
        <div className="flex justify-end gap-2">
          <Button
            variant="ghost"
            onClick={async () => {
              const full = await requestTemplatesApi.get(t.id);
              setEditing(full);
            }}
          >
            Edit
          </Button>
          <Button variant="ghost" onClick={() => setDeleting(t)}>
            Delete
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 text-ink">Request Templates</h1>
        <Button onClick={() => setCreateOpen(true)}>New template</Button>
      </div>

      <p className="text-body-sm text-ink-dim">
        A reusable base URL, headers, auth, and timeout that an API step references by{" "}
        <code className="rounded bg-surface-2 px-1 py-0.5 font-mono text-caption">templateId</code>. Editing a template changes every flow
        that references it, with no flow edits — expand a row to see which flows that would affect before you change it.
      </p>

      <DataTable
        columns={columns}
        rows={templatesQuery.data ?? []}
        rowKey={(t) => t.id}
        loading={templatesQuery.isLoading}
        emptyTitle="No request templates yet"
        emptyDescription="Create one to share a base URL, headers, and auth across flows."
        expandedContent={(t) => <UsagesPanel templateId={t.id} />}
        pageSize={10}
      />

      <RequestTemplateDialog open={createOpen} template={null} onClose={() => setCreateOpen(false)} />
      <RequestTemplateDialog open={editing !== null} template={editing} onClose={() => setEditing(null)} />

      <ConfirmDialog
        open={deleting !== null}
        onCancel={() => setDeleting(null)}
        onConfirm={() => deleting && deleteMutation.mutate(deleting.id)}
        title="Delete request template?"
        message={`"${deleting?.name}" will be removed. Any flow step still referencing it by templateId will fail to resolve on its next run.`}
        confirmLabel="Delete"
      />
    </div>
  );
}

/** DISC-20's own "blast radius visible before editing" — fetched only once this specific
 * row is expanded (DataTable only calls expandedContent for an already-expanded row). */
function UsagesPanel({ templateId }: { templateId: string }) {
  const usagesQuery = useQuery({
    queryKey: ["request-templates", templateId, "usages"],
    queryFn: () => requestTemplatesApi.usages(templateId),
  });

  if (usagesQuery.isLoading) return <Spinner className="h-4 w-4 text-accent" />;

  const usages = usagesQuery.data ?? [];
  if (usages.length === 0) {
    return <p className="text-body-sm text-ink-dim">Not used by any flow yet.</p>;
  }

  return (
    <div className="flex flex-col gap-2">
      <p className="text-body-sm font-medium text-ink">Used by {usages.length} step(s):</p>
      <ul className="flex flex-col gap-1">
        {usages.map((u, i) => (
          <li key={`${u.flowId}-${u.stepName}-${i}`} className="text-body-sm text-ink-dim">
            <span className="text-ink">{u.flowName}</span> — step "{u.stepName}"
          </li>
        ))}
      </ul>
    </div>
  );
}

function RequestTemplateDialog({
  open,
  template,
  onClose,
}: {
  open: boolean;
  template: RequestTemplateResponse | null;
  onClose: () => void;
}) {
  const isEdit = template !== null;
  const [name, setName] = useState(template?.name ?? "");
  const [baseUrl, setBaseUrl] = useState(template?.baseUrl ?? "");
  const [defaultTimeoutMs, setDefaultTimeoutMs] = useState(template?.defaultTimeoutMs?.toString() ?? "");
  const [headerRows, setHeaderRows] = useState<KeyValueRow[]>(() => headersToRows(template?.headers ?? {}));
  const [auth, setAuth] = useState<AuthDraft>(() => authDraftFromWire(template?.auth));
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);

  // Re-seeds the form every time a different template is opened for edit (or the create
  // dialog is reopened), same reset-on-identity-change the dialog needs since React won't
  // otherwise re-run useState initializers for an already-mounted component.
  const [seededFor, setSeededFor] = useState(template?.id ?? "create");
  if ((template?.id ?? "create") !== seededFor) {
    setSeededFor(template?.id ?? "create");
    setName(template?.name ?? "");
    setBaseUrl(template?.baseUrl ?? "");
    setDefaultTimeoutMs(template?.defaultTimeoutMs?.toString() ?? "");
    setHeaderRows(headersToRows(template?.headers ?? {}));
    setAuth(authDraftFromWire(template?.auth));
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      const req = {
        name,
        baseUrl: baseUrl.trim() || null,
        headers: rowsToHeaders(headerRows),
        auth: authDraftToWire(auth),
        defaultTimeoutMs: defaultTimeoutMs.trim() ? Number(defaultTimeoutMs) : null,
      };
      if (isEdit && template) {
        await requestTemplatesApi.update(template.id, req);
      } else {
        await requestTemplatesApi.create(req);
      }
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["request-templates"] });
      pushToast({ kind: "success", message: isEdit ? "Request template updated." : "Request template created." });
      onClose();
    },
    onError: (err) =>
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to save request template." }),
  });

  return (
    <Dialog open={open} onClose={onClose} title={isEdit ? `Edit "${template?.name}"` : "New request template"}>
      <form
        className="flex max-h-[70vh] flex-col gap-4 overflow-y-auto"
        onSubmit={(e) => {
          e.preventDefault();
          saveMutation.mutate();
        }}
      >
        <Input placeholder="Template name" value={name} onChange={(e) => setName(e.target.value)} required autoFocus />
        <Input placeholder="Base URL (optional)" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} />
        <Input
          placeholder="Default timeout (ms, optional)"
          type="number"
          value={defaultTimeoutMs}
          onChange={(e) => setDefaultTimeoutMs(e.target.value)}
        />

        <Card className="flex flex-col gap-2 p-3">
          <label className="text-body-sm text-ink-dim">Headers</label>
          <KeyValueEditor rows={headerRows} onChange={setHeaderRows} keyPlaceholder="Header" valuePlaceholder="Value" />
        </Card>

        <Card className="flex flex-col gap-2 p-3">
          <label className="text-body-sm text-ink-dim">Auth</label>
          <AuthEditor draft={auth} onChange={setAuth} />
        </Card>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={!name.trim() || saveMutation.isPending}>
            {saveMutation.isPending ? <Spinner /> : isEdit ? "Save changes" : "Create"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
