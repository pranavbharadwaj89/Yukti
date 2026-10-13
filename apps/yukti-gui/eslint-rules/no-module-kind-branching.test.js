import { RuleTester } from "eslint";
import rule from "./no-module-kind-branching.js";

// D4-T2: proves the custom lint rule actually catches a planted violation
// (RuleTester, not oxlint — oxlint can't run a JS-authored rule at all,
// which is exactly why this rule is wired through a separate ESLint pass;
// see eslint.config.js). RuleTester.run() drives vitest's global
// describe/it itself (globals: true in vite.config.ts) — it must be
// called at the top level, not nested inside another it().
const ruleTester = new RuleTester();

ruleTester.run("no-module-kind-branching", rule, {
  valid: [
    // The sanctioned pattern: a plain object lookup into the registry.
    'MODULE_EXTRAS[moduleKind];',
    'const extra = MODULE_EXTRAS[moduleKind]?.PreRunPanel;',
    // Comparing some other identifier named "kind" is not this rule's
    // concern — it only targets the literal name "moduleKind".
    'if (kind === "web") {}',
    // Comparing moduleKind against something that isn't a string literal
    // (e.g. another variable) isn't a hardcoded branch.
    'if (moduleKind === otherModuleKind) {}',
  ],
  invalid: [
    {
      code: 'if (moduleKind === "web") {}',
      errors: [{ messageId: "comparison" }],
    },
    {
      code: 'if (moduleKind !== "web") {}',
      errors: [{ messageId: "comparison" }],
    },
    {
      code: 'const isWeb = step.moduleKind === "web";',
      errors: [{ messageId: "comparison" }],
    },
    {
      code: 'switch (moduleKind) { case "mobile": break; }',
      errors: [{ messageId: "switchStatement" }],
    },
  ],
});
