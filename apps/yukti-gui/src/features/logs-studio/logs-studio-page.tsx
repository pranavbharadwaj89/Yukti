import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { flowsApi, ApiError } from "@/services/api-client";
import type { FlowRunResponse } from "@/services/types";
import { Button, Card, Input, Spinner, Textarea } from "@/components/ui/primitives";
import { Tabs, TabList, Tab, TabPanel } from "@/components/ui/tabs";
import { useToastStore } from "@/store/toast-store";
import { useSelectedEnvironmentVariables } from "@/hooks";
import { RulesEditor, newRuleRow, type RuleRow } from "./rules-editor";
import { LogResultsViewer } from "./log-results-viewer";

type LogsAction = "checkRules" | "detectAnomalies";

function buildRulesPayload(rows: RuleRow[]) {
  return rows
    .filter((r) => r.name.trim() !== "" && r.pattern.trim() !== "")
    .map((r) => ({
      name: r.name,
      pattern: r.pattern,
      maxAllowed: Number(r.maxAllowed) || 0,
      severity: r.severity,
    }));
}

// Dedicated Logs Studio — mirrors api-studio/request-designer.tsx's
// execution shape (LogsModule has no separate "run one action" endpoint
// either, same ad-hoc create->addStep->publish->triggerRun sequence) but
// exposes both LogsModule actions (checkRules/detectAnomalies) as tabs
// instead of one generic per-param JSON form. No saved-test-case Explorer
// here — the backend has no collections concept for Logs, unlike API's
// apiCollectionsApi, so there's nothing to persist against.
export function LogsStudioPage() {
  const pushToast = useToastStore((s) => s.push);
  const environmentVariables = useSelectedEnvironmentVariables();

  const [action, setAction] = useState<LogsAction>("checkRules");
  const [logText, setLogText] = useState("");
  const [ruleRows, setRuleRows] = useState<RuleRow[]>([newRuleRow()]);
  const [stdDevThreshold, setStdDevThreshold] = useState("2.0");
  const [runId, setRunId] = useState<string | undefined>(undefined);

  const runMutation = useMutation({
    mutationFn: async () => {
      const params: Record<string, unknown> =
        action === "checkRules"
          ? { logText, rules: buildRulesPayload(ruleRows) }
          : { logText, stdDevThreshold: Number(stdDevThreshold) || 2.0 };

      const { flowId } = await flowsApi.create(
        `Logs Test — ${new Date().toLocaleString()}`,
        "Ad-hoc logs test from Logs Studio.",
      );
      await flowsApi.addStep(flowId, { name: "test", module: "logs", action, params });
      const publishResult = await flowsApi.publish(flowId);
      if (!publishResult.succeeded) throw new Error(publishResult.errors.join("; "));
      return flowsApi.triggerRun(flowId, environmentVariables);
    },
    onSuccess: (run: FlowRunResponse) => setRunId(run.id),
    onError: (err) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : err instanceof Error ? err.message : "Failed to run test.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      }),
  });

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 text-ink">Logs Testing</h1>
        <Button
          onClick={() => runMutation.mutate()}
          disabled={!logText.trim() || runMutation.isPending}
        >
          {runMutation.isPending ? <Spinner /> : "Run test"}
        </Button>
      </div>

      <Card className="flex flex-col gap-3 p-4">
        <label className="text-body-sm text-ink-dim">Log text</label>
        <Textarea
          rows={8}
          placeholder="Paste log lines here…"
          value={logText}
          onChange={(e) => setLogText(e.target.value)}
        />
      </Card>

      <Tabs
        value={action}
        onChange={(v) => {
          setAction(v as LogsAction);
          setRunId(undefined);
        }}
      >
        <TabList>
          <Tab value="checkRules">Check Rules</Tab>
          <Tab value="detectAnomalies">Detect Anomalies</Tab>
        </TabList>

        <TabPanel value="checkRules" className="pt-3">
          <RulesEditor rows={ruleRows} onChange={setRuleRows} />
        </TabPanel>

        <TabPanel value="detectAnomalies" className="flex flex-col gap-1 pt-3">
          <label className="text-body-sm text-ink-dim">Std-dev threshold</label>
          <Input
            className="w-32"
            type="number"
            step="0.1"
            min={0}
            value={stdDevThreshold}
            onChange={(e) => setStdDevThreshold(e.target.value)}
          />
        </TabPanel>
      </Tabs>

      <LogResultsViewer runId={runId} action={action} />
    </div>
  );
}
