import { createContext, useContext, useId, type KeyboardEvent, type ReactNode } from "react";

// Minimal controlled tabs — no primitive like this existed yet. Follows the
// hand-rolled variant-map style the rest of components/ui/ uses (badge.tsx,
// primitives.tsx), not a real cva() despite the dependency being present.

interface TabsContextValue {
  value: string;
  onChange: (value: string) => void;
  idBase: string;
}

const TabsContext = createContext<TabsContextValue | null>(null);

function useTabsContext(component: string): TabsContextValue {
  const ctx = useContext(TabsContext);
  if (!ctx) throw new Error(`<${component}> must be used inside <Tabs>`);
  return ctx;
}

export function Tabs({
  value,
  onChange,
  children,
  className = "",
}: {
  value: string;
  onChange: (value: string) => void;
  children: ReactNode;
  className?: string;
}) {
  const idBase = useId();
  return (
    <TabsContext.Provider value={{ value, onChange, idBase }}>
      <div className={className}>{children}</div>
    </TabsContext.Provider>
  );
}

export function TabList({ children, className = "" }: { children: ReactNode; className?: string }) {
  return (
    <div role="tablist" className={`flex items-center gap-1 border-b border-border ${className}`}>
      {children}
    </div>
  );
}

export function Tab({ value, children }: { value: string; children: ReactNode }) {
  const { value: active, onChange, idBase } = useTabsContext("Tab");
  const selected = active === value;

  function onKeyDown(e: KeyboardEvent<HTMLButtonElement>) {
    const target = e.currentTarget;
    const tabs = Array.from(target.parentElement?.querySelectorAll<HTMLButtonElement>('[role="tab"]') ?? []);
    const index = tabs.indexOf(target);
    if (e.key === "ArrowRight" || e.key === "ArrowLeft") {
      e.preventDefault();
      const next = e.key === "ArrowRight" ? (index + 1) % tabs.length : (index - 1 + tabs.length) % tabs.length;
      tabs[next]?.focus();
      tabs[next]?.click();
    }
  }

  return (
    <button
      type="button"
      role="tab"
      id={`${idBase}-tab-${value}`}
      aria-selected={selected}
      aria-controls={`${idBase}-panel-${value}`}
      tabIndex={selected ? 0 : -1}
      onClick={() => onChange(value)}
      onKeyDown={onKeyDown}
      className={`border-b-2 px-3 py-2 text-body-sm font-medium transition-colors ${
        selected ? "border-accent text-accent" : "border-transparent text-ink-dim hover:text-ink"
      }`}
    >
      {children}
    </button>
  );
}

export function TabPanel({ value, children, className = "" }: { value: string; children: ReactNode; className?: string }) {
  const { value: active, idBase } = useTabsContext("TabPanel");
  if (active !== value) return null;
  return (
    <div role="tabpanel" id={`${idBase}-panel-${value}`} aria-labelledby={`${idBase}-tab-${value}`} className={className}>
      {children}
    </div>
  );
}
