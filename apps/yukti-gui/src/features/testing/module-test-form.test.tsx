import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ModuleTestForm } from "./module-test-form";
import { modulesApi } from "@/services/api-client";
import type { ModuleResponse } from "@/services/types";

vi.mock("@/services/api-client", () => ({
  flowsApi: { create: vi.fn(), addStep: vi.fn(), publish: vi.fn(), triggerRun: vi.fn() },
  runsApi: { get: vi.fn() },
  modulesApi: { list: vi.fn() },
  getActionParams: vi.fn(() => undefined),
  ApiError: class ApiError extends Error {},
}));

const modules: ModuleResponse[] = [
  { kind: "web", displayName: "Web", trust: "BuiltIn", contractVersion: "1.0.0", actions: [{ actionName: "navigate", description: "", parameters: [] }] },
  { kind: "mobile", displayName: "Mobile", trust: "BuiltIn", contractVersion: "1.0.0", actions: [{ actionName: "tap", description: "", parameters: [] }] },
];

function renderForm(moduleKind: string) {
  vi.mocked(modulesApi.list).mockResolvedValue(modules);
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ModuleTestForm moduleKind={moduleKind} title={`${moduleKind} test`} />
    </QueryClientProvider>,
  );
}

// D4-T2: proves the module-extras registry refactor preserves real
// behavior (Mobile's pre-run panel appears, driven by a plain object
// lookup — see module-extras.ts — not by `moduleKind === "mobile"` inside
// this component anymore).
describe("ModuleTestForm — module-extras registry", () => {
  it("shows the Mobile pre-run panel for the mobile module", async () => {
    renderForm("mobile");
    expect(await screen.findByText("Device config")).toBeInTheDocument();
    expect(screen.getByText(/Platform name/)).toBeInTheDocument();
  });

  it("shows no pre-run panel for a module with no registry entry", async () => {
    renderForm("web");
    await screen.findByText("web test");
    expect(screen.queryByText("Device config")).not.toBeInTheDocument();
  });
});
