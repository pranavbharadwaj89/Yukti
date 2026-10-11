import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { MarketplacePage } from "./marketplace-page";

describe("MarketplacePage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    const { container } = render(<MarketplacePage />);
    await screen.findByText("Module Marketplace");
    expect(await wcagViolations(container)).toEqual([]);
  });
});
