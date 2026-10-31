import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestTemplatesPage } from "./request-templates-page";
import { requestTemplatesApi } from "@/services/api-client";

vi.mock("@/services/api-client", () => ({
  requestTemplatesApi: { list: vi.fn(), get: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), usages: vi.fn() },
  ApiError: class ApiError extends Error {},
}));

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <RequestTemplatesPage />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("RequestTemplatesPage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    vi.mocked(requestTemplatesApi.list).mockResolvedValue([
      { id: "tmpl-1", name: "Billing API", baseUrl: "https://billing.example.com", version: 2 },
    ]);
    const { container } = renderPage();
    await screen.findByText("Billing API");
    expect(await wcagViolations(container)).toEqual([]);
  });

  it("lists templates with their base URL and version", async () => {
    vi.mocked(requestTemplatesApi.list).mockResolvedValue([
      { id: "tmpl-1", name: "Billing API", baseUrl: "https://billing.example.com", version: 2 },
    ]);
    renderPage();
    await screen.findByText("Billing API");
    expect(screen.getByText("https://billing.example.com")).toBeInTheDocument();
    expect(screen.getByText("v2")).toBeInTheDocument();
  });

  it("shows which flows use a template once its row is expanded — the whole point of DISC-20", async () => {
    vi.mocked(requestTemplatesApi.list).mockResolvedValue([
      { id: "tmpl-1", name: "Billing API", baseUrl: null, version: 1 },
    ]);
    vi.mocked(requestTemplatesApi.usages).mockResolvedValue([
      { flowId: "flow-1", flowName: "Checkout smoke test", stepName: "Create invoice" },
    ]);
    renderPage();
    await screen.findByText("Billing API");

    expect(requestTemplatesApi.usages).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole("button", { name: "Toggle row" }));

    expect(await screen.findByText(/Checkout smoke test/)).toBeInTheDocument();
    expect(screen.getByText(/Create invoice/)).toBeInTheDocument();
  });

  it("says plainly when a template is unused", async () => {
    vi.mocked(requestTemplatesApi.list).mockResolvedValue([
      { id: "tmpl-1", name: "Billing API", baseUrl: null, version: 1 },
    ]);
    vi.mocked(requestTemplatesApi.usages).mockResolvedValue([]);
    renderPage();
    await screen.findByText("Billing API");
    await userEvent.click(screen.getByRole("button", { name: "Toggle row" }));
    expect(await screen.findByText("Not used by any flow yet.")).toBeInTheDocument();
  });

  it("creates a template with the fields entered in the dialog", async () => {
    vi.mocked(requestTemplatesApi.list).mockResolvedValue([]);
    vi.mocked(requestTemplatesApi.create).mockResolvedValue({ templateId: "tmpl-new" });
    renderPage();
    await screen.findByText("No request templates yet");

    await userEvent.click(screen.getByRole("button", { name: "New template" }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByPlaceholderText("Template name"), "Payments API");
    await userEvent.type(within(dialog).getByPlaceholderText("Base URL (optional)"), "https://pay.example.com");
    await userEvent.click(within(dialog).getByRole("button", { name: "Create" }));

    expect(requestTemplatesApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: "Payments API", baseUrl: "https://pay.example.com", auth: null }),
    );
  });

  it("deletes a template after confirmation", async () => {
    vi.mocked(requestTemplatesApi.list).mockResolvedValue([
      { id: "tmpl-1", name: "Billing API", baseUrl: null, version: 1 },
    ]);
    vi.mocked(requestTemplatesApi.delete).mockResolvedValue(undefined);
    renderPage();
    await screen.findByText("Billing API");

    await userEvent.click(screen.getByRole("button", { name: "Delete" }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: "Delete" }));

    expect(requestTemplatesApi.delete).toHaveBeenCalledWith("tmpl-1");
  });
});
