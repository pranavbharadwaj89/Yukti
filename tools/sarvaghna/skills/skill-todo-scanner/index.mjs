// tools/sarvaghna/skills/skill-todo-scanner/index.mjs
// Skill: Flags unresolved FIXME and CRITICAL-TODO comments in code changes

export const skill = {
  name: 'skill-todo-scanner',
  description: 'Audits diff additions for unresolved FIXME and high-priority debt markers',

  async execute(diffFiles, context) {
    const findings = [];

    for (const file of diffFiles) {
      for (const chunk of file.chunks) {
        for (const line of chunk.addedLines) {
          if (/\b(FIXME|BUG:|HACK:)\b/i.test(line.content)) {
            findings.push({
              file: file.path,
              line: line.newLineNumber,
              rule: 'SKILL:TODO_SCANNER:FIXME_DETECTED',
              title: 'Unresolved FIXME / HACK Marker in Code',
              message: 'Found FIXME or HACK marker in committed changes. Resolve the underlying issue or file a tracking item before merging.',
              severity: 'WARNING',
              snippet: line.content.trim()
            });
          }
        }
      }
    }

    return {
      status: 'COMPLETED',
      summary: `Scanned files for FIXME markers. Found: ${findings.length}`,
      findings
    };
  }
};
