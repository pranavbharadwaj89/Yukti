// sarvaghna/agents/git-agent.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { GitAgent } from './git-agent.mjs';

test('severityFor maps axe-core impact to Sarvaghna severity, never escalating moderate/minor to blocking', () => {
  const gitAgent = new GitAgent();
  assert.equal(gitAgent.severityFor('critical'), 'CRITICAL');
  assert.equal(gitAgent.severityFor('serious'), 'HIGH');
  assert.equal(gitAgent.severityFor('moderate'), 'WARNING');
  assert.equal(gitAgent.severityFor('minor'), 'WARNING');
});

test('finding() produces the exact shape the rest of Sarvaghna\'s formatters/PolicyMatrix expect', () => {
  const gitAgent = new GitAgent();
  const f = gitAgent.finding('HIGH', 'git-agent-console-error', '/flows', '2 console error(s)');
  assert.deepEqual(f, {
    rule: 'git-agent-console-error',
    severity: 'HIGH',
    title: 'Git Agent UI audit: git-agent-console-error',
    message: '2 console error(s)',
    file: '/flows',
    line: 0
  });
});

test('auditRoutes fails as one CRITICAL finding, not a thrown error, when @playwright/test cannot be loaded', async () => {
  const gitAgent = new GitAgent();
  // Simulates a checkout where apps/yukti-gui's own node_modules hasn't been
  // installed yet — auditRoutes must still return a result, not throw.
  gitAgent._requireFromGui = () => { throw new Error('Cannot find module \'@playwright/test\''); };

  const { findings, routeReports, auditedCount } = await gitAgent.auditRoutes([{ name: 'dashboard', path: '/' }]);

  assert.equal(auditedCount, 0);
  assert.deepEqual(routeReports, []);
  assert.equal(findings.length, 1);
  assert.equal(findings[0].severity, 'CRITICAL');
  assert.equal(findings[0].rule, 'git-agent-no-playwright');
  assert.match(findings[0].message, /npm ci && npx playwright install chromium/);
});

test('a route that can never be reached (dead signIn target) is reported as a CRITICAL finding, not a crash', async () => {
  const gitAgent = new GitAgent({ baseURL: 'http://127.0.0.1:1' }); // port 1: nothing ever listens here
  const { findings, routeReports, auditedCount } = await gitAgent.auditRoutes([{ name: 'dashboard', path: '/' }]);

  assert.equal(auditedCount, 0);
  assert.deepEqual(routeReports, []);
  assert.equal(findings.length, 1);
  assert.equal(findings[0].severity, 'CRITICAL');
  assert.equal(findings[0].rule, 'git-agent-signin-failed');
  assert.equal(findings[0].file, '/login');
});
