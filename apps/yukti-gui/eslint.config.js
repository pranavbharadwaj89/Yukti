// oxlint (npm run lint's primary linter) can't run custom JS-authored
// rules — it's a fixed Rust rule set. This narrow ESLint config exists
// only to run src/eslint-rules's local rules (currently
// no-module-kind-branching, D4-T2) on top of oxlint, not to replace it.
import tsParser from "@typescript-eslint/parser";
import noModuleKindBranching from "./eslint-rules/no-module-kind-branching.js";

export default [
  {
    files: ["src/**/*.{ts,tsx}"],
    ignores: ["src/api/generated/**", "**/*.test.{ts,tsx}"],
    languageOptions: {
      parser: tsParser,
      parserOptions: { ecmaFeatures: { jsx: true } },
    },
    plugins: {
      local: { rules: { "no-module-kind-branching": noModuleKindBranching } },
    },
    rules: {
      "local/no-module-kind-branching": "error",
    },
  },
];
