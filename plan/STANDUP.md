# Yukti v0.1 — Standup Log

Append-only. New entries go at the bottom.

---

## 2026-10-11 ~22:00 IST — Day 0
Done:        D0-T1 (paused cloud routine trig_01768W9Jm3uZvZhcbHktj8Ky), D0-T2 (SUTRADHAR.md/CLAUDE.md/plan/PLAN.md/plan/STANDUP.md merged via `chore/sutradhar-plan`, commit b5b2188)
Blocked:     D0-T3 — branch protection PUT returned `403 Resource not accessible by personal access token`; the owner's PAT lacks Administration permission. Needs either a PAT with that scope, or the owner adding the rule manually (repo → Settings → Branches → Add rule → `main` → Require PR before merging).
Discovered:  none yet
Scope guards: none yet
Exit check:  on track
Next:        Day 1 baseline (done below) and GAP-MATRIX (D1-T3)

## 2026-10-11 ~22:20 IST — Day 1
Done:        D1-T1 (true baseline recorded — see below), D1-T2 (baseline made green)
In progress: D1-T3 GAP-MATRIX.md (next)
Blocked:     D0-T3 carried over (see above, still needs owner action)
Discovered:  D1-T2 found a real strict-build break: `dotnet build -warnaserror` failed with 5× CA1416
             ("This call site is reachable on all platforms... DesktopUiModule... only supported on
             'windows'") in src/Yukti.Api/Program.cs — the plain `dotnet build` used all week never
             surfaced this because it only warns, not errors. Fixed by guarding every DesktopUiModule
             DI-registration/resolution site with `OperatingSystem.IsWindows()`, matching the module's
             own documented Windows-only nature (Volume 1 Part III) — not a suppression, a genuine
             platform guard. Verified: `dotnet build -warnaserror` -> 0 Warnings, 0 Errors;
             `dotnet test` -> 71/71 passed; `npm run build` (apps/yukti-gui) -> green.
Scope guards: none
Exit check:  on track (no feature code merged yet, per Day 1's own rule — this fix is baseline
             stabilization, not a feature)
Next:        D1-T3 GAP-MATRIX.md — audit each P0 item + frozen feature against the real codebase
