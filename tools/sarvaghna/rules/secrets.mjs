// tools/sarvaghna/rules/secrets.mjs
// Tier 1: High-entropy secret and credential detector for git diffs

export function scanSecrets(diffFiles, config) {
  const findings = [];
  const patterns = config.rules.secrets.patterns || [];

  for (const file of diffFiles) {
    // Skip test files or sample configs from strict secret checks if appropriate
    const isSample = file.path.endsWith('.sample') || file.path.includes('test') || file.path.endsWith('.example');

    for (const chunk of file.chunks) {
      for (const line of chunk.addedLines) {
        const text = line.content;

        // Skip comments or placeholders
        if (/^\s*(\/\/|#|\/\*|\*)\s*(example|sample|placeholder|test|dummy)/i.test(text)) {
          continue;
        }

        for (const pat of patterns) {
          const regex = new RegExp(pat.regex, pat.flags || '');
          if (regex.test(text)) {
            // Check for false positives like dummy tokens or documentation placeholders
            if (/(placeholder|your[-_]api[-_]key|fake|dummy|test[-_]token|xxx+|\.\.\.)/i.test(text)) {
              continue;
            }

            findings.push({
              file: file.path,
              line: line.newLineNumber,
              rule: `SECRET_LEAK:${pat.name.toUpperCase().replace(/\s+/g, '_')}`,
              title: `Potential Secret Exposed: ${pat.name}`,
              message: `Detected potential credential matching pattern "${pat.name}". Do not commit secrets or tokens directly to Git. Use environment variables or dotnet user-secrets per Volume 4 & 5 standards.`,
              severity: isSample ? 'WARNING' : pat.severity,
              snippet: text.trim().substring(0, 100)
            });
          }
        }
      }
    }
  }

  return findings;
}
