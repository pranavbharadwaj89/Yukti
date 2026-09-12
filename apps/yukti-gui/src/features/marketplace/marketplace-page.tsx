import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { Package, Search, CheckCircle2, ShieldCheck, Download, ExternalLink } from "lucide-react";
import { Button, Input, Spinner } from "@/components/ui/primitives";
import { useToastStore } from "@/store/toast-store";

interface ModulePackage {
  id: string;
  name: string;
  category: "Core" | "Community" | "Enterprise";
  version: string;
  author: string;
  description: string;
  supportedActions: string[];
  installed: boolean;
  trustTier: "Verified" | "BuiltIn" | "Community";
}

const AVAILABLE_MODULES: ModulePackage[] = [
  {
    id: "mod-api",
    name: "HTTP / REST API Module",
    category: "Core",
    version: "1.0.0",
    author: "Yukti Core Team",
    description: "High-performance HTTP client with JSONPath assertions, variable chaining, and environment scopes.",
    supportedActions: ["ExecuteRequest", "AssertStatusCode", "ExtractJsonPath"],
    installed: true,
    trustTier: "BuiltIn"
  },
  {
    id: "mod-web",
    name: "Web Browser Module",
    category: "Core",
    version: "1.0.0",
    author: "Yukti Core Team",
    description: "Playwright-backed multi-browser automation with self-healing locator fallbacks.",
    supportedActions: ["Navigate", "Click", "Type", "TakeScreenshot", "AssertVisible"],
    installed: true,
    trustTier: "BuiltIn"
  },
  {
    id: "mod-logs",
    name: "Log & Observability Module",
    category: "Core",
    version: "1.0.0",
    author: "Yukti Core Team",
    description: "Real-time log tailing, rule evaluation, and anomaly pattern detection.",
    supportedActions: ["AssertPattern", "VerifyNoErrors", "DetectAnomalies"],
    installed: true,
    trustTier: "BuiltIn"
  },
  {
    id: "mod-mobile",
    name: "Mobile Appium Module",
    category: "Core",
    version: "1.0.0",
    author: "Yukti Core Team",
    description: "W3C-compliant mobile app automation for iOS and Android.",
    supportedActions: ["LaunchApp", "TapElement", "InputText", "Swipe"],
    installed: true,
    trustTier: "BuiltIn"
  },
  {
    id: "mod-desktop",
    name: "Desktop UI RPA Module",
    category: "Core",
    version: "1.0.0",
    author: "Yukti Core Team",
    description: "Windows UI Automation accessibility-tree RPA module.",
    supportedActions: ["AttachWindow", "InvokeControl", "ReadText"],
    installed: true,
    trustTier: "BuiltIn"
  },
  {
    id: "mod-sql",
    name: "Database SQL Automation Module",
    category: "Community",
    version: "0.9.4",
    author: "DataOps Community",
    description: "Execute and assert Postgres, MySQL, and SQL Server queries in the flow pipeline.",
    supportedActions: ["ExecuteQuery", "AssertRowCount", "ExtractScalar"],
    installed: false,
    trustTier: "Verified"
  },
  {
    id: "mod-kafka",
    name: "Apache Kafka Event Module",
    category: "Community",
    version: "0.8.2",
    author: "Streaming QA Group",
    description: "Produce test records and consume topic events with assertion windows.",
    supportedActions: ["ProduceMessage", "ConsumeRecord", "AssertOffsetLag"],
    installed: false,
    trustTier: "Verified"
  },
  {
    id: "mod-graphql",
    name: "GraphQL Quality Module",
    category: "Community",
    version: "0.9.1",
    author: "GraphQL Guild",
    description: "Type-safe GraphQL query and mutation executions with schema validation.",
    supportedActions: ["ExecuteQuery", "AssertSchema", "ValidateDirective"],
    installed: false,
    trustTier: "Community"
  }
];

