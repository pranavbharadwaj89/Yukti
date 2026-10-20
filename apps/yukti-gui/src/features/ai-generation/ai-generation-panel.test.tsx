import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createRootRoute,
  createRoute,
  createRouter,
  createMemoryHistory,
  Outlet,
  RouterProvider,
} from "@tanstack/react-router";
import { AiGenerationPanel, MAX_DESCRIPTION_LENGTH, REUSE_WINDOW_MS } from "./ai-generation-panel";
import { AiAttributionBadge } from "./ai-attribution-badge";
import { aiApi, ApiError, flowsApi } from "@/services/api-client";
import type { GenerateFlowResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  flowsApi: { generate: vi.fn() },
  aiApi: { status: vi.fn() },
  // Mirrors the real constructor (status, title, correlationId, detail) — `message` is the
  // RFC 7807 `detail`, exactly as the real client builds it.
  ApiError: class ApiError extends Error {
    status: number;
    title: string;
    correlationId: string | undefined;
    constructor(status: number, title: string, correlationId: string | undefined, detail: string) {
      super(detail);
      this.status = status;
      this.title = title;
      this.correlationId = correlationId;
    }
  },
}));

const generated = (flowId: string): GenerateFlowResponse => ({
  status: "generated",
  flowId,
  capability: "coder",
  confidence: 0.9,
  modelIdentifier: "stub",
  ambiguities: [],
});

function renderPanel() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  const rootRoute = createRootRoute({ component: () => <Outlet /> });
  const indexRoute = createRoute({ getParentRoute: () => rootRoute, path: "/", component: () => <AiGenerationPanel /> });
  const draftRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/flows/$flowId",
    component: function Draft() {
      const { flowId } = draftRoute.useParams();
      return <div>Draft page {flowId}</div>;
    },
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([indexRoute, draftRoute]),
    history: createMemoryHistory({ initialEntries: ["/"] }),
  });
  return { router, ...render(
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  ) };
}

const input = () => screen.findByLabelText(/describe a test/i);
const submitButton = () => screen.getByRole("button", { name: "Suggest a flow" });

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(aiApi.status).mockResolvedValue({ enabled: true });
});

afterEach(() => vi.restoreAllMocks());

