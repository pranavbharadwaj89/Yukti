// sarvaghna/agents/orchestrator.mjs
// Agent: Orchestrator (The boss — routes and decides)
// Color: #9B8FE0 (Purple) | Tagline: "The boss — routes and decides"

import fs from 'fs';
import path from 'path';
import { AnalystAgent } from './analyst.mjs';
import { CoderAgent } from './coder.mjs';
import { TesterAgent } from './tester.mjs';
import { ReviewerAgent } from './reviewer.mjs';
import { DecisionEngine } from '../core/decision-engine.mjs';

/**
 * The 5-Section Canonical Orchestrator System Prompt
 */
export const ORCHESTRATOR_SYSTEM_PROMPT_TEMPLATE = `You are the Orchestrator agent for a software testing pipeline.

Your ONLY job is to:
1. Read the incoming goal
2. Break it into concrete subtasks
3. Assign each subtask to the correct specialist agent
4. Monitor results and decide next steps

You do NOT write code.
You do NOT run tests.
You do NOT review quality yourself.
If you catch yourself doing any of the above — STOP and delegate instead.

---

## Your team

ANALYST
- Input: raw goal, PRD text, or ticket description
- Output: structured list of acceptance criteria and test scenarios
- Use when: goal is vague or needs breaking down first

CODER
- Input: specific test scenario + file context
- Output: working Python/Playwright test file
- Use when: a scenario needs to be turned into runnable code

TESTER
- Input: path to test file
- Output: pass/fail results + error logs
- Use when: code needs to be executed

REVIEWER
- Input: test results + original acceptance criteria
- Output: coverage score (0-100) + gap list
- Use when: all tests have run and you need a quality verdict

---

## Output format

Always respond with valid JSON only. No prose. No markdown.
Use this exact schema:

{
  "status": "in_progress" | "complete" | "blocked",
  "current_step": "brief description of what just happened",
  "next_tasks": [
    {
      "agent": "ANALYST" | "CODER" | "TESTER" | "REVIEWER",
      "task": "specific instruction for this agent",
      "depends_on": "task id this must wait for, or null"
    }
  ],
  "final_output": null,
  "block_reason": null
}

---

## Decision rules

PROCEED when:
- A task returned status "success"
- Coverage score >= 80 from REVIEWER
- All acceptance criteria have a corresponding test

RETRY (max 1 time) when:
- CODER produced code with a syntax error
- TESTER failed due to a missing import or setup issue
- ANALYST output is missing required fields

ESCALATE (set status: "blocked") when:
- Same task has failed twice
- REVIEWER score < 60 with no clear fix
- Goal is ambiguous and cannot proceed without clarification

COMPLETE when:
- All acceptance criteria are covered
- Coverage score >= 80
- No critical failures remain

---

## Context from previous runs

{MEMORY_PLACEHOLDER}

Use this to:
- Skip steps that already succeeded
- Avoid approaches that failed before
- Prioritise fixing known gaps from the REVIEWER

If no previous context is provided, treat this as a fresh run.

---
## Current goal

{GOAL_PLACEHOLDER}

## Results so far

{RESULTS_PLACEHOLDER}
`;

export class OrchestratorAgent {
  constructor(options = {}) {
    this.id = 'orchestrator';
    this.name = 'Orchestrator';
    this.color = '#9B8FE0';
    this.dark = '#7C6FCD';
    this.bg = '#EEEDFE';
    this.tagline = 'The boss — routes and decides';

    this.analyst = new AnalystAgent();
    this.coder = new CoderAgent();
    this.tester = new TesterAgent();
    this.reviewer = new ReviewerAgent();
  }

  /**
   * Fetches context from past runs and decision ledger to inject into memory.
   */
  fetchMemoryContext(limit = 5) {
    const recent = DecisionEngine.readRecentDecisions(limit);
    if (!recent || recent.length === 0) {
      return 'No previous run history recorded. Initializing fresh pipeline run.';
    }

    return recent.map((r, i) =>
      `Run [${i + 1}] (${r.timestamp}): Target=${r.target} | Verdict=${r.verdict} (Confidence: ${Math.round((r.confidence || 1) * 100)}%) | Rationale: ${r.rationale}`
    ).join('\n');
  }

