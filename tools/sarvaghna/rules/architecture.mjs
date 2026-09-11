// tools/sarvaghna/rules/architecture.mjs
// Tier 2 & 3: Architectural invariant and Volume 5 playbook enforcement

export function checkArchitecture(diffFiles, config) {
  const findings = [];

  for (const file of diffFiles) {
    const isCSharp = file.path.endsWith('.cs');
    const isCsproj = file.path.endsWith('.csproj');
    const isAppSettings = file.path.endsWith('appsettings.json') || file.path.endsWith('appsettings.Development.json');

    // 1. Yukti.Domain Zero Dependencies Check (Volume 1 §15.5)
    if (file.path.includes('Yukti.Domain')) {
      if (isCsproj) {
        for (const chunk of file.chunks) {
          for (const line of chunk.addedLines) {
            if (/<PackageReference\s+Include=/.test(line.content)) {
              findings.push({
                file: file.path,
                line: line.newLineNumber,
                rule: 'ARCH:DOMAIN_ZERO_DEPENDENCIES',
                title: 'Yukti.Domain Dependency Violation',
                message: 'Volume 1 §15.5 constraint violated: Yukti.Domain must have ZERO external package dependencies. All third-party libraries belong in Infrastructure or Application layers.',
                severity: 'CRITICAL',
                snippet: line.content.trim()
              });
            }
          }
        }
      }

      if (isCSharp) {
        for (const chunk of file.chunks) {
          for (const line of chunk.addedLines) {
            if (/^\s*using\s+(Microsoft\.EntityFrameworkCore|Newtonsoft\.Json|Npgsql|StackExchange\.Redis|System\.Net\.Http)/.test(line.content)) {
              findings.push({
                file: file.path,
                line: line.newLineNumber,
                rule: 'ARCH:DOMAIN_POLLUTION',
                title: 'Yukti.Domain External Namespace Reference',
                message: 'Yukti.Domain aggregates and entities cannot reference ORM, HTTP, or infrastructure namespaces directly (Volume 1 §15.5).',
                severity: 'CRITICAL',
                snippet: line.content.trim()
              });
            }
          }
        }
      }
    }

    // 2. Structured Logging Rule (Volume 5 §8.2)
    if (isCSharp) {
      for (const chunk of file.chunks) {
        for (const line of chunk.addedLines) {
          if (/\b_?logger\s*\.\s*Log(Information|Warning|Error|Critical|Debug|Trace)\s*\(\s*\$"/i.test(line.content)) {
            findings.push({
              file: file.path,
              line: line.newLineNumber,
              rule: 'PLAYBOOK:STRUCTURED_LOGGING_REQUIRED',
              title: 'String Interpolation in Logger Call',
              message: 'Volume 5 §8.2 & Volume 1 §28.2 prohibit string interpolation in logging calls. Use structured message templates with named placeholders: _logger.LogInformation("Processing order {OrderId}", orderId);',
              severity: 'HIGH',
              snippet: line.content.trim()
            });
          }
        }
      }
    }

    // 3. No Sync-Over-Async Rule (Volume 5 §5.2)
    if (isCSharp) {
      for (const chunk of file.chunks) {
        for (const line of chunk.addedLines) {
          if (/\.(GetAwaiter\(\)\.GetResult\(\)|\.Result\b|\.Wait\(\))/i.test(line.content)) {
            // Check if not a test file
            if (!file.path.includes('tests') && !file.path.includes('Tests')) {
              findings.push({
                file: file.path,
                line: line.newLineNumber,
                rule: 'PLAYBOOK:NO_SYNC_OVER_ASYNC',
                title: 'Sync-Over-Async Detected',
                message: 'Volume 5 §5.2 & Roslyn Analyzer YUKTI002 violation: Calling .Result, .Wait(), or .GetAwaiter().GetResult() can cause thread-pool starvation and deadlocks. Always await Tasks asynchronously.',
                severity: 'HIGH',
                snippet: line.content.trim()
              });
            }
          }
        }
      }
    }

    // 4. appsettings.json Secrets Hygiene
    if (isAppSettings) {
      for (const chunk of file.chunks) {
        for (const line of chunk.addedLines) {
          if (/"ConnectionStrings"\s*:/i.test(line.content) || /"Host=.*Password="/i.test(line.content)) {
            findings.push({
              file: file.path,
              line: line.newLineNumber,
              rule: 'ARCH:NO_SECRETS_IN_APPSETTINGS',
              title: 'ConnectionStrings Committed in Config',
              message: 'Per docs/PROGRESS.md & Volume 4 conventions: ConnectionStrings must NEVER be committed to appsettings.json. Use dotnet user-secrets or environment variables.',
              severity: 'HIGH',
              snippet: line.content.trim()
            });
          }
        }
      }
    }
  }

  return findings;
}
