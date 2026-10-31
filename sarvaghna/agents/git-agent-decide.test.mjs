// sarvaghna/agents/git-agent-decide.test.mjs
//
// The failure mode this guards against is silence: if decide() wrongly
// returns SKIP, the audit never runs and nothing complains. So the
// negative cases below matter at least as much as the positive ones --
// same framing decide.py's own test suite (the Brevitas template this was
// ported from) used.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Action, SkipReason, decide, selectRoutes } from './git-agent-decide.mjs';

const ROUTE_TRIGGERS = {
  dashboard: ['apps/yukti-gui/src/pages/dashboard-page'],
  settings: ['apps/yukti-gui/src/pages/settings-page'],
  flows: ['apps/yukti-gui/src/features/flow-authoring/'],
};
const ALL_ROUTES = ['dashboard', 'settings', 'flows'];

function base(overrides = {}) {
  return decide({
    changedFiles: ['apps/yukti-gui/src/pages/dashboard-page.tsx'],
    routeTriggers: ROUTE_TRIGGERS,
    allRoutes: ALL_ROUTES,
    ...overrides,
  });
}

test('a backend-only change skips with NO_UI_CHANGES', () => {
  const d = base({ changedFiles: ['src/Yukti.Api/Program.cs', 'README.md'] });
  assert.equal(d.action, Action.SKIP);
  assert.equal(d.skipReason, SkipReason.NO_UI_CHANGES);
});

test('a docs-only change skips', () => {
  const d = base({ changedFiles: ['docs/quickstart.md'] });
  assert.equal(d.skipReason, SkipReason.NO_UI_CHANGES);
});

test('a real UI change audits', () => {
  assert.equal(base().action, Action.AUDIT);
});

test('a mixed backend + UI change still audits', () => {
  const d = base({ changedFiles: ['src/Yukti.Api/Program.cs', 'apps/yukti-gui/src/pages/dashboard-page.tsx'] });
  assert.equal(d.action, Action.AUDIT);
});

test('a null diff (unavailable) audits everything, never read as "nothing changed"', () => {
  const d = base({ changedFiles: null });
  assert.equal(d.action, Action.AUDIT);
  assert.deepEqual([...d.routes].sort(), [...ALL_ROUTES].sort());
});

test('an empty diff audits everything', () => {
  const d = base({ changedFiles: [] });
  assert.equal(d.action, Action.AUDIT);
  assert.deepEqual([...d.routes].sort(), [...ALL_ROUTES].sort());
});

test('force overrides every gate, including a backend-only diff', () => {
  const d = base({ changedFiles: ['README.md'], force: true });
  assert.equal(d.action, Action.AUDIT);
  assert.deepEqual([...d.routes].sort(), [...ALL_ROUTES].sort());
});

test('no routes configured skips with NO_ROUTES', () => {
  const d = base({ allRoutes: [] });
  assert.equal(d.skipReason, SkipReason.NO_ROUTES);
});

test('selects only the single affected route', () => {
  const d = base({ changedFiles: ['apps/yukti-gui/src/pages/settings-page.tsx'] });
  assert.deepEqual(d.routes, ['settings']);
});

test('selects multiple affected routes for a multi-file change', () => {
  const d = base({
    changedFiles: ['apps/yukti-gui/src/pages/dashboard-page.tsx', 'apps/yukti-gui/src/features/flow-authoring/flows-page.tsx'],
  });
  assert.deepEqual([...d.routes].sort(), ['dashboard', 'flows']);
});

test('an unmapped UI file audits every route rather than guessing the blast radius', () => {
  const d = base({ changedFiles: ['apps/yukti-gui/src/components/mystery.tsx'] });
  assert.deepEqual([...d.routes].sort(), [...ALL_ROUTES].sort());
  assert.ok(d.notes.some((n) => n.includes('match no route trigger')));
});

test('a shared file (layout) triggers every route, with its own distinct note', () => {
  const d = base({ changedFiles: ['apps/yukti-gui/src/layouts/app-shell.tsx'] });
  assert.deepEqual([...d.routes].sort(), [...ALL_ROUTES].sort());
  assert.ok(d.notes.some((n) => n.includes('shared file')));
});

test('a change to the generated API types triggers every route', () => {
  const d = base({ changedFiles: ['apps/yukti-gui/src/api/generated/schema.ts'] });
  assert.deepEqual([...d.routes].sort(), [...ALL_ROUTES].sort());
});

test('selectRoutes alone: a shared file wins even alongside a specific one', () => {
  const { routes, notes } = selectRoutes(
    ['apps/yukti-gui/src/layouts/app-shell.tsx', 'apps/yukti-gui/src/pages/dashboard-page.tsx'],
    ROUTE_TRIGGERS,
    ALL_ROUTES,
  );
  assert.deepEqual([...routes].sort(), [...ALL_ROUTES].sort());
  assert.ok(notes.some((n) => n.includes('shared file')));
});

test('decide() result is a plain object, not something a caller can accidentally mutate upstream', () => {
  const d = base();
  const routesCopy = [...d.routes];
  d.routes.push('tampered');
  assert.notDeepEqual(d.routes, routesCopy); // documents it's a fresh array per call, not a frozen one -- mutation only affects this result
});
