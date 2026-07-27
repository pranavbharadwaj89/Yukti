// Typed DTOs mirroring Yukti.Api's actual response shapes (Yukti.Api/Dtos.cs
// and the endpoint definitions in Program.cs) — hand-written against the
// live contract verified this session, not generated, since the backend
// has no OpenAPI document exposed yet.

export interface ProblemDetails {
  title: string;
  status: number;
  detail: string;
  correlationId: string;
}

export interface TokenResponse {
  accessToken: string;
  expiresAt: string;
  refreshToken: string;
}

// FR-DOM-13's strongly-typed IDs serialize as {value: string} from the
// FR-CQRS-01 read model query (EfFlowSummaryQuery), unlike FlowResponse
// below which flattens to a plain string `id` — a real inconsistency
// between these two endpoints, confirmed live, not assumed. Adapting to
// each shape here rather than normalizing in the backend this pass.
export interface FlowSummary {
  flowId: { value: string };
  name: string;
  status: number; // raw FlowStatus enum ordinal: 0=Draft, 1=Published, 2=Archived
  version: number;
}

export function flowStatusLabel(status: number): string {
  return ["Draft", "Published", "Archived"][status] ?? "Unknown";
}

export interface ActionSchemaParam {
  name: string;
  type: string;
  required: boolean;
  description?: string;
  defaultValue?: unknown;
}

export interface ActionSchema {
  actionName: string;
  description: string;
  parameters: ActionSchemaParam[];
}

export interface ModuleResponse {
  kind: string;
  displayName: string;
  trust: string;
  contractVersion: string;
  actions: ActionSchema[];
}

export interface FlowStepResponse {
  id: string;
  name: string;
  module: string;
  action: string;
  order: number;
  params: Record<string, unknown>;
  saveAs?: string | null;
  when?: string | null;
}

export interface FlowResponse {
  id: string;
  familyId: string;
  version: number;
  name: string;
  description?: string | null;
  status: string;
  steps: FlowStepResponse[];
}

export interface FlowPublishResponse {
  succeeded: boolean;
  errors: string[];
}

export interface RetryAttemptResponse {
  id: string;
  attemptNumber: number;
  status: string;
  durationMs: number;
  error?: string | null;
  attemptedAt: string;
}

export interface StepResultResponse {
  id: string;
  stepName: string;
  module: string;
  action: string;
  status: string;
  durationMs: number;
  message?: string | null;
  error?: string | null;
  data?: unknown;
  isFlaky: boolean;
  retryHistory: RetryAttemptResponse[];
}

export interface FlowRunResponse {
  id: string;
  flowId: string;
  status: string;
  trigger: string;
  startedAt: string;
  finishedAt?: string | null;
  results: StepResultResponse[];
}

export interface TrendAggregateResponse {
  tenantId: string;
  totalRunsLast24h: number;
  passRateLast24h: number;
  flakeRateLast24h: number;
  lastUpdatedAt: string;
}
