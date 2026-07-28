import { Fragment, useMemo, useState, type ReactNode } from "react";
import { ChevronDown, ChevronRight, ChevronsUpDown, ChevronUp } from "lucide-react";
import { Checkbox } from "@/components/ui/form-controls";
import { Pagination } from "@/components/ui/nav-components";
import { Spinner } from "@/components/ui/primitives";
import { EmptyState } from "@/components/ui/feedback";

// UI_Component_Spec.md Part 3 §1 (Data Table): sortable columns, selectable
// rows, expandable rows, pagination — generic over T so every feature
// (flows list, reports, future run history) shares one implementation
// instead of hand-rolling its own table.

export interface Column<T> {
  key: string;
  header: string;
  sortable?: boolean;
  align?: "left" | "right" | "center";
  render: (row: T) => ReactNode;
  sortValue?: (row: T) => string | number;
}

export function DataTable<T>({
  columns,
  rows,
  rowKey,
  loading,
  emptyTitle = "No data",
  emptyDescription,
  selectable = false,
  selected,
  onSelectedChange,
  expandedContent,
  pageSize,
}: {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string;
  loading?: boolean;
  emptyTitle?: string;
  emptyDescription?: string;
  selectable?: boolean;
  selected?: Set<string>;
  onSelectedChange?: (selected: Set<string>) => void;
  expandedContent?: (row: T) => ReactNode;
  pageSize?: number;
}) {
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  const [page, setPage] = useState(1);

  const sorted = useMemo(() => {
    if (!sortKey) return rows;
    const col = columns.find((c) => c.key === sortKey);
    if (!col?.sortValue) return rows;
    const copy = [...rows];
    copy.sort((a, b) => {
      const av = col.sortValue!(a);
      const bv = col.sortValue!(b);
      const cmp = av < bv ? -1 : av > bv ? 1 : 0;
      return sortDir === "asc" ? cmp : -cmp;
    });
    return copy;
  }, [rows, sortKey, sortDir, columns]);

  const pageCount = pageSize ? Math.max(1, Math.ceil(sorted.length / pageSize)) : 1;
  const paged = pageSize ? sorted.slice((page - 1) * pageSize, page * pageSize) : sorted;

  function toggleSort(key: string) {
    if (sortKey === key) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortKey(key);
      setSortDir("asc");
    }
  }

  function toggleExpand(id: string) {
    setExpanded((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleRowSelected(id: string) {
    if (!selected || !onSelectedChange) return;
    const next = new Set(selected);
    if (next.has(id)) next.delete(id);
    else next.add(id);
    onSelectedChange(next);
  }

  const allSelected = selected && paged.length > 0 && paged.every((r) => selected.has(rowKey(r)));

  if (loading) {
    return (
      <div className="flex justify-center py-12">
        <Spinner className="h-6 w-6 text-accent" />
      </div>
    );
  }

  if (rows.length === 0) {
    return <EmptyState title={emptyTitle} description={emptyDescription} />;
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="overflow-hidden overflow-x-auto rounded-md border border-border">
        <table className="w-full text-body">
          <thead className="border-b-2 border-border bg-surface-2">
            <tr>
              {expandedContent && <th className="w-10" />}
              {selectable && onSelectedChange && (
                <th className="w-12 px-4 py-3 text-center">
                  <Checkbox
                    checked={!!allSelected}
                    onChange={(checked) => {
                      const next = new Set(selected);
                      for (const r of paged) {
                        if (checked) next.add(rowKey(r));
                        else next.delete(rowKey(r));
                      }
                      onSelectedChange(next);
                    }}
                  />
                </th>
              )}
              {columns.map((col) => (
                <th
                  key={col.key}
                  onClick={() => col.sortable && toggleSort(col.key)}
                  className={`select-none whitespace-nowrap px-4 py-3 font-semibold text-ink ${
                    col.align === "right" ? "text-right" : col.align === "center" ? "text-center" : "text-left"
                  } ${col.sortable ? "cursor-pointer hover:bg-surface" : ""}`}
                >
                  <span className="inline-flex items-center gap-1.5">
                    {col.header}
                    {col.sortable &&
                      (sortKey === col.key ? (
                        sortDir === "asc" ? (
                          <ChevronUp size={14} className="text-ink-dim" />
                        ) : (
                          <ChevronDown size={14} className="text-ink-dim" />
                        )
                      ) : (
                        <ChevronsUpDown size={14} className="text-ink-dim/50" />
                      ))}
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {paged.map((row) => {
              const id = rowKey(row);
              const isSelected = selected?.has(id);
              const isExpanded = expanded.has(id);
              return (
                <Fragment key={id}>
                  <tr className={`border-b border-border last:border-0 hover:bg-surface-2 ${isSelected ? "bg-accent-soft" : ""}`}>
                    {expandedContent && (
                      <td className="px-2 text-center">
                        <button type="button" onClick={() => toggleExpand(id)} aria-label="Toggle row" className="text-ink-dim hover:text-ink">
                          {isExpanded ? <ChevronDown size={16} /> : <ChevronRight size={16} />}
                        </button>
                      </td>
                    )}
                    {selectable && onSelectedChange && (
                      <td className="px-4 py-3 text-center">
                        <Checkbox checked={!!isSelected} onChange={() => toggleRowSelected(id)} />
                      </td>
                    )}
                    {columns.map((col) => (
                      <td
                        key={col.key}
                        className={`px-4 py-3 text-ink ${
                          col.align === "right" ? "text-right" : col.align === "center" ? "text-center" : "text-left"
                        }`}
                      >
                        {col.render(row)}
                      </td>
                    ))}
                  </tr>
                  {expandedContent && isExpanded && (
                    <tr className="border-b border-border bg-surface-2 last:border-0">
                      <td colSpan={columns.length + 1 + (selectable ? 1 : 0)} className="px-6 py-4">
                        {expandedContent(row)}
                      </td>
                    </tr>
                  )}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      </div>
      {pageSize && <Pagination page={page} pageCount={pageCount} onChange={setPage} />}
    </div>
  );
}
