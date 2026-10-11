// tools/sarvaghna/rules/semantic-reviewer.mjs
// Tier 4: Gemini-powered semantic code review agent wired to @pranavbharadwaj89

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export async function runSemanticReview(diffFiles, fullDiffText, config) {
  const apiKey = process.env.GEMINI_API_KEY;
  if (!apiKey) {
    return {
      enabled: false,
      reason: 'GEMINI_API_KEY not configured. Deterministic rules (Tiers 1-3) ran at full fidelity.'
    };
  }

  const model = config.rules.aiReview?.model || 'gemini-3.8-flash';
  const endpoint = `https://generativelanguage.googleapis.com/v1beta/models/${model}:generateContent?key=${apiKey}`;

  // Read active goals from GOALS.md dynamically
  let activeGoalsText = '';
  const goalsPath = path.resolve(__dirname, '..', 'GOALS.md');
  if (fs.existsSync(goalsPath)) {
    try {
      activeGoalsText = fs.readFileSync(goalsPath, 'utf8');
    } catch (e) {
      // Gracefully continue
    }
  }

  // Truncate diff text if overly large to stay within context and cost limits
  const maxDiffLength = 35000;
  let truncatedDiff = fullDiffText;
  if (fullDiffText.length > maxDiffLength) {
    truncatedDiff = fullDiffText.substring(0, maxDiffLength) + '\n\n... [Diff truncated by SARVAGHNA for context window] ...';
  }

  const prompt = `You are SARVAGHNA (सर्वज्ञ), the autonomous GitHub Code Intelligence & Quality Agent dedicated to @pranavbharadwaj89 for the Yukti Unified Automation Platform.

You are NOT a dumb linter; you are a skilled, sensible, context-aware engineering agent.
Your primary directives and active goals set by @pranavbharadwaj89:
================================================================
${activeGoalsText || 'Guard Yukti architecture, zero secrets, high developer velocity, constructive feedback.'}
================================================================

SENSIBILITY & REVIEW GUIDELINES:
1. High Signal, Zero Noise:
   - Do NOT flag stylistic pedantry or formatting trivia.
   - Recognize intentional design decisions, mocks, test fixtures, and documentation placeholders (e.g. '...', '<placeholder>').
2. Architectural Invariants (Yukti Volume Alignment):
   - Yukti.Domain must be pure DDD with zero external dependencies.
   - Flow execution must maintain crash durability (atomic per-step unit of work commit).
   - Async methods must accept CancellationTokens.
   - Structured logging only (no string interpolation in logger calls).
3. Concurrency & Logic Defect Detection:
   - Identify race conditions, deadlock vectors (.Result / .Wait()), unhandled exceptions, and edge-case mishandling.
4. Constructive Precision:
   - Provide concrete replacement snippets ('suggestion') for any issue flagged.

Please return your evaluation in pure JSON matching this exact structure:
{
  "verdict": "APPROVED" | "COMMENT" | "REQUEST_CHANGES",
  "summary": "Concise 2-3 sentence executive summary of the changes and overall quality verdict signed by SARVAGHNA.",
  "strengths": ["list of positive aspects of the code changes"],
  "lineReviews": [
    {
      "file": "path/to/file.ext",
      "line": 42,
      "severity": "INFO" | "WARNING" | "CRITICAL",
      "comment": "Specific, actionable feedback on this line/block",
      "suggestion": "Optional concrete replacement code snippet"
    }
  ]
}

GIT DIFF TO ANALYZE:
\`\`\`diff
${truncatedDiff}
\`\`\`
`;

  try {
    const response = await fetch(endpoint, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        contents: [
          {
            role: 'user',
            parts: [{ text: prompt }]
          }
        ],
        generationConfig: {
          temperature: config.rules.aiReview?.temperature || 0.2,
          maxOutputTokens: config.rules.aiReview?.maxOutputTokens || 4096,
          responseMimeType: 'application/json'
        }
      })
    });

    if (!response.ok) {
      const errText = await response.text();
      return {
        enabled: false,
        reason: `Gemini API returned error ${response.status}: ${errText.substring(0, 200)}`
      };
    }

    const data = await response.json();
    const candidateText = data.candidates?.[0]?.content?.parts?.[0]?.text;
    if (!candidateText) {
      return {
        enabled: false,
        reason: 'Empty response received from AI model.'
      };
    }

    const parsed = JSON.parse(candidateText);
    return {
      enabled: true,
      verdict: parsed.verdict || 'COMMENT',
      summary: parsed.summary || 'Code analysis completed.',
      strengths: parsed.strengths || [],
      lineReviews: parsed.lineReviews || []
    };
  } catch (err) {
    return {
      enabled: false,
      reason: `Semantic review failed: ${err.message}`
    };
  }
}
