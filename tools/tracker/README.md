# Build tracker

`docs/TRACKER.html` is generated, not hand-edited. It reflects real repo state —
file existence and content checks — not a manually maintained checklist.

- **Regenerate:** `node tools/tracker/generate-tracker.mjs` from the repo root.
- **What each item checks:** `tools/tracker/checks.json` — add/edit items there,
  not in the HTML.
- **Auto-regenerate on commit:** a local `pre-commit` git hook (`.git/hooks/pre-commit`)
  runs the generator and stages `docs/TRACKER.html` automatically. Hooks aren't
  tracked by git, so anyone who clones this repo needs to copy
  `tools/tracker/pre-commit.sample` to `.git/hooks/pre-commit` (and `chmod +x` it
  on macOS/Linux) to get the same behavior locally.
