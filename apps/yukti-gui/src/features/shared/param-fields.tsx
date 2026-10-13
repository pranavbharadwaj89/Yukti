import { useEffect } from "react";
import { getActionParams } from "@/services/api-client";
import { Input, Textarea } from "@/components/ui/primitives";
import { Checkbox } from "@/components/ui/form-controls";

export type FieldValue = string | boolean;

const KNOWN_PARAM_TYPES = new Set(["String", "Number", "Boolean", "Object", "Array"]);

// Extracted from module-test-form.tsx (originally inline there only) —
// the exact same schema-driven param-building logic Tests tabs already
// use, now shared with Flow Authoring's Add-step dialog so a user never
// hand-writes JSON for a step whose param shape the backend already
// describes via GET /api/v1/modules. D4-T2: this is Yukti's
// SchemaDrivenParamEditor — every module's step form is built from here,
// with zero module-kind branching (see module-extras.ts's registry for
// the one legitimate per-module exception, a pre-run config panel).
export function buildParamsFromFields(
  schema: ReturnType<typeof getActionParams>,
  values: Record<string, FieldValue>,
): Record<string, unknown> {
  if (!schema) return {};
  const params: Record<string, unknown> = {};
  for (const p of schema.parameters) {
    const raw = values[p.name];
    if (raw === undefined || raw === "") continue;
    switch (p.type) {
      case "Number": {
        const n = Number(raw);
        if (!Number.isNaN(n)) params[p.name] = n;
        break;
      }
      case "Boolean":
        params[p.name] = raw === true || raw === "true";
        break;
      case "Object":
      case "Array":
        try {
          params[p.name] = JSON.parse(String(raw));
        } catch {
          throw new Error(`"${p.name}" must be valid JSON.`);
        }
        break;
      case "String":
        params[p.name] = String(raw);
        break;
      default:
        // Unknown type: same raw-JSON path as Object/Array, since that's
        // what the editor falls back to rendering for it (see ParamFields
        // below) — never silently stringify something that was actually
        // meant to be structured data.
        try {
          params[p.name] = JSON.parse(String(raw));
        } catch {
          params[p.name] = String(raw);
        }
    }
  }
  return params;
}

export function ParamFields({
  schema,
  values,
  onChange,
}: {
  schema: ReturnType<typeof getActionParams>;
  values: Record<string, FieldValue>;
  onChange: (values: Record<string, FieldValue>) => void;
}) {
  // Pre-fill each parameter's declared default the first time this schema
  // is shown, without clobbering anything the user (or a loaded draft
  // step) already set. Centralized here, not in each caller, so every
  // module's form gets it for free.
  useEffect(() => {
    if (!schema) return;
    const withDefaults = schema.parameters.filter((p) => p.defaultValue !== undefined && values[p.name] === undefined);
    if (withDefaults.length === 0) return;
    const next = { ...values };
    for (const p of withDefaults) {
      next[p.name] = typeof p.defaultValue === "boolean" ? p.defaultValue : String(p.defaultValue);
    }
    onChange(next);
    // Re-run only when the schema itself changes (a different action
    // selected) — not on every value edit, which would fight the user.
  }, [schema]);

  if (!schema) return null;

  function setField(name: string, value: FieldValue) {
    onChange({ ...values, [name]: value });
  }

  return (
    <div className="flex flex-col gap-3">
      {schema.parameters.map((p) => {
        const isKnownType = KNOWN_PARAM_TYPES.has(p.type);
        return (
          <div key={p.name} className="flex flex-col gap-1">
            <label className="text-body-sm text-ink-dim">
              {p.name}
              {p.required ? "*" : ""}
              {p.description && <span className="ml-2 text-caption text-ink-dim/70">{p.description}</span>}
            </label>
            {!isKnownType && (
              <p className="text-caption text-warning" role="alert">
                Unknown parameter type &quot;{p.type}&quot; — edit as raw JSON.
              </p>
            )}
            {p.type === "Boolean" ? (
              <Checkbox ariaLabel={p.name} checked={values[p.name] === true} onChange={(checked) => setField(p.name, checked)} />
            ) : p.type === "Object" || p.type === "Array" || !isKnownType ? (
              <Textarea
                aria-label={p.name}
                rows={3}
                placeholder={p.type === "Array" ? "[...]" : "{...}"}
                value={(values[p.name] as string) ?? ""}
                onChange={(e) => setField(p.name, e.target.value)}
              />
            ) : (
              <Input
                aria-label={p.name}
                type={p.type === "Number" ? "number" : "text"}
                value={(values[p.name] as string) ?? ""}
                onChange={(e) => setField(p.name, e.target.value)}
              />
            )}
          </div>
        );
      })}
    </div>
  );
}

// D4-T2 names this component SchemaDrivenParamEditor; ParamFields is kept
// as the primary export since three existing call sites already import it
// by that name.
export { ParamFields as SchemaDrivenParamEditor };
