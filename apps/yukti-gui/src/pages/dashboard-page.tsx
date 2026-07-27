import { useQuery } from "@tanstack/react-query";
import { Link } from "@tanstack/react-router";
import { flowsApi, trendsApi } from "@/services/api-client";
import { flowStatusLabel } from "@/services/types";
import { Card, StatusPill } from "@/components/ui/primitives";

// FR-FEAT-01: a true landing page composing summary widgets from multiple
// features — not just a redirect into reporting-audit specifically.
export function DashboardPage() {
  const flowsQuery = useQuery({ queryKey: ["flows"], queryFn: flowsApi.list });
  const trendQuery = useQuery({ queryKey: ["trends"], queryFn: trendsApi.get });

  const recentFlows = (flowsQuery.data ?? []).slice(0, 5);
  const trend = trendQuery.data;

  return (
    <div className="flex flex-col gap-6">
      <h1 className="font-mono text-xl text-ink">Dashboard</h1>

      <div className="grid grid-cols-3 gap-4">
        <Card className="p-4">
          <div className="text-xs uppercase tracking-wide text-ink-dim">Runs, last 24h</div>
          <div className="mt-1 font-mono text-2xl tabular-nums text-ink">{trend?.totalRunsLast24h ?? "—"}</div>
        </Card>
        <Card className="p-4">
          <div className="text-xs uppercase tracking-wide text-ink-dim">Pass rate, last 24h</div>
          <div className="mt-1 font-mono text-2xl tabular-nums text-success">
            {trend ? `${(trend.passRateLast24h * 100).toFixed(0)}%` : "—"}
          </div>
        </Card>
        <Card className="p-4">
          <div className="text-xs uppercase tracking-wide text-ink-dim">Flows</div>
          <div className="mt-1 font-mono text-2xl tabular-nums text-ink">{flowsQuery.data?.length ?? "—"}</div>
        </Card>
      </div>

      <Card className="p-4">
        <div className="mb-3 flex items-center justify-between">
          <h2 className="font-mono text-sm text-ink-dim">Recent flows</h2>
          <Link to="/flows" className="text-xs text-accent hover:underline">
            View all
          </Link>
        </div>
        <div className="flex flex-col gap-2">
          {recentFlows.length === 0 && <div className="text-sm text-ink-dim">No flows yet.</div>}
          {recentFlows.map((f) => (
            <Link
              key={f.flowId.value}
              to="/flows/$flowId"
              params={{ flowId: f.flowId.value }}
              className="flex items-center justify-between rounded-md px-3 py-2 hover:bg-surface-2"
            >
              <span className="text-sm text-ink">{f.name}</span>
              <StatusPill status={flowStatusLabel(f.status)} />
            </Link>
          ))}
        </div>
      </Card>
    </div>
  );
}
