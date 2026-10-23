import { useAuthStore, getStoredRefreshToken } from "@/store/auth-store";
import type {
  ActionSchema,
  ApiCollectionResponse,
  FlowPublishResponse,
  FlowResponse,
  FlowRunResponse,
  FlowSummary,
  GenerateFlowResponse,
  ModuleResponse,
  ProblemDetails,
  AuditEntryResponse,
  AuditEntryDetailResponse,
  FlowReportSummaryResponse,
  FlowRunReportResponse,
  ProjectResponse,
  TestEnvironmentResponse,
  TokenResponse,
  TrendAggregateResponse,
  TriggerResponse,
} from "@/services/types";
// D4-T1: request-body shapes come from the generated OpenAPI schema
// (src/api/generated/schema.ts), not hand-written inline types. Response
// shapes still come from services/types.ts — Yukti.Api's minimal-API
// endpoints don't yet declare `.Produces<T>()`, so Swashbuckle has nothing
// to infer a response schema from (every endpoint shows bare `200: OK` in
// swagger.json). See PLAN.md DISCOVERED for that follow-up.
import type { components } from "@/api/generated/schema";
type Req<K extends keyof components["schemas"]> = components["schemas"][K];

// FR-STRUCT-05: the one typed API client every feature calls through —
// nothing else in this app calls fetch() directly. FR-API-03/FR-UX-05:
// every non-2xx response is RFC 7807 (Yukti.Api's ProblemResults.cs) —
// this throws ApiError carrying the correlationId so the UI can surface it.
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "http://localhost:5000";

export class ApiError extends Error {
  status: number;
  title: string;
  correlationId: string | undefined;

  constructor(status: number, title: string, correlationId: string | undefined, detail: string) {
    super(detail);
    this.status = status;
    this.title = title;
    this.correlationId = correlationId;
  }
}

let refreshInFlight: Promise<void> | null = null;

// Exported for main.tsx's boot-time session restore: the access token is
// deliberately in-memory only (FR-SEC-01) and lost on every reload — the
// sessionStorage refresh token exists precisely so a reload doesn't force
// a fresh login. Silently resolves to "no session" if there's nothing to
// restore, rather than throwing into the app's error boundary over an
// entirely expected first-visit case.
export async function restoreSession(): Promise<void> {
  try {
    await refreshSession();
  } catch {
    /* no valid refresh token — starting logged out is correct, not an error */
  }
}

async function refreshSession(): Promise<void> {
  const refreshToken = getStoredRefreshToken();
  if (!refreshToken) throw new ApiError(401, "Unauthorized", undefined, "No refresh token available.");
  const res = await fetch(`${BASE_URL}/api/v1/auth/refresh`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ refreshToken }),
  });
  if (!res.ok) {
    useAuthStore.getState().clearSession();
    throw new ApiError(401, "Unauthorized", undefined, "Session expired.");
  }
  const tokens = (await res.json()) as TokenResponse;
  useAuthStore.getState().setSession(tokens);
}

interface RequestOptions {
  method?: string;
  body?: unknown;
  headers?: Record<string, string>;
  auth?: boolean; // defaults true
}

