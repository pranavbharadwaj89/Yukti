import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { StatusPill } from "./primitives";

describe("StatusPill", () => {
  it("renders the status text", () => {
    render(<StatusPill status="Passed" />);
    expect(screen.getByText("Passed")).toBeInTheDocument();
  });

  it("applies the success style for a Passed status", () => {
    render(<StatusPill status="Passed" />);
    expect(screen.getByText("Passed")).toHaveClass("bg-success-soft", "text-success");
  });

  it("applies the danger style for a Failed status", () => {
    render(<StatusPill status="Failed" />);
    expect(screen.getByText("Failed")).toHaveClass("bg-danger-soft", "text-danger");
  });

  it("falls back to the neutral style for an unrecognized status", () => {
    render(<StatusPill status="SomeUnknownStatus" />);
    const pill = screen.getByText("SomeUnknownStatus");
    expect(pill).toBeInTheDocument();
    expect(pill).toHaveClass("bg-surface-2", "text-ink-dim");
  });
});
