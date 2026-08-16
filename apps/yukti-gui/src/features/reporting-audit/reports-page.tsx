import { useMemo } from "react";
import { useQueries, useQuery } from "@tanstack/react-query";
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer } from "recharts";
import { auditApi, flowReportsApi, trendsApi } from "@/services/api-client";
import { Card, Spinner, StatusPill } from "@/components/ui/primitives";
import { EmptyState } from "@/components/ui/feedback";
import { DataTable, type Column } from "@/components/ui/data-table";
import type { AuditEntryResponse, FlowReportSummaryResponse, FlowRunReportResponse } from "@/services/types";

// FR-FEAT-07/FR-CQRS-03: staleness is part of the payload, not inferred —
// LastUpdatedAt is always shown alongside the numbers it describes.
export function ReportsPage() {
  const trendQuery = useQuery({ queryKey: ["trends"], queryFn: trendsApi.get });
  const flowReportsQuery = useQuery({ queryKey: ["flow-reports"], queryFn: flowReportsApi.list });

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

      <div className="flex flex-col gap-3">
        <h2 className="font-mono text-sm text-ink-dim">By flow</h2>
        <FlowReportsTable rows={flowReportsQuery.data ?? []} loading={flowReportsQuery.isLoading} />
      </div>
    </div>
  );
}

// Per-flow drill-down over the same FlowReportReadModel rows /trends
// aggregates tenant-wide — GET /api/v1/flow-reports groups them by flow
// instead. Expanding a row shows individual run history plus the
// TriggerFlowRunCommand/CancelFlowRunCommand audit entries whose
// metadata.FlowId matches this flow (the audit tie-in).
function FlowReportsTable({ rows, loading }: { rows: FlowReportSummaryResponse[]; loading: boolean }) {
  const columns: Column<FlowReportSummaryResponse>[] = [
    { key: "flowName", header: "Flow", sortable: true, sortValue: (r) => r.flowName, render: (r) => <span className="font-mono text-body-sm text-ink">{r.flowName}</span> },
    { key: "totalRuns", header: "Runs", sortable: true, align: "right", sortValue: (r) => r.totalRuns, render: (r) => <span className="tabular-nums">{r.totalRuns}</span> },
    {
      key: "passRate",
      header: "Pass rate",
      sortable: true,
      align: "right",
      sortValue: (r) => (r.totalRuns > 0 ? r.passedRuns / r.totalRuns : 0),
      render: (r) => <span className="tabular-nums">{r.totalRuns > 0 ? `${((r.passedRuns / r.totalRuns) * 100).toFixed(0)}%` : "—"}</span>,
    },
    { key: "lastRunStatus", header: "Last run", sortable: true, sortValue: (r) => r.lastRunStatus, render: (r) => <StatusPill status={r.lastRunStatus} /> },
    { key: "lastRunAt", header: "Last run at", sortable: true, sortValue: (r) => r.lastRunAt, render: (r) => <span className="text-ink-dim">{new Date(r.lastRunAt).toLocaleString()}</span> },
  ];

  return (
    <DataTable
      columns={columns}
      rows={rows}
      rowKey={(r) => r.flowId}
      loading={loading}
      emptyTitle="No flow runs reported yet"
      emptyDescription="Per-flow numbers appear once a flow has completed at least one run."
      pageSize={10}
      expandedContent={(r) => <FlowDrilldown flowId={r.flowId} />}
    />
  );
}

