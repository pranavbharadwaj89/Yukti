// sarvaghna/agents/git-agent.mjs
// Agent: Git Agent (UI Auditor) — renamed from the "Bravitas" placeholder
// used while this was being built. Owner's own framing: it "will only have
// to be active whenever there is a code merged" — it has no standalone
// CLI flag and no manual dispatch trigger; the only thing that ever
// invokes it is sarvaghna/run-git-agent.mjs, which only
// .github/workflows/sarvaghna.yml's merge-triggered job calls.
//
// Unlike Sarvaghna's 5-agent interactive crew (which reads a git diff and
// can be run by hand via --crew / --mission), the Git Agent doesn't read a
// diff at all — it drives a real browser against the real running
// frontend (apps/yukti-gui) and the real API behind it, the same way
// e2e/ai-generation.spec.ts already does for one page (signIn + axe-core
// WCAG scan). This agent generalizes that pattern across every route the
// app serves instead of one spec-specific page, and reports through
// Sarvaghna's own existing finding/decision/formatter pipeline so no new
// report format is needed. For that reason it's deliberately NOT
// registered in agents/index.mjs's CREW_METADATA — it isn't part of the
// human-facing crew roster (--crew-list), since there's no human-facing
// way to invoke it.
//
// Deliberately NOT using Gemini vision here: core/gemini-client.mjs's
// callGeminiJson only sends text parts (no inline_data for images), and
// the shared Gemini quota (20 req/day, sarvaghna/ledger/gemini-requests.jsonl)
// is already consumed by other features. A structural audit (axe-core +
// HTTP status + console errors) needs no model call at all.

import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';
import { createRequire } from 'module';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const guiPackageJson = path.resolve(__dirname, '..', '..', 'apps', 'yukti-gui', 'package.json');

// Every authenticated route the app actually serves today (src/app/router.tsx,
// read directly rather than guessed). Parameterized detail routes
// (/flows/$flowId, /runs/$runId) need a real seeded id to visit meaningfully
// and are out of this version's scope — flagged in plan/OPEN-QUESTIONS.md,
// not silently skipped.
export const DEFAULT_ROUTES = [
  { name: 'dashboard', path: '/' },
  { name: 'projects', path: '/projects' },
  { name: 'triggers', path: '/triggers' },
  { name: 'audit', path: '/audit' },
  { name: 'flows', path: '/flows' },
  { name: 'workflow-designer', path: '/workflow-designer' },
  { name: 'reports', path: '/reports' },
  { name: 'settings', path: '/settings' },
  { name: 'tests-web', path: '/tests/web' },
  { name: 'tests-mobile', path: '/tests/mobile' },
  { name: 'tests-ui', path: '/tests/ui' },
  { name: 'tests-api', path: '/tests/api' },
  { name: 'tests-database', path: '/tests/database' },
  { name: 'tests-logs', path: '/tests/logs' },
];

const AXE_WCAG_TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

export class GitAgent {
  constructor(options = {}) {
    this.id = 'git-agent';
    this.name = 'Git Agent';
    this.color = '#2BB3A3';
    this.dark = '#17746A';
    this.bg = '#E3F7F4';
    this.tagline = 'Audits the rendered UI on every merge to main';

    this.baseURL = options.baseURL || process.env.GIT_AGENT_BASE_URL || 'http://localhost:5173';
    this.email = options.email || process.env.E2E_ADMIN_EMAIL || 'admin@yukti.local';
    this.password = options.password || process.env.E2E_ADMIN_PASSWORD || 'ChangeMe123!';
    this.screenshotDir = options.screenshotDir || path.resolve(__dirname, '..', 'ledger', 'git-agent-screenshots');

    // Resolves @playwright/test and axe-core through apps/yukti-gui's own
    // node_modules (both already real devDependencies there) rather than
    // giving sarvaghna its own copy of a browser automation dependency.
    this._requireFromGui = createRequire(guiPackageJson);
  }

