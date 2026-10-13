#!/usr/bin/env node
// SARVAGHNA — report content generator.
// Two modes:
//   node generate_report.mjs update  -> one push just landed on main
//   node generate_report.mjs digest  -> periodic status digest, independent of pushes
// Prints {"subject": "...", "bodyText": "...", "bodyHtml": "..."} as JSON to stdout.

import fs from 'fs';
import path from 'path';
import { execSync } from 'child_process';
import { fileURLToPath } from 'url';
import { DecisionEngine } from '../core/decision-engine.mjs';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..', '..');

function latestCrewMission() {
  const recent = DecisionEngine.readRecentDecisions(50);
  return [...recent].reverse().find(r => r.actor === 'SARVAGHNA (सर्वज्ञ) Crew') || null;
}

function sh(cmd, fallback = '') {
  try {
    return execSync(cmd, { cwd: repoRoot, encoding: 'utf8' }).trim();
  } catch {
    return fallback;
  }
}

function readProgress() {
  const file = path.join(repoRoot, 'docs', 'PROGRESS.md');
  if (!fs.existsSync(file)) return { headline: '(docs/PROGRESS.md not found)', openTable: '' };
  const text = fs.readFileSync(file, 'utf8').replace(/\r\n/g, '\n');

  const headlineMatch = text.match(/^Overall:.*$/m);
  const headline = headlineMatch ? headlineMatch[0].replace(/\*\*/g, '') : '(no Overall line found)';

  const openSection = text.match(/## What's still open\n([\s\S]*?)(\n##|\n$)/);
  const openTable = openSection ? openSection[1].trim() : '(no open-items table found)';

  return { headline, openTable };
}

function markdownTableToText(tbl) {
  return tbl
    .split('\n')
    .filter((l) => l.trim() && !/^\|\s*---/.test(l))
    .map((l) => l.replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => c.trim()).join(' — '))
    .join('\n');
}

function markdownTableToHtml(tbl) {
  const rows = tbl
    .split('\n')
    .filter((l) => l.trim() && !/^\|\s*---/.test(l))
    .map((l) => l.replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => c.trim()));
  if (rows.length === 0) return '<p>(none)</p>';
  const [header, ...body] = rows;
  const th = header.map((c) => `<th style="text-align:left;padding:4px 8px;border-bottom:1px solid #ccc">${c}</th>`).join('');
  const trs = body
    .map((r) => `<tr>${r.map((c) => `<td style="padding:4px 8px;border-bottom:1px solid #eee">${c}</td>`).join('')}</tr>`)
    .join('');
  return `<table style="border-collapse:collapse;font-size:13px">${`<tr>${th}</tr>`}${trs}</table>`;
}

const mode = process.argv[2] || 'update';
const repoSlug = process.env.GITHUB_REPOSITORY || 'pranavbharadwaj89/Yukti';
const serverUrl = process.env.GITHUB_SERVER_URL || 'https://github.com';
const runId = process.env.GITHUB_RUN_ID;
const runUrl = runId ? `${serverUrl}/${repoSlug}/actions/runs/${runId}` : `${serverUrl}/${repoSlug}`;

const { headline, openTable } = readProgress();

let subject, bodyText, bodyHtml;

if (mode === 'digest') {
  const today = new Date().toISOString().slice(0, 10);
  const recentCommits = sh('git log -10 --date=short --format="%h %ad %s"');
  const crew = latestCrewMission();

  const crewText = crew
    ? [
        `Crew mission (${crew.timestamp}): ${crew.verdict} (confidence ${Math.round(crew.confidence * 100)}%)`,
        `  Coverage: ${crew.metrics?.coverageScore ?? 'n/a'}% | Quality: ${crew.metrics?.qualityScore ?? 'n/a'}% | Tests: ${crew.metrics?.testsPassed ?? '?'}/${crew.metrics?.testsTotal ?? '?'} | Analysis: ${crew.metrics?.analysisMode ?? 'n/a'}`,
        `  ${crew.rationale}`
      ].join('\n')
    : 'Crew mission: did not run this cycle.';

  const crewHtml = crew
    ? `<p><b>Crew mission</b> (${crew.timestamp}): <b>${crew.verdict}</b> (confidence ${Math.round(crew.confidence * 100)}%)<br/>
         Coverage: ${crew.metrics?.coverageScore ?? 'n/a'}% | Quality: ${crew.metrics?.qualityScore ?? 'n/a'}% | Tests: ${crew.metrics?.testsPassed ?? '?'}/${crew.metrics?.testsTotal ?? '?'} | Analysis: ${crew.metrics?.analysisMode ?? 'n/a'}<br/>
         <i>${crew.rationale}</i></p>`
    : '<p><b>Crew mission:</b> did not run this cycle.</p>';

  subject = `[Yukti] Daily Status Digest — ${today}`;
  bodyText = [
    `SARVAGHNA daily digest for Yukti (${today})`,
    '',
    headline,
    '',
    'Still open:',
    markdownTableToText(openTable),
    '',
    crewText,
    '',
    'Last 10 commits on main:',
    recentCommits,
    '',
    `Repo: ${serverUrl}/${repoSlug}`,
  ].join('\n');

  bodyHtml = `
    <h2>SARVAGHNA Daily Digest — Yukti</h2>
    <p><b>${headline}</b></p>
    <h3>Still open</h3>
    ${markdownTableToHtml(openTable)}
    <h3>5-Agent Crew</h3>
    ${crewHtml}
    <h3>Last 10 commits on main</h3>
    <pre style="font-size:12px">${recentCommits.replace(/</g, '&lt;')}</pre>
    <p><a href="${serverUrl}/${repoSlug}">${repoSlug}</a></p>
  `;
} else {
  const sha = (process.env.GITHUB_SHA || sh('git rev-parse HEAD')).slice(0, 7);
  const commitMsg = sh('git log -1 --format="%s"');
  const author = sh('git log -1 --format="%an (%ae)"');

  subject = `[Yukti] Update pushed to main — ${sha}: ${commitMsg}`;
  bodyText = [
    `New commit landed on Yukti main.`,
    '',
    `Commit: ${sha} — ${commitMsg}`,
    `Author: ${author}`,
    '',
    headline,
    '',
    `CI/SARVAGHNA run: ${runUrl}`,
  ].join('\n');

  bodyHtml = `
    <h2>SARVAGHNA — Yukti Update</h2>
    <p><b>Commit:</b> ${sha} — ${commitMsg}<br/>
       <b>Author:</b> ${author}</p>
    <p><b>${headline}</b></p>
    <p><a href="${runUrl}">View CI / SARVAGHNA run</a></p>
  `;
}

process.stdout.write(JSON.stringify({ subject, bodyText, bodyHtml }));
