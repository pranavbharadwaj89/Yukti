import { Plus, Trash2 } from "lucide-react";
import { Input } from "@/components/ui/primitives";
import { Select, type SelectOption } from "@/components/ui/form-controls";

export interface RuleRow {
  id: string;
  name: string;
  pattern: string;
  maxAllowed: string;
  severity: string;
}

const SEVERITY_OPTIONS: SelectOption[] = [
  { value: "info", label: "Info" },
  { value: "warning", label: "Warning" },
  { value: "error", label: "Error" },
];

let rowCounter = 0;
export function newRuleRow(): RuleRow {
  rowCounter += 1;
  return { id: `rule-${rowCounter}`, name: "", pattern: "", maxAllowed: "0", severity: "error" };
}

// Row editor for LogsModule's checkRules `rules` param — each row is
// {name, pattern, maxAllowed, severity} (src/Yukti.Infrastructure.InMemory/
// Modules/LogsModule.cs). Structurally mirrors api-studio's KeyValueEditor
// (same add/update/remove pattern) but with the rule-specific field set.
export function RulesEditor({ rows, onChange }: { rows: RuleRow[]; onChange: (rows: RuleRow[]) => void }) {
  function updateRow(id: string, patch: Partial<RuleRow>) {
    onChange(rows.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  }

  function removeRow(id: string) {
    onChange(rows.filter((r) => r.id !== id));
  }

  function addRow() {
    onChange([...rows, newRuleRow()]);
  }

  return (
    <div className="flex flex-col gap-2">
      {rows.map((row) => (
        <div key={row.id} className="flex items-center gap-2">
          <Input
            className="w-32"
            placeholder="Rule name"
            value={row.name}
            onChange={(e) => updateRow(row.id, { name: e.target.value })}
          />
          <Input
            className="flex-1 font-mono"
            placeholder="Regex pattern"
            value={row.pattern}
            onChange={(e) => updateRow(row.id, { pattern: e.target.value })}
          />
          <Input
            className="w-24"
            type="number"
            min={0}
            placeholder="Max allowed"
            value={row.maxAllowed}
            onChange={(e) => updateRow(row.id, { maxAllowed: e.target.value })}
          />
          <div className="w-32">
            <Select value={row.severity} options={SEVERITY_OPTIONS} onChange={(severity) => updateRow(row.id, { severity })} />
          </div>
          <button
            type="button"
            aria-label="Remove rule"
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
        <Plus size={14} /> Add rule
      </button>
    </div>
  );
}
