import { getActionParams } from "@/services/api-client";
import { Input, Textarea } from "@/components/ui/primitives";
import { Checkbox } from "@/components/ui/form-controls";

export type FieldValue = string | boolean;

// Extracted from module-test-form.tsx (originally inline there only) —
// the exact same schema-driven param-building logic Tests tabs already
// use, now shared with Flow Authoring's Add-step dialog so a user never
// hand-writes JSON for a step whose param shape the backend already
// describes via GET /api/v1/modules.
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
      default:
        params[p.name] = String(raw);
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
  if (!schema) return null;

  function setField(name: string, value: FieldValue) {
    onChange({ ...values, [name]: value });
  }

  return (
    <div className="flex flex-col gap-3">
      {schema.parameters.map((p) => (
        <div key={p.name} className="flex flex-col gap-1">
          <label className="text-body-sm text-ink-dim">
            {p.name}
            {p.required ? "*" : ""}
            {p.description && <span className="ml-2 text-caption text-ink-dim/70">{p.description}</span>}
          </label>
          {p.type === "Boolean" ? (
            <Checkbox checked={values[p.name] === true} onChange={(checked) => setField(p.name, checked)} />
          ) : p.type === "Object" || p.type === "Array" ? (
            <Textarea
              rows={3}
              placeholder={p.type === "Array" ? "[...]" : "{...}"}
              value={(values[p.name] as string) ?? ""}
              onChange={(e) => setField(p.name, e.target.value)}
            />
          ) : (
            <Input
              type={p.type === "Number" ? "number" : "text"}
              value={(values[p.name] as string) ?? ""}
              onChange={(e) => setField(p.name, e.target.value)}
            />
          )}
        </div>
      ))}
    </div>
  );
}