function FlowDrilldown({ flowId }: { flowId: string }) {
  const runsQuery = useQuery({ queryKey: ["flow-reports", flowId, "runs"], queryFn: () => flowReportsApi.runsByFlow(flowId) });
  const auditQuery = useQuery({ queryKey: ["audit-entries"], queryFn: auditApi.list });

  const flowRunCommandEntries = useMemo(
    () => (auditQuery.data ?? []).filter((a) => a.commandType === "TriggerFlowRunCommand" || a.commandType === "CancelFlowRunCommand"),
    [auditQuery.data],
  );

  const detailQueries = useQueries({
    queries: flowRunCommandEntries.map((entry) => ({
      queryKey: ["audit-entry", entry.id],
      queryFn: () => auditApi.getById(entry.id),
      enabled: flowRunCommandEntries.length > 0,
    })),
  });

  const relatedAuditEntries = useMemo(() => {
    return flowRunCommandEntries
      .map((entry, i): AuditEntryResponse | null => {
        const metadata = detailQueries[i]?.data?.metadata;
        const rawFlowId = metadata?.FlowId as unknown;
        const matches =
          rawFlowId === flowId || (typeof rawFlowId === "object" && rawFlowId !== null && (rawFlowId as { value?: string }).value === flowId);
        return matches ? entry : null;
      })
      .filter((e): e is AuditEntryResponse => e !== null);
  }, [flowRunCommandEntries, detailQueries, flowId]);

  const auditLoading = auditQuery.isLoading || detailQueries.some((q) => q.isLoading);

  return (
    <div className="flex flex-col gap-4">
      <div>
        <h3 className="mb-2 font-mono text-xs uppercase tracking-wide text-ink-dim">Run history</h3>
        <RunHistoryTable runs={runsQuery.data ?? []} loading={runsQuery.isLoading} />
      </div>
      <div>
        <h3 className="mb-2 font-mono text-xs uppercase tracking-wide text-ink-dim">Audit trail</h3>
        {auditLoading ? (
          <div className="flex items-center gap-2 text-body-sm text-ink-dim">
            <Spinner /> Loading audit tie-in…
          </div>
        ) : relatedAuditEntries.length === 0 ? (
          <div className="text-body-sm text-ink-dim">No trigger/cancel commands recorded for this flow yet.</div>
        ) : (
          <ul className="flex flex-col gap-1.5">
            {relatedAuditEntries.map((a) => (
              <li key={a.id} className="flex items-center gap-2 text-body-sm">
                <StatusPill status={a.succeeded ? "Passed" : "Failed"} />
                <span className="font-mono text-ink">{a.commandType}</span>
                <span className="text-ink-dim">{new Date(a.occurredAt).toLocaleString()}</span>
                {a.failureReason && <span className="text-danger">{a.failureReason}</span>}
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
}

function RunHistoryTable({ runs, loading }: { runs: FlowRunReportResponse[]; loading: boolean }) {
  if (loading) {
    return (
      <div className="flex items-center gap-2 text-body-sm text-ink-dim">
        <Spinner /> Loading run history…
      </div>
    );
  }
  if (runs.length === 0) {
    return <div className="text-body-sm text-ink-dim">No runs reported for this flow yet.</div>;
  }
  return (
    <table className="w-full text-body-sm">
      <thead>
        <tr className="text-left text-xs uppercase tracking-wide text-ink-dim">
          <th className="pb-1.5 pr-4 font-normal">Status</th>
          <th className="pb-1.5 pr-4 font-normal">Passed / Failed / Skipped</th>
          <th className="pb-1.5 pr-4 font-normal">Duration</th>
          <th className="pb-1.5 font-normal">Occurred at</th>
        </tr>
      </thead>
      <tbody>
        {runs.map((r) => (
          <tr key={r.flowRunId} className="border-t border-border">
            <td className="py-1.5 pr-4">
              <StatusPill status={r.finalStatus} />
            </td>
            <td className="py-1.5 pr-4 font-mono tabular-nums text-ink-dim">
              {r.passedCount} / {r.failedCount} / {r.skippedCount}
            </td>
            <td className="py-1.5 pr-4 font-mono tabular-nums text-ink-dim">{Math.round(r.totalDurationMs)}ms</td>
            <td className="py-1.5 text-ink-dim">{new Date(r.occurredAt).toLocaleString()}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
