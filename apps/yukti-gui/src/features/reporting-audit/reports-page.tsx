import { useQuery } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from "recharts";
import { trendsApi } from "@/services/api-client";
import { Card, Spinner } from "@/components/ui/primitives";
import { EmptyState } from "@/components/ui/feedback";

// FR-FEAT-07/FR-CQRS-03: staleness is part of the payload, not inferred —
// LastUpdatedAt is always shown alongside the numbers it describes.
export function ReportsPage() {
  const trendQuery = useQuery({ queryKey: ["trends"], queryFn: trendsApi.get });

  if (trendQuery.isLoading) return <Spinner />;
  const trend = trendQuery.data;

  if (!trend) {
    return (
      <div className="flex flex-col gap-4">
        <h1 className="text-h1 text-ink">Reports</h1>
        <EmptyState title="No trend data yet" description="Run a flow to start populating this tenant's trends." />
      </div>
    );
  }

  const data = [
    { name: "Passed", value: Math.round(trend.totalRunsLast24h * trend.passRateLast24h) },
    { name: "Failed", value: trend.totalRunsLast24h - Math.round(trend.totalRunsLast24h * trend.passRateLast24h) },
  ];

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="font-mono text-xl text-ink">Reports</h1>
        <div className="font-mono text-xs text-ink-dim">last updated {new Date(trend.lastUpdatedAt).toLocaleString()}</div>
      </div>

      <div className="grid grid-cols-3 gap-4">
        <Card className="p-4">
          <div className="text-xs uppercase tracking-wide text-ink-dim">Total runs, 24h</div>
          <div className="mt-1 font-mono text-2xl tabular-nums">{trend.totalRunsLast24h}</div>
        </Card>
        <Card className="p-4">
          <div className="text-xs uppercase tracking-wide text-ink-dim">Pass rate</div>
          <div className="mt-1 font-mono text-2xl tabular-nums text-success">{(trend.passRateLast24h * 100).toFixed(1)}%</div>
        </Card>
        <Card className="p-4">
          <div className="text-xs uppercase tracking-wide text-ink-dim">Flake rate</div>
          <div className="mt-1 font-mono text-2xl tabular-nums text-warning">{(trend.flakeRateLast24h * 100).toFixed(1)}%</div>
        </Card>
      </div>

      <Card className="p-4">
        <h2 className="mb-3 font-mono text-sm text-ink-dim">Pass / Fail, last 24h</h2>
        <div style={{ width: "100%", height: 260 }}>
          <ResponsiveContainer>
            <BarChart data={data}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--yukti-border)" />
              <XAxis dataKey="name" stroke="var(--yukti-ink-dim)" fontSize={12} />
              <YAxis stroke="var(--yukti-ink-dim)" fontSize={12} allowDecimals={false} />
              <Tooltip contentStyle={{ background: "var(--yukti-surface)", border: "1px solid var(--yukti-border)" }} />
              <Bar dataKey="value" fill="var(--yukti-accent)" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </Card>
    </div>
  );
}
