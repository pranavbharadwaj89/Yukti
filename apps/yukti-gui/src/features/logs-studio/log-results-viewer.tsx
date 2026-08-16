import { useState } from "react";
import { useLiveRunProgress } from "@/hooks";
import { Card, Spinner, StatusPill } from "@/components/ui/primitives";
import { CodeEditor } from "@/components/ui/code-editor";
import { Tabs, TabList, Tab, TabPanel } from "@/components/ui/tabs";
import type { LogCheckRulesResultData, LogAnomalyResultData } from "@/services/types";

function isCheckRulesResultData(data: unknown): data is LogCheckRulesResultData {
  return typeof data === "object" && data !== null && "linesScanned" in data;
}

function isAnomalyResultData(data: unknown): data is LogAnomalyResultData {
  return typeof data === "object" && data !== null && "bucketsScanned" in data;
}

// Mirrors api-studio/response-viewer.tsx's structure: live-updates via the
// same SignalR+REST-catch-up hook, narrows the module-owned StepResultResponse.data
// via a type guard, falls back to the raw step message/error when the step
// didn't run to a structured-data outcome (e.g. a transport-level failure).
export function LogResultsViewer({
  runId,
  action,
}: {
  runId: string | undefined;
  action: "checkRules" | "detectAnomalies";
}) {
  const { run, error } = useLiveRunProgress(runId);
  const [tab, setTab] = useState("results");

  if (!runId) return null;

  if (error) {
    return (
      <Card className="p-4">
        <p className="text-body-sm text-danger">{error}</p>
      </Card>
    );
  }

  if (!run) {
    return (
      <Card className="flex items-center gap-2 p-4">
        <Spinner /> <span className="text-body-sm text-ink-dim">Waiting for result…</span>
      </Card>
    );
  }

  const step = run.results[0];
  const isTerminal = run.status === "Passed" || run.status === "Failed" || run.status === "Cancelled";

  if (!step) {
    return (
      <Card className="flex items-center gap-2 p-4">
        <Spinner /> <span className="text-body-sm text-ink-dim">Waiting for result…</span>
      </Card>
    );
  }

  const checkRulesData = action === "checkRules" && isCheckRulesResultData(step.data) ? step.data : undefined;
  const anomalyData = action === "detectAnomalies" && isAnomalyResultData(step.data) ? step.data : undefined;

  if (!checkRulesData && !anomalyData) {
    return (
      <Card className="flex flex-col gap-2 p-4">
        <div className="flex items-center justify-between">
          <h2 className="text-body-sm font-medium text-ink">Result</h2>
          <StatusPill status={step.status} />
        </div>
        <p className={`text-body-sm ${step.error ? "text-danger" : "text-ink-dim"}`}>
          {step.error ?? step.message ?? "No result data."}
        </p>
      </Card>
    );
  }

  return (
    <Card className="flex flex-col gap-3 p-4">
      <div className="flex items-center gap-3">
        <StatusPill status={isTerminal ? run.status : "Running"} />
        {checkRulesData && <span className="text-body-sm text-ink-dim">{checkRulesData.linesScanned} lines scanned</span>}
        {anomalyData && <span className="text-body-sm text-ink-dim">{anomalyData.bucketsScanned} buckets scanned</span>}
      </div>

      <Tabs value={tab} onChange={setTab}>
        <TabList>
          <Tab value="results">Results</Tab>
          <Tab value="raw">Raw</Tab>
        </TabList>

        <TabPanel value="results" className="pt-3">
          {checkRulesData && (
            <table className="w-full text-body-sm">
              <thead>
                <tr className="border-b border-border text-left text-ink-dim">
                  <th className="pb-1.5 pr-3 font-medium">Rule</th>
                  <th className="pb-1.5 pr-3 font-medium">Count</th>
                  <th className="pb-1.5 font-medium">Samples</th>
                </tr>
              </thead>
              <tbody>
                {checkRulesData.matches.map((m) => (
                  <tr key={m.rule} className="border-b border-border last:border-0 align-top">
                    <td className="py-1.5 pr-3 font-mono text-ink">{m.rule}</td>
                    <td className="py-1.5 pr-3 font-mono text-ink">{m.count}</td>
                    <td className="py-1.5 font-mono text-ink-dim">
                      {m.samples.map((s, i) => (
                        <div key={i} className="truncate">
                          {s}
                        </div>
                      ))}
                    </td>
                  </tr>
                ))}
                {checkRulesData.matches.length === 0 && (
                  <tr>
                    <td colSpan={3} className="py-1.5 text-ink-dim">
                      No rule matches.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          )}

          {anomalyData && (
            <div className="flex flex-col gap-3">
              <div className="flex gap-4 text-body-sm text-ink-dim">
                <span>mean: {anomalyData.mean.toFixed(3)}</span>
                <span>stdDev: {anomalyData.stdDev.toFixed(3)}</span>
                <span>threshold: {anomalyData.threshold}</span>
              </div>
              <table className="w-full text-body-sm">
                <thead>
                  <tr className="border-b border-border text-left text-ink-dim">
                    <th className="pb-1.5 pr-3 font-medium">Bucket</th>
                    <th className="pb-1.5 pr-3 font-medium">Error rate</th>
                    <th className="pb-1.5 pr-3 font-medium">Errors</th>
                    <th className="pb-1.5 font-medium">Total</th>
                  </tr>
                </thead>
                <tbody>
                  {anomalyData.anomalousBuckets.map((b) => (
                    <tr key={b.bucket} className="border-b border-border last:border-0">
                      <td className="py-1.5 pr-3 font-mono text-ink">{b.bucket}</td>
                      <td className="py-1.5 pr-3 font-mono text-ink">{b.errorRate.toFixed(3)}</td>
                      <td className="py-1.5 pr-3 font-mono text-ink">{b.errors}</td>
                      <td className="py-1.5 font-mono text-ink">{b.total}</td>
                    </tr>
                  ))}
                  {anomalyData.anomalousBuckets.length === 0 && (
                    <tr>
                      <td colSpan={4} className="py-1.5 text-ink-dim">
                        No anomalies detected.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )}
        </TabPanel>

        <TabPanel value="raw" className="pt-3">
          <CodeEditor value={JSON.stringify(checkRulesData ?? anomalyData, null, 2)} language="json" height={280} readOnly />
        </TabPanel>
      </Tabs>
    </Card>
  );
}
