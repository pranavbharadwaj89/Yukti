import { createRootRoute, createRoute, createRouter, redirect, Outlet } from "@tanstack/react-router";
import { AppShell } from "@/layouts/app-shell";
import { LoginPage } from "@/pages/login-page";
import { DashboardPage } from "@/pages/dashboard-page";
import { SettingsPage } from "@/pages/settings-page";
import { ProjectsPage } from "@/features/projects/projects-page";
import { SchedulerPage } from "@/features/scheduler/scheduler-page";
import { AuditPage } from "@/features/audit/audit-page";
import { FlowsPage } from "@/features/flow-authoring/flows-page";
import { FlowDetailPage } from "@/features/flow-authoring/flow-detail-page";
import { RunDetailPage } from "@/features/execution/run-detail-page";
import { ReportsPage } from "@/features/reporting-audit/reports-page";
import { TestPlaceholderPage } from "@/features/testing/test-placeholder-page";
import { ModuleTestForm } from "@/features/testing/module-test-form";
import { ApiStudioPage } from "@/features/api-studio/api-studio-page";
import { LogsStudioPage } from "@/features/logs-studio/logs-studio-page";
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
const projectsRoute = createRoute({ getParentRoute: () => authenticatedLayoutRoute, path: "/projects", component: ProjectsPage });
const schedulerRoute = createRoute({ getParentRoute: () => authenticatedLayoutRoute, path: "/triggers", component: SchedulerPage });
const auditRoute = createRoute({ getParentRoute: () => authenticatedLayoutRoute, path: "/audit", component: AuditPage });
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

// Tests section — nav structure + routes now, real per-type forms later.
const testWebRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/tests/web",
  component: () => <ModuleTestForm moduleKind="web" title="Web Testing" />,
});
const testMobileRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/tests/mobile",
  component: () => <ModuleTestForm moduleKind="mobile" title="Mobile Testing" />,
});
const testUiRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/tests/ui",
  component: () => <ModuleTestForm moduleKind="ui" title="Desktop UI Testing" />,
});
const testApiRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/tests/api",
  component: ApiStudioPage,
});
const testDatabaseRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/tests/database",
  component: () => <TestPlaceholderPage title="Database Testing" />,
});
const testLogsRoute = createRoute({
  getParentRoute: () => authenticatedLayoutRoute,
  path: "/tests/logs",
  component: LogsStudioPage,
});

const routeTree = rootRoute.addChildren([
  loginRoute,
  authenticatedLayoutRoute.addChildren([
    dashboardRoute,
    projectsRoute,
    schedulerRoute,
    auditRoute,
    flowsRoute,
    flowDetailRoute,
    workflowDesignerRoute,
    runDetailRoute,
    reportsRoute,
    settingsRoute,
    testWebRoute,
    testMobileRoute,
    testUiRoute,
    testApiRoute,
    testDatabaseRoute,
    testLogsRoute,
  ]),
]);

export const router = createRouter({ routeTree });

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
