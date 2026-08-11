import { useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { flowsApi, ApiError } from "@/services/api-client";
import type { FlowRunResponse } from "@/services/types";
import { Button, Input, Spinner } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { CodeEditor } from "@/components/ui/code-editor";
import { Tabs, TabList, Tab, TabPanel } from "@/components/ui/tabs";
import { useToastStore } from "@/store/toast-store";
import { KeyValueEditor, newKeyValueRow, type KeyValueRow } from "./key-value-editor";
import { AssertionsEditor, buildAssertPayload, newAssertionDraft, type AssertionDraft } from "./assertions-editor";
import { ResponseViewer } from "./response-viewer";

const METHODS = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"].map((m) => ({ value: m, label: m }));

function buildRequestParams(
  method: string,
  url: string,
  headerRows: KeyValueRow[],
  queryRows: KeyValueRow[],
  bodyText: string,
  assertionDrafts: AssertionDraft[],
): Record<string, unknown> {
  const params: Record<string, unknown> = { url, method };

  const headers = Object.fromEntries(headerRows.filter((r) => r.enabled && r.key.trim() !== "").map((r) => [r.key, r.value]));
  if (Object.keys(headers).length > 0) params.headers = headers;

  const queryParams = Object.fromEntries(queryRows.filter((r) => r.enabled && r.key.trim() !== "").map((r) => [r.key, r.value]));
  if (Object.keys(queryParams).length > 0) params.queryParams = queryParams;

  if (bodyText.trim() !== "") {
    try {
      params.body = JSON.parse(bodyText);
    } catch {
      params.body = bodyText;
    }
  }

  const assert = buildAssertPayload(assertionDrafts);
  if (assert.length > 0) params.assert = assert;

  return params;
}

// Real Request Designer for the API tab — supersedes the generic
// ModuleTestForm there. Still executes through the same ad-hoc
// create-flow -> add-step -> publish -> trigger-run sequence
// ModuleTestForm uses (Yukti has no separate "run one action" API), just
// with params hand-built from dedicated Headers/Query Params/Body/
// Assertions editors instead of one generic JSON textarea per param.
export function RequestDesigner() {
  const pushToast = useToastStore((s) => s.push);

  const [method, setMethod] = useState("GET");
  const [url, setUrl] = useState("");
  const [headerRows, setHeaderRows] = useState<KeyValueRow[]>([newKeyValueRow()]);
  const [queryRows, setQueryRows] = useState<KeyValueRow[]>([newKeyValueRow()]);
  const [bodyText, setBodyText] = useState("");
  const [assertionDrafts, setAssertionDrafts] = useState<AssertionDraft[]>([newAssertionDraft()]);
  const [requestTab, setRequestTab] = useState("headers");
  const [runId, setRunId] = useState<string | undefined>(undefined);

  const runMutation = useMutation({
    mutationFn: async () => {
      const params = buildRequestParams(method, url, headerRows, queryRows, bodyText, assertionDrafts);
      const { flowId } = await flowsApi.create(
        `API Request — ${new Date().toLocaleString()}`,
        "Ad-hoc API request from the Request Designer.",
      );
      await flowsApi.addStep(flowId, { name: "request", module: "api", action: "request", params });
      const publishResult = await flowsApi.publish(flowId);
      if (!publishResult.succeeded) throw new Error(publishResult.errors.join("; "));
      return flowsApi.triggerRun(flowId);
    },
    onSuccess: (run: FlowRunResponse) => setRunId(run.id),
    onError: (err) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : err instanceof Error ? err.message : "Failed to send request.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      }),
  });

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-h1 text-ink">API Testing</h1>

      <div className="flex items-center gap-2">
        <div className="w-32">
          <Select value={method} options={METHODS} onChange={setMethod} />
        </div>
        <Input
          className="flex-1"
          placeholder="https://api.example.com/resource"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
        />
        <Button onClick={() => runMutation.mutate()} disabled={!url.trim() || runMutation.isPending}>
          {runMutation.isPending ? <Spinner /> : "Send"}
        </Button>
      </div>

      <Tabs value={requestTab} onChange={setRequestTab}>
        <TabList>
          <Tab value="headers">Headers</Tab>
          <Tab value="query">Query Params</Tab>
          <Tab value="body">Body</Tab>
          <Tab value="assertions">Assertions</Tab>
        </TabList>

        <TabPanel value="headers" className="pt-3">
          <KeyValueEditor rows={headerRows} onChange={setHeaderRows} keyPlaceholder="Header" valuePlaceholder="Value" />
        </TabPanel>

        <TabPanel value="query" className="pt-3">
          <KeyValueEditor rows={queryRows} onChange={setQueryRows} keyPlaceholder="Param" valuePlaceholder="Value" />
        </TabPanel>

        <TabPanel value="body" className="pt-3">
          <CodeEditor value={bodyText} onChange={setBodyText} language="json" height={200} />
        </TabPanel>

        <TabPanel value="assertions" className="pt-3">
          <AssertionsEditor drafts={assertionDrafts} onChange={setAssertionDrafts} />
        </TabPanel>
      </Tabs>

      <ResponseViewer runId={runId} />
    </div>
  );
}
