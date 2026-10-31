// sarvaghna/agents/git-agent-decide.mjs
// Pure triggering decision for the Git Agent.
//
// Ported from the Brevitas pipeline template the owner handed over
// (agent/decide.py + agent/context.py), after the owner's own call: keep
// the decision-making sophistication it brought, but make it live inside
// Sarvaghna's own Git Agent, under Sarvaghna's sole authority -- not a
// second, parallel pipeline outside Sarvaghna's control. The standalone
// Brevitas pipeline (agent/, scripts/run-ui-audit.sh, ui-audit.yml) was
// never merged; its PR was closed in favor of this.
//
// What's genuinely portable from decide.py, kept here: skip the audit
// entirely when nothing UI-relevant changed, and narrow which routes
// actually need auditing instead of always sweeping all of them. What
// wasn't ported, and why: Brevitas's upstream deploy/regression gating has
// no equivalent here -- the Git Agent's own CI job builds and serves the
// frontend itself rather than depending on a separate deploy stage: Brevitas's
// baseline-gating has no equivalent either -- the Git Agent has no
// screenshot/finding baseline to diff against (its own escalation is
// axe-core's impact tiering, already unconditional); there's no
// "regression suite" stage upstream of this one to consult.
//
// Deliberately pure: no git calls, no filesystem, no network. Gathering
// changedFiles is run-git-agent.mjs's job, kept separate so this stays
// directly unit-testable without a repo or a CI event to replay.

export const Action = Object.freeze({ AUDIT: 'audit', SKIP: 'skip' });
export const SkipReason = Object.freeze({
  NO_UI_CHANGES: 'no_ui_relevant_files_changed',
  NO_ROUTES: 'no_routes_configured',
});

// Files that affect every route regardless of which one a visitor is on:
// the shared layout, the login page every route redirects to without a
// session, the router itself, global client state, and the generated API
// types. A change here is read the same way an unmapped file is (audit
// everything) but reported as a distinct, named reason rather than lumped
// in with "match no route trigger".
export const SHARED_UI_PREFIXES = [
  'apps/yukti-gui/src/layouts/',
  'apps/yukti-gui/src/pages/login-page',
  'apps/yukti-gui/src/app/router.tsx',
  'apps/yukti-gui/src/store/',
  'apps/yukti-gui/src/api/',
];

// "Did this change touch the UI at all" -- checked before route selection,
// so a backend-only or docs-only merge skips without even considering routes.
export const DEFAULT_UI_PREFIXES = [
  'apps/yukti-gui/src/',
  'apps/yukti-gui/public/',
  'apps/yukti-gui/index.html',
];

// Every route's own real source file(s), read directly from
// apps/yukti-gui/src/app/router.tsx's own imports (git-agent.mjs's
// DEFAULT_ROUTES lists the same 14 routes this keys on) -- not guessed.
// workflow-designer and flows share flow-authoring's own source, same as
// router.tsx has them share FlowsPage; the three generic test-kind routes
// (web/mobile/ui) share module-test-form.tsx the same way.
export const ROUTE_TRIGGERS = {
  dashboard: ['apps/yukti-gui/src/pages/dashboard-page'],
  projects: ['apps/yukti-gui/src/features/projects/'],
  triggers: ['apps/yukti-gui/src/features/scheduler/'],
  audit: ['apps/yukti-gui/src/features/audit/'],
  flows: ['apps/yukti-gui/src/features/flow-authoring/'],
  'workflow-designer': ['apps/yukti-gui/src/features/flow-authoring/'],
  reports: ['apps/yukti-gui/src/features/reporting-audit/'],
  settings: ['apps/yukti-gui/src/pages/settings-page'],
  'tests-web': ['apps/yukti-gui/src/features/testing/module-test-form'],
  'tests-mobile': ['apps/yukti-gui/src/features/testing/module-test-form'],
  'tests-ui': ['apps/yukti-gui/src/features/testing/module-test-form'],
  'tests-api': ['apps/yukti-gui/src/features/api-studio/'],
  'tests-database': ['apps/yukti-gui/src/features/testing/test-placeholder-page'],
  'tests-logs': ['apps/yukti-gui/src/features/logs-studio/'],
};