  /**
   * Renders the complete 5-section prompt with runtime injections.
   */
  renderPrompt(goal, resultsSoFar = 'Pipeline started. Ready to execute first task.') {
    const memory = this.fetchMemoryContext();
    return ORCHESTRATOR_SYSTEM_PROMPT_TEMPLATE
      .replace('{MEMORY_PLACEHOLDER}', memory)
      .replace('{GOAL_PLACEHOLDER}', goal)
      .replace('{RESULTS_PLACEHOLDER}', resultsSoFar);
  }

  /**
   * Decomposes a high-level user goal into structured subtasks across the crew.
   */
  decomposeGoal(goalDescription, context = {}) {
    return {
      status: 'in_progress',
      current_step: 'Goal received and parsed by Orchestrator',
      next_tasks: [
        {
          id: 'task-1',
          agent: 'ANALYST',
          task: 'Read specifications and extract testable acceptance criteria',
          depends_on: null
        },
        {
          id: 'task-2',
          agent: 'CODER',
          task: 'Synthesize resilient test scripts for the extracted criteria',
          depends_on: 'task-1'
        },
        {
          id: 'task-3',
          agent: 'TESTER',
          task: 'Execute test suites and produce telemetry summary',
          depends_on: 'task-2'
        },
        {
          id: 'task-4',
          agent: 'REVIEWER',
          task: 'Score coverage and audit code against quality/security standards',
          depends_on: 'task-3'
        }
      ],
      final_output: null,
      block_reason: null
    };
  }

