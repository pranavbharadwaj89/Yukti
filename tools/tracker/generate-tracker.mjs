#!/usr/bin/env node
// Regenerates docs/TRACKER.html from tools/tracker/checks.json by inspecting
// actual repo state (file existence / content grep) — see checks.json for
// what each item tests. Run: node tools/tracker/generate-tracker.mjs
import { readFileSync, writeFileSync, existsSync, readdirSync, statSync } from "node:fs";
import { execSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, "..", "..");
const CHECKS_PATH = path.join(__dirname, "checks.json");
const OUTPUT_PATH = path.join(REPO_ROOT, "docs", "TRACKER.html");
const TEMPLATE_PATH = path.join(__dirname, "tracker-template.html");

const SKIP_DIRS = new Set(["node_modules", "bin", "obj", "dist", ".git", "coverage"]);

function walk(dir, out) {
  let entries;
  try {
    entries = readdirSync(dir, { withFileTypes: true });
  } catch {
    return;
  }
  for (const entry of entries) {
    if (SKIP_DIRS.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) walk(full, out);
    else out.push(full);
  }
}

function filesUnder(relScope) {
  const abs = path.join(REPO_ROOT, relScope);
  if (!existsSync(abs)) return [];
  if (statSync(abs).isFile()) return [abs];
  const out = [];
  walk(abs, out);
  return out;
}

function grepFound(item) {
  const files = filesUnder(item.scope);
  const pattern = item.matchCase === false ? item.pattern.toLowerCase() : item.pattern;
  for (const file of files) {
    let content;
    try {
      content = readFileSync(file, "utf8");
    } catch {
      continue;
    }
    const haystack = item.matchCase === false ? content.toLowerCase() : content;
    if (haystack.includes(pattern)) return true;
  }
  return false;
}

function evaluateItem(item) {
  if (item.type === "manual") return item.status;
  if (item.type === "exists") {
    return existsSync(path.join(REPO_ROOT, item.path)) ? item.whenFound : item.whenMissing;
  }
  if (item.type === "grep") {
    return grepFound(item) ? item.whenFound : item.whenMissing;
  }
  throw new Error(`Unknown check type: ${item.type}`);
}

function gitInfo() {
  try {
    const sha = execSync("git rev-parse --short HEAD", { cwd: REPO_ROOT }).toString().trim();
    const branch = execSync("git rev-parse --abbrev-ref HEAD", { cwd: REPO_ROOT }).toString().trim();
    return { sha, branch };
  } catch {
    return { sha: "unknown", branch: "unknown" };
  }
}

const checks = JSON.parse(readFileSync(CHECKS_PATH, "utf8"));
const data = {};
for (const [column, group] of Object.entries(checks.columns)) {
  data[column] = {
    items: group.items.map((item) => [item.name, evaluateItem(item), item.note || ""]),
  };
}

const { sha, branch } = gitInfo();
const template = readFileSync(TEMPLATE_PATH, "utf8");
const html = template
  .replace("__TRACKER_DATA__", JSON.stringify(data, null, 2))
  .replace("__GENERATED_AT__", new Date().toISOString())
  .replace("__GIT_SHA__", sha)
  .replace("__GIT_BRANCH__", branch);

writeFileSync(OUTPUT_PATH, html);
console.log(`Wrote ${path.relative(REPO_ROOT, OUTPUT_PATH)} from repo state at ${branch}@${sha}`);
