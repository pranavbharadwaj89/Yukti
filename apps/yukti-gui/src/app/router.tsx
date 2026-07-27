import { createRootRoute, createRoute, createRouter, redirect, Outlet } from "@tanstack/react-router";
import { AppShell } from "@/layouts/app-shell";
import { LoginPage } from "@/pages/login-page";
import { DashboardPage } from "@/pages/dashboard-page";
import { SettingsPage } from "@/pages/settings-page";
import { FlowsPage } from "@/features/flow-authoring/flows-page";
import { FlowDetailPage } from "@/features/flow-authoring/flow-detail-page";
import { RunDetailPage } from "@/features/execution/run-detail-page";
import { ReportsPage } from "@/features/reporting-audit/reports-page";
import { useAuthStore } from "@/store/auth-store";

// FR-ROUTE-09: every authenticated route's loader checks for a session and
// redirects to /login otherwise — the identical guard pattern applied
// uniformly, not per-page ad hoc checks.
function requireSession() {
  if (!useAuthStore.getState().accessToken) {
    throw redirect({ to: "/login" });
  }
}

const rootRoute = createRootRoute({ component: () => <Outlet /> });

const loginRoute = createRoute({ getParentRoute: () => rootRoute, path: "/login", component: LoginPage });

const authenticatedLayoutRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: "authenticated",
  beforeLoad: requireSession,
  component: AppShell,
});

const dashboardRoute = createRoute({ getParentRoute: () => authenticatedLayoutRoute, path: "/", component: DashboardPage });
const flowsRoute = createRoute({ getParentRoute: () => authenticatedLayoutRoute, path: "/flows", component: FlowsPage });
const flowDetailRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/flows/$flowId",
  component: FlowDetailPage,
});
// FR-ROUTE-04: Workflow Designer maps to the same flow-authoring surface
// today — no dedicated drag-and-drop canvas exists yet (see
// FlowDetailPage's own doc comment on that simplification).
const workflowDesignerRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/workflow-designer",
  component: FlowsPage,
});
const runDetailRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/runs/$runId",
  component: RunDetailPage,
});
const reportsRoute = createRoute({ getParentRoute: () => authenticatedLayoutRoute, path: "/reports", component: ReportsPage });
const settingsRoute = createRoute({ getParentRoute: () => authenticatedLayoutRoute, path: "/settings", component: SettingsPage });

const routeTree = rootRoute.addChildren([
  loginRoute,
  authenticatedLayoutRoute.addChildren([
    dashboardRoute,
    flowsRoute,
    flowDetailRoute,
    workflowDesignerRoute,
    runDetailRoute,
    reportsRoute,
    settingsRoute,
  ]),
]);

export const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