describe("AiGenerationPanel", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    const { container } = renderPanel();
    await input();
    expect(await wcagViolations(container)).toEqual([]);
  });

  it("sends nothing for empty input — the button is disabled and submit is a no-op", async () => {
    renderPanel();
    const box = await input();
    fireEvent.change(box, { target: { value: "   " } });
    expect(submitButton()).toBeDisabled();
    fireEvent.submit(box.closest("form")!);
    expect(flowsApi.generate).not.toHaveBeenCalled();
  });

  it(`blocks a description over ${MAX_DESCRIPTION_LENGTH} characters before any request`, async () => {
    renderPanel();
    const box = await input();
    fireEvent.change(box, { target: { value: "a".repeat(MAX_DESCRIPTION_LENGTH + 1) } });
    expect(screen.getByText(/too long/i)).toBeInTheDocument();
    expect(box).toHaveAttribute("aria-invalid", "true");
    expect(submitButton()).toBeDisabled();
    fireEvent.submit(box.closest("form")!);
    expect(flowsApi.generate).not.toHaveBeenCalled();
  });

  it("a double-click sends exactly one request — the button is disabled while in flight", async () => {
    let resolve!: (v: GenerateFlowResponse) => void;
    vi.mocked(flowsApi.generate).mockReturnValue(new Promise((r) => (resolve = r)));
    renderPanel();
    const user = userEvent.setup();
    await user.type(await input(), "log in and check the order");

    const button = submitButton();
    await user.dblClick(button);
    expect(flowsApi.generate).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(button).toBeDisabled());
    expect(screen.getByRole("status")).toHaveTextContent(/drafting/i);

    resolve(generated("flow-1"));
    expect(await screen.findByText("Draft page flow-1")).toBeInTheDocument();
  });

  it("a generated flow lands on its Draft page in the normal editor", async () => {
    vi.mocked(flowsApi.generate).mockResolvedValue(generated("flow-42"));
    renderPanel();
    const user = userEvent.setup();
    await user.type(await input(), "check the orders API");
    await user.click(submitButton());

    expect(await screen.findByText("Draft page flow-42")).toBeInTheDocument();
    expect(flowsApi.generate).toHaveBeenCalledWith("check the orders API");
  });

  it(`identical text re-submitted within ${REUSE_WINDOW_MS / 1000}s reopens the same Draft without a second request; after the window it asks again`, async () => {
    // §R11. Success navigates away (unmounting the panel), so this deliberately goes there and
    // back — the reuse memory has to survive exactly that, or it never fires in real use.
    const now = vi.spyOn(Date, "now").mockReturnValue(1_000_000);
    vi.mocked(flowsApi.generate).mockResolvedValue(generated("flow-reuse"));
    const { router } = renderPanel();
    const user = userEvent.setup();

    await user.type(await input(), "same text");
    await user.click(submitButton());
    await screen.findByText("Draft page flow-reuse");
    expect(flowsApi.generate).toHaveBeenCalledTimes(1);

    await router.navigate({ to: "/" });
    now.mockReturnValue(1_000_000 + REUSE_WINDOW_MS - 1);
    await user.type(await input(), "  same text  "); // whitespace-insensitive, like the request itself
    await user.click(submitButton());
    await screen.findByText("Draft page flow-reuse");
    expect(flowsApi.generate).toHaveBeenCalledTimes(1); // reused — no second AI call

    await router.navigate({ to: "/" });
    now.mockReturnValue(1_000_000 + REUSE_WINDOW_MS + 1);
    await user.type(await input(), "same text");
    await user.click(submitButton());
    await screen.findByText("Draft page flow-reuse");
    expect(flowsApi.generate).toHaveBeenCalledTimes(2); // window elapsed — a real second request
  });

  it("an ambiguous result creates nothing and renders each reason as a clarifying button", async () => {
    vi.mocked(flowsApi.generate).mockResolvedValue({
      status: "ambiguous",
      flowId: null,
      capability: null,
      confidence: null,
      modelIdentifier: null,
      ambiguities: ["Could mean the api module or the web module.", "No module can send SMS."],
    });
    renderPanel();
    const user = userEvent.setup();
    const box = await input();
    await user.type(box, "check it worked");
    await user.click(submitButton());

    const clarify = await screen.findByRole("button", { name: "Could mean the api module or the web module." });
    expect(screen.getByRole("button", { name: "No module can send SMS." })).toBeInTheDocument();
    expect(screen.queryByText(/Draft page/)).not.toBeInTheDocument();

    await user.click(clarify);
    expect((box as HTMLTextAreaElement).value).toContain("(Re: Could mean the api module or the web module.)");
    expect(box).toHaveFocus();
    expect(screen.queryByRole("button", { name: "No module can send SMS." })).not.toBeInTheDocument();
  });

  it("an API error shows its plain-English detail inline — never a stack trace", async () => {
    vi.mocked(flowsApi.generate).mockRejectedValue(
      new ApiError(
        502,
        "AI Could Not Produce A Valid Flow",
        "corr-123",
        "The AI couldn't produce a flow that passes validation, even after a retry. Try rephrasing your description.",
      ),
    );
    renderPanel();
    const user = userEvent.setup();
    await user.type(await input(), "do something odd");
    await user.click(submitButton());

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Try rephrasing your description.");
    expect(alert.textContent).not.toMatch(/at \w+ \(|Exception|\.cs:/);
  });

  it("a non-API failure gets a generic plain sentence, not the raw error", async () => {
    vi.mocked(flowsApi.generate).mockRejectedValue(new TypeError("Failed to fetch at fetchWrapper (api-client.ts:12)"));
    renderPanel();
    const user = userEvent.setup();
    await user.type(await input(), "check the orders API");
    await user.click(submitButton());

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Something went wrong suggesting a flow. Please try again.");
    expect(alert.textContent).not.toContain("api-client.ts");
  });

  it("with AI off, the form is replaced by an explanation — no button to press", async () => {
    vi.mocked(aiApi.status).mockResolvedValue({ enabled: false });
    renderPanel();
    expect(await screen.findByRole("note")).toHaveTextContent(/turned off/i);
    expect(screen.queryByRole("button", { name: "Suggest a flow" })).not.toBeInTheDocument();
  });
});

describe("AiAttributionBadge", () => {
  it("shows the backend's own confidence as a percentage", () => {
    render(<AiAttributionBadge attribution={{ capability: "coder", confidence: 0.6, modelIdentifier: "gemini", promptVersion: "v1" }} />);
    expect(screen.getByText(/AI-assisted · 60% confidence/)).toBeInTheDocument();
  });

  it('says "confidence unavailable" rather than inventing a number', () => {
    render(<AiAttributionBadge attribution={{ capability: "coder", confidence: null, modelIdentifier: "gemini", promptVersion: "v1" }} />);
    expect(screen.getByText(/AI-assisted · confidence unavailable/)).toBeInTheDocument();
  });
});
