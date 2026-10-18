/// <reference types="vitest/config" />
import path from "node:path";
import { configDefaults, defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Routes are defined code-based (src/app/router.tsx), not file-based, so
// the file-based @tanstack/router-plugin codegen isn't used here.
// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { "@": path.resolve(__dirname, "./src") },
  },
  server: {
    port: 5173,
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: "./src/test-setup.ts",
    // ADR-0005: e2e/ holds Playwright specs (`npm run test:e2e`), which need the real running
    // stack — never Vitest unit tests. Excluded, not include-narrowed, so eslint-rules/ tests run.
    exclude: [...configDefaults.exclude, "e2e/**"],
  },
});
