import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createRootRoute, createRouter, createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { RunDetailPage } from "./run-detail-page";
import * as hooks from "@/hooks";
import type { FlowRunResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  runsApi: { cancel: vi.fn() },
  ApiError: class ApiError extends Error {},
}));

vi.mock("@/hooks", async () => {
  const actual = await vi.importActual<typeof hooks>("@/hooks");
  return { ...actual, useLiveRunProgress: vi.fn() };
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