export function MarketplacePage() {
  const [search, setSearch] = useState("");
  const [selectedCategory, setSelectedCategory] = useState<string>("All");
  const [installedList, setInstalledList] = useState<Record<string, boolean>>({
    "mod-api": true,
    "mod-web": true,
    "mod-logs": true,
    "mod-mobile": true,
    "mod-desktop": true
  });

  const pushToast = useToastStore((s) => s.push);

  const filtered = AVAILABLE_MODULES.filter((m) => {
    const matchesSearch =
      m.name.toLowerCase().includes(search.toLowerCase()) ||
      m.description.toLowerCase().includes(search.toLowerCase()) ||
      m.supportedActions.some((a) => a.toLowerCase().includes(search.toLowerCase()));

    const matchesCategory = selectedCategory === "All" || m.category === selectedCategory;
    return matchesSearch && matchesCategory;
  });

  function handleInstallToggle(mod: ModulePackage) {
    const currentlyInstalled = !!installedList[mod.id];
    setInstalledList((prev) => ({ ...prev, [mod.id]: !currentlyInstalled }));
    pushToast({
      kind: "success",
      message: currentlyInstalled
        ? `Module ${mod.name} uninstalled.`
        : `Module ${mod.name} registered and activated successfully!`
    });
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold text-ink flex items-center gap-2">
            <Package className="text-accent" size={28} />
            Module Marketplace
          </h1>
          <p className="text-sm text-ink-dim mt-1">
            Discover, inspect, and install extensible capability modules for the Yukti orchestrator.
          </p>
        </div>
      </div>

      <div className="flex flex-col sm:flex-row items-center gap-3">
        <div className="relative flex-1 w-full">
          <Search className="absolute left-3 top-2.5 text-ink-dim" size={16} />
          <Input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search modules by name, action, or capability..."
            className="pl-9 w-full"
          />
        </div>
        <div className="flex items-center gap-1 bg-surface-raised p-1 rounded-md border border-edge">
          {["All", "Core", "Community"].map((cat) => (
            <button
              key={cat}
              type="button"
              onClick={() => setSelectedCategory(cat)}
              className={`px-3 py-1.5 text-xs font-medium rounded transition-colors ${
                selectedCategory === cat ? "bg-accent text-white" : "text-ink-dim hover:text-ink"
              }`}
            >
              {cat}
            </button>
          ))}
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
        {filtered.map((mod) => {
          const isInstalled = !!installedList[mod.id];
          return (
            <div
              key={mod.id}
              className="p-5 rounded-lg border border-edge bg-surface flex flex-col justify-between hover:border-accent/50 transition-all shadow-sm"
            >
              <div>
                <div className="flex items-start justify-between gap-2">
                  <h2 className="font-semibold text-ink">{mod.name}</h2>
                  <span
                    className={`text-[10px] uppercase font-bold px-2 py-0.5 rounded border ${
                      mod.trustTier === "BuiltIn"
                        ? "bg-emerald-500/10 text-emerald-600 border-emerald-500/20"
                        : mod.trustTier === "Verified"
                        ? "bg-blue-500/10 text-blue-600 border-blue-500/20"
                        : "bg-amber-500/10 text-amber-600 border-amber-500/20"
                    }`}
                  >
                    {mod.trustTier}
                  </span>
                </div>
                <div className="text-xs text-ink-dim mt-1">v{mod.version} · by {mod.author}</div>
                <p className="text-xs text-ink mt-3 leading-relaxed">{mod.description}</p>

                <div className="mt-4">
                  <span className="text-[11px] font-medium text-ink-dim">Exported Actions:</span>
                  <div className="flex flex-wrap gap-1 mt-1.5">
                    {mod.supportedActions.map((act) => (
                      <code
                        key={act}
                        className="text-[11px] px-1.5 py-0.5 bg-surface-raised border border-edge rounded text-ink"
                      >
                        {act}
                      </code>
                    ))}
                  </div>
                </div>
              </div>

              <div className="mt-5 pt-3 border-t border-edge flex items-center justify-between">
                {isInstalled ? (
                  <span className="text-xs text-emerald-600 font-medium flex items-center gap-1.5">
                    <CheckCircle2 size={15} /> Active
                  </span>
                ) : (
                  <span className="text-xs text-ink-dim flex items-center gap-1.5">
                    <ShieldCheck size={15} /> Verified Plugin
                  </span>
                )}

                <Button
                  variant={isInstalled ? "outline" : "primary"}
                  size="sm"
                  onClick={() => handleInstallToggle(mod)}
                  disabled={mod.trustTier === "BuiltIn"}
                  className="text-xs"
                >
                  {isInstalled ? (mod.trustTier === "BuiltIn" ? "Built-in" : "Deactivate") : "Install"}
                </Button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