function matchesAny(path, prefixes) {
  return prefixes.some((p) => path.startsWith(p));
}

/**
 * Maps changed files onto the routes they could have affected.
 *
 * A changed file that matches no route's trigger prefixes means the blast
 * radius is unknown, so every route is audited -- auditing too much costs a
 * few minutes, auditing too little means a regression ships unobserved.
 * Same asymmetry decide.py's own select_flows() documents, same reasoning.
 */
export function selectRoutes(changedFiles, routeTriggers, allRoutes) {
  const notes = [];

  if (changedFiles.some((f) => matchesAny(f, SHARED_UI_PREFIXES))) {
    notes.push('A shared file (layout, login page, router, store, or generated API types) changed, so every route is audited.');
    return { routes: [...allRoutes], notes };
  }

  const selected = new Set();
  const unmapped = [];
  for (const file of changedFiles) {
    let hit = false;
    for (const [route, prefixes] of Object.entries(routeTriggers)) {
      if (matchesAny(file, prefixes)) {
        selected.add(route);
        hit = true;
      }
    }
    if (!hit) unmapped.push(file);
  }

  if (unmapped.length > 0) {
    notes.push(`${unmapped.length} changed file(s) match no route trigger (e.g. ${unmapped[0]}), so every route is audited rather than guessing the blast radius.`);
    return { routes: [...allRoutes], notes };
  }

  const routes = allRoutes.filter((r) => selected.has(r));
  return { routes, notes };
}

/**
 * Decide whether to audit at all, and if so, which routes.
 *
 * @param {object} params
 * @param {string[]|null} params.changedFiles - null means the diff itself
 *   was unavailable (shallow clone, first commit) -- read as "audit
 *   everything", never as "nothing changed".
 * @param {string[]} [params.uiPrefixes]
 * @param {Record<string,string[]>} [params.routeTriggers]
 * @param {string[]} params.allRoutes
 * @param {boolean} [params.force]
 */
export function decide({
  changedFiles, uiPrefixes = DEFAULT_UI_PREFIXES, routeTriggers = ROUTE_TRIGGERS, allRoutes, force = false,
}) {
  const notes = [];

  if (!allRoutes || allRoutes.length === 0) {
    return { action: Action.SKIP, reason: 'No routes are configured to audit.', skipReason: SkipReason.NO_ROUTES, routes: [], notes };
  }

  if (force) {
    return { action: Action.AUDIT, reason: `Forced: auditing all ${allRoutes.length} route(s).`, routes: [...allRoutes], notes };
  }

  if (changedFiles === null) {
    notes.push('Changed-file diff was unavailable, so every route is audited rather than guessing nothing changed.');
    return { action: Action.AUDIT, reason: `Auditing all ${allRoutes.length} route(s) (diff unavailable).`, routes: [...allRoutes], notes };
  }

  if (changedFiles.length === 0) {
    notes.push('No changed files were detected for this event.');
    return { action: Action.AUDIT, reason: `Auditing all ${allRoutes.length} route(s) (no diff).`, routes: [...allRoutes], notes };
  }

  const uiChanged = changedFiles.filter((f) => matchesAny(f, uiPrefixes));
  if (uiChanged.length === 0) {
    return {
      action: Action.SKIP,
      reason: `None of the ${changedFiles.length} changed file(s) touch apps/yukti-gui, so the rendered UI cannot have changed.`,
      skipReason: SkipReason.NO_UI_CHANGES,
      routes: [],
      notes,
    };
  }
  notes.push(`${uiChanged.length} of ${changedFiles.length} changed file(s) are UI-relevant.`);

  const { routes, notes: routeNotes } = selectRoutes(changedFiles, routeTriggers, allRoutes);
  notes.push(...routeNotes);

  return { action: Action.AUDIT, reason: `Auditing ${routes.length} route(s): ${routes.join(', ')}.`, routes, notes };
}
