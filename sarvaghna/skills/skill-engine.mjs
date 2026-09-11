// tools/sarvaghna/skills/skill-engine.mjs
// Dynamic Skills Engine: Discovers and executes modular skills from tools/sarvaghna/skills/

import fs from 'fs';
import path from 'path';
import { pathToFileURL } from 'url';

export async function loadAndExecuteSkills(diffFiles, skillsDir, context = {}) {
  const findings = [];
  const skillReports = [];
  if (!fs.existsSync(skillsDir)) return { findings, skillReports };

  const entries = fs.readdirSync(skillsDir, { withFileTypes: true });

  for (const entry of entries) {
    if (!entry.isDirectory()) continue;

    const skillIndexPath = path.join(skillsDir, entry.name, 'index.mjs');
    if (!fs.existsSync(skillIndexPath)) continue;

    try {
      const fileUrl = pathToFileURL(skillIndexPath).href;
      const mod = await import(fileUrl);
      const skill = mod.default || mod.skill;

      if (skill && typeof skill.execute === 'function') {
        const result = await skill.execute(diffFiles, context);
        if (result?.findings) {
          findings.push(...result.findings);
        }
        skillReports.push({
          name: skill.name || entry.name,
          description: skill.description || '',
          status: result?.status || 'COMPLETED',
          summary: result?.summary || ''
        });
      }
    } catch (err) {
      console.warn(`[SARVAGHNA] Failed to execute skill ${entry.name}: ${err.message}`);
    }
  }

  return { findings, skillReports };
}
