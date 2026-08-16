import { useState } from "react";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Select, Checkbox } from "./form-controls";

const OPTIONS = [
  { value: "a", label: "Alpha" },
  { value: "b", label: "Bravo" },
  { value: "c", label: "Charlie" },
];

function ControlledSelect() {
  const [value, setValue] = useState<"a" | "b" | "c" | "">("");
  return <Select value={value} options={OPTIONS} onChange={setValue} placeholder="Pick one…" />;
}

describe("Select", () => {
  it("shows the placeholder when nothing is selected", () => {
    render(<ControlledSelect />);
    expect(screen.getByRole("button")).toHaveTextContent("Pick one…");
  });

  it("opens the listbox and selects an option on click", async () => {
    const user = userEvent.setup();
    render(<ControlledSelect />);
    await user.click(screen.getByRole("button"));
    await user.click(screen.getByRole("option", { name: "Bravo" }));
    expect(screen.getByRole("button")).toHaveTextContent("Bravo");
  });

  it("supports full keyboard navigation: open, arrow down, Enter to select", async () => {
    const user = userEvent.setup();
    render(<ControlledSelect />);
    const trigger = screen.getByRole("button");
    trigger.focus();
    await user.keyboard("{ArrowDown}"); // opens, highlights first option
    expect(screen.getByRole("listbox")).toBeInTheDocument();
    await user.keyboard("{ArrowDown}"); // move to second option
    await user.keyboard("{Enter}");
    expect(trigger).toHaveTextContent("Bravo");
    expect(trigger).toHaveFocus();
  });

  it("closes the listbox on Escape and returns focus to the trigger", async () => {
    const user = userEvent.setup();
    render(<ControlledSelect />);
    const trigger = screen.getByRole("button");
    await user.click(trigger);
    expect(screen.getByRole("listbox")).toBeInTheDocument();
    await user.keyboard("{Escape}");
    expect(screen.queryByRole("listbox")).not.toBeInTheDocument();
    expect(trigger).toHaveFocus();
  });
});

describe("Checkbox", () => {
  it("calls onChange with the toggled value when clicked", async () => {
    const user = userEvent.setup();
    let checked = false;
    render(<Checkbox checked={checked} onChange={(v) => (checked = v)} label="Enable thing" />);
    await user.click(screen.getByRole("checkbox"));
    expect(checked).toBe(true);
  });
});
