import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiStudioPage } from "./api-studio-page";
import { apiCollectionsApi } from "@/services/api-client";

vi.mock("@/services/api-client", () => ({
  apiCollectionsApi: {
    list: vi.fn(),
    create: vi.fn(),
    rename: vi.fn(),
    delete: vi.fn(),
    addRequest: vi.fn(),
    updateRequest: vi.fn(),
    deleteRequest: vi.fn(),
  },
  projectsApi: {
    listEnvironments: vi.fn().mockResolvedValue([]),
  },
  ApiError: class ApiError extends Error {},
}));

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ApiStudioPage />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("ApiStudioPage", () => {
  it("renders the explorer tree and request designer panes", async () => {
    vi.mocked(apiCollectionsApi.list).mockResolvedValue([
      {
        id: "col-1",
        name: "User Service",
        projectId: null,
        requests: [
          {
            id: "req-1",
            name: "Get Current User",
            method: "GET",
            url: "https://api.example.com/user",
            headers: {},
            queryParams: {},
            body: null,
            assertions: [],
            order: 1,
          },
        ],
      },
    ]);

    renderPage();

    expect(await screen.findByText("User Service")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Send|Run/i }) || screen.getByText(/Send/i)).toBeDefined();
  });

  it("has no WCAG 2.1 A/AA axe violations", async () => {
    vi.mocked(apiCollectionsApi.list).mockResolvedValue([
      { id: "col-1", name: "User Service", projectId: null, requests: [
        { id: "req-1", name: "Get Current User", method: "GET", url: "https://api.example.com/user",
          headers: {}, queryParams: {}, body: null, assertions: [], order: 1 } ] },
    ]);
    const { container } = renderPage();
    await screen.findByText("User Service");
    expect(await wcagViolations(container)).toEqual([]);
  });
});
