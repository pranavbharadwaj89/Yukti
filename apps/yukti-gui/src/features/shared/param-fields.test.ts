import { describe, it, expect } from "vitest";
import { buildParamsFromFields } from "./param-fields";
import type { ActionSchema } from "@/services/types";

// Mirrors web.click's real shape (String + Number) plus a Boolean and an
// Object param to exercise every branch of the switch this function is
// built around — see MobileModule.cs/WebModule.cs's real ActionSchemas for
// the params this is modeled on.
const schema: ActionSchema = {
  actionName: "testAction",
  description: "",
  parameters: [
    { name: "selector", type: "String", required: true },
    { name: "timeoutMs", type: "Number", required: false },
    { name: "fullPage", type: "Boolean", required: false },
    { name: "headers", type: "Object", required: false },
  ],
};

describe("buildParamsFromFields", () => {
  it("returns an empty object when schema is undefined", () => {
    expect(buildParamsFromFields(undefined, { selector: "#x" })).toEqual({});
  });

  it("coerces Number fields to a real number, not a string", () => {
    const result = buildParamsFromFields(schema, { selector: "#x", timeoutMs: "8000" });
    expect(result.timeoutMs).toBe(8000);
    expect(typeof result.timeoutMs).toBe("number");
  });

  it("coerces Boolean fields from the checkbox's boolean value", () => {
    const result = buildParamsFromFields(schema, { selector: "#x", fullPage: true });
    expect(result.fullPage).toBe(true);
  });

  it("parses Object/Array fields as JSON", () => {
    const result = buildParamsFromFields(schema, { selector: "#x", headers: '{"Accept":"json"}' });
    expect(result.headers).toEqual({ Accept: "json" });
  });

  it("throws a descriptive error for malformed JSON in an Object field", () => {
    expect(() => buildParamsFromFields(schema, { selector: "#x", headers: "{not json" })).toThrow(
      '"headers" must be valid JSON.',
    );
  });

  it("skips fields that are undefined or an empty string", () => {
    const result = buildParamsFromFields(schema, { selector: "#x", timeoutMs: "" });
    expect(result).toEqual({ selector: "#x" });
  });

  it("passes String fields through as-is", () => {
    const result = buildParamsFromFields(schema, { selector: "#submit-button" });
    expect(result).toEqual({ selector: "#submit-button" });
  });
});