async function request<T>(path: string, options: RequestOptions = {}): Promise<T> {
  const { method = "GET", body, headers = {}, auth = true } = options;
  const doFetch = async () => {
    const finalHeaders: Record<string, string> = { "Content-Type": "application/json", ...headers };
    if (auth) {
      const token = useAuthStore.getState().accessToken;
      if (token) finalHeaders.Authorization = `Bearer ${token}`;
    }
    return fetch(`${BASE_URL}${path}`, {
      method,
      headers: finalHeaders,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
  };

  let res = await doFetch();

  // FR-AUTH-02: silent refresh-and-retry exactly once on a 401 — never a
  // second automatic retry, to avoid looping against a truly dead session.
  if (res.status === 401 && auth) {
    if (!refreshInFlight) refreshInFlight = refreshSession().finally(() => (refreshInFlight = null));
    try {
      await refreshInFlight;
      res = await doFetch();
    } catch {
      throw new ApiError(401, "Unauthorized", undefined, "Session expired. Please sign in again.");
    }
  }

  if (!res.ok) {
    let problem: Partial<ProblemDetails> = {};
    try {
      problem = (await res.json()) as ProblemDetails;
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(
      res.status,
      problem.title ?? res.statusText,
      problem.correlationId,
      problem.detail ?? "The request failed.",
    );
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

export const authApi = {
  register: (email: string, password: string, displayName: string) =>
    request<{ userId: string }>("/api/v1/auth/register", {
      method: "POST",
      body: { email, password, displayName } satisfies Req<"RegisterRequest">,
      auth: false,
    }),
  login: (email: string, password: string) =>
    request<TokenResponse>("/api/v1/auth/login", {
      method: "POST",
      body: { email, password } satisfies Req<"LoginRequest">,
      auth: false,
    }),
};

export const flowsApi = {
  list: () => request<FlowSummary[]>("/api/v1/flows"),
  get: (flowId: string) => request<FlowResponse>(`/api/v1/flows/${flowId}`),
  create: (name: string, description: string | undefined, projectId?: string) =>
    request<{ flowId: string }>("/api/v1/flows", {
      method: "POST",
      body: { name, description, projectId } satisfies Req<"CreateFlowRequest">,
    }),
  addStep: (flowId: string, step: Req<"AddStepRequest"> & { name: string; module: string; action: string }) =>
    request<void>(`/api/v1/flows/${flowId}/steps`, { method: "POST", body: step }),
  publish: (flowId: string) => request<FlowPublishResponse>(`/api/v1/flows/${flowId}/publish`, { method: "POST" }),
  // D5-T7: the Sarvagna pipeline (Analyst -> Coder), never the legacy /ai/flows/suggest.
  generate: (description: string) =>
    request<GenerateFlowResponse>("/api/v1/flows/generate", {
      method: "POST",
      body: { description } satisfies Req<"GenerateFlowRequest">,
    }),
  triggerRun: (flowId: string, variableOverrides?: Record<string, unknown>, idempotencyKey?: string) =>
    request<FlowRunResponse>(`/api/v1/flows/${flowId}/runs`, {
      method: "POST",
      body: variableOverrides ? ({ variableOverrides } satisfies Req<"TriggerRunRequest">) : undefined,
      headers: idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {},
    }),
};

export const runsApi = {
  get: (runId: string) => request<FlowRunResponse>(`/api/v1/runs/${runId}`),
  cancel: (runId: string) => request<void>(`/api/v1/runs/${runId}/cancel`, { method: "POST" }),
};

// D6-T5: FR-STRUCT-05 still applies to binary responses — this is the one place that fetches a
// screenshot, so nothing else needs to touch fetch() or the auth token to show one. A plain
// <img src="..."> can't attach the Authorization header the endpoint requires, so this fetches the
// bytes itself and hands back an object URL for the caller to render and revoke.
export const screenshotsApi = {
  async fetchObjectUrl(runId: string, fileName: string): Promise<string> {
    const token = useAuthStore.getState().accessToken;
    const res = await fetch(`${BASE_URL}/api/v1/runs/${runId}/screenshots/${encodeURIComponent(fileName)}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (!res.ok) throw new ApiError(res.status, res.statusText, undefined, "Could not load the screenshot.");
    return URL.createObjectURL(await res.blob());
  },
};

// D5-T7: up-front AI on/off signal (D5-T2's switch), so the generation panel can be hidden with
// an explanation instead of being discovered broken on submit.
export const aiApi = {
  status: () => request<{ enabled: boolean }>("/api/v1/ai/status"),
};

export const modulesApi = {
  list: () => request<ModuleResponse[]>("/api/v1/modules"),
};

export const apiCollectionsApi = {
  list: () => request<ApiCollectionResponse[]>("/api/v1/api-collections"),
  create: (name: string, description: string | undefined, projectId?: string) =>
    request<{ collectionId: string }>("/api/v1/api-collections", {
      method: "POST",
      body: { name, description, projectId } satisfies Req<"CreateApiCollectionRequest">,
    }),
  rename: (collectionId: string, name: string, description: string | undefined) =>
    request<void>(`/api/v1/api-collections/${collectionId}`, {
      method: "PUT",
      body: { name, description } satisfies Req<"RenameApiCollectionRequest">,
    }),
  delete: (collectionId: string) => request<void>(`/api/v1/api-collections/${collectionId}`, { method: "DELETE" }),
  addRequest: (collectionId: string, req: Req<"AddApiRequestRequest"> & { name: string; method: string; url: string }) =>
    request<{ requestId: string }>(`/api/v1/api-collections/${collectionId}/requests`, { method: "POST", body: req }),
  updateRequest: (
    collectionId: string,
    requestId: string,
    req: Req<"UpdateApiRequestRequest"> & { name: string; method: string; url: string },
  ) => request<void>(`/api/v1/api-collections/${collectionId}/requests/${requestId}`, { method: "PUT", body: req }),
  deleteRequest: (collectionId: string, requestId: string) =>
    request<void>(`/api/v1/api-collections/${collectionId}/requests/${requestId}`, { method: "DELETE" }),
};

export const projectsApi = {
  list: () => request<ProjectResponse[]>("/api/v1/projects"),
  create: (name: string, description: string | undefined) =>
    request<{ projectId: string }>("/api/v1/projects", {
      method: "POST",
      body: { name, description } satisfies Req<"CreateProjectRequest">,
    }),
  rename: (projectId: string, name: string, description: string | undefined) =>
    request<void>(`/api/v1/projects/${projectId}`, {
      method: "PUT",
      body: { name, description } satisfies Req<"RenameProjectRequest">,
    }),
  delete: (projectId: string) => request<void>(`/api/v1/projects/${projectId}`, { method: "DELETE" }),
};

export const environmentsApi = {
  list: (projectId: string) => request<TestEnvironmentResponse[]>(`/api/v1/projects/${projectId}/environments`),
  create: (projectId: string, name: string, variables: Record<string, unknown>) =>
    request<{ environmentId: string }>(`/api/v1/projects/${projectId}/environments`, {
      method: "POST",
      body: { name, variables } satisfies Req<"CreateTestEnvironmentRequest">,
    }),
  update: (projectId: string, environmentId: string, name: string, variables: Record<string, unknown>) =>
    request<void>(`/api/v1/projects/${projectId}/environments/${environmentId}`, {
      method: "PUT",
      body: { name, variables } satisfies Req<"UpdateTestEnvironmentRequest">,
    }),
  delete: (projectId: string, environmentId: string) =>
    request<void>(`/api/v1/projects/${projectId}/environments/${environmentId}`, { method: "DELETE" }),
};

export const triggersApi = {
  list: () => request<TriggerResponse[]>("/api/v1/triggers"),
  create: (flowId: string, req: Req<"CreateTriggerRequest"> & { kind: "Cron" | "Webhook" | "FileWatch" }) =>
    request<{ triggerId: string }>(`/api/v1/flows/${flowId}/triggers`, { method: "POST", body: req }),
  enable: (triggerId: string) => request<void>(`/api/v1/triggers/${triggerId}/enable`, { method: "PUT" }),
  disable: (triggerId: string) => request<void>(`/api/v1/triggers/${triggerId}/disable`, { method: "PUT" }),
};

export const auditApi = {
  list: () => request<AuditEntryResponse[]>("/api/v1/audit-entries"),
  getById: (id: string) => request<AuditEntryDetailResponse>(`/api/v1/audit-entries/${id}`),
};

export const trendsApi = {
  get: () => request<TrendAggregateResponse | undefined>("/api/v1/trends"),
};

export const flowReportsApi = {
  list: () => request<FlowReportSummaryResponse[]>("/api/v1/flow-reports"),
  runsByFlow: (flowId: string) => request<FlowRunReportResponse[]>(`/api/v1/flow-reports/${flowId}/runs`),
};

export function getActionParams(modules: ModuleResponse[] | undefined, moduleKind: string, action: string): ActionSchema | undefined {
  return modules?.find((m) => m.kind === moduleKind)?.actions.find((a) => a.actionName === action);
}

export const apiStudioApi = apiCollectionsApi;

