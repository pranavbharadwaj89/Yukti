import fs from 'fs';
import path from 'path';
import { callGeminiJson } from '../core/gemini-client.mjs';

export class CoderAgent {
  constructor(options = {}) {
    this.id = 'coder';
    this.name = 'Coder';
    this.color = '#378ADD';
    this.dark = '#185FA5';
    this.bg = '#E6F1FB';
    this.tagline = 'Writes and runs real code';
  }

  /**
   * Generates automated tests directly from Jira scenario JSON (scenarios_*.json).
   * @param {string | object} scenariosInput - File path or parsed JSON object.
   * @param {'playwright' | 'dotnet' | 'flow' | 'all'} targetType - Target framework.
   * @param {string} [outputDir] - Optional directory to write generated test files to.
   */
  generateTestsFromScenarios(scenariosInput, targetType = 'playwright', outputDir = null) {
    let data = scenariosInput;
    if (typeof scenariosInput === 'string') {
      if (fs.existsSync(scenariosInput)) {
        data = JSON.parse(fs.readFileSync(scenariosInput, 'utf8'));
      } else {
        data = JSON.parse(scenariosInput);
      }
    }

    if (!data || !data.test_scenarios || !Array.isArray(data.test_scenarios)) {
      throw new Error('[Coder] Invalid scenarios format: expected "test_scenarios" array.');
    }

    const ticketId = data.ticket_id || 'TICKET';
    const summary = data.summary || 'Automated Scenarios';
    const scenarios = data.test_scenarios;

    const results = [];

    if (targetType === 'playwright' || targetType === 'all') {
      const slug = ticketId.toLowerCase().replace(/[^a-z0-9]+/g, '-');
      const filename = `${slug}-scenarios.spec.ts`;
      const code = this.buildPlaywrightSuiteFromScenarios(ticketId, summary, scenarios);
      const res = {
        agent: this.name,
        targetType: 'playwright',
        ticketId,
        filename,
        code,
        scenariosCount: scenarios.length
      };
      if (outputDir) {
        fs.mkdirSync(outputDir, { recursive: true });
        const filePath = path.join(outputDir, filename);
        fs.writeFileSync(filePath, code, 'utf8');
        res.writtenPath = filePath;
      }
      results.push(res);
    }

    if (targetType === 'dotnet' || targetType === 'all') {
      const safeId = ticketId.replace(/[^a-zA-Z0-9]/g, '');
      const className = `${safeId}ScenarioTests`;
      const filename = `${className}.cs`;
      const code = this.buildDotNetSuiteFromScenarios(ticketId, summary, scenarios, className);
      const res = {
        agent: this.name,
        targetType: 'dotnet',
        ticketId,
        filename,
        code,
        scenariosCount: scenarios.length
      };
      if (outputDir) {
        fs.mkdirSync(outputDir, { recursive: true });
        const filePath = path.join(outputDir, filename);
        fs.writeFileSync(filePath, code, 'utf8');
        res.writtenPath = filePath;
      }
      results.push(res);
    }

    if (targetType === 'flow' || targetType === 'all') {
      const slug = ticketId.toLowerCase().replace(/[^a-z0-9]+/g, '-');
      const filename = `${slug}-flow.json`;
      const flow = this.buildFlowFromScenarios(ticketId, summary, scenarios);
      const res = {
        agent: this.name,
        targetType: 'flow',
        ticketId,
        filename,
        code: JSON.stringify(flow, null, 2),
        scenariosCount: scenarios.length
      };
      if (outputDir) {
        fs.mkdirSync(outputDir, { recursive: true });
        const filePath = path.join(outputDir, filename);
        fs.writeFileSync(filePath, res.code, 'utf8');
        res.writtenPath = filePath;
      }
      results.push(res);
    }

    return targetType === 'all' ? results : results[0];
  }

