import { useEffect, useState } from "react";
import { useMutation } from "@tanstack/react-query";
import { apiCollectionsApi, flowsApi, ApiError } from "@/services/api-client";
import type { ApiRequestResponse, FlowRunResponse } from "@/services/types";
import { Button, Input, Spinner } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { CodeEditor } from "@/components/ui/code-editor";
import { Tabs, TabList, Tab, TabPanel } from "@/components/ui/tabs";
import { useToastStore } from "@/store/toast-store";
import { KeyValueEditor, newKeyValueRow, type KeyValueRow } from "./key-value-editor";
import {
  AssertionsEditor,
  buildAssertPayload,
  newAssertionDraft,
  type AssertionDraft,
  type AssertionType,
} from "./assertions-editor";
import { ResponseViewer } from "./response-viewer";

const METHODS = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS", "HEAD"].map((m) => ({ value: m, label: m }));

function objectToRows(obj: Record<string, unknown> | undefined | null): KeyValueRow[] {
  const entries = obj ? Object.entries(obj) : [];
  const rows = entries.map(([key, value]) => ({ ...newKeyValueRow(), key, value: value == null ? "" : String(value) }));
  return rows.length > 0 ? rows : [newKeyValueRow()];
}

function bodyToText(body: unknown): string {
  if (body === undefined || body === null) return "";
  return typeof body === "string" ? body : JSON.stringify(body, null, 2);
}

function assertionsToDrafts(assertions: unknown): AssertionDraft[] {
  if (!Array.isArray(assertions) || assertions.length === 0) return [newAssertionDraft()];
  return assertions.map((entry) => {
    const raw = (entry ?? {}) as Record<string, unknown>;
    const type = (typeof raw.type === "string" ? raw.type : "status") as AssertionType;
    const draft = newAssertionDraft(type);
    draft.expectedStatus = raw.expectedStatus !== undefined ? String(raw.expectedStatus) : "";
    draft.path = typeof raw.path === "string" ? raw.path : "";
    draft.equals = raw.equals !== undefined ? (typeof raw.equals === "string" ? raw.equals : JSON.stringify(raw.equals)) : "";
    draft.contains = raw.contains !== undefined ? (typeof raw.contains === "string" ? raw.contains : JSON.stringify(raw.contains)) : "";
    draft.header = typeof raw.header === "string" ? raw.header : "";
    draft.cookie = typeof raw.cookie === "string" ? raw.cookie : "";
    draft.schema = raw.schema !== undefined ? JSON.stringify(raw.schema, null, 2) : "";
    return draft;
  });
}

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
//
// Optionally wired to a saved ApiRequest (Explorer selection): loadedRequest
// prefills every field, and Save persists back into collectionId via the
// same apiCollectionsApi CRUD Explorer itself uses — Save never goes through
// FlowEngine, only Send does.
export function RequestDesigner({
  collectionId,
  loadedRequest,
  onSaved,
}: {
  collectionId?: string | null;
  loadedRequest?: ApiRequestResponse | null;
  onSaved?: () => void;
} = {}) {
  const pushToast = useToastStore((s) => s.push);

  const [name, setName] = useState(loadedRequest?.name ?? "");
  const [method, setMethod] = useState(loadedRequest?.method ?? "GET");
  const [url, setUrl] = useState(loadedRequest?.url ?? "");
  const [headerRows, setHeaderRows] = useState<KeyValueRow[]>(objectToRows(loadedRequest?.headers));
  const [queryRows, setQueryRows] = useState<KeyValueRow[]>(objectToRows(loadedRequest?.queryParams));
  const [bodyText, setBodyText] = useState(bodyToText(loadedRequest?.body));
  const [assertionDrafts, setAssertionDrafts] = useState<AssertionDraft[]>(assertionsToDrafts(loadedRequest?.assertions));
  const [currentRequestId, setCurrentRequestId] = useState<string | undefined>(loadedRequest?.id);
  const [requestTab, setRequestTab] = useState("headers");
  const [runId, setRunId] = useState<string | undefined>(undefined);

  // Re-hydrate every field when a different saved request is selected in
  // Explorer (or "New Request" clears loadedRequest back to undefined).
  useEffect(() => {
    setName(loadedRequest?.name ?? "");
    setMethod(loadedRequest?.method ?? "GET");
    setUrl(loadedRequest?.url ?? "");
    setHeaderRows(objectToRows(loadedRequest?.headers));
    setQueryRows(objectToRows(loadedRequest?.queryParams));
    setBodyText(bodyToText(loadedRequest?.body));
    setAssertionDrafts(assertionsToDrafts(loadedRequest?.assertions));
    setCurrentRequestId(loadedRequest?.id);
    setRunId(undefined);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loadedRequest?.id]);

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

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!collectionId) throw new Error("Select a collection in Explorer first.");
      const headers = Object.fromEntries(headerRows.filter((r) => r.enabled && r.key.trim() !== "").map((r) => [r.key, r.value]));
      const queryParams = Object.fromEntries(queryRows.filter((r) => r.enabled && r.key.trim() !== "").map((r) => [r.key, r.value]));
      let body: unknown;
      if (bodyText.trim() !== "") {
        try {
          body = JSON.parse(bodyText);
        } catch {
          body = bodyText;
        }
      }
      const assertions = buildAssertPayload(assertionDrafts);
      const req = { name: name.trim() || "Untitled request", method, url, headers, queryParams, body, assertions };

      if (currentRequestId) {
        await apiCollectionsApi.updateRequest(collectionId, currentRequestId, req);
        return currentRequestId;
      }
      const { requestId } = await apiCollectionsApi.addRequest(collectionId, req);
      return requestId;
    },
    onSuccess: (requestId) => {
      setCurrentRequestId(requestId);
      pushToast({ kind: "success", message: "Request saved." });
      onSaved?.();
    },
    onError: (err) =>
      pushToast({
        kind: "error",
        message: err instanceof ApiError ? err.message : err instanceof Error ? err.message : "Failed to save request.",
        correlationId: err instanceof ApiError ? err.correlationId : undefined,
      }),
  });

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <h1 className="text-h1 text-ink">API Testing</h1>
        {collectionId && (
          <div className="flex items-center gap-2">
            <Input className="w-56" placeholder="Request name" value={name} onChange={(e) => setName(e.target.value)} />
            <Button variant="secondary" onClick={() => saveMutation.mutate()} disabled={!name.trim() || saveMutation.isPending}>
              {saveMutation.isPending ? <Spinner /> : currentRequestId ? "Save" : "Save as new"}
            </Button>
          </div>
        )}
      </div>

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
