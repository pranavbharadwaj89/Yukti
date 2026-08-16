import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { projectsApi, environmentsApi, ApiError } from "@/services/api-client";
import type { TestEnvironmentResponse } from "@/services/types";
import { Button, Card, Dialog, Input, Spinner } from "@/components/ui/primitives";
import { DataTable, type Column } from "@/components/ui/data-table";
import { useProjectStore } from "@/store/project-store";
import { useToastStore } from "@/store/toast-store";
import { KeyValueEditor, newKeyValueRow, type KeyValueRow } from "@/features/api-studio/key-value-editor";

interface ProjectRow {
  id: string;
  name: string;
  description?: string | null;
}

// Mirrors flows-page.tsx's DataTable + create-dialog pattern, plus a
// selected-project's Environments panel (variables + Mobile device config,
// the same fields module-test-form.tsx's Mobile Device config panel
// collects — stored under Variables.mobile so MobileModule.Setup can read
// it unchanged, see TestEnvironment.cs's doc comment).
export function ProjectsPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const selectedProjectId = useProjectStore((s) => s.selectedProjectId);
  const selectProject = useProjectStore((s) => s.selectProject);
  const projectsQuery = useQuery({ queryKey: ["projects"], queryFn: projectsApi.list });

  const columns: Column<ProjectRow>[] = [
    {
      key: "name",
      header: "Name",
      sortable: true,
      sortValue: (p) => p.name.toLowerCase(),
      render: (p) => (
        <button
          type="button"
          className={`text-left hover:text-accent hover:underline ${p.id === selectedProjectId ? "text-accent" : "text-ink"}`}
          onClick={() => selectProject(p.id)}
        >
          {p.name}
          {p.id === selectedProjectId && <span className="ml-2 text-caption text-ink-dim">(active)</span>}
        </button>
      ),
    },
    { key: "description", header: "Description", render: (p) => <span className="text-ink-dim">{p.description ?? "—"}</span> },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 text-ink">Projects</h1>
        <Button onClick={() => setCreateOpen(true)}>New project</Button>
      </div>

      <p className="text-body-sm text-ink-dim">
        Click a project name to make it the active project — Flows and the API Explorer scope to whichever one is active.
      </p>

      <DataTable
        columns={columns}
        rows={projectsQuery.data ?? []}
        rowKey={(p) => p.id}
        loading={projectsQuery.isLoading}
        emptyTitle="No projects yet"
        emptyDescription="Create one to get started."
        pageSize={10}
      />

      {selectedProjectId && <EnvironmentsPanel projectId={selectedProjectId} />}

      <CreateProjectDialog open={createOpen} onClose={() => setCreateOpen(false)} />
    </div>
  );
}

function CreateProjectDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);

  const createMutation = useMutation({
    mutationFn: () => projectsApi.create(name, description || undefined),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["projects"] });
      pushToast({ kind: "success", message: `Project "${name}" created.` });
      setName("");
      setDescription("");
      onClose();
    },
    onError: (err) =>
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to create project." }),
  });

  return (
    <Dialog open={open} onClose={onClose} title="New project">
      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault();
          createMutation.mutate();
        }}
      >
        <Input placeholder="Project name" value={name} onChange={(e) => setName(e.target.value)} required autoFocus />
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

function variablesToRows(variables: Record<string, unknown>): KeyValueRow[] {
  const entries = Object.entries(variables).filter(([key]) => key !== "mobile");
  const rows = entries.map(([key, value]) => ({
    ...newKeyValueRow(),
    key,
    value: typeof value === "string" ? value : JSON.stringify(value),
  }));
  return rows.length > 0 ? rows : [newKeyValueRow()];
}

interface MobileConfigDraft {
  platformName: string;
  deviceName: string;
  automationName: string;
  appiumUrl: string;
  app: string;
}

function mobileConfigFromVariables(variables: Record<string, unknown>): MobileConfigDraft {
  const mobile = (variables.mobile as Record<string, unknown> | undefined) ?? {};
  return {
    platformName: typeof mobile.platformName === "string" ? mobile.platformName : "",
    deviceName: typeof mobile.deviceName === "string" ? mobile.deviceName : "",
    automationName: typeof mobile.automationName === "string" ? mobile.automationName : "",
    appiumUrl: typeof mobile.appiumUrl === "string" ? mobile.appiumUrl : "",
    app: typeof mobile.app === "string" ? mobile.app : "",
  };
}

function buildVariables(rows: KeyValueRow[], mobile: MobileConfigDraft): Record<string, unknown> {
  const variables: Record<string, unknown> = {};
  for (const row of rows.filter((r) => r.enabled && r.key.trim() !== "")) {
    variables[row.key] = row.value;
  }
  const mobileConfig: Record<string, unknown> = {};
  if (mobile.platformName.trim()) mobileConfig.platformName = mobile.platformName.trim();
  if (mobile.deviceName.trim()) mobileConfig.deviceName = mobile.deviceName.trim();
  if (mobile.automationName.trim()) mobileConfig.automationName = mobile.automationName.trim();
  if (mobile.appiumUrl.trim()) mobileConfig.appiumUrl = mobile.appiumUrl.trim();
  if (mobile.app.trim()) mobileConfig.app = mobile.app.trim();
  if (Object.keys(mobileConfig).length > 0) variables.mobile = mobileConfig;
  return variables;
}

