import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { auditApi } from "@/services/api-client";
import { DataTable, type Column } from "@/components/ui/data-table";
import { StatusPill, Input, Spinner } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import type { AuditEntryResponse } from "@/services/types";

const RESULT_OPTIONS = [
  { value: "all", label: "All results" },
  { value: "succeeded", label: "Succeeded" },
  { value: "failed", label: "Failed" },
];

// Read-only, list-only — matches the backend's append-only, no-delete
// design (AuditEntry.cs: "no repository interface exposes an Update/Delete").
// Metadata is fetched per-row on expand (GET /audit-entries/{id}), not in
// the list response — IAuditSummaryQuery deliberately excludes it there.
export function AuditPage() {
  const [search, setSearch] = useState("");
  const [result, setResult] = useState<"all" | "succeeded" | "failed">("all");
  const auditQuery = useQuery({ queryKey: ["audit-entries"], queryFn: auditApi.list });

  const filtered = useMemo(() => {
    const entries = auditQuery.data ?? [];
    const q = search.trim().toLowerCase();
    return entries.filter((a) => {
      if (result === "succeeded" && !a.succeeded) return false;
      if (result === "failed" && a.succeeded) return false;
      if (!q) return true;
      return a.commandType.toLowerCase().includes(q) || (a.failureReason ?? "").toLowerCase().includes(q);
    });
  }, [auditQuery.data, search, result]);

  const columns: Column<AuditEntryResponse>[] = [
    { key: "commandType", header: "Command", sortable: true, sortValue: (a) => a.commandType, render: (a) => <span className="font-mono text-body-sm text-ink">{a.commandType}</span> },
    { key: "succeeded", header: "Result", sortable: true, sortValue: (a) => (a.succeeded ? 1 : 0), render: (a) => <StatusPill status={a.succeeded ? "Passed" : "Failed"} /> },
    { key: "failureReason", header: "Failure reason", render: (a) => <span className="text-ink-dim">{a.failureReason ?? "—"}</span> },
    { key: "occurredAt", header: "Occurred at", sortable: true, sortValue: (a) => a.occurredAt, render: (a) => <span className="text-ink-dim">{new Date(a.occurredAt).toLocaleString()}</span> },
  ];

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-h1 text-ink">Audit</h1>

      <div className="flex flex-wrap items-center gap-3">
        <div className="w-64">
          <label htmlFor="audit-search" className="sr-only">
            Search audit entries
          </label>
          <Input
            id="audit-search"
            placeholder="Search command or failure reason…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        <div className="w-44">
          <label htmlFor="audit-result-filter" className="sr-only">
            Filter by result
          </label>
          <Select id="audit-result-filter" value={result} options={RESULT_OPTIONS} onChange={(v) => setResult(v as typeof result)} />
        </div>
      </div>

      <DataTable
        columns={columns}
        rows={filtered}
        rowKey={(a) => a.id}
        loading={auditQuery.isLoading}
        emptyTitle={auditQuery.data && auditQuery.data.length > 0 ? "No matching audit entries" : "No audit entries yet"}
        emptyDescription="Every command handler writes one of these as it runs."
        pageSize={20}
        expandedContent={(a) => <AuditEntryDetail id={a.id} />}
      />
    </div>
  );
}

function AuditEntryDetail({ id }: { id: string }) {
  const detailQuery = useQuery({ queryKey: ["audit-entry", id], queryFn: () => auditApi.getById(id) });

  if (detailQuery.isLoading) {
    return (
      <div className="flex items-center gap-2 text-body-sm text-ink-dim">
        <Spinner /> Loading metadata…
      </div>
    );
  }

  if (detailQuery.isError) {
    return <div className="text-body-sm text-danger">Failed to load entry metadata.</div>;
  }

  const metadata = detailQuery.data?.metadata ?? {};
  const entries = Object.entries(metadata);

  if (entries.length === 0) {
    return <div className="text-body-sm text-ink-dim">No metadata recorded for this entry.</div>;
  }

  return (
    <dl className="grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1.5 text-body-sm">
      {entries.map(([key, value]) => (
        <div key={key} className="contents">
          <dt className="font-mono text-ink-dim">{key}</dt>
          <dd className="break-all font-mono text-ink">{formatMetadataValue(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

function formatMetadataValue(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "string") return value;
  return JSON.stringify(value);
}
