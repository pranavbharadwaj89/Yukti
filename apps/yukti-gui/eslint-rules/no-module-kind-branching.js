// D4-T2: Yukti's module contract (SUTRADHAR.md §2.1 — "the orchestrator,
// report and UI contain no module-specific branching") means a new module
// must work in the UI with zero UI code changes. A string comparison or
// switch against the step's moduleKind is exactly the kind of branch that
// silently breaks that promise — it compiles fine today and quietly stops
// working the day someone adds a module this rule never saw.
//
// Flags: `moduleKind === "web"` / `!==` / `==` / `!=`, and
// `switch (moduleKind) { case "web": ... }`. Does NOT flag a plain object
// lookup (`MODULE_EXTRAS[moduleKind]`) — that's the registry pattern this
// rule exists to push code towards, and the only sanctioned way to special
// -case one module (see src/features/shared/module-extras.ts).
const TARGET_NAMES = new Set(["moduleKind"]);

function isTargetNode(node) {
  if (!node) return false;
  if (node.type === "Identifier") return TARGET_NAMES.has(node.name);
  if (node.type === "MemberExpression" && !node.computed && node.property.type === "Identifier") {
    return TARGET_NAMES.has(node.property.name);
  }
  return false;
}

function isStringLiteralish(node) {
  return (
    (node.type === "Literal" && typeof node.value === "string") ||
    (node.type === "TemplateLiteral" && node.expressions.length === 0)
  );
}

/** @type {import('eslint').Rule.RuleModule} */
export default {
  meta: {
    type: "problem",
    docs: {
      description: "disallow branching UI logic on a module's kind; use the module-extras registry instead",
    },
    schema: [],
    messages: {
      comparison:
        "Don't branch on moduleKind with {{operator}} — add an entry to src/features/shared/module-extras.ts (MODULE_EXTRAS) instead, and look it up with a plain object access.",
      switchStatement:
        "Don't switch on moduleKind — add an entry to src/features/shared/module-extras.ts (MODULE_EXTRAS) instead, and look it up with a plain object access.",
    },
  },
  create(context) {
    return {
      BinaryExpression(node) {
        if (!["===", "!==", "==", "!="].includes(node.operator)) return;
        const targetSide = isTargetNode(node.left) ? node.left : isTargetNode(node.right) ? node.right : null;
        const literalSide = targetSide === node.left ? node.right : node.left;
        if (targetSide && isStringLiteralish(literalSide)) {
          context.report({ node, messageId: "comparison", data: { operator: node.operator } });
        }
      },
      SwitchStatement(node) {
        if (isTargetNode(node.discriminant)) {
          context.report({ node, messageId: "switchStatement" });
        }
      },
    };
  },
};