function EnvironmentsPanel({ projectId }: { projectId: string }) {
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);
  const selectedEnvironmentId = useProjectStore((s) => s.selectedEnvironmentId);
  const selectEnvironment = useProjectStore((s) => s.selectEnvironment);
  const environmentsQuery = useQuery({
    queryKey: ["environments", projectId],
    queryFn: () => environmentsApi.list(projectId),
  });

  const [editing, setEditing] = useState<TestEnvironmentResponse | null>(null);
  const [name, setName] = useState("");
  const [rows, setRows] = useState<KeyValueRow[]>([newKeyValueRow()]);
  const [mobile, setMobile] = useState<MobileConfigDraft>({ platformName: "", deviceName: "", automationName: "", appiumUrl: "", app: "" });

  function startNew() {
    setEditing(null);
    setName("");
    setRows([newKeyValueRow()]);
    setMobile({ platformName: "", deviceName: "", automationName: "", appiumUrl: "", app: "" });
  }

  function startEdit(env: TestEnvironmentResponse) {
    setEditing(env);
    setName(env.name);
    setRows(variablesToRows(env.variables));
    setMobile(mobileConfigFromVariables(env.variables));
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      const variables = buildVariables(rows, mobile);
      if (editing) {
        await environmentsApi.update(projectId, editing.id, name, variables);
      } else {
        await environmentsApi.create(projectId, name, variables);
      }
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["environments", projectId] });
      pushToast({ kind: "success", message: "Environment saved." });
      startNew();
    },
    onError: (err) =>
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to save environment." }),
  });

  const deleteMutation = useMutation({
    mutationFn: (environmentId: string) => environmentsApi.delete(projectId, environmentId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["environments", projectId] });
      pushToast({ kind: "success", message: "Environment deleted." });
    },
  });

  return (
    <Card className="flex flex-col gap-4 p-4">
      <div className="flex items-center justify-between">
        <h2 className="text-body-sm font-medium text-ink">Environments</h2>
        <Button variant="secondary" onClick={startNew}>
          New environment
        </Button>
      </div>

      {environmentsQuery.isLoading ? (
        <Spinner />
      ) : (
        <div className="flex flex-col gap-2">
          {(environmentsQuery.data ?? []).map((env) => (
            <div
              key={env.id}
              className={`flex items-center justify-between rounded-md border px-3 py-2 ${env.id === selectedEnvironmentId ? "border-accent" : "border-border"}`}
            >
              <button
                type="button"
                className="text-left text-sm text-ink hover:text-accent hover:underline"
                onClick={() => selectEnvironment(env.id)}
              >
                {env.name}
                {env.id === selectedEnvironmentId && <span className="ml-2 text-caption text-ink-dim">(active)</span>}
              </button>
              <div className="flex gap-2">
                <Button variant="ghost" onClick={() => startEdit(env)}>
                  Edit
                </Button>
                <Button variant="ghost" onClick={() => deleteMutation.mutate(env.id)}>
                  Delete
                </Button>
              </div>
            </div>
          ))}
          {(environmentsQuery.data ?? []).length === 0 && <div className="text-body-sm text-ink-dim">No environments yet.</div>}
        </div>
      )}

      <div className="flex flex-col gap-3 border-t border-border pt-4">
        <Input placeholder="Environment name" value={name} onChange={(e) => setName(e.target.value)} />

        <div className="flex flex-col gap-1">
          <label className="text-body-sm text-ink-dim">Variables</label>
          <KeyValueEditor rows={rows} onChange={setRows} keyPlaceholder="Key" valuePlaceholder="Value" />
        </div>

        <div className="flex flex-col gap-2">
          <label className="text-body-sm text-ink-dim">Mobile device config (optional)</label>
          <div className="grid grid-cols-2 gap-2">
            <Input placeholder="Platform name" value={mobile.platformName} onChange={(e) => setMobile((m) => ({ ...m, platformName: e.target.value }))} />
            <Input placeholder="Device name" value={mobile.deviceName} onChange={(e) => setMobile((m) => ({ ...m, deviceName: e.target.value }))} />
            <Input placeholder="Automation name" value={mobile.automationName} onChange={(e) => setMobile((m) => ({ ...m, automationName: e.target.value }))} />
            <Input placeholder="Appium URL" value={mobile.appiumUrl} onChange={(e) => setMobile((m) => ({ ...m, appiumUrl: e.target.value }))} />
            <Input placeholder="App" value={mobile.app} onChange={(e) => setMobile((m) => ({ ...m, app: e.target.value }))} />
          </div>
        </div>

        <div className="flex justify-end gap-2">
          {editing && (
            <Button variant="secondary" onClick={startNew}>
              Cancel edit
            </Button>
          )}
          <Button onClick={() => saveMutation.mutate()} disabled={!name.trim() || saveMutation.isPending}>
            {saveMutation.isPending ? <Spinner /> : editing ? "Save changes" : "Create environment"}
          </Button>
        </div>
      </div>
    </Card>
  );
}
