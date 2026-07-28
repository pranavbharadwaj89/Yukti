import { Fragment, type ReactNode } from "react";
import { Link, type LinkComponentProps } from "@tanstack/react-router";
import { ChevronLeft, ChevronRight } from "lucide-react";

// UI_Component_Spec.md Part 2 §6 (Tabs), §10 (Breadcrumb), §11 (Pagination).

export interface TabItem {
  id: string;
  label: string;
  disabled?: boolean;
}

export function Tabs({
  tabs,
  active,
  onChange,
}: {
  tabs: TabItem[];
  active: string;
  onChange: (id: string) => void;
}) {
  return (
    <div role="tablist" className="flex border-b-2 border-border">
      {tabs.map((tab) => (
        <button
          key={tab.id}
          type="button"
          role="tab"
          aria-selected={tab.id === active}
          disabled={tab.disabled}
          onClick={() => onChange(tab.id)}
          className={`relative px-4 py-3 text-body font-medium transition-colors disabled:cursor-not-allowed disabled:text-ink-dim/50 ${
            tab.id === active ? "text-accent" : "text-ink-dim hover:text-ink"
          }`}
        >
          {tab.label}
          {tab.id === active && <span className="absolute inset-x-0 -bottom-0.5 h-0.5 bg-accent" />}
        </button>
      ))}
    </div>
  );
}

export interface Crumb {
  label: string;
  to?: LinkComponentProps["to"];
}

export function Breadcrumb({ items }: { items: Crumb[] }) {
  return (
    <nav aria-label="Breadcrumb" className="flex items-center text-body">
      {items.map((item, i) => (
        <Fragment key={i}>
          {i > 0 && <span className="px-2 select-none text-ink-dim">/</span>}
          {item.to && i < items.length - 1 ? (
            <Link to={item.to} className="text-accent hover:underline">
              {item.label}
            </Link>
          ) : (
            <span className="font-medium text-ink">{item.label}</span>
          )}
        </Fragment>
      ))}
    </nav>
  );
}

function pageButtonClass(active: boolean, disabled: boolean) {
  if (disabled) return "border-border text-ink-dim/40 cursor-not-allowed";
  if (active) return "border-accent bg-accent text-accent-ink font-medium";
  return "border-border text-ink hover:border-accent hover:text-accent";
}

export function Pagination({
  page,
  pageCount,
  onChange,
}: {
  page: number;
  pageCount: number;
  onChange: (page: number) => void;
}) {
  if (pageCount <= 1) return null;
  const pages = Array.from({ length: pageCount }, (_, i) => i + 1);
  const windowed = pages.filter((p) => p === 1 || p === pageCount || Math.abs(p - page) <= 1);

  const items: ReactNode[] = [];
  let prev = 0;
  for (const p of windowed) {
    if (prev && p - prev > 1) {
      items.push(
        <span key={`e${p}`} className="flex h-9 w-9 items-center justify-center text-ink-dim">
          …
        </span>,
      );
    }
    items.push(
      <button
        key={p}
        type="button"
        onClick={() => onChange(p)}
        className={`flex h-9 min-w-9 items-center justify-center rounded-md border text-body transition-colors ${pageButtonClass(p === page, false)}`}
      >
        {p}
      </button>,
    );
    prev = p;
  }

  return (
    <nav aria-label="Pagination" className="flex items-center justify-center gap-1">
      <button
        type="button"
        aria-label="Previous page"
        disabled={page === 1}
        onClick={() => onChange(page - 1)}
        className={`flex h-9 w-9 items-center justify-center rounded-md border transition-colors ${pageButtonClass(false, page === 1)}`}
      >
        <ChevronLeft size={16} />
      </button>
      {items}
      <button
        type="button"
        aria-label="Next page"
        disabled={page === pageCount}
        onClick={() => onChange(page + 1)}
        className={`flex h-9 w-9 items-center justify-center rounded-md border transition-colors ${pageButtonClass(false, page === pageCount)}`}
      >
        <ChevronRight size={16} />
      </button>
    </nav>
  );
}
