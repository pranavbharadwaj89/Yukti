import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { flowsApi, getActionParams, modulesApi, runsApi, ApiError } from "@/services/api-client";
import type { FlowRunResponse } from "@/services/types";
import { Button, Card, Input, Spinner, StatusPill } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { useToastStore } from "@/store/toast-store";
import { useSelectedEnvironmentVariables } from "@/hooks";
import { ParamFields, buildParamsFromFields, type FieldValue } from "@/features/shared/param-fields";

// Each Tests tab (Web/Mobile/API) is a standalone single-action test runner
// against its module's real plugin — no dependency on the Flows builder UI.
// Under the hood it still uses the same flow endpoints (Yukti has no
// separate "run one action" API), but that's invisible here: create,
// add the one step, publish, run, and poll the result inline. The
// throwaway flow this creates is named so it's identifiable in the Flows
// list, not hidden.

function isTerminal(status: string) {
  return status === "Passed" || status === "Failed" || status === "Cancelled";
}

// MobileModule.Setup (src/Yukti.Infrastructure.InMemory/Modules/MobileModule.cs)
// only opens an Appium session if ctx.Variables.mobile is populated — that
// dictionary comes from TriggerFlowRunCommand.VariableOverrides, not step
// params. Without this panel there was no way to supply it, so every
// Mobile test failed with "not set up" regardless of what the user filled
// into the action form above.
interface MobileDeviceConfig {
  platformName: string;
  deviceName: string;
  automationName: string;
  appiumUrl: string;
  app: string;
}

const emptyDeviceConfig: MobileDeviceConfig = { platformName: "", deviceName: "", automationName: "", appiumUrl: "", app: "" };

function buildMobileVariableOverrides(config: MobileDeviceConfig): Record<string, unknown> | undefined {
  const mobile: Record<string, unknown> = {};
  if (config.platformName.trim()) mobile.platformName = config.platformName.trim();
  if (config.deviceName.trim()) mobile.deviceName = config.deviceName.trim();
  if (config.automationName.trim()) mobile.automationName = config.automationName.trim();
  if (config.appiumUrl.trim()) mobile.appiumUrl = config.appiumUrl.trim();
  if (config.app.trim()) mobile.app = config.app.trim();
  return Object.keys(mobile).length > 0 ? { mobile } : undefined;
}

function isPathData(data: unknown): data is { path: string } {
  return typeof data === "object" && data !== null && typeof (data as Record<string, unknown>).path === "string";
}

function isMatchData(data: unknown): data is { x: number; y: number; confidence?: number } {
  return (
    typeof data === "object" &&
    data !== null &&
    typeof (data as Record<string, unknown>).x === "number" &&
    typeof (data as Record<string, unknown>).y === "number"
  );
}

export function ModuleTestForm({ moduleKind, title }: { moduleKind: string; title: string }) {
  const queryClient = useQueryClient();
  const pushToast = useToastStore((s) => s.push);
  const modulesQuery = useQuery({ queryKey: ["modules"], queryFn: modulesApi.list });

  const [action, setAction] = useState("");
  const [fieldValues, setFieldValues] = useState<Record<string, FieldValue>>({});
  const [runId, setRunId] = useState<string | null>(null);
  const [deviceConfig, setDeviceConfig] = useState<MobileDeviceConfig>(emptyDeviceConfig);
  const environmentVariables = useSelectedEnvironmentVariables();

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
      const params = buildParamsFromFields(selectedActionSchema, fieldValues);
      const { flowId } = await flowsApi.create(`${title} — ${new Date().toLocaleString()}`, `Ad-hoc ${moduleKind}.${action} test run from the Tests tab.`);
      await flowsApi.addStep(flowId, { name: action, module: moduleKind, action, params });
      const publishResult = await flowsApi.publish(flowId);
      if (!publishResult.succeeded) throw new Error(publishResult.errors.join("; "));
      // Environment variables are the base (e.g. a Project's saved Mobile
      // device config); the form's own Device config panel — if filled —
      // takes priority, since a manual override at run time is more
      // specific than the saved default.
      const manualMobileOverride = moduleKind === "mobile" ? buildMobileVariableOverrides(deviceConfig) : undefined;
      const variableOverrides =
        environmentVariables || manualMobileOverride
          ? { ...environmentVariables, ...manualMobileOverride }
          : undefined;
      return flowsApi.triggerRun(flowId, variableOverrides);
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

      {moduleKind === "mobile" && (
        <Card className="flex flex-col gap-3 p-4">
          <h2 className="text-body-sm font-medium text-ink">Device config</h2>
          <p className="text-caption text-ink-dim">
            Required for the module to open an Appium session — without this, every action fails with "not set up".
          </p>
          <div className="grid grid-cols-2 gap-3">
            <div className="flex flex-col gap-1">
              <label className="text-body-sm text-ink-dim">Platform name*</label>
              <Input
                placeholder="Android or iOS"
                value={deviceConfig.platformName}
                onChange={(e) => setDeviceConfig((c) => ({ ...c, platformName: e.target.value }))}
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-body-sm text-ink-dim">Device name*</label>
              <Input
                value={deviceConfig.deviceName}
                onChange={(e) => setDeviceConfig((c) => ({ ...c, deviceName: e.target.value }))}
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-body-sm text-ink-dim">Automation name*</label>
              <Input
                placeholder="UiAutomator2 or XCUITest"
                value={deviceConfig.automationName}
                onChange={(e) => setDeviceConfig((c) => ({ ...c, automationName: e.target.value }))}
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-body-sm text-ink-dim">Appium URL</label>
              <Input
                placeholder="http://localhost:4723"
                value={deviceConfig.appiumUrl}
                onChange={(e) => setDeviceConfig((c) => ({ ...c, appiumUrl: e.target.value }))}
              />
            </div>
            <div className="flex flex-col gap-1">
              <label className="text-body-sm text-ink-dim">App</label>
              <Input value={deviceConfig.app} onChange={(e) => setDeviceConfig((c) => ({ ...c, app: e.target.value }))} />
            </div>
          </div>
        </Card>
      )}

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
          <ParamFields schema={selectedActionSchema} values={fieldValues} onChange={setFieldValues} />
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
                {isPathData(r.data) && <p className="font-mono text-body-sm text-ink-dim">Saved: {r.data.path}</p>}
                {isMatchData(r.data) && (
                  <p className="font-mono text-body-sm text-ink-dim">
                    Match at ({r.data.x}, {r.data.y}){r.data.confidence !== undefined ? ` — confidence ${r.data.confidence}` : ""}
                  </p>
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
