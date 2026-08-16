import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SchedulerPage } from "./scheduler-page";
import { triggersApi, flowsApi } from "@/services/api-client";
import type { TriggerResponse, FlowSummary } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  triggersApi: { list: vi.fn(), create: vi.fn(), enable: vi.fn(), disable: vi.fn() },
  flowsApi: { list: vi.fn() },
  ApiError: class ApiError extends Error {},
}));

const flows: FlowSummary[] = [{ flowId: { value: "flow-1" }, name: "Onboarding", status: 1, version: 1, projectId: null }];

const webhookTrigger: TriggerResponse = {
  id: "trig-1",
  flowId: "flow-1",
  kind: "Webhook",
  isEnabled: true,
  lastFiredAt: null,
  cronExpression: null,
  webhookPath: "abc123deadbeef",
  watchPath: null,
};

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <SchedulerPage />
    </QueryClientProvider>,
  );
}

if (!navigator.clipboard) {
  Object.defineProperty(navigator, "clipboard", { value: { writeText: async () => {} }, configurable: true });
}
let writeText: ReturnType<typeof vi.spyOn>;

beforeEach(() => {
  vi.clearAllMocks();
  writeText = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue(undefined);
});

describe("SchedulerPage", () => {
  it("shows a copy button next to an existing webhook trigger's path", async () => {
    vi.mocked(triggersApi.list).mockResolvedValue([webhookTrigger]);
    vi.mocked(flowsApi.list).mockResolvedValue(flows);
    renderPage();

    expect(await screen.findByText("abc123deadbeef")).toBeInTheDocument();
    const copyButton = screen.getByRole("button", { name: "Copy webhook path" });

    fireEvent.click(copyButton);
    await vi.waitFor(() => expect(writeText).toHaveBeenCalledWith("abc123deadbeef"));
  });

  it("shows the webhook path with a copy affordance right after creating a Webhook trigger", async () => {
    vi.mocked(flowsApi.list).mockResolvedValue(flows);
    vi.mocked(triggersApi.list)
      .mockResolvedValueOnce([]) // initial page load
      .mockResolvedValueOnce([webhookTrigger]); // re-fetched after create to find the new trigger
    vi.mocked(triggersApi.create).mockResolvedValue({ triggerId: "trig-1" });

    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "New trigger" }));
    await user.click(screen.getByRole("button", { name: "Flow…" }));
    await user.click(screen.getByRole("option", { name: "Onboarding" }));

    // Kind defaults to Cron — switch to Webhook.
    await user.click(screen.getByRole("button", { name: "Cron" }));
    await user.click(screen.getByRole("option", { name: "Webhook" }));

    await user.click(screen.getByRole("button", { name: "Create" }));

    const dialog = await screen.findByRole("dialog", { name: "Webhook trigger created" });
    expect(within(dialog).getByText("abc123deadbeef")).toBeInTheDocument();
  });
});
