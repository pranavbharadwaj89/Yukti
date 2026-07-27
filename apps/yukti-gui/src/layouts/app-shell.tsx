import { Link, Outlet, useNavigate, useRouterState } from "@tanstack/react-router";
import { useAuthStore } from "@/store/auth-store";
import { useThemeStore, type Theme } from "@/store/theme-store";
import { ToastViewport } from "@/components/ui/primitives";

// FR-STRUCT-04/FR-PHIL-01: persistent nav rail + top bar, composed once at
// the app root — no page duplicates this chrome.
const NAV_ITEMS = [
  { to: "/", label: "Dashboard" },
  { to: "/flows", label: "Flows" },
  { to: "/workflow-designer", label: "Workflow Designer" },
  { to: "/reports", label: "Reports" },
  { to: "/settings", label: "Settings" },
];

export function AppShell() {
  const user = useAuthStore((s) => s.user);
  const clearSession = useAuthStore((s) => s.clearSession);
  const theme = useThemeStore((s) => s.theme);
  const setTheme = useThemeStore((s) => s.setTheme);
  const navigate = useNavigate();
  const pathname = useRouterState({ select: (s) => s.location.pathname });

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-bg text-ink">
      <nav className="flex w-56 flex-shrink-0 flex-col border-r border-border bg-surface">
        <div className="px-4 py-4 font-mono text-sm font-semibold tracking-wide text-accent">YUKTI</div>
        <ul className="flex flex-col gap-0.5 px-2">
          {NAV_ITEMS.map((item) => (
            <li key={item.to}>
              <Link
                to={item.to}
                className={`block rounded-md px-3 py-2 text-sm ${
                  pathname === item.to ? "bg-accent-soft text-accent" : "text-ink-dim hover:bg-surface-2 hover:text-ink"
                }`}
              >
                {item.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>

      <div className="flex flex-1 flex-col overflow-hidden">
        <header className="flex h-14 flex-shrink-0 items-center justify-between border-b border-border bg-surface px-5">
          <div className="font-mono text-xs text-ink-dim">{user?.email}</div>
          <div className="flex items-center gap-3">
            <select
              aria-label="Theme"
              value={theme}
              onChange={(e) => setTheme(e.target.value as Theme)}
              className="rounded-md border border-border bg-surface-2 px-2 py-1 text-xs text-ink"
            >
              <option value="dark">Dark</option>
              <option value="light">Light</option>
              <option value="high-contrast">High contrast</option>
            </select>
            <button
              className="text-xs text-ink-dim hover:text-ink"
              onClick={() => {
                clearSession();
                void navigate({ to: "/login" });
              }}
            >
              Sign out
            </button>
          </div>
        </header>

        <main className="flex-1 overflow-auto p-6">
          <Outlet />
        </main>
      </div>
      <ToastViewport />
    </div>
  );
}
