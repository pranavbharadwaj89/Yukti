import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { MarketplacePage } from "./marketplace-page";
import { modulesApi } from "@/services/api-client";
import type { ModuleResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  modulesApi: { list: vi.fn() },
}));

const modules: ModuleResponse[] = [
  {
    kind: "web",
    displayName: "Web",
    trust: "BuiltIn",
    contractVersion: "1.0.0",
    actions: [
      { actionName: "navigate", description: "", parameters: [] },
      { actionName: "click", description: "", parameters: [] },
    ],
  },
  {
    kind: "logs",
    displayName: "Logs",
    trust: "BuiltIn",
    contractVersion: "1.0.0",
    actions: [{ actionName: "checkRules", description: "", parameters: [] }],
  },
];

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <MarketplacePage />
    </QueryClientProvider>,
  );
}

describe("MarketplacePage", () => {
  it("shows a loading state before the real modules arrive", () => {
    vi.mocked(modulesApi.list).mockReturnValue(new Promise(() => {})); // never resolves
    renderPage();
    expect(screen.getByText(/Loading modules/)).toBeInTheDocument();
  });

  it("renders every real module returned by GET /api/v1/modules, with its real kind, trust tier, contract version and action count", async () => {
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    renderPage();

    expect(await screen.findByText("Web")).toBeInTheDocument();
    expect(screen.getByText("Logs")).toBeInTheDocument();
    expect(screen.getByText(/web · v1\.0\.0/)).toBeInTheDocument();
    expect(screen.getByText("2 actions")).toBeInTheDocument();
    expect(screen.getByText("1 action")).toBeInTheDocument();
    expect(screen.getAllByText("BuiltIn").length).toBeGreaterThanOrEqual(2);
  });

  it("shows a distinct empty state when the backend genuinely has zero modules — never looks like an error", async () => {
    vi.mocked(modulesApi.list).mockResolvedValue([]);
    renderPage();
    expect(await screen.findByText("No modules are registered yet.")).toBeInTheDocument();
    expect(screen.queryByText(/Couldn't load/)).not.toBeInTheDocument();
  });

  it("shows a distinct message when a search matches nothing, never an empty list indistinguishable from zero modules", async () => {
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    const user = userEvent.setup();
    renderPage();
    await screen.findByText("Web");
    await user.type(screen.getByPlaceholderText(/Search modules/), "nonexistent-module-xyz");
    expect(await screen.findByText(/No modules match/)).toBeInTheDocument();
  });

  it("shows an error state with a retry button on failure — never an empty list indistinguishable from \"no modules\"", async () => {
    vi.mocked(modulesApi.list).mockRejectedValueOnce(new Error("network down")).mockResolvedValueOnce(modules);
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByText(/Couldn't load modules/)).toBeInTheDocument();
    const retry = screen.getByRole("button", { name: "Retry" });
    await user.click(retry);

    expect(await screen.findByText("Web")).toBeInTheDocument();
  });

  it("every module the marketplace lists also has its real actions shown — the same list the step picker's GET /api/v1/modules call returns, not a separate hardcoded set", async () => {
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    renderPage();
    await screen.findByText("Web");

    const fromApi = modules.flatMap((m) => m.actions.map((a) => a.actionName));
    for (const actionName of fromApi) {
      expect(screen.getByText(actionName)).toBeInTheDocument();
    }
  });

  it("the Install button is keyboard-reachable and announces why it's disabled, instead of a real disabled attribute that removes it from the tab order", async () => {
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    renderPage();
    await screen.findByText("Web");

    const installButtons = screen.getAllByRole("button", { name: "Install" });
    const first = installButtons[0];
    expect(first).toHaveAttribute("aria-disabled", "true");
    expect(first).not.toHaveAttribute("disabled");

    first.focus();
    expect(first).toHaveFocus();

    const describedBy = first.getAttribute("aria-describedby");
    expect(describedBy).toBeTruthy();
    const tooltip = document.getElementById(describedBy!);
    expect(tooltip).toHaveTextContent("Publishing and install arrive in v0.2");
  });

  it("has no WCAG 2.1 A/AA axe violations with real modules loaded", async () => {
    vi.mocked(modulesApi.list).mockResolvedValue(modules);
    const { container } = renderPage();
    await screen.findByText("Web");
    await waitFor(async () => expect(await wcagViolations(container)).toEqual([]));
  });

  it("no hardcoded module list remains in the page's source", () => {
    const thisFile = fileURLToPath(import.meta.url);
    const source = readFileSync(path.join(path.dirname(thisFile), "marketplace-page.tsx"), "utf8");
    expect(source).not.toMatch(/AVAILABLE_MODULES/);
    expect(source).not.toMatch(/Apache Kafka/i);
  });
});
