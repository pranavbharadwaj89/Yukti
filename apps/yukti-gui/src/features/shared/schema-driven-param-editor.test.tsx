import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { SchemaDrivenParamEditor, type FieldValue } from "./param-fields";
import type { ActionSchema } from "@/services/types";

// Mirrors tests/Yukti.Orchestration.Tests/ModuleRegistryTests.cs's
// SyntheticXyzModule exactly (D2-T1's "a brand-new module needs zero other
// code changes" proof, now exercised on the frontend too): a required
// String, a Number with a real backend-declared default, and a required
// Object. Zero component code here or in param-fields.tsx knows this
// module or action exists — only its schema, fetched the same way any
// other module's would be via GET /api/v1/modules.
const syntheticSchema: ActionSchema = {
  actionName: "doSomethingNovel",
  description: "A throwaway action proving a brand-new module needs zero core changes.",
  parameters: [
    { name: "targetId", type: "String", required: true, description: "Id of the thing this action targets." },
    { name: "retryCount", type: "Number", required: false, defaultValue: 3, description: "How many times to retry internally before giving up." },
    { name: "payload", type: "Object", required: true, description: "Arbitrary structured input for the action." },
  ],
};

function Harness({ schema, onValues }: { schema: ActionSchema; onValues?: (v: Record<string, FieldValue>) => void }) {
  const [values, setValues] = useState<Record<string, FieldValue>>({});
  return (
    <SchemaDrivenParamEditor
      schema={schema}
      values={values}
      onChange={(v) => {
        setValues(v);
        onValues?.(v);
      }}
    />
  );
}

describe("SchemaDrivenParamEditor — synthetic module (D4-T2)", () => {
  it("renders the synthetic module's required fields, marking them, with zero UI code for this module", () => {
    render(<Harness schema={syntheticSchema} />);
    expect(screen.getByText(/targetId\*/)).toBeInTheDocument();
    expect(screen.getByText(/payload\*/)).toBeInTheDocument();
    // retryCount isn't required, so no asterisk on it specifically — the
    // regex anchors on the exact field name immediately before the *.
    expect(screen.queryByText(/^retryCount\*/)).not.toBeInTheDocument();
  });

  it("pre-fills the Number parameter's declared default (3), without being asked to", async () => {
    const onValues = vi.fn();
    render(<Harness schema={syntheticSchema} onValues={onValues} />);
    const retryCount = (await screen.findByLabelText("retryCount")) as HTMLInputElement;
    expect(retryCount.value).toBe("3");
  });

  it("renders the Object parameter as a JSON field, not a plain text input", () => {
    render(<Harness schema={syntheticSchema} />);
    const payload = screen.getByLabelText("payload");
    expect(payload.tagName).toBe("TEXTAREA");
  });

  it("still enforces the String parameter as a plain text field", () => {
    render(<Harness schema={syntheticSchema} />);
    const targetId = screen.getByLabelText("targetId");
    expect(targetId.tagName).toBe("INPUT");
  });

  it("falls back to a JSON field with a visible warning for a parameter type it doesn't recognize", () => {
    const schemaWithUnknownType: ActionSchema = {
      ...syntheticSchema,
      parameters: [...syntheticSchema.parameters, { name: "secretRef", type: "VaultSecretRef", required: false }],
    };
    render(<Harness schema={schemaWithUnknownType} />);
    expect(screen.getByLabelText("secretRef").tagName).toBe("TEXTAREA");
    expect(screen.getByRole("alert")).toHaveTextContent(/unknown parameter type.*VaultSecretRef/i);
  });

  it("still lets the user type into a field after the default pre-fill runs", async () => {
    const user = userEvent.setup();
    render(<Harness schema={syntheticSchema} />);
    const targetId = screen.getByLabelText("targetId");
    await user.type(targetId, "abc-123");
    expect((targetId as HTMLInputElement).value).toBe("abc-123");
  });
});
