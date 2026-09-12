import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ApiStudioPage } from "./api-studio-page";
import { apiStudioApi } from "@/services/api-client";

vi.mock("@/services/api-client", () => ({
  apiStudioApi: {
    listCollections: vi.fn(),
    createCollection: vi.fn(),
    renameCollection: vi.fn(),
    deleteCollection: vi.fn(),
    addRequest: vi.fn(),
    updateRequest: vi.fn(),
    deleteRequest: vi.fn(),
    executeAdHoc: vi.fn(),
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
    vi.mocked(apiStudioApi.listCollections).mockResolvedValue([
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
            body: null,
            timeoutMs: 5000,
            assertions: [],
          },
        ],
      },
    ]);

    renderPage();

    expect(await screen.findByText("User Service")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Send|Run/i }) || screen.getByText(/Send/i)).toBeDefined();
  });
});
