// sarvaghna/core/decision-engine.mjs
// The Autonomous Decision Engine of SARVAGHNA

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';
import { PolicyMatrix } from './policy-matrix.mjs';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const ledgerDir = path.resolve(__dirname, '..', 'ledger');
const ledgerFile = path.join(ledgerDir, 'decisions.jsonl');

export const DecisionEngine = {
  makeDecision(context) {
    const { target, ruleFindings = [], aiReview = {}, filesCount = 0, linesAdded = 0, linesDeleted = 0 } = context;

    // 1. Evaluate policy
    const policyResult = PolicyMatrix.evaluate(ruleFindings, aiReview);

    const decisionRecord = {
      timestamp: new Date().toISOString(),
      actor: 'SARVAGHNA (सर्वज्ञ)',
      mandate: 'pranavbharadwaj89',
      target: target || 'working-tree',
      verdict: policyResult.verdict,
      prStatus: policyResult.prStatus,
      confidence: policyResult.confidence,
      rationale: policyResult.rationale,
      blockers: policyResult.blockers,
      metrics: {
        filesScanned: filesCount,
        added: linesAdded,
        deleted: linesDeleted,
        findingsCount: ruleFindings.length
      }
    };

    // 2. Commit to Immutable Ledger
    this.recordToLedger(decisionRecord);

    return decisionRecord;
  },

  recordToLedger(record) {
    try {
      if (!fs.existsSync(ledgerDir)) {
        fs.mkdirSync(ledgerDir, { recursive: true });
      }
      fs.appendFileSync(ledgerFile, JSON.stringify(record) + '\n', 'utf8');
    } catch (err) {
      console.warn(`[SARVAGHNA] Failed to write decision to ledger: ${err.message}`);
    }
  },

  readRecentDecisions(limit = 10) {
    if (!fs.existsSync(ledgerFile)) return [];
    try {
      const lines = fs.readFileSync(ledgerFile, 'utf8').trim().split('\n');
      return lines.slice(-limit).map(l => JSON.parse(l));
    } catch (e) {
      return [];
    }
  }
};
