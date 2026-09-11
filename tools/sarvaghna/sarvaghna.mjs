#!/usr/bin/env node
// tools/sarvaghna/sarvaghna.mjs
// SARVAGHNA (सर्वज्ञ) — Omniscient Code Analysis & Review Agent for Yukti

import { execSync } from 'child_process';
import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

import { scanSecrets } from './rules/secrets.mjs';
import { checkArchitecture } from './rules/architecture.mjs';
import { runSemanticReview } from './rules/semantic-reviewer.mjs';
import { formatTerminalOutput, writeGitHubStepSummary, emitGitHubAnnotations } from './formatters/github.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// 1. Load Configuration
const configPath = path.join(__dirname, 'sarvaghna.config.json');
let config = {};
try {
  config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
} catch (e) {
  console.warn(`[SARVAGHNA] Warning: Could not read config file, using defaults: ${e.message}`);
}

// 2. Parse CLI Arguments
const args = process.argv.slice(2);
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
    // Default to comparing against previous commit or HEAD
    baseCommit = 'HEAD~1';
  }
}

// 3. Obtain Git Diff
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
  // If base commit not found (e.g. initial commit or shallow clone), fallback to HEAD diff
  try {
    rawDiff = execSync('git diff HEAD -U3', { encoding: 'utf8', maxBuffer: 10 * 1024 * 1024 });
  } catch (fallbackErr) {
    console.warn(`[SARVAGHNA] Warning: git diff failed: ${fallbackErr.message}`);
    rawDiff = '';
  }
}

// 4. Parse Git Diff
function parseGitDiff(diffText) {
  const files = [];
  if (!diffText || diffText.trim().length === 0) return files;

  const rawFiles = diffText.split(/^diff --git /m);

  for (const rf of rawFiles) {
    if (!rf.trim()) continue;

    const lines = rf.split(/\r?\n/);
    const headerLine = lines[0]; // e.g. "a/path/file.cs b/path/file.cs"
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
        // e.g. @@ -10,5 +12,7 @@
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

// 5. Execute Analysis Pipeline
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

  // Tier 1: Secrets Scanning
  const secretFindings = config.rules?.secrets?.enabled ? scanSecrets(diffFiles, config) : [];

  // Tier 2 & 3: Architectural and Playbook Invariants
  const archFindings = config.rules?.architecture?.enabled ? checkArchitecture(diffFiles, config) : [];

  const allRuleFindings = [...secretFindings, ...archFindings];

  // Tier 4: AI Semantic Review
  let aiReview = { enabled: false, reason: 'AI review disabled in config.' };
  if (config.rules?.aiReview?.enabled) {
    aiReview = await runSemanticReview(diffFiles, rawDiff, config);
  }

  const durationMs = Date.now() - startTime;
  const hasCritical = allRuleFindings.some(f => f.severity === 'CRITICAL' || f.severity === 'HIGH') ||
                      (aiReview.enabled && aiReview.verdict === 'REQUEST_CHANGES');

  const result = {
    ruleFindings: allRuleFindings,
    aiReview,
    durationMs,
    filesCount: diffFiles.length,
    linesAdded,
    linesDeleted,
    hasCritical
  };

  // Format and Output Results
  formatTerminalOutput(result);
  emitGitHubAnnotations(allRuleFindings);
  writeGitHubStepSummary(result);

  // Exit with error code if blocking issues found
  if (hasCritical) {
    console.error('❌ SARVAGHNA Check Failed: Blocking quality, security, or architectural issues detected.\n');
    process.exit(1);
  } else {
    console.log('✅ SARVAGHNA Check Passed: All quality gates cleared successfully.\n');
    process.exit(0);
  }
}

run().catch(err => {
  console.error(`[SARVAGHNA] Fatal execution error: ${err.stack || err.message}`);
  process.exit(1);
});