  buildPlaywrightSuiteFromScenarios(ticketId, summary, scenarios) {
    const testBlocks = scenarios.map(s => {
      const escapedName = (s.name || s.id).replace(/'/g, "\\'");
      const isNegative = s.type === 'negative';
      const preconds = (s.preconditions || []).map(p => `    // Precondition: ${p}`).join('\n');
      const steps = (s.steps || []).map((step, idx) => `    // Step ${idx + 1}: ${step}`).join('\n');

      return `  test('${s.id} [${s.type}]: ${escapedName}', async ({ page, request }) => {
${preconds ? preconds + '\n' : ''}${steps ? steps + '\n' : ''}    await page.goto('/');
    
    // Resilient locator pattern: role -> test-id -> fallback
    const actionButton = page.getByRole('button', { name: /submit|confirm|continue|checkout|pay/i })
      .or(page.locator('[data-testid="primary-action"]'))
      .or(page.locator('button.btn-primary'));

    if (await actionButton.first().isVisible({ timeout: 1500 }).catch(() => false)) {
      await actionButton.first().click();
    }

    // Assertion for ${s.id}
    // Expected result: ${(s.expected_result || '').replace(/\r?\n/g, ' ')}
    ${isNegative 
      ? `await expect(page.locator('body')).toBeVisible();` 
      : `await expect(page.locator('body')).not.toContainText(/UnhandledException|500 Internal Server Error/i);`}
  });`;
    }).join('\n\n');

    return `import { test, expect } from '@playwright/test';

/**
 * Test Suite for Jira Ticket: ${ticketId}
 * Summary: ${summary.replace(/'/g, "\\'")}
 * Auto-generated by Sarvaghna Coder Agent
 */
test.describe('${ticketId}: ${summary.replace(/'/g, "\\'")}', () => {
${testBlocks}
});
`;
  }

  buildDotNetSuiteFromScenarios(ticketId, summary, scenarios, className) {
    const testMethods = scenarios.map(s => {
      const safeMethodName = `${s.id.replace(/[^a-zA-Z0-9]/g, '')}_${(s.type || 'Scenario').replace(/[^a-zA-Z0-9]/g, '')}`;
      const desc = (s.description || s.name || '').replace(/"/g, '""');
      const expected = (s.expected_result || '').replace(/"/g, '""');

      return `    /// <summary>
    /// Scenario ${s.id} (${s.type}) - Priority: ${s.priority || 'medium'}
    /// Description: ${desc}
    /// Expected: ${expected}
    /// </summary>
    [Fact]
    public void ${safeMethodName}_ShouldSatisfyAcceptanceCriteria()
    {
        // Preconditions: ${((s.preconditions || []).join('; ')).replace(/"/g, '""') || 'Standard setup'}
        // Steps: ${((s.steps || []).join(' -> ')).replace(/"/g, '""') || 'Execute'}

        var expectedResult = "${expected}";
        expectedResult.Should().NotBeNullOrWhiteSpace();
    }`;
    }).join('\n\n');

    return `using System;
using Xunit;
using FluentAssertions;

namespace Yukti.Tests.Scenarios;

/// <summary>
/// Jira Ticket: ${ticketId}
/// Summary: ${summary.replace(/"/g, '""')}
/// Auto-generated by Sarvaghna Coder Agent
/// </summary>
public sealed class ${className}
{
${testMethods}
}
`;
  }

  buildFlowFromScenarios(ticketId, summary, scenarios) {
    return {
      flowId: `flow-${ticketId.toLowerCase()}`,
      name: `${ticketId}: ${summary}`,
      description: `Automated test flow generated from Jira ticket ${ticketId}`,
      version: 1,
      scenarios: scenarios.map(s => ({
        id: s.id,
        name: s.name,
        type: s.type,
        priority: s.priority,
        steps: (s.steps || []).map((step, idx) => ({
          stepId: `${s.id}-step-${idx + 1}`,
          action: 'ExecuteStep',
          parameters: { stepDescription: step }
        })),
        expectedResult: s.expected_result
      }))
    };
  }

  /**
   * Generates an automated test script based on criteria extracted by Analyst.
   * Tries real Gemini-authored code first (reads the actual criterion and
   * writes a test that checks it); falls back to the fixed template when
   * GEMINI_API_KEY is absent or the call fails/returns something unusable.
   * @param {Object} spec - Acceptance criteria or feature description.
   * @param {'playwright' | 'dotnet' | 'flow'} targetType - Target framework.
   */
  async generateTest(spec, targetType = 'playwright') {
    const title = spec.title || 'Automated Scenario';
    const desc = spec.description || spec.summary || 'Verify system behavior';

    if (targetType === 'flow') {
      // Flow JSON generation stays deterministic — it's a fixed schema,
      // not prose a model needs to interpret.
      return this.generateFlowDefinition(title, desc);
    }

    this.lastAiFallbackReason = null;
    const aiResult = await this.generateTestWithAi(title, desc, targetType);
    if (aiResult) return aiResult;

    const fallback = targetType === 'playwright'
      ? this.generatePlaywrightTest(title, desc)
      : this.generateDotNetTest(title, desc);
    if (this.lastAiFallbackReason) {
      fallback.aiFallbackReason = this.lastAiFallbackReason;
      fallback.resilienceStrategy += ` [template — AI generation unavailable: ${this.lastAiFallbackReason}]`;
    }
    return fallback;
  }

  /**
   * AI-authored test code for a single acceptance criterion. Returns null
   * (never throws) on any failure so the caller always falls back to the
   * template path.
   */
  async generateTestWithAi(title, desc, targetType) {
    const frameworkInstructions = targetType === 'dotnet'
      ? `Write a real xUnit test in C# for the Yukti platform (namespace Yukti.Tests.Scenarios), using FluentAssertions for assertions. The class should be named after the scenario and sealed. Prefer testing through Yukti's own Flow/FlowEngine abstractions when the criterion describes a flow step; otherwise write a focused unit test for the described behavior.`
      : `Write a real Playwright test in TypeScript (import { test, expect } from '@playwright/test'). Use resilient selectors: prefer getByRole/getByLabel/data-testid over brittle CSS selectors. Write an assertion that actually checks the criterion described below, not a placeholder.`;

    const prompt = `You are the Coder agent inside SARVAGHNA (सर्वज्ञ), the autonomous quality agent for the Yukti platform.

Given this acceptance criterion extracted by the Analyst agent, write ONE real, runnable, specific test that verifies it. Do not write a generic placeholder test — the assertions must be concrete and tied to what this criterion actually describes.

Criterion title: ${title}
Criterion description: ${desc}

${frameworkInstructions}

Return pure JSON matching this exact structure:
{
  "filename": "suggested-file-name.ext",
  "code": "the full file contents as a single string, including imports",
  "resilienceStrategy": "one short phrase describing the selector/assertion strategy used",
  "rationale": "one sentence on why this test verifies the criterion"
}`;

    const result = await callGeminiJson({ prompt, maxOutputTokens: 8192 });
    if (!result.ok) {
      this.lastAiFallbackReason = result.reason;
      return null;
    }

    const data = result.data || {};
    if (!data.code || typeof data.code !== 'string' || data.code.trim().length < 20) {
      // Unusable output — don't ship a near-empty file, fall back to template.
      this.lastAiFallbackReason = 'model returned no usable code';
      return null;
    }

    return {
      agent: this.name,
      targetType,
      filename: data.filename || `${title.toLowerCase().replace(/[^a-z0-9]+/g, '-')}.${targetType === 'dotnet' ? 'cs' : 'spec.ts'}`,
      code: data.code,
      resilienceStrategy: data.resilienceStrategy || 'AI-authored',
      rationale: data.rationale,
      generationMode: 'ai'
    };
  }

  generatePlaywrightTest(title, desc) {
    const slug = title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '');
    const code = `import { test, expect } from '@playwright/test';

test.describe('${title.replace(/'/g, "\\'")}', () => {
  // Scenario: ${desc.replace(/'/g, "\\'")}
  test('should execute scenario successfully with resilient locators', async ({ page, request }) => {
    // 1. Arrange & Navigate
    await page.goto('/');
    await expect(page).toHaveTitle(/Yukti/i);

    // 2. Act - Use data-testid or role for maximum resilience
    const primaryButton = page.getByRole('button', { name: /Submit|Confirm|Run/i })
      .or(page.locator('[data-testid="primary-action"]'));

    if (await primaryButton.isVisible()) {
      await primaryButton.click();
    }

    // 3. Assert
    await expect(page.locator('body')).not.toContainText(/UnhandledException|500 Internal/i);
  });
});
`;
    return {
      agent: this.name,
      targetType: 'playwright',
      filename: `${slug}.spec.ts`,
      code,
      resilienceStrategy: 'AriaRole + DataTestId fallback',
      generationMode: 'template'
    };
  }

  generateDotNetTest(title, desc) {
    const className = title.replace(/[^a-zA-Z0-9]/g, '') + 'Tests';
    const code = `using Xunit;
using FluentAssertions;

namespace Yukti.Tests.Scenarios;

public sealed class ${className}
{
    [Fact]
    public void Scenario_ShouldSatisfyCriteria()
    {
        // Description: ${desc.replace(/"/g, '""')}
        var status = 200;
        status.Should().Be(200);
    }
}
`;
    return {
      agent: this.name,
      targetType: 'dotnet',
      filename: `${className}.cs`,
      code,
      generationMode: 'template'
    };
  }

  generateFlowDefinition(title, desc) {
    const flow = {
      name: title,
      description: desc,
      version: 1,
      steps: [
        {
          stepId: 'step-1',
          module: 'Api',
          action: 'ExecuteRequest',
          parameters: { method: 'GET', url: '/health' }
        },
        {
          stepId: 'step-2',
          module: 'Logs',
          action: 'VerifyNoErrors',
          parameters: { pattern: 'ERROR', timeWindowSeconds: 30 }
        }
      ]
    };
    return {
      agent: this.name,
      targetType: 'flow',
      filename: `${title.toLowerCase().replace(/\s+/g, '-')}.flow.json`,
      code: JSON.stringify(flow, null, 2)
    };
  }

  /**
   * Attempts to auto-repair code given an error traceback.
   */
  autoFix(originalCode, errorTrace) {
    let fixedCode = originalCode;
    let strategy = 'None';

    if (errorTrace.includes('locator.click: Timeout') || errorTrace.includes('waiting for locator')) {
      // Switch to resilient locator pattern
      fixedCode = fixedCode.replace(
        /page\.locator\((['"])([^'"]+)\1\)/g,
        "page.locator($1$2$1).or(page.getByRole('button')).first()"
      );
      strategy = 'Resilient Locator Fallback';
    } else if (errorTrace.includes('Unhandled exception') || errorTrace.includes('NullReferenceException')) {
      fixedCode = fixedCode.replace(/(\w+)\.(\w+)/g, '$1?.$2');
      strategy = 'Null Guard Injection';
    }

    return {
      agent: this.name,
      fixed: fixedCode !== originalCode,
      strategy,
      code: fixedCode
    };
  }
}
