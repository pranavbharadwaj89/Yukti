import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { LogsStudioPage } from "./logs-studio-page";

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <LogsStudioPage />
    </QueryClientProvider>,
  );
}

describe("LogsStudioPage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    const { container } = renderPage();
    await screen.findByText("Logs Testing");
    expect(await wcagViolations(container)).toEqual([]);
  });
});
