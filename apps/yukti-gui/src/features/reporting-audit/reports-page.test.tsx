import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ReportsPage } from "./reports-page";
import { auditApi, flowReportsApi, trendsApi } from "@/services/api-client";
import type { AuditEntryResponse, FlowReportSummaryResponse, FlowRunReportResponse, TrendAggregateResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  trendsApi: { get: vi.fn() },
  flowReportsApi: { list: vi.fn(), runsByFlow: vi.fn() },
  auditApi: { list: vi.fn(), getById: vi.fn() },
}));

const trend: TrendAggregateResponse = {
  tenantId: "t1",
  totalRunsLast24h: 10,
  passRateLast24h: 0.8,
  flakeRateLast24h: 0.1,
  lastUpdatedAt: "2026-01-01T00:00:00Z",
};

const flowReport: FlowReportSummaryResponse = {
  flowId: "f-1",
  flowName: "Checkout smoke test",
  totalRuns: 5,
  passedRuns: 4,
  failedRuns: 1,
  lastRunAt: "2026-01-02T00:00:00Z",
  lastRunStatus: "Passed",
};

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ReportsPage />
    </QueryClientProvider>,
  );
}

describe("ReportsPage", () => {
  it("renders tenant-wide trend cards", async () => {
    vi.mocked(trendsApi.get).mockResolvedValue(trend);
    vi.mocked(flowReportsApi.list).mockResolvedValue([]);
    renderPage();
    expect(await screen.findByText("10")).toBeInTheDocument();
    expect(screen.getByText("80.0%")).toBeInTheDocument();
  });

  it("renders per-flow rows from the flow-reports endpoint", async () => {
    vi.mocked(trendsApi.get).mockResolvedValue(trend);
    vi.mocked(flowReportsApi.list).mockResolvedValue([flowReport]);
    renderPage();
    expect(await screen.findByText("Checkout smoke test")).toBeInTheDocument();
    expect(screen.getByText("80%")).toBeInTheDocument();
  });

  it("expanding a flow row shows run history and ties in matching audit entries", async () => {
    vi.mocked(trendsApi.get).mockResolvedValue(trend);
    vi.mocked(flowReportsApi.list).mockResolvedValue([flowReport]);

    const run: FlowRunReportResponse = {
      flowRunId: "r-1",
      finalStatus: "Passed",
      passedCount: 3,
      failedCount: 0,
      skippedCount: 0,
      totalDurationMs: 1200,
      occurredAt: "2026-01-02T00:00:00Z",
      projectedAt: "2026-01-02T00:00:01Z",
    };
    vi.mocked(flowReportsApi.runsByFlow).mockResolvedValue([run]);

    const matchingEntry: AuditEntryResponse = {
      id: "a-1",
      commandType: "TriggerFlowRunCommand",
      tenantId: "t1",
      succeeded: true,
      failureReason: null,
      occurredAt: "2026-01-02T00:00:00Z",
    };
    const otherFlowEntry: AuditEntryResponse = {
      id: "a-2",
      commandType: "TriggerFlowRunCommand",
      tenantId: "t1",
      succeeded: true,
      failureReason: null,
      occurredAt: "2026-01-02T01:00:00Z",
    };
    vi.mocked(auditApi.list).mockResolvedValue([matchingEntry, otherFlowEntry]);
    vi.mocked(auditApi.getById).mockImplementation((id: string) =>
      Promise.resolve({
        ...(id === "a-1" ? matchingEntry : otherFlowEntry),
        metadata: { FlowId: { value: id === "a-1" ? "f-1" : "f-2" } },
      }),
    );

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Checkout smoke test");

    await user.click(screen.getByRole("button", { name: "Toggle row" }));

    expect(await screen.findByText("1200ms")).toBeInTheDocument();
    expect(await screen.findByText("TriggerFlowRunCommand")).toBeInTheDocument();
    expect(auditApi.getById).toHaveBeenCalledWith("a-1");
    expect(auditApi.getById).toHaveBeenCalledWith("a-2");
  });
});
