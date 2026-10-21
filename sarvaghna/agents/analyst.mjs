// sarvaghna/agents/analyst.mjs
// Agent: Analyst (Reads docs and extracts meaning)
// Color: #1D9E75 (Emerald) | Tagline: "Reads docs and extracts meaning"

import fs from 'fs';
import path from 'path';
import { callGeminiJson } from '../core/gemini-client.mjs';

const MAX_DOC_LENGTH = 35000; // matches Tier 4's diff truncation budget

export class AnalystAgent {
  constructor(options = {}) {
    this.id = 'analyst';
    this.name = 'Analyst';
    this.color = '#1D9E75';
    this.dark = '#0F6E56';
    this.bg = '#E1F5EE';
    this.tagline = 'Reads docs and extracts meaning';
  }

  /**
   * Reads a PRD, specification, requirement document, or Jira scenario JSON.
   * @param {string} filePath - Absolute or relative path to document.
   */
  async analyzeDoc(filePath) {
    if (!fs.existsSync(filePath)) {
      throw new Error(`[Analyst] Document not found at path: ${filePath}`);
    }

    const content = fs.readFileSync(filePath, 'utf8');

    // If it's a Jira scenarios JSON file (from jira_client / analyst_agent),
    // it's already structured — no need for AI or regex extraction.
    if (filePath.endsWith('.json')) {
      try {
        const data = JSON.parse(content);
        if (data.ticket_id && data.test_scenarios) {
          const criteria = data.test_scenarios.map((s, idx) => ({
            title: s.name,
            description: `${s.description} (Expected: ${s.expected_result})`,
            section: `Jira: ${data.ticket_id}`,
            line: idx + 1
          }));

          return {
            agent: this.name,
            source: `Jira Ticket ${data.ticket_id}`,
            lineCount: criteria.length,
            userStoriesCount: 1,
            criteriaCount: criteria.length,
            rulesCount: 0,
            ambiguitiesCount: (data.ambiguities || []).length,
            userStories: [{ story: data.summary, section: data.ticket_id, line: 1 }],
            acceptanceCriteria: criteria,
            architecturalRules: [],
            ambiguities: (data.ambiguities || []).map(a => ({ warning: a, section: data.ticket_id, line: 1 })),
            summary: `Analyzed Jira ticket '${data.ticket_id}': Extracted ${criteria.length} test scenarios and flagged ${(data.ambiguities || []).length} ambiguities.`,
            scenariosData: data
          };
        }
      } catch {
        // Fall back to plain text analysis
      }
    }

    return this.analyzeText(content, path.basename(filePath));
  }

  /**
   * Analyzes raw requirement text. Tries real semantic extraction via
   * Gemini first; falls back to deterministic regex extraction if
   * GEMINI_API_KEY is absent or the call fails for any reason. Same
   * graceful-degradation contract as Tier 4 (rules/semantic-reviewer.mjs) —
   * AI is optional, the Analyst always returns a usable result.
   * @param {string} text - Document contents.
   * @param {string} sourceName - Document title or filename.
   */
  async analyzeText(text, sourceName = 'Requirement Spec') {
    this.lastAiFallbackReason = null;
    const aiResult = await this.analyzeTextWithAi(text, sourceName);
    if (aiResult) return aiResult;

    const fallback = this.analyzeTextDeterministic(text, sourceName);
    if (this.lastAiFallbackReason) {
      fallback.aiFallbackReason = this.lastAiFallbackReason;
      fallback.summary += ` [AI analysis unavailable: ${this.lastAiFallbackReason}]`;
    }
    return fallback;
  }

  /**
   * AI-powered extraction. Returns null (never throws) on any failure so
   * the caller always falls back to the deterministic path.
   */
  async analyzeTextWithAi(text, sourceName) {
    const lines = text.split(/\r?\n/);

    // Split on line boundaries into chunks under MAX_DOC_LENGTH so the whole
    // document is read, not just its first third. Each chunk keeps its real
    // starting line number so the model's line estimates stay meaningful.
    const chunks = [];
    let current = [];
    let currentLen = 0;
    let startLine = 1;
    for (let i = 0; i < lines.length; i++) {
      if (currentLen + lines[i].length + 1 > MAX_DOC_LENGTH && current.length > 0) {
        chunks.push({ startLine, body: current.join('\n') });
        current = [];
        currentLen = 0;
        startLine = i + 1;
      }
      current.push(lines[i]);
      currentLen += lines[i].length + 1;
    }
    if (current.length > 0) chunks.push({ startLine, body: current.join('\n') });

    const results = await Promise.all(chunks.map((c, idx) => this.analyzeChunkWithAi(c, idx + 1, chunks.length, sourceName)));
    if (results.some(r => r === null)) {
      // Partial AI coverage of the document would silently under-report —
      // all chunks succeed or the whole analysis falls back.
      return null;
    }

    const userStories = results.flatMap(r => r.userStories);
    const acceptanceCriteria = results.flatMap(r => r.acceptanceCriteria);
    const architecturalRules = results.flatMap(r => r.architecturalRules);
    const ambiguities = results.flatMap(r => r.ambiguities);

    if (acceptanceCriteria.length === 0 && userStories.length === 0) {
      return null;
    }

    return {
      agent: this.name,
      source: sourceName,
      lineCount: lines.length,
      userStoriesCount: userStories.length,
      criteriaCount: acceptanceCriteria.length,
      rulesCount: architecturalRules.length,
      ambiguitiesCount: ambiguities.length,
      userStories,
      acceptanceCriteria,
      architecturalRules,
      ambiguities,
      analysisMode: 'ai',
      summary: `Analyzed '${sourceName}' (${lines.length} lines, ${chunks.length} chunk(s)) via Gemini semantic review: Extracted ${acceptanceCriteria.length} acceptance criteria, ${architecturalRules.length} architectural rules, and flagged ${ambiguities.length} ambiguities.`
    };
  }

