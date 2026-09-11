# SARVAGHNA (सर्वज्ञ) — Autonomous Git Code Review Agent

> **सर्वज्ञ (Sarvaghna)** — *Sanskrit: "All-Knowing", "Omniscient".*
> An autonomous GitHub agent that inspects and analyzes every line of code pushed or merged into the Yukti platform repository.

---

## Capabilities & Architecture

SARVAGHNA runs a 4-tier inspection pipeline across code changes (`git diff`):

```
                       ┌─────────────────────────────────────┐
                       │   Git Push / Pull Request Event     │
                       └──────────────────┬──────────────────┘
                                          │
                        ┌─────────────────▼─────────────────┐
                        │      SARVAGHNA Pipeline Core      │
                        └─────────────────┬─────────────────┘
                                          │
       ┌──────────────────┬───────────────┴───────────────┬──────────────────┐
       │                  │                               │                  │
┌──────▼──────┐    ┌──────▼──────┐                 ┌──────▼──────┐    ┌──────▼──────┐
│   Tier 1    │    │   Tier 2    │                 │   Tier 3    │    │   Tier 4    │
│  Diff &     │    │  Playbook   │                 │ Architecture│    │ AI Semantic │
│  Secrets    │    │  Analyzers  │                 │ Invariants  │    │ Deep Review │
└──────┬──────┘    └──────┬──────┘                 └──────┬──────┘    └──────┬──────┘
       │                  │                               │                  │
       └──────────────────┴───────────────┬───────────────┴──────────────────┘
                                          │
                       ┌──────────────────▼──────────────────┐
                       │   GitHub Step Summary & Annotations │
                       │   Status Checks (Pass / Fail Gate)  │
                       └─────────────────────────────────────┘
```

### 1. Tier 1: Zero-Tolerance Secrets & Credential Detection
* High-entropy regex scanner inspecting added lines.
* Detects AWS access keys, GitHub tokens, Google/Gemini API keys, private keys, database passwords, and connection strings.
* Emits `CRITICAL` blocking errors to prevent credential leaks into Git history.

### 2. Tier 2: Static Analysis & Roslyn Integration
* Enforces custom Roslyn analyzers in `src/Yukti.Analyzers`:
  * `AsyncMethodsRequireCancellationTokenAnalyzer` (YUKTI001)
  * `NoSyncOverAsyncAnalyzer` (YUKTI002)
  * `NoInterpolatedLoggerMessageAnalyzer` (YUKTI003)
  * `NoUnauditedCommandHandlerAnalyzer` (YUKTI004)

### 3. Tier 3: Architectural Invariant Enforcement (Volumes 1 & 5)
* **Zero-Dependency Domain**: Guarantees `Yukti.Domain` contains zero external package dependencies (Volume 1 §15.5).
* **Configuration Hygiene**: Guarantees `ConnectionStrings` are never committed to `appsettings.json` (Volume 4 & 5).
* **Structured Logging**: Flags any string interpolation in logger calls (Volume 5 §8.2).

### 4. Tier 4: Gemini-Powered Deep Semantic Analysis (LLM Reviewer)
* Runs deep semantic review on the diff for race conditions, edge-case mishandling, null-safety, and logic flaws.
* Produces line-level inline feedback and a quality verdict (`APPROVED`, `COMMENT`, `REQUEST_CHANGES`).
* Gracefully degrades to 100% deterministic rules if `GEMINI_API_KEY` is not configured.

---

## Local Usage (Pre-Flight Check)

Developers can run SARVAGHNA locally before pushing changes:

```powershell
# Analyze staged changes before committing
node tools/sarvaghna/sarvaghna.mjs --staged

# Analyze changes between main and current branch
node tools/sarvaghna/sarvaghna.mjs --base main --head HEAD

# Analyze the most recent commit
node tools/sarvaghna/sarvaghna.mjs --base HEAD~1 --head HEAD
```

---

## GitHub Actions Integration

SARVAGHNA runs automatically on every `push` to `main` and every `pull_request` via [`.github/workflows/sarvaghna.yml`](../../.github/workflows/sarvaghna.yml).

### Optional: Enabling AI Semantic Review
To enable Tier 4 (AI Deep Review):
1. Go to your GitHub repository: **Settings → Secrets and variables → Actions**.
2. Add a new repository secret: `GEMINI_API_KEY`.
3. SARVAGHNA will automatically activate deep semantic analysis on subsequent runs.
