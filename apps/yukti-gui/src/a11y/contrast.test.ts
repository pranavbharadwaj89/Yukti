/// <reference types="node" />
import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

const css = readFileSync(resolve(process.cwd(), "src/index.css"), "utf8");

// WCAG 2.1 AA contrast audit over the design tokens in index.css, for every
// theme block. Text pairs need 4.5:1 (SC 1.4.3); non-text UI (borders of
// inputs, focus ring) needs 3:1 (SC 1.4.11).

function blockVars(selectorPattern: RegExp): Record<string, string> {
  const vars: Record<string, string> = {};
  const re = new RegExp("(?:" + selectorPattern.source + ")" + String.raw`\s*\{([^}]*)\}`, "g");
  for (const m of css.matchAll(re)) {
    for (const v of m[1].matchAll(/--yukti-([\w-]+):\s*(#[0-9a-fA-F]{6})/g)) vars[v[1]] = v[2];
  }
  return vars;
}

function firstRootVars(): Record<string, string> {
  const m = css.match(/^:root\s*\{([^}]*)\}/m)!;
  const vars: Record<string, string> = {};
  for (const v of m[1].matchAll(/--yukti-([\w-]+):\s*(#[0-9a-fA-F]{6})/g)) vars[v[1]] = v[2];
  return vars;
}

const themes: Record<string, Record<string, string>> = {
  dark: blockVars(/:root\[data-theme="dark"\]/),
  "dark (default)": firstRootVars(),
  light: blockVars(/:root\[data-theme="light"\]/),
  "high-contrast": blockVars(/:root\[data-theme="high-contrast"\]/),
};

function lum(hex: string): number {
  const c = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) =>
    v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4,
  );
  return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
}
export function ratio(a: string, b: string): number {
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const grounds = ["bg", "surface", "surface-2"];
const textOnGround = ["ink", "ink-dim", "accent", "success", "warning", "danger", "info"];
const textOnSoft: [string, string][] = [
  ["accent", "accent-soft"], ["success", "success-soft"], ["warning", "warning-soft"],
  ["danger", "danger-soft"], ["info", "info-soft"], ["ink", "accent-soft"],
];

// The light theme also applies via prefers-color-scheme, so audit that block too.
const lightMedia = css.match(/@media \(prefers-color-scheme: light\)\s*\{\s*:root\s*\{([^}]*)\}/);
if (lightMedia) {
  const v: Record<string, string> = {};
  for (const m of lightMedia[1].matchAll(/--yukti-([\w-]+):\s*(#[0-9a-fA-F]{6})/g)) v[m[1]] = m[2];
  themes["light (prefers-color-scheme)"] = v;
}

describe.each(Object.entries(themes))("WCAG AA contrast — %s theme", (_name, t) => {
  it("defines all tokens", () => {
    for (const k of [...grounds, ...textOnGround, "accent-soft", "border", "accent-ink"]) expect(t[k], k).toBeTruthy();
  });
  for (const g of grounds)
    for (const f of textOnGround)
      it(`${f} text on ${g} >= 4.5:1`, () => expect(ratio(t[f], t[g])).toBeGreaterThanOrEqual(4.5));
  for (const [f, b] of textOnSoft)
    it(`${f} text on ${b} >= 4.5:1`, () => expect(ratio(t[f], t[b])).toBeGreaterThanOrEqual(4.5));
  it("accent-ink on accent (primary button) >= 4.5:1", () =>
    expect(ratio(t["accent-ink"], t.accent)).toBeGreaterThanOrEqual(4.5));
  it("accent focus indicator on grounds >= 3:1", () => {
    for (const g of grounds) expect(ratio(t.accent, t[g]), g).toBeGreaterThanOrEqual(3);
  });
});
