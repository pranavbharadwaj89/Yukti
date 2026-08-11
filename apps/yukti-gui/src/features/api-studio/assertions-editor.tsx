import { Plus, Trash2 } from "lucide-react";
import { Input } from "@/components/ui/primitives";
import { Select } from "@/components/ui/form-controls";
import { CodeEditor } from "@/components/ui/code-editor";

export type AssertionType = "status" | "pathEquals" | "pathContains" | "pathExists" | "headerExists" | "cookieExists" | "schema";

const ASSERTION_TYPES: { value: AssertionType; label: string }[] = [
  { value: "status", label: "Status code" },
  { value: "pathEquals", label: "Path equals" },
  { value: "pathContains", label: "Path contains" },
  { value: "pathExists", label: "Path exists" },
  { value: "headerExists", label: "Header exists" },
  { value: "cookieExists", label: "Cookie exists" },
  { value: "schema", label: "Body matches schema" },
];

// One draft shape covers all seven wire shapes (Yukti.Domain.Assertions,
// wired into ApiModule this session) — only the fields relevant to the
// selected `type` are shown, the rest are carried but unused until the
// type changes back.
export interface AssertionDraft {
  id: string;
  type: AssertionType;
  expectedStatus: string;
  path: string;
  equals: string;
  contains: string;
  header: string;
  cookie: string;
  schema: string;
}

let draftCounter = 0;
export function newAssertionDraft(type: AssertionType = "status"): AssertionDraft {
  draftCounter += 1;
  return {
    id: `assertion-${draftCounter}`,
    type,
    expectedStatus: type === "status" ? "200" : "",
    path: "",
    equals: "",
    contains: "",
    header: "",
    cookie: "",
    schema: "",
  };
}

// Parses a user-typed "equals"/"contains" value as JSON when possible (so
// `42`, `true`, `"a string"`, `{"a":1}` all work as the author intends),
// falling back to the raw string — same dual-mode convention ApiModule
// itself uses for the request body.
function parseLoose(text: string): unknown {
  if (text.trim() === "") return "";
  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
}

// Builds the exact `assert` array wire shape ApiModule.Run expects. Drafts
// missing their required field for the selected type are skipped rather
// than sent malformed — the backend would reject them with a clear error,
// but catching it client-side avoids a round trip for an obviously
// incomplete row (e.g. mid-edit).
export function buildAssertPayload(drafts: AssertionDraft[]): Record<string, unknown>[] {
  const result: Record<string, unknown>[] = [];
  for (const d of drafts) {
    switch (d.type) {
      case "status": {
        const n = Number(d.expectedStatus);
        if (!Number.isNaN(n) && d.expectedStatus.trim() !== "") result.push({ type: "status", expectedStatus: n });
        break;
      }
      case "pathEquals":
        if (d.path.trim() !== "") result.push({ type: "pathEquals", path: d.path, equals: parseLoose(d.equals) });
        break;
      case "pathContains":
        if (d.path.trim() !== "" && d.contains.trim() !== "")
          result.push({ type: "pathContains", path: d.path, contains: parseLoose(d.contains) });
        break;
      case "pathExists":
        if (d.path.trim() !== "") result.push({ type: "pathExists", path: d.path });
        break;
      case "headerExists":
        if (d.header.trim() !== "") result.push({ type: "headerExists", header: d.header });
        break;
      case "cookieExists":
        if (d.cookie.trim() !== "") result.push({ type: "cookieExists", cookie: d.cookie });
        break;
      case "schema":
        if (d.schema.trim() !== "") {
          try {
            result.push({ type: "schema", schema: JSON.parse(d.schema) });
          } catch {
            // invalid JSON schema draft — skip rather than send garbage
          }
        }
        break;
    }
  }
  return result;
}

export function AssertionsEditor({ drafts, onChange }: { drafts: AssertionDraft[]; onChange: (drafts: AssertionDraft[]) => void }) {
  function updateDraft(id: string, patch: Partial<AssertionDraft>) {
    onChange(drafts.map((d) => (d.id === id ? { ...d, ...patch } : d)));
  }

  function removeDraft(id: string) {
    onChange(drafts.filter((d) => d.id !== id));
  }

  function addDraft() {
    onChange([...drafts, newAssertionDraft()]);
  }

  return (
    <div className="flex flex-col gap-3">
      {drafts.map((d) => (
        <div key={d.id} className="flex flex-col gap-2 rounded-md border border-border p-3">
          <div className="flex items-center gap-2">
            <div className="w-48">
              <Select value={d.type} options={ASSERTION_TYPES} onChange={(type) => updateDraft(d.id, { type: type as AssertionType })} />
            </div>
            <button
              type="button"
              aria-label="Remove assertion"
              onClick={() => removeDraft(d.id)}
              className="ml-auto flex h-9 w-9 flex-shrink-0 items-center justify-center rounded-md text-ink-dim hover:bg-surface-2 hover:text-danger"
            >
              <Trash2 size={16} />
            </button>
          </div>

          {d.type === "status" && (
            <Input
              type="number"
              placeholder="Expected status, e.g. 200"
              value={d.expectedStatus}
              onChange={(e) => updateDraft(d.id, { expectedStatus: e.target.value })}
            />
          )}

          {(d.type === "pathEquals" || d.type === "pathContains" || d.type === "pathExists") && (
            <Input
              placeholder="Path, e.g. data.items[0].id"
              value={d.path}
              onChange={(e) => updateDraft(d.id, { path: e.target.value })}
            />
          )}
          {d.type === "pathEquals" && (
            <Input
              placeholder='Expected value, e.g. 42 or "active"'
              value={d.equals}
              onChange={(e) => updateDraft(d.id, { equals: e.target.value })}
            />
          )}
          {d.type === "pathContains" && (
            <Input
              placeholder="Expected fragment"
              value={d.contains}
              onChange={(e) => updateDraft(d.id, { contains: e.target.value })}
            />
          )}

          {d.type === "headerExists" && (
            <Input
              placeholder="Header name, e.g. X-Request-Id"
              value={d.header}
              onChange={(e) => updateDraft(d.id, { header: e.target.value })}
            />
          )}

          {d.type === "cookieExists" && (
            <Input
              placeholder="Cookie name, e.g. session"
              value={d.cookie}
              onChange={(e) => updateDraft(d.id, { cookie: e.target.value })}
            />
          )}

          {d.type === "schema" && (
            <CodeEditor
              value={d.schema}
              onChange={(schema) => updateDraft(d.id, { schema })}
              language="json"
              height={160}
            />
          )}
        </div>
      ))}
      <button
        type="button"
        onClick={addDraft}
        className="flex w-fit items-center gap-1.5 rounded-md px-2 py-1.5 text-body-sm text-ink-dim hover:bg-surface-2 hover:text-ink"
      >
        <Plus size={14} /> Add assertion
      </button>
    </div>
  );
}
