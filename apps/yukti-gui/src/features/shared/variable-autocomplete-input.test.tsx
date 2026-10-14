import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { VariableAutocompleteInput, findUnresolvedVariableErrors } from "./variable-autocomplete-input";

function Harness({
  initial = "",
  availableBindings,
  fieldsByBinding,
}: {
  initial?: string;
  availableBindings: string[];
  fieldsByBinding?: Record<string, string[]>;
}) {
  const [value, setValue] = useState(initial);
  return (
    <VariableAutocompleteInput
      aria-label="url"
      stepName="Confirm order"
      value={value}
      onChange={setValue}
      availableBindings={availableBindings}
      fieldsByBinding={fieldsByBinding}
    />
  );
}

// D4-T6. "Never a later step's" is enforced by the caller (flow-detail-page
// only ever passes earlier steps' saveAs names — see its own test); this
// component's contract is simpler: it only ever suggests from whatever
// `availableBindings` it's given, nothing it invents.
describe("VariableAutocompleteInput", () => {
  it("suggests saveAs names after typing {{vars.", async () => {
    const user = userEvent.setup();
    render(<Harness availableBindings={["order", "login"]} />);
    const input = screen.getByLabelText("url");

    await user.type(input, "{{{{vars.");

    expect(screen.getByRole("listbox")).toBeInTheDocument();
    expect(screen.getByText("vars.order")).toBeInTheDocument();
    expect(screen.getByText("vars.login")).toBeInTheDocument();
  });

  it("narrows suggestions as more is typed, and offers real field paths from the last run once a binding is chosen", async () => {
    const user = userEvent.setup();
    render(<Harness availableBindings={["order", "login"]} fieldsByBinding={{ order: ["id", "status"] }} />);
    const input = screen.getByLabelText("url");

    await user.type(input, "{{{{vars.ord");
    expect(screen.queryByText("vars.login")).not.toBeInTheDocument();
    expect(screen.getByText("vars.order")).toBeInTheDocument();

    await user.type(input, ".");
    expect(screen.getByText("vars.order.id")).toBeInTheDocument();
    expect(screen.getByText("vars.order.status")).toBeInTheDocument();
  });

  it("is fully keyboard-operable: arrow keys move selection, Enter accepts, Escape dismisses", async () => {
    const user = userEvent.setup();
    render(<Harness availableBindings={["order", "login"]} />);
    const input = screen.getByLabelText("url");

    await user.type(input, "{{{{vars.");
    await user.keyboard("{ArrowDown}"); // order -> login
    await user.keyboard("{Enter}");

    expect(input).toHaveValue("{{vars.login}}");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();

    await user.clear(input);
    await user.type(input, "{{{{vars.");
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
  });

  it("announces the active suggestion to screen readers via a real ARIA 1.2 combobox, not just a styled list", async () => {
    const user = userEvent.setup();
    render(<Harness availableBindings={["order"]} />);
    const input = screen.getByLabelText("url");

    await user.type(input, "{{{{vars.");

    expect(input).toHaveAttribute("role", "combobox");
    expect(input).toHaveAttribute("aria-expanded", "true");
    expect(input).toHaveAttribute("aria-autocomplete", "list");
    const activeDescendant = input.getAttribute("aria-activedescendant");
    expect(activeDescendant).toBeTruthy();
    expect(document.getElementById(activeDescendant!)).toHaveTextContent("vars.order");
  });

  it("flags a reference to a binding that doesn't exist, with the exact wording Flow.Publish itself uses", () => {
    render(<Harness initial="{{vars.nope}}" availableBindings={["order"]} />);
    const input = screen.getByLabelText("url");

    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(screen.getByRole("alert")).toHaveTextContent("Step 'Confirm order' references undeclared variable 'nope'.");
  });

  it("does not flag a reference to a real binding", () => {
    render(<Harness initial="{{vars.order.id}}" availableBindings={["order"]} />);
    expect(screen.getByLabelText("url")).toHaveAttribute("aria-invalid", "false");
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});

describe("findUnresolvedVariableErrors", () => {
  it("matches Flow.Publish's own unresolved_variable message exactly", () => {
    const errors = findUnresolvedVariableErrors("{{vars.ghost}} and {{vars.order.id}}", "Step A", ["order"]);
    expect(errors).toEqual(["Step 'Step A' references undeclared variable 'ghost'."]);
  });

  it("reports each distinct unresolved binding once, even if referenced twice", () => {
    const errors = findUnresolvedVariableErrors("{{vars.ghost}} {{vars.ghost.x}}", "Step A", []);
    expect(errors).toHaveLength(1);
  });
});
