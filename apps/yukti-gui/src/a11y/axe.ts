import axe from "axe-core";

/** Runs axe-core (WCAG 2.0/2.1 A + AA rules) against a rendered container and
 *  returns human-readable violations. color-contrast is skipped because jsdom
 *  has no layout/CSS engine; contrast is audited from the tokens in
 *  contrast.test.ts instead. */
export async function wcagViolations(container: Element): Promise<string[]> {
  const results = await axe.run(container, {
    runOnly: { type: "tag", values: ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] },
    rules: { "color-contrast": { enabled: false }, region: { enabled: false } },
  });
  return results.violations.map((v) => `${v.id}: ${v.help} — ${v.nodes.map((n) => n.target.join(" ")).join(", ")}`);
}
