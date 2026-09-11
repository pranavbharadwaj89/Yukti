// sarvaghna/core/policy-matrix.mjs
// Executive Policy Matrix defining criteria for SARVAGHNA's autonomous decisions

export const PolicyMatrix = {
  // Decision outcomes
  DECISIONS: {
    AUTHORIZE_PASS: 'AUTHORIZE_PASS',
    BLOCK_AND_PREVENT: 'BLOCK_AND_PREVENT',
    NEEDS_ATTENTION: 'NEEDS_ATTENTION'
  },

  evaluate(ruleFindings, aiReview) {
    const criticalIssues = ruleFindings.filter(f => f.severity === 'CRITICAL');
    const highIssues = ruleFindings.filter(f => f.severity === 'HIGH');
    const warningIssues = ruleFindings.filter(f => f.severity === 'WARNING');

    // 1. Zero tolerance on CRITICAL (secrets, domain corruption)
    if (criticalIssues.length > 0) {
      return {
        verdict: this.DECISIONS.BLOCK_AND_PREVENT,
        confidence: 1.0,
        prStatus: 'REQUEST_CHANGES',
        rationale: `Blocking action taken: ${criticalIssues.length} CRITICAL security/architectural violation(s) detected.`,
        blockers: criticalIssues.map(i => `${i.title} at ${i.file}:${i.line}`)
      };
    }

    // 2. Strict blocking on HIGH (deadlocks, sync-over-async, config secrets)
    if (highIssues.length > 0) {
      return {
        verdict: this.DECISIONS.BLOCK_AND_PREVENT,
        confidence: 0.95,
        prStatus: 'REQUEST_CHANGES',
        rationale: `Blocking action taken: ${highIssues.length} HIGH-severity defect(s) detected violating Volume 5 standards.`,
        blockers: highIssues.map(i => `${i.title} at ${i.file}:${i.line}`)
      };
    }

    // 3. AI review veto
    if (aiReview?.enabled && aiReview.verdict === 'REQUEST_CHANGES') {
      return {
        verdict: this.DECISIONS.BLOCK_AND_PREVENT,
        confidence: 0.90,
        prStatus: 'REQUEST_CHANGES',
        rationale: `AI Semantic Analysis advised against merging: ${aiReview.summary}`,
        blockers: (aiReview.lineReviews || []).map(r => `${r.comment} at ${r.file}:${r.line}`)
      };
    }

    // 4. Warnings only -> Pass with advisory
    if (warningIssues.length > 0) {
      return {
        verdict: this.DECISIONS.AUTHORIZE_PASS,
        confidence: 0.88,
        prStatus: 'COMMENT',
        rationale: `Authorized with ${warningIssues.length} non-blocking advisory note(s).`,
        blockers: []
      };
    }

    // 5. Clean bill of health
    return {
      verdict: this.DECISIONS.AUTHORIZE_PASS,
      confidence: 0.99,
      prStatus: 'APPROVE',
      rationale: 'Clean execution. Zero security leaks, architectural invariants verified, Volume 5 compliant.',
      blockers: []
    };
  }
};
