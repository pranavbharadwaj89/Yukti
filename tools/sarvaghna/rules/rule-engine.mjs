// tools/sarvaghna/rules/rule-engine.mjs
// Dynamic Rules Engine: Discovers and executes custom user-defined rules (.rule.json and .rule.mjs)

import fs from 'fs';
import path from 'path';
import { pathToFileURL } from 'url';

export async function loadAndExecuteCustomRules(diffFiles, rulesDir) {
  const findings = [];
  if (!fs.existsSync(rulesDir)) return findings;

  const entries = fs.readdirSync(rulesDir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(rulesDir, entry.name);

    if (entry.isDirectory()) {
      // Recurse into subdirectories (e.g. custom/)
      const subFindings = await loadAndExecuteCustomRules(diffFiles, fullPath);
      findings.push(...subFindings);
      continue;
    }

    // 1. Declarative JSON rules (*.rule.json)
    if (entry.name.endsWith('.rule.json')) {
      try {
        const raw = fs.readFileSync(fullPath, 'utf8');
        const ruleDef = JSON.parse(raw);
        const ruleFindings = evaluateJsonRule(ruleDef, diffFiles);
        findings.push(...ruleFindings);
      } catch (err) {
        console.warn(`[SARVAGHNA] Failed to load JSON rule ${entry.name}: ${err.message}`);
      }
    }

    // 2. Executable JavaScript rules (*.rule.mjs)
    if (entry.name.endsWith('.rule.mjs')) {
      try {
        const fileUrl = pathToFileURL(fullPath).href;
        const mod = await import(fileUrl);
        const rule = mod.default || mod.rule;
        if (rule && typeof rule.evaluate === 'function') {
          const ruleFindings = rule.evaluate(diffFiles) || [];
          findings.push(...ruleFindings);
        }
      } catch (err) {
        console.warn(`[SARVAGHNA] Failed to execute custom rule ${entry.name}: ${err.message}`);
      }
    }
  }

  return findings;
}

function evaluateJsonRule(rule, diffFiles) {
  const findings = [];
  const regex = new RegExp(rule.regex, rule.flags || '');
  const fileGlob = rule.filePattern || '*';

  for (const file of diffFiles) {
    const normalizedPath = file.path.replace(/\\/g, '/');

    // Check if file matches filePattern
    if (fileGlob !== '*') {
      if (fileGlob.startsWith('*.')) {
        const ext = fileGlob.replace('*.', '.');
        if (!normalizedPath.endsWith(ext)) continue;
      } else if (!normalizedPath.includes(fileGlob.replace('/**/*', ''))) {
        continue;
      }
    }

    for (const chunk of file.chunks) {
      for (const line of chunk.addedLines) {
        if (regex.test(line.content)) {
          findings.push({
            file: file.path,
            line: line.newLineNumber,
            rule: rule.id || `CUSTOM_RULE:${rule.name?.toUpperCase().replace(/\s+/g, '_') || 'RULE'}`,
            title: rule.name || 'Custom Rule Violation',
            message: rule.message || 'Custom rule check failed.',
            severity: rule.severity || 'WARNING',
            snippet: line.content.trim()
          });
        }
      }
    }
  }

  return findings;
}
