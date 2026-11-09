import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createRootRoute, createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { FlowDetailPage } from "./flow-detail-page";
import { flowsApi, modulesApi, flowReportsApi } from "@/services/api-client";
import type { FlowResponse, ModuleResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  flowsApi: { get: vi.fn(), addStep: vi.fn(), publish: vi.fn() },
  modulesApi: { list: vi.fn() },
  flowReportsApi: { runsByFlow: vi.fn() },
  runsApi: { get: vi.fn() },
  // Pre-existing gap in this mock, found while writing a test that actually opens the dialog
  // with a real module/action selected (no prior test did): the real getActionParams returns a
  // single ActionSchema | undefined, not an array, so ParamFields' own schema.parameters.map
  // crashed on the array this mock used to return unconditionally. Mirrors the real
  // implementation instead of a canned value.
  getActionParams: vi.fn((modules: { kind: string; actions: { actionName: string }[] }[] | undefined, moduleKind: string, action: string) =>
    modules?.find((m) => m.kind === moduleKind)?.actions.find((a) => a.actionName === action)),
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
  {
    kind: "api",
    displayName: "API",
    trust: "BuiltIn",
    contractVersion: "1.0.0",
    actions: [{ actionName: "sendRequest", description: "Sends an HTTP request.", parameters: [] }],
  },
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
  vi.mocked(flowReportsApi.runsByFlow).mockResolvedValue([]);
});

describe("FlowDetailPage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    vi.mocked(flowsApi.get).mockResolvedValue(flow);
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    const { container } = renderPage();
    await screen.findByText("Checkout regression");
    expect(await wcagViolations(container)).toEqual([]);
  });

  it("adds a step with soft checked as a soft step (ADR-0010)", async () => {
    vi.mocked(flowsApi.get).mockResolvedValue(flow);
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    vi.mocked(flowsApi.addStep).mockResolvedValue(undefined);
    renderPage();
    await screen.findByText("Checkout regression");

    await userEvent.click(screen.getByTestId("add-step-trigger"));
    fireEvent.change(screen.getByTestId("step-name-input"), { target: { value: "Soft health check" } });

    await userEvent.click(screen.getByTestId("module-select"));
    await userEvent.click(screen.getByTestId("module-select-option-api"));
    await userEvent.click(screen.getByTestId("action-select"));
    await userEvent.click(screen.getByTestId("action-select-option-sendRequest"));

    // Checkbox renders its own visible label text (form-controls.tsx's own Checkbox drops
    // ariaLabel whenever a visible `label` is also given), so the accessible name to query by
    // is that text, not the ariaLabel prop passed alongside it.
    await userEvent.click(screen.getByText("Soft — a failure here won't stop the flow"));
    await userEvent.click(screen.getByTestId("add-step-submit"));

    // This test harness's root route never sets a $flowId param (useParams({ strict: false })
    // tolerates that, same as flows-page.test.tsx), so flowId is always undefined here --
    // asserting only the step payload, not the (deliberately unresolved) flow id.
    expect(flowsApi.addStep).toHaveBeenCalledWith(
      undefined,
      expect.objectContaining({ name: "Soft health check", soft: true }),
    );
  });

  it("adds a step with soft left unchecked as a non-soft step by default", async () => {
    vi.mocked(flowsApi.get).mockResolvedValue(flow);
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    vi.mocked(flowsApi.addStep).mockResolvedValue(undefined);
    renderPage();
    await screen.findByText("Checkout regression");

    await userEvent.click(screen.getByTestId("add-step-trigger"));
    fireEvent.change(screen.getByTestId("step-name-input"), { target: { value: "Ordinary health check" } });
    await userEvent.click(screen.getByTestId("module-select"));
    await userEvent.click(screen.getByTestId("module-select-option-api"));
    await userEvent.click(screen.getByTestId("action-select"));
    await userEvent.click(screen.getByTestId("action-select-option-sendRequest"));
    await userEvent.click(screen.getByTestId("add-step-submit"));

    expect(flowsApi.addStep).toHaveBeenCalledWith(
      undefined,
      expect.objectContaining({ name: "Ordinary health check", soft: false }),
    );
  });
});
