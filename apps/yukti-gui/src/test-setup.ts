import "@testing-library/jest-dom/vitest";

// React Flow (used by WorkflowCanvas) measures node/viewport size via
// ResizeObserver, which jsdom doesn't implement. A minimal no-op stub is
// sufficient for tests that render the canvas without asserting on layout
// measurements themselves.
if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}
