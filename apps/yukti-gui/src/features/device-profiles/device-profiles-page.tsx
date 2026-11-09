import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { deviceProfilesApi, ApiError } from "@/services/api-client";
import type { DeviceProfileResponse, DeviceProfileSummaryResponse, DeviceProfileTestResponse } from "@/services/types";
import { deviceEndpointKindLabel, mobilePlatformLabel, resetStrategyLabel } from "@/services/types";
import { Button, Card, Dialog, Input, Spinner } from "@/components/ui/primitives";
import { ConfirmDialog } from "@/components/ui/feedback";
import { DataTable, type Column } from "@/components/ui/data-table";
import { useToastStore } from "@/store/toast-store";

// Mobile Testing Module Spec, MOB-T2: "Admin-configured, tenant-scoped. A flow references a
// profile by name, never a raw capability blob — the same principle as DB connections." Same
// page shape as RequestTemplatesPage (DISC-20) — a flow referencing this by name is the mobile
// module's own equivalent of a flow referencing a request template by templateId.

const selectClassName =
  "w-full rounded-md border border-border bg-surface px-3 py-2 text-sm text-ink focus:border-accent";

export function DeviceProfilesPage() {
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<DeviceProfileResponse | null>(null);
  const [deleting, setDeleting] = useState<DeviceProfileSummaryResponse | null>(null);
  const [testResult, setTestResult] = useState<{ name: string; result: DeviceProfileTestResponse } | null>(null);
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);

  const profilesQuery = useQuery({ queryKey: ["device-profiles"], queryFn: deviceProfilesApi.list });

  const deleteMutation = useMutation({
    mutationFn: (profileId: string) => deviceProfilesApi.delete(profileId),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["device-profiles"] });
      pushToast({ kind: "success", message: "Device profile deleted." });
      setDeleting(null);
    },
    onError: (err) => {
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to delete device profile." });
      setDeleting(null);
    },
  });

  const testMutation = useMutation({
    mutationFn: (profile: DeviceProfileSummaryResponse) => deviceProfilesApi.testConnection(profile.id),
    onSuccess: (result, profile) => setTestResult({ name: profile.name, result }),
    onError: (err) =>
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to test device profile." }),
  });

  const columns: Column<DeviceProfileSummaryResponse>[] = [
    {
      key: "name",
      header: "Name",
      sortable: true,
      sortValue: (p) => p.name.toLowerCase(),
      render: (p) => <span className="font-medium text-ink">{p.name}</span>,
    },
    { key: "platform", header: "Platform", render: (p) => <span className="text-ink-dim">{mobilePlatformLabel(p.platform)}</span> },
    { key: "deviceName", header: "Device", render: (p) => <span className="text-ink-dim">{p.deviceName ?? "—"}</span> },
    { key: "endpointKind", header: "Endpoint", render: (p) => <span className="text-ink-dim">{deviceEndpointKindLabel(p.endpointKind)}</span> },
    { key: "version", header: "Version", align: "right", render: (p) => <span className="text-ink-dim">v{p.version}</span> },
    {
      key: "actions",
      header: "",
      align: "right",
      render: (p) => (
        <div className="flex justify-end gap-2">
          <Button variant="ghost" disabled={testMutation.isPending} onClick={() => testMutation.mutate(p)}>
            {testMutation.isPending && testMutation.variables?.id === p.id ? <Spinner className="h-4 w-4" /> : "Test connection"}
          </Button>
          <Button
            variant="ghost"
            onClick={async () => {
              try {
                const full = await deviceProfilesApi.get(p.id);
                setEditing(full);
              } catch (err) {
                pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to fetch device profile." });
              }
            }}
          >
            Edit
          </Button>
          <Button variant="ghost" onClick={() => setDeleting(p)}>
            Delete
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 text-ink">Device Profiles</h1>
        <Button onClick={() => setCreateOpen(true)}>New profile</Button>
      </div>

      <p className="text-body-sm text-ink-dim">
        An Appium endpoint, device, and app a mobile flow references by{" "}
        <code className="rounded bg-surface-2 px-1 py-0.5 font-mono text-caption">profileName</code>. Use{" "}
        <span className="text-ink">Test connection</span> to catch a misconfiguration here, before a flow run does.
      </p>

      <DataTable
        columns={columns}
        rows={profilesQuery.data ?? []}
        rowKey={(p) => p.id}
        loading={profilesQuery.isLoading}
        emptyTitle="No device profiles yet"
        emptyDescription="Create one to let a mobile flow reference an Appium endpoint and device by name."
        pageSize={10}
      />

      <DeviceProfileDialog open={createOpen} profile={null} onClose={() => setCreateOpen(false)} />
      <DeviceProfileDialog open={editing !== null} profile={editing} onClose={() => setEditing(null)} />

      <ConfirmDialog
        open={deleting !== null}
        onCancel={() => setDeleting(null)}
        onConfirm={() => deleting && deleteMutation.mutate(deleting.id)}
        title="Delete device profile?"
        message={`"${deleting?.name}" will be removed. Any flow step still referencing it by profileName will fail to resolve on its next run.`}
        confirmLabel="Delete"
      />

      <TestResultDialog open={testResult !== null} name={testResult?.name ?? ""} result={testResult?.result ?? null} onClose={() => setTestResult(null)} />
    </div>
  );
}

function TestResultDialog({
  open,
  name,
  result,
  onClose,
}: {
  open: boolean;
  name: string;
  result: DeviceProfileTestResponse | null;
  onClose: () => void;
}) {
  return (
    <Dialog open={open} onClose={onClose} title={`Test connection — "${name}"`}>
      <div className="flex flex-col gap-3">
        {result?.success ? (
          <p className="text-body-sm text-success">Connected and took a screenshot successfully.</p>
        ) : (
          <p className="text-body-sm text-danger">{result?.error ?? "Connection failed."}</p>
        )}
        {result?.screenshot ? (
          <img
            src={`data:image/png;base64,${result.screenshot}`}
            alt={`Screenshot from device profile "${name}"`}
            className="max-h-96 w-auto rounded-md border border-border"
          />
        ) : null}
        <div className="flex justify-end">
          <Button variant="secondary" onClick={onClose}>
            Close
          </Button>
        </div>
      </div>
    </Dialog>
  );
}

function DeviceProfileDialog({
  open,
  profile,
  onClose,
}: {
  open: boolean;
  profile: DeviceProfileResponse | null;
  onClose: () => void;
}) {
  const isEdit = profile !== null;
  const [name, setName] = useState(profile?.name ?? "");
  // Typed as the request DTOs' own literal unions (0 | 1, 0 | 1 | 2), not plain `number` --
  // `DeviceProfileResponse`'s fields are the wider `number` (generated from an int column), so a
  // profile's value is cast at the one point it enters the form instead of widening the state
  // itself and failing the save request's own structural type check at the call site.
  const [platform, setPlatform] = useState<0 | 1>((profile?.platform ?? 0) as 0 | 1);
  const [platformVersion, setPlatformVersion] = useState(profile?.platformVersion ?? "");
  const [deviceName, setDeviceName] = useState(profile?.deviceName ?? "");
  const [app, setApp] = useState(profile?.app ?? "");
  const [appiumUrl, setAppiumUrl] = useState(profile?.appiumUrl ?? "http://localhost:4723");
  const [endpointKind, setEndpointKind] = useState<0 | 1 | 2>((profile?.endpointKind ?? 0) as 0 | 1 | 2);
  const [farmCredentialRef, setFarmCredentialRef] = useState(profile?.farmCredentialRef ?? "");
  const [reset, setReset] = useState<0 | 1 | 2>((profile?.reset ?? 1) as 0 | 1 | 2);
  const [sessionTimeoutSeconds, setSessionTimeoutSeconds] = useState((profile?.sessionTimeoutSeconds ?? 300).toString());
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);

  // Re-seeds the form every time the dialog opens -- for edit, when a different profile is
  // opened; for create, every reopen, not just the first -- otherwise `profile` stays `null`
  // across every "New profile" open/close cycle and dirty, unsaved values from a prior open
  // leak into the next one. `closed` is a key no real profile id or "create" can ever collide
  // with, so closing always forces the next open to reseed.
  const seedKey = open ? (profile?.id ?? "create") : "closed";
  const [seededFor, setSeededFor] = useState(seedKey);
  if (seedKey !== seededFor) {
    setSeededFor(seedKey);
    setName(profile?.name ?? "");
    setPlatform((profile?.platform ?? 0) as 0 | 1);
    setPlatformVersion(profile?.platformVersion ?? "");
    setDeviceName(profile?.deviceName ?? "");
    setApp(profile?.app ?? "");
    setAppiumUrl(profile?.appiumUrl ?? "http://localhost:4723");
    setEndpointKind((profile?.endpointKind ?? 0) as 0 | 1 | 2);
    setFarmCredentialRef(profile?.farmCredentialRef ?? "");
    setReset((profile?.reset ?? 1) as 0 | 1 | 2);
    setSessionTimeoutSeconds((profile?.sessionTimeoutSeconds ?? 300).toString());
  }

  const saveMutation = useMutation({
    mutationFn: async () => {
      const req = {
        name,
        platform,
        platformVersion: platformVersion.trim() || null,
        deviceName: deviceName.trim() || null,
        app: app.trim() || null,
        appiumUrl,
        endpointKind,
        farmCredentialRef: farmCredentialRef.trim() || null,
        reset,
        // Editing has no field for this yet -- preserve whatever is already on the profile
        // (e.g. set via the API directly) instead of silently wiping it on every save.
        extraCapabilities: isEdit ? profile?.extraCapabilities ?? null : null,
      };
      if (isEdit && profile) {
        await deviceProfilesApi.update(profile.id, { ...req, sessionTimeoutSeconds: Number(sessionTimeoutSeconds) || 300 });
      } else {
        await deviceProfilesApi.create({
          ...req,
          sessionTimeoutSeconds: sessionTimeoutSeconds.trim() ? Number(sessionTimeoutSeconds) : null,
        });
      }
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ["device-profiles"] });
      pushToast({ kind: "success", message: isEdit ? "Device profile updated." : "Device profile created." });
      onClose();
    },
    onError: (err) =>
      pushToast({ kind: "error", message: err instanceof ApiError ? err.message : "Failed to save device profile." }),
  });

  return (
    <Dialog open={open} onClose={onClose} title={isEdit ? `Edit "${profile?.name}"` : "New device profile"}>
      <form
        className="flex max-h-[70vh] flex-col gap-4 overflow-y-auto"
        onSubmit={(e) => {
          e.preventDefault();
          saveMutation.mutate();
        }}
      >
        <Input placeholder="Profile name" value={name} onChange={(e) => setName(e.target.value)} required autoFocus />

        <div className="flex gap-3">
          <select className={selectClassName} value={platform} onChange={(e) => setPlatform(Number(e.target.value) as 0 | 1)} aria-label="Platform">
            <option value={0}>Android</option>
            <option value={1}>iOS</option>
          </select>
          <Input placeholder="Platform version (optional)" value={platformVersion} onChange={(e) => setPlatformVersion(e.target.value)} />
        </div>

        <Input placeholder="Device name (optional)" value={deviceName} onChange={(e) => setDeviceName(e.target.value)} />
        <Input placeholder="App path, URL, or installed bundle id (optional)" value={app} onChange={(e) => setApp(e.target.value)} />
        <Input placeholder="Appium endpoint URL" value={appiumUrl} onChange={(e) => setAppiumUrl(e.target.value)} required />

        <div className="flex gap-3">
          <select className={selectClassName} value={endpointKind} onChange={(e) => setEndpointKind(Number(e.target.value) as 0 | 1 | 2)} aria-label="Endpoint kind">
            <option value={0}>Local</option>
            <option value={1}>Self-hosted</option>
            <option value={2}>Device farm</option>
          </select>
          <select className={selectClassName} value={reset} onChange={(e) => setReset(Number(e.target.value) as 0 | 1 | 2)} aria-label="Reset strategy">
            <option value={0}>Full reset</option>
            <option value={1}>Fast reset</option>
            <option value={2}>No reset</option>
          </select>
        </div>

        {endpointKind !== 0 ? (
          <Input
            placeholder="Farm credential reference (resolved via {{secrets}}, never a literal key)"
            value={farmCredentialRef}
            onChange={(e) => setFarmCredentialRef(e.target.value)}
          />
        ) : null}

        <Input
          placeholder="Session timeout (seconds)"
          type="number"
          value={sessionTimeoutSeconds}
          onChange={(e) => setSessionTimeoutSeconds(e.target.value)}
        />

        <Card className="p-3">
          <p className="text-body-sm text-ink-dim">
            {mobilePlatformLabel(platform)} · {deviceEndpointKindLabel(endpointKind)} · {resetStrategyLabel(reset)} reset
          </p>
        </Card>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={!name.trim() || !appiumUrl.trim() || saveMutation.isPending}>
            {saveMutation.isPending ? <Spinner /> : isEdit ? "Save changes" : "Create"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
