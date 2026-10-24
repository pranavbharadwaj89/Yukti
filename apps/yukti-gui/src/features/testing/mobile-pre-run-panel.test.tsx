import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { emptyMobileDeviceConfig, MobilePreRunPanel } from "./mobile-pre-run-panel";

// DISC-4: every <label> here used to render with no htmlFor/id pairing, so
// clicking a label or using screen-reader "next form field" navigation did
// nothing useful. getByLabelText only resolves when the association is real.
describe("MobilePreRunPanel", () => {
  it("associates every label with its input via htmlFor/id", () => {
    render(<MobilePreRunPanel config={emptyMobileDeviceConfig} onChange={vi.fn()} />);

    expect(screen.getByLabelText("Platform name*")).toBeInTheDocument();
    expect(screen.getByLabelText("Device name*")).toBeInTheDocument();
    expect(screen.getByLabelText("Automation name*")).toBeInTheDocument();
    expect(screen.getByLabelText("Appium URL")).toBeInTheDocument();
    expect(screen.getByLabelText("App")).toBeInTheDocument();
  });

  it("clicking a label focuses its associated input", async () => {
    const user = userEvent.setup();
    render(<MobilePreRunPanel config={emptyMobileDeviceConfig} onChange={vi.fn()} />);

    await user.click(screen.getByText("Device name*"));

    expect(screen.getByLabelText("Device name*")).toHaveFocus();
  });
});
