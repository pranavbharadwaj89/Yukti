#!/usr/bin/env node
// tools/sarvaghna/sarvaghna.mjs
// SARVAGHNA (सर्वज्ञ) — Omniscient Code Analysis & Review Agent for Yukti

import { execSync } from 'child_process';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

import { scanSecrets } from './rules/secrets.mjs';
import { checkArchitecture } from './rules/architecture.mjs';
import { loadAndExecuteCustomRules } from './rules/rule-engine.mjs';
import { loadAndExecuteSkills } from './skills/skill-engine.mjs';
import { skill as gitHooksSkill } from './skills/skill-git-hooks/index.mjs';
import { DecisionEngine } from './core/decision-engine.mjs';
import { runSemanticReview } from './rules/semantic-reviewer.mjs';
import { formatTerminalOutput, writeGitHubStepSummary, emitGitHubAnnotations, postOrUpdatePRComment } from './formatters/github.mjs';
import { OrchestratorAgent, AnalystAgent, CoderAgent, TesterAgent, ReviewerAgent, CREW_METADATA } from './agents/index.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const repoRoot = path.resolve(__dirname, '..');

// 0. Load local-only secrets for manual CLI testing (e.g. GEMINI_API_KEY)
// from sarvaghna/.env.local if present. Never committed — see .gitignore.
// Real secrets already present in the environment (CI, a real shell export)
// always take priority over this file.
const envLocalPath = path.join(__dirname, '.env.local');
if (fs.existsSync(envLocalPath)) {
  for (const line of fs.readFileSync(envLocalPath, 'utf8').split(/\r?\n/)) {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) continue;
    const eq = trimmed.indexOf('=');
    if (eq === -1) continue;
    const key = trimmed.slice(0, eq).trim();
    const value = trimmed.slice(eq + 1).trim().replace(/^['"]|['"]$/g, '');
    if (key && !(key in process.env)) {
      process.env[key] = value;
    }
  }
}

// 1. Handle Git Hook Inculcation & Ledger Commands
const args = process.argv.slice(2);

if (args.includes('--install-hooks')) {
  gitHooksSkill.installHooks(repoRoot);
  process.exit(0);
}

if (args.includes('--uninstall-hooks')) {
  gitHooksSkill.uninstallHooks(repoRoot);
  process.exit(0);
}

if (args.includes('--ledger')) {
  const recents = DecisionEngine.readRecentDecisions(10);
  console.log('\n================================================================');
  console.log('         📜 SARVAGHNA IMMUTABLE DECISION LEDGER (Recent 10)     ');
  console.log('================================================================');
  if (recents.length === 0) {
    console.log('No decisions recorded yet.\n');
  } else {
    recents.forEach((r, idx) => {
      const icon = r.verdict === 'AUTHORIZE_PASS' ? '✅' : '❌';
      console.log(`[${idx + 1}] ${r.timestamp} | ${icon} ${r.verdict} (Confidence: ${Math.round(r.confidence * 100)}%)`);
      console.log(`    Target: ${r.target} | Mandate: @${r.mandate}`);
      console.log(`    Rationale: ${r.rationale}`);
    });
    console.log('\n================================================================\n');
  }
  process.exit(0);
}

if (args.includes('--crew-list')) {
  console.log('\n================================================================');
  console.log('         🤖 SARVAGHNA 5-AGENT SPECIALIST CREW ROSTER           ');
  console.log('================================================================');
  CREW_METADATA.forEach(agent => {
    console.log(`• ${agent.name} (${agent.color}) — ${agent.tagline}`);
  });
  console.log('================================================================\n');
  process.exit(0);
}

if (args.includes('--orchestrator-prompt')) {
  const orchestrator = new OrchestratorAgent();
  const rendered = orchestrator.renderPrompt(
    'Execute comprehensive test generation and quality verification for the Yukti platform',
    'Previous mission accomplished with all 67 tests passing and 100% architectural quality score.'
  );
  console.log(rendered);
  process.exit(0);
}

if (args.includes('--scenarios') || args.includes('--coder-tests')) {
  const flagIndex = args.indexOf('--scenarios') !== -1 ? args.indexOf('--scenarios') : args.indexOf('--coder-tests');
  const scenariosPath = args[flagIndex + 1] || 'scenarios_PROJ-123.json';
  const targetType = args.includes('--target') ? args[args.indexOf('--target') + 1] : 'all';

  console.log('\n================================================================');
  console.log('         🤖 SARVAGHNA CODER AGENT: SCENARIO INGESTION           ');
  console.log('================================================================');
  console.log(`Ingesting scenarios from: ${scenariosPath}`);
  console.log(`Target frameworks: ${targetType}\n`);

  const coder = new CoderAgent();
  const results = coder.generateTestsFromScenarios(scenariosPath, targetType, path.resolve(repoRoot, 'tests', 'scenarios'));

  const list = Array.isArray(results) ? results : [results];
  list.forEach(res => {
    console.log(`✅ Generated ${res.targetType.toUpperCase()} test suite:`);
    console.log(`   File: ${res.writtenPath || res.filename}`);
    console.log(`   Total Scenarios: ${res.scenariosCount}\n`);
  });
  console.log('================================================================\n');
  process.exit(0);
}

if (args.includes('--crew') || args.includes('--mission')) {
  const goalIndex = args.indexOf('--mission');
  const goal = goalIndex !== -1 && args[goalIndex + 1]
    ? args[goalIndex + 1]
    : 'Execute end-to-end multi-agent architecture analysis, test generation, test execution, and quality scoring';

  console.log('\n================================================================');
  console.log('         🤖 SARVAGHNA 5-AGENT AUTONOMOUS CREW ACTIVATED         ');
  console.log('================================================================');
  console.log('Crew Members:');
  CREW_METADATA.forEach((m, idx) => {
    console.log(`  [${idx + 1}] ${m.name.padEnd(14)} | ${m.tagline}`);
  });
  console.log('----------------------------------------------------------------');
  console.log(`🎯 Mission Goal: "${goal}"\n`);

  // Give the Reviewer's architecture/secrets audit something real to look
  // at. Without this, auditDiff(context.rawDiff || '') always runs against
  // an empty diff and trivially "passes" regardless of what's actually in
  // the working tree.
  let rawDiff = '';
  try {
    rawDiff = execSync('git diff origin/main...HEAD', { cwd: repoRoot, encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 });
  } catch {
    try {
      rawDiff = execSync('git diff HEAD', { cwd: repoRoot, encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 });
    } catch {
      rawDiff = '';
    }
  }

  const orchestrator = new OrchestratorAgent();
  const mission = await orchestrator.executeMission(goal, {
    repoRoot,
    docPath: path.resolve(repoRoot, '..', 'New Text Document.txt'),
    rawDiff
  });

  console.log('📋 Crew Mission Execution Log:');
  mission.log.forEach(entry => {
    const icon = entry.status === 'SUCCESS' || entry.status === 'PASSED' || entry.status === 'APPROVED' ? '✅' : 'ℹ️';
    console.log(`  ${icon} Step ${entry.step} [${entry.agent}]: ${entry.result}`);
  });

  console.log('\n📦 Orchestrator Structured State JSON:');
  console.log(JSON.stringify(mission.state, null, 2));

  console.log('\n================================================================');
  console.log(`🏆 Crew Mission Verdict: [${mission.verdict}] (Confidence: ${Math.round(mission.confidence * 100)}%)`);
  console.log('================================================================\n');
  process.exit(mission.verdict === 'MISSION_ACCOMPLISHED' ? 0 : 1);
}

// 2. Load Configuration
const configPath = path.join(__dirname, 'sarvaghna.config.json');
let config = {};
try {
  config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
} catch (e) {
  console.warn(`[SARVAGHNA] Warning: Could not read config file, using defaults: ${e.message}`);
}

// 3. Parse Inspection Arguments
let baseCommit = null;
let headCommit = 'HEAD';
let isStaged = false;

for (let i = 0; i < args.length; i++) {
  if (args[i] === '--base' && args[i + 1]) {
    baseCommit = args[++i];
  } else if (args[i] === '--head' && args[i + 1]) {
    headCommit = args[++i];
  } else if (args[i] === '--staged') {
    isStaged = true;
  }
}

// In GitHub Actions PR context: auto-detect base
if (!baseCommit && !isStaged) {
  if (process.env.GITHUB_BASE_REF) {
    baseCommit = `origin/${process.env.GITHUB_BASE_REF}`;
  } else {
    baseCommit = 'HEAD~1';
  }
}

// 4. Obtain Git Diff
let diffCommand = '';
if (isStaged) {
  diffCommand = 'git diff --staged -U3';
} else {
  diffCommand = `git diff ${baseCommit}..${headCommit} -U3`;
}

let rawDiff = '';
try {
  rawDiff = execSync(diffCommand, { encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 });
} catch (err) {
  try {
    rawDiff = execSync('git diff HEAD -U3', { encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 });
  } catch (fallbackErr) {
    console.warn(`[SARVAGHNA] Warning: git diff failed: ${fallbackErr.message}`);
    rawDiff = '';
  }
}

// 5. Parse Git Diff into Structured Chunks
function parseGitDiff(diffText) {
  const files = [];
  if (!diffText || diffText.trim().length === 0) return files;

  const rawFiles = diffText.split(/^diff --git /m);

  for (const rf of rawFiles) {
    if (!rf.trim()) continue;

    const lines = rf.split(/\r?\n/);
    const headerLine = lines[0];
    const m = headerLine.match(/a\/(.+?)\s+b\/(.+)/);
    if (!m) continue;

    const filePath = m[2];

    // Check ignored paths
    const normalizedPath = filePath.replace(/\\/g, '/');
    const isIgnored = (config.ignoredPaths || []).some(ig => {
      const normalizedIg = ig.replace(/\\/g, '/');
      if (normalizedIg.endsWith('/')) return normalizedPath.includes(normalizedIg);
      if (normalizedIg.startsWith('*.')) return normalizedPath.endsWith(normalizedIg.replace('*.', '.'));
      return normalizedPath === normalizedIg || normalizedPath.endsWith(normalizedIg);
    });

    if (isIgnored) continue;

    const chunks = [];
    let currentChunk = null;
    let currentNewLine = 0;

    for (let i = 1; i < lines.length; i++) {
      const line = lines[i];

      if (line.startsWith('@@')) {
        const chunkMatch = line.match(/@@ -\d+(?:,\d+)? \+(\d+)(?:,\d+)? @@/);
        if (chunkMatch) {
          currentNewLine = parseInt(chunkMatch[1], 10);
        }
        currentChunk = { header: line, addedLines: [], deletedLines: [] };
        chunks.push(currentChunk);
      } else if (currentChunk) {
        if (line.startsWith('+') && !line.startsWith('+++')) {
          currentChunk.addedLines.push({
            newLineNumber: currentNewLine,
            content: line.substring(1)
          });
          currentNewLine++;
        } else if (line.startsWith('-') && !line.startsWith('---')) {
          currentChunk.deletedLines.push(line.substring(1));
        } else if (!line.startsWith('\\')) {
          currentNewLine++;
        }
      }
    }

    files.push({
      path: filePath,
      chunks
    });
  }

  return files;
}

// 6. Execute Analysis Pipeline
async function run() {
  const startTime = Date.now();
  const diffFiles = parseGitDiff(rawDiff);

  let linesAdded = 0;
  let linesDeleted = 0;
  diffFiles.forEach(f => {
    f.chunks.forEach(c => {
      linesAdded += c.addedLines.length;
      linesDeleted += c.deletedLines.length;
    });
  });

  if (diffFiles.length === 0) {
    console.log('\n[SARVAGHNA] No code changes detected in diff. Nothing to analyze.\n');
    process.exit(0);
  }

  // Tier 1: Core Secrets Scanner
  const secretFindings = config.rules?.secrets?.enabled ? scanSecrets(diffFiles, config) : [];

  // Tier 2: Core Architectural Invariants
  const archFindings = config.rules?.architecture?.enabled ? checkArchitecture(diffFiles, config) : [];

  // Tier 3: User Custom Rules Engine (loads *.rule.json and *.rule.mjs)
  const rulesDir = path.join(__dirname, 'rules');
  const customRuleFindings = await loadAndExecuteCustomRules(diffFiles, rulesDir);

  // Tier 4: Modular Skills Engine (loads tools/sarvaghna/skills/*)
  const skillsDir = path.join(__dirname, 'skills');
  const { findings: skillFindings, skillReports } = await loadAndExecuteSkills(diffFiles, skillsDir, { config, rawDiff });

  const allRuleFindings = [...secretFindings, ...archFindings, ...customRuleFindings, ...skillFindings];

  // Tier 5: AI Semantic Review (Gemini LLM)
  let aiReview = { enabled: false, reason: 'AI review disabled in config.' };
  if (config.rules?.aiReview?.enabled) {
    aiReview = await runSemanticReview(diffFiles, rawDiff, config);
  }

  const durationMs = Date.now() - startTime;

  // Compute Sovereign Decision on behalf of @pranavbharadwaj89
  const targetLabel = isStaged ? 'staged-changes' : `${baseCommit}..${headCommit}`;
  const decision = DecisionEngine.makeDecision({
    target: targetLabel,
    ruleFindings: allRuleFindings,
    aiReview,
    filesCount: diffFiles.length,
    linesAdded,
    linesDeleted
  });

  const hasCritical = decision.verdict === 'BLOCK_AND_PREVENT';

  const result = {
    ruleFindings: allRuleFindings,
    skillReports,
    aiReview,
    decision,
    durationMs,
    filesCount: diffFiles.length,
    linesAdded,
    linesDeleted,
    hasCritical
  };

  // Format and Output Results
  formatTerminalOutput(result);

  console.log(`👑 SARVAGHNA AUTONOMOUS DECISION: [${decision.verdict}] (Confidence: ${Math.round(decision.confidence * 100)}%)`);
  console.log(`   Mandate: Acting on behalf of @pranavbharadwaj89`);
  console.log(`   Rationale: ${decision.rationale}\n`);

  emitGitHubAnnotations(allRuleFindings);
  writeGitHubStepSummary(result);
  await postOrUpdatePRComment(result);

  // Exit with error code if blocking issues found
  if (hasCritical) {
    console.error('❌ SARVAGHNA Mandate: Action REJECTED. Fix the blocking issues above.\n');
    process.exit(1);
  } else {
    console.log('✅ SARVAGHNA Mandate: Action AUTHORIZED & Cleared.\n');
    process.exit(0);
  }
}

run().catch(err => {
  console.error(`[SARVAGHNA] Fatal execution error: ${err.stack || err.message}`);
  process.exit(1);
});
