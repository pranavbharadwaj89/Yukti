// sarvaghna/agents/analyst.mjs
// Agent: Analyst (Reads docs and extracts meaning)
// Color: #1D9E75 (Emerald) | Tagline: "Reads docs and extracts meaning"

import fs from 'fs';
import path from 'path';

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
   * Reads a PRD, specification, or requirement document and extracts testable criteria.
   * @param {string} filePath - Absolute or relative path to document.
   */
  analyzeDoc(filePath) {
    if (!fs.existsSync(filePath)) {
      throw new Error(`[Analyst] Document not found at path: ${filePath}`);
    }

    const content = fs.readFileSync(filePath, 'utf8');
    return this.analyzeText(content, path.basename(filePath));
  }

  /**
   * Analyzes raw requirement text.
   * @param {string} text - Document contents.
   * @param {string} sourceName - Document title or filename.
   */
  analyzeText(text, sourceName = 'Requirement Spec') {
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
      summary: `Analyzed '${sourceName}' (${lines.length} lines): Extracted ${acceptanceCriteria.length} acceptance criteria, ${architecturalRules.length} architectural rules, and flagged ${ambiguities.length} ambiguities.`
    };
  }
}
