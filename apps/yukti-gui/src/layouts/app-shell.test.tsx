import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { wcagViolations } from "@/a11y/axe";
import { AppShell } from "./app-shell";

vi.mock("@tanstack/react-router", () => ({
  Link: ({ to, children, ...rest }: { to: string; children: React.ReactNode }) => (
    <a href={to} {...rest}>
      {children}
    </a>
  ),
  Outlet: () => <h1>Page content</h1>,
  useNavigate: () => vi.fn(),
  useRouterState: ({ select }: { select: (s: { location: { pathname: string } }) => unknown }) =>
    select({ location: { pathname: "/audit" } }),
}));
vi.mock("@/services/api-client", () => ({
  projectsApi: { list: vi.fn().mockResolvedValue([{ id: "p1", name: "Alpha" }]) },
}));

function renderShell() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <AppShell />
    </QueryClientProvider>,
  );
}

describe("AppShell accessibility", () => {
  it("offers a skip link that targets the main landmark", () => {
    renderShell();
    const skip = screen.getByRole("link", { name: "Skip to main content" });
    expect(skip).toHaveAttribute("href", "#main-content");
    expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
  });

  it("labels the nav landmark and marks the current page", () => {
    renderShell();
    expect(screen.getByRole("navigation", { name: "Primary" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Audit" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", { name: "Reports" })).not.toHaveAttribute("aria-current");
  });

  it("has no WCAG 2.1 A/AA axe violations", async () => {
    const { container } = renderShell();
    await screen.findByRole("option", { name: "Alpha" });
    expect(await wcagViolations(container)).toEqual([]);
  });
});
