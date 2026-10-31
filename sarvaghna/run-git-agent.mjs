#!/usr/bin/env node
// sarvaghna/run-git-agent.mjs
// Runs the Git Agent (sarvaghna/agents/git-agent.mjs) end to end and
// reports through Sarvaghna's existing formatters/decision pipeline.
//
// This file is the ONLY thing that invokes the Git Agent. There is no
// `sarvaghna.mjs --something` flag and no npm script for it, by design —
// the owner's own words: "it will only have to be active whenever there
// is a code merged." The only caller is the merge-triggered job in
// .github/workflows/sarvaghna.yml (gated to the push event, i.e. a
// merge to main). Running this file directly, outside that workflow,
// is possible but is not a supported entry point — it exists for CI,
// not for ad hoc/manual use.

import { execSync } from 'child_process';
import { GitAgent, DEFAULT_ROUTES } from './agents/git-agent.mjs';
import { decide } from './agents/git-agent-decide.mjs';
import { DecisionEngine } from './core/decision-engine.mjs';
import { formatTerminalOutput, writeGitHubStepSummary, emitGitHubAnnotations, postOrUpdatePRComment } from './formatters/github.mjs';

console.log('\n================================================================');
console.log('         🚦 GIT AGENT — SARVAGHNA UI AUDIT (merge-triggered)   ');
console.log('================================================================');

/**
 * Files this push actually touched, or null when that diff is unavailable
 * (shallow clone, first commit on the branch, local/manual run). GITHUB_EVENT_BEFORE
 * is the commit main was at before this push -- the exact comparison a merge-to-main
 * event needs; sarvaghna.yml passes it through. null is never read as "nothing
 * changed" (decide.py's own documented reasoning, ported along with the rest of it).
 */
function changedFiles() {
  const before = process.env.GITHUB_EVENT_BEFORE;
  try {
    if (before && !/^0+$/.test(before)) {
      const out = execSync(`git diff --name-only ${before}..HEAD`, { encoding: 'utf8' });
      return out.split('\n').filter(Boolean);
    }
  } catch {
    // falls through to the HEAD~1 fallback below
  }
  try {
    const out = execSync('git diff --name-only HEAD~1..HEAD', { encoding: 'utf8' });
    return out.split('\n').filter(Boolean);
  } catch {
    return null;
  }
}

const triggerDecision = decide({
  changedFiles: changedFiles(),
  allRoutes: DEFAULT_ROUTES.map((r) => r.name),
});

console.log(`DECISION: ${triggerDecision.action.toUpperCase()} — ${triggerDecision.reason}`);
for (const n of triggerDecision.notes) console.log(`          · ${n}`);
console.log('================================================================\n');

if (triggerDecision.action === 'skip') {
  console.log('No audit run — this is not a UI finding.');
  process.exitCode = 0;
  process.exit(0);
}

const routesToAudit = DEFAULT_ROUTES.filter((r) => triggerDecision.routes.includes(r.name));

const gitAgent = new GitAgent();
const startTime = Date.now();
const { findings, routeReports, auditedCount } = await gitAgent.auditRoutes(routesToAudit);
const durationMs = Date.now() - startTime;

const result = {
  ruleFindings: findings,
  skillReports: [{
    name: 'git-agent-ui-audit',
    summary: `Audited ${auditedCount} route(s), ${findings.length} finding(s).`
  }],
  aiReview: { enabled: false, reason: 'The Git Agent does not use AI semantic review in this version (axe-core + structural checks only).' },
  durationMs,
  filesCount: auditedCount,
  linesAdded: 0,
  linesDeleted: 0
};

formatTerminalOutput(result);

if (routeReports.length > 0) {
  console.log('Per-route status:');
  routeReports.forEach(r => console.log(`  ${r.status === 'PASSED' ? '✅' : '❌'} ${r.route} — ${r.violationCount} finding(s)`));
  console.log('');
}

const decision = DecisionEngine.makeDecision({
  target: 'git-agent-ui-audit',
  ruleFindings: findings,
  aiReview: result.aiReview,
  filesCount: auditedCount
});

console.log(`👑 GIT AGENT DECISION: [${decision.verdict}] (Confidence: ${Math.round(decision.confidence * 100)}%)`);
console.log(`   Rationale: ${decision.rationale}\n`);

emitGitHubAnnotations(findings);
writeGitHubStepSummary(result);
await postOrUpdatePRComment(result);

process.exitCode = decision.verdict === 'BLOCK_AND_PREVENT' ? 1 : 0;
