import { useAuthStore, getStoredRefreshToken } from "@/store/auth-store";
import type {
  ActionSchema,
  FlowPublishResponse,
  FlowResponse,
  FlowRunResponse,
  FlowSummary,
  ModuleResponse,
  ProblemDetails,
  TokenResponse,
  TrendAggregateResponse,
} from "@/services/types";

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
      body: { email, password, displayName },
      auth: false,
    }),
  login: (email: string, password: string) =>
    request<TokenResponse>("/api/v1/auth/login", { method: "POST", body: { email, password }, auth: false }),
};

export const flowsApi = {
  list: () => request<FlowSummary[]>("/api/v1/flows"),
  get: (flowId: string) => request<FlowResponse>(`/api/v1/flows/${flowId}`),
  create: (name: string, description: string | undefined) =>
    request<{ flowId: string }>("/api/v1/flows", { method: "POST", body: { name, description } }),
  addStep: (
    flowId: string,
    step: { name: string; module: string; action: string; params: Record<string, unknown>; saveAs?: string; when?: string },
  ) => request<void>(`/api/v1/flows/${flowId}/steps`, { method: "POST", body: step }),
  publish: (flowId: string) => request<FlowPublishResponse>(`/api/v1/flows/${flowId}/publish`, { method: "POST" }),
  triggerRun: (flowId: string, variableOverrides?: Record<string, unknown>, idempotencyKey?: string) =>
    request<FlowRunResponse>(`/api/v1/flows/${flowId}/runs`, {
      method: "POST",
      body: variableOverrides ? { variableOverrides } : undefined,
      headers: idempotencyKey ? { "Idempotency-Key": idempotencyKey } : {},
    }),
};

export const runsApi = {
  get: (runId: string) => request<FlowRunResponse>(`/api/v1/runs/${runId}`),
  cancel: (runId: string) => request<void>(`/api/v1/runs/${runId}/cancel`, { method: "POST" }),
};

export const modulesApi = {
  list: () => request<ModuleResponse[]>("/api/v1/modules"),
};

export const trendsApi = {
  get: () => request<TrendAggregateResponse | undefined>("/api/v1/trends"),
};

export function getActionParams(modules: ModuleResponse[] | undefined, moduleKind: string, action: string): ActionSchema | undefined {
  return modules?.find((m) => m.kind === moduleKind)?.actions.find((a) => a.actionName === action);
}
