// sarvaghna/agents/orchestrator.mjs
// Agent: Orchestrator (The boss — routes and decides)
// Color: #9B8FE0 (Purple) | Tagline: "The boss — routes and decides"

import { AnalystAgent } from './analyst.mjs';
import { CoderAgent } from './coder.mjs';
import { TesterAgent } from './tester.mjs';
import { ReviewerAgent } from './reviewer.mjs';

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
   * Decomposes a high-level user goal into structured subtasks across the crew.
   * @param {string} goalDescription - Goal prompt from user or CI.
   * @param {Object} context - Context files, doc paths, or diffs.
   */
  decomposeGoal(goalDescription, context = {}) {
    const tasks = [
      {
        id: 'task-1',
        title: 'Requirement & Spec Analysis',
        assignedTo: 'analyst',
        agent: this.analyst.name,
        color: this.analyst.color,
        description: 'Read input specifications, PRDs, and architecture documents; extract testable acceptance criteria.',
        input: context.docPath || context.text || 'd:/Yukti/New Text Document.txt'
      },
      {
        id: 'task-2',
        title: 'Test & Code Generation',
        assignedTo: 'coder',
        agent: this.coder.name,
        color: this.coder.color,
        description: 'Synthesize resilient Playwright, API, or .NET test scenarios satisfying the extracted criteria.',
        input: 'Requirements from Analyst'
      },
      {
        id: 'task-3',
        title: 'Suite Execution & Telemetry Verification',
        assignedTo: 'tester',
        agent: this.tester.name,
        color: this.tester.color,
        description: 'Execute local test runners, measure duration, verify zero regressions, and summarize failures.',
        input: 'Test runners in repository'
      },
      {
        id: 'task-4',
        title: 'Quality & Architectural Scoring',
        assignedTo: 'reviewer',
        agent: this.reviewer.name,
        color: this.reviewer.color,
        description: 'Score test coverage against acceptance criteria and verify secrets/architectural compliance.',
        input: 'Git diff and test telemetry'
      }
    ];

    return {
      agent: this.name,
      goal: goalDescription,
      tasksCount: tasks.length,
      tasks
    };
  }

  /**
   * Executes the autonomous multi-agent pipeline end-to-end.
   */
  async executeMission(goalDescription, context = {}) {
    const plan = this.decomposeGoal(goalDescription, context);
    const missionLog = [];

    // Step 1: Analyst
    let analysisResult = null;
    const docPath = context.docPath || 'd:/Yukti/New Text Document.txt';
    try {
      analysisResult = this.analyst.analyzeDoc(docPath);
      missionLog.push({
        step: 1,
        agent: 'Analyst',
        status: 'SUCCESS',
        result: analysisResult.summary
      });
    } catch (e) {
      analysisResult = this.analyst.analyzeText(goalDescription, 'User Goal');
      missionLog.push({
        step: 1,
        agent: 'Analyst',
        status: 'FALLBACK',
        result: `Parsed user prompt directly (${analysisResult.criteriaCount} criteria extracted).`
      });
    }

    // Step 2: Coder
    const sampleCriterion = analysisResult.acceptanceCriteria[0] || {
      title: 'Unified Flow Execution',
      description: goalDescription
    };
    const codeGenResult = this.coder.generateTest(sampleCriterion, 'playwright');
    missionLog.push({
      step: 2,
      agent: 'Coder',
      status: 'SUCCESS',
      result: `Generated ${codeGenResult.targetType} test script '${codeGenResult.filename}' with ${codeGenResult.resilienceStrategy}.`
    });

    // Step 3: Tester
    const testResult = this.tester.runSuite('dotnet', context.repoRoot || process.cwd());
    missionLog.push({
      step: 3,
      agent: 'Tester',
      status: testResult.success ? 'PASSED' : 'FAILED',
      result: testResult.humanSummary
    });

    // Step 4: Reviewer
    const coverageResult = this.reviewer.scoreCoverage(analysisResult.acceptanceCriteria, testResult);
    const auditResult = this.reviewer.auditDiff(context.rawDiff || '');
    missionLog.push({
      step: 4,
      agent: 'Reviewer',
      status: auditResult.passed ? 'APPROVED' : 'CHANGES_REQUESTED',
      result: `Coverage: ${coverageResult.coverageScore}% | Quality Score: ${auditResult.qualityScore}% (${auditResult.verdict})`
    });

    const allSuccessful = testResult.success && auditResult.passed;

    return {
      orchestrator: this.name,
      goal: goalDescription,
      verdict: allSuccessful ? 'MISSION_ACCOMPLISHED' : 'MISSION_COMPLETED_WITH_WARNINGS',
      confidence: allSuccessful ? 0.98 : 0.85,
      crew: {
        analyst: analysisResult,
        coder: codeGenResult,
        tester: testResult,
        reviewer: { coverage: coverageResult, audit: auditResult }
      },
      log: missionLog
    };
  }
}
