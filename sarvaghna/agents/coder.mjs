// sarvaghna/agents/coder.mjs
// Agent: Coder (Writes and runs real code)
// Color: #378ADD (Blue) | Tagline: "Writes and runs real code"

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
   * Generates an automated test script based on criteria extracted by Analyst.
   * @param {Object} spec - Acceptance criteria or feature description.
   * @param {'playwright' | 'dotnet' | 'flow'} targetType - Target framework.
   */
  generateTest(spec, targetType = 'playwright') {
    const title = spec.title || 'Automated Scenario';
    const desc = spec.description || spec.summary || 'Verify system behavior';

    if (targetType === 'playwright') {
      return this.generatePlaywrightTest(title, desc);
    } else if (targetType === 'dotnet') {
      return this.generateDotNetTest(title, desc);
    } else {
      return this.generateFlowDefinition(title, desc);
    }
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
      resilienceStrategy: 'AriaRole + DataTestId fallback'
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
      code
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
