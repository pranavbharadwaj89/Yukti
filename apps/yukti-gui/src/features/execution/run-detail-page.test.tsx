import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createRootRoute, createRoute, createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { RunDetailPage } from "./run-detail-page";
import * as hooks from "@/hooks";
import { screenshotsApi } from "@/services/api-client";
import type { FlowRunResponse, StepResultResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  runsApi: { cancel: vi.fn() },
  screenshotsApi: { fetchObjectUrl: vi.fn() },
  ApiError: class ApiError extends Error {},
}));

vi.mock("@/hooks", async () => {
  const actual = await vi.importActual<typeof hooks>("@/hooks");
  return { ...actual, useLiveRunProgress: vi.fn() };
});

beforeEach(() => {
  vi.clearAllMocks();
});

const run: FlowRunResponse = {
  id: "run-12345678",
  flowId: "flow-1",
  status: "Running",
  trigger: "Api",
  startedAt: new Date().toISOString(),
  results: [],
};

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const rootRoute = createRootRoute({ component: () => <RunDetailPage /> });
  const router = createRouter({ routeTree: rootRoute, history: createMemoryHistory({ initialEntries: ["/"] }) });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

// D4-T4: proves the UI actually wires useLiveRunProgress's connectionState
// to something visible — the hook's own logic is proven separately in
// hooks/index.test.ts, this is "does the page show it".
describe("RunDetailPage — connection indicator (D4-T4)", () => {
  it("shows an amber Reconnecting indicator, distinct from the run's own status, while reconnecting", async () => {
    vi.mocked(hooks.useLiveRunProgress).mockReturnValue({ run, error: null, connectionState: "reconnecting" });
    renderPage();

    expect(await screen.findByText("Reconnecting…")).toBeInTheDocument();
    // The run itself is still shown as Running, never as failed/disconnected.
    expect(screen.getByText("Running")).toBeInTheDocument();
  });

  it("shows the polling fallback message, distinct from reconnecting, when SignalR has given up", async () => {
    vi.mocked(hooks.useLiveRunProgress).mockReturnValue({ run, error: null, connectionState: "polling" });
    renderPage();

    expect(await screen.findByText("Live updates unavailable — refreshing periodically")).toBeInTheDocument();
    expect(screen.queryByText("Reconnecting…")).not.toBeInTheDocument();
  });

  it("shows neither indicator while connected", async () => {
    vi.mocked(hooks.useLiveRunProgress).mockReturnValue({ run, error: null, connectionState: "connected" });
    renderPage();

    await screen.findByText("Running");
    expect(screen.queryByText("Reconnecting…")).not.toBeInTheDocument();
    expect(screen.queryByText(/Live updates unavailable/)).not.toBeInTheDocument();
  });
});

// D6-T5: unlike renderPage() above, this binds a real "/runs/$runId" param — the page reads runId
// via useParams, and the screenshot fetch call needs the real value to assert against.
function renderPageWithRunId(runId = run.id) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const rootRoute = createRootRoute();
  const runRoute = createRoute({ getParentRoute: () => rootRoute, path: "/runs/$runId", component: RunDetailPage });
  const router = createRouter({
    routeTree: rootRoute.addChildren([runRoute]),
    history: createMemoryHistory({ initialEntries: [`/runs/${runId}`] }),
  });
  return render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

function stepResult(overrides: Partial<StepResultResponse> = {}): StepResultResponse {
  return {
    id: "step-1",
    stepName: "Capture",
    module: "web",
    action: "screenshot",
    status: "Passed",
    durationMs: 12,
    isFlaky: false,
    retryHistory: [],
    ...overrides,
  };
}

// D6-T5: screenshots. The convention is module-agnostic (any step Data carrying a string "path"
// field), proven here against a generic step rather than one specifically named "web.screenshot"
// or "ui.screenshot" — the page itself never branches on module kind.
describe("RunDetailPage — screenshots (D6-T5)", () => {
  it("fetches and renders a screenshot for a step whose data carries a path", async () => {
    vi.mocked(screenshotsApi.fetchObjectUrl).mockResolvedValue("blob:fake-object-url");
    vi.mocked(hooks.useLiveRunProgress).mockReturnValue({
      run: { ...run, results: [stepResult({ data: { path: "login-page.png" } })] },
      error: null,
      connectionState: "connected",
    });

    renderPageWithRunId();

    const img = await screen.findByAltText("Screenshot: login-page.png");
    expect(img).toHaveAttribute("src", "blob:fake-object-url");
    expect(screenshotsApi.fetchObjectUrl).toHaveBeenCalledWith(run.id, "login-page.png");
  });

  it("renders no image for a step with no screenshot path in its data", async () => {
    vi.mocked(hooks.useLiveRunProgress).mockReturnValue({
      run: { ...run, results: [stepResult({ data: { text: "some other payload" } })] },
      error: null,
      connectionState: "connected",
    });

    renderPageWithRunId();

    await screen.findAllByText("Capture"); // the step name legitimately renders twice (Steps card + console) — waits for render without demanding a single match
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screenshotsApi.fetchObjectUrl).not.toHaveBeenCalled();
  });
});