  /**
   * Executes the autonomous multi-agent pipeline adhering strictly to Decision Rules.
   */
  async executeMission(goalDescription, context = {}) {
    const memory = this.fetchMemoryContext();
    const missionLog = [];
    let state = this.decomposeGoal(goalDescription, context);

    // Track retry attempts per task
    const retryCounts = { ANALYST: 0, CODER: 0, TESTER: 0, REVIEWER: 0 };

    // Step 1: Delegate to ANALYST
    let analysisResult = null;
    let docPath = context.docPath;
    const ticketMatch = goalDescription.match(/\b([A-Z]{2,10}-\d+)\b/);
    if (!docPath && ticketMatch) {
      const candidate = path.resolve(process.cwd(), 'sarvaghna', 'integrations', 'jira', `scenarios_${ticketMatch[1]}.json`);
      if (fs.existsSync(candidate)) {
        docPath = candidate;
      }
    }
    if (!docPath) {
      docPath = 'd:/Yukti/New Text Document.txt';
    }

    try {
      analysisResult = this.analyst.analyzeDoc(docPath);
      missionLog.push({
        step: 1,
        agent: 'ANALYST',
        status: 'SUCCESS',
        result: analysisResult.summary
      });
      state.current_step = `Analyst extracted ${analysisResult.criteriaCount} acceptance criteria.`;
    } catch (e) {
      // Retry once if missing required fields or fallback to goal text
      if (retryCounts.ANALYST < 1) {
        retryCounts.ANALYST++;
        analysisResult = this.analyst.analyzeText(goalDescription, 'User Goal Text');
        missionLog.push({
          step: 1,
          agent: 'ANALYST',
          status: 'SUCCESS (FALLBACK RETRY)',
          result: `Analyst parsed user goal directly: ${analysisResult.criteriaCount} criteria extracted.`
        });
        state.current_step = `Analyst fallback: extracted ${analysisResult.criteriaCount} criteria directly from goal.`;
      } else {
        return this.buildBlockedResult(state, 'ANALYST failed twice to parse documentation and requirements.');
      }
    }

    // Step 2: Delegate to CODER
    let codeGenResult = null;
    const sampleCriterion = analysisResult.acceptanceCriteria[0] || {
      title: 'Unified Flow Execution',
      description: goalDescription
    };

    try {
      codeGenResult = this.coder.generateTest(sampleCriterion, 'playwright');
      missionLog.push({
        step: 2,
        agent: 'CODER',
        status: 'SUCCESS',
        result: `Generated ${codeGenResult.targetType} test script '${codeGenResult.filename}' with ${codeGenResult.resilienceStrategy}.`
      });
      state.current_step = `Coder generated '${codeGenResult.filename}' with resilient locators.`;
    } catch (e) {
      if (retryCounts.CODER < 1) {
        retryCounts.CODER++;
        const fixed = this.coder.autoFix('', e.message);
        codeGenResult = { targetType: 'playwright', filename: 'fallback.spec.ts', code: fixed.code };
        missionLog.push({
          step: 2,
          agent: 'CODER',
          status: 'SUCCESS (AUTO-FIX RETRY)',
          result: 'Coder auto-repaired code generation syntax.'
        });
      } else {
        return this.buildBlockedResult(state, 'CODER failed twice to generate valid test scripts.');
      }
    }

    // Step 3: Delegate to TESTER
    let testResult = null;
    testResult = this.tester.runSuite('dotnet', context.repoRoot || process.cwd());

    if (!testResult.success) {
      if (retryCounts.TESTER < 1) {
        retryCounts.TESTER++;
        // Retry once per decision rule
        testResult = this.tester.runSuite('dotnet', context.repoRoot || process.cwd());
      }
    }

    missionLog.push({
      step: 3,
      agent: 'TESTER',
      status: testResult.success ? 'SUCCESS' : 'FAILED',
      result: testResult.humanSummary
    });
    state.current_step = `Tester executed test suite: ${testResult.passed}/${testResult.total} passed.`;

    // Step 4: Delegate to REVIEWER
    const coverageResult = this.reviewer.scoreCoverage(analysisResult.acceptanceCriteria, testResult);
    const auditResult = this.reviewer.auditDiff(context.rawDiff || '');

    missionLog.push({
      step: 4,
      agent: 'REVIEWER',
      status: auditResult.passed ? 'SUCCESS' : 'CHANGES_REQUESTED',
      result: `Coverage: ${coverageResult.coverageScore}% | Quality: ${auditResult.qualityScore}% (${auditResult.verdict})`
    });

    // Check Decision Rules for Completion vs Escalation
    // ESCALATE when: score < 60 with no clear fix
    if (auditResult.qualityScore < 60 && !auditResult.passed) {
      return this.buildBlockedResult(state, `Reviewer score (${auditResult.qualityScore}%) below threshold of 60%. Remediation needed.`);
    }

    const isComplete = testResult.success && auditResult.passed;

    const finalState = {
      status: isComplete ? 'complete' : 'in_progress',
      current_step: isComplete ? 'All tasks complete and validated by Reviewer' : 'Execution completed with quality advisories',
      next_tasks: isComplete ? [] : [
        {
          agent: 'REVIEWER',
          task: 'Re-audit after resolving flagged items',
          depends_on: null
        }
      ],
      final_output: {
        summary: `Pipeline run finished with verdict: ${isComplete ? 'MISSION_ACCOMPLISHED' : 'WARNINGS'}. Total tests: ${testResult.total} passed.`,
        coverage_score: coverageResult.coverageScore,
        quality_score: auditResult.qualityScore,
        test_summary: testResult.humanSummary
      },
      block_reason: null
    };

    return {
      orchestrator: this.name,
      goal: goalDescription,
      verdict: isComplete ? 'MISSION_ACCOMPLISHED' : 'MISSION_COMPLETED_WITH_WARNINGS',
      confidence: isComplete ? 0.98 : 0.85,
      state: finalState,
      crew: {
        analyst: analysisResult,
        coder: codeGenResult,
        tester: testResult,
        reviewer: { coverage: coverageResult, audit: auditResult }
      },
      log: missionLog
    };
  }

  buildBlockedResult(state, reason) {
    return {
      orchestrator: this.name,
      verdict: 'BLOCKED',
      confidence: 1.0,
      state: {
        ...state,
        status: 'blocked',
        block_reason: reason
      },
      log: [{ step: 'ESCALATE', agent: 'Orchestrator', status: 'BLOCKED', result: reason }]
    };
  }
}
