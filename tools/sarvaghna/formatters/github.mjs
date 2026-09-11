// tools/sarvaghna/formatters/github.mjs
// GitHub Actions Step Summary, Annotations, and PR Review Comment Formatter

import fs from 'fs';

export function formatTerminalOutput(result) {
  const { ruleFindings, aiReview, durationMs, filesCount, linesAdded, linesDeleted } = result;

  console.log('\n================================================================');
  console.log('              SARVAGHNA (सर्वज्ञ) — CODE ANALYSIS REPORT        ');
  console.log('================================================================');
  console.log(`Inspected: ${filesCount} files (+${linesAdded} / -${linesDeleted} lines) in ${durationMs}ms\n`);

  if (ruleFindings.length === 0) {
    console.log('✅ Rule Inspection: PASSED — Zero security or architectural violations detected.');
  } else {
    console.log(`⚠️ Rule Inspection: ${ruleFindings.length} issue(s) detected:`);
    for (const f of ruleFindings) {
      const icon = f.severity === 'CRITICAL' ? '🚨' : f.severity === 'HIGH' ? '❌' : '⚠️';
      console.log(`  ${icon} [${f.severity}] ${f.file}:${f.line} — ${f.title}`);
      console.log(`     ${f.message}`);
      if (f.snippet) {
        console.log(`     > ${f.snippet}`);
      }
      console.log('');
    }
  }

  console.log('----------------------------------------------------------------');
  if (aiReview.enabled) {
    console.log(`🧠 AI Semantic Review: [${aiReview.verdict}]`);
    console.log(`   ${aiReview.summary}\n`);
    if (aiReview.strengths?.length > 0) {
      console.log('   Highlights:');
      aiReview.strengths.forEach(s => console.log(`     • ${s}`));
      console.log('');
    }
    if (aiReview.lineReviews?.length > 0) {
      console.log('   Inline Observations:');
      aiReview.lineReviews.forEach(r => {
        console.log(`     [${r.severity}] ${r.file}:${r.line} — ${r.comment}`);
        if (r.suggestion) console.log(`       Suggestion: ${r.suggestion}`);
      });
      console.log('');
    }
  } else {
    console.log(`ℹ️ AI Semantic Review: Skipped (${aiReview.reason})`);
  }
  console.log('================================================================\n');
}

export function writeGitHubStepSummary(result) {
  const summaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (!summaryPath) return;

  const { ruleFindings, aiReview, durationMs, filesCount, linesAdded, linesDeleted, hasCritical } = result;

  let md = `## 👁️ SARVAGHNA (सर्वज्ञ) Code Analysis Report\n\n`;
  md += `**Status:** ${hasCritical ? '❌ **CHANGES REQUESTED (Blocking Issues Found)**' : '✅ **PASSED QUALITY GATES**'}\n\n`;
  md += `* **Files Scanned:** \`${filesCount}\` (\`+${linesAdded}\` / \`-${linesDeleted}\` lines)\n`;
  md += `* **Scan Duration:** \`${durationMs}ms\`\n`;
  md += `* **Deterministic Rule Findings:** \`${ruleFindings.length}\`\n\n`;

  // Deterministic Findings Table
  if (ruleFindings.length > 0) {
    md += `### 🚨 Rule & Architecture Findings\n\n`;
    md += `| Severity | Location | Rule | Description |\n`;
    md += `| :--- | :--- | :--- | :--- |\n`;
    for (const f of ruleFindings) {
      const badge = f.severity === 'CRITICAL' ? '🔴 CRITICAL' : f.severity === 'HIGH' ? '🟠 HIGH' : '🟡 WARNING';
      md += `| ${badge} | \`${f.file}:${f.line}\` | **${f.title}** | ${f.message} |\n`;
    }
    md += `\n`;
  } else {
    md += `> ✅ **Zero security vulnerabilities or architectural violations detected.**\n\n`;
  }

  // AI Semantic Review
  md += `### 🧠 AI Semantic Review\n\n`;
  if (aiReview.enabled) {
    const verdictBadge = aiReview.verdict === 'APPROVED' ? '🟢 APPROVED' : aiReview.verdict === 'REQUEST_CHANGES' ? '🔴 CHANGES REQUESTED' : '🟡 COMMENT';
    md += `**Verdict:** ${verdictBadge}\n\n`;
    md += `> ${aiReview.summary}\n\n`;

    if (aiReview.strengths?.length > 0) {
      md += `**Key Strengths:**\n`;
      aiReview.strengths.forEach(s => md += `* ${s}\n`);
      md += `\n`;
    }

    if (aiReview.lineReviews?.length > 0) {
      md += `**Line-Level Suggestions:**\n\n`;
      for (const r of aiReview.lineReviews) {
        md += `* **\`${r.file}:${r.line}\`** (${r.severity}): ${r.comment}\n`;
        if (r.suggestion) {
          md += `  \`\`\`suggestion\n  ${r.suggestion}\n  \`\`\`\n`;
        }
      }
      md += `\n`;
    }
  } else {
    md += `*AI Semantic Review was skipped: ${aiReview.reason}*\n\n`;
  }

  md += `---\n*Generated automatically by SARVAGHNA — Yukti Quality Agent*\n`;

  try {
    fs.appendFileSync(summaryPath, md, 'utf8');
  } catch (err) {
    console.error(`Failed to write to GITHUB_STEP_SUMMARY: ${err.message}`);
  }
}

export function emitGitHubAnnotations(ruleFindings) {
  if (!process.env.GITHUB_ACTIONS) return;

  for (const f of ruleFindings) {
    const command = f.severity === 'CRITICAL' || f.severity === 'HIGH' ? 'error' : 'warning';
    // GitHub Actions command format: ::error file={name},line={line},title={title}::{message}
    console.log(`::${command} file=${f.file},line=${f.line},title=${f.title}::${f.message}`);
  }
}
