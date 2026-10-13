import fs from 'fs';
import { execSync } from 'child_process';
import path from 'path';

export class TesterAgent {
  constructor(options = {}) {
    this.id = 'tester';
    this.name = 'Tester';
    this.color = '#EF9F27';
    this.dark = '#854F0B';
    this.bg = '#FAEEDA';
    this.tagline = 'Runs tests and reports results';
  }

  /**
   * Executes the real test suite using local CLI runner.
   * @param {'dotnet' | 'frontend' | 'pytest' | 'all'} suiteType
   * @param {string} cwd - Working directory.
   */
  runSuite(suiteType = 'dotnet', cwd = process.cwd()) {
    // Normal console verbosity is what prints one "Passed <Name> [n ms]" line
    // per test; the default minimal verbosity only prints assembly totals.
    let command = 'dotnet test --logger "console;verbosity=normal"';
    if (suiteType === 'frontend') {
      command = 'npm.cmd test -- --run';
    } else if (suiteType === 'pytest') {
      command = 'python run_tester.py coder_results.json';
    }

    const startTime = Date.now();
    let stdout = '';
    let stderr = '';
    let exitCode = 0;

    try {
      stdout = execSync(command, {
        cwd,
        encoding: 'utf8',
        stdio: ['pipe', 'pipe', 'pipe'],
        maxBuffer: 10 * 1024 * 1024
      });
    } catch (err) {
      exitCode = err.status || 1;
      stdout = err.stdout ? err.stdout.toString() : '';
      stderr = err.stderr ? err.stderr.toString() : err.message;
    }

    const durationMs = Date.now() - startTime;
    return this.parseAndSummarize(command, suiteType, exitCode, stdout, stderr, durationMs, cwd);
  }

  /**
   * Parses test output and formats a clear, human-readable summary.
   */
  parseAndSummarize(command, suiteType, exitCode, stdout, stderr, durationMs, cwd = process.cwd()) {
    let passed = 0;
    let failed = 0;
    let skipped = 0;
    let total = 0;

    if (suiteType === 'pytest') {
      const reportPath = path.resolve(cwd, 'tester_report.json');
      if (fs.existsSync(reportPath)) {
        try {
          const report = JSON.parse(fs.readFileSync(reportPath, 'utf8'));
          let pCount = 0;
          let fCount = 0;
          (report.results || []).forEach(r => {
            pCount += (r.tests_passed || 0);
            fCount += (r.tests_failed || 0);
          });
          return {
            agent: this.name,
            suiteType,
            command,
            exitCode,
            success: report.status === 'all_passed',
            total: pCount + fCount,
            passed: pCount,
            failed: fCount,
            skipped: 0,
            durationMs,
            humanSummary: report.summary || `Pytest suite finished with status: ${report.status}`,
            structuredReport: report,
            failureDetails: (report.results || []).flatMap(r => (r.failures || []).map(f => `${f.test_name}: ${f.message}`))
          };
        } catch {
          // fallback to stdout
        }
      }
    }

    // Parse dotnet test output: "Passed!  - Failed: 0, Passed: 17, Skipped: 0, Total: 17"
    const matches = [...stdout.matchAll(/Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)/g)];
    if (matches.length > 0) {
      for (const m of matches) {
        failed += parseInt(m[1], 10);
        passed += parseInt(m[2], 10);
        skipped += parseInt(m[3], 10);
        total += parseInt(m[4], 10);
      }
    } else if (exitCode === 0) {
      passed = 1;
      total = 1;
    } else {
      failed = 1;
      total = 1;
    }

    // Also capture individual test case names ("Passed Namespace.Class.Method [12 ms]")
    // so downstream agents (Reviewer) can match criteria against real tests
    // instead of aggregate counts alone.
    const testCases = [];
    for (const m of stdout.matchAll(/^\s*(Passed|Failed|Skipped)\s+([^\s\[]+)/gm)) {
      testCases.push({ name: m[2], status: m[1] });
    }

    // Normal verbosity prints "Total tests: N / Passed: N" instead of the
    // one-line minimal summary the regex above expects — derive totals from
    // the real per-test lines rather than falling back to a fake "1 test".
    if (matches.length === 0 && testCases.length > 0) {
      passed = testCases.filter(t => t.status === 'Passed').length;
      failed = testCases.filter(t => t.status === 'Failed').length;
      skipped = testCases.filter(t => t.status === 'Skipped').length;
      total = testCases.length;
    }

    const humanSummary = exitCode === 0
      ? `All ${total} tests passed cleanly across all test assemblies in ${(durationMs / 1000).toFixed(1)}s.`
      : `Test run failed with ${failed} failure(s) out of ${total} total tests.`;

    const failureDetails = [];
    if (exitCode !== 0) {
      const errorLines = (stdout + '\n' + stderr).split('\n')
        .filter(l => l.includes('error CS') || l.includes('Failed') || l.includes('Exception:'));
      failureDetails.push(...errorLines.slice(0, 5));
    }

    return {
      agent: this.name,
      suiteType,
      command,
      exitCode,
      success: exitCode === 0,
      total,
      passed,
      failed,
      skipped,
      durationMs,
      humanSummary,
      failureDetails,
      testCases
    };
  }

  /**
   * Diagnoses reasons behind test flakiness or failure given error and context.
   */
  diagnoseFlakiness(errorMessage, domSnippet = '') {
    const reasons = [];

    if (errorMessage.includes('Timeout') || errorMessage.includes('timed out')) {
      reasons.push({
        cause: 'Asynchronous Rendering Delay',
        remedy: 'Use auto-waiting assertions (e.g. expect(locator).toBeVisible()) instead of fixed sleeps.'
      });
    }

    if (domSnippet && errorMessage.includes('strict mode violation')) {
      reasons.push({
        cause: 'Ambiguous Selector',
        remedy: 'Locator resolved to multiple DOM nodes. Scope locator with role or specific parent container.'
      });
    }

    if (reasons.length === 0) {
      reasons.push({
        cause: 'Assertion Mismatch',
        remedy: 'Examine API response or UI state right before assertion boundary.'
      });
    }

    return {
      agent: this.name,
      diagnoses: reasons
    };
  }
}
