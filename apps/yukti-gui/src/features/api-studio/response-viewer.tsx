import { useState } from "react";
import { CheckCircle2, XCircle } from "lucide-react";
import { useLiveRunProgress } from "@/hooks";
import { Card, Spinner, StatusPill } from "@/components/ui/primitives";
import { CodeEditor } from "@/components/ui/code-editor";
import { Tabs, TabList, Tab, TabPanel } from "@/components/ui/tabs";
import type { ApiRequestResultData } from "@/services/types";

function isApiRequestResultData(data: unknown): data is ApiRequestResultData {
  return typeof data === "object" && data !== null && "status" in data && "assertionResults" in data;
}

function byteSize(value: unknown): number {
  try {
    return new TextEncoder().encode(typeof value === "string" ? value : JSON.stringify(value)).length;
  } catch {
    return 0;
  }
}

export function ResponseViewer({ runId }: { runId: string | undefined }) {
  const { run, error } = useLiveRunProgress(runId);
  const [tab, setTab] = useState("pretty");

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
        <Spinner /> <span className="text-body-sm text-ink-dim">Waiting for response…</span>
      </Card>
    );
  }

  const step = run.results[0];
  const isTerminal = run.status === "Passed" || run.status === "Failed" || run.status === "Cancelled";

  if (!step) {
    return (
      <Card className="flex items-center gap-2 p-4">
        <Spinner /> <span className="text-body-sm text-ink-dim">Waiting for response…</span>
      </Card>
    );
  }

  const data = isApiRequestResultData(step.data) ? step.data : undefined;

  if (!data) {
    // Network/transport error or a non-api-module response shape — fall
    // back to the raw step message/error rather than crashing on the
    // missing structured Data.
    return (
      <Card className="flex flex-col gap-2 p-4">
        <div className="flex items-center justify-between">
          <h2 className="text-body-sm font-medium text-ink">Response</h2>
          <StatusPill status={step.status} />
        </div>
        <p className={`text-body-sm ${step.error ? "text-danger" : "text-ink-dim"}`}>{step.error ?? step.message ?? "No response data."}</p>
      </Card>
    );
  }

  const passCount = data.assertionResults.filter((a) => a.passed).length;

  return (
    <Card className="flex flex-col gap-3 p-4">
      <div className="flex items-center gap-3">
        <StatusPill status={isTerminal ? run.status : "Running"} />
        <span className="font-mono text-body-sm text-ink">{data.status}</span>
        <span className="text-body-sm text-ink-dim">{data.durationMs}ms</span>
        <span className="text-body-sm text-ink-dim">{byteSize(data.body)} B</span>
        {data.assertionResults.length > 0 && (
          <span className="text-body-sm text-ink-dim">
            {passCount}/{data.assertionResults.length} assertions passed
          </span>
        )}
      </div>

      <Tabs value={tab} onChange={setTab}>
        <TabList>
          <Tab value="pretty">Pretty</Tab>
          <Tab value="raw">Raw</Tab>
          <Tab value="headers">Headers</Tab>
          <Tab value="assertions">Assertions</Tab>
        </TabList>

        <TabPanel value="pretty" className="pt-3">
          <CodeEditor value={JSON.stringify(data.body, null, 2)} language="json" height={280} readOnly />
        </TabPanel>

        <TabPanel value="raw" className="pt-3">
          <pre className="max-h-[280px] overflow-auto rounded-md border border-border bg-surface p-3 font-mono text-body-sm text-ink">
            {typeof data.body === "string" ? data.body : JSON.stringify(data.body)}
          </pre>
        </TabPanel>

        <TabPanel value="headers" className="pt-3">
          <table className="w-full text-body-sm">
            <tbody>
              {Object.entries(data.headers).map(([key, value]) => (
                <tr key={key} className="border-b border-border last:border-0">
                  <td className="py-1.5 pr-3 font-mono text-ink-dim">{key}</td>
                  <td className="py-1.5 font-mono text-ink">{value}</td>
                </tr>
              ))}
              {Object.keys(data.headers).length === 0 && (
                <tr>
                  <td className="py-1.5 text-ink-dim">No headers.</td>
                </tr>
              )}
            </tbody>
          </table>
        </TabPanel>

        <TabPanel value="assertions" className="flex flex-col gap-2 pt-3">
          {data.assertionResults.map((a, i) => (
            <div key={i} className="flex items-start gap-2 rounded-md border border-border p-2.5">
              {a.passed ? (
                <CheckCircle2 size={16} className="mt-0.5 flex-shrink-0 text-success" />
              ) : (
                <XCircle size={16} className="mt-0.5 flex-shrink-0 text-danger" />
              )}
              <div>
                <div className="text-body-sm text-ink">{a.description}</div>
                {a.error && <div className="text-body-sm text-danger">{a.error}</div>}
              </div>
            </div>
          ))}
          {data.assertionResults.length === 0 && <div className="text-body-sm text-ink-dim">No assertions configured.</div>}
        </TabPanel>
      </Tabs>
    </Card>
  );
}
