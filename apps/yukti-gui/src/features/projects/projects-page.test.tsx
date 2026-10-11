import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ProjectsPage } from "./projects-page";
import { projectsApi } from "@/services/api-client";

vi.mock("@/services/api-client", () => ({
  projectsApi: { list: vi.fn(), create: vi.fn() },
  environmentsApi: { list: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn() },
  ApiError: class ApiError extends Error {},
}));

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ProjectsPage />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});

describe("ProjectsPage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    vi.mocked(projectsApi.list).mockResolvedValue([
      { id: "proj-1", name: "Core Platform", description: "Primary test project" },
    ]);
    const { container } = renderPage();
    await screen.findByText("Core Platform");
    expect(await wcagViolations(container)).toEqual([]);
  });
});
