import { Plus, Trash2 } from "lucide-react";
import { Input } from "@/components/ui/primitives";
import { Checkbox } from "@/components/ui/form-controls";

export interface KeyValueRow {
  id: string;
  key: string;
  value: string;
  enabled: boolean;
}

let rowCounter = 0;
export function newKeyValueRow(): KeyValueRow {
  rowCounter += 1;
  return { id: `row-${rowCounter}`, key: "", value: "", enabled: true };
}

// Shared row editor for Headers and Query Params — both are flat
// string->string maps on the wire (ApiModule's `headers`/`queryParams`
// params), so one component covers both tabs.
export function KeyValueEditor({
  rows,
  onChange,
  keyPlaceholder = "Key",
  valuePlaceholder = "Value",
}: {
  rows: KeyValueRow[];
  onChange: (rows: KeyValueRow[]) => void;
  keyPlaceholder?: string;
  valuePlaceholder?: string;
}) {
  function updateRow(id: string, patch: Partial<KeyValueRow>) {
    onChange(rows.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  }

  function removeRow(id: string) {
    onChange(rows.filter((r) => r.id !== id));
  }

  function addRow() {
    onChange([...rows, newKeyValueRow()]);
  }

  return (
    <div className="flex flex-col gap-2">
      {rows.map((row) => (
        <div key={row.id} className="flex items-center gap-2">
          <Checkbox checked={row.enabled} onChange={(enabled) => updateRow(row.id, { enabled })} />
          <Input
            className="flex-1"
            placeholder={keyPlaceholder}
            value={row.key}
            onChange={(e) => updateRow(row.id, { key: e.target.value })}
          />
          <Input
            className="flex-1"
            placeholder={valuePlaceholder}
            value={row.value}
            onChange={(e) => updateRow(row.id, { value: e.target.value })}
          />
          <button
            type="button"
            aria-label="Remove row"
            onClick={() => removeRow(row.id)}
            className="flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-md text-ink-dim hover:bg-surface-2 hover:text-danger"
          >
            <Trash2 size={16} />
          </button>
        </div>
      ))}
      <button
        type="button"
        onClick={addRow}
        className="flex w-fit items-center gap-1.5 rounded-md px-2 py-1.5 text-body-sm text-ink-dim hover:bg-surface-2 hover:text-ink"
      >
        <Plus size={14} /> Add
      </button>
    </div>
  );
}
