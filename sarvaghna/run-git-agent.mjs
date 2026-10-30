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

import { GitAgent } from './agents/git-agent.mjs';
import { DecisionEngine } from './core/decision-engine.mjs';
import { formatTerminalOutput, writeGitHubStepSummary, emitGitHubAnnotations, postOrUpdatePRComment } from './formatters/github.mjs';

console.log('\n================================================================');
console.log('         🚦 GIT AGENT — SARVAGHNA UI AUDIT (merge-triggered)   ');
console.log('================================================================');

const gitAgent = new GitAgent();
const startTime = Date.now();
const { findings, routeReports, auditedCount } = await gitAgent.auditRoutes();
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