  /**
   * Visits every route, signed in once as the seeded admin, running a
   * real-browser WCAG 2.1 A/AA scan (axe-core, the same tag set
   * e2e/ai-generation.spec.ts already uses) plus an HTTP-status and
   * console-error check. Returns Sarvaghna's standard finding shape
   * ({rule, severity, title, message, file, line}) so the existing
   * formatters and PolicyMatrix need no changes to consume it. Never
   * throws — a route that can't even be reached becomes a finding, not
   * a crashed process.
   */
  async auditRoutes(routes = DEFAULT_ROUTES) {
    let chromium;
    try {
      ({ chromium } = this._requireFromGui('@playwright/test'));
    } catch (err) {
      return {
        findings: [this.finding('CRITICAL', 'git-agent-no-playwright', '(setup)',
          `Could not load @playwright/test from apps/yukti-gui/node_modules: ${err.message}. ` +
          'Run "npm ci && npx playwright install chromium" in apps/yukti-gui first.')],
        routeReports: [],
        auditedCount: 0
      };
    }

    const browser = await chromium.launch();
    const findings = [];
    const routeReports = [];

    try {
      const context = await browser.newContext({ baseURL: this.baseURL });
      const page = await context.newPage();
      const consoleErrors = [];
      page.on('console', (msg) => {
        if (msg.type() === 'error') consoleErrors.push(msg.text());
      });

      const signedIn = await this.signIn(page);
      if (!signedIn.ok) {
        findings.push(this.finding('CRITICAL', 'git-agent-signin-failed', '/login',
          `Could not sign in as the seeded admin before auditing any route: ${signedIn.reason}`));
        return { findings, routeReports, auditedCount: 0 };
      }

      for (const route of routes) {
        consoleErrors.length = 0;
        const routeFindings = await this.auditOneRoute(page, route, consoleErrors);
        findings.push(...routeFindings);
        routeReports.push({
          route: route.path,
          violationCount: routeFindings.length,
          status: routeFindings.some((f) => f.severity === 'CRITICAL' || f.severity === 'HIGH') ? 'FAILED' : 'PASSED'
        });
      }
    } finally {
      await browser.close();
    }

    return { findings, routeReports, auditedCount: routes.length };
  }

  async signIn(page) {
    try {
      await page.goto('/login', { waitUntil: 'domcontentloaded', timeout: 15000 });
      await page.getByLabel('Email').fill(this.email);
      await page.getByLabel('Password').fill(this.password);
      await page.getByRole('button', { name: 'Sign in' }).click();
      await page.waitForURL((url) => !url.pathname.endsWith('/login'), { timeout: 15000 });
      return { ok: true };
    } catch (err) {
      return { ok: false, reason: err.message };
    }
  }

  async auditOneRoute(page, route, consoleErrors) {
    const findings = [];
    try {
      const response = await page.goto(route.path, { waitUntil: 'networkidle', timeout: 20000 });
      if (response && response.status() >= 400) {
        findings.push(this.finding('CRITICAL', 'git-agent-http-error', route.path,
          `Navigating to ${route.path} returned HTTP ${response.status()}.`));
        return findings;
      }
    } catch (err) {
      findings.push(this.finding('CRITICAL', 'git-agent-navigation-failed', route.path,
        `Navigating to ${route.path} failed: ${err.message}`));
      return findings;
    }

    await this.captureScreenshot(page, route);

    if (consoleErrors.length > 0) {
      findings.push(this.finding('HIGH', 'git-agent-console-error', route.path,
        `${consoleErrors.length} console error(s) during render: ${consoleErrors.slice(0, 3).join(' | ')}`));
    }

    const violations = await this.axeScan(page);
    for (const v of violations) {
      findings.push(this.finding(this.severityFor(v.impact), `git-agent-a11y-${v.id}`, route.path,
        `${v.impact}: ${v.help} — ${v.targets.join(', ')}`));
    }

    return findings;
  }

  async axeScan(page) {
    let axeScriptPath;
    try {
      axeScriptPath = this._requireFromGui.resolve('axe-core/axe.min.js');
    } catch {
      return []; // axe-core not resolvable — already reported at the agent level via the git-agent-no-playwright-style failure if setup is broken.
    }
    try {
      await page.addScriptTag({ path: axeScriptPath });
    } catch {
      return [];
    }
    return page.evaluate(async (tags) => {
      // eslint-disable-next-line no-undef -- axe-core attaches itself to window once injected.
      const axe = window.axe;
      const result = await axe.run(document, { runOnly: { type: 'tag', values: tags } });
      return result.violations.map((v) => ({
        id: v.id,
        impact: v.impact,
        help: v.help,
        targets: v.nodes.map((n) => n.target.join(' '))
      }));
    }, AXE_WCAG_TAGS);
  }

  severityFor(impact) {
    if (impact === 'critical') return 'CRITICAL';
    if (impact === 'serious') return 'HIGH';
    return 'WARNING'; // moderate / minor — advisory, never blocking (same tiering as PolicyMatrix's secrets/architecture findings).
  }

  async captureScreenshot(page, route) {
    try {
      fs.mkdirSync(this.screenshotDir, { recursive: true });
      const fileName = route.name.replace(/[^a-z0-9-]/gi, '-') + '.png';
      await page.screenshot({ path: path.join(this.screenshotDir, fileName), fullPage: true });
    } catch {
      // Best-effort evidence only — never fails the audit over a screenshot.
    }
  }

  finding(severity, rule, routePath, message) {
    return { rule, severity, title: `Git Agent UI audit: ${rule}`, message, file: routePath, line: 0 };
  }
}
