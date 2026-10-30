// sarvaghna/agents/bravitas.test.mjs

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { BravitasAgent } from './bravitas.mjs';

test('severityFor maps axe-core impact to Sarvaghna severity, never escalating moderate/minor to blocking', () => {
  const bravitas = new BravitasAgent();
  assert.equal(bravitas.severityFor('critical'), 'CRITICAL');
  assert.equal(bravitas.severityFor('serious'), 'HIGH');
  assert.equal(bravitas.severityFor('moderate'), 'WARNING');
  assert.equal(bravitas.severityFor('minor'), 'WARNING');
});

test('finding() produces the exact shape the rest of Sarvaghna\'s formatters/PolicyMatrix expect', () => {
  const bravitas = new BravitasAgent();
  const f = bravitas.finding('HIGH', 'bravitas-console-error', '/flows', '2 console error(s)');
  assert.deepEqual(f, {
    rule: 'bravitas-console-error',
    severity: 'HIGH',
    title: 'Bravitas UI audit: bravitas-console-error',
    message: '2 console error(s)',
    file: '/flows',
    line: 0
  });
});

test('auditRoutes fails as one CRITICAL finding, not a thrown error, when @playwright/test cannot be loaded', async () => {
  const bravitas = new BravitasAgent();
  // Simulates a checkout where apps/yukti-gui's own node_modules hasn't been
  // installed yet — auditRoutes must still return a result, not throw.
  bravitas._requireFromGui = () => { throw new Error('Cannot find module \'@playwright/test\''); };

  const { findings, routeReports, auditedCount } = await bravitas.auditRoutes([{ name: 'dashboard', path: '/' }]);

  assert.equal(auditedCount, 0);
  assert.deepEqual(routeReports, []);
  assert.equal(findings.length, 1);
  assert.equal(findings[0].severity, 'CRITICAL');
  assert.equal(findings[0].rule, 'bravitas-no-playwright');
  assert.match(findings[0].message, /npm ci && npx playwright install chromium/);
});

test('a route that can never be reached (dead signIn target) is reported as a CRITICAL finding, not a crash', async () => {
  const bravitas = new BravitasAgent({ baseURL: 'http://127.0.0.1:1' }); // port 1: nothing ever listens here
  const { findings, routeReports, auditedCount } = await bravitas.auditRoutes([{ name: 'dashboard', path: '/' }]);

  assert.equal(auditedCount, 0);
  assert.deepEqual(routeReports, []);
  assert.equal(findings.length, 1);
  assert.equal(findings[0].severity, 'CRITICAL');
  assert.equal(findings[0].rule, 'bravitas-signin-failed');
  assert.equal(findings[0].file, '/login');
});
