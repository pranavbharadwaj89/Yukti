# Volume 3 — AI Architecture · Part I — AI Foundations




## Table of Contents

  - [Section 1 — AI Vision](#1-ai-vision)
  - [Section 2 — AI Planning Engine](#2-ai-planning-engine)
  - [Section 3 — Prompt Library](#3-prompt-library)
  - [Section 4 — Retrieval-Augmented Generation (RAG)](#4-retrieval-augmented-generation-rag)
  - [Section 1 — Query construction — capability-specific (Section 2.4 step 2 is invoked with the](#1-query-construction-capability-specific-section-2-4-step-2-is-invoked-with-the)
  - [Section 2 — Embedding — the query is embedded using the same model/dimension as the](#2-embedding-the-query-is-embedded-using-the-same-model-dimension-as-the)
  - [Section 3 — Tenant-scoped similarity search — VectorStore.query(tenantId, ...), topK](#3-tenant-scoped-similarity-search-vectorstore-query-tenantid-topk)
  - [Section 4 — Re-ranking (optional, capability-dependent) — for capabilities where recall](#4-re-ranking-optional-capability-dependent-for-capabilities-where-recall)
  - [Section 5 — Context assembly — trimmed and formatted to fit the target PromptTemplate's](#5-context-assembly-trimmed-and-formatted-to-fit-the-target-prompttemplate-s)
  - [Section 5 — Memory](#5-memory)
  - [Section 6 — Knowledge Base](#6-knowledge-base)
  - [Section 7 — Requirement Parser](#7-requirement-parser)
  - [Section 8 — Test Generator](#8-test-generator)
  - [Section 9 — Locator Generator](#9-locator-generator)
  - [Section 10 — Self-Healing](#10-self-healing)
  - [Section 11 — Failure Analysis](#11-failure-analysis)
  - [Section 12 — Root Cause Analysis](#12-root-cause-analysis)
  - [Section 13 — Report Summarization](#13-report-summarization)
  - [Section 14 — AI Provider Abstraction](#14-ai-provider-abstraction)
  - [Section 15 — Multi-model Routing](#15-multi-model-routing)
  - [Section 16 — Cost Optimization](#16-cost-optimization)
  - [Section 17 — Guardrails](#17-guardrails)
  - [Section 18 — Security](#18-security)
  - [Section 19 — Evaluation Metrics](#19-evaluation-metrics)

---

1. AI Vision
1.1 Purpose and relationship to Volume 0's AI Strategy

Volume 0, Part III, Section 18 established Nexus's AI product strategy: the three-capability value framework (authoring acceleration, maintenance reduction, diagnostic acceleration), three permanent product boundaries (never autonomous, never required, never data-pooling), and provider-abstraction/guardrail commitments. This volume takes that strategy and specifies the actual system that implements it — every architectural decision here traces back to a specific clause of Volume 0, Section 18, restated at the top of the relevant section throughout this volume, exactly as Volume 1 and Volume 2 both traced back to their respective upstream commitments.

1.2 What "AI Architecture" means as a distinct volume, precisely

A clarification worth stating up front, since it determines this volume's entire scope: this is not a volume about using a large language model as a feature bolted onto Nexus — it is the architecture of a capability layer that sits across the entire platform, consuming the domain model Volume 1 specified (a Flow, a StepResult, a FlowRun) and producing outputs that flow back into that same domain model through the exact same commands Volume 1 already catalogued (GenerateFlowFromDescriptionCommand, RequestSelectorHealCommand, Volume 1 Part II Section 13). This volume does not introduce new domain concepts that bypass Volume 1's model — it specifies how the AI capability engines (Part III of this volume) produce values that are valid instances of types Volume 1 already defined, and it specifies the infrastructure (Parts II and IV) those engines run on.

1.3 The three-capability framework, restated as an architectural mandate

Volume 0, Section 18.3 defined three value mechanisms — authoring acceleration, maintenance reduction, diagnostic acceleration — and required every AI capability to map to exactly one. This volume's Part III (Capability Engines) is organized by this exact mapping, not by technology (e.g., not "the RAG-based features" grouped separately from "the fine-tuned-model features") — because the product value the capability delivers, not its implementation technique, is what this document set has consistently used to organize itself (Volume 1's bounded contexts mirrored Volume 0's problem decomposition; Volume 2's feature modules mirrored Volume 1's bounded contexts; this volume's capability engines mirror Volume 0's value framework).

Value mechanism (Volume 0 §18.3)  Capability engines (this volume, Part III)
Authoring acceleration  Requirement Parser (§7), Test Generator (§8)
Maintenance reduction Locator Generator (§9), Self-Healing (§10)
Diagnostic acceleration Failure Analysis (§11), Root Cause Analysis (§12), Report Summarization (§13)
1.4 The permanent boundaries, restated as architectural invariants

Volume 0, Section 18.4's three permanent boundaries become, in this volume, concrete architectural properties the system is built to structurally enforce, not merely policies engineers are asked to follow:

"AI never executes or persists a change autonomously" — architecturally, this means every capability engine in Part III has exactly one output shape: a proposal, never a direct mutation. A capability engine's method signature never accepts a repository or Unit of Work (Volume 1, Part III, Sections 15–16); it returns a value that a Volume 1 command handler subsequently, separately decides whether to persist. This is enforced by this volume's own dependency-direction rule (fully specified in Part IV once the provider-abstraction layer is defined), directly analogous to Volume 1's Nexus.Domain having zero outward dependencies (Volume 1, Part V, Section 36.3).
"AI is never a required dependency for core platform functionality" — architecturally, every capability engine implements a common interface (Section 2's AI Planning Engine formalizes this) whose absence is a first-class, tested code path throughout this entire volume, not just at the Volume 1 orchestration boundary where NFR-REL-3 was originally enforced.
"AI does not become a data-pooling mechanism across customers" — architecturally, this constrains Part II (Knowledge & Context) specifically: every Memory and Knowledge Base construct (Sections 5–6) is tenant-scoped by construction, using the exact same TenantId shared-kernel discipline Volume 1, Part II, Section 7.4 established, never a cross-tenant corpus.
1.5 The AI Vision statement for this volume

Nexus's AI layer exists to make the deterministic platform Volume 1 specifies easier to author, cheaper to maintain, and faster to diagnose — never to become a second, parallel system a user must trust independently of the platform's own guarantees.

Every capability engine in Part III should be evaluated against this statement specifically: does it make Volume 1's platform easier to author/maintain/diagnose, or does it ask a user to trust something new and separate? A proposed AI capability that fails this test — because it would require a user to trust the AI's judgment in place of, rather than in addition to, Volume 1's own deterministic verification — is out of scope for this volume, consistent with Volume 0, Section 18.4's boundary and this document set's repeated practice of naming out-of-scope territory explicitly (Volume 0, Section 12.6; Volume 1, Part I, Section 2.4's system boundary statement).

1.6 Why this volume comes after Volumes 1 and 2, architecturally

This ordering is not incidental to the six-volume plan — it reflects a real dependency direction worth stating explicitly: every capability engine in this volume produces an output that must be a valid instance of a Volume 1 domain type (a generated Flow must satisfy Flow.Publish's invariants, Volume 1 Part II Section 9.2; a healed selector must produce a valid AiAttribution, Volume 1 Part II Section 11.5) and every capability's UI surface was already scoped by Volume 2 (Section 1.7 there; Section 6.2's AiFlowGenerationPanel; Section 13's Plugin UI sandbox). This volume could not have been written first without either contradicting or duplicating decisions Volumes 1 and 2 already made — a concrete, worked illustration of why this whole project's volume ordering (Product Blueprint → Backend → Frontend → AI → Infrastructure → Engineering Playbook) was chosen the way it was.

1.7 What "depth" means for this volume specifically

Given this volume's explicit mandate ("really innovate," per the original project scope) alongside its equally explicit permanent boundaries (1.4), this volume's depth is expressed through rigor about constraint enforcement and evaluation, not through relaxing the boundaries Volume 0 already committed to. The most technically ambitious sections in this volume (Part II's RAG/Memory architecture, Part III's capability engines) are exactly as ambitious as ordinary AI-product engineering typically is — what makes this volume's treatment distinct is the density of guardrail, evaluation, and cost-governance specification surrounding that ambition (Parts IV and V), reflecting Volume 0, Section 18.6's explicit competitive-differentiation thesis: unified, architected guardrails across every AI touchpoint, not raw model capability, is Nexus's actual moat.

1.8 Section roadmap for the rest of this Part

Section 2 (AI Planning Engine) specifies the routing/orchestration layer that decides which capability engine and which prompt template handles a given AI-assisted request. Section 3 (Prompt Library) specifies how prompts themselves are authored, versioned, and tested as a managed artifact — the direct foundation every capability engine in Part III builds on.



2. AI Planning Engine
2.1 Purpose — the router above the capability engines

Part III of this volume specifies seven distinct capability engines (Requirement Parser, Test Generator, Locator Generator, Self-Healing, Failure Analysis, Root Cause Analysis, Report Summarization). This section specifies the layer that sits above all of them: the component that receives an AI-assisted request from Volume 1 (a GenerateFlowFromDescriptionCommand, a RequestSelectorHealCommand) and determines which capability engine, which prompt template (Section 3), and — per Part IV — which model/provider should handle it, then assembles the final structured output back into the shape Volume 1's command handler expects.

2.2 Why a distinct planning layer, rather than each capability engine standing alone

A tempting simpler design is letting each Volume 1 AI-related command call directly into its own, independent capability-engine implementation with no shared routing layer. This volume rejects that design for a reason directly traceable to Volume 0, Section 18.6's guardrail-centralization thesis: guardrails, cost governance (Part IV), and evaluation instrumentation (Part V) need to apply uniformly across every AI touchpoint, and a shared planning layer is what makes "uniform" an architectural property rather than a discipline seven independently-implemented engines would each need to separately, correctly re-implement — directly mirroring Volume 1's own reasoning for why the orchestrator core has zero module-specific branching (Volume 1, Architecture Principle 20.3) applied here to AI capabilities specifically.

2.3 The Planning Engine's interface
typescript
interface AiPlanningRequest {
  capability: 'flow-generation' | 'selector-healing' | 'log-triage' | 'requirement-parsing'
            | 'test-generation' | 'failure-analysis' | 'root-cause-analysis' | 'report-summarization';
  tenantId: TenantId;
  triggeringUserId: UserId;          // for the permission-inheritance rule, Volume 1 Part IV §25.6
  input: CapabilitySpecificInput;     // typed per capability, Part III
  context?: RetrievalContext;         // populated by Part II's RAG layer, when applicable
}

interface AiPlanningResult<TOutput> {
  output: TOutput;                    // a valid Volume 1 domain-adjacent value —
                                          // never persisted here, only returned
  confidence: number;
  modelUsed: string;                  // Part IV §15's routing decision, recorded
  guardrailChecks: GuardrailCheckResult[];  // Part V §17, always populated, never skipped
  costRecord: AiCostRecord;           // Part IV §16
}

interface AiPlanningEngine {
  plan<TOutput>(request: AiPlanningRequest): Promise<AiPlanningResult<TOutput>>;
}

Every field on AiPlanningResult is populated on every call, without exception — guardrailChecks and costRecord are never optional or best-effort, because Part V's evaluation metrics and Part IV's cost governance both depend on every single AI-assisted operation producing this data uniformly, not only the operations a particular engineer remembered to instrument.

2.4 The planning pipeline
AiPlanningRequest
      │
      ▼
┌─────────────────┐
│ 1. Guardrail        │  Part V §17 — input-side checks (PII detection, prompt-injection
│    pre-check         │  resistance screening, tenant policy enforcement) BEFORE any
└────────┬─────────┘  model call is made — rejecting a request here costs nothing and
         │              risks nothing, the cheapest possible point to enforce a boundary
         ▼
┌─────────────────┐
│ 2. Context           │  Part II §4-6 — RAG retrieval and Memory lookup, populating
│    retrieval          │  RetrievalContext, scoped strictly to the requesting tenant
└────────┬─────────┘  (Volume 0 §18.4's data-pooling boundary, enforced here concretely)
         │
         ▼
┌─────────────────┐
│ 3. Prompt            │  Section 3 — selects and renders the versioned prompt template
│    assembly           │  for this capability, injecting the retrieved context
└────────┬─────────┘
         │
         ▼
┌─────────────────┐
│ 4. Model routing     │  Part IV §15 — selects provider/model based on capability,
│                       │  cost policy, and tenant configuration
└────────┬─────────┘
         │
         ▼
┌─────────────────┐
│ 5. Invocation        │  Part IV §14's provider-abstracted call, with the
│                       │  NFR-PERF-4 15-second timeout (Volume 0/Volume 1) enforced
└────────┬─────────┘  at exactly this boundary
         │
         ▼
┌─────────────────┐
│ 6. Output            │  Part V §17 — structural validation (does the response parse
│    validation          │  as the expected TOutput shape) AND output-side guardrail
└────────┬─────────┘  checks (does it violate a content/safety policy)
         │
         ▼
┌─────────────────┐
│ 7. Evaluation         │  Part V §19 — records the interaction for ongoing quality
│    instrumentation     │  metrics (self-heal acceptance rate, flow-generation
└────────┬─────────┘  structural validity rate — Volume 0 §18.7)
         │
         ▼
AiPlanningResult<TOutput>

Every one of these seven steps is mandatory and non-bypassable for every capability — a specific capability engine (Part III) customizes steps 2, 3, and the shape of TOutput, but never skips steps 1, 6, or 7, which is exactly the "uniform across every AI touchpoint" property Section 2.2 argued this layer exists to guarantee.

2.5 Failure and timeout handling — the AI-optionality boundary, implemented here

Directly implementing Volume 1, Part VI, Section 38.5's AiProviderClient timeout design and Volume 0's NFR-REL-3: if step 5 (Invocation) times out or the provider is unreachable, the Planning Engine returns a typed AiUnavailableError rather than throwing an unhandled exception — every Volume 1 command handler consuming this engine (Section 2.6) is required to catch this specific error type and fall back to normal, non-AI failure handling, never letting an AI outage cascade into a platform-wide failure. This is the concrete architectural seam where Volume 0's "AI proposes, deterministic code disposes, gracefully absent when unavailable" philosophy becomes a specific, typed contract rather than a general intention.

2.6 Consumers — mapping back to Volume 1's command catalog
Volume 1 command  Planning Engine capability  Part III section
GenerateFlowFromDescriptionCommand  flow-generation Requires composition of Requirement Parser (§7) + Test Generator (§8)
RequestSelectorHealCommand  selector-healing  Locator Generator (§9) + Self-Healing (§10)
(Logs module AI-triage capability, Volume 0 §14.9)  log-triage  Failure Analysis (§11), Root Cause Analysis (§12)
(Reporting dashboard's future NL-query capability, Volume 0 Future Vision §23.3)  report-summarization  Report Summarization (§13)

This table is the direct, concrete bridge between Volume 1's already-fixed command catalog and this volume's capability engines — every row is a contract this volume must satisfy exactly, not a suggestion.

2.7 Why "planning," not just "routing"

The name reflects a deliberate scope decision worth explaining: for the simpler capabilities (log-triage, selector-healing), step 3 (Prompt assembly) is genuinely a fixed, single-template selection — "routing" would be an equally accurate name. For flow-generation specifically, the Planning Engine performs genuine multi-step planning (per Section 2.6's table: composing Requirement Parser and Test Generator outputs, potentially iterating if the Test Generator's output fails structural validation at step 6, per Volume 1's FlowGenerationResult design already anticipated in Volume 1, Part II, Section 13.2) — the "Planning Engine" name is chosen to accurately describe its most sophisticated responsibility, not its simplest one, avoiding the understatement "router" would imply for that capability specifically.



## 3. Prompt Library

### 3.1 Purpose — prompts as a managed, versioned artifact

Directly extending the Planning Engine's step 3 (Section 2.4): this section specifies how prompts are authored, stored, versioned, and evaluated as first-class engineering artifacts — never as inline strings scattered through capability-engine code. This is a direct, deliberate application of the same discipline Volume 1, Part VI, Section 40.7 established for `ActionSchema` documentation ("undocumented actions are a direct, user-visible product defect") applied here: an unversioned, untested prompt is treated as a defect in this volume's engineering practice, not an acceptable shortcut.

### 3.2 Prompt template structure

```typescript
interface PromptTemplate {
  id: string;                        // e.g., "flow-generation.v3"
  capability: AiPlanningRequest['capability'];   // Section 2.3
  version: string;                    // semantic version, mirroring Volume 1 Part III
                                          // §18.7's AutomationModule contract versioning
  systemPrompt: string;                // the instruction/role template
  outputSchema: JSONSchema;            // enforced structurally, Section 3.5
  requiredContextFields: string[];      // which RetrievalContext fields this template expects
  changelog: PromptChangelogEntry[];
}
```

### 3.3 Versioning discipline

Every prompt template change is a new version, never an in-place edit to an existing one — directly mirroring Volume 1, Part III, Section 18.7's `AutomationModule` contract-versioning discipline, applied here because a prompt change can silently alter a capability's output distribution in ways that are much harder to detect than a code change's compiler-checked type signature. A `PromptTemplate`'s version is recorded on every `AiPlanningResult` (Section 2.3's `modelUsed` field is joined with the prompt version for full reproducibility) — this is what makes Part V's evaluation metrics (self-heal acceptance rate, structural validity rate) meaningful over time: a metric regression can be correlated to a specific prompt version change, not just a vague "quality seems worse lately."

### 3.4 Prompt authoring principles

- **Capability-scoped, never general-purpose.** Each `PromptTemplate` serves exactly one capability (Section 2.3's closed capability enum) — there is no shared "general Nexus assistant" system prompt that multiple capabilities repurpose with different framing, since that pattern makes prompt-injection resistance (Part V, Section 18) and output-schema enforcement (3.5) both structurally harder to reason about per capability.
- **Structured output required, always.** Every `PromptTemplate.outputSchema` mandates a specific, machine-parseable structure (JSON, matching the target `TOutput` type from Section 2.3) — no capability in this volume relies on free-text output that a downstream regex or heuristic tries to interpret, directly serving the "make illegal states unrepresentable" discipline Volume 1, Part II, Section 11.6 established for domain value objects, now extended to AI output specifically.
- **Context injection points are explicit and typed**, per `requiredContextFields` (3.2) — a prompt template declares exactly what retrieved context (Part II) it expects, and the Planning Engine (Section 2.4, step 3) fails fast with a clear error if that context wasn't successfully retrieved, rather than silently rendering a prompt with missing sections and hoping the model compensates.

### 3.5 Output schema enforcement — where it happens and why it's not optional

Referencing Section 2.4's step 6: every model response is parsed against `outputSchema` before being treated as a valid `TOutput`. A response that fails to parse is **not** silently retried with a more lenient parser or passed through with best-effort coercion — it is treated as a capability failure, surfaced through the exact same `AiUnavailableError`-adjacent typed-failure path Section 2.5 established for provider timeouts, letting the calling Volume 1 command handler fall back to normal failure handling. This directly mirrors the exact behavior Volume 1's original `AiModule` prototype already established for `generateFlow`/`triageLogs`/`healSelector` ("Model did not return valid JSON" → typed failure, never a silent guess) — this volume formalizes that prototype behavior as a mandatory, uniform rule enforced at the Planning Engine layer for every capability, not an ad hoc per-capability choice.

### 3.6 Prompt testing — the evaluation-set discipline

Every `PromptTemplate` version ships with an associated **evaluation set**: a curated collection of representative inputs and expected-acceptable-output characteristics (not necessarily exact-match expected outputs, given LLM output's inherent variability, but structural and semantic acceptance criteria — does a generated flow reference only valid modules, does a proposed selector fix stay within a reasonable edit distance of the original) that must pass before a new prompt version can be promoted from a staging configuration to production traffic. This is the direct mechanism that makes Part V, Section 19's evaluation metrics a *release gate*, not just a *dashboard* — mirroring Volume 1, Part VI, Section 38.8's load-testing release-gate philosophy, applied here to prompt-quality regression specifically.

### 3.7 Prompt library storage and deployment

Prompt templates are stored as versioned configuration (not compiled into application code), deployed independently of the Planning Engine's own code releases — this decouples "ship a prompt improvement" from "ship a code deployment," a meaningfully faster iteration loop for prompt engineering specifically, while still going through the same evaluation-set gate (3.6) before any production traffic sees a new version. A/B or canary rollout of a new prompt version (routing a small percentage of a specific capability's traffic to the new version, comparing evaluation metrics, Part V Section 19, before full rollout) is a supported deployment pattern this storage design enables, detailed operationally in Volume 4.

### 3.8 Localization and tenant-specific prompt variants (forward-looking, scoped)

Consistent with this document set's established practice of naming a plausible extension without over-building it now (Volume 0, Section 23's Future Vision discipline): `PromptTemplate` is designed to support tenant-specific or locale-specific variants of a given capability's prompt (e.g., a tenant whose target systems are documented in a specific domain vocabulary benefiting from a tailored system prompt) as an additive, optional override resolved at Section 2.4's step 3 — but no such variant exists in the GA scope this volume specifies; this is noted here only so the storage design (3.7) doesn't need retrofitting if and when a real tenant-customization requirement emerges, mirroring exactly how Volume 2, Section 3.6 handled the analogous tenant-branding extension point for the theme system.


### 3.9 Relationship to Part III's capability engines

Every capability engine specified in Part III (Sections 7–13) consumes exactly one or more `PromptTemplate`s defined per the structure in this section — Part III does not re-specify prompt authoring or versioning per capability; it specifies each capability's *specific* input/output types, its *specific* retrieval-context requirements (feeding into Part II), and its *specific* evaluation criteria (feeding into Part V), always assuming this section's prompt-management infrastructure as the substrate underneath.

____________________________________________________________________________________________________________________________________________________



4. Retrieval-Augmented Generation (RAG)
4.1 Purpose — what gets retrieved and why

Section 2.4's Planning Engine pipeline names "Context retrieval" as step 2, populating a RetrievalContext before prompt assembly. This section specifies that retrieval system: what content is indexed, how it's retrieved, and — the single most important constraint governing this entire section — how tenant isolation (Volume 0, Section 18.4's data-pooling boundary) is enforced structurally, not just by policy.

4.2 Why RAG, specifically, for Nexus's capabilities

Every one of Part III's capability engines benefits from context beyond what fits in a prompt template's static text: the Requirement Parser (Section 7) benefits from knowing which modules and actions actually exist and how they're typically composed; the Self-Healing engine (Section 10) benefits from knowing how a similar selector was successfully healed before; the Root Cause Analysis engine (Section 12) benefits from knowing this tenant's historical failure patterns. None of this is knowledge a general-purpose LLM has out of the box, and none of it should be baked into a fine-tuned model (which would be slow to update as a tenant's own flows and modules evolve, and would risk exactly the cross-tenant data-mixing Volume 0, Section 18.4 prohibits) — retrieval against a live, tenant-scoped index is the correct architectural fit specifically because Nexus's knowledge is per-tenant and continuously changing (Volume 1's flows, step_results, and module_registrations tables, Part V Section 32, are themselves living, growing data).

4.3 What gets indexed — four distinct corpora
Corpus  Source (Volume 1) Used by (Part III)
Module/action documentation ActionSchema.Description, ParamSpec.Description (Volume 1, Part II §11.8; the mandatory documentation Volume 1, Part VI §40.7 requires) Requirement Parser (§7), Test Generator (§8)
Historical flow examples  Published Flow definitions, tenant-scoped (Volume 1, Part V §32.2)  Test Generator (§8) — "how does this tenant typically structure a checkout-verification flow"
Historical execution outcomes StepResult.Data, AiAttribution records where self-heals occurred (Volume 1, Part II §10.3, §11.5) Self-Healing (§10), Root Cause Analysis (§12)
Log pattern history Aggregated, anonymized-within-tenant patterns from the Logs module's rule/anomaly history (Volume 0, Section 14.8)  Failure Analysis (§11), Root Cause Analysis (§12)

Every corpus above is populated from data Volume 1 already persists — this volume's RAG system indexes Nexus's own operational history, it does not introduce a new, independent data-collection mechanism. This is a direct, deliberate design choice: the platform's own accumulated usage is its richest source of tenant-specific context, and building the RAG index as a derived, continuously-updated view over Volume 1's existing schema (rather than a separately-maintained corpus) keeps this volume's data honest and current by construction.

4.4 Indexing pipeline
Volume 1 domain events (Part II §12) ──► RAG Indexer (this volume) ──► Vector Store
   FlowPublishedEvent                                                    (tenant-partitioned)
   StepCompletedEvent (with AiAttribution)
   ModuleRegisteredEvent

The RAG Indexer is itself an Event Bus consumer (Volume 1, Part III, Section 22.5's consumer table gains a new row for this volume: "RAG Indexer — Tier 2 durable — embeds and upserts the relevant document into the tenant's vector-store partition"), directly reusing Volume 1's existing outbox-backed, at-least-once delivery infrastructure rather than this volume inventing a separate change-data-capture mechanism. Idempotency (required of every Tier-2 consumer per Volume 1, Part II, Section 12.7) is satisfied by upserting on a stable document key (e.g., flow:{flowId}:{version}), consistent with every other durable consumer's obligation established there.

4.5 Vector store architecture and tenant isolation
typescript
interface VectorStore {
  upsert(tenantId: TenantId, documentId: string, embedding: number[], metadata: DocumentMetadata): Promise<void>;
  query(tenantId: TenantId, queryEmbedding: number[], topK: number): Promise<RetrievedDocument[]>;
}

Every method on this interface requires a TenantId as its first parameter, and the underlying implementation (a vector database, or a pgvector extension on the same PostgreSQL instance Volume 1 already specifies, Part V Section 32.6's indexing discipline extended to vector similarity search — a deliberate infrastructure-minimization choice consistent with Volume 1, Part III, Section 22.3's "no new hard dependency for GA scale" reasoning) enforces physical partitioning by tenant, not merely a filtered query — directly mirroring Volume 1, Part IV, Section 26.3's three-layer tenant-isolation defense-in-depth (repository filter, row-level security, application-service assertion), now applied to vector retrieval specifically: a query for tenant A's context can structurally never retrieve tenant B's embedded documents, verified the same way Volume 1's row-level-security policies are (Volume 1, Part V, Section 32.7).

4.6 Retrieval pipeline
1. Query construction — capability-specific (Section 2.4 step 2 is invoked with the
   capability's specific input, e.g., a natural-language flow description for
   Requirement Parser, or a broken selector + page HTML for Self-Healing)
2. Embedding — the query is embedded using the same model/dimension as the
   indexed corpora (Section 4.4) — a versioned choice, Part IV §14's provider
   abstraction covers embedding-model selection alongside generation-model selection
3. Tenant-scoped similarity search — VectorStore.query(tenantId, ...), topK
   configurable per capability (a broad Requirement Parser query might retrieve
   more candidate module examples; a narrow Self-Healing query might retrieve
   just the single most similar prior heal)
4. Re-ranking (optional, capability-dependent) — for capabilities where recall
   quality materially affects output quality (Test Generator, Self-Healing),
   a lightweight re-ranking step reorders the topK results before they're
   assembled into RetrievalContext
5. Context assembly — trimmed and formatted to fit the target PromptTemplate's
   requiredContextFields (Section 3.4), with an explicit token budget per
   capability to keep prompt-assembly cost bounded and predictable (feeding
   directly into Part IV §16's cost-optimization concerns)
4.7 Freshness and staleness

Directly mirroring Volume 1, Part V, Section 23.6's explicit per-read-model consistency table (a pattern this document set has now applied at every layer — backend read models, frontend cache invalidation in Volume 2 Section 8.3, and now RAG indexing): the RAG index is eventually consistent relative to Volume 1's source-of-truth data, with staleness bounded by the Event Bus Tier 2 relay's typical latency (Volume 1, Part III, Section 22.2). This is an accepted, stated tradeoff — a flow published moments ago might not yet be retrievable as a "similar prior example" for a Test Generator request, which is a materially less severe consequence than, say, FlowSummaryReadModel's strong-consistency requirement (Volume 1, Part V, Section 23.4), so no synchronous-indexing exception is warranted here the way one was warranted there.

4.8 Retrieval quality evaluation

Consistent with Part V's evaluation-metrics discipline (forward reference, fully specified there): retrieval quality itself — not just final capability-output quality — is tracked as a distinct metric (e.g., a periodic, curated "does this query retrieve the document a human expert would consider most relevant" evaluation set, mirroring Section 3.6's prompt evaluation-set discipline applied to retrieval specifically) — because a capability engine's output can only be as good as the context it was given, and conflating "the model produced a bad output" with "the model was given bad context" would make debugging a capability-quality regression significantly harder than treating them as two separately-measured, separately-improvable concerns.



5. Memory
5.1 Purpose and distinction from RAG

Section 4 specified retrieval against a persistent, continuously-indexed corpus — Nexus's accumulated operational history. This section specifies a genuinely different concern: conversational and interaction memory — what the AI layer remembers about the current, in-progress interaction (a flow-generation session where a user iteratively refines a generated draft) and, more narrowly, what it's permitted to carry forward about a specific user's or tenant's interaction patterns over time. Conflating these two concerns (as "memory" and "RAG" are sometimes loosely conflated in general AI-system discourse) would be a mistake this document set's precision-about-terminology discipline (Volume 1, Part II, Section 6.3's ubiquitous-language rule) exists specifically to prevent.

5.2 Session memory — scoped to one Planning Engine interaction sequence

The most common memory need: Volume 0's UC-2 (Priya using AI flow generation) is not always a single request-response exchange — a user might review a generated flow, ask for a specific adjustment ("also check that the response includes a tracking number"), and expect the AI to understand this as a refinement of the prior output, not a from-scratch new request.

typescript
interface AiSession {
  sessionId: string;
  tenantId: TenantId;
  userId: UserId;
  capability: AiPlanningRequest['capability'];
  turns: AiSessionTurn[];             // bounded — see 5.3
  createdAt: Date;
  expiresAt: Date;                     // short-lived, see 5.4
}

interface AiSessionTurn {
  request: AiPlanningRequest;
  result: AiPlanningResult<unknown>;
}

A session is explicitly scoped to one capability and one Planning Engine interaction sequence — a flow-generation session's memory is never implicitly available to a subsequent, unrelated self-healing request, even within the same user's browser session (Volume 2, Section 14's authentication session is a distinct, longer-lived concept from this AiSession, deliberately not conflated with it).

5.3 Session memory bounds — why unbounded conversation history is rejected

AiSession.turns is capped (a configurable maximum, e.g., 10 turns) rather than allowed to grow indefinitely, for two compounding reasons: unbounded conversation history directly threatens Part IV, Section 16's cost-optimization goals (every additional turn's history is repeated context in every subsequent prompt, per standard LLM context-window economics), and — more importantly for this volume's own stated priorities (Section 1.7) — a very long, drifting conversation is exactly the shape of interaction most vulnerable to the prompt-injection and goal-drift risks Part V, Section 17's guardrails are designed to catch early, not after many compounding turns have accumulated. A session reaching its turn limit prompts the user to start a fresh session (with the option, per 5.2's session-turn history, to reference the prior session's final output as an explicit starting point) rather than silently truncating history in a way that could produce confusing, context-losing behavior.

5.4 Session lifetime and expiry

Sessions expire after a short period of inactivity (e.g., 30 minutes, deliberately shorter than Volume 2, Section 14.5's 15-minute access-token lifetime is long relative to, chosen independently based on realistic authoring-session behavior rather than tied to the authentication token's own expiry) and are never persisted beyond that window — this is a direct, deliberate architectural choice reinforcing Volume 0, Section 18.4's data-minimization spirit: conversational context is ephemeral working memory, not a permanent record Nexus retains about how a user phrases their requests. (Contrast this explicitly with Volume 1, Part IV, Section 27's AuditEntry — the fact that an AI flow-generation request occurred is audited permanently per NFR-SEC-3, but the specific back-and-forth conversational content refining that request is not retained beyond the session, a deliberate and different retention posture for a deliberate and different reason.)

5.5 Tenant-scoped pattern memory — the Future Vision capability, scoped honestly

Volume 0's Future Vision (Section 23.3) named "cross-flow pattern learning... using the accumulated history of self-heals, flake patterns, and failure diagnostics across a customer's own flow history" as a plausible future direction, explicitly not committed GA scope. This section names the architectural seam for it without over-building it now, consistent with this document set's established practice (Section 3.8 of this volume already applied this same discipline to prompt localization): a TenantPatternMemory construct — a longer-lived, tenant-scoped (never cross-tenant) summary derived periodically from Section 4's RAG corpora (e.g., "this tenant's Web-module selectors most commonly break due to CSS-class renames, and heals involving data-testid fallback attributes have historically had a 90%+ acceptance rate for this tenant specifically") — is architecturally anticipated as an optional, additional RetrievalContext input (Section 2.3) that Section 10's Self-Healing engine could consume, but is not part of this volume's GA-scoped capability engines (Part III). This is stated explicitly rather than silently implied, matching Volume 0's own discipline of clearly marking aspiration versus commitment (Volume 0, Section 8.4's assumption-labeling; Section 23.7's explicit non-inclusions).

5.6 What memory never does — restating the permanent boundaries here specifically

Directly re-applying Section 1.4's architectural invariants to this section's specific concern: session memory (5.2) and tenant pattern memory (5.5) are both read-only inputs to a capability engine's context — neither is ever treated as authoritative in place of Volume 1's actual domain state. A session's remembered "the user asked to check for a tracking number" is a hint informing the next prompt's construction, never a substitute for the actual generated Flow draft's real, persisted content — if a session's memory and the actual current state of a referenced Flow draft ever diverge (e.g., the user edited the draft directly in the Workflow Designer, Volume 2 Section 11, between AI turns), the actual persisted Flow state wins, and the Planning Engine's next turn is constructed from that authoritative state, not from a possibly-stale memory of what the AI last proposed.

5.7 Memory and multi-tab/multi-device consistency

Referencing Volume 2, Section 14.9's cross-tab session-consistency design: AiSession (5.2) is deliberately not synchronized across browser tabs the way authentication state is — an AI flow-generation conversation happening in one tab is scoped to that tab's interaction sequence, and opening the same flow in a second tab starts a fresh session rather than attempting to merge or share conversational memory across tabs. This is a deliberate simplicity choice: the complexity of synchronizing conversational AI state across concurrent tabs was judged not to earn its cost (Volume 1, Architecture Principle 20.7's recurring standard) relative to the straightforward alternative of scoping each tab's AI interaction independently, with Section 5.6's "actual persisted state always wins" rule ensuring no data-consistency hazard results from this choice even if a user does have the same flow open in two tabs simultaneously.




6. Knowledge Base
6.1 Purpose — the curated layer above raw RAG retrieval

Section 4 specified RAG as retrieval against Nexus's own accumulated operational data (flows, executions, module schemas). This section specifies a distinct, complementary corpus: curated, authored knowledge that doesn't arise automatically from platform usage — module developer documentation beyond the mandatory ActionSchema descriptions (Volume 1, Part VI, Section 40.7), guardrail/prompt packs (Volume 0, Section 17.2's marketplace artifact type), and platform-level best-practice content Nexus's own team authors and maintains. Where Section 4's RAG corpora are derived from Volume 1's live data, this section's Knowledge Base is authored content with its own lifecycle, review process, and versioning.

6.2 Knowledge Base content types
Content type  Authored by Scope Used by
Platform best-practice guides Nexus's own team  Global (not tenant-scoped — this is Nexus's own product knowledge, not customer data) Requirement Parser (§7) — e.g., "how to phrase a testable acceptance criterion"
Module developer documentation  Module authors (built-in team or marketplace developers, Volume 0 §17)  Per-module, global once published Test Generator (§8) — deeper usage examples beyond the minimum ActionSchema.Description
Guardrail/prompt packs  Nexus's team or Enterprise-certified marketplace partners (Volume 0, Section 17.2's third artifact type)  Tenant-installable, never auto-global Part V, Section 17 — domain-specific guardrail policy content (e.g., a healthcare PHI-redaction pack)
Tenant-authored knowledge (optional, tenant-supplied) The tenant itself Strictly tenant-scoped  Any capability, when a tenant chooses to supply supplementary context (e.g., internal API naming conventions)
6.3 Why a separate system from Section 4's vector store, not the same index

A deliberate architectural distinction: Knowledge Base content is reviewed, versioned, and explicitly published content (mirroring Volume 1, Part II, Section 9.2's Draft/Published Flow lifecycle discipline, applied here to knowledge content) — it does not get silently, automatically indexed the instant it's authored the way Section 4's RAG corpora are event-driven-indexed from live platform activity. This matters specifically for the global content types in 6.2's table (platform best-practice guides, module documentation): this content directly shapes AI capability behavior for potentially every tenant, so it goes through the same rigor Volume 0, Section 17.3 established for marketplace module certification, before it's retrievable at all — an unreviewed draft of a best-practice guide must never silently influence a live flow-generation request.

6.4 Retrieval integration — one unified RetrievalContext, two distinct sources
typescript
interface RetrievalContext {
  ragResults: RetrievedDocument[];        // Section 4 — tenant-scoped, live-data-derived
  knowledgeBaseResults: KnowledgeBaseEntry[];  // this section — curated, published content
}

Both sources feed into the same RetrievalContext the Planning Engine assembles (Section 2.3, 2.4 step 2) and the same prompt-assembly step (Section 2.4 step 3) consumes — a PromptTemplate's requiredContextFields (Section 3.4) can reference either or both, but the two remain architecturally distinct sources with different governance (6.3), different tenant-scoping rules (6.5), and different freshness guarantees (Section 4.7's eventual consistency versus this section's explicit-publish-triggered consistency).

6.5 Tenant scoping for Knowledge Base content

Directly extending Volume 0, Section 18.4's data-pooling boundary and Section 4.5's vector-store tenant partitioning to this section's different content shape: global content (best-practice guides, module documentation) is retrievable by every tenant equally — this is not customer data, it's Nexus's own product knowledge, so no isolation concern applies. Guardrail/prompt packs and tenant-authored knowledge (6.2's third and fourth rows) are strictly opt-in and tenant-scoped, using the identical TenantId-partitioned enforcement Section 4.5 established for the RAG vector store — a marketplace guardrail pack a tenant hasn't installed is never retrievable in that tenant's RetrievalContext, mirroring exactly how Volume 1, Part IV, Section 26.5 scoped Community-tier module registration per-tenant rather than globally.

6.6 Knowledge Base authoring and review workflow
Draft (author writes/edits content)
   │
   ▼
Review (Nexus team for platform content; Verified/Enterprise-certified
         review process, Volume 0 §17.3, for marketplace guardrail packs)
   │
   ▼
Published (indexed and retrievable — mirrors Volume 1 Part II §9.2's
            Flow Draft→Published transition exactly, including the same
            "immutable once published, new version for changes" discipline,
            Section 6.3 already flagged)
   │
   ▼
Deprecated (superseded by a newer version, retained for audit/rollback
             per Volume 1 Part III §18.7's contract-deprecation pattern,
             no longer retrieved for new requests)
6.7 Module developer authoring experience

Directly serving Persona Raj (Volume 0, Section 10.5): a module developer supplying documentation beyond the mandatory ActionSchema.Description (Volume 1, Part VI, Section 40.7) does so through the same marketplace publishing flow already established (Volume 0, Section 17.4's installation flow; Volume 1, Part III, Section 18.8's registration flow), with an additional, optional documentation-authoring step — this is explicitly framed as an enhancement to a module's AI-assisted discoverability (a well-documented module's actions are more likely to be correctly suggested by the Requirement Parser and Test Generator, Sections 7–8), giving Raj a concrete, tangible incentive to invest in thorough documentation beyond the bare minimum Volume 1 requires.

6.8 Freshness and cache invalidation

Unlike Section 4.7's eventually-consistent RAG index, Knowledge Base retrieval is synchronously consistent with the Published state — the moment a piece of content transitions Draft → Published (6.6), it is immediately retrievable, and the moment a version is superseded (Deprecated), it is immediately no longer retrieved for new requests. This stronger consistency guarantee is justified, unlike the RAG index's acceptable eventual consistency (Section 4.7), because Knowledge Base publish events are comparatively rare, deliberate, human-reviewed actions (6.6) rather than the high-frequency, automatic platform-activity events driving Section 4's index — the same reasoning Volume 1, Part V, Section 23.4 used to justify FlowSummaryReadModel's synchronous consistency over FlowReportReadModel's eventual consistency, applied here to an analogous low-frequency-write, consistency-matters case.

6.9 Relationship to Part III's capability engines

As with Section 4's RAG corpora, Part III's capability engines consume this section's Knowledge Base purely through the unified RetrievalContext (6.4) — no capability engine queries Knowledge Base content directly or independently of the Planning Engine's step-2 retrieval, preserving the same "one retrieval boundary, uniformly applied" discipline Section 2.2 established as this volume's core organizing principle.


____________________________________________________________________________________________________________________________________________________



7. Requirement Parser
7.1 Purpose and position in the flow-generation pipeline

Volume 1's GenerateFlowFromDescriptionCommand (Part II, Section 13.2) takes a single natural-language description and must produce a valid, structurally-sound Flow draft. Section 2.6's mapping table split this into two composed capabilities: the Requirement Parser (this section) and the Test Generator (Section 8). This section specifies the first stage — turning unstructured English into a structured, intermediate Parsed Intent representation, before any actual flow steps are constructed. This staged design exists for a concrete reason: separating "what does the user want verified" from "how do we construct valid steps to verify it" makes each stage independently testable and independently improvable, mirroring the same separation-of-concerns discipline Volume 1 applied to its own orchestration layers (Part I, Section 5's Flow Parser versus Flow Engine as genuinely distinct components).

7.2 Output type — Parsed Intent
typescript
interface ParsedIntent {
  targetSystemHints: string[];        // e.g., ["health endpoint", "/api/health"] — extracted
                                          // mentions of what system/endpoint is under test
  candidateModules: ModuleKind[];      // which of the tenant's available modules
                                          // (Volume 1 Part II §14.2's GetAvailableModuleActionsQuery
                                          // result, retrieved via this volume's Section 4 RAG,
                                          // not guessed from general knowledge) plausibly apply
  assertionIntents: AssertionIntent[]; // structured, not yet module-specific
  ambiguities: AmbiguityFlag[];        // explicit — see 7.5
}

interface AssertionIntent {
  description: string;                 // "the response body contains status: ok"
  assertionKind: 'status' | 'bodyField' | 'visualState' | 'logPattern' | 'unclear';
}

interface AmbiguityFlag {
  aspect: string;                       // e.g., "target module unclear"
  candidates: string[];
  requiresClarification: boolean;
}
7.3 Why a distinct intermediate representation, not direct English-to-Flow generation

A tempting simpler design has the Test Generator (Section 8) work directly from the raw natural-language string. This section's staged design is preferred for three reasons: first, ParsedIntent is a much smaller, more constrained structure for output-schema validation (Section 3.5) to check — catching a Requirement Parser failure ("the model couldn't identify any candidate module") early, before the more expensive Test Generator stage runs, directly serving Part IV's cost-optimization goals by failing cheaply. Second, ParsedIntent.ambiguities gives the Planning Engine (Section 2.4) an explicit, structured signal for when to prompt the user for clarification rather than guessing — a capability directly serving UX Principle 1.2's "never a fiction of the real state" (Volume 2, Section 1.3) applied to AI-generated content specifically: an ambiguous request should surface as a visible ambiguity, not a silently-guessed, confidently-wrong flow. Third, ParsedIntent is independently reusable — a future capability (e.g., Volume 0's Future Vision Section 23.3's "autonomous exploration and coverage suggestion") could consume ParsedIntent without depending on flow-construction logic at all.

7.4 Retrieval context this capability consumes

Per Section 4.3's corpora table, the Requirement Parser primarily retrieves module/action documentation — a natural-language description mentioning "check the health endpoint" needs grounding in which of this specific tenant's registered modules and actions could plausibly implement that check, retrieved via Section 4.6's pipeline rather than relying on the model's general training-data knowledge of what a generic testing tool might support. This retrieval step is precisely why candidateModules (7.2) is populated from actually-registered modules, never a hallucinated module name — a direct, concrete enforcement mechanism for UX Principle 1.3's module-parity guarantee (Volume 2, Section 1.4) extended into this volume: the Requirement Parser structurally cannot suggest a module that doesn't exist in this tenant's environment, because its retrieval context never contains one.

7.5 Handling ambiguity — the clarification loop

When ParsedIntent.ambiguities contains an entry with requiresClarification: true, the Planning Engine (Section 2.4) does not proceed to Section 8's Test Generator — it returns the ambiguity to the calling Volume 1 command handler as a distinct, structured outcome (an extension of FlowGenerationResult, Volume 1 Part II Section 13.2, carrying a clarification-needed variant alongside its success/failure variants), which Volume 2's AiFlowGenerationPanel (Section 6.2 there) surfaces as a follow-up question rather than silently picking the most likely interpretation. This is the Requirement Parser's most direct implementation of Section 1.5's AI Vision statement — making the platform "easier to author" specifically means surfacing genuine ambiguity for a human to resolve, not hiding it behind confident-sounding but potentially wrong output.

7.6 Multi-turn refinement via Session Memory

Referencing Section 5.2's AiSession: a follow-up request within the same session ("also check for a tracking number in the response") is parsed with the prior turn's ParsedIntent included as additional context — the Requirement Parser's prompt template (Section 3.2) has a distinct variant specifically for refinement turns versus initial-request turns, since a refinement should produce an updated ParsedIntent (adding to or modifying the prior one), not a wholly independent parse that discards everything previously established.

7.7 Evaluation criteria for this capability

Per Section 3.6's evaluation-set discipline: the Requirement Parser's evaluation set consists of representative natural-language descriptions (drawn from real, anonymized-where-needed usage patterns once available, and from Volume 0's own use-case catalog, Section 11, as an initial seed set before real usage data exists) with human-annotated expected candidateModules and assertionIntents — measuring precision (did it suggest only genuinely relevant modules) and recall (did it identify every assertion the description actually implies) separately, since these two failure modes have different product consequences: low precision produces a cluttered, confusing generated flow; low recall produces an incomplete one that looks complete, the more dangerous failure mode per this volume's general bias toward surfacing rather than hiding uncertainty (7.5).



8. Test Generator
8.1 Purpose — from Parsed Intent to a valid Flow

Where Section 7's Requirement Parser produces a structured, module-agnostic-until-grounded ParsedIntent, this section specifies the capability that consumes it and produces an actual, structurally valid Flow draft (Volume 1, Part II, Section 8.2) — the second and final stage of the pipeline backing GenerateFlowFromDescriptionCommand (Volume 1, Part II, Section 13.2), directly implementing Volume 0's UC-2.

8.2 Output type — a Flow draft, not a new type

Unlike Section 7's bespoke ParsedIntent type, the Test Generator's output is deliberately the exact same Flow domain type Volume 1 already defines (Part II, Section 9.2), never a parallel "AI-generated flow" type requiring separate handling downstream:

typescript
interface TestGenerationOutput {
  flow: FlowDraftDto;              // structurally identical to a human-authored draft
  stepProvenance: Map<FlowStepId, StepProvenance>;   // per-step: which ParsedIntent
                                                          // assertion/module hint it was
                                                          // derived from, and confidence
}

interface StepProvenance {
  derivedFromIntent: string;         // which AssertionIntent.description this step implements
  confidence: number;
  retrievedExampleUsed?: string;      // if a historical flow example (Section 4.3) informed this step
}

This is a direct, deliberate application of Section 1.2's framing — this volume does not introduce new domain concepts; it produces valid instances of Volume 1's existing ones. A generated Flow draft is, from the moment it's returned, indistinguishable in type from a human-authored one; stepProvenance is the only AI-specific addition, and it travels alongside the draft as metadata for the review experience (Volume 2, Section 6.2's AiFlowGenerationPanel), never as part of the Flow aggregate itself (Volume 1, Part II, Section 9.2 defines that aggregate's shape completely; this volume does not extend it).

8.3 Step construction — grounded in retrieved ActionSchemas, never invented

For each AssertionIntent in the input ParsedIntent, the Test Generator constructs one or more FlowSteps (Volume 1, Part II, Section 10.2), with every step's params validated against the actual retrieved ActionSchema (Section 4.3's module/action documentation corpus) for the chosen module and action — this is the same JSON-Schema-derived validation Volume 2, Section 10.4 specified for the frontend's manual parameter editor, now applied here to verify AI-generated parameters before they're ever returned to a user. A generated step whose parameters fail this validation is not silently included with best-effort values (directly consistent with Section 3.5's "no lenient coercion" rule) — it's either regenerated with corrective feedback (a bounded retry, not unlimited, per Part IV's cost-optimization concerns) or omitted with the gap surfaced via stepProvenance's confidence field dropping low enough to flag for human attention.

8.4 Variable chaining in generated flows

Where a ParsedIntent implies multi-step data dependency (e.g., "create an order, then check its status via the API" implies the order ID from step 1 feeds step 2), the Test Generator constructs the saveAs/{{vars.x.y}} chaining (Volume 1, Part II, Section 10.2, Section 11.7) explicitly — informed by retrieved historical flow examples (Section 4.3) showing how this tenant's existing flows typically chain similar data, when such examples exist, falling back to generic, schema-informed chaining (matching a saveAs binding's likely field name against the target step's parameter names) when no closely-relevant historical example is retrieved.

8.5 Retrieval context this capability consumes

Beyond the ActionSchema documentation already used in Section 7.4 (shared, not re-retrieved independently — Section 2.4's context-retrieval step populates RetrievalContext once per Planning Engine invocation, and the Test Generator stage reuses the same retrieved module documentation the Requirement Parser stage already grounded its module suggestions in): this capability additionally retrieves historical flow examples (Section 4.3) specifically to inform step structure and chaining patterns (8.4), and, where the Knowledge Base (Section 6) has published best-practice content relevant to the target module (e.g., a guide on structuring API-assertion flows), that content is included as well.

8.6 Structural validation before returning to Volume 1

Before TestGenerationOutput is returned from the Planning Engine (Section 2.4, step 6), the constructed Flow draft is validated against the exact same publish-time-adjacent structural checks Volume 1's Flow.Publish performs (Volume 1, Part II, Section 9.2) — module/action resolution, parameter schema conformance — run here proactively, before the user ever sees the draft, so that a generated flow that would fail publication is caught and either corrected or flagged, not discovered only when the user attempts to publish it themselves. This double-checking (once here, once again authoritatively by Volume 1's own Flow.Publish at actual publish time) is intentional redundancy, not waste — this volume's capability engines never get to skip Volume 1's authoritative validation just because they performed a similar check first, directly reinforcing Section 1.4's "AI never executes or persists autonomously" boundary: this capability's own validation is advisory quality-control, Volume 1's is the real, load-bearing gate.

8.7 Human review remains mandatory regardless of confidence

Even a TestGenerationOutput with uniformly high per-step confidence (8.2) never bypasses the human-review step Volume 0's UC-2 specifies — the generated Flow always lands as a Draft, requiring an explicit, separate PublishFlowCommand (Volume 1, Part II, Section 13.2) issued by the human reviewing it. This is stated here explicitly, even though it's already a structural consequence of 8.2's design (a Draft is simply what this capability produces), because it's worth restating as a substantive product guarantee, not just an implementation detail: no confidence threshold, however high, ever promotes this capability's output directly to Published — that would cross Section 1.4's permanent autonomy boundary regardless of measured accuracy, a line this volume does not treat as negotiable based on quality metrics improving over time.

8.8 Evaluation criteria

Directly extending Section 7.7's evaluation-set discipline: the Test Generator's evaluation set measures structural validity rate (does the generated Flow pass Section 8.6's proactive check, the exact metric Volume 0, Section 18.7 named as this capability's primary quality signal) and, separately, a human-rated semantic-fidelity score (does the generated flow actually verify what the original natural-language description asked for, judged against Section 7's ParsedIntent as the intermediate ground truth) — tracked as two distinct numbers specifically because a flow can be structurally perfect while missing the user's actual intent, or vice versa, and conflating them into one score would hide which failure mode a given prompt or retrieval change is actually improving or regressing.



9. Locator Generator
9.1 Purpose — a shared capability, not a standalone product feature

Unlike Sections 7–8 (which compose to serve exactly one Volume 1 command) and Section 10 (which this capability directly feeds), the Locator Generator is specified as its own section because it is consumed by two distinct callers: the Test Generator (Section 8.3, when constructing Web-module steps that need a selector for a described UI element) and the Self-Healing engine (Section 10, when an existing selector has broken). Separating it out avoids duplicating selector-generation logic in two places, directly consistent with this document set's repeated "define once, reuse everywhere" discipline (Volume 2, Sections 9.4, 10.6, 12.5 all made this same choice for their respective concerns).

9.2 Output type
typescript
interface LocatorProposal {
  selector: string;                    // e.g., "[data-testid='submit-button']"
  selectorStrategy: 'testId' | 'role' | 'text' | 'css' | 'xpath';
  confidence: number;
  alternativeSelectors: RankedAlternative[];   // ranked fallback options, see 9.5
  reasoning: string;                    // human-readable — always populated, see 9.6
}

interface RankedAlternative {
  selector: string;
  selectorStrategy: LocatorProposal['selectorStrategy'];
  confidence: number;
}
9.3 Two distinct inputs, one shared output shape

The Locator Generator accepts either of two input shapes, reflecting its two calling contexts:

typescript
type LocatorGeneratorInput =
  | { mode: 'describe'; elementDescription: string; pageContext: PageStructureSummary }
  | { mode: 'heal'; brokenSelector: string; originalIntent: string; currentPageHtml: string };

The 'describe' mode serves Section 8.3's Test Generator use case ("a submit button" → a proposed selector against a described or retrieved page structure); the 'heal' mode serves Section 10's Self-Healing use case, directly matching Volume 1's already-specified RequestSelectorHealCommand input shape (Volume 1, Part II, Section 13.3: brokenSelector, intent, pageHtml) exactly — this volume's LocatorGeneratorInput.heal variant is a direct, typed realization of what Volume 1 already anticipated needing, not a new design.

9.4 Selector strategy preference — why testId and role are preferred over css/xpath

The Locator Generator's prompt template (Section 3.2) explicitly instructs a preference ordering — data-testid attributes and ARIA-role-based selectors are preferred over raw CSS selectors or XPath, and CSS/XPath are used only when no more stable alternative is identifiable — because this preference ordering directly correlates with real-world selector stability (a data-testid attribute is far less likely to change during unrelated styling refactors than a CSS class chain). This is a concrete, worked example of Section 1.5's AI Vision statement in practice: preferring stable selectors is what makes the platform genuinely "cheaper to maintain" (Volume 0, Section 18.3's maintenance-reduction value mechanism), not merely "produces a working selector today with no regard for tomorrow's flake rate."

9.5 Ranked alternatives — never a single point of failure

Every LocatorProposal includes alternativeSelectors — a ranked list of fallback strategies for the same target element, not just the single top choice. This directly informs the Web module's own resilience: Volume 1, Part III, Section 18's WebModule implementation (originally prototyped with a single-selector click/fill design) is extended, per this volume's influence flowing back into how the Web module could optionally attempt ranked alternatives before failing outright — noted here as a forward-looking architectural connection between this volume and Volume 1's module implementation, flagged explicitly (consistent with this document set's practice of surfacing cross-volume dependencies rather than leaving them implicit) as a Volume 1 amendment worth considering, similar in spirit to the Unit of Work correction made earlier in this project, though not applied unilaterally here — this volume specifies the capability; whether the Web module actually consumes alternativeSelectors for automatic fallback-attempt behavior is a Volume 1 implementation decision this volume flags but does not unilaterally impose.

9.6 reasoning — mandatory, never omitted

Every LocatorProposal.reasoning field is populated, always, even for high-confidence proposals — directly implementing UX Principle 1.6's transparency requirement (Volume 2, Section 1.7) at the data-shape level: the Volume 2 self-heal review UI (Volume 2, Section 10.5's read-only Monaco display of page HTML during self-heal review) surfaces this reasoning text alongside the proposed selector, so a human reviewing a heal sees not just what is proposed but why — e.g., "the original selector targeted a class that no longer exists on this element; the data-testid='submit-btn' attribute is present and semantically matches the original intent."

9.7 Confidence calibration — why this is the capability Part V scrutinizes most

Because Section 10's Self-Healing engine uses LocatorProposal.confidence directly against Volume 1, Part III, Section 18.5's configured confidence threshold to decide whether a heal auto-applies or only advises, this capability's confidence calibration is held to a materially higher evaluation bar than the other capability engines in this Part — Part V, Section 19's evaluation metrics track not just whether proposals are generally good, but specifically whether confidence scores are well-calibrated (a proposal scored 0.9 confidence should be correct roughly 90% of the time across a large evaluation set, not just "usually pretty good") — poorly-calibrated confidence is arguably more dangerous than uniformly-low confidence, since it's what Volume 1's threshold-based auto-apply decision (Volume 1, Part V, Section 34.4's sequence diagram) directly trusts.

9.8 Retrieval context this capability consumes

Primarily historical execution outcomes (Section 4.3) — specifically, prior successful heals for similar broken-selector patterns on this tenant's flows, retrieved to inform both the proposed selector and its confidence (a pattern that has healed successfully many times before for this tenant warrants materially higher confidence than a novel one) — directly the mechanism Section 5.5 named as a Future Vision extension point (TenantPatternMemory) but, consistent with that section's honest scoping, this capability's GA behavior relies on Section 4's already-specified RAG retrieval over historical AiAttribution records (Volume 1, Part II, Section 11.5), not the not-yet-built tenant-pattern-memory summarization layer.



10. Self-Healing
10.1 Purpose — the orchestrating capability around Locator Generator

Section 9 specified the Locator Generator as a pure proposal-generation capability. This section specifies Self-Healing as the capability that consumes a LocatorProposal and makes the confidence-threshold-gated decision Volume 1, Part III, Section 18.5 and Part V, Section 34.4 already anticipated architecturally — this is the direct, full specification of the capability those Volume 1 sections referenced but correctly deferred to this volume.

10.2 Output type
typescript
interface SelfHealOutcome {
  decision: 'auto-applied' | 'advisory-only' | 'no-viable-proposal';
  proposal: LocatorProposal;            // Section 9.2 — always included, even when advisory-only
  attribution: AiAttribution;            // Volume 1 Part II §11.5 — constructed here, directly
                                            // consumed by Volume 1's StepResult (Volume 1 Part II §10.3)
}
10.3 The threshold decision — implemented exactly where Volume 1 specified it would be
typescript
function decideSelfHeal(proposal: LocatorProposal, threshold: number): SelfHealOutcome['decision'] {
  if (!proposal) return 'no-viable-proposal';
  return proposal.confidence >= threshold ? 'auto-applied' : 'advisory-only';
}

This is the literal implementation of the decision Volume 1, Part III, Section 18.5 named ("if confidence exceeds a configured threshold, the flow proceeds using the healed selector... if confidence is below threshold, the flow fails normally") and Volume 1, Part V, Section 34.4's sequence diagram traced end-to-end. This volume's contribution is specifying how the confidence number itself is produced and trusted (Section 9.7's calibration discipline) — Volume 1 correctly specified the decision's mechanics, deferring the harder problem of making that confidence number meaningful to this volume, exactly as this document set's volume-ordering was designed to allow (Section 1.6).

10.4 Threshold configuration — tenant and flow-level, never hardcoded

The confidence threshold is not a single, global constant — it is configurable per-tenant (a tenant with lower risk tolerance sets a higher bar for auto-apply) and, per Volume 1's flow-authoring flexibility, overridable per-flow (a flow author can disable self-healing entirely for a specific flow, or set a stricter threshold for a particularly sensitive flow) — surfaced in Volume 2's flow-authoring UI (a settings panel extension to Section 11 there, the Workflow Designer) as an explicit, visible configuration a flow author sets deliberately, never an invisible platform default a user might not realize is active.

10.5 Permission inheritance — restating Volume 1's rule as this capability's binding constraint

Directly re-citing Volume 1, Part IV, Section 25.6: a self-heal action, even when auto-applied, executes under the authorization context of the FlowRun's original triggering identity — this volume's Self-Healing capability never has, requests, or is granted any elevated privilege beyond what that triggering identity already held. This section restates the rule here specifically because it's the exact boundary that makes "auto-applied" safe to say at all — an auto-applied heal is not the AI acting with independent authority; it is the AI proposing an action that executes within the already-authorized triggering identity's existing permission envelope, a distinction worth being precise about given how easily "auto-applied" could be misread as a relaxation of Section 1.4's autonomy boundary (it is not — the boundary that's actually relaxed is human-in-the-loop-per-instance, not authorization scope, which never changes).

10.6 Transparency — every heal is visible, auto-applied or not

Directly implementing FR-AI-3 (Volume 0, Section 12.3) and Volume 1, Part II, Section 12.3's StepSelfHealedEvent, which fires in addition to StepCompletedEvent regardless of whether the decision (10.3) was auto-applied or advisory-only: there is no code path in this capability where a heal occurs and no attribution record results. SelfHealOutcome.attribution (10.2) is always constructed, always attached to the resulting StepResult, and always visible in Volume 2's reporting surface (Section 9.3's AiAttributionBadge) and Volume 1's audit trail (Part IV, Section 27) — this is the single most heavily cross-volume-enforced guarantee in this entire document set, appearing as a binding requirement in Volume 0 (product strategy), Volume 1 (domain model, events, sequence diagram), Volume 2 (UI treatment, accessibility), and now this volume (the capability that actually produces the attribution data all those other volumes render and persist).

10.7 The no-viable-proposal outcome — honest failure, not a forced guess

When the Locator Generator (Section 9) cannot produce any proposal meeting even a minimal quality bar (distinct from, and lower than, the auto-apply threshold — a floor below which even an advisory suggestion isn't worth showing), SelfHealOutcome.decision is 'no-viable-proposal', and Volume 1's RequestSelectorHealCommand handler (Part II, Section 13.3) treats this identically to the AI module being entirely unavailable (Section 2.5's AiUnavailableError path) — the flow fails normally, with no AI-attributed content at all, rather than this capability manufacturing a low-confidence proposal just to have something to show. This is a direct, deliberate application of Section 7.5's "surface ambiguity, don't hide it behind confident-sounding guesses" principle, now applied to the case where the honest answer is "no good proposal exists."

10.8 Retrieval context this capability consumes

Entirely inherited from Section 9.8 — Self-Healing does not perform independent retrieval; it consumes the LocatorProposal (and the retrieval context that informed it) that Section 9's engine already produced, since Self-Healing's job is the threshold decision (10.3) and attribution construction (10.6), not additional context-gathering.

10.9 Evaluation criteria — self-heal acceptance rate, the metric Volume 0 already named

Directly implementing Volume 0, Section 18.7's designated primary quality signal for this capability: the percentage of auto-applied heals that a human, reviewing the resulting run, does not subsequently revert or flag as incorrect (tracked via a lightweight, optional "this heal was wrong" action surfaced in Volume 2's run-detail view alongside the AiAttributionBadge) — this is the exact metric Volume 0, Part I, Section 6.2 set a ≥70% target for, now given its full, precise operational definition: the denominator is every auto-applied decision (10.3), the numerator is every one not subsequently flagged incorrect, computed on a rolling window and reviewed as part of Part V, Section 19's release-gating discipline for any change to this capability's threshold defaults, prompt template (Section 3), or the underlying Locator Generator (Section 9).



11. Failure Analysis
11.1 Purpose — the first-tier diagnostic capability

This section specifies the direct formalization of the triageLogs capability already sketched in this project's original AiModule prototype and named explicitly in Volume 0, Section 14.9 ("log-triage capability"). Failure Analysis is the fast, first-tier diagnostic engine — given a failed StepResult or a batch of log lines, it produces an immediate severity/category classification. Section 12 (Root Cause Analysis) is the deeper, slower capability that Failure Analysis's output feeds into when a more thorough investigation is warranted — the two are distinct capabilities specifically because they serve different points in Persona Sofia's and Priya's diagnostic workflow (Volume 0, Section 10.2–10.3): a quick triage label appears immediately in a failed run's report; a full root-cause narrative is available on demand, not computed eagerly for every failure.

11.2 Output type
typescript
interface FailureAnalysisResult {
  severity: 'info' | 'warn' | 'error' | 'critical';   // matches Volume 1 Part II §10.4's
                                                          // RetryAttempt/StepStatus vocabulary
                                                          // and the LogModule's LogRule severity
                                                          // (Volume 1's original prototype), never
                                                          // a separately-invented severity scale
  category: FailureCategory;
  affectedComponent: string;
  summary: string;                      // one-sentence, human-readable
  suggestedNextStep: string;
  rootCauseAnalysisRecommended: boolean; // signals to the caller whether Section 12 is warranted
}

type FailureCategory =
  | 'target-system-error' | 'network-timeout' | 'assertion-mismatch'
  | 'selector-not-found' | 'auth-failure' | 'data-dependency-missing' | 'unknown';

This is a direct, typed evolution of the original prototype's triageLogs output shape (severity, likelyRootCause, affectedComponent, suggestedNextStep), now formalized with a closed FailureCategory enum (replacing the prototype's free-text likelyRootCause field, which becomes this capability's summary) — the closed-enum change is a direct application of Section 3.4's "structured output required, always" principle, since a free-text root-cause field is exactly the kind of unstructured output this volume's design discipline has consistently moved away from.

11.3 Two distinct inputs — step failure and log batch
typescript
type FailureAnalysisInput =
  | { mode: 'stepFailure'; stepResult: StepResultDto; flowContext: FlowContextSummary }
  | { mode: 'logBatch'; logText: string; sourceContext: string };

The 'stepFailure' mode serves the automatic, eager-triage case (every failed StepResult, per Volume 1, Part II, Section 12.6's event-consumer table, could trigger this capability as an additional Event Bus consumer reacting to a failed outcome, surfaced immediately in the run report). The 'logBatch' mode serves the Logs module's direct triage capability (Volume 0, Section 14.9), operating on raw log text independent of any specific FlowRun.

11.4 Why eager, automatic triage for every failure — and why it's still optional

Unlike Sections 7–10, which are always explicitly user-invoked (a user asks for flow generation, self-healing is opt-in per flow per Section 10.4), Failure Analysis is specified as an eager, automatic capability — triggered on every failed StepResult by default, not only when a user explicitly requests it, directly serving Section 1.5's "diagnostic acceleration" value mechanism by having the triage already available the instant a user opens a failed run's detail view, rather than making them wait for an on-demand call. This eagerness is still subject to Section 1.4's optionality boundary: it is tenant-configurable to disable (directly reusing NFR-REL-3's AI-optionality infrastructure, Volume 1, Part IV, Section 24.6 area), and its absence never blocks or delays the underlying StepResult from being recorded and reported — Failure Analysis output, when present, is an additive annotation on an already-complete report, never a gate on report availability, consistent with Volume 1, Part III, Section 19's fail-fast execution loop never waiting on this capability before proceeding.

11.5 Retrieval context this capability consumes

Primarily log pattern history and historical execution outcomes (Section 4.3) — specifically, how similar failures on this tenant's flows were previously categorized and what their eventual actual root cause turned out to be (informed by human feedback where available, Section 11.7) — allowing the category/severity classification to improve in accuracy specifically for this tenant's own recurring failure patterns over time, without any cross-tenant data mixing (Section 4.5's tenant-partitioned vector store enforced identically here).

11.6 Cost-conscious model routing for this specific capability

Forward-referencing Part IV, Section 15: because Failure Analysis runs eagerly and potentially frequently (every failed step, per 11.4), it is a primary candidate for routing to a smaller, cheaper, faster model than the Test Generator (Section 8) or Root Cause Analysis (Section 12) use — this capability's relatively narrow, well-structured output (11.2's closed enums) is well-suited to a lighter model, and Part IV, Section 15's routing policy explicitly names this capability as a default target for cost-optimized routing, directly serving Part IV, Section 16's cost-optimization goals without sacrificing meaningful quality, since the evaluation-set discipline (Section 3.6) validates this routing choice empirically rather than assuming it.

11.7 Human feedback loop — the mechanism that actually improves categorization over time

Volume 2's run-detail view (Section 6.3 there) surfaces a lightweight "was this triage accurate" affordance alongside the FailureAnalysisResult display — feedback that flows back into two places: Part V's evaluation metrics (was this specific categorization correct, aggregated over time) and, indirectly, Section 11.5's retrieval corpus (a human-corrected categorization becomes part of the historical-outcomes corpus future retrievals draw on for this tenant specifically) — this is the concrete mechanism by which this capability's accuracy for a specific tenant improves with usage, without any model fine-tuning or cross-tenant learning, consistent with Section 1.4's permanent data-pooling boundary.

11.8 Evaluation criteria

Category and severity classification accuracy against a human-labeled evaluation set (Section 3.6), plus the rootCauseAnalysisRecommended flag's own precision (does a "yes, dig deeper" recommendation actually correlate with cases where Section 12's deeper analysis reveals something Failure Analysis's own summary didn't already make clear) — this second metric matters specifically because over-recommending deep analysis undermines the two-tier design's entire cost/speed rationale (11.1), while under-recommending it risks a genuinely complex failure being under-diagnosed by a deliberately lightweight first pass.



12. Root Cause Analysis
12.1 Purpose — the second-tier, on-demand diagnostic capability

Where Section 11's Failure Analysis is eager, fast, and narrow (a single failed step, classified quickly), Root Cause Analysis is on-demand, slower, and broad — it correlates across multiple steps, multiple runs, and potentially multiple modules to answer a genuinely harder question: not "what kind of failure is this" but "why did this actually happen, and what's the underlying cause." This directly serves Volume 0's diagnostic-acceleration value mechanism (Section 1.3) at its deepest point, and is the capability most directly responsible for reducing Persona Sofia's and Priya's mean-time-to-diagnose (Volume 0, Part I, Section 6.2's metric).

12.2 Output type
typescript
interface RootCauseAnalysisResult {
  rootCause: string;                    // human-readable narrative, structured per 12.4
  contributingFactors: ContributingFactor[];
  correlatedFailures: CorrelatedFailure[];   // other steps/runs sharing the likely cause
  confidence: number;
  recommendedActions: RecommendedAction[];
}

interface ContributingFactor {
  description: string;
  evidenceSource: 'log-pattern' | 'historical-correlation' | 'step-sequence' | 'timing-analysis';
  weight: number;                        // relative contribution, not independently meaningful
                                             // in isolation — see 12.4
}

interface CorrelatedFailure {
  flowRunId: string;
  stepResultId: string;
  similarityScore: number;
}

interface RecommendedAction {
  action: string;
  actionKind: 'fix-flow' | 'fix-target-system' | 'adjust-assertion' | 'investigate-further';
}
12.3 When this capability is invoked — always on-demand, never eager

Unlike Section 11's default-eager triage, Root Cause Analysis is invoked only explicitly — either directly by a user (a "dig deeper" action in Volume 2's run-detail view, Section 6.3) or automatically when Section 11's rootCauseAnalysisRecommended flag is true and the tenant has configured automatic escalation (a tenant-level setting, not a platform default, per Section 1.4's optionality discipline applied per-capability, not just per-volume). This on-demand-by-default posture is a direct cost-optimization decision (Part IV, Section 16 will elaborate): this capability's broader retrieval scope (12.5) and typically larger model requirement (Part IV, Section 15's routing) make it meaningfully more expensive per invocation than Section 11's lightweight triage, and running it eagerly for every failure would be a poor cost/value tradeoff for the majority of failures Section 11's faster triage already explains adequately.

12.4 Narrative construction — grounded claims only, never speculative fabrication

The rootCause narrative field is constructed with an explicit prompt-template constraint (Section 3.4's structured-output discipline extended to narrative-shaped output specifically): every claim in the narrative must trace to at least one ContributingFactor with a concrete evidenceSource — the model is instructed, and the output is validated (Section 3.5), to never assert a root cause with no corresponding evidence entry. This is a direct, deliberate guard against a known LLM failure mode (confident-sounding but ungrounded causal narratives) that matters more for this capability than almost any other in this volume, since a root-cause explanation is exactly the kind of output a time-pressured engineer (Priya, mid-incident) might act on without independently re-verifying — Part V, Section 17's guardrails treat unsupported causal claims in this capability's output as a specific, named check, not just general output-quality monitoring.

12.5 Retrieval context — the broadest in this volume

Root Cause Analysis draws on all four corpora from Section 4.3 simultaneously — module/action documentation (does this failure pattern match a known limitation or common misconfiguration of the target module), historical flow examples (has a similarly-structured flow had similar issues), historical execution outcomes (has this exact tenant seen this failure signature before, and if so what was it eventually determined to be), and log pattern history (does the surrounding log context, beyond just the failed step itself, suggest a broader system issue) — this breadth is precisely why this capability is the most expensive in this Part and the strongest candidate for on-demand-only invocation (12.3).

12.6 Cross-step and cross-run correlation

correlatedFailures (12.2) is populated by a similarity search (Section 4.6's retrieval pipeline, applied here specifically against the historical-execution-outcomes corpus) identifying other StepResults — potentially from entirely different FlowRuns, even different Flows — that share a likely common cause with the failure under analysis. This is the capability most directly responsible for answering a question no single-run view could answer alone: "is this an isolated failure, or part of a broader pattern" — directly feeding Volume 2's FailureClusterDrilldown component (Section 9.3 there), which was originally specified against Volume 1's DrillDownFailureSignatureQuery (Volume 1, Part II, Section 14.4) using purely statistical/textual signature clustering; this capability offers a richer, causally-reasoned alternative or complement to that purely statistical approach, worth noting as a concrete example of where this volume's AI capability meaningfully extends, rather than merely accelerates, something Volume 1 already specified in simpler form.

12.7 Recommended actions — always advisory, categorized by who acts on them

recommendedActions (12.2) are explicitly categorized (fix-flow vs. fix-target-system vs. adjust-assertion vs. investigate-further) specifically because different categories imply different next actors — a fix-target-system recommendation is information for a team Nexus has no direct relationship with (the tenant's own backend team), while fix-flow/adjust-assertion are directly actionable within Nexus itself (potentially, in a future capability, even pre-populating a suggested flow edit — explicitly out of this volume's GA scope, per Section 1.4's autonomy boundary: this capability recommends, it never constructs or proposes an actual flow modification the way Section 8's Test Generator does for new-flow generation). This categorization is what keeps recommendedActions genuinely useful rather than a generic, one-size-fits-all "investigate this" suggestion.

12.8 Evaluation criteria

Given 12.4's grounding requirement, evaluation specifically measures claim-evidence alignment (does every asserted contributing factor trace to real evidence, verified against a held-out evaluation set with known-correct root causes) as a distinct metric from general narrative quality — directly analogous to Section 8.8's "structural validity versus semantic fidelity" split, applied here as "grounded versus fabricated" instead, and treated as the higher-priority of the two metrics given this capability's specific vulnerability to confident-sounding fabrication (12.4).



13. Report Summarization
13.1 Purpose — serving Persona Sofia specifically

Where Sections 11–12 serve the diagnostic-acceleration need of an engineer investigating a specific failure (Priya, primarily), this capability serves Persona Sofia's distinct need (Volume 0, Section 10.3; UC-5): turning accumulated report and trend data into a digestible narrative suitable for a leadership review, and — per Volume 0's Future Vision, Section 23.3 — eventually supporting natural-language querying of the reporting layer ("why did mobile pass rate drop last week"). This section specifies both the GA-scoped capability (narrative summarization of existing report data) and the architecturally-anticipated-but-not-GA natural-language query extension, following this volume's established practice of clearly separating the two (Section 5.5 already did this for tenant pattern memory).

13.2 Output type — GA scope
typescript
interface ReportSummaryResult {
  summary: string;                      // 2-4 sentence executive summary
  keyFindings: KeyFinding[];
  trendDirection: 'improving' | 'stable' | 'degrading' | 'insufficient-data';
  suggestedFocusAreas: string[];         // e.g., "Mobile module flake rate" — names a
                                            // dimension worth attention, never a specific
                                            // prescribed fix (this capability summarizes
                                            // and highlights; it does not diagnose root
                                            // cause — that remains Section 12's job)
}

interface KeyFinding {
  finding: string;
  supportingMetric: string;              // e.g., "flake rate: 3.2% (up from 1.1%)" —
                                             // every finding traces to an actual number
                                             // from Volume 1's reporting queries, never
                                             // an unsupported qualitative claim
}
13.3 Input — read-only over Volume 1's existing reporting queries

This capability's input is constructed entirely from Volume 1's already-specified reporting query results (Part II, Section 14.4 — GetPassRateTrendQuery, GetFlakeRateQuery, DrillDownFailureSignatureQuery, GetSelfHealAcceptanceRateQuery) over a caller-specified date range — this capability performs no independent data access, it summarizes data Volume 1's existing, already-built reporting infrastructure provides. This is a deliberate, minimal-footprint design: Report Summarization adds a narrative layer on top of Volume 1's reporting system, it does not duplicate or bypass it, directly consistent with Section 1.2's framing that this volume produces outputs consuming Volume 1's existing model rather than introducing parallel data infrastructure.

13.4 Every claim traces to a real number — the same grounding discipline as Section 12

Directly extending Section 12.4's grounding requirement to this capability: every KeyFinding.finding must have a corresponding supportingMetric sourced from the actual query results in 13.3, never a qualitative claim without a number behind it. This matters specifically for this capability's audience — Sofia is explicitly walking into a leadership review with this content (Volume 0, UC-5), and an ungrounded claim discovered as wrong in front of leadership is a direct, serious trust failure for exactly the persona this document set has repeatedly identified as needing a "trustworthy reporting surface" (Volume 2, Section 1.6) above all else.

13.5 Retrieval context — narrower than Root Cause Analysis, historical-trend-focused

This capability's retrieval (Section 4) draws primarily on historical execution outcomes across the requested date range — but unlike Section 12's cross-run correlation search, Report Summarization's retrieval is deliberately time-bounded and aggregate-focused (matching Volume 1, Part V, Section 23.5's TrendAggregateReadModel batch-consistency window) rather than searching for individual similar failures — this capability summarizes patterns over time, not individual incidents, and its retrieval scope is designed accordingly.

13.6 The GA scope stops at structured summarization — natural-language querying is explicitly Future Vision

Consistent with Section 1.7's stated depth philosophy (rigor about constraints, not relaxing them for ambition's sake): this section's GA-scoped capability takes a date range as input and produces a summary — it does not yet accept an arbitrary natural-language question ("why did mobile pass rate drop last week") and resolve it into the appropriate underlying queries dynamically. Volume 0's Future Vision (Section 23.3) named this natural-language-query capability explicitly as a plausible extension; this volume names the architectural seam for it (a query-resolution stage that would sit before Section 13.2's summarization, translating a natural-language question into the specific GetPassRateTrendQuery/GetFlakeRateQuery parameters needed to answer it — architecturally similar in spirit to Section 7's Requirement Parser, but resolving to reporting queries rather than flow steps) without building it now, exactly matching the honesty discipline this document set applied to Volume 2's offline support (Section 16.1 there) and this volume's own tenant pattern memory (Section 5.5).

13.7 Delivery surface

ReportSummaryResult is surfaced in Volume 2's reporting dashboard (Section 6.5 there) as an optional, explicitly-requested addition to the existing PassRateTrendChart/FlakeRateWidget components — never replacing the underlying numeric/chart data those components already render, consistent with UX Principle 1.2's "never a fiction of the real state": the narrative summary is presented alongside the real numbers it was derived from (13.4's grounding requirement made visually concrete), letting Sofia verify the AI's characterization against the actual chart in the same view, never requiring blind trust in the narrative alone.

13.8 Evaluation criteria

Grounding accuracy (every KeyFinding traces to a real, correctly-cited metric, verified automatically since the underlying query results are known ground truth — a stronger, more mechanically-verifiable check than Section 12's evaluation, since this capability's inputs are exact numbers rather than ambiguous log evidence) and, separately, a human-rated usefulness score (would this summary genuinely save Sofia preparation time for a real leadership review, evaluated periodically with actual design-partner feedback per Volume 0, Section 8.4's validation discipline) — the second metric acknowledging that a summary can be perfectly grounded and still not be useful, a distinct failure mode worth tracking independently.


____________________________________________________________________________________________________________________________________________________



14. AI Provider Abstraction
14.1 Purpose — fulfilling Volume 0's locked commitment

Volume 0, Section 18.5 committed to "provider abstraction, not lock-in" as a strategic requirement, and Volume 0, Section 21.7 locked this as a technology decision: "no component outside the AI module's provider-adapter layer should have any provider-specific dependency." This section is where that commitment becomes a concrete interface — the layer Section 2.4's Planning Engine pipeline invokes at step 5 (Invocation).

14.2 The provider interface
typescript
interface AiProvider {
  id: string;                          // e.g., "anthropic", "self-hosted-llama"
  generateStructured<TOutput>(request: StructuredGenerationRequest<TOutput>): Promise<ProviderResponse<TOutput>>;
  embed(text: string): Promise<number[]>;   // Section 4.6's embedding step — same
                                                abstraction covers both generation and
                                                embedding models, never two separate
                                                un-unified provider concepts
  capabilities: ProviderCapabilities;    // what this provider actually supports —
                                             see 14.5
}

interface StructuredGenerationRequest<TOutput> {
  systemPrompt: string;                  // rendered from Section 3's PromptTemplate
  userContent: string;
  outputSchema: JSONSchema;
  maxTokens: number;
  timeout: number;                       // Volume 1 Part VI §38.5's 15s NFR-PERF-4 default,
                                             overridable per Part IV §15's routing policy
}

interface ProviderResponse<TOutput> {
  parsed: TOutput | null;                // null if schema validation failed, Section 3.5
  raw: string;
  tokenUsage: { input: number; output: number };
  latencyMs: number;
}

Every capability engine in Part III interacts with this interface exclusively — no capability engine imports a provider-specific SDK type or references a provider-specific API shape directly, mirroring exactly the discipline Volume 1, Part V, Section 36.3 established for Nexus.Domain's zero-outward-dependency rule, applied here to this volume's capability engines relative to any specific AI vendor.

14.3 Why abstraction lives here, not inside each capability engine

A tempting alternative is letting each capability engine (Part III) call its preferred provider's SDK directly, with abstraction as an optional, later refactor. This volume rejects that design from the start, for a reason directly traceable to Volume 0, Section 18.5's stated rationale: enterprise customers with existing model-provider relationships or compliance constraints (Persona Elena's segment, Volume 0 Section 10.6) must be able to substitute their preferred provider, and retrofitting abstraction after seven capability engines have each grown provider-specific assumptions would be significantly more expensive than establishing the boundary correctly from this volume's first capability-engine specification (Section 7) forward — directly consistent with Volume 1, Architecture Principle 20.2's "contract stability over implementation convenience" applied here at this volume's founding, not as an afterthought.

14.4 Structured output enforcement at the provider-adapter layer

Different providers have different native mechanisms for constrained/structured output (native JSON-schema-constrained generation, function-calling-style tool use, or, for providers lacking native support, prompt-engineered JSON output with post-hoc parsing) — this variance is entirely absorbed inside each provider adapter's generateStructured implementation, never leaking into Section 3's PromptTemplate.outputSchema design or any capability engine's expectations. A capability engine receives either a correctly-typed TOutput or null (14.2), regardless of which underlying mechanism a specific provider used to get there — this is the single most valuable thing this abstraction layer does, since Section 3.5's "no lenient coercion" rule depends on every provider adapter honoring the same strict pass/fail contract uniformly.

14.5 ProviderCapabilities — honest declaration, not assumed uniformity
typescript
interface ProviderCapabilities {
  supportsNativeStructuredOutput: boolean;
  maxContextTokens: number;
  supportsEmbedding: boolean;
  averageLatencyClass: 'fast' | 'standard' | 'slow';
}

Not every provider supports every capability this volume needs (14.2's embed method, for instance, may be unavailable on a provider used only for generation) — ProviderCapabilities is declared explicitly and checked by Part IV, Section 15's routing layer before a provider is selected for a given request, rather than assumed uniform and discovered to be missing only at runtime failure. This is a direct application of Volume 1, Architecture Principle 20.4 (fail loud, fail informatively) to provider selection specifically: a misconfigured routing rule that would send an embedding request to a generation-only provider is caught at routing-decision time, not as a confusing downstream API error.

14.6 Self-hosted / open-weight model support

Per Volume 0, Section 18.5's explicit scoping ("self-hosted/open-weight model support is a Should-priority, not Must-priority, capability for GA, but... adding this support later is a configuration change, not an architecture change"): this interface is designed so a self-hosted model server (exposing an OpenAI-compatible or similarly standard API surface) is just another AiProvider implementation — no different in kind from a commercial hosted provider from the Planning Engine's or any capability engine's perspective. This directly serves the air-gapped, self-hosted deployment scenario Volume 1, Part VI, Section 37.7 already specified at the infrastructure level: an air-gapped Nexus deployment with AI enabled (as opposed to Volume 1's more common AI-disabled air-gapped case) is architecturally supported by pointing this abstraction at a self-hosted provider, requiring zero changes to this volume's capability engines.

14.7 Provider health and circuit-breaking

Each AiProvider is wrapped with a circuit breaker (tracking recent success/failure/timeout rates) — a provider experiencing sustained failures is temporarily excluded from Part IV, Section 15's routing decisions, with automatic recovery probing, rather than every request continuing to attempt and time out against a degraded provider. This directly extends Volume 1, Part VI, Section 38.5's per-call timeout enforcement with a higher-level, cross-request health signal, feeding the same nexus.ai.request.timeout metric Volume 1, Part IV, Section 29.4 already specified, now informing routing decisions in addition to the graceful-degradation behavior Volume 1 already established at the individual-request level.

14.8 Authentication and credential handling

Provider API credentials are resolved through the same ICredentialResolver pattern Volume 1, Part III, Section 18.4 established for module execution — this volume's provider adapters are, in this specific respect, treated identically to a built-in Nexus module accessing a scoped secret, never given broader credential access than the specific provider connection requires, and never logging or persisting a credential value, directly extending Volume 1, Part IV, Section 28.5's sensitive-data logging denylist to this volume's provider-adapter layer explicitly.

14.9 Testing provider adapters

Every AiProvider implementation is tested against a shared contract-test suite (asserting every implementation correctly handles the full StructuredGenerationRequest/ProviderResponse contract, including timeout behavior and malformed-response handling) before being eligible for production routing — directly mirroring Volume 1, Part VI, Section 40.6's testing-tier discipline, applied here to ensure a new provider adapter (added to support a new commercial provider or a self-hosted deployment) cannot silently violate the uniform contract Section 14.2 defines, which every capability engine in Part III depends on holding exactly.



15. Multi-model Routing
15.1 Purpose — the decision layer between capabilities and providers

Section 14 specified the uniform interface every provider implements. This section specifies the policy that decides which provider and which specific model a given request should use — Section 2.4's Planning Engine pipeline step 4, sitting between context retrieval (step 2, prompt assembly step 3) and invocation (step 5).

15.2 Routing dimensions

A routing decision considers four inputs, in order of precedence:

typescript
interface RoutingPolicy {
  resolve(request: AiPlanningRequest): ModelSelection;
}

interface ModelSelection {
  providerId: string;
  modelId: string;
  fallbackChain: { providerId: string; modelId: string }[];   // Section 15.6
}
Tenant configuration (highest precedence) — an enterprise tenant may pin specific capabilities to a specific provider/model (e.g., a compliance requirement mandating a specific certified provider), overriding platform defaults entirely.
Capability-specific default policy — per Section 11.6's example, Failure Analysis defaults to a smaller/faster/cheaper model tier; Test Generator (Section 8) and Root Cause Analysis (Section 12), given their higher output-quality bar and broader context needs, default to a larger model tier.
Provider health (Section 14.7's circuit-breaker state) — a provider currently circuit-broken is excluded from consideration regardless of the above two inputs' preference.
Cost policy (Part IV, Section 16) — where multiple providers/models satisfy the above three inputs at acceptable quality, the lowest-cost option is selected, per Section 16's budget-governance rules.
15.3 Model tiers — a deliberately small, closed set
typescript
type ModelTier = 'lightweight' | 'standard' | 'advanced';

Rather than routing decisions referencing specific model names directly throughout this volume's specification (which would create a maintenance burden every time a provider releases a new model generation), every capability's default policy (15.2, precedence 2) references one of these three tiers, with a separate, independently-updatable mapping table (ModelTier → actual providerId/modelId) maintained as operational configuration, not embedded in this volume's architectural specification — directly mirroring Section 3.7's decision to store prompts as versioned configuration rather than compiled code, applied here to model selection for the same "decouple frequently-changing configuration from infrequently-changing architecture" reasoning.

15.4 Capability-to-tier default mapping
Capability (Part III) Default tier  Rationale
Requirement Parser (§7) Standard  Moderate reasoning demand, structured but not deeply complex output
Test Generator (§8) Advanced  Highest output-quality bar — a structurally invalid or semantically wrong generated flow has real downstream cost
Locator Generator (§9)  Standard  Well-scoped task, benefits from reasoning quality but not the most demanding in this Part
Self-Healing (§10)  (inherits Locator Generator's tier — no independent model call, per §10.1's orchestration-only design)  —
Failure Analysis (§11)  Lightweight High frequency, narrow closed-enum output, explicitly named in §11.6 as a cost-optimization target
Root Cause Analysis (§12) Advanced  Broadest context, highest grounding-accuracy bar (§12.4)
Report Summarization (§13)  Standard  Grounded-numeric-summary task, moderate complexity

This table is the concrete, capability-by-capability resolution of Volume 0, Section 18.3's three-mechanism framework applied to cost/quality tradeoffs specifically — authoring-acceleration and diagnostic-acceleration capabilities that carry higher downstream risk if wrong (Test Generator, Root Cause Analysis) default to the highest-quality tier; the highest-frequency, narrowest-output capability (Failure Analysis) defaults to the most cost-efficient tier, with Part V's evaluation metrics (Section 3.6's evaluation-set discipline) empirically validating that this tier assignment doesn't sacrifice acceptable quality for any specific capability, rather than this table being permanently fixed by initial judgment alone.

15.5 Tenant override granularity

A tenant may override tier assignment per-capability (not only globally) — a tenant especially sensitive to Self-Healing accuracy might upgrade that capability to advanced while accepting lightweight defaults elsewhere, directly serving the same per-tenant risk-tolerance configurability Section 10.4 already established for the self-heal confidence threshold specifically, now generalized as a routing-policy capability spanning every engine in Part III.

15.6 Fallback chains — provider-outage resilience

ModelSelection.fallbackChain (15.2) specifies an ordered list of alternative provider/model pairs to attempt if the primary selection is circuit-broken (Section 14.7) or times out (Volume 1, Part VI, Section 38.5's 15-second budget) — critically, a fallback is only attempted if it satisfies the same ProviderCapabilities requirements (Section 14.5) the primary selection was chosen for, never a lower-capability substitute silently accepted just to return something. If no viable fallback exists, the Planning Engine returns AiUnavailableError (Section 2.5) exactly as if no provider had ever been configured — graceful degradation to the non-AI path, never a degraded-but-still-attempted AI response the calling capability engine's evaluation criteria (Part III, each section's closing subsection) never accounted for.

15.7 Routing decisions are recorded, not just applied

Every ModelSelection actually used is recorded on the resulting AiPlanningResult.modelUsed field (Section 2.3) — this is not incidental logging; it is the specific data Section 3.3's prompt-version correlation and Part V's evaluation metrics depend on to answer "did this specific model/prompt/tier combination perform well," making routing policy changes (15.4's table, or a tenant's override, 15.5) empirically evaluable over time rather than a permanent, unverified assumption.

15.8 Multi-model routing and cost — the direct connection to Section 16

This section's precedence-4 cost consideration (15.2) is deliberately underspecified here and fully elaborated in Section 16 — this section owns which dimensions influence a routing decision and in what order; Section 16 owns the actual budget mechanics (per-tenant spend tracking, alerting, hard limits) that precedence-4 consults. This split mirrors the same "what versus how" separation Volume 2, Section 11 and Section 12 used for the Workflow Designer (product behavior versus React Flow implementation) — routing policy is the product-level decision; cost governance is the infrastructure enforcing it.



16. Cost Optimization
16.1 Purpose — fulfilling Volume 0's cost-governance commitment

Volume 0, Section 18.5 named "cost governance is a first-class product capability, not an operational afterthought," and Volume 0, Section 16.2 scoped "managed AI-provider integration and cost-governance tooling" as an enterprise-licensing-tier capability. This section specifies the mechanics both of those commitments depend on — how AI spend is tracked, budgeted, and optimized, at both the platform-operational level and the tenant-visible level.

16.2 Cost tracking — every invocation, not sampled

Every provider invocation (Section 14.2's generateStructured/embed calls) records exact token usage (ProviderResponse.tokenUsage) and computed cost (token usage × the specific provider/model's known per-token pricing, maintained as operational configuration alongside Section 15.3's tier-to-model mapping) — this is exhaustive, not sampled, tracking, directly because Section 16.4's per-tenant budget enforcement and Section 16.6's cost-attribution reporting both require complete data, not statistically-estimated data, to be trustworthy at the level Persona Elena's and Sofia's review needs (Volume 0, Sections 10.3, 10.6) demand.

typescript
interface AiCostRecord {
  tenantId: TenantId;
  capability: AiPlanningRequest['capability'];
  providerId: string;
  modelId: string;
  tokenUsage: { input: number; output: number };
  computedCost: number;                  // in a normalized currency unit
  timestamp: Date;
  flowRunId?: string;                     // correlates back to Volume 1's FlowRun,
                                              // when the invocation originated from
                                              // one (e.g., a self-heal during execution,
                                              // versus a standalone flow-generation request)
}

This is the same costRecord field already named on AiPlanningResult in Section 2.3 — this section specifies its full shape and what happens to it after the Planning Engine populates it.

16.3 Where cost records are persisted and how they relate to Volume 1's audit trail

AiCostRecords are persisted to their own store (not Volume 1's audit_entries table, Volume 1 Part V Section 32.5, since cost records are high-frequency, append-only operational data with different query patterns — trend aggregation, budget-threshold checking — than the compliance-oriented audit trail), but every AI-assisted command that Volume 1 already audits (Volume 1, Part IV, Section 27's AuditableCommandHandler base class, applied to GenerateFlowFromDescriptionCommand, RequestSelectorHealCommand) additionally references its associated AiCostRecord(s) via flowRunId/session correlation — giving a complete picture (what happened, per Volume 1's audit trail; what it cost, per this volume's cost records) without merging two data stores that Volume 1, Part IV, Section 27.7 already established should remain conceptually and physically distinct (that section's Audit-vs-Logging distinction, extended here to a third, similarly-distinct concern: cost).

16.4 Per-tenant budget enforcement
typescript
interface TenantAiBudget {
  tenantId: TenantId;
  monthlyLimit: number | null;           // null = unlimited (typically self-hosted-with-
                                             // own-provider-key tenants, who bear their own
                                             // provider cost directly, per Volume 0 §16.2's
                                             // "self-hosted users bring their own LLM API key"
  currentSpend: number;                   // rolling, computed from Section 16.2's records
  alertThresholds: number[];              // e.g., [0.5, 0.8, 1.0] — percentage-of-limit
                                             // alerting, not just a hard cutoff at 100%
  onLimitExceeded: 'block' | 'degrade-to-lightweight-tier' | 'alert-only';
}

The onLimitExceeded policy is deliberately tenant-configurable rather than a single platform-wide behavior: 'block' (AI capabilities become unavailable, gracefully degrading per Section 2.5's AiUnavailableError path — never a platform failure, consistent with NFR-REL-3) suits a tenant prioritizing predictable cost; 'degrade-to-lightweight-tier' (Section 15.4's tier assignments are overridden downward, trading quality for continued availability) suits a tenant prioritizing continuity; 'alert-only' suits a tenant that wants visibility without any automatic behavior change. This range of policies is a direct, concrete implementation of Volume 0, Section 18.5's "customers will require visibility and control over it as a condition of adoption at scale."

16.5 Optimization technique 1 — prompt and context caching

Where a capability's retrieval context (Part II) or prompt-template rendering (Section 3) is identical or near-identical across nearby requests (e.g., repeated Failure Analysis calls against similar log patterns in a short window), a short-lived cache (keyed by a hash of the rendered prompt plus retrieval context, scoped strictly per-tenant per Section 4.5's isolation discipline extended here) avoids a redundant provider call entirely — this is a narrow, specific optimization, not a general response-memoization layer, since most capability inputs (a specific broken selector, a specific natural-language description) are unique enough that caching's hit rate would otherwise be low; this technique is deliberately scoped to the capabilities and scenarios (primarily Failure Analysis, Section 11, given its high invocation frequency per 11.6) where repeated near-identical inputs are actually common.

16.6 Optimization technique 2 — context trimming and token budgets

Directly extending Section 4.6's retrieval-pipeline step 5 ("an explicit token budget per capability"): every capability's PromptTemplate (Section 3.2) declares a maximum context-token budget, and the retrieval pipeline trims (via the re-ranking step, Section 4.6 step 4) retrieved context to fit that budget before assembly — rather than sending maximally broad context on every request and letting cost scale unnecessarily with retrieval breadth. This budget is tuned per capability against Section 3.6's evaluation-set discipline specifically: a capability's token budget is reduced only as far as evaluation metrics confirm no meaningful quality regression results, never cut purely for cost savings without that empirical check.

16.7 Optimization technique 3 — tier-appropriate routing as the primary lever

Restating Section 15.4's table as this section's single largest cost lever: routing Failure Analysis (the highest-frequency capability, per Section 11.4's eager-by-default design) to the lightweight tier by default is, in practice, the most impactful cost-optimization decision in this entire volume — a single tier misassignment on a high-frequency capability affects total platform AI spend far more than any prompt-trimming or caching refinement, which is why Section 15.4's table is treated as a cost-critical artifact requiring the same empirical-validation discipline (evaluation sets, Section 3.6) as any other quality-affecting decision, not a one-time judgment call made once and forgotten.

16.8 Cost visibility for enterprise tenants

Directly implementing Volume 0, Section 16.2's "managed AI-provider integration and cost-governance tooling" enterprise capability: a dedicated reporting view (a Volume 2 extension to the reporting-audit feature module, Volume 2 Section 6.5, gated behind the enterprise licensing tier per Volume 0's licensing boundary) surfaces AiCostRecord data aggregated by capability, by tenant-configured budget period, and by trend over time — giving Sofia and Elena's segment the same trustworthy, numbers-grounded reporting experience (directly reusing Section 13's Report Summarization capability, applied reflexively to AI cost data itself) that Volume 0's Business Objective 5 requires for adoption at enterprise scale.

16.9 Cost optimization and quality — the tension this section does not pretend away

Consistent with this document set's established honesty discipline: every optimization technique in this section (caching, trimming, tier-downgrading) carries a real, non-zero quality risk, and this section does not claim otherwise. The governing rule, restated from Section 16.6's specific case and generalized: no cost-optimization technique is applied without first being validated against Section 3.6's evaluation-set discipline — cost and quality are managed as two explicitly-tracked, jointly-optimized dimensions (Part V, Section 19's evaluation metrics track both), never treated as though minimizing cost were free or as though quality could be assumed constant while cost is optimized without verification.


____________________________________________________________________________________________________________________________________________________



17. Guardrails
17.1 Purpose — the section Volume 0 named as Nexus's actual moat

Volume 0, Section 18.6 was explicit: "a unified guardrail strategy across all six modules' AI touchpoints is something a retrofitted competitor structurally cannot easily replicate." This section is where that strategic claim becomes a concrete, specified system — the full detail behind Section 2.4's pipeline steps 1 (input pre-check) and 6 (output validation), which every one of Part III's seven capability engines passes through without exception.

17.2 Guardrail check taxonomy
typescript
interface GuardrailCheck {
  id: string;
  stage: 'input' | 'output';
  category: GuardrailCategory;
  severity: 'block' | 'warn' | 'flag-for-review';
  evaluate(payload: GuardrailPayload): Promise<GuardrailCheckResult>;
}

type GuardrailCategory =
  | 'prompt-injection' | 'pii-detection' | 'content-safety'
  | 'grounding-verification' | 'schema-conformance' | 'tenant-policy';

interface GuardrailCheckResult {
  checkId: string;
  passed: boolean;
  details?: string;
}

Every GuardrailCheckResult produced during a single Planning Engine invocation (Section 2.4) is collected into AiPlanningResult.guardrailChecks (Section 2.3) — populated on every request, every capability, unconditionally, exactly as Section 2.3 originally specified.

17.3 Input-stage checks — cheapest point to enforce a boundary

Referencing Section 2.4's step 1: input-stage checks run before any provider call, catching problems at the point where rejecting a request costs nothing and risks nothing.

Prompt-injection resistance: every piece of retrieved context (Part II) and user-supplied input is scanned for patterns characteristic of instruction-injection attempts (text embedded in, for instance, a target system's page HTML during self-healing, Section 9.3's 'heal' mode, that tries to redirect the model's behavior) — detected content is stripped or the request is flagged for review (per severity, 17.2), never silently passed through unmodified. This matters specifically because Section 9.3's currentPageHtml input is, by definition, content Nexus does not control — it's whatever the tenant's target system happens to render, making this capability's input-stage guardrail the single highest-exposure injection surface in this entire volume.
PII detection: input content is scanned for patterns matching common PII shapes (email addresses, credit-card-like number sequences, government-ID-like patterns) before being included in any prompt sent to an external provider — a detected match triggers redaction (replacing the matched span with a placeholder) rather than blocking the request outright by default, tenant-configurable toward stricter blocking behavior per 17.6.
Tenant policy enforcement: a tenant's configured restrictions (e.g., "never send raw log content to any AI provider," a stricter posture than the platform default) are checked here, before retrieval even completes — directly extending Volume 1, Part IV, Section 26's tenant-isolation discipline into AI-specific policy space.
17.4 Output-stage checks — catching what the model itself produced

Referencing Section 2.4's step 6:

Schema conformance: Section 3.5's structural-validation rule, formalized here as the first and most basic output-stage guardrail — a response failing to parse against outputSchema never proceeds further.
Grounding verification: directly implementing Section 12.4's and Section 13.4's "every claim traces to real evidence" requirement, generalized here as a reusable guardrail check applicable to any capability producing narrative or causal output (currently Root Cause Analysis and Report Summarization specifically, per those sections) — an automated check cross-referencing claimed evidence citations against the actual RetrievalContext (Part II) that was provided, flagging any claim with no traceable source.
Content safety: standard harmful-content screening (the same category of check any responsible AI-product deployment applies) — included here for completeness, though Nexus's capability engines' narrow, structured-output nature (Section 3.4) makes this category of risk meaningfully lower than in an open-ended conversational product, since there's little surface for a capability engine to produce, say, harmful creative content when its output schema is a closed FailureCategory enum or a Flow definition.
17.5 Marketplace guardrail packs — the extension mechanism, made concrete

Directly implementing Section 6.2's third Knowledge Base content type: a guardrail pack is a bundle of additional GuardrailCheck implementations (17.2), reviewed and certified per Volume 0, Section 17.3's tiering (Enterprise-certified specifically, given the elevated trust required to add checks that run against every AI-assisted request for an installing tenant), installed per-tenant exactly like a marketplace module (Volume 1, Part III, Section 18.8's installation flow, structurally mirrored here for guardrail packs rather than automation modules). A healthcare-focused pack, for instance, might add a PHI-specific detection check stricter than Section 17.3's general PII pattern-matching — additive to the platform's baseline checks, never replacing or weakening them, a hard rule enforced by the Planning Engine only ever running the union of platform-baseline checks and installed-pack checks, never allowing an installed pack to disable a baseline check.

17.6 Guardrail configuration — tenant-adjustable severity, never tenant-removable checks

A tenant may adjust a check's severity (e.g., upgrading PII detection, 17.3, from warn to block) but cannot disable a platform-baseline check entirely — this asymmetry (adjustable strictness, non-removable presence) is deliberate: Volume 0's trust posture (Business Objective 5) depends on every tenant's AI-assisted requests passing through the same baseline guardrail floor, regardless of that tenant's own configured tolerance, while still respecting that different tenants have legitimately different strictness needs above that floor.

17.7 Guardrail check failures — what happens next

A block-severity failure at any stage terminates the Planning Engine's pipeline immediately, returning a structured GuardrailBlockedError (a sibling to Section 2.5's AiUnavailableError, handled identically by calling Volume 1 command handlers — falling back to normal, non-AI failure handling). A warn-severity failure allows the pipeline to continue but attaches the warning to AiPlanningResult.guardrailChecks for downstream visibility (surfaced in Volume 2's AI-attribution UI, Volume 2 Section 9.3, alongside the AiAttributionBadge). A flag-for-review failure behaves like warn but additionally creates a queued item in a guardrail-review workflow (Volume 1, Part IV, Section 27's audit-adjacent infrastructure, extended here) for periodic human review of edge cases — this three-tier severity model exists specifically so guardrails can be tuned (moving a check from flag-for-review toward block as confidence in its accuracy grows) without a binary "the guardrail system works or doesn't" all-or-nothing posture.

17.8 Guardrails and Part IV's cost/routing interaction

Referencing Section 15.6's fallback-chain design: a block-severity input-stage failure (17.3) prevents any provider call from being made at all — this is, incidentally, also the cheapest possible cost-optimization outcome (Section 16), a case where guardrail enforcement and cost governance align rather than trade off, worth noting explicitly since Section 16.9 was careful to name where cost and quality/safety do trade off; this is a case where they don't.

17.9 Testing guardrails — adversarial, not just functional

Directly extending Volume 2, Section 19.11's plugin-sandbox adversarial-testing discipline to this volume: every guardrail category (17.2) has a maintained adversarial test suite — deliberately crafted inputs designed to attempt to evade detection (a prompt-injection attempt phrased to avoid known-pattern matching, a PII value formatted to dodge a regex) — run continuously in CI, since a guardrail system's real-world value is entirely a function of how well it holds up against inputs specifically designed to defeat it, not just well-behaved test fixtures that were never going to trigger it in the first place.



18. Security
18.1 Purpose and scope — AI-specific security concerns beyond Section 17's guardrails

Section 17 specified content-level guardrails (what the model receives and produces). This section specifies the surrounding security architecture — data flow, credential handling, and deployment posture — treating AI-specific risks as an extension of, never a replacement for, the security architecture Volume 1, Part IV already established. Every subsection here names the specific Volume 1 mechanism it extends, consistent with this volume's practice throughout (Section 1.2).

18.2 Data flow mapping — what leaves the Nexus deployment boundary, and when

Restating and completing Part I, Section 2.5's trust-boundary analysis specifically for AI: the only data that ever crosses the Nexus-to-LLM-provider trust boundary (Volume 1, Part I, Section 2.5) is what a specific capability engine's RetrievalContext and input payload (Part III, each section's input type) explicitly include — never a broader export of tenant data. This is a direct, auditable claim: Section 14.2's StructuredGenerationRequest shape is exhaustive and typed, meaning "what does Nexus send to an AI provider for capability X" is answerable precisely by reading that capability's input type definition (Part III) plus Section 17.3's redaction behavior, not by inspecting runtime behavior empirically.

Capability  Highest-sensitivity data potentially sent to provider Mitigations
Requirement Parser (§7) User's natural-language description (may reference internal system names) PII detection (§17.3); no target-system credentials ever included
Test Generator (§8) Retrieved historical flow examples (tenant's own prior flow structures) Tenant-isolated retrieval (§4.5) — never another tenant's flows
Locator Generator (§9), 'heal' mode Page HTML from the tenant's target system — the highest-exposure case, per §17.3  Prompt-injection screening (§17.3); this is also the strongest argument for self-hosted-provider support (§14.6) for tenants whose target systems render genuinely sensitive content
Failure Analysis (§11), Root Cause Analysis (§12) Log content, StepResult.Data (may contain response bodies from tenant's own APIs) PII/secret redaction (§17.3); NFR-SEC-3-aligned logging discipline (Volume 1 Part IV §28.5) applied to what's sent, not just what's logged locally
Report Summarization (§13)  Aggregate metrics only — no raw execution data  Lowest-sensitivity capability in this volume by design (§13.3's read-only-over-aggregates scope)

This table exists specifically to make Persona Elena's security review (Volume 0, UC-7) tractable without requiring her to independently reverse-engineer this volume's data flow from source code — it is the direct, AI-specific extension of the data-flow documentation Volume 1, Part I, Section 2.3 already provided at the system-context level.

18.3 Credential and secret handling — restating Section 14.8 with the full security frame

Section 14.8 already established that provider credentials resolve through Volume 1's ICredentialResolver pattern. This section adds the reverse direction, which matters equally: tenant target-system credentials must never be included in any AI provider request. This is enforced structurally, not just by convention — every capability engine's input type (Part III) is defined without any credential-shaped field, meaning a capability engine has no way to accidentally include a secret in a prompt even if a calling Volume 1 command handler mistakenly tried to pass one, since the type system rejects it at the call site, directly extending Volume 1, Part II, Section 10.8's strongly-typed-identifier discipline ("this class of bug is eliminated at compile time") to secret-handling specifically.

18.4 Data residency and the AI-disabled deployment guarantee

Directly extending Volume 1's NFR-SEC-4 ("no mandatory outbound internet dependency for core functionality") and NFR-COMP-1 (full data residency, self-hosted): when a tenant's deployment has the AI module disabled (Volume 1, Part IV, Section 24's AI-optionality architecture), this volume's entire provider-abstraction layer (Section 14) is never invoked — verified the same way Volume 1, Part VI, Section 40.2 statically enforces zero outbound calls in that configuration (a build-time/network-policy check, not merely a runtime conditional that could have a bug). When AI is enabled but the tenant requires strict data residency, Section 14.6's self-hosted-provider support is the concrete mechanism satisfying that requirement — the entire data flow in Section 18.2's table stays within the tenant's own infrastructure boundary, never reaching a third-party commercial provider at all.

18.5 Model and provider security review

Before any new AiProvider implementation (Section 14) is eligible for production routing, it undergoes a security review distinct from Section 14.9's functional contract-test suite — verifying the provider's own data-handling commitments (does the provider retain submitted data for model training, what is the provider's own compliance certification posture) are compatible with Volume 0's Business Objective 5 trust commitments. This review's outcome is recorded and surfaced as part of Section 18.2-style documentation available to Persona Elena's review process — a tenant evaluating whether to enable AI at all is evaluating not just Nexus's own architecture but, transitively, whatever provider Nexus or the tenant has configured, and this volume's documentation is designed to make that transitive trust chain inspectable rather than opaque.

18.6 Prompt-injection defense in depth — beyond Section 17.3's detection

Section 17.3 specified detection (scanning for injection patterns). This section adds the complementary, structural mitigation: every PromptTemplate's system prompt (Section 3.2) is constructed so that retrieved/user-supplied content is clearly delineated from instructions (e.g., wrapped in explicit boundary markers the model is instructed to treat as inert data, never as directives) — a standard, structural prompt-engineering mitigation applied uniformly across every capability's template, reviewed as part of Section 3.6's evaluation-set discipline (an injection-resistance test case is a standard, mandatory entry in every capability's evaluation set, not an optional addition). Detection (17.3) and structural mitigation (this section) are treated as complementary layers, consistent with Volume 1, Part IV, Section 26.3's "defense in depth, not a single checkpoint" philosophy, now applied to prompt-injection resistance specifically.

18.7 Incident response — AI-specific extensions to Volume 1's posture

An AI-specific security incident (a confirmed prompt-injection success, a guardrail bypass, a provider-side data-handling concern discovered post-hoc) follows Volume 1's general incident posture (informed by, though not fully detailed within, Volume 1's own scope) with one AI-specific addition: because AiCostRecord (Section 16.2) and guardrailChecks (Section 17.2) together provide a complete, per-invocation forensic trail (what was sent, what was retrieved, what checks fired, what was returned), incident investigation for an AI-related concern is tractable at the individual-request level, not just in aggregate — a direct, practical payoff of Section 2.3's "every field populated on every call, without exception" design discipline, now shown to matter for security-incident response specifically, not only for the evaluation and cost-governance purposes it was originally justified by.

18.8 Relationship to Section 19

This section's data-flow mapping (18.2) and review process (18.5) establish what should be true; Section 19 (Evaluation Metrics) specifies how this volume continuously verifies, in production, that guardrails (Section 17) and this section's security posture are actually holding — closing the loop from architectural intention to measured, ongoing reality, the same closing move this document set has made at the end of every prior volume's trust-related sections.



19. Evaluation Metrics
19.1 Purpose — the section that closes every open evaluation reference in this volume

Every section in Part III ended with an "Evaluation criteria" subsection; Section 3.6 established the evaluation-set discipline those subsections all depend on; Section 15.7 and Section 16.9 both named evaluation as the mechanism validating routing and cost decisions. This closing section of Volume 3 unifies all of it into one coherent evaluation framework — the system that makes every quality claim in this volume a continuously-measured fact, not a one-time design assertion.

19.2 Two evaluation regimes — offline and online
typescript
interface EvaluationRegime {
  offline: OfflineEvaluation;    // Section 19.3 — pre-release, evaluation-set-based
  online: OnlineEvaluation;      // Section 19.4 — production, real-usage-based
}

This split mirrors a distinction familiar from Volume 1's own testing tiers (Part VI, Section 40.6's fast-unit versus slower-integration split) and Volume 2's testing strategy (Section 19.2 there's four-tier model) — offline evaluation is fast, reproducible, and gates releases (19.3); online evaluation is slower to accumulate but measures what actually happens with real tenant usage, which no offline evaluation set can fully substitute for (19.4).

19.3 Offline evaluation — the release gate

Directly formalizing Section 3.6's evaluation-set discipline as a complete system: every capability engine (Part III) and every guardrail category (Part V, Section 17) maintains a versioned evaluation set — curated inputs with human-annotated acceptance criteria, stored and versioned alongside the PromptTemplates (Section 3.7) and RoutingPolicy configuration (Section 15.3) they evaluate. A release (a new prompt version, a new routing-tier assignment, a new or updated guardrail check) runs the full relevant evaluation set and must meet or exceed the previous version's scores on every named metric before promotion to production traffic — directly mirroring Volume 1, Part VI, Section 38.8's load-testing release gate and Volume 2, Section 17.9's Lighthouse CI performance gate, applied here to AI quality specifically.

Capability  Primary offline metric  Threshold discipline
Requirement Parser (§7) Precision/recall on candidate-module and assertion-intent identification  No regression versus prior version
Test Generator (§8) Structural validity rate + semantic-fidelity score  Structural validity: no regression; semantic fidelity: tracked, reviewed
Locator Generator (§9)  Confidence calibration accuracy (§9.7)  Calibration error must stay within a defined band — this is the strictest offline gate in this volume, per §9.7's own reasoning
Self-Healing (§10)  Self-heal acceptance rate (§10.9) Volume 0's named ≥70% target — release-blocking if a change would predictably drop below it
Failure Analysis (§11)  Category/severity classification accuracy No regression
Root Cause Analysis (§12) Claim-evidence alignment (grounding accuracy, §12.8)  No regression — treated as the higher-priority of its two metrics, per §12.8
Report Summarization (§13)  Grounding accuracy (mechanically verifiable, §13.8) Must remain at or near 100%, given this metric's mechanical verifiability
19.4 Online evaluation — production signal

Continuous, real-usage-derived metrics, computed from the same data every capability engine already produces as a matter of course — no separate instrumentation beyond what Sections 2.3, 10.6, 11.7, and 16.2 already specify:

Self-heal acceptance rate (Section 10.9) — Volume 0's named metric, computed continuously in production, not only against the offline evaluation set.
Structural validity rate (Section 8.8) — the percentage of real, production flow-generation requests producing a structurally valid Flow on the first attempt, tracked as a rolling production metric alongside its offline evaluation-set counterpart.
Human feedback signals — Section 11.7's "was this triage accurate" affordance, and equivalent lightweight feedback mechanisms for other capabilities where a human reviewing AI output can cheaply signal correctness, aggregated as an ongoing production quality signal distinct from, and complementary to, the offline evaluation set's necessarily limited coverage.
Guardrail trigger rates (Section 17.2's GuardrailCheckResult data) — tracked by category and severity over time; a rising flag-for-review rate for a specific check, for instance, might indicate either a genuine increase in adversarial activity or a guardrail that's becoming miscalibrated (too sensitive) as usage patterns shift, requiring investigation either way.
19.5 The relationship between online metrics and offline evaluation sets

Online production data is the primary source for growing offline evaluation sets over time — a production case where a human feedback signal (19.4) indicates a capability got something wrong is a strong candidate for addition to that capability's offline evaluation set (Section 19.3), turning a real, encountered failure mode into a permanent regression check. This is the concrete mechanism by which this volume's evaluation infrastructure improves continuously rather than being fixed at whatever the initial evaluation-set authors anticipated — directly analogous to how Volume 1, Part VI, Section 40's coding standards are themselves expected to evolve via the ADR-for-deviations process (Volume 1, Section 40.8), rather than being permanently frozen at their initial specification.

19.6 Dashboards and visibility

Every metric named in Sections 19.3–19.4 is surfaced on an internal (Nexus-team-facing, not tenant-facing by default) evaluation dashboard, built — consistent with this volume's "eat your own cooking" ethos already demonstrated in Volume 2, Section 19.8's E2E-testing-via-Nexus's-own-Web-module design — using Nexus's own Report Summarization capability (Section 13) applied reflexively to this volume's own evaluation data, alongside standard numeric/trend visualization. Tenant-facing visibility is narrower and more targeted: Section 16.8's enterprise cost-visibility dashboard is joined by an equivalent quality-visibility view for enterprise tenants specifically wanting insight into self-heal acceptance rate and structural validity rate for their own tenant's data specifically — never aggregate cross-tenant figures, per Section 1.4's permanent data-pooling boundary applied here to metrics visibility, not just to underlying training/retrieval data.

19.7 Metric ownership and review cadence

Directly extending Volume 0's risk-register review discipline (Section 22.3 there: reviewed at the close of each Roadmap phase) to this volume's evaluation metrics specifically: every metric in Section 19.3's table has a named owner (the engineer or team responsible for that capability engine) and is reviewed at a minimum monthly cadence — a metric trending toward its threshold (19.3) without yet crossing it is exactly the kind of leading indicator this cadence exists to catch before it becomes a release-blocking regression discovered only at the worst possible moment (mid-release, under time pressure).

19.8 Evaluation metrics and Volume 0's success metrics — the final traceability link

This section closes the specific traceability chain Volume 0, Part I, Section 6 opened at the very start of this entire six-volume project: Volume 0 named self-heal acceptance rate (≥70% target) and flow-generation structural validity rate as headline AI-quality success metrics for the whole product. This volume has now specified, precisely: what these metrics mean (Sections 10.9, 8.8), how they're measured (this section, both offline and online), what infrastructure produces the underlying data (every capability engine in Part III, the Planning Engine in Section 2, the guardrail system in Section 17), and what governs changes that might affect them (Section 19.3's release gate). A metric named in Volume 0's opening pages as a business commitment is, by this volume's closing section, a fully-specified, continuously-measured, release-gated engineering reality — the same "vision through to verified implementation" arc this entire document set has traced at every level, from the whole platform (Volume 0) down to this one, specific, strategically-named number.

19.9 Closing note — Volume 3 complete

This section closes Volume 3 in full: 19 sections across 5 Parts, tracing from AI vision and planning (Part I) through knowledge and context infrastructure (Part II), the seven capability engines that do the actual work (Part III), the provider and cost infrastructure they run on (Part IV), to the guardrails, security posture, and evaluation discipline that make all of it trustworthy (this Part) — directly fulfilling the mandate Volume 0 set for this volume specifically: not raw AI capability as the differentiator, but unified, architected, continuously-verified trust across every AI touchpoint in the platform. Volume 4 (Infrastructure Architecture) now begins from a complete backend (Volume 1), frontend (Volume 2), and AI (Volume 3) specification, turning to how all three are actually deployed, monitored, and operated at scale.


___________________________________________________________________________________________________________________________________________________________________



📘 ADS-004 — Infrastructure Architecture Design

Objective

Define a scalable, observable, secure deployment architecture for enterprise environments.

Chapter 1 – Deployment Strategy

Development

QA

UAT

Production


Chapter 2 – Containers

Backend

Frontend

AI Service

Redis

PostgreSQL

Elasticsearch


Chapter 3 – Kubernetes

Pods

Deployments

Services

Ingress

ConfigMaps

Secrets

Autoscaling


Chapter 4 – CI/CD

GitHub Actions

Build

Test

Security Scan

Docker Build

Deployment

Rollback


Chapter 5 – Monitoring

Prometheus

Grafana

OpenTelemetry

Health Checks

Metrics

Tracing


Chapter 6 – Logging

Centralized logs

Structured logging

Correlation IDs

Retention



Chapter 7 – Security

TLS

Secrets management

Image scanning

RBAC

Network policies



Chapter 8 – Backup & Recovery

Database backups

Artifact storage

Disaster recovery

High availability


____________________________________________________________________________________________________________________________________________________________________

