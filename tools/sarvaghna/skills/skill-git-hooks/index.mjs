// tools/sarvaghna/skills/skill-git-hooks/index.mjs
// Skill: Git Hook Inculcator — Installs native Git hooks to enforce SARVAGHNA locally

import fs from 'fs';
import path from 'path';

export const skill = {
  name: 'skill-git-hooks',
  description: 'Inculcates SARVAGHNA into native Git hooks (pre-commit and pre-push)',

  installHooks(repoRoot) {
    const hooksDir = path.join(repoRoot, '.git', 'hooks');
    if (!fs.existsSync(hooksDir)) {
      fs.mkdirSync(hooksDir, { recursive: true });
    }

    // 1. pre-commit hook (runs on staged files before commit)
    const preCommitScript = `#!/bin/sh
# SARVAGHNA Git pre-commit hook
echo "👁️  SARVAGHNA: Scanning staged changes before commit..."
node tools/sarvaghna/sarvaghna.mjs --staged
RESULT=$?
if [ $RESULT -ne 0 ]; then
  echo ""
  echo "❌ SARVAGHNA rejected commit: Quality, security, or architectural violations detected."
  echo "   Fix the issues above or bypass with 'git commit --no-verify' (not recommended)."
  exit 1
fi
exit 0
`;

    // 2. pre-push hook (runs before pushing to remote)
    const prePushScript = `#!/bin/sh
# SARVAGHNA Git pre-push hook
echo "👁️  SARVAGHNA: Validating commits before push..."
node tools/sarvaghna/sarvaghna.mjs --base origin/main --head HEAD
RESULT=$?
if [ $RESULT -ne 0 ]; then
  echo ""
  echo "❌ SARVAGHNA rejected push: Rule violations detected on branch."
  echo "   Fix the issues above before pushing to remote."
  exit 1
fi
exit 0
`;

    const preCommitPath = path.join(hooksDir, 'pre-commit');
    const prePushPath = path.join(hooksDir, 'pre-push');

    fs.writeFileSync(preCommitPath, preCommitScript, { encoding: 'utf8', mode: 0o755 });
    fs.writeFileSync(prePushPath, prePushScript, { encoding: 'utf8', mode: 0o755 });

    console.log('✅ SARVAGHNA successfully inculcated into Git!');
    console.log(`   Installed: ${preCommitPath}`);
    console.log(`   Installed: ${prePushPath}`);
    console.log('\nGit will now automatically invoke SARVAGHNA on every `git commit` and `git push`.\n');
  },

  uninstallHooks(repoRoot) {
    const hooksDir = path.join(repoRoot, '.git', 'hooks');
    const preCommitPath = path.join(hooksDir, 'pre-commit');
    const prePushPath = path.join(hooksDir, 'pre-push');

    let removed = false;
    if (fs.existsSync(preCommitPath)) {
      fs.unlinkSync(preCommitPath);
      console.log(`Removed: ${preCommitPath}`);
      removed = true;
    }
    if (fs.existsSync(prePushPath)) {
      fs.unlinkSync(prePushPath);
      console.log(`Removed: ${prePushPath}`);
      removed = true;
    }

    if (removed) {
      console.log('✅ SARVAGHNA Git hooks uninstalled.');
    } else {
      console.log('ℹ️ No SARVAGHNA Git hooks were found.');
    }
  }
};
