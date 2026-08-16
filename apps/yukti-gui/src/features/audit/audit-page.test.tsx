import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AuditPage } from "./audit-page";
import { auditApi } from "@/services/api-client";
import type { AuditEntryResponse, AuditEntryDetailResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  auditApi: { list: vi.fn(), getById: vi.fn() },
}));

const entries: AuditEntryResponse[] = [
  { id: "1", commandType: "PublishFlowCommand", tenantId: "t1", succeeded: true, failureReason: null, occurredAt: "2026-01-01T00:00:00Z" },
  { id: "2", commandType: "RunFlowCommand", tenantId: "t1", succeeded: false, failureReason: "Module timeout", occurredAt: "2026-01-02T00:00:00Z" },
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <AuditPage />
    </QueryClientProvider>,
  );
}

describe("AuditPage", () => {
  it("renders every audit entry from the list endpoint", async () => {
    vi.mocked(auditApi.list).mockResolvedValue(entries);
    renderPage();
    expect(await screen.findByText("PublishFlowCommand")).toBeInTheDocument();
    expect(screen.getByText("RunFlowCommand")).toBeInTheDocument();
  });

  it("filters rows by the search box against command type and failure reason", async () => {
    vi.mocked(auditApi.list).mockResolvedValue(entries);
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("PublishFlowCommand");

    await user.type(screen.getByLabelText("Search audit entries"), "timeout");
    expect(screen.queryByText("PublishFlowCommand")).not.toBeInTheDocument();
    expect(screen.getByText("RunFlowCommand")).toBeInTheDocument();
  });

  it("filters rows by result via the result select", async () => {
    vi.mocked(auditApi.list).mockResolvedValue(entries);
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("PublishFlowCommand");

    await user.click(screen.getByLabelText("Filter by result"));
    await user.click(screen.getByRole("option", { name: "Failed" }));
    expect(screen.queryByText("PublishFlowCommand")).not.toBeInTheDocument();
    expect(screen.getByText("RunFlowCommand")).toBeInTheDocument();
  });

  it("fetches and renders metadata when a row is expanded", async () => {
    vi.mocked(auditApi.list).mockResolvedValue(entries);
    const detail: AuditEntryDetailResponse = { ...entries[0], metadata: { flowId: "f-123" } };
    vi.mocked(auditApi.getById).mockResolvedValue(detail);
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("PublishFlowCommand");

    const toggles = screen.getAllByRole("button", { name: "Toggle row" });
    await user.click(toggles[0]);

    expect(await screen.findByText("flowId")).toBeInTheDocument();
    expect(screen.getByText("f-123")).toBeInTheDocument();
    expect(auditApi.getById).toHaveBeenCalledWith("1");
  });
});