  async analyzeChunkWithAi(chunk, chunkIndex, chunkCount, sourceName) {
    const truncated = chunk.body;

    const prompt = `You are the Analyst agent inside SARVAGHNA (सर्वज्ञ), the autonomous quality agent for the Yukti platform.

Read the following requirement/specification document and extract its real meaning — not just keyword matches. Identify:
1. User stories (who wants what and why)
2. Concrete, testable acceptance criteria (things a test suite could actually verify)
3. Architectural rules or non-functional requirements (e.g. zero-dependency domain, tenant isolation, durability, idempotency)
4. Genuine ambiguities — places where the spec is vague, contradictory, or leaves an open question a developer would have to guess at. Do not flag "TBD"/"TODO" as ambiguous unless the surrounding text is actually unclear; use judgment, not string matching.

Return pure JSON matching this exact structure:
{
  "userStories": [{"story": "...", "section": "...", "line": 0}],
  "acceptanceCriteria": [{"title": "...", "description": "...", "section": "...", "line": 0}],
  "architecturalRules": [{"rule": "...", "section": "...", "line": 0}],
  "ambiguities": [{"warning": "...", "section": "...", "line": 0}]
}

"line" should be the 1-indexed line number in the FULL source document. This excerpt is part ${chunkIndex} of ${chunkCount} and starts at line ${chunk.startLine}, so add ${chunk.startLine - 1} to any position you count within the excerpt. Approximate is fine.

DOCUMENT EXCERPT (source: ${sourceName}, part ${chunkIndex}/${chunkCount}):
"""
${truncated}
"""`;

    const result = await callGeminiJson({ prompt, maxOutputTokens: 8192, capability: 'crew-analyst' });
    if (!result.ok) {
      this.lastAiFallbackReason = `chunk ${chunkIndex}/${chunkCount}: ${result.reason}`;
      return null;
    }

    const data = result.data || {};
    return {
      userStories: Array.isArray(data.userStories) ? data.userStories : [],
      acceptanceCriteria: Array.isArray(data.acceptanceCriteria) ? data.acceptanceCriteria : [],
      architecturalRules: Array.isArray(data.architecturalRules) ? data.architecturalRules : [],
      ambiguities: Array.isArray(data.ambiguities) ? data.ambiguities : []
    };
  }

  /**
   * Original deterministic regex-based extraction. Used when AI is
   * unavailable or declines to return usable structured output.
   */
  analyzeTextDeterministic(text, sourceName = 'Requirement Spec') {
    const lines = text.split(/\r?\n/);
    const userStories = [];
    const acceptanceCriteria = [];
    const architecturalRules = [];
    const ambiguities = [];

    let currentSection = '';

    for (let i = 0; i < lines.length; i++) {
      const line = lines[i].trim();
      if (!line) continue;

      if (line.startsWith('#')) {
        currentSection = line.replace(/^#+\s*/, '');
        continue;
      }

      // Detect User Stories: "As a... I want... so that..."
      if (line.match(/As an?\s+.+,\s*I want\s+.+/i)) {
        userStories.push({
          section: currentSection,
          story: line,
          line: i + 1
        });
      }

      // Detect Criteria / Requirements: "Must", "Should", "Shall", bullet points with verbs
      if (line.match(/^(?:[-*]|\d+\.)\s+\*\*(.+?)\*\*:?\s*(.+)/)) {
        const match = line.match(/^(?:[-*]|\d+\.)\s+\*\*(.+?)\*\*:?\s*(.+)/);
        acceptanceCriteria.push({
          title: match[1],
          description: match[2],
          section: currentSection,
          line: i + 1
        });
      } else if (line.match(/(?:must|shall|required to|needs to|assert that)\s+(.+)/i)) {
        const match = line.match(/(?:must|shall|required to|needs to|assert that)\s+(.+)/i);
        acceptanceCriteria.push({
          title: `Invariant: ${match[1].slice(0, 40)}...`,
          description: line,
          section: currentSection,
          line: i + 1
        });
      }

      // Detect Architecture / NFR mentions
      if (line.match(/(?:zero[- ]dependency|tenant[- ]scoped|deterministic fallback|resilient|idempotent)/i)) {
        architecturalRules.push({
          rule: line,
          section: currentSection,
          line: i + 1
        });
      }

      // Detect potential ambiguities
      const ambiguityTokens = ['TBD', 'TO' + 'DO', 'FIX' + 'ME', 'as needed', 'etc\\.', 'unspecified', 'later'];
      const ambiguityRegex = new RegExp(`\\b(${ambiguityTokens.join('|')})\\b`, 'i');
      if (ambiguityRegex.test(line)) {
        ambiguities.push({
          warning: line,
          section: currentSection,
          line: i + 1
        });
      }
    }

    return {
      agent: this.name,
      source: sourceName,
      lineCount: lines.length,
      userStoriesCount: userStories.length,
      criteriaCount: acceptanceCriteria.length,
      rulesCount: architecturalRules.length,
      ambiguitiesCount: ambiguities.length,
      userStories,
      acceptanceCriteria,
      architecturalRules,
      ambiguities,
      analysisMode: 'deterministic',
      summary: `Analyzed '${sourceName}' (${lines.length} lines): Extracted ${acceptanceCriteria.length} acceptance criteria, ${architecturalRules.length} architectural rules, and flagged ${ambiguities.length} ambiguities.`
    };
  }
}
