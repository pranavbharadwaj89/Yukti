import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Package, Search } from "lucide-react";
import { Button, Input, Spinner } from "@/components/ui/primitives";
import { modulesApi } from "@/services/api-client";
import type { ModuleResponse } from "@/services/types";

// D4-T5: this page used to render a hardcoded module list — eight
// entries that don't exist in the backend (a fake SQL module, a fake
// Kafka module...), with a fake install toggle that just flipped local
// React state and congratulated the user on "registering" a module that
// was never contacted. Every card below now comes from the same
// GET /api/v1/modules the flow editor's step picker uses — there is
// exactly one source of truth for "what modules exist", so these two
// surfaces cannot drift apart.
const TRUST_TIER_STYLES: Record<string, string> = {
  BuiltIn: "bg-emerald-500/10 text-emerald-600 border-emerald-500/20",
  Verified: "bg-blue-500/10 text-blue-600 border-blue-500/20",
  Community: "bg-amber-500/10 text-amber-600 border-amber-500/20",
};

export function MarketplacePage() {
  const [search, setSearch] = useState("");
  const modulesQuery = useQuery({ queryKey: ["modules"], queryFn: modulesApi.list });

  const modules = modulesQuery.data ?? [];
  const filtered = modules.filter((m) => {
    const q = search.toLowerCase();
    return (
      m.displayName.toLowerCase().includes(q) ||
      m.kind.toLowerCase().includes(q) ||
      m.actions.some((a) => a.actionName.toLowerCase().includes(q))
    );
  });

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-ink flex items-center gap-2">
          <Package className="text-accent" size={28} />
          Module Marketplace
        </h1>
        <p className="text-sm text-ink-dim mt-1">
          Every automation module Yukti has registered, with its real trust tier and actions.
        </p>
      </div>

      <div className="relative w-full sm:max-w-md">
        <Search className="absolute left-3 top-2.5 text-ink-dim" size={16} />
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search modules by name, kind, or action..."
          className="pl-9 w-full"
          disabled={modulesQuery.isLoading || modulesQuery.isError}
        />
      </div>

      {modulesQuery.isLoading && (
        <div className="flex items-center gap-2 text-sm text-ink-dim py-8 justify-center">
          <Spinner /> Loading modules…
        </div>
      )}

      {modulesQuery.isError && (
        <div className="flex flex-col items-center gap-3 py-8 text-center">
          <p className="text-sm text-danger">
            Couldn't load modules — the backend may be unreachable.
          </p>
          <Button variant="secondary" onClick={() => void modulesQuery.refetch()}>
            Retry
          </Button>
        </div>
      )}

      {modulesQuery.isSuccess && modules.length === 0 && (
        <p className="text-sm text-ink-dim py-8 text-center">
          No modules are registered yet.
        </p>
      )}

      {modulesQuery.isSuccess && modules.length > 0 && filtered.length === 0 && (
        <p className="text-sm text-ink-dim py-8 text-center">
          No modules match &quot;{search}&quot;.
        </p>
      )}

      {filtered.length > 0 && (
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
          {filtered.map((mod) => (
            <ModuleCard key={mod.kind} module={mod} />
          ))}
        </div>
      )}
    </div>
  );
}

function ModuleCard({ module: mod }: { module: ModuleResponse }) {
  const tooltipId = `install-tooltip-${mod.kind}`;
  return (
    <div className="p-5 rounded-lg border border-border bg-surface flex flex-col justify-between hover:border-accent/50 transition-all shadow-sm">
      <div>
        <div className="flex items-start justify-between gap-2">
          <h2 className="font-semibold text-ink">{mod.displayName}</h2>
          <span
            className={`text-[10px] uppercase font-bold px-2 py-0.5 rounded border ${TRUST_TIER_STYLES[mod.trust] ?? TRUST_TIER_STYLES.Community}`}
          >
            {mod.trust}
          </span>
        </div>
        <div className="text-xs text-ink-dim mt-1 font-mono">
          {mod.kind} · v{mod.contractVersion}
        </div>
        <p className="text-xs text-ink-dim mt-3">
          {mod.actions.length} action{mod.actions.length === 1 ? "" : "s"}
        </p>
        <div className="flex flex-wrap gap-1 mt-1.5">
          {mod.actions.map((a) => (
            <code key={a.actionName} className="text-[11px] px-1.5 py-0.5 bg-surface-2 border border-border rounded text-ink">
              {a.actionName}
            </code>
          ))}
        </div>
      </div>

      <div className="mt-5 pt-3 border-t border-border flex items-center justify-end">
        <div className="relative group/tooltip">
          <Button
            variant="secondary"
            aria-disabled="true"
            aria-describedby={tooltipId}
            className="text-xs py-1 px-2.5 opacity-50"
            onClick={(e) => e.preventDefault()}
          >
            Install
          </Button>
          <span
            id={tooltipId}
            role="tooltip"
            className="pointer-events-none absolute bottom-full right-0 mb-1.5 whitespace-nowrap rounded-md border border-border bg-surface px-2 py-1 text-[11px] text-ink opacity-0 shadow-sm transition-opacity group-hover/tooltip:opacity-100 group-focus-within/tooltip:opacity-100"
          >
            Publishing and install arrive in v0.2
          </span>
        </div>
      </div>
    </div>
  );
}
