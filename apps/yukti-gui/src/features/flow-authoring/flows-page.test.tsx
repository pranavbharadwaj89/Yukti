import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createRootRoute, createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { FlowsPage } from "./flows-page";
import { flowsApi } from "@/services/api-client";
import type { FlowSummary } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  flowsApi: { list: vi.fn(), create: vi.fn() },
  ApiError: class ApiError extends Error {},
}));

const flows: FlowSummary[] = [{ flowId: { value: "flow-1" }, name: "Onboarding", status: 1, version: 1, projectId: null }];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  // Minimal single-route harness — FlowsPage calls useNavigate() but this
  // test never clicks a link that triggers it, so no sibling route needs
  // to actually exist for the render itself to succeed.
  const rootRoute = createRootRoute({ component: () => <FlowsPage /> });
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

describe("FlowsPage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    vi.mocked(flowsApi.list).mockResolvedValue(flows);
    const { container } = renderPage();
    await screen.findByText("Onboarding");
    expect(await wcagViolations(container)).toEqual([]);
  });
});
