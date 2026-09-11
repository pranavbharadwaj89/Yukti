# Volume 5 — Development Standards & Engineering Playbook · Part I — Source Control Workflow


## Table of Contents

  - [Section 1 — Git Branching Strategy](#1-git-branching-strategy)
  - [Section 2 — Pull Request Guidelines](#2-pull-request-guidelines)
  - [Section 3 — Code Review Checklist](#3-code-review-checklist)
  - [Section 4 — Commit Conventions](#4-commit-conventions)
  - [Section 5 — Coding Standards (.NET, React, Python)](#5-coding-standards-net-react-python)
  - [Section 6 — API Versioning](#6-api-versioning)
  - [Section 7 — Error Handling](#7-error-handling)
  - [Section 8 — Logging Conventions](#8-logging-conventions)
  - [Section 9 — Testing Pyramid](#9-testing-pyramid)
  - [Section 10 — Performance Benchmarking](#10-performance-benchmarking)
  - [Section 11 — Security Practices](#11-security-practices)
  - [Section 12 — Dependency Management](#12-dependency-management)
  - [Section 13 — Release Management](#13-release-management)
  - [Section 14 — CI/CD Quality Gates](#14-ci-cd-quality-gates)
  - [Section 1 — Lint & Static Analysis	.NET Roslyn analyzers (strongly-typed IDs, dependency direction, structured logging)	Volume 1, §40.2](#1-lint-static-analysis-net-roslyn-analyzers-strongly-typed-ids-dependency-direction-structured-logging-volume-1-40-2)
  - [Section 2 — Unit & Component Tests	Domain/Application tests (zero infra dependency)	Volume 1, §40.6](#2-unit-component-tests-domain-application-tests-zero-infra-dependency-volume-1-40-6)
  - [Section 3 — Build & Image Creation	Bundle-size budget	Volume 2, §17.2](#3-build-image-creation-bundle-size-budget-volume-2-17-2)
  - [Section 4 — Security & Vulnerability Scanning	Image CVE scanning	Volume 4, §2.5](#4-security-vulnerability-scanning-image-cve-scanning-volume-4-2-5)
  - [Section 5 — Integration Tests	Infrastructure/API integration tests (real test-container DB/Redis)	Volume 1, §40.6](#5-integration-tests-infrastructure-api-integration-tests-real-test-container-db-redis-volume-1-40-6)
  - [Section 6 — End-to-End Tests	Full Nexus-testing-itself E2E suite	Volume 2, §19.8](#6-end-to-end-tests-full-nexus-testing-itself-e2e-suite-volume-2-19-8)
  - [Section 7 — Deploy to Staging	Migration Job gate (schema before app rollout)	Volume 1, §37.5](#7-deploy-to-staging-migration-job-gate-schema-before-app-rollout-volume-1-37-5)
  - [Section 8 — Staging Verification	E2E smoke subset	Volume 2, §19.9](#8-staging-verification-e2e-smoke-subset-volume-2-19-9)
  - [Section 9 — Promote to Production	Manual approval (timing decision only)	Volume 4, §5.5](#9-promote-to-production-manual-approval-timing-decision-only-volume-4-5-5)
  - [Section 15 — Definition of Done](#15-definition-of-done)
  - [Section 16 — Documentation Standards](#16-documentation-standards)

---

## 1. Git Branching Strategy

### 1.1 Purpose — the branch model Volume 4's pipeline already assumes

Volume 4, Section 5.3 specified environments (local, ephemeral-per-PR, staging, production) and Section 6.7 specified branch protection on `main` — both sections implicitly assumed a specific branching model without this volume having yet defined it. This section closes that gap: Nexus uses **trunk-based development**, not GitFlow or a long-lived release-branch model, and this section states precisely why, given everything Volumes 1–4 already committed to.

### 1.2 The model — one long-lived branch, short-lived feature branches

```
main (protected, always deployable — Volume 4 §5.3, §6.7)
 │
 ├── feature/flow-step-retry-policy    (short-lived, days not weeks)
 ├── fix/signalr-reconnect-race         (short-lived)
 └── feature/locator-generator-v2       (short-lived, even for Volume 3 capability work)
```

Every feature branch merges directly to `main` via pull request (Section 2), never through an intermediate `develop` or `release` branch — `main` is the single source of truth or trunk, continuously deployable, exactly as Volume 4, Section 5.4's ephemeral-per-PR-environment design already assumes (that design only makes sense against a trunk-based model; a long-lived `develop` branch would make "deploy this PR's exact state" an ambiguous question relative to which branch it's ultimately headed toward).

### 1.3 Why trunk-based, not GitFlow — reasoning from what this document set already committed to

This is not a default, unexamined choice — it follows directly from decisions already made: Volume 4, Section 5.2's nine-stage pipeline runs in full on every merge to `main`, and Section 5.5 established that production promotion is a *timing* decision, not a *correctness* gate (correctness is fully automated). GitFlow's long-lived `release` branches exist specifically to stabilize a release candidate over time before it ships — a pattern that only makes sense when correctness verification is slow, manual, or incomplete at merge time. Volume 4's pipeline is none of those things by design (Section 5.2's full gate suite runs before merge is even permitted, per Section 6.7's required-status-checks), which makes a stabilization branch pure overhead: there is no additional correctness work left to do on a release branch that the trunk-based pipeline hasn't already done before merge.

### 1.4 Branch naming and scope discipline

```
feature/<short-description>    # new capability, any volume's scope
fix/<short-description>         # bug fix
chore/<short-description>       # tooling, dependency, non-functional change
docs/<short-description>        # this document set itself, or code documentation
```

Every branch is scoped to a single, reviewable unit of change — directly serving Section 2's PR-size guidance and, more fundamentally, Volume 1, Part VI, Section 40.8's ADR-for-deviations discipline, which depends on a change being small and coherent enough that its architectural rationale (if any) is legible in one review, not buried inside an unrelated bundle of changes.

### 1.5 Branch lifetime — short by policy, not just convention

Feature branches are expected to merge within days, not weeks — a branch living longer than a defined threshold (e.g., 5 business days) triggers an automated reminder, not because long-lived branches are forbidden outright, but because they directly work against trunk-based development's core value: the longer a branch diverges from `main`, the more expensive its eventual merge-conflict resolution and the less its ephemeral-per-PR environment (Volume 4, Section 5.4) reflects what `main` will actually look like once it lands. Large, genuinely multi-week efforts (a new capability engine in Volume 3's scope, for instance) are broken into a sequence of smaller, individually-mergeable branches behind a feature flag (Section 1.6) rather than developed on one long-lived branch.

### 1.6 Feature flags — how incomplete work reaches `main` safely

Directly enabling Section 1.5's short-branch-lifetime discipline: incomplete or not-yet-customer-visible functionality merges to `main` behind a feature flag (a simple, centrally-managed boolean/percentage-rollout configuration, distinct from and simpler than Volume 3, Part IV's AI-specific tenant configuration, though following the same "configuration, not code branching" philosophy) — this is what makes "merge early and often" compatible with "never ship incomplete work to a customer," the two properties trunk-based development needs to hold simultaneously to actually work.

### 1.7 Hotfix handling — no separate branch model

Consistent with Section 1.3's reasoning: an urgent production fix follows the *exact same* `fix/` branch → PR → full pipeline → merge → deploy path as any other change, never a separate, abbreviated "hotfix branch" process that skips gates for the sake of speed. This is a deliberate, hard rule directly extending Volume 4, Section 6.7's "no administrator override" branch-protection posture to incident response specifically — the temptation to bypass review and testing under incident pressure is exactly when Volume 1, Architecture Principle 20.4's fail-loud discipline matters most, not a scenario where it's suspended. Volume 4, Section 5.4's ephemeral-per-PR environment makes this fast enough in practice (a hotfix's correctness is verified against a real, isolated environment within minutes, not hours) to not meaningfully trade safety for speed.

### 1.8 Tagging and release correlation

Every merge to `main` that reaches production (Volume 4, Section 5.2, stage 9) is tagged with a semantic version (Section 13, Release Management, specifies the full versioning scheme) — this tag is the same artifact Volume 4, Section 2.6's immutable image-tagging and Section 4.5's Helm chart versioning both correlate against, making "what code is running in production right now" answerable by a single, authoritative Git tag rather than requiring cross-referencing multiple, potentially-drifting version identifiers across this document set's various volumes' artifacts.

### 1.9 Relationship to Volume 1's bounded-context team ownership

Directly extending Volume 1, Part II, Section 7.6's observation that bounded contexts are natural team-ownership units, and Volume 4, Section 7.6's per-SLI ownership model: branch naming (1.4) and PR review assignment (Section 2) both route through this same ownership structure — a branch touching `features/flow-authoring` (Volume 2, Section 5.4) or the Flow Authoring bounded context (Volume 1, Part II, Section 7) is reviewed by that context's owning team by default, giving this volume's process discipline a concrete, already-established organizational structure to route through rather than inventing a new one.



2. Pull Request Guidelines
2.1 Purpose — the unit of change this entire document set's gates apply to

Every quality gate specified across Volumes 1–4 (Volume 1, Part VI, Section 40's coding-standard analyzers; Volume 4, Section 5.2's nine-stage pipeline) fires against a pull request. This section specifies what a well-formed PR looks like — size, description content, and the linkage between a PR and the architectural decisions it might represent — so those gates have a consistently well-shaped artifact to run against.

2.2 PR size — small by policy, with a stated exception process

Target PR size is under 400 lines changed (excluding generated code — Volume 2, Section 4.7's generated DTO types, Volume 1's EF Core migrations — which don't count against this budget since they're not hand-authored, reviewable logic). This is a guideline enforced by CI-surfaced warning (not a hard block, since a small number of legitimate changes — a large but mechanical rename, a generated-migration-heavy schema change — genuinely exceed this size without being harder to review) but treated as the default expectation, directly serving Section 1.5's short-branch-lifetime discipline and the basic principle that a smaller PR gets a more careful review than a large one, an effect worth designing process around rather than treating as an unfortunate but unavoidable human limitation.

2.3 PR description template
markdown
## What
[One or two sentences: what does this change do]

## Why
[The motivating requirement, bug, or improvement — link to an issue/ticket]

## Which volume(s) / bounded context(s) does this touch
[Volume 1 Flow Authoring / Volume 2 flow-authoring feature / Volume 4 CI pipeline / etc.
 — directly informs reviewer routing, Section 1.9]

## ADR required?
[ ] Yes — this deviates from or extends a pattern specified in Volumes 0-4 (link the ADR)
[ ] No — this follows existing, already-specified patterns

## Testing
[What tiers of Volume 1 §40.6 / Volume 2 §19.2 / Volume 3 §19 / Volume 4 §5.2 testing
 does this change add or modify coverage for]

## Security/compliance impact
[Per Volume 1 Architecture Principle 20.5's "security designed in" — does this touch
 authentication, authorization, tenant isolation, secrets, or AI guardrails? If yes, detail.]

The "ADR required?" checkbox directly operationalizes Volume 1, Part VI, Section 40.8's rule that "any code change that deviates from a pattern specified in this volume... requires an accompanying ADR" — making that rule a literal, unskippable checklist item on every PR rather than a norm a reviewer must remember to check for independently.

2.4 Draft PRs and early feedback

A PR may be opened as a draft before it's ready for full review, deliberately triggering Volume 4, Section 5.4's ephemeral-per-PR environment and the fast-feedback tiers of Section 5.2's pipeline (lint, unit tests) without yet requesting human review time — this is the concrete mechanism for getting early, automated signal on a large or uncertain change before investing a reviewer's attention, directly serving Section 1.5's goal of catching problems while a branch is still young and cheap to redirect.

2.5 Linking commits to PRs — squash merge as the default

main's history (Section 1.2) consists of one commit per merged PR (squash-merge), with the PR's description (2.3) becoming that commit's message — this keeps main's history legible at the level that actually matters for future archaeology (what change, why, per Section 2.3's template) rather than preserving every intermediate "fix typo" / "address review comment" commit a branch's development process naturally accumulates. Commit message format for the squashed commit follows Section 4's Commit Conventions specification exactly.

2.6 Cross-volume PRs — when a change spans multiple bounded contexts

A PR touching both a Volume 1 backend command (Part II, Section 13) and its Volume 2 frontend consumer (Section 8's TanStack Query hook) is not split into two separate PRs by default — Section 1.9's ownership-routing model handles this by requesting review from both owning teams on the single PR, since Volume 2, Section 4.7's generated-DTO-type contract means the backend and frontend changes are often genuinely coupled (a backend DTO shape change requires a corresponding frontend regeneration in the same unit of deployable change) and splitting them would create an interim, broken state between the two PRs' merges.

2.7 PR review SLA

A PR requesting non-draft review receives an initial response (approval, requested changes, or a clarifying question) within one business day — directly serving Section 1.5's short-branch-lifetime goal, since a PR waiting days for its first review is functionally a long-lived branch regardless of how small the diff itself is. This SLA is a team-level commitment tracked as a process health metric (Volume 4, Section 7's monitoring philosophy extended to engineering process itself, not just running infrastructure), not merely an aspiration.

2.8 Reverting — a normal, low-friction action

Any merged PR can be reverted by any engineer without requiring the same review SLA (2.7) that landed it — a revert is treated as a safety action, not a normal change, and Volume 1, Architecture Principle 20.4's fail-fast philosophy extends here directly: if a merged change is suspected of causing a regression, reverting first and investigating afterward (rather than debugging forward under production pressure) is the explicitly sanctioned default response, directly consistent with Volume 4, Section 5.6's rollback-integration design at the infrastructure layer, now stated as the equivalent source-control-level practice.

2.9 What a PR is not a substitute for

Consistent with Section 2.1's framing: a PR review is not where architectural decisions get made for the first time — a change significant enough to need an ADR (2.3's checkbox) should have that ADR drafted and, ideally, discussed before the implementing PR is opened, not negotiated for the first time in PR review comments. This mirrors Volume 1, Part VI, Section 40.8's explicit instruction that a deviation be "reviewed and approved by the architecture owner... before merge, not merged first and documented later" — PR review verifies correct implementation of an already-agreed decision, it is not the forum for making that decision.



3. Code Review Checklist
3.1 Purpose — one consolidated checklist for scattered review requirements

Across Volumes 1 through 4, more than a dozen sections established a specific review-time check without any of them being collected into one place a reviewer could actually use — "a reviewer checks that X" appears repeatedly, each time in the section that originally motivated it. This section is the consolidation: the master checklist every PR reviewer works from, with each item citing its origin so a reviewer (or a future volume revision) can trace back to the full reasoning, not just the rule.

3.2 Domain and architecture integrity
 Strongly-typed identifiers used, never raw Guid/int in public signatures (Volume 1, Part II, §10.8)
 No cross-aggregate direct reference — aggregates communicate only via injected port interfaces, never holding another aggregate's repository or instance (Volume 1, Part II, §9.5; Part VI, §40.3)
 No project reference from Nexus.Domain to any outer layer — dependency direction intact (Volume 1, Part V, §36.3)
 Composition vs. association used correctly — a parent aggregate's owned entities use composition; cross-aggregate references use identifiers only (Volume 1, Part V, §35.4, §35.6)
 Command handlers check authorization before loading any aggregate — EnsurePermission is the first meaningful line (Volume 1, Part II, §13.6; Part IV, §25.3; Part VI, §40.3)
 New commands inherit AuditableCommandHandler, or carry an explicit, reviewed justification for exemption (Volume 1, Part IV, §27.2; Part VI, §40.3)
 TenantId is never derived from a client-supplied header, query parameter, or any input other than ITenantContextAccessor (Volume 1, Part IV, §26.4)
3.3 Plugin and module-parity integrity
 No ModuleKind-specific branching in orchestrator-core or frontend authoring/canvas code — the module-parity principle (Volume 1, Architecture Principle 20.3; Volume 2, UX Principle 1.3–1.4)
 New IAutomationModule interface changes are additive-only within a minor/patch contract version (Volume 1, Part III, §18.7)
 Singleton-lifetime services hold no new mutable instance state — reviewed explicitly for any field added to a built-in module (Volume 1, Part III, §17.5, §40.3)
 A change to the module-parity test (Volume 2, §19.5) is itself reviewed with elevated scrutiny — this test's own weakening is a standing risk, not just its failure
3.4 Data and query integrity
 New queries against flow_runs, step_results, or audit_entries include EXPLAIN ANALYZE output in the PR if the table is expected to scale toward NFR-SCALE-3 (Volume 1, Part VI, §38.6, §40.3)
 Row-level security policy coverage verified for any new multi-tenant table (Volume 1, Part IV, §26.3; Volume 4, §13.4)
 DTOs, never domain entities, cross the API boundary (Volume 1, Part II, §14.8)
3.5 Security and secrets
 No credential-shaped field on any capability-engine or module input type (Volume 3, Part V, §18.3)
 No raw secret value in any Helm values file, at any tier (Volume 4, §4.8)
 [SensitiveValue]-attributed fields correctly excluded from audit metadata and log derivation (Volume 1, Part IV, §27.4, §28.5)
 New NetworkPolicy or Kubernetes RBAC changes reviewed with elevated attention — the highest-security-impact category of infrastructure change (Volume 4, §3.5, §4.9)
 New container images pass CVE scanning before merge is possible — automated gate, but a reviewer confirms no scan-suppression was added without justification (Volume 4, §2.5)
3.6 AI-specific integrity
 New capability-engine output types enforce structured output — no free-text field a downstream consumer must parse heuristically (Volume 3, Part I, §3.4)
 Every AiPlanningResult field is populated on every code path — guardrailChecks and costRecord are never optional (Volume 3, Part I, §2.3)
 New guardrail checks are additive, never disabling or weakening a platform-baseline check (Volume 3, Part V, §17.5, §17.6)
 New capability-engine changes include an evaluation-set update and pass the offline gate before merge (Volume 3, Part V, §19.3)
3.7 Frontend-specific integrity
 Import boundaries respected — primitives/ → composites/ → features/*/ → pages/, no upward or lateral violation (Volume 2, §5.3)
 Server state never duplicated into local component state (Volume 2, §4.5, §8.2)
 No hardcoded design-token value — colors, spacing, motion all reference --nexus-* custom properties (Volume 2, §2.9, §3.2)
 New interactive components implement their accessibility contract — keyboard operability, ARIA roles — before merge, not as a follow-up (Volume 2, §18.4, §18.8)
3.8 Documentation and traceability
 "ADR required?" checkbox (Section 2.3) correctly answered — a deviation from an established pattern has a linked ADR
 New ActionSchema/ParamSpec entries have non-empty Description fields (Volume 1, Part VI, §40.7)
 New dashboards, runbooks, or alert rules are versioned alongside the code they describe, not created ad hoc outside version control (Volume 4, §7.7, §10.2)
3.9 How this checklist is maintained

This checklist is not frozen at this volume's publication — per Volume 1, Part VI, Section 40.8's ADR discipline extended to this document set's own review process: a new pattern established anywhere in Volumes 0–4 (or a future amendment to any of them) that implies a new review-time check is added here, in the same PR that establishes the pattern, keeping this section synchronized with the architecture it enforces rather than drifting stale — directly the same "documentation as code, updated in the same change that necessitates it" discipline this document set has applied consistently since Volume 1.

3.10 What this checklist does not replace

This checklist covers the specific, previously-scattered rules this document set has already established — it is not a substitute for a reviewer's own judgment about code clarity, test adequacy, or whether a change actually solves the problem it claims to. A PR that satisfies every item here but is otherwise confusing, undertested by ordinary standards, or solving the wrong problem should still receive that feedback — this checklist is a floor, ensuring architectural integrity specifically, not a ceiling defining the entirety of what good review looks like.



4. Commit Conventions
4.1 Purpose — the format behind Section 2.5's squash-merge commits

Section 2.5 established that main's history consists of one commit per merged PR, with the PR description becoming that commit's message. This section specifies the exact format that message follows — Conventional Commits, chosen specifically because Section 13 (Release Management) depends on it for automated changelog generation and semantic-version determination.

4.2 Format
<type>(<scope>): <short description>

<body — the "why," expanded from the PR's Why section, §2.3>

<footer — breaking changes, issue references>
4.3 Type taxonomy
Type	Meaning	Version impact (Section 13.3)
feat	New capability	Minor version bump
fix	Bug fix	Patch version bump
perf	Performance improvement with no behavior change	Patch version bump
refactor	Code restructuring with no behavior or performance change	No version bump (internal only)
docs	Documentation-only change (including this document set itself)	No version bump
chore	Tooling, dependency, CI/CD configuration	No version bump
test	Test-only addition or change	No version bump
breaking (via footer, not type)	Any type combined with a BREAKING CHANGE: footer	Major version bump

This taxonomy directly drives Section 13.3's automated semantic-versioning — a feat commit automatically proposes a minor bump, a BREAKING CHANGE: footer automatically proposes a major bump, removing manual version-number judgment from the release process for the common case (Section 13.5 covers the exception process for when automated determination needs override).

4.4 Scope — mapping to this document set's bounded contexts and volumes
feat(flow-authoring): add conditional step branching to the visual canvas
fix(orchestration): correct retry backoff calculation for flaky steps
feat(ai-planning): add claim-evidence grounding check to Root Cause Analysis
chore(helm): bump PostgreSQL subchart to 16.2

Scope values map directly to Volume 1's bounded contexts (Part II, Section 7), Volume 2's feature modules (Section 6), Volume 3's capability engines (Part III), or Volume 4's infrastructure components — reusing, once again, the exact same ubiquitous-language vocabulary Volume 1, Part II, Section 6.3 established and every subsequent volume has continued to reuse rather than inventing a parallel commit-scope taxonomy that would need independent maintenance.

4.5 Breaking change footer — the specific trigger for major version bumps
BREAKING CHANGE: the AutomationModule interface's Setup method now requires
a CancellationToken parameter. Existing modules must update their
implementation signature.

Directly correlated with Volume 1, Part III, Section 18.7's AutomationModule contract-versioning discipline and Volume 3, Part I, Section 3.3's prompt-versioning discipline — a commit introducing a breaking change to either of these two especially consequential contracts (this document set's two most-emphasized "everything depends on this staying stable" interfaces) must carry this footer, making the breaking change mechanically detectable by Section 13's release tooling rather than relying on a human remembering to flag it during release preparation.

4.6 Commit message quality — the body as durable documentation

The commit body (4.2) is written for a future engineer with zero context on the original PR discussion — directly extending this document set's "why," not just "what," documentation philosophy (every volume's sections have consistently explained rationale, not just stated decisions) to the individual-commit level, the most granular unit of durable project history. A commit body that says only "fixes bug" fails this bar; a commit body explaining what was actually wrong and why the fix is correct meets it — this distinction is a named item reviewers check for (extending Section 3's checklist informally, since commit-message quality is judgment-based rather than mechanically checkable the way Section 3's items mostly are).

4.7 Automated enforcement

Commit message format (4.2's structure, 4.3's type taxonomy) is validated by a commit-lint check integrated into Volume 4, Section 5.2's pipeline (stage 1, Lint & Static Analysis) — a malformed commit message fails the same build-blocking gate as a linting violation, consistent with this document set's consistent preference for tooling-enforced discipline over convention-dependent discipline (Volume 1, Part VI, Section 40.2's analyzer-enforced coding rules being the original template this practice extends).

4.8 Relationship to Section 1's squash-merge and Section 13's release automation

This section's format is not an isolated stylistic preference — it is the specific, mechanically-parseable input Section 13's automated changelog generation and version-bump determination directly consume. A commit that doesn't follow this format isn't merely "less readable" — it's a data-integrity gap in the automated release pipeline Section 13 specifies, which is the concrete reason this convention is enforced by tooling (4.7) rather than left as team guidance a busy engineer might reasonably skip under deadline pressure.


____________________________________________________________________________________________________________________________________________________



5. Coding Standards (.NET, React, Python)
5.1 Purpose — pointing to authoritative sources, not duplicating them

Two of this section's three languages already have a fully-specified coding standard elsewhere in this document set: Volume 1, Part VI, Section 40 specified .NET/C# standards in complete, enforced detail; Volume 2's Sections 4, 5, and 9 collectively specified React/TypeScript standards. This section does not restate either — it points to them as authoritative and adds only what those volumes correctly left unaddressed: cross-language consistency principles, and a full, honest specification of Python's role, which no prior volume locked down.

5.2 .NET/C# — authoritative source

Volume 1, Part VI, Section 40 is the complete standard. Every rule there — strongly-typed identifiers, dependency-direction enforcement, structured-logging-only, the AuditableCommandHandler inheritance requirement, async/CancellationToken propagation, the testing-tier conventions — applies without modification. This section's only addition: Volume 1, Section 40's rules are enforced via Roslyn analyzers (Section 40.2 there) configured as .editorconfig + custom analyzer package, versioned in the same repository and updated through the same PR process as any other code change (Volume 5, Section 2), never configured ad hoc per-project or left to individual IDE settings.

5.3 React/TypeScript — authoritative source

Volume 2, Sections 4 (React Architecture), 5 (Folder Structure), and 9 (Component Library) collectively form the complete standard. Functional components and hooks exclusively; strict TypeScript with generated DTO types (Volume 2, Section 4.7); the four-tier component classification (Section 4.4) and its enforced import boundaries (Section 5.3); design-token-only styling (Section 2.9, 3.2). This section's only addition: these rules are enforced via ESLint configuration (the import-boundary plugin named in Volume 2, Section 5.3) plus TypeScript's own strict-mode compiler, both configured and versioned identically to Section 5.2's .NET analyzer approach — one consistent enforcement philosophy across both primary languages, even though the specific tooling necessarily differs.

5.4 Python — honest scoping, since no prior volume locked this down

This is the one language in this section's title that genuinely needs new specification here, and this document set's practice of honest scoping (Volume 2, Section 16.1's offline-support disclosure; Volume 1, Section 39.9's scaling-ceiling disclosure) applies directly: Python has no role in Nexus's production request path. The backend is .NET exclusively (Volume 0, Section 21.2, locked); the frontend is React exclusively (Volume 0, Section 21.3, locked). Python's actual, legitimate scope is narrower and specific:

Use case	Where it appears
AI evaluation-set tooling and offline evaluation harnesses	Volume 3, Section 3.6, 19.3 — running evaluation sets against capability engines, computing structural-validity/grounding-accuracy scores
RAG indexing and embedding-pipeline experimentation	Volume 3, Section 4.4 — prototyping and validating retrieval-quality changes before they're operationalized in the production indexing pipeline
Data analysis for capacity planning and cost modeling	Volume 4, Section 19.3, 21.2 — ad hoc analysis of usage/cost trends, not the production monitoring pipeline itself (which remains Prometheus/Grafana, Volume 4 Sections 9–10)
One-off infrastructure/migration scripting	Rare, reviewed case-by-case — most infrastructure automation is Helm/YAML (Volume 4, Section 4) or bash (CI workflow steps, Volume 4, Section 6), with Python reserved for genuinely data-processing-heavy one-off tasks poorly suited to either

Python code in every one of these categories is tooling and analysis code, never a deployed service — it does not run in any of the containers Volume 4, Section 2.2 specified, is not part of any request-serving path, and has no NFR-PERF-style latency commitment of its own. This boundary is stated as a hard architectural constraint, not a soft preference: a proposal to implement any customer-facing capability in Python would be a significant, ADR-requiring deviation from Volume 0's locked technology decisions (Section 21.2–21.3), not a routine implementation choice available to an individual engineer.

5.5 Python style and tooling

Where Python is used (5.4's four categories), it follows PEP 8 via ruff (linting and formatting in one tool, chosen over the historically-separate black/flake8/isort combination for its speed and configuration simplicity) and mypy in strict mode for type checking — Python code is type-annotated throughout, directly consistent with this document set's cross-language preference for strong typing wherever the language supports it (Volume 1's strongly-typed IDs, Volume 2's TypeScript strict mode, now Python's mypy strict mode as the third instance of the same underlying principle).

5.6 Cross-language consistency principles

Three principles apply identically regardless of which of these three languages a given piece of code is written in, restated here as the unifying thread across Sections 5.2–5.5:

Strong typing wherever the language allows it — strongly-typed IDs (.NET), strict TypeScript (React), mypy strict mode (Python).
Structured logging, never string interpolation — Volume 1, Section 28.2's rule, extended to Python tooling via Python's standard logging module's structured-extra-fields capability rather than f-string-interpolated log messages.
Tooling-enforced, not convention-dependent — every standard in this section has an automated enforcement mechanism (analyzers, ESLint, ruff/mypy), never a rule that exists only as documentation a developer might or might not follow.
5.7 Adding a fourth language — the bar this section sets

Consistent with Volume 0, Section 21.2's original locked-technology reasoning: introducing any additional language beyond these three requires the same ADR-and-architecture-owner-approval process Volume 1, Part VI, Section 40.8 established for any deviation, with a specific, required justification addressing why none of the three already-standardized languages (5.2–5.5) adequately serve the new use case — this section's three-language scope is a deliberate constraint on this platform's own complexity, not an arbitrary historical accident to be casually expanded.



6. API Versioning
6.1 Purpose — one unified policy across this platform's three versioned contracts

This document set has specified three genuinely distinct versioned contracts, each in the volume where it was architecturally motivated: the REST API (Volume 1, Part V, Section 30.6), the AutomationModule plugin interface (Volume 1, Part III, Section 18.7), and AI prompt templates (Volume 3, Part I, Section 3.3). Each of those sections specified versioning correctly for its own context, but never as one unified, cross-referenced policy — this section is that unification, stated once so a future contract (a fourth versioned surface this platform doesn't yet have) has one clear policy to follow rather than three scattered precedents to reconcile.

6.2 The three existing contracts, compared
Contract	Versioning scheme	Breaking-change trigger	Deprecation window
REST API	Path segment (/api/v1/...)	Any change removing/renaming a field, changing a field's type, or changing an endpoint's semantics	Both versions served concurrently for a defined period (Volume 1, Part V, §30.6)
AutomationModule interface	Semantic version of the interface itself	Adding a required method, removing any method	Additive-only for minor/patch; major requires a defined migration window (Volume 1, Part III, §18.7)
AI Prompt Templates	Semantic version per template (flow-generation.v3)	Any change to outputSchema or requiredContextFields	New version must pass Section 3.6's evaluation gate before traffic shifts; old version retained for rollback (Volume 3, Part I, §3.3)
6.3 The common thread — semantic versioning as the universal scheme

Every contract in 6.2 uses semantic versioning (major.minor.patch or an equivalent path-segment analog), with the same major/minor/patch semantics applying uniformly: major = breaking, requires explicit migration; minor = additive, backward-compatible; patch = non-functional refinement, invisible to consumers. This uniformity is deliberate — an engineer who understands what "major version bump" means for the REST API (6.2's first row) should not need to relearn the concept for the plugin contract or prompt templates; the underlying discipline is identical even though the mechanics of expressing a "version" differ (a URL path segment versus a semantic-version string versus a template identifier suffix).

6.4 Deprecation policy — the universal minimum window

Every deprecation, regardless of which of 6.2's three contracts it applies to, carries a minimum 90-day concurrent-availability window between announcing a deprecation and actually removing the deprecated version — chosen as a floor, not a ceiling (a contract with especially broad external consumption, like the REST API's third-party CI integrations per Volume 0's UC-4, may warrant a longer window, decided case-by-case, but never shorter than this floor). This directly protects Persona Marcus's CI integrations (Volume 0, Section 10.3) and any marketplace module developer's (Persona Raj's) integration against Nexus's own release cadence — a deprecation window shorter than this floor would directly contradict Volume 1, Part V, Section 30.6's original stated rationale ("Marcus's CI integrations depend on this API not breaking underneath a working pipeline without warning").

6.5 Deprecation notification

Every deprecation is announced through three simultaneous channels: the API's own response (a Deprecation HTTP header for REST endpoints, directly extending Volume 1's already-specified error-response discipline, Part V Section 30.5, to non-error responses too), the changelog (Section 13's release-management artifact), and — for the AutomationModule interface specifically, given Volume 1, Part III, Section 18's emphasis on this contract's outsized importance to the plugin ecosystem — a direct notification to registered marketplace module developers (Volume 0, Section 17), not merely a passive changelog entry a developer would need to actively check for.

6.6 Versioning and the evaluation-gate difference — why prompts aren't purely mechanical

Worth naming explicitly, since it's a genuine, deliberate asymmetry between 6.2's three rows: REST API and AutomationModule version bumps are triggered by a structural, mechanically-detectable change (a field removed, a method signature changed) — but a prompt template version bump (Volume 3, Section 3.6) additionally requires passing an evaluation-set quality gate before the new version can carry production traffic, even for a version bump that wouldn't itself be "breaking" in the structural sense. This is not an inconsistency; it's the correct reflection of a genuine difference in what could go wrong: a REST API's correctness is determined by its schema, fully verifiable by static analysis, while a prompt's correctness depends on the model's actual behavior against real inputs, which only Volume 3's evaluation-set discipline can verify — the versioning policy (6.3's shared semantic-versioning discipline) is unified; the promotion gate is appropriately different per contract type.

6.7 Internal versioning — what this policy does not cover

This section's scope is deliberately limited to contracts with external or semi-external consumers (a third-party CI integration, a marketplace module developer, a specific model/prompt pairing whose behavior matters to evaluation history) — purely internal interfaces (a private method signature within Nexus.Application, an internal Zustand store's shape within Volume 2's frontend) are not versioned under this policy at all; they change freely as part of normal refactoring, governed only by Section 3's code-review checklist and ordinary testing, not by this section's deprecation-window discipline. Applying full versioning ceremony to purely internal code would be exactly the kind of "complexity that doesn't earn its cost" this document set has consistently avoided (Volume 1, Architecture Principle 20.7).

6.8 Versioning documentation as a release artifact

Every version bump across all three contracts (6.2) is documented in the same changelog (Section 13) using the same Conventional Commits BREAKING CHANGE: footer convention (Volume 5, Section 4.5) that already specifically named the AutomationModule and prompt-versioning contracts as requiring this footer — this section closes the loop Section 4.5 opened, confirming that footer's purpose is precisely to drive this section's unified versioning-and-deprecation policy, not an independent, disconnected commit-message convention.



7. Error Handling
7.1 Purpose — the topic prior volumes touched but never fully specified

Every volume in this document set has handled errors within its own scope — Volume 1's RFC 7807 REST error format (Part V, Section 30.5), domain-invariant rejections returning result objects rather than throwing (Part II, Section 9.2's FlowPublishResult), Volume 3's typed failures (AiUnavailableError, GuardrailBlockedError). This section is where those scattered, individually-correct decisions get unified into one coherent error-handling philosophy — the "when do we throw, when do we return a result, and how does an error surface to a human" policy this document set has practiced consistently but never stated as a single, named standard.

7.2 The core decision — exceptions for the exceptional, result types for the expected

The single governing rule: an outcome that is a normal, anticipated possibility of an operation is represented as a typed result, never an exception; an outcome that indicates something has gone genuinely wrong with the system itself is an exception. This is not a novel rule invented here — it is the exact pattern already present throughout Volumes 1 and 3, now named explicitly:

Outcome	Exceptional or expected?	Representation
A Flow fails to publish because a referenced module doesn't exist	Expected — a normal outcome of a normal operation	FlowPublishResult (Volume 1, Part II, §9.2) — a typed result, not an exception
A StepResult fails because the target system returned an error	Expected — this is what the flow was testing for	StepStatus.Failed (Volume 1, Part II, §11.3) — a typed status, not an exception
An AI provider times out	Expected — provider unavailability is anticipated, per NFR-REL-3	AiUnavailableError typed exception (Volume 3, Part I, §2.5) — see 7.3 for why this is an exception despite being "expected"
A database connection is lost mid-transaction	Genuinely exceptional — the system itself is in a degraded state	A thrown, unhandled-at-the-business-logic-layer exception, caught only at the infrastructure boundary (Section 7.5)
A required configuration value is missing at startup	Genuinely exceptional — the application cannot function	A thrown exception, crashing startup deliberately (fail-fast, not a degraded partial start)
7.3 Why AiUnavailableError is an exception despite being an "expected" outcome — resolving the apparent tension

Row three of 7.2's table deserves explanation, since it looks like it violates the rule stated in 7.2: an AI provider timeout is expected (NFR-REL-3 anticipates it), yet it's represented as a thrown exception, not a result type. The resolution: the type of representation (exception vs. result) is chosen based on how many layers need to react to it, not purely on how "expected" the outcome is. A FlowPublishResult's failure is handled by exactly one layer (the calling command handler, which returns it directly to the API response) — a result type is the right fit. AiUnavailableError needs to propagate through the Planning Engine (Volume 3, Section 2.4), potentially through Section 15.6's fallback-chain logic, and finally to a Volume 1 command handler that catches it specifically — an exception's automatic propagation-until-caught behavior is the better structural fit for a condition that legitimately needs to unwind through multiple layers before reaching the place equipped to handle it, even though the underlying event is entirely expected. This is the precise, stated resolution to what would otherwise look like an inconsistency between Volume 1's and Volume 3's error-handling choices.

7.4 Exception hierarchy
csharp
public abstract class NexusException : Exception { }                    // root — never thrown directly

public class DomainException : NexusException { }                        // Volume 1, Part II §9's
                                                                              // aggregate invariant violations —
                                                                              // used sparingly, since most domain
                                                                              // rejections are result types (7.2),
                                                                              // reserved for truly invalid states
                                                                              // a well-behaved caller should never
                                                                              // actually trigger

public class InfrastructureException : NexusException { }                // database, cache, external-service
                                                                              // failures — Section 7.5's boundary

public class AiCapabilityException : NexusException { }                   // AiUnavailableError, GuardrailBlockedError
    // : AiCapabilityException                                              (Volume 3, §2.5, §17.7) both derive here

public class AuthorizationException : NexusException { }                  // Volume 1, Part IV §25's ForbiddenException

Every custom exception in the codebase derives from NexusException, never directly from the base .NET Exception — this is what makes Section 7.6's uniform correlation-ID attachment and Section 7.7's uniform logging behavior possible to enforce as a single base-class concern rather than requiring every individual exception type to separately remember to implement it.

7.5 The catch boundary — where exceptions stop propagating

Exceptions are caught at exactly two layers, never scattered as ad hoc try/catch blocks throughout business logic: the command/query handler boundary (Volume 1, Part II, Section 13.7's pattern — a handler catches domain-specific exceptions it knows how to translate into a result, like AiCapabilityException becoming a graceful non-AI fallback per Volume 3, Section 2.5) and the API controller boundary (Volume 1, Part V, Section 30.3 — any exception surviving to this point is translated into the RFC 7807 error response, Section 30.5, with InfrastructureException and any truly unhandled exception type mapped to a generic 500 response, never leaking internal exception details to an API consumer). No business-logic code catches a generic Exception — every catch clause is specific to a named exception type this section's hierarchy (7.4) defines, directly enforcing Architecture Principle 20.4's "fail loud, fail informatively": a broad catch-and-swallow is exactly the pattern that principle exists to prevent.

7.6 Correlation and context on every exception

Every NexusException carries the same CorrelationId/FlowRunId scope Volume 1, Part IV, Section 28.4 established for logging — attached automatically via the exception's construction within an active ILogger.BeginScope, not manually threaded through by each throw site — ensuring Section 7.5's API-boundary translation into an RFC 7807 response's correlationId field (Volume 1, Part V, Section 30.5) is always populated correctly, regardless of how deep in the call stack the original exception was thrown.

7.7 Frontend error handling — consistency with the backend philosophy

Volume 2's error boundaries (Section 4.6's module-scoped boundaries, Section 7.7's route-level boundary) implement the identical underlying philosophy: a known, anticipated failure (a query returning an error response, per Volume 2, Section 8.2's TanStack Query error states) is handled as data — rendered as an explicit error UI state — never thrown as a JavaScript exception the component tree must catch; a genuinely unexpected rendering failure (a bug causing a component to throw during render) is caught by an error boundary, the React equivalent of Section 7.5's API-controller catch boundary. The same "expected → typed data, unexpected → exception-and-boundary" split from 7.2 holds across both languages, restated here as explicit cross-language consistency (extending Section 5.6's three cross-language principles with a fourth, error-handling-specific one).

7.8 What error handling is not a substitute for

Consistent with Section 3.10's equivalent caveat for the code review checklist: this section's rules produce consistent, well-structured error handling — they do not, by themselves, guarantee an error is handled correctly in the sense of choosing the right recovery action for a given failure. That judgment remains the responsibility of the engineer writing the handling code and the reviewer verifying it (Section 3's checklist), with this section providing the structural framework that judgment operates within, not a replacement for it.



8. Logging Conventions
8.1 Purpose — restating Volume 1's standard as binding, and closing the frontend gap

Volume 1, Part IV, Section 28 specified backend operational logging completely — structured logging, log levels, correlation scoping, sensitive-data exclusion. This section restates that as the binding standard (no changes), and closes a genuine gap: Volume 2 never specified an equivalent frontend logging standard, and Volume 4's infrastructure/CI tooling has its own implicit logging patterns that were never reconciled with Volume 1's discipline. This section unifies all three.

8.2 Backend logging — authoritative source

Volume 1, Part IV, Section 28 is the complete standard. Structured logging exclusively (Section 28.2's prohibition on string interpolation, enforced via the Roslyn analyzer named there); the six-level severity table (Section 28.3); correlation-scope propagation via ILogger.BeginScope (Section 28.4); the sensitive-data denylist (Section 28.5); tiered retention (Section 28.6, operationalized in Volume 4, Section 11.3's Elasticsearch ILM policy). Nothing in this section modifies any of it.

8.3 Frontend logging — the standard Volume 2 never specified

Browser-based structured logging follows the identical philosophy Volume 1, Section 28.2 established, adapted to the browser environment:

typescript
// Required convention — structured, matching Volume 1 §28.2's backend pattern exactly:
logger.info('Flow published', { flowId, userId, tenantId });

// Prohibited — the frontend equivalent of Volume 1 §28.2's banned string interpolation:
console.log(`Flow ${flowId} published by ${userId}`);

Frontend logs are exported via the browser OpenTelemetry SDK to the same Collector gateway (Volume 4, Section 8.4) already receiving Core Web Vitals RUM data — meaning frontend operational logs join the exact same correlation, scrubbing (Volume 4, Section 8.6), and retention (Volume 1, Section 28.6, via Volume 4, Section 11.3) pipeline as backend logs, rather than living in a separate, browser-console-only world invisible to the same investigative workflow Volume 1, Section 28.4 designed for backend correlation. A raw console.log/console.error call is prohibited in production code paths (permitted only in local-development-only code, clearly marked, per Volume 4, Section 1's dev-environment scope) — enforced via the same ESLint tooling already governing Volume 2's other conventions (Section 5.3).

8.4 Frontend-specific correlation

Every frontend log statement automatically carries the current CorrelationId (propagated from the initiating API request, Volume 1, Section 28.4) plus a frontend-specific SessionId (distinct from AiSession, Volume 3, Section 5.2 — this is the browser tab's own lifecycle identifier) — giving an engineer investigating a customer-reported frontend issue the same "reconstruct exactly what happened" capability Volume 1, Section 28.4 described for backend investigation, now genuinely spanning both the browser and server side of a single user interaction.

8.5 CI/infrastructure logging — reconciling Volume 4's implicit patterns

Volume 4's CI pipeline (Section 5–6) and infrastructure tooling (Helm hooks, migration Jobs) produce their own operational output, which — until this section — was never explicitly reconciled with Volume 1's structured-logging discipline. This section closes that gap: CI/CD pipeline logs (Volume 4, Section 5.7's pipeline-observability data) and infrastructure Job logs (the migration Job, Volume 1, Part VI, Section 37.5) are captured as structured output wherever the underlying tool supports it (GitHub Actions' own structured step-output capability, kubectl logs output parsed and re-emitted in structured form by the same Collector pipeline, Volume 4, Section 8.2) — brought into the same observability pipeline as application logs specifically so a deployment-correlated investigation (Volume 4, Section 10.8's deployment-annotation correlation) can trace from "this deployment happened" through "here's what the migration Job logged" through "here's what changed in application behavior afterward" as one continuous, correlated narrative, not three disconnected log sources an engineer must manually stitch together.

8.6 Log level consistency across languages

Volume 1, Section 28.3's six-level table (Trace/Debug/Information/Warning/Error/Critical) is the canonical severity vocabulary — frontend logging (8.3) and CI/infrastructure logging (8.5) both map to this exact same six-level scale, never an independently-invented severity taxonomy, ensuring Volume 4, Section 7.5's three-tier alerting-severity model (which itself derives from this same underlying vocabulary) applies uniformly regardless of which layer of the stack a given log record originated from.

8.7 What gets logged — restating the "when," not just the "how"

Beyond format (8.2–8.5) and severity (8.6): every state-changing operation (a command handled, per Volume 1, Section 13; a mutation dispatched, per Volume 2, Section 8.2) logs at Information level on success and Warning/Error on failure, as a baseline — this is distinct from and complementary to Volume 1, Part IV, Section 27's audit trail (which records who did what for compliance) — operational logging additionally records enough technical detail (which layer, what parameters, what the actual failure was) to support the debugging use case Section 8.4 described, a genuinely different purpose from audit's compliance purpose even when both are triggered by the same underlying event, restating Volume 1, Section 27.7's Audit-vs-Logging distinction one final time as this document set's last word on a distinction it needed to make repeatedly across four volumes.

8.8 Enforcement

Every logging rule in this section is enforced by the same tooling-first philosophy this entire Part has applied consistently: Roslyn analyzers for .NET (8.2, inherited from Volume 1), ESLint rules for TypeScript (8.3), and a CI-pipeline linting step verifying structured-output usage where tooling supports it (8.5) — logging convention violations fail the build, joining every other coding-standard violation Section 5 already established as a build-blocking, not merely review-flagged, category of issue.


____________________________________________________________________________________________________________________________________________________


9. Testing Pyramid
9.1 Purpose — one diagram for four volumes' worth of testing tiers

Volume 1, Part VI, Section 40.6 specified backend testing tiers. Volume 2, Section 19.2 specified a parallel four-tier frontend model. Volume 3, Section 19.2 specified offline/online AI evaluation as a distinct regime. Volume 4, Sections 17.5, 18.5, and 20.7 specified disaster-recovery, chaos-engineering, and restore testing. None of these were ever drawn as one picture. This section is that picture — the single testing pyramid every engineer on this project should be able to point to and locate their own work within.

9.2 The unified pyramid
                              ▲
                             ╱ ╲
                            ╱   ╲     E2E / System Tests
                           ╱     ╲    Volume 2 §19.8 (Nexus testing itself via
                          ╱───────╲   its own Web module) + Volume 4 §5.2 stage 6
                         ╱         ╲  Slowest, fewest, highest confidence
                        ╱  Integr-  ╲
                       ╱   ation     ╲  Volume 1 §40.6 (Infrastructure/API
                      ╱───────────────╲ IntegrationTests) + Volume 2 §19.6 (MSW-backed)
                     ╱                 ╲
                    ╱   Component /      ╲  Volume 1 §40.6 (Application tests) +
                   ╱    Application       ╲ Volume 2 §19.4 (component + visual regression)
                  ╱───────────────────────╲
                 ╱                         ╲
                ╱   Unit / Domain            ╲  Volume 1 §40.6 (Domain.Tests, zero
               ╱────────────────────────────── ╲ infrastructure dependency) + Volume 2
              ╱                                 ╲ §19.3 (hook unit tests) + Volume 5 §5's
             ╱___________________________________╲ Python evaluation-tooling unit tests
                                                      Fastest, most numerous, run on every commit

     ═══════════════════════════════════════════════════════════════
     Orthogonal, not part of the vertical stack above:

     AI Evaluation (Volume 3 §19.2-19.4)     — offline evaluation sets gate every
                                                  capability-engine release; online
                                                  metrics run continuously in production

     Resilience Testing (Volume 4 §17.5,      — quarterly DR game days, continuous
       §18.5, §20.7)                             chaos engineering, weekly restore tests

     Adversarial Testing (Volume 2 §19.11,    — plugin-sandbox and guardrail-bypass
       Volume 3 §17.9)                            attempts, deliberately hostile inputs
9.3 Why AI evaluation and resilience testing sit outside the classic pyramid

This is a deliberate structural choice worth explaining rather than a diagram simplification: the classic testing pyramid (unit → integration → E2E) measures correctness of code given known inputs — a fundamentally different question from quality of AI-generated output given open-ended real-world input (Volume 3's concern) or survival of infrastructure given a catastrophic failure condition (Volume 4's concern). Forcing these into the same vertical hierarchy would misrepresent their actual relationship to the pyramid's tiers — an AI evaluation set isn't "slower and more expensive than a unit test but faster than an E2E test" in any meaningful sense; it's answering a different kind of question entirely, using a different kind of pass/fail criterion (structural validity, grounding accuracy, calibration — Volume 3, Section 19.3's table — rather than a deterministic assertion). Drawing them as orthogonal, rather than jamming them into a fifth tier, is more honest about what they actually measure.

9.4 Test-tier ownership and the "who writes what" question
Tier	Written by	Reviewed against
Unit/Domain	The engineer implementing the change	Section 3's code review checklist §3.2 items
Component/Application	The engineer implementing the change	Section 3's §3.3, §3.7 items
Integration	The engineer implementing the change, for changes touching persistence/external boundaries	Section 3's §3.4 items
E2E/System	Shared ownership — new user-journey coverage added by whichever team owns that journey's primary feature module (Volume 2, §6)	Volume 2, §19.8's dogfooding design
AI Evaluation	The Volume 3 capability-engine owner (Volume 3, §19.7's per-metric ownership)	Volume 3, §19.3's release gate
Resilience	Infrastructure/platform team (Volume 4, §7.6's ownership model)	Volume 4, §17.5, §18.5

This table directly extends Volume 4, Section 7.6's per-SLI ownership model and Volume 1, Part II, Section 7.6's bounded-context-as-team-ownership-unit principle to testing specifically — the same organizational structure this document set has used consistently for operational ownership now governs test-writing ownership too, rather than testing responsibility being assigned through an independent, parallel structure.

9.5 Coverage expectations per tier

Consistent with this document set's preference for tooling-enforced standards over aspirational guidance (Section 5.6, 5.7): unit/domain tier coverage is measured and enforced at a high threshold (e.g., 90%+ line coverage on Nexus.Domain, directly achievable given that layer's zero-dependency design per Volume 1, Part III, Section 15.5 making it fully unit-testable) — coverage thresholds decrease progressively up the pyramid (component/application tier, then integration tier), reflecting that higher tiers are inherently more expensive per test and correctness at lower tiers reduces the marginal value of exhaustive higher-tier coverage, not that higher-tier correctness matters less. E2E/System tier coverage is measured differently — not by code-line percentage but by use-case coverage against Volume 0's use-case catalog (Part II, Section 11) — every named use case (UC-1 through UC-7) has at least one E2E test exercising it, tracked as a checklist against that catalog rather than a percentage metric that wouldn't meaningfully apply to this tier.

9.6 Flake tolerance per tier

Directly extending Volume 1, Part IV, Section 29.5's platform flake-rate metric (a production concern) to this document set's own test suite (a development concern, the analogous but distinct question of "how often does a test fail for reasons unrelated to a real regression"): unit/domain and component/application tiers have effectively zero flake tolerance (a flaky unit test is treated as a bug in the test, fixed or deleted, never tolerated as "normal") — integration and E2E tiers have a defined, monitored, non-zero flake budget (Volume 2, Section 19.9 already anticipated this for the E2E suite specifically), consistent with those tiers' inherent exposure to real infrastructure timing variance that lower tiers, by design, don't touch.

9.7 Where this pyramid is enforced

Every tier in Section 9.2 maps directly to a specific stage in Volume 4, Section 5.2's CI/CD pipeline — this pyramid is not a separate, aspirational document from the pipeline that actually runs; it is the conceptual model the pipeline's stage ordering (fast, cheap tiers first; slow, expensive tiers only after cheaper tiers pass, per Volume 4, Section 5.2's sequential-dependency design) directly implements. A reader who understands this section's pyramid already understands why Volume 4's pipeline is shaped the way it is.



10. Performance Benchmarking
10.1 Purpose — one benchmarking practice across three volumes' worth of performance gates

Volume 1, Part VI, Section 38 specified backend performance engineering against NFR-PERF-1 through NFR-PERF-4. Volume 2, Section 17 specified frontend bundle-size budgets and Lighthouse CI gates. Volume 4, Section 19.5 specified a quarterly standalone load-testing cadence beyond the per-release gate. This section is where these three, individually-correct practices become one benchmarking discipline with consistent regression-detection and historical-tracking behavior.

10.2 Benchmark inventory — restating, not duplicating
Benchmark	Target	Source	Gate location (Volume 4 §5.2)
Step-dispatch p95 latency	<50ms (NFR-PERF-1)	Volume 1, Part VI §38.2	Stage 8, staging verification
Flow CRUD p95 latency	<200ms (NFR-PERF-2)	Volume 1, Part VI §38.3	Stage 8
Trend-dashboard load p95	<2s (NFR-PERF-3)	Volume 1, Part VI §38.4	Stage 8
AI request timeout compliance	<15s (NFR-PERF-4)	Volume 1, Part VI §38.5	Stage 8
Frontend LCP/INP/CLS	Lighthouse CI thresholds	Volume 2, §17.9	Stage 8
Frontend bundle size	Per-bundle KB budgets	Volume 2, §17.2	Stage 3, build
Concurrent execution capacity	500 (NFR-SCALE-1)	Volume 1, Part VI §38.8	Stage 8
CI build time	<10min cold, <3min warm	Volume 4, §2.9	Meta — measures the pipeline itself

Every row already has a fully-specified target and origin — this section's contribution is not new targets, it is the shared measurement and regression-tracking infrastructure underneath all of them.

10.3 Historical tracking — every benchmark run is a data point, not a pass/fail event

Every benchmark in 10.2's table, on every run (both the per-release gate, stage 8, and Volume 4, Section 19.5's quarterly standalone load test), writes its result to a persistent time-series store — the same Prometheus instance already receiving production metrics (Volume 4, Section 9), tagged distinctly (environment=ci-benchmark) so benchmark history is queryable and dashboard-visible (Volume 4, Section 10.3's Platform Health dashboard gains a benchmark-trend panel) alongside, but clearly distinguished from, actual production telemetry. This is what turns "did this release pass its performance gate" (a single-run, binary question) into "is this metric trending in a concerning direction across the last 20 releases" (a trend question) — the more valuable question, and one none of Volumes 1, 2, or 4's individually-specified gates could answer in isolation, since each only evaluated its own single run against a fixed threshold.

10.4 Regression detection — relative, not just absolute

Beyond each benchmark's absolute NFR-derived threshold (10.2), this section adds a relative regression check: a release that passes its absolute threshold but represents a statistically significant regression versus the trailing 10-release average (e.g., step-dispatch p95 creeping from 30ms to 45ms over several releases — each individually still under the 50ms absolute threshold, but a clear, real degradation trend) triggers a Warning-severity flag (Volume 4, Section 7.5's severity model) even though it wouldn't fail the absolute gate. This directly closes a gap the absolute-threshold-only approach in Volumes 1, 2, and 4 left open: a slow, creeping regression that never crosses the hard NFR threshold in any single release can still silently erode headroom release after release until a single additional small change is the one that finally crosses the line — this relative check catches the trend well before that point.

10.5 Benchmark environment consistency

Every benchmark run (per-release and quarterly) executes against the identical staging topology (Volume 4, Section 5.3) with identical resource sizing (Volume 4, Section 3.3) — a benchmark run against inconsistent hardware or scaled-differently infrastructure produces numbers that aren't meaningfully comparable across runs, silently invalidating Section 10.3 and 10.4's trend-tracking value. This is enforced by running every benchmark through the exact same staging-environment Helm values (Volume 4, Section 4.3) rather than a separately-configured "benchmark environment" that could drift from staging's actual configuration over time.

10.6 Benchmark failure response — the same severity as a functional bug, restated once more

Directly restating Volume 1, Part VI, Section 38.9's "performance regression as a first-class defect category," Volume 2, Section 17.9's equivalent frontend statement, and Volume 4, Section 19's capacity-planning discipline, unified here as this document set's single, final statement of the principle: any benchmark failure — absolute threshold breach (10.2) or relative regression flag (10.4) — is triaged with the same urgency as a functional test failure, tracked in the same incident/risk-register discipline Volume 0, Section 22 established, never deferred to a "performance backlog" reviewed at leisure. This principle has now been stated in five different places across this document set (Volumes 1, 2, 4 individually, and this section unifying them) — the repetition is deliberate, not redundant documentation: a principle this consistently restated across independently-authored sections is a genuine, load-bearing organizational value, not an incidental stylistic echo.

10.7 AI capability latency — a distinct benchmarking concern, cross-referenced not duplicated

Volume 3, Part IV, Section 15's model-routing decisions and Part V, Section 19's evaluation metrics already track AI capability latency and quality as part of that volume's own, distinct evaluation regime (Section 9.3 of this Part already explained why AI evaluation sits outside the classic pyramid). This section's benchmark inventory (10.2) includes only the platform-level NFR-PERF-4 timeout-compliance check — the deeper question of whether a specific capability's latency is good, not just within its timeout budget, remains Volume 3's evaluation-metrics domain, referenced here rather than duplicated, consistent with this Part's general practice of unifying overlapping concerns without erasing genuinely distinct ones.

10.8 Load testing cadence — restating Volume 4's schedule as this section's authoritative reference

Volume 4, Section 19.5 already specified the quarterly standalone load-test cadence in full detail (independent of any specific release, validating growth-projection assumptions against the current system). This section adopts that cadence as authoritative for the full benchmark suite (10.2), not only the concurrent-execution-capacity benchmark Volume 4, Section 19.5 originally focused on — every benchmark in this section's inventory runs at both the per-release cadence (Volume 4, Section 5.2, stage 8) and this quarterly, standalone cadence, giving Section 10.3's trend-tracking two different sampling frequencies: dense (every release) for fast-moving regression detection, and a periodic, deliberately-isolated full-system baseline (quarterly) that Section 10.5's environment-consistency requirement makes trustworthy as a genuine apples-to-apples comparison over longer time horizons.



11. Security Practices
11.1 Purpose — the master checklist behind "security designed in, not bolted on"

Architecture Principle 20.5 (Volume 1, Section 20.5) established "security and compliance posture is designed in, not bolted on" as a governing principle, and every subsequent volume has honored it in its own scope — Volume 1's authorization-first command handlers, Volume 3's guardrails and provider-security review, Volume 4's secrets architecture and network policies. This section is where that principle becomes an actual, checkable practice: threat modeling requirements, a secure-coding checklist, and the testing/disclosure cadence that verifies all of it continuously, not just at design time.

11.2 Threat modeling — required for a defined category of change

A lightweight threat-modeling exercise (STRIDE-based, chosen for its accessibility to engineers without dedicated security-specialist training) is a required part of the design review for any change that: introduces a new trust boundary (directly extending Volume 1, Part I, Section 2.5's boundary catalog — a new one requires this exercise before implementation), handles a new category of sensitive data (Volume 4, Section 14.2's secret-classification table gaining a new row), or adds a new external integration (a new AI provider, Volume 3, Part IV, Section 14.9's contract-test-plus-security-review requirement). This threat-modeling requirement is attached to the same "ADR required?" PR checkbox (Section 2.3) already governing architectural deviations — a change requiring threat modeling is, by definition, also a change requiring an ADR, since a new trust boundary or sensitive-data category is exactly the kind of pattern-deviation Volume 1, Part VI, Section 40.8 already requires documentation for.

11.3 Secure coding checklist — consolidating, not duplicating, four volumes of security rules

This checklist deliberately does not restate Section 3's code review checklist (which already consolidated most security-relevant review items in its Section 3.5) — it adds only the items that are secure-coding practice rather than review-time verification:

Input validation at every trust boundary, restated as a practice: every point where data crosses a boundary catalogued in Volume 1, Part I, Section 2.5 (target system → Nexus, third-party module → orchestrator, browser → API) validates and sanitizes input at that specific boundary, never relying on a downstream layer to catch what an upstream layer should have rejected.
Least privilege as a default, not an optimization — every new credential, API scope, or Kubernetes RBAC grant (Volume 4, Section 3.9) starts at the narrowest plausible scope and is widened only with explicit justification, never granted broadly "to be safe" or "in case it's needed later."
No security control implemented exactly once — restating Volume 1, Part IV, Section 26.3's defense-in-depth philosophy as a general coding practice: a security-relevant guarantee (tenant isolation, credential scoping, guardrail enforcement) is verified at more than one layer wherever architecturally feasible, following the pattern already established for tenant isolation (three layers), the sandbox boundary (network policy + mTLS + application scoping), and AI guardrails (input-stage + output-stage checks).
Fail closed, not open — any security-relevant check whose own execution fails (a guardrail check that errors out, per Volume 3, Section 17.2's evaluate method throwing rather than returning) is treated as a failure of the operation it was protecting, never silently bypassed to let the operation proceed anyway.
11.4 Dependency and supply-chain security — cross-reference to Section 12

Volume 4, Section 5.2's stage 4 (Security & Vulnerability Scanning) already gates every PR on dependency and image vulnerability scanning. This section's contribution is the practice this gate depends on being meaningful: Section 12 (Dependency Management) specifies the ongoing, scheduled (not just per-PR) scanning cadence and the remediation SLA that keeps the dependency tree this scanning inspects actually current — a per-PR scan is only as good as the vulnerability database and dependency versions it's checking against, both of which degrade in usefulness over time without the ongoing practice Section 12 specifies.

11.5 Penetration testing

An independent, third-party penetration test is conducted at minimum annually against the managed/hosted production environment, and additionally before any major architectural change to a trust boundary (Section 11.2's threat-modeling trigger, extended here to require external validation for the highest-risk category of change, not just internal threat-modeling review). Findings are triaged with the same severity model already established across this document set (Volume 4, Section 7.5's three-tier model, applied to security findings specifically: a critical finding blocks release until remediated, consistent with Volume 1, Architecture Principle 20.4's fail-loud philosophy applied to the most consequential category of defect this platform can have).

11.6 Vulnerability disclosure policy

A public, documented responsible-disclosure process (a security contact, a defined acknowledgment and remediation-timeline commitment) is maintained — this is a genuinely new commitment this section introduces, not previously specified anywhere in Volumes 0–4, added here because Volume 0's Business Objective 5 (enterprise trust, "true at GA, not promised as a roadmap item") implies exactly this kind of external-facing security posture, and its absence would be a real, visible gap for Persona Elena's review process (Volume 0, UC-7) to notice. External researchers reporting a genuine vulnerability through this channel receive the same severity-triage treatment (11.5) as an internally-discovered finding, with a defined SLA (e.g., critical findings acknowledged within 24 hours) — treating external disclosure as a valued, structured input to this platform's security posture rather than an unwelcome surprise to be managed defensively.

11.7 Security training and the review checklist's own maintenance

Every engineer completes security-awareness training covering this section's practices and Section 3.5's review checklist specifically, on onboarding and annually thereafter — directly serving the same "tooling-enforced, not convention-dependent" philosophy (Section 5.6) applied to the one category of standard where pure tooling enforcement genuinely cannot cover everything (recognizing a novel social-engineering attempt, judging whether a new integration's data flow deserves threat modeling, per 11.2's judgment-dependent trigger) — training is this section's explicit acknowledgment that some security practice depends on human judgment tooling can't fully substitute for, the security-specific instance of the same caveat Section 7.8 and Section 3.10 both already stated for their respective domains.

11.8 Relationship to this platform's own security-focused volumes

This section is deliberately a practice document, not an architecture document — Volume 1, Part IV (Authentication, Authorization, Multi-tenancy, Audit) and Volume 3, Part V (Guardrails, Security) and Volume 4, Part IV (Secrets, Vault, TLS) already specified the actual security architecture in full. This section specifies the ongoing human and process discipline that keeps that architecture correctly implemented and continuously verified as the system evolves — the difference between "the system was designed securely" (those three volumes' claim) and "the system remains securely implemented, checked continuously, as engineers who didn't originally design it keep changing it" (this section's claim), a distinction worth stating explicitly since it's the entire reason this section exists as a companion to, not a replacement for, those architectural volumes.



12. Dependency Management
12.1 Purpose — the ongoing practice behind Volume 4's per-PR scanning gate

Volume 4, Section 5.2's stage 4 gates every PR on dependency vulnerability scanning — a point-in-time check. This section specifies the continuous practice that makes that point-in-time check meaningful over time: version-pinning policy, scheduled (not just PR-triggered) scanning, remediation SLAs, and license-compliance review, across all three languages this document set has standardized on (Section 5).

12.2 Version pinning — exact versions, never floating ranges

Every dependency — NuGet packages (.NET), npm packages (React/TypeScript), and pip packages (Python, per Section 5.4's scoped usage) — is pinned to an exact version in the respective lock file (packages.lock.json, package-lock.json, poetry.lock or requirements.txt with hashes), never a floating range (^1.2.0, ~1.2.0) in the committed lock artifact. This directly serves Section 10.5's benchmark-environment-consistency principle extended to dependency resolution generally: a floating-range dependency means two builds at different times, from the identical source commit, could resolve to different actual dependency versions — directly undermining Volume 4, Section 2.6's SHA-based image-tagging guarantee that a given image tag corresponds to fully deterministic, reproducible build inputs, not just deterministic source code.

12.3 Dependency update cadence — scheduled, not reactive-only

Beyond Volume 4, Section 5.2's per-PR vulnerability gate (which only catches a newly introduced vulnerable dependency), a scheduled dependency-update process (weekly automated PRs via Dependabot or equivalent, proposing updates to the latest patch/minor versions of every dependency) runs continuously — this is what keeps the dependency tree itself current enough that Section 11.4's vulnerability scanning remains meaningful, since a scanning tool checking a badly-outdated dependency tree against a vulnerability database finds only vulnerabilities in versions nobody would reasonably still be running, while missing the practical reality that an outdated tree is itself a growing risk surface regardless of whether a specific CVE has been filed against the exact pinned version yet.

12.4 Update review and merge — the same pipeline, no shortcut

Automated dependency-update PRs (12.3) pass through the identical CI pipeline (Volume 4, Section 5.2) and code-review process (Section 2) as any human-authored PR — no auto-merge without human review, even for a patch-level update, because Section 5.6's "tooling-enforced, not convention-dependent" philosophy applies to catching problems, not to bypassing the review that would catch a dependency update silently breaking something the automated test suite doesn't cover. This is a deliberate, conservative choice consistent with Section 1.7's "no shortcuts even under pressure" principle already established for hotfixes, applied here to the analogous temptation to fast-track routine-seeming dependency bumps.

12.5 Remediation SLA for known vulnerabilities
Severity	Remediation SLA
Critical	48 hours from disclosure/detection
High	7 days
Medium	30 days
Low	Next scheduled dependency-update cycle (12.3)

This table directly extends Volume 4, Section 7.5's severity-tiering philosophy to dependency vulnerabilities specifically, and gives Section 11.4's cross-reference from Security Practices a concrete, numeric commitment rather than a vague "addressed promptly" — a Critical-severity dependency vulnerability is treated with the same urgency Section 11.5 established for penetration-test findings, since an unpatched, exploitable dependency is functionally equivalent to a known, unfixed vulnerability in this platform's own code from an attacker's perspective.

12.6 License compliance

Every dependency's license is scanned (as part of the same automated pipeline stage, Volume 4, Section 5.2) against an approved-license allowlist (permissive licenses — MIT, Apache 2.0, BSD variants — approved by default; copyleft licenses — GPL, AGPL — requiring explicit legal review before introduction, given Volume 0, Section 16.2's open-core licensing model's own need for clarity about what can and cannot be redistributed under Nexus's chosen terms) — a dependency introducing an unapproved license fails the same CI gate as a vulnerability finding, directly protecting Volume 0's licensing strategy (Section 16) from being quietly compromised by a transitive dependency's incompatible license terms, a risk that licensing strategy itself didn't anticipate needing to guard against at the individual-dependency level.

12.7 Dependency footprint minimization — the plugin-SDK special case

Directly extending Volume 1, Part V, Section 36.2's decision to publish Nexus.Contracts (the AutomationModule interface) as its own minimal, independently-versioned package: this package's own dependency footprint is held to a stricter standard than the rest of the codebase — zero or near-zero third-party dependencies beyond what .NET's base class library provides — since every dependency Nexus.Contracts carries is a dependency every third-party module developer (Persona Raj, Volume 0, Section 10.5) inherits merely by referencing the SDK to build a module at all. A dependency appropriate for Nexus.Infrastructure's internal use is not automatically appropriate for Nexus.Contracts, and this distinction is a specific, named exception to this section's otherwise-uniform dependency policy.

12.8 Locked technology stack aging — Volume 0's decisions, revisited on a schedule

Beyond individual dependency versions (12.2–12.6): Volume 0, Section 21's locked technology choices themselves (.NET version, React version, PostgreSQL major version) are reviewed against their own upstream support lifecycles on an annual cadence — a .NET version approaching its own end-of-support date, for instance, triggers a planned major-version migration project tracked with the same rigor as any other significant architectural initiative, rather than being discovered as an urgent, unplanned problem only once the locked version has already gone unsupported. This is the dependency-management-level expression of Volume 1, Part VI, Section 40.8's ADR discipline applied to the platform's own foundational technology choices, not just to individual libraries built on top of them — Volume 0's "locked" decisions were never intended as permanently frozen, only as stable until deliberately, visibly revisited.

12.9 Closing note for this Part

This section closes Part III: Section 9 unified this document set's testing tiers into one pyramid, Section 10 unified its performance benchmarks into one tracked practice, Section 11 consolidated its security principles into an ongoing discipline, and this section closes the loop on keeping the dependencies underlying all of it current, licensed appropriately, and free of known vulnerabilities within a defined SLA. Part IV (Release & Delivery) now turns to how verified, quality-gated change actually ships — release management, the final synthesis of every CI/CD gate this document set has specified, the Definition of Done that ties every prior standard together into one checklist, and documentation standards for maintaining this document set itself going forward.


____________________________________________________________________________________________________________________________________________________


13. Release Management
13.1 Purpose — one product version across four independently-versioned components

This document set has specified versioning for individual artifacts — container images by Git SHA (Volume 4, Section 2.6), Helm charts semantically (Volume 4, Section 4.5), the REST API/plugin contract/prompt templates via Section 6's unified policy. None of these is "the Nexus version" a customer or Persona Sofia's leadership review would reference. This section specifies that product-level version and how it correlates every component-level artifact beneath it.

13.2 Product versioning scheme

Nexus ships under a single product semantic version (MAJOR.MINOR.PATCH, e.g., 2.4.1), automatically derived from Conventional Commits (Section 4.3's type-to-version-impact table) across every merge since the last release — a feat commit anywhere in the monorepo (backend, frontend, AI capability engines, or infrastructure charts) proposes a minor bump; any BREAKING CHANGE: footer proposes a major bump; fix/perf commits propose a patch bump. This single version number is what appears in release notes, the changelog, and Volume 2's UserSettingsPage "About" panel — the version a customer actually sees and references in a support conversation.

13.3 Component correlation — one product version, several artifact identifiers
Product version:  v2.4.1
   │
   ├── Backend images (Volume 4 §2.6):    tagged by Git SHA, correlated via release manifest
   ├── Frontend bundle (Volume 2 §17):     versioned alongside backend, same SHA correlation
   ├── Helm chart (Volume 4 §4.5):         chart version bumped only if topology changed
   │                                         this release (may lag product version)
   ├── REST API version (Volume 1 §30.6):  /api/v1/ or /api/v2/ — independent, slower-moving
   │                                         semantic scheme per Section 6.2
   ├── AutomationModule contract (V1 §18.7): independent, slowest-moving per Section 6.2
   └── Prompt template versions (V3 §3.3):   independent per-capability, per Section 6.2

The product version (13.2) is a release-train identifier, not a repackaging of every component's own independent version — Section 6's unified versioning policy already established that the REST API, plugin contract, and prompt templates version independently and more slowly, by design, since their consumers (Persona Marcus's CI integrations, Persona Raj's modules) need stability the product version's more frequent cadence would violate if those contracts moved in lockstep with it. A release manifest (a machine-readable artifact generated at release time) is the authoritative mapping from one product version to the exact set of component versions/SHAs that shipped together — this manifest, not manual cross-referencing, is what answers "what exact API version and chart version did v2.4.1 ship with."

13.4 Release cadence

Product releases (13.2) ship on a regular, predictable cadence (bi-weekly, chosen to balance Section 1.5's short-branch-lifetime trunk-based philosophy — which naturally supports frequent releases, since main is always deployable per Volume 4, Section 5.3 — against the operational overhead of Section 13.6's release-note curation and Volume 4, Section 5.5's manual production-promotion-timing decision, which benefits from not happening continuously) — every merge to main is technically release-able immediately (Volume 4, Section 5.2's fully-automated gate suite), but the scheduled cadence is a deliberate business/communication rhythm distinct from technical readiness, directly extending Volume 4, Section 5.5's "production promotion is a timing decision, not a correctness gate" principle to the product-release level specifically.

13.5 Changelog generation

The changelog is generated automatically from Conventional Commits (Section 4) since the prior release, grouped by type (Section 4.3's taxonomy) and, where relevant, cross-referenced to the ADR (Section 2.3's checkbox) or ticket that motivated the change — this automated generation is reviewed and lightly edited by a release owner before publication (never fully unedited machine output reaching customers, since a fix(orchestration): correct retry backoff calculation commit message, while accurate for engineering purposes, may need a customer-facing rewrite for clarity) but the underlying data (what changed, by commit) is never hand-assembled from scratch, directly avoiding the class of error where a manually-maintained changelog silently omits a real change.

13.6 Coordinated multi-component releases — when Section 13.3's independence has limits

Most releases ship backend, frontend, and infrastructure changes together under one product version with no special coordination needed, since Volume 4, Section 5.2's pipeline builds and deploys all of them from the same main state. The exception requiring explicit coordination: a release containing a REST API major version bump (Section 6.2, requiring Volume 1, Section 30.6's dual-version-serving deprecation window) is flagged in the release manifest (13.3) with an extended validation period — Volume 4, Section 5.2's stage 8 (Staging Verification) runs an additional check specifically confirming both API versions are correctly served concurrently before this category of release is eligible for production promotion, a stricter gate than the standard release's stage 8 requires.

13.7 Hotfix releases — restating Section 1.7, now at the release-management level

Directly extending Section 1.7's "hotfixes follow the identical branch/PR/pipeline path, no shortcut" rule to release-numbering specifically: a hotfix ships as an out-of-cadence patch release (13.2's patch component incrementing outside the normal bi-weekly rhythm, 13.4), through the exact same automated pipeline and manual production-promotion decision as any scheduled release — the only difference from a scheduled release is timing, never process rigor, consistent with this document set's now-repeated insistence (Section 1.7, Section 12.4) that urgency is never a valid reason to skip a gate.

13.8 Release communication

Every release (scheduled or hotfix) is announced through the customer-facing changelog (13.5) and, for releases affecting Volume 0's named enterprise-trust-relevant surfaces (a security fix, a change to AI-optionality behavior, a change to data-residency posture) or coordinated multi-component releases (13.6), an additional, more prominent notification — directly extending Volume 4, Section 17.6's DR-event communication principle ("a DR event handled competently but silently... would undermine exactly the enterprise trust this document set has repeatedly prioritized") to routine release communication as the same underlying principle applied to a far more common, non-emergency scenario.

13.9 Post-release monitoring

Every release is followed by an elevated-attention monitoring window (Volume 4, Section 10.8's automatic deployment annotation makes this concretely actionable — the Platform Health dashboard, Volume 4 Section 10.3, is actively watched, not just passively available, for a defined period after each production promotion), with Volume 4, Section 7.4's error-budget-consumption tracking specifically reviewed against the just-shipped release's own contribution — directly closing the loop from Section 10.3's benchmark trend-tracking (did this release regress a benchmark) to real production behavior (is this release actually behaving as its benchmarks predicted once it's serving genuine customer traffic, not just staging load).



14. CI/CD Quality Gates
14.1 Purpose — the master gate manifest

Section 3 consolidated scattered code-review rules. Section 9 consolidated scattered testing tiers. This section performs the same consolidation for every automated quality gate specified across all five volumes, mapped onto the nine pipeline stages Volume 4, Section 5.2 already defined — the single manifest answering "what, exactly, does a change have to pass before it reaches a customer," with every gate's origin cited.

14.2 The complete gate manifest
Pipeline stage (Volume 4 §5.2)	Gate	Origin
1. Lint & Static Analysis	.NET Roslyn analyzers (strongly-typed IDs, dependency direction, structured logging)	Volume 1, §40.2
	ESLint import-boundary + TypeScript strict mode	Volume 2, §5.3, §4.7
	ruff/mypy for Python tooling	Volume 5, §5.5
	Commit-message format (Conventional Commits)	Volume 5, §4.7
	Helm lint, Dockerfile lint	Volume 4, §4.6, §2
2. Unit & Component Tests	Domain/Application tests (zero infra dependency)	Volume 1, §40.6
	Component + hook tests	Volume 2, §19.3–19.4
	Visual regression (Storybook baselines)	Volume 2, §9.7, §19.4
	AI prompt offline evaluation sets	Volume 3, §3.6, §19.3
3. Build & Image Creation	Bundle-size budget	Volume 2, §17.2
	Docker multi-stage build, layer-cache verification	Volume 4, §2.2, §2.9
4. Security & Vulnerability Scanning	Image CVE scanning	Volume 4, §2.5
	Dependency vulnerability + license scanning	Volume 5, §12.1, §12.6
	AI guardrail adversarial suite	Volume 3, §17.9
	Plugin-sandbox adversarial suite	Volume 2, §19.11
5. Integration Tests	Infrastructure/API integration tests (real test-container DB/Redis)	Volume 1, §40.6
	Frontend integration tier (MSW)	Volume 2, §19.6
	RLS policy performance verification	Volume 4, §13.4
6. End-to-End Tests	Full Nexus-testing-itself E2E suite	Volume 2, §19.8
	Accessibility (axe-core) integration checks	Volume 2, §18.9, §19.7
7. Deploy to Staging	Migration Job gate (schema before app rollout)	Volume 1, §37.5
	Helm chart deployment	Volume 4, §4.7
8. Staging Verification	E2E smoke subset	Volume 2, §19.9
	Load test against NFR-SCALE-1	Volume 1, §38.8
	Lighthouse CI performance gate	Volume 2, §17.9
	AI evaluation-set regression check	Volume 3, §19.3
	Performance benchmark absolute + relative regression check	Volume 5, §10.2, §10.4
	Dual-API-version serving check (major-version releases only)	Volume 5, §13.6
9. Promote to Production	Manual approval (timing decision only)	Volume 4, §5.5
14.3 Reading this table — every gate traces to exactly one origin

Every row in 14.2 cites the single section where that gate was originally, substantively specified — this table is deliberately a map, not a re-specification; a reader wanting the full reasoning behind any individual gate follows the citation back to its origin volume rather than expecting this section to re-derive it. This is the same "define once, reference everywhere" discipline this document set has applied since Volume 2 (Sections 9.4, 10.6, 12.5 there), now applied at this document set's largest scale — one table spanning all five volumes.

14.4 Gate additions — the process for extending this manifest

A new gate is added to this manifest through the same process as any other architectural pattern in this document set: specified substantively in the volume/section where it naturally belongs (a new AI capability's evaluation gate belongs in Volume 3; a new infrastructure check belongs in Volume 4), then added to this table in the same PR (directly extending Section 3.9's "checklist maintained in the same PR that establishes the pattern" discipline to this manifest specifically) — this table is never edited to add a gate whose substantive specification lives nowhere else, keeping the "map, not specification" property in 14.3 true by construction rather than by discipline alone.

14.5 Gate removal — a higher bar than addition

Removing a gate from this manifest requires an ADR (Section 2.3's checkbox) explicitly justifying why the risk that gate was originally added to catch is no longer present or is now covered by a different, still-active gate — consistent with Volume 1, Part VI, Section 40.8's general deviation-requires-ADR discipline, but called out here specifically because gate removal is a category of change this document set's authors consider meaningfully higher-risk than gate addition: an unnecessary gate costs pipeline time (a bounded, measurable cost, Volume 4 Section 2.9's build-budget discipline already tracks this); a wrongly-removed gate risks reintroducing a defect class this entire document set spent considerable effort preventing in the first place.

14.6 Gate performance — the pipeline's own budget, restated

Directly restating Volume 4, Section 5.7's "the pipeline monitors itself" and Section 6.8's PR/nightly cost split: this manifest's full gate suite (14.2) does not all run on every single commit-level push — Volume 4, Section 6.8 already specified that every-PR runs a representative, budget-conscious subset (targeting under 20 minutes total feedback latency) while the complete manifest, including every gate in 14.2 at full fidelity, runs nightly and before any tagged release (Section 13.4's cadence). This section's manifest is the complete, authoritative list of what exists; Volume 4, Section 6.8 remains authoritative for when each gate actually runs at what fidelity.

14.7 This manifest as the literal Definition of Done's foundation

Section 15 (Definition of Done) is built directly on this manifest — every gate in 14.2 that applies to a given category of change (a backend-only fix doesn't trigger Volume 2's frontend-specific gates; a documentation-only change doesn't trigger performance benchmarking) becomes a checklist item there. This section's manifest is the exhaustive, mechanically-enforced substrate; Section 15 is the human-facing checklist derived from it, scoped per change type.



15. Definition of Done
15.1 Purpose — the human-facing checklist derived from Section 14's manifest

Section 14 specified the mechanically-enforced gate manifest. This section is its human-facing counterpart — the checklist an engineer and reviewer use to judge "is this actually done," scoped by change type, since not every gate in Section 14.2 applies to every change. This is the final synthesis this entire document set has been building toward: the single checklist that, if genuinely satisfied, means a change embodies every standard Volumes 0 through 5 collectively established.

15.2 Universal criteria — every change, regardless of type
 Passes every gate in Section 14.2's manifest applicable to the files changed (mechanically verified by CI, not self-assessed)
 PR description complete per Section 2.3's template, including an honest "ADR required?" answer
 Code review checklist (Section 3) satisfied, with review from the owning team per Section 1.9's routing
 Commit messages follow Section 4's conventions
 No new console.log/string-interpolated logging introduced (Section 8.2–8.3)
 No new hardcoded secret, credential, or design-token value (Volume 4 §4.8; Volume 2 §2.9)
15.3 Additional criteria — backend changes (Volume 1 scope)
 New commands inherit AuditableCommandHandler or carry justified exemption (Volume 1, §27.2)
 New aggregates/entities follow strongly-typed-identifier and composition-vs-association discipline (Volume 1, §10.8, §35.4)
 New queries against scaling-sensitive tables include EXPLAIN ANALYZE output (Volume 1, §38.6)
 Domain/Application unit test coverage added for new business logic (Volume 5, §9.5)
15.4 Additional criteria — frontend changes (Volume 2 scope)
 Import boundaries respected (Volume 2, §5.3)
 New interactive elements meet WCAG 2.1 AA — keyboard operability and ARIA verified, not assumed (Volume 2, §18.4, §18.9)
 Bundle-size budget check passes or an explicit, reviewed exception is documented (Volume 2, §17.2)
 Module-parity test (Volume 2, §19.5) still passes if the change touches the schema-driven editor or canvas
15.5 Additional criteria — AI capability changes (Volume 3 scope)
 Evaluation-set update included and offline gate passes (Volume 3, §3.6, §19.3)
 Every AiPlanningResult field remains populated on every code path (Volume 3, §2.3)
 Guardrail changes are additive-only, never weakening a baseline check (Volume 3, §17.5–17.6)
 New capability output types remain structured (no new free-text field) (Volume 3, §3.4)
15.6 Additional criteria — infrastructure changes (Volume 4 scope)
 Helm chart change passes lint, template-diff review, and ephemeral-cluster deployment test (Volume 4, §4.6)
 NetworkPolicy or Kubernetes RBAC changes receive elevated review (Volume 4, §3.5, §4.9)
 Cost-impact estimate included for changes with material infrastructure-cost implications (Volume 4, §21.8)
 Backup/restore or DR runbook updated if the change affects a component Section 17/20 (Volume 4) specifies procedures for
15.7 Additional criteria — architectural deviations
 ADR drafted and approved by the architecture owner before merge, not after (Volume 1, §40.8)
 ADR cites the specific principle(s) it deviates from, by number (Volume 0, §20.9's citation discipline)
 Threat-modeling exercise completed if the change introduces a new trust boundary or sensitive-data category (Volume 5, §11.2)
15.8 What "Done" explicitly does not mean

Consistent with Section 3.10's and Section 7.8's equivalent caveats: satisfying this checklist means a change meets this document set's collective, established standards — it does not mean the change is necessarily the right change, that its underlying design is optimal, or that no further iteration will ever be needed. "Done" in this section's sense is a quality-and-consistency bar, not a claim of perfection or finality — a satisfied Definition of Done is the condition for merging with confidence that the change won't silently violate this project's accumulated architectural and process discipline, which is a meaningful, achievable, and honest claim, deliberately narrower than an unachievable claim of overall correctness or optimality.

15.9 Using this checklist in practice

This checklist is not read top-to-bottom as a manual gate on every PR — Section 15.2 applies universally; Sections 15.3–15.7 apply only to the categories of change the PR actually touches, determined directly from the same "which volume(s) / bounded context(s) does this touch" field Section 2.3's PR template already requires. A PR touching only Volume 4 infrastructure never needs to satisfy Section 15.5's AI-specific criteria — this checklist is scoped automatically by what the PR description already declares, not applied as one undifferentiated wall of items every change must wade through regardless of relevance.

15.10 This checklist's own maintenance

Directly extending Section 3.9's and Section 14.4's maintenance discipline to this final synthesis: any new standard established anywhere in a future amendment to Volumes 0–5 is added to the relevant subsection here (15.2–15.7) in the same change that establishes it — this is the last, and highest-leverage, instance of this document set's "documentation as code, updated where it's created" principle, since this checklist is the one artifact every single future change will be measured against.



16. Documentation Standards
16.1 Purpose — the standard for code-adjacent documentation, and for this document set itself

Every prior volume practiced "documentation as code" in its own scope — Volume 1, Part VI, Section 40.7's ActionSchema description requirement; Volume 4, Section 7.7's versioned runbooks and Section 10.2's dashboard-as-code. This section unifies that practice into one standard, and then does something no prior section has done: specifies how this six-volume document set itself — the specification these sixteen sections of Volume 5 close out — is maintained, reviewed, and kept honest going forward, directly fulfilling the governance mechanism Volume 0's RISK-13 flagged as required from the very first volume.

16.2 Code-adjacent documentation — the unified standard

Every artifact this document set has established a documentation requirement for follows one rule: documentation lives in version control, in the same repository, updated in the same PR as the code/configuration it describes — never in a separate wiki, a separate document store, or a separate review process. This applies uniformly to: ActionSchema/ParamSpec descriptions (Volume 1, §40.7), Grafana dashboards (Volume 4, §10.2), incident runbooks (Volume 4, §7.7), the code review checklist (Volume 5, §3), the CI/CD gate manifest (Volume 5, §14), and the Definition of Done (Volume 5, §15) itself.

16.3 ADRs — the format, consolidated

Architecture Decision Records have been required by name since Volume 0, Section 20.9, and invoked by nearly every subsequent volume (Volume 1, §40.8; Volume 4, §14.5; Volume 5, §11.2, §14.5, §15.7) without this document set ever specifying their actual format until now:

markdown
# ADR-NNNN: [Short, descriptive title]

## Status
[Proposed | Accepted | Superseded by ADR-XXXX]

## Context
[What situation motivates this decision — link the specific requirement,
 risk, or limitation that makes a decision necessary]

## Decision
[What was decided, stated plainly]

## Principle(s) deviated from or extended
[Cite by number — e.g., "Architecture Principle 20.3" or "UX Principle 1.4" —
 per Volume 0 §20.9's citation discipline]

## Consequences
[What becomes easier, what becomes harder, what risk this introduces or closes]

## Alternatives considered
[What else was evaluated, and why it wasn't chosen — mirroring the
 alternatives-considered format Volume 0 §21's technology decisions used]

Every ADR is stored in version control (16.2's rule applied to ADRs specifically), numbered sequentially, and linked from the PR that implements it (Section 2.3's checkbox) — an ADR is never retroactively written after the implementing PR has already merged, per Volume 1, Section 40.8's explicit "approved before merge, not merged first and documented later" requirement, restated here as the final, canonical statement of that rule.

16.4 Documentation review — the same rigor as code

A documentation-only PR (Section 4.3's docs commit type) passes through the identical review process (Section 2) as a code change — no separate, lighter-weight review path for documentation, because this document set's own internal consistency (a section citing another section correctly, a table accurately reflecting the architecture it describes) is exactly the kind of property that degrades under casual, unreviewed editing, the same reasoning Volume 1, Section 40.7 already applied to ActionSchema documentation specifically, now generalized to all documentation this platform maintains.

16.5 Maintaining this six-volume document set specifically

This is the section Volume 0's RISK-13 ("the scope of this documentation program itself... outpaces the team's capacity to keep it consistent and current") was written to eventually be answered by. The answer, concretely:

Ownership: each volume has a named owning role (a Volume 1 owner for backend architecture, a Volume 2 owner for frontend, and so on, directly mirroring Volume 4, Section 7.6's per-SLI ownership model applied to documentation authority) — a change to any volume requires that volume's owner's review, in addition to Section 2's standard PR review.
Amendment process: a correction or extension to any volume follows exactly the ADR process (16.3) if it changes an architectural decision, or a standard documentation PR (16.4) if it clarifies or corrects without changing the underlying decision — the distinction between these two cases is itself judged using this document set's own established vocabulary (does this change a "decision" or only its "documentation," a distinction Volume 1, Section 40.8 already implicitly relied on).
Cross-volume consistency checks: because this document set's central discipline is that every section traces to an upstream commitment (Volume 0's business objectives → Volume 1–4's architecture → Volume 5's process), a scheduled quarterly review specifically checks for drift — a place where a later volume's citation of an earlier volume's decision no longer accurately reflects that decision, because the earlier volume was amended without the citing section being updated. This is the concrete, scheduled practice preventing exactly the failure mode this project encountered once, live, during its own writing — the Volume 1 Unit-of-Work correction that Volume 2 was already citing before the fix, requiring Volume 2's citing sections to be revisited and confirmed still accurate once Volume 1 changed underneath them.
Version correlation with the product: this document set's own version (a straightforward semantic version, incrementing per Section 13.2's same scheme) is referenced in the product's release manifest (Section 13.3) — "what did this document set say the architecture was, as of the release that shipped" is always answerable precisely, the documentation equivalent of Section 13.3's component-correlation discipline applied reflexively to the documentation itself.
16.6 The traceability chain, restated one final time

Every section across all six volumes has, at some point, cited an upstream commitment — a business objective (Volume 0, Part I), a requirement (Volume 0, Part II), an architecture principle (Volume 1, Section 20), a UX principle (Volume 2, Section 1), an AI boundary (Volume 3, Section 1.4), an infrastructure decision (Volume 4), or a process standard (this volume). This chain is this document set's single most consistently-applied discipline, present throughout its 138 sections, and its continued integrity — verifiable by following any section's citations backward to Volume 0's original business case, and forward to this volume's process enforcement — is the actual, concrete measure of whether this documentation program succeeded at the goal Volume 0 set for it at the very beginning: not simply producing a large volume of text, but producing a genuinely internally-consistent, traceable specification where a reader can always ask "why does this exist" and receive a real answer, not an assertion.

16.7 Closing note — the six-volume project complete

This section closes Volume 5, and with it, the complete document set: Volume 0 (Product Blueprint) established the vision, market case, platform shape, and architectural foundation. Volume 1 (Backend Architecture) specified the domain model, engineering patterns, cross-cutting services, API, and data layer in full implementation detail — including a real, load-bearing correction made mid-project when a genuine crash-durability gap was found and fixed. Volume 2 (Frontend Architecture) specified the UX principles, application architecture, core product surfaces, platform integration, and quality discipline of the client experience, with an honest, deliberately-scoped account of what it does and does not attempt. Volume 3 (AI Architecture) specified the planning engine, knowledge infrastructure, seven capability engines, provider abstraction, and — per Volume 0's explicit mandate — the guardrails, security, and evaluation discipline that make Nexus's AI layer trustworthy rather than merely capable. Volume 4 (Infrastructure Architecture) specified how all of the above is built, deployed, observed, secured, and kept running reliably and affordably, closing every forward reference the earlier volumes had deliberately deferred to it. Volume 5 (this volume) specified the human and process discipline — source control, coding standards, testing, security practice, and release management — that governs how this entire system is actually built and maintained day to day, closing with this section's account of how the document set itself stays honest over time.

Six volumes, and — with this section — the complete engineering bible this project set out to build, incrementally, exactly as proposed at its outset: not all at once, but volume by volume, each one complete before the next began, each one tracing back to the vision the very first section established and forward to the process discipline this final section now closes the loop on.


