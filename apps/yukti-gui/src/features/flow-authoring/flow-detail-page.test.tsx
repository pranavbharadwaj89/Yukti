import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createRootRoute, createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { FlowDetailPage } from "./flow-detail-page";
import { flowsApi, modulesApi } from "@/services/api-client";
import type { FlowResponse, ModuleResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  flowsApi: { get: vi.fn(), addStep: vi.fn(), publish: vi.fn() },
  modulesApi: { list: vi.fn() },
  getActionParams: vi.fn(() => []),
  ApiError: class ApiError extends Error {},
}));

const flow: FlowResponse = {
  id: "flow-1",
  familyId: "family-1",
  version: 1,
  name: "Checkout regression",
  description: null,
  status: "Draft",
  steps: [
    { id: "step-1", name: "Hit health endpoint", module: "api", action: "sendRequest", order: 1, params: {}, saveAs: null, when: null },
  ],
};

const modules: ModuleResponse[] = [
  { kind: "api", displayName: "API", trust: "BuiltIn", contractVersion: "1.0.0", actions: [] },
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // Minimal single-route harness, same pattern as flows-page.test.tsx —
  // useParams({ strict: false }) tolerates the root route having no
  // $flowId segment; flowsApi.get is mocked regardless of the id it's
  // called with.
  const rootRoute = createRootRoute({ component: () => <FlowDetailPage /> });
  const router = createRouter({ routeTree: rootRoute, history: createMemoryHistory({ initialEntries: ["/"] }) });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});

describe("FlowDetailPage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    vi.mocked(flowsApi.get).mockResolvedValue(flow);
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    const { container } = renderPage();
    await screen.findByText("Checkout regression");
    expect(await wcagViolations(container)).toEqual([]);
  });
});
