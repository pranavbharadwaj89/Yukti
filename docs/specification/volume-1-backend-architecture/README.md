# Volume 1 — 

Backend Architecture · Part I — System Foundations



## Table of Contents

  - [Section 1 — Executive Summary](#1-executive-summary)
  - [Section 2 — System Context](#2-system-context)
  - [Section 3 — C4 Context Diagram](#3-c4-context-diagram)
  - [Section 4 — Container Diagram](#4-container-diagram)
  - [Section 5 — Component Diagram](#5-component-diagram)
  - [Section 6 — Domain-Driven Design](#6-domain-driven-design)
  - [Section 7 — Bounded Contexts](#7-bounded-contexts)
  - [Section 8 — Domain Model](#8-domain-model)
  - [Section 9 — Aggregates](#9-aggregates)
  - [Section 10 — Entities](#10-entities)
  - [Section 11 — Value Objects](#11-value-objects)
  - [Section 12 — Domain Events](#12-domain-events)
  - [Section 13 — Commands](#13-commands)
  - [Section 14 — Queries](#14-queries)
  - [Section 15 — Repository Pattern](#15-repository-pattern)
  - [Section 16 — Unit of Work](#16-unit-of-work)
  - [Section 17 — Dependency Injection](#17-dependency-injection)
  - [Section 18 — Plugin Architecture](#18-plugin-architecture)
  - [Section 19 — Execution Engine](#19-execution-engine)
  - [Section 20 — Workflow Engine](#20-workflow-engine)
  - [Section 21 — Scheduler](#21-scheduler)
  - [Section 22 — Event Bus](#22-event-bus)
  - [Section 23 — CQRS](#23-cqrs)
  - [Section 24 — Authentication](#24-authentication)
  - [Section 25 — Authorization](#25-authorization)
  - [Section 26 — Multi-tenancy](#26-multi-tenancy)
  - [Section 27 — Audit](#27-audit)
  - [Section 28 — Logging](#28-logging)
  - [Section 29 — Observability](#29-observability)
  - [Section 30 — REST API](#30-rest-api)
  - [Section 31 — SignalR](#31-signalr)
  - [Section 32 — Database Design](#32-database-design)
  - [Section 33 — ER Diagrams](#33-er-diagrams)
  - [Section 34 — Sequence Diagrams](#34-sequence-diagrams)
  - [Section 35 — Class Diagrams](#35-class-diagrams)
  - [Section 36 — Package Diagrams](#36-package-diagrams)
  - [Section 37 — Deployment](#37-deployment)
  - [Section 38 — Performance](#38-performance)
  - [Section 39 — Scalability](#39-scalability)
  - [Section 40 — Coding Standards](#40-coding-standards)

---

1. Executive Summary
1.1 What this volume is

Volume 0 answered what Nexus is and why it should exist, and closed with a locked high-level architecture (five layers), seven architecture principles, and a locked technology stack (.NET/C#, React, Playwright, Appium, nut.js, PostgreSQL, Redis, SignalR). Volume 1 is where that high-level shape becomes an implementation-grade specification of the backend: the actual bounded contexts, the actual domain model, the actual execution engine design, the actual database schema, and the actual coding standards an engineer opens a pull request against. Where Volume 0 could reasonably leave a decision as "Volume 1's responsibility," this volume does not have that luxury — everything here should be precise enough that two engineers reading it independently would build compatible systems.

1.2 Relationship to Volume 0 — the traceability chain continues

Every architectural decision in this volume traces back through the chain Volume 0 established: a business objective (Volume 0, Part I) motivates a functional or non-functional requirement (Volume 0, Part II), which is served by a platform-level decision (Volume 0, Part III) and constrained by an architecture principle or locked technology choice (Volume 0, Part IV). Where this volume makes a design decision, it cites the specific upstream item it satisfies (e.g., "per FR-ORCH-6" or "per Architecture Principle 20.3") rather than re-justifying from first principles. This is not a stylistic preference — it is what keeps a six-volume, 550-page document set from silently drifting from its own stated intentions as different sections are authored over time (the exact risk Volume 0's RISK-13 named explicitly).

1.3 What Volume 1 covers, and how it's organized

Six Parts, each answering a different layer of "how is the backend actually built":

Part I — System Foundations (this Part): the C4 model (Context, Container, Component diagrams) that formalizes Volume 0 Section 19's simplified diagram into implementation-grade precision.
Part II — Domain-Driven Design: the bounded contexts, domain model, aggregates, entities, value objects, domain events, commands, and queries that give the system's core concepts (Flow, Module, Execution, Report) a rigorous, code-mappable definition.
Part III — Core Engineering Patterns: the concrete design of the orchestration layer's internals — the execution engine, workflow engine, scheduler, event bus, and the supporting patterns (repository, unit of work, dependency injection, CQRS) that implement it — plus the plugin architecture that makes the AutomationModule contract real.
Part IV — Cross-Cutting Platform Services: authentication, authorization, multi-tenancy, audit, logging, and observability — the horizontal concerns Volume 0 Section 19.4 flagged as belonging to no single layer.
Part V — API & Data: the REST API surface, SignalR real-time design, and the full database design (schema, ER diagrams, and the supporting sequence/class/package diagrams that show how the domain model in Part II actually persists and flows through the system).
Part VI — Operations & Standards: deployment topology detail (building on Volume 0 Section 19.5 and 21.12), performance and scalability engineering against the NFRs from Volume 0 Section 13, and the coding standards that govern how all of the above is actually written.
1.4 Who this volume is for

Primarily Persona Raj (Volume 0, Section 10.5 — the engineer building a custom module) and any backend engineer joining the core team, with Persona Marcus (Section 10.3) as a secondary audience for the API and deployment sections specifically. Unlike Volume 0, which was written to be legible to product, business, and security stakeholders alike, Volume 1 assumes software engineering fluency throughout — it does not re-explain what a bounded context or a repository pattern is in general terms; it explains how Nexus specifically applies them.

1.5 A note on rigor versus premature specificity

Volume 0's Architecture Principle 20.2 (contract stability over implementation convenience) and 20.7 (composable primitives over monolithic capability) apply directly to how this volume itself should be read: where this volume specifies a contract (the AutomationModule interface, the domain events schema, the REST API surface), that specification should be treated as load-bearing and stable. Where this volume specifies an internal implementation detail with no external contract surface (e.g., a specific internal class's private method structure), it should be read as the current best design, revisable through normal engineering judgment and code review — not with the same weight as a contract decision. Part III will flag explicitly, at each pattern's introduction, which category it falls into.

1.6 Documents this volume produces

Consistent with the "every volume includes" checklist from the original project scope: this volume will include Architecture Decision Records (citing Volume 0's architecture principles by number per Volume 0 Section 20.9's instruction), C4 diagrams (this Part), UML diagrams (class, sequence, package — Part V), ER diagrams (Part V), folder structure and naming conventions (Part VI), sample configuration, and coding standards (Part VI). Risks specific to backend architecture decisions will be added to Volume 0's living risk register (Section 22) rather than duplicated into a separate Volume 1 risk section — one register, continuously maintained, per Volume 0's own closing guidance.



2. System Context
2.1 Purpose

Before decomposing Nexus into containers and components (Sections 4 and 5), this section fixes the system's boundary: who and what sits outside Nexus, and what crosses that boundary. This is the direct precursor to the C4 Context diagram in Section 3, and its content is drawn from the personas (Volume 0, Section 10) and the module target integrations (Volume 0, Section 14) already established.

2.2 Human actors at the system boundary
Actor Corresponds to (Volume 0 persona)   Interaction
Flow Author Priya Authors and edits flows via the GUI or directly via the REST API/CLI
CI/Pipeline Operator Marcus   Triggers flow execution from CI systems via CLI or REST API; consumes reports in CI-native formats
Engineering Manager / QA Lead Sofia Views the reporting dashboard; rarely interacts with any other surface
Module Developer  Raj   Implements and registers custom modules against the plugin SDK; does not interact with the GUI or REST API as an end user in this role
Administrator  (implicit — a superset role, not a distinct Volume 0 persona)  Manages RBAC, licensing tier configuration, plugin certification-tier policy, and system-level configuration
Security/Compliance Reviewer  Elena Interacts with documentation and, for self-hosted deployments, infrastructure configuration — not the running application directly in the common case
2.3 External systems at the boundary
External system   Which module(s) integrate with it   Nature of integration
Systems under test — arbitrary HTTP/REST APIs   API module  Outbound HTTP requests, defined per-flow, target system entirely customer-controlled
Systems under test — web applications  Web module  Outbound browser automation (Playwright driving a real or headless browser instance) against a customer-specified URL
Systems under test — mobile apps/devices  Mobile module  Outbound Appium protocol calls to a device, emulator, or device-farm endpoint
Systems under test — desktop applications Desktop UI module Local OS-level automation (mouse/keyboard/screen) against a target running in the same execution environment
Log sources Logs module Inbound read access to files, streams, or third-party observability platforms (ELK, Datadog, CloudWatch, etc., per Volume 0 Section 14.8's "read from, don't replace" boundary)
LLM provider   AI module (optional, per NFR-REL-3) Outbound API calls for flow generation, self-healing, and triage; entirely absent from the system's runtime behavior when the AI module is disabled
Secrets manager (Vault or equivalent)  Cross-cutting (all modules, via credential resolution)   Outbound calls to resolve secrets at flow-execution time; per NFR-SEC-2, secrets are never persisted in Nexus's own primary datastore in plaintext
Identity provider (SSO, enterprise tier)  API layer (authentication) Inbound/outbound per standard OIDC/SAML flows
CI/CD systems (GitHub Actions, Jenkins, GitLab CI, etc.) API layer + CLI   Inbound trigger calls; outbound report delivery in CI-native formats (FR-REP-2)
Plugin marketplace registry   Module layer (plugin installation)  Outbound calls to discover, download, and verify marketplace-sourced modules (Volume 0, Section 17)
Observability backend (Prometheus, or customer's own stack) Cross-cutting (platform self-observability)  Outbound telemetry export via OpenTelemetry (Volume 0, Section 21.11)
2.4 System boundary statement

Nexus's system boundary, precisely stated: Nexus orchestrates and executes automation flows against externally-owned target systems, and optionally consults an externally-owned LLM provider to assist with flow authoring and maintenance — but Nexus does not own, host, or modify any target system, and does not own the LLM provider's model. This boundary statement is the direct architectural expression of two Volume 0 commitments: the Logs module's "read from, don't replace" positioning (Section 14.8) and the AI module's provider-abstraction, non-data-pooling posture (Section 18.4, 18.5). Any future design that would blur this boundary (e.g., Nexus caching or persisting a full copy of a target system's data beyond what a specific flow run's step results require) should be treated as a boundary violation requiring explicit architectural review, not a routine implementation choice.

2.5 Trust boundaries

Distinct from the system boundary (2.4) is the question of trust boundaries, which matters directly for the security architecture in Part IV:

Nexus-to-target-system: target systems are not trusted by default — credential scoping (least-privilege execution, per the Prohibited/Explicit-permission-required framing that governs Nexus's own operational posture) should be assumed necessary per flow, not assumed safe by default.
Nexus-to-LLM-provider: data sent to the LLM provider (page HTML for selector healing, log excerpts for triage, natural-language descriptions for flow generation) crosses an explicit trust boundary that Persona Elena's review process (Volume 0, UC-7) is specifically designed to evaluate. Part IV's Authentication/Authorization sections and Volume 3's guardrail design both treat this boundary as requiring active data minimization (sending only what's needed for the specific AI task), not just encryption in transit.
Nexus-to-plugin-code: the highest-risk trust boundary in the system, per Volume 0 RISK-08 — third-party module code executes with orchestrator-granted privileges unless explicitly sandboxed, which is why Volume 0 Section 17.4 and Architecture Principle 20.5 both treat plugin sandboxing as a hard requirement, elaborated fully in Part III's Plugin Architecture section.
2.6 Context diagram forward reference

Section 3 renders the actors and external systems above as a formal C4 Level 1 Context diagram. The table format used in this section (rather than a diagram) is deliberate — Section 3's diagram is easier to read correctly once the boundary statement (2.4) and trust-boundary distinctions (2.5) are already understood in prose, rather than trying to convey both the what and the why in a single visual.



3. C4 Context Diagram
3.1 Purpose

This section renders Section 2's actors, external systems, and boundary statement as a formal C4 Level 1 (System Context) diagram — the highest, most abstract level of the C4 model, showing Nexus as a single box and everything outside it as either a person or an external system, with no internal structure exposed yet (that begins at Level 2, Section 4).

3.2 Diagram
                    ┌──────────────┐        ┌──────────────┐        ┌──────────────┐
                    │  Flow Author │        │ CI/Pipeline  │        │  Eng Manager │
                    │   (Priya)    │        │  Operator    │        │   (Sofia)    │
                    │  [Person]    │        │  (Marcus)    │        │  [Person]    │
                    └──────┬───────┘        │  [Person]    │        └──────┬───────┘
                           │                └──────┬───────┘               │
                           │  authors flows via GUI │ triggers runs via CLI │ views reports via GUI
                           │  (HTTPS)                │ / REST API (HTTPS)   │ (HTTPS)
                           │                         │                      │
                           ▼                         ▼                      ▼
                    ┌─────────────────────────────────────────────────────────────┐
                    │                                                               │
                    │                          N E X U S                           │
                    │      Automation Orchestration Platform  [Software System]     │
                    │                                                               │
                    │   Executes cross-domain automation flows (API, Web, Mobile,   │
                    │   Desktop UI, Logs) with an optional AI-assisted layer for     │
                    │   flow generation, selector self-healing, and log triage.      │
                    │                                                               │
                    └───┬─────────┬─────────┬─────────┬─────────┬─────────┬────────┘
                        │         │         │         │         │         │
        registers/uses  │  reads  │ outbound│ outbound│ outbound│ outbound│ outbound (optional)
        modules via SDK │  config │ browser │ mobile  │ desktop │ log     │ LLM API calls
        (local dev)     │  from   │ automa- │ device  │ OS      │ source  │ (HTTPS, only if
                        │  Vault  │ tion    │ proto-  │ automa- │ reads   │  AI module enabled)
                        │(HTTPS)  │(various)│ col     │ tion    │(various)│
                        ▼         ▼         ▼         ▼         ▼         ▼
                  ┌─────────┐┌─────────┐┌─────────┐┌─────────┐┌─────────┐┌─────────────┐
                  │ Module  ││ Secrets ││ Target  ││ Target  ││   Log   ││     LLM      │
                  │Developer││ Manager ││ Web Apps││ Mobile  ││ Sources ││  Provider    │
                  │  (Raj)  ││ (Vault) ││& REST   ││& Desktop││(ELK,    ││ (optional,   │
                  │[Person] ││[External││ APIs    ││ Apps    ││Datadog, ││ provider-    │
                  │         ││ System] ││[External││[External││etc.)    ││ abstracted)  │
                  │         ││         ││ System] ││ System] ││[External││[External     │
                  │         ││         ││         ││         ││ System] ││  System]     │
                  └─────────┘└─────────┘└─────────┘└─────────┘└─────────┘└─────────────┘

                    ┌──────────────┐                              ┌──────────────────┐
                    │ Security /   │  reviews architecture         │  Identity         │
                    │ Compliance   │  docs & infra config          │  Provider (SSO)   │
                    │ Reviewer     │  (offline, not runtime)        │  [External System] │
                    │  (Elena)     │                              └─────────┬─────────┘
                    │  [Person]    │                                        │
                    └──────────────┘                              authenticates users
                                                                    (OIDC/SAML, HTTPS)
                                                                              │
                                                                              ▼
                                                                    ┌──────────────────┐
                                                                    │      NEXUS       │
                                                                    │  (API Layer)      │
                                                                    └──────────────────┘
3.3 Reading notes
Elena (Security/Compliance Reviewer) is deliberately shown with a dashed/offline relationship rather than a runtime arrow into Nexus, consistent with Section 2.2's observation that her primary interaction surface is documentation and infrastructure configuration, not the running system.
The LLM Provider relationship is explicitly labeled optional — this is the single most important annotation on this diagram, since it is the visual proof-point of NFR-REL-3 and the AI-optionality architecture principle (Volume 0, Section 20.6) at the highest, most-viewed level of the architecture documentation. Any future edit to this diagram must preserve that "(optional)" label; its removal would be a documentation-level regression against a permanent product commitment (Volume 0, Section 18.4).
Module Developer (Raj) is shown interacting with Nexus differently from the other human actors — via SDK/local development rather than a runtime HTTPS call into a deployed instance — reflecting that his primary relationship is to the plugin contract (formalized in Part III), not to a running system as an end user.
This diagram intentionally omits the internal five-layer structure from Volume 0, Section 19.3 — that decomposition is out of scope for a C4 Context diagram by definition, and appears starting at the Container level in Section 4.
3.4 Diagram maintenance note

Per Architecture Principle 20.2 (contract stability) applied to documentation itself: this diagram's actor and external-system list should be treated as reasonably stable, changing only when a new class of external integration is added (e.g., a new module type per Volume 0's Future Vision, Section 23.2) — not redrawn casually. Internal restructuring within Nexus (which Sections 4 and 5 cover) should never require a change to this Context-level diagram; if it does, that is itself a signal the internal change may be crossing the system boundary in a way that needs Section 2's boundary statement (2.4) revisited.



4. Container Diagram
4.1 Purpose

This is the C4 Level 2 diagram: Nexus's single box from Section 3 is now decomposed into its deployable/runnable units ("containers" in the C4 sense — not necessarily Docker containers specifically, though in Nexus's case the mapping is close given the containerized deployment model from Volume 0, Section 21.12). This formalizes Volume 0 Section 19.3's simplified diagram with explicit technology annotations and protocol labels, per that section's own stated intent to defer the "formal version with deployment cardinality, protocols, and data-flow direction fully annotated" to Volume 1.

4.2 Diagram
 ┌────────────┐  ┌────────────┐
 │ Flow Author│  │CI Operator │  ...(other actors per Section 3)
 └─────┬──────┘  └─────┬──────┘
       │ HTTPS         │ HTTPS/CLI
       ▼                ▼
┌──────────────────────────────────────────────────────────────────────┐
│                            NEXUS  (System boundary)                   │
│                                                                        │
│  ┌────────────────┐        ┌────────────────┐                        │
│  │   Web GUI       │        │      CLI        │                        │
│  │  [Container:     │        │  [Container:     │                        │
│  │   React SPA]     │        │   .NET console    │                        │
│  │                 │        │   app]           │                        │
│  └────────┬────────┘        └────────┬────────┘                        │
│           │  REST/HTTPS + WebSocket   │  REST/HTTPS                     │
│           │  (SignalR for live        │                                 │
│           │   run updates)            │                                 │
│           ▼                            ▼                                │
│  ┌──────────────────────────────────────────────┐                      │
│  │              API Layer                        │                      │
│  │  [Container: ASP.NET Core Web API]              │                      │
│  │  AuthN/AuthZ · Flow CRUD · Execution trigger    │                      │
│  │  · Report retrieval · Admin endpoints           │                      │
│  └───────────────────┬────────────────────────────┘                      │
│                       │  in-process call / internal RPC                  │
│                       ▼                                                  │
│  ┌──────────────────────────────────────────────┐                      │
│  │           Orchestration Engine                 │                      │
│  │  [Container: .NET background service /          │                      │
│  │   worker process]                               │                      │
│  │  Flow Engine · Scheduler · Event Bus ·           │                      │
│  │  Execution Dispatcher · Retry/Flake handling     │                      │
│  └───┬──────┬──────┬──────┬──────┬──────┬─────────┘                      │
│      │      │      │      │      │      │  AutomationModule contract     │
│      ▼      ▼      ▼      ▼      ▼      ▼  (in-process or sandboxed RPC) │
│  ┌──────┐┌──────┐┌──────┐┌──────┐┌──────┐┌──────────┐                   │
│  │ API  ││ Web  ││Mobile││Desk- ││ Logs ││    AI     │                   │
│  │Module││Module││Module││top UI││Module││  Module   │                   │
│  │[.NET ││[.NET ││[.NET ││Module││[.NET ││ [.NET     │                   │
│  │ lib] ││ lib +││ lib +││[.NET ││ lib] ││  lib +    │                   │
│  │      ││Play- ││Appium││ lib +││      ││  provider │                   │
│  │      ││wright││/WDIO ││nut.js││      ││  adapter] │                   │
│  │      ││ proc]││ proc]││ proc]││      ││           │                   │
│  └──────┘└──────┘└──────┘└──────┘└──────┘└──────────┘                   │
│                       │                                                  │
│                       │  reads/writes                                    │
│                       ▼                                                  │
│  ┌──────────────────────┐   ┌──────────────────┐   ┌──────────────────┐ │
│  │  Primary Database      │   │   Cache/State      │   │  Audit & Report    │ │
│  │  [Container:            │   │  [Container:        │   │  Store             │ │
│  │   PostgreSQL]           │   │   Redis]            │   │  [same PostgreSQL   │ │
│  │  Flow defs · exec        │   │  Session state ·    │   │   instance,         │ │
│  │  history · RBAC data     │   │  in-flight run       │   │   separate schema]  │ │
│  │                         │   │  coordination        │   │                     │ │
│  └──────────────────────┘   └──────────────────┘   └──────────────────┘ │
│                                                                          │
└──────────────────────────────────────────────────────────────────────┘
       │                    │                    │                    │
       │ outbound            │ outbound            │ outbound (optional) │ outbound
       ▼                    ▼                    ▼                    ▼
 [Secrets Manager]    [Target Systems]      [LLM Provider]      [Observability
  (Vault, HTTPS)     (per module, various)  (HTTPS, provider-    Backend]
                                              abstracted)        (OTLP export)
4.3 Container inventory
Container   Technology  Responsibility Deployment unit
Web GUI  React SPA   Flow authoring, reporting dashboard Static assets served via CDN or app-server, independently deployable from the API
CLI   .NET console application   CI-triggered execution, local development/debugging   Distributed as a self-contained executable/NuGet tool; not a long-running service
API Layer   ASP.NET Core Web API Public REST surface, authentication/authorization, request validation   Horizontally-scalable stateless service (supports NFR-SCALE-2)
Orchestration Engine .NET background service Flow execution, scheduling, event dispatch, retry logic  Horizontally-scalable worker process, separable from the API Layer's deployment (allows independent scaling of "how many flows can run concurrently" from "how many API requests can be served," directly serving NFR-SCALE-1)
API / Web / Mobile / Desktop UI / Logs / AI Modules   .NET libraries, some with external process dependencies (Playwright browser process, Appium/WebdriverIO session, nut.js OS hooks)   Module-specific execution logic behind the common AutomationModule contract   Loaded in-process by the Orchestration Engine for built-in modules; sandboxed/isolated process boundary for marketplace-installed third-party modules (per Volume 0 Section 17.4 and the trust-boundary analysis in Section 2.5 of this Part)
Primary Database  PostgreSQL  Flow definitions, execution history, RBAC/identity data  Managed or self-hosted PostgreSQL instance per Volume 0 Section 21.8
Cache/State Redis Session state, rate limiting, in-flight cross-worker coordination Managed or self-hosted Redis instance per Volume 0 Section 21.9
Audit & Report Store PostgreSQL (separate schema, same or dedicated instance depending on deployment scale) Immutable audit log (NFR-SEC-3), historical report data feeding the dashboard (FR-REP-3)  Schema-level separation from operational data at minimum; full instance separation is a scaling option, not a GA requirement
4.4 Why the Orchestration Engine and API Layer are separate containers, not one

This is the single most consequential container-boundary decision in this diagram, so it is stated explicitly rather than left implicit: the API Layer (request/response, typically fast, stateless) and the Orchestration Engine (flow execution, potentially long-running, resource-intensive per concurrent flow) have fundamentally different scaling profiles. Coupling them into one deployable would force scaling one to scale the other, directly working against NFR-SCALE-1 (500 concurrent flow executions) and NFR-SCALE-2 (horizontal scaling by adding workers, without client-facing changes). This decision also cleanly supports Architecture Principle 20.8 (self-hosted is a first-class target) — a resource-constrained self-hosted deployment can run both containers on shared infrastructure for simplicity, while a large-scale deployment can scale them independently, without any code change, only a deployment-topology change.

4.5 Communication patterns summary
Client → API Layer: synchronous REST over HTTPS for all request/response operations; SignalR (WebSocket, falling back to long-polling) specifically for live flow-execution progress streaming to the GUI.
API Layer → Orchestration Engine: for GA, an in-process or internal-RPC call is assumed sufficient given the container-separation rationale in 4.4 is about scaling independently, not necessarily about network-separating by default — the precise mechanism (in-process for a combined deployment, message-queue-mediated for a fully split deployment) is a Part III (Execution Engine, Scheduler) design decision, not fixed at this diagram's level of abstraction.
Orchestration Engine → Modules: the AutomationModule contract call, in-process for built-in and Verified-tier trusted modules, sandboxed-process boundary for lower-trust marketplace modules (Section 2.5's trust-boundary distinction made concrete).
All containers → Observability Backend: asynchronous OTLP telemetry export, non-blocking with respect to the primary request/execution path (a telemetry-export failure must never cause a flow-execution failure — this is a direct corollary of Architecture Principle 20.4, fail loud/fast/informatively, applied to the observability path itself: observability failures should be visible in their own right, not silently swallowed, but also must not cascade into unrelated failures).
4.6 Forward reference

Section 5 takes the Orchestration Engine container specifically (the most architecturally significant container in this diagram) and decomposes it to the C4 Component level, since its internal structure — the Flow Engine, Scheduler, Event Bus, and Dispatcher named in Section 4.2's diagram — is exactly what Part III's Core Engineering Patterns will specify in full implementation detail.



## 5. Component Diagram

### 5.1 Purpose and scope

This is the C4 Level 3 diagram, decomposing one container from Section 4 — the **Orchestration Engine** — into its internal components. This container is chosen for the Level 3 treatment (rather than, say, the API Layer) because it is the architecturally central, most novel piece of the system: the API Layer is a comparatively conventional ASP.NET Core Web API (its component structure follows standard layered-API conventions and doesn't need a bespoke diagram to be understood), while the Orchestration Engine's internal design is Nexus's actual engineering core and directly implements FR-ORCH-1 through FR-ORCH-7 (Volume 0, Section 12.2).

### 5.2 Diagram

```
┌────────────────────────────────────────────────────────────────────────────┐
│                          Orchestration Engine  [Container]                    │
│                                                                                │
│  ┌───────────────┐                                                            │
│  │  Flow Parser    │  Parses a Flow definition (JSON/YAML) into a validated,     │
│  │  [Component]    │  strongly-typed in-memory Flow graph. Validates against       │
│  │                 │  the module-action schema (Section 5.4).                     │
│  └───────┬───────┘                                                            │
│          │  validated Flow                                                     │
│          ▼                                                                     │
│  ┌───────────────┐        ┌───────────────┐                                  │
│  │  Flow Engine    │◄──────►│ Variable Store │  Holds flow-scoped variables         │
│  │  [Component]    │        │  [Component]    │  (ctx.vars), resolves               │
│  │  Executes steps  │        │                 │  {{interpolation}} expressions       │
│  │  in sequence,    │        └───────────────┘  per Section 5.5.                    │
│  │  evaluates        │                                                            │
│  │  conditionals     │        ┌───────────────┐                                  │
│  │  (FR-ORCH-4)      │◄──────►│ Retry/Flake     │  Implements FR-ORCH-5's retry/       │
│  │                 │        │ Handler         │  backoff and flake-vs-genuine-       │
│  │                 │        │  [Component]    │  failure classification.             │
│  └───────┬───────┘        └───────────────┘                                  │
│          │  step dispatch request                                              │
│          ▼                                                                     │
│  ┌───────────────┐                                                            │
│  │  Module          │  Resolves the target module (per FR-ORCH-1's "each step        │
│  │  Dispatcher      │  targets exactly one registered module"), looks it up in       │
│  │  [Component]     │  the Module Registry, and invokes its `run()` method with       │
│  │                 │  interpolated params. Enforces the sandboxing/trust             │
│  │                 │  boundary from Part I Section 2.5 for non-built-in modules.     │
│  └───────┬───────┘                                                            │
│          │                                                                     │
│          ▼                                                                     │
│  ┌───────────────┐        ┌───────────────┐                                  │
│  │  Module          │◄──────►│  Module         │  The introspectable module/action     │
│  │  Registry        │        │  Registration   │  metadata store implementing          │
│  │  [Component]     │        │  API            │  FR-ORCH-7; consumed by both the      │
│  │  Runtime map of   │        │  [Component]    │  Dispatcher above and, indirectly     │
│  │  registered       │        │  Exposed to the │  via the API Layer, by the GUI's      │
│  │  AutomationModule │        │  API Layer for   │  auto-surfacing of module actions     │
│  │  instances        │        │  introspection   │  (Volume 0, UC-6).                    │
│  └───────────────┘        └───────────────┘                                  │
│                                                                                │
│  ┌───────────────┐        ┌───────────────┐        ┌───────────────┐        │
│  │  Scheduler       │        │  Event Bus       │        │  Execution         │        │
│  │  [Component]     │───────►│  [Component]     │───────►│  Reporter          │        │
│  │  Cron/webhook/    │ emits │  Publishes step-  │consumed│  [Component]       │        │
│  │  file-watch        │ events│  and flow-level    │  by   │  Assembles the      │        │
│  │  triggers (FR-       │        │  events (started,  │        │  unified              │        │
│  │  ORCH scheduling      │        │  completed,        │        │  FlowReport            │        │
│  │  requirement)         │        │  failed, healed)   │        │  (FR-REP-1),           │        │
│  │                       │        │  for SignalR        │        │  persists via the      │        │
│  │                       │        │  streaming and      │        │  Report Repository     │        │
│  │                       │        │  audit logging      │        │  (Part III).           │        │
│  └───────────────┘        └───────────────┘        └───────────────┘        │
│                                                                                │
└────────────────────────────────────────────────────────────────────────────┘
        │                          │                              │
        ▼                          ▼                              ▼
  [Module instances,        [SignalR Hub, in the        [Report/Audit persistence,
   per Container diagram]    API Layer container]         via Repository pattern —
                                                            Part III]
```

### 5.3 Component responsibilities in detail

**Flow Parser** — the single entry point for turning a serialized flow definition into the strongly-typed domain model that Part II formalizes (the `Flow`, `FlowStep` entities). Schema validation happens here, not deeper in the pipeline, consistent with Architecture Principle 20.4 (fail loud, fail fast) — a malformed flow definition should be rejected before any execution resources are committed, with a specific, structured validation error, not discovered mid-execution.

**Flow Engine** — the direct implementation of the orchestrator design already prototyped conceptually in this project's early exploration (the `Orchestrator.runFlow` logic pattern), now formalized as a first-class component with its own dependencies (Variable Store, Retry/Flake Handler) rather than a monolithic function. This component owns fail-fast sequencing (Product Philosophy 4.2, Volume 0) as its default behavior and the continue-on-failure override (FR-ORCH-3) as an explicit per-flow configuration it must respect.

**Variable Store** — scoped strictly to a single flow-run's lifetime (never shared across concurrent flow-runs, which is a specific concurrency-safety requirement given NFR-SCALE-1's 500-concurrent-execution target). Implements the `{{vars.x.y}}` interpolation resolution logic.

**Retry/Flake Handler** — a distinct component from the Flow Engine specifically so that retry/backoff policy can be unit-tested and reasoned about independently of step-sequencing logic, and so that its flake-classification output (Volume 0, NFR-REL-2's platform flake-rate metric) has one clear, auditable source rather than being computed ad hoc wherever a retry happens to occur in the code.

**Module Dispatcher** — the component that actually calls across the trust boundary described in Part I, Section 2.5. Its design must treat every module identically per Architecture Principle 20.3 (every module executed the same way) — there is no module-specific branching inside this component; any module-specific behavior is entirely inside the module implementation on the other side of the `AutomationModule` contract.

**Module Registry / Module Registration API** — the runtime and introspection halves of FR-ORCH-6 and FR-ORCH-7. The Registry is the in-memory (or distributed-cache-backed, for horizontally-scaled deployments) map the Dispatcher queries at execution time; the Registration API is what the API Layer calls to expose available modules/actions to the GUI and CLI, directly enabling the "GUI automatically surfaces new module actions" requirement demonstrated in Volume 0's UC-6.

**Scheduler** — owns trigger mechanisms (cron, webhook, file-watch) that initiate flow execution outside of a direct API call, feeding into the same Flow Engine entry point as an API-triggered run, so that a scheduled run and an API-triggered run are indistinguishable to every downstream component — another direct application of "one flow language, one execution path" (Volume 0 Vision Statement).

**Event Bus** — the internal publish/subscribe mechanism that decouples the Flow Engine's step-by-step progress from its two independent consumers: SignalR (for live GUI updates) and the audit-logging pipeline (Part IV). This decoupling is what allows a future third consumer (e.g., a webhook-notification feature) to be added without modifying the Flow Engine itself — a direct application of Architecture Principle 20.7 (composable primitives).

**Execution Reporter** — assembles the unified `FlowReport` (Volume 0's `FlowReport` type, now formalized fully in Part II's domain model) from the accumulated step results, and is the component responsible for satisfying FR-REP-1's "consistent pass/fail/skip status model across modules" — this consistency guarantee lives here, in one place, rather than being each module's individual responsibility to get right.

### 5.4 Module-action schema, referenced above

The Flow Parser's validation (5.3) and the Module Registration API's introspection (5.3) both depend on a shared schema describing what parameters a given module action accepts — this schema is defined once, per module, and consumed by both components, avoiding the drift risk of two independently-maintained descriptions of the same module surface. Full schema design is a Part II (Domain Model) and Part III (Plugin Architecture) topic; it is referenced here only to explain the Flow Parser and Registration API's shared dependency.

### 5.5 Variable interpolation, referenced above

The `{{vars.x.y}}` interpolation syntax already prototyped in this project's early exploration phase is adopted as the formal design baseline for the Variable Store component, with Part II's domain model formalizing the exact resolution-path grammar and Part III specifying its performance characteristics against NFR-PERF-1 (50ms p95 step-dispatch overhead, which the Variable Store's resolution logic must fit comfortably inside).

### 5.6 What's deliberately not shown at this level

Consistent with C4's leveling discipline, this diagram does not show classes, methods, or database tables — that level of detail is Part II (domain model, entities) and Part V (class diagrams, ER diagrams). It also does not show the API Layer's internal components, since Section 5.1 already scoped this Level 3 treatment specifically to the Orchestration Engine container as the architecturally novel piece; the API Layer's internal structure, being conventional layered-API design, is covered in Part V's REST API section at the level of detail actually needed (endpoint inventory and contract), not a bespoke component diagram.


____________________________________________________________________________________________________________________________________________________



6. Domain-Driven Design
6.1 Why DDD, specifically, for Nexus

Domain-Driven Design is not adopted here as a default enterprise-architecture reflex — it is adopted because Nexus's core problem, as stated all the way back in Volume 0's Problem Statement (Section 7), is fundamentally a modeling problem before it is an execution problem. The hard part of Nexus is not "can we call a REST endpoint" or "can we click a button" — every module in isolation is a solved problem in the industry. The hard part is defining, precisely and consistently, what a "Flow" is, what it means for a "Step" to belong to exactly one "Module," what a "Flow Run" is as distinct from a "Flow" definition, and how these concepts relate identically regardless of which of the six modules is involved — which is exactly Architecture Principle 20.3 (every module executed, observed, and reported on the same way) restated as a modeling challenge. DDD's tactical patterns (aggregates, entities, value objects, domain events) are the direct tool for making that consistency a property of the code's structure, not just a design intention documented in prose.

6.2 Strategic vs. tactical DDD — what Nexus adopts

DDD separates strategic design (bounded contexts, context mapping — Section 7) from tactical design (aggregates, entities, value objects, domain events, repositories — Sections 9 through 12, and Part III's Repository Pattern section). Nexus adopts both, but with different degrees of rigor:

Strategic patterns are adopted fully and treated as architecturally load-bearing. The bounded-context boundaries defined in Section 7 map directly onto the container boundaries already fixed in Part I (Section 4) — this is not a coincidence; the container decomposition was informed by an implicit domain analysis that Section 7 now makes explicit and formal.
Tactical patterns are adopted pragmatically, not dogmatically. Nexus does not, for example, adopt full event sourcing for every aggregate, or insist on strict aggregate-per-transaction boundaries where a pragmatic read-model shortcut serves the reporting use cases (Volume 0, UC-5) better. Section 23 (CQRS, Part III) discusses specifically where read/write separation is and isn't applied, and why.
6.3 Ubiquitous language — the discipline this section establishes

The single most valuable and most fragile output of a DDD effort is a ubiquitous language: a vocabulary that means the same thing in conversation between a product person (Volume 0's personas), in this documentation, and in the actual C# code — the same term, not a synonym translated at each boundary. Nexus's ubiquitous language was substantially already established informally across Volume 0 and Part I of this volume (Flow, Step, Module, Run, Report). Sections 8 through 14 of this Part exist specifically to make that informal vocabulary rigorous and non-negotiable: once a term is defined here, it is the only acceptable name for that concept anywhere in the codebase, the API surface, the GUI copy, and future documentation. A pull request that introduces a synonym for an already-defined ubiquitous-language term (e.g., calling a FlowRun an "Execution" in one part of the codebase and a "Run" in another) should be treated as a defect, not a style nit — this is the direct, concrete mechanism by which DDD prevents the "five tools with five vocabularies" fragmentation problem (Volume 0, Section 7.2) from recurring inside Nexus's own codebase.

6.4 Relationship between the domain model and the AutomationModule plugin contract

A specific tension needs to be named directly: the AutomationModule interface (introduced conceptually in Volume 0 and formalized in Part III's Plugin Architecture section) is, by design, generic and minimally opinionated — it has to be, since a third-party module developer (Persona Raj) must be able to implement it without deep knowledge of Nexus's internal domain model. The domain model in this Part, by contrast, is Nexus's own rich, internal understanding of what a Flow, Step, and Run mean. These two things are related but not identical: a StepResult returned across the plugin contract boundary is a deliberately minimal DTO-like shape, while the Step entity inside the domain model (Section 10) carries richer internal state (audit metadata, AI-attribution flags, retry history) that the module contract itself never needs to know about. Section 9's aggregate design makes this boundary explicit — the plugin contract is an anti-corruption-layer-adjacent boundary, translating between the module's minimal view and the orchestrator's richer internal domain model, consistent with DDD's standard treatment of integration boundaries with external/less-trusted code (directly reinforcing the trust-boundary analysis from Part I, Section 2.5).

6.5 What this Part does not attempt

Consistent with Section 1.5's rigor-versus-premature-specificity guidance: this Part defines the domain model's structure and invariants precisely, because those are contract-level decisions per Architecture Principle 20.2. It does not attempt to fully specify every private implementation method or every validation edge case — that level of detail belongs in the actual codebase and its own inline documentation/tests, not in this blueprint. Where this Part shows C# code, it should be read as an illustrative, structurally-accurate sketch of the intended shape, not as literal, ready-to-compile production code (which would require dependencies, full error handling, and test coverage this document format cannot responsibly provide).

6.6 Section roadmap for the rest of this Part
Section 7 (Bounded Contexts) — the strategic decomposition: which parts of the domain belong to which context, and how contexts relate.
Section 8 (Domain Model) — the ubiquitous-language glossary and a birds-eye view of how the concepts in Sections 9–14 fit together.
Section 9 (Aggregates) — the consistency boundaries: Flow, FlowRun, and ModuleRegistration as the three aggregate roots.
Section 10 (Entities) — the identity-bearing objects within and across those aggregates.
Section 11 (Value Objects) — the immutable, identity-free concepts (status enums, selectors, assertions, variable expressions).
Section 12 (Domain Events) — what the system announces as domain-significant occurrences, and who consumes them (directly implementing the Event Bus component from Part I, Section 5.3).
Section 13 (Commands) — the write-side operations that mutate the domain model.
Section 14 (Queries) — the read-side operations that serve the reporting and GUI-introspection use cases.



7. Bounded Contexts
7.1 Identifying the contexts

Five bounded contexts partition Nexus's domain. Each has its own internal model, its own ubiquitous-language vocabulary where terms might otherwise ambiguously overlap, and — critically — maps onto the container boundaries already fixed in Part I, Section 4, rather than introducing a competing decomposition.

Bounded Context   Owns  Maps to container(s) (Part I, Section 4)
Flow Authoring Flow definitions, versioning, validation  API Layer (write side)
Execution   Flow runs, step dispatch, retry/flake logic, live progress  Orchestration Engine
Module & Plugin   Module registration, the AutomationModule contract, marketplace metadata   Orchestration Engine (registry) + API Layer (registration endpoints)
Reporting & Audit Historical run data, trend aggregation, immutable audit log Audit & Report Store, API Layer (read side)
Identity & Access Users, roles, RBAC policy, tenant boundaries API Layer (authentication/authorization)

A sixth candidate context — AI Assistance — is deliberately not modeled as a fully independent bounded context with its own aggregates. Instead, it is treated as a supporting capability that participates in the Flow Authoring and Execution contexts (a flow-generation request is a Flow Authoring concern; a self-heal is an Execution-context concern), consistent with Volume 0's repeated framing of AI as a layer across the platform rather than a seventh peer module structurally. Section 7.5 elaborates this decision specifically, since it is a deliberate deviation from the six-module framing used elsewhere and needs its own justification.

7.2 Context map
┌─────────────────────┐        Flow definition        ┌─────────────────────┐
│   Flow Authoring      │ ─────────(Customer/Supplier)──►│     Execution         │
│                       │                                │                       │
│  Flow (draft/         │◄────────Validation feedback────│  FlowRun,             │
│  published)           │        (on publish attempt)     │  step dispatch        │
└──────────┬───────────┘                                └──────────┬───────────┘
           │                                                        │
           │ queries available                                     │ dispatches to,
           │ module actions                                        │ receives StepResult from
           │ (Open Host Service)                                    │ (Conformist — accepts the
           ▼                                                        ▼  AutomationModule contract
┌─────────────────────┐                                ┌─────────────────────┐
│  Module & Plugin       │◄───────────────────────────────│   (same context,      │
│                       │      registers/discovers         │    Module Dispatcher   │
│  ModuleRegistration,   │                                │    consumes registry)  │
│  AutomationModule      │                                └─────────────────────┘
│  contract              │
└──────────┬───────────┘
           │ publishes domain events
           │ (Published Language — Section 12)
           ▼
┌─────────────────────┐        Historical run data      ┌─────────────────────┐
│  Reporting & Audit     │◄────────(Customer/Supplier,────│    Execution          │
│                       │         Execution is upstream)  │    (continued)        │
│  FlowReport,           │                                │                       │
│  AuditEntry            │                                │                       │
└──────────┬───────────┘                                └─────────────────────┘
           │
           │ enforces access policy on
           │ (Conformist to Identity & Access's
           │  RBAC decisions)
           ▼
┌─────────────────────┐
│  Identity & Access     │  Shared Kernel: User/Role identifiers are referenced
│                       │  (not duplicated) by every other context that needs
│  User, Role, Tenant,   │  to record "who did this" — Section 7.4 elaborates.
│  Permission            │
└─────────────────────┘
7.3 Integration patterns between contexts

Using standard DDD context-mapping vocabulary, precisely applied:

Flow Authoring → Execution: Customer/Supplier. Execution is the customer — it consumes validated, published Flow definitions and has no ability to modify them. Flow Authoring is the supplier and must not break Execution's expectations about what a "published" flow guarantees (schema-valid, all referenced modules registered) — this is the direct source of Section 9's Flow aggregate invariant that a flow cannot transition to "published" status while referencing an unregistered module or action.
Execution ↔ Module & Plugin: Conformist. The Execution context's Module Dispatcher (Part I, Section 5.3) conforms entirely to the AutomationModule contract as defined by the Module & Plugin context — Execution does not get to unilaterally demand contract changes; contract evolution follows the versioning policy from Architecture Principle 20.2, owned by the Module & Plugin context.
Module & Plugin → Reporting & Audit, and Execution → Reporting & Audit: Published Language. Both upstream contexts communicate with Reporting & Audit exclusively through the formally-defined domain events (Section 12), never through direct database access or ad hoc internal API calls. This is the single most important integration-pattern decision in this context map, because it's what keeps Reporting & Audit's read models decoupled enough to evolve independently (e.g., adding new trend calculations) without requiring changes to Execution's write-side logic — a direct enabler of the CQRS approach discussed in Part III, Section 23.
All contexts → Identity & Access: Shared Kernel (identifiers only). This is deliberately the narrowest possible shared-kernel relationship: other contexts hold and reference UserId/RoleId/TenantId values, but never duplicate Identity & Access's internal logic for evaluating permissions — every permission check is a call into Identity & Access (Part IV's Authorization section formalizes this as a policy-evaluation service), never a re-implementation.
7.4 Why Identity & Access is a shared kernel rather than fully isolated

A fully isolated Identity & Access context (communicating only via events/APIs, with zero shared types) is the more "textbook-pure" DDD choice, but was rejected specifically because TenantId needs to be a first-class, type-safe, universally-present value object across every other context's aggregates to make multi-tenancy (Part IV, Section 26) structurally enforceable rather than optionally remembered. A shared kernel limited strictly to identifier value objects (never behavior) is judged the pragmatic middle ground, consistent with Section 6.2's stated preference for pragmatic over dogmatic tactical DDD application.

7.5 Why AI Assistance is not its own bounded context

Three reasons, each tracing back to Volume 0 commitments: first, per Volume 0 Section 18.4, the AI module never autonomously persists changes — every AI output becomes either a Flow Authoring concern (a generated flow draft, subject to the same publish-time validation as any human-authored draft) or an Execution concern (a self-heal proposal, subject to the same StepResult/domain-event pipeline as any other step outcome). It has no independent domain concept that isn't already "a Flow" or "a FlowRun" with an AI-attribution flag attached (Section 10's entity design formalizes this flag). Second, treating AI as a cross-cutting capability rather than an isolated context is the domain-modeling expression of Volume 0's own "AI is a layer across the platform, not a seventh peer" framing (Section 18.8) — the bounded-context map should reflect product strategy, not contradict it. Third, isolating AI as its own context would force an awkward Customer/Supplier or Conformist relationship with both Flow Authoring and Execution simultaneously for what is, in domain terms, a single coherent concern (AI-assisted authoring and healing), which is a sign the boundary is being drawn in the wrong place per standard DDD context-mapping heuristics.

7.6 Context ownership and team implications

This context map has a direct implication for team structure (relevant to Volume 5's engineering playbook and Volume 0's RISK-14 team-scaling risk): each bounded context is a natural unit of team ownership, and the Customer/Supplier and Conformist relationships above define which team's API changes require coordination with which other team, versus which changes are purely internal to one context. This should inform, not be dictated by, Volume 5's team-structure guidance.



8. Domain Model
8.1 Purpose

This section is the ubiquitous-language glossary (per Section 6.3's discipline) and the birds-eye map of how the concepts detailed fully in Sections 9–14 relate to one another, primarily within the Execution and Flow Authoring bounded contexts (Section 7), since those two contexts hold the richest domain model. Reporting & Audit, Module & Plugin, and Identity & Access have comparatively thinner domain models of their own, described briefly here and detailed further where relevant in Part III and Part IV.

8.2 Core glossary — Flow Authoring context
Term  Definition  Formalized in
Flow  A named, versioned definition of an ordered sequence of Steps, optionally parameterized by declared Variables. The aggregate root of the Flow Authoring context.  Section 9 (Aggregates), Section 10 (Entities)
FlowStep A single unit of work within a Flow, targeting exactly one Module and one Action, with declared Parameters and an optional saveAs binding and when condition.  Section 10
Draft / Published The two lifecycle states of a Flow. A Draft may be freely edited; a Published Flow is immutable and is the only state Execution may run (Section 7.3's Customer/Supplier invariant).   Section 9
Module (as referenced by a FlowStep)   Not itself owned by this context — a FlowStep holds a reference (ModuleKind + action name) resolved against the Module & Plugin context's registry at validation and execution time.   Section 11 (Value Objects), Section 7 (context relationship)
8.3 Core glossary — Execution context
Term  Definition  Formalized in
FlowRun  A single execution instance of a specific Published Flow version, with its own runtime Variable state and ordered list of StepResults. The aggregate root of the Execution context. Section 9, Section 10
StepResult  The outcome of executing one FlowStep within one FlowRun: status, timing, message/error, and any data produced (available to later steps via variable interpolation).   Section 10
RunStatus   The lifecycle state of a FlowRun: Pending, Running, Passed, Failed, Cancelled.   Section 11
StepStatus  The outcome state of an individual StepResult: Passed, Failed, Skipped. Section 11
RetryAttempt   A record of one retry of a given step, distinct from the final StepResult, retained specifically to support the flake-rate metric (Volume 0, NFR-REL-2) — a step that fails once and passes on retry produces both a RetryAttempt record and a final "passed, flaky" StepResult, not just the latter. Section 10
AiAttribution  A value object attached to a StepResult when an AI capability (self-heal) materially altered the step's execution, per FR-AI-3's transparency requirement. Never silently absent when AI intervened.  Section 11
8.4 Core glossary — Module & Plugin context
Term  Definition  Formalized in
ModuleRegistration   The record of a registered AutomationModule implementation: its ModuleKind, available actions, each action's parameter schema, and its trust tier (Built-in, Verified, Community — extending Volume 0 Section 17.3's marketplace certification tiers into the runtime domain model). Aggregate root of this context. Part III, Section 18 (Plugin Architecture)
ActionSchema   The declared shape (parameter names, types, required/optional) of one action a module exposes — the shared dependency the Flow Parser and Module Registration API both consume, per Part I, Section 5.4. Part III, Section 18
8.5 Core glossary — Reporting & Audit context
Term  Definition  Formalized in
FlowReport  The read-model aggregation of a completed (or in-progress) FlowRun's StepResults into the unified pass/fail/skip summary required by FR-REP-1. Distinct from FlowRun itself — FlowReport is a projection, not the source of truth.  Part V (Database Design), Part III (CQRS)
AuditEntry  An immutable record of a state-changing action (flow published, run triggered, role changed, credential updated) satisfying NFR-SEC-3. Part IV, Section 27
8.6 Core glossary — Identity & Access context
Term  Definition  Formalized in
User  A human or service-account identity.   Part IV, Section 24
Role  A named bundle of Permissions (FR-SEC-4's minimum three: flow author, flow runner, administrator). Part IV, Section 25
Tenant   The isolation boundary for a customer organization's data, referenced as a shared-kernel identifier (Section 7.4) by every other context's aggregates.   Part IV, Section 26
8.7 How the glossary terms relate — a worked example

To make the abstract glossary concrete, here is how the terms above compose for a single realistic scenario — Volume 0's UC-1 (cross-domain regression flow) — traced through the domain model:

Priya authors a Flow (Flow Authoring context) named "Checkout regression," containing three FlowSteps targeting the Web, API, and Logs modules respectively, and publishes it (Draft → Published transition).
Marcus's CI pipeline triggers execution. The Execution context creates a new FlowRun referencing that specific Published Flow version.
The Flow Engine (Part I, Section 5) dispatches each FlowStep in order via the Module Dispatcher, which resolves each against the Module & Plugin context's ModuleRegistration for Web, API, and Logs respectively.
Each dispatch produces a StepResult, accumulated onto the FlowRun. The second step's StepResult carries data that the third step's parameters reference via variable interpolation (Section 8.2's saveAs mechanism, resolved by the Variable Store).
On completion, the Execution context's Event Bus (Part I, Section 5.3) publishes domain events (Section 12) that the Reporting & Audit context consumes (Published Language relationship, Section 7.3) to materialize a FlowReport and append AuditEntry records.
Every step of this sequence carries the Tenant identifier from Priya's authenticated session (Identity & Access shared kernel, Section 7.4), scoping every aggregate created along the way.
8.8 Diagram — domain model overview
 Flow Authoring                Execution                    Module & Plugin
┌───────────────┐          ┌───────────────┐              ┌───────────────────┐
│     Flow        │ 1    n  │    FlowRun      │  n       1   │  ModuleRegistration  │
│ (aggregate root) │────────►│ (aggregate root) │◄─────────────│  (aggregate root)    │
│                 │published │                 │  resolves    │                     │
│  - FlowStep[]    │ version │  - StepResult[]  │  each step   │  - ActionSchema[]    │
│  - Variables[]    │  ref    │  - RetryAttempt[]│   against    │                     │
└───────────────┘         └────────┬────────┘              └───────────────────┘
                                    │ domain events
                                    │ (Published Language)
                                    ▼
                          Reporting & Audit
                         ┌───────────────────┐
                         │    FlowReport        │  (projection, not source of truth)
                         │    AuditEntry         │
                         └───────────────────┘

  Identity & Access (shared-kernel identifiers referenced by all aggregates above)
 ┌─────────────────────────────────────────────────────────────────────────┐
 │  User · Role · Tenant                                                      │
 └─────────────────────────────────────────────────────────────────────────┘
8.9 Forward reference

Sections 9 through 14 now take each piece of this overview and specify it fully: aggregate boundaries and invariants (9), entity definitions with C# sketches (10), value object definitions (11), the domain events referenced in Section 8.7 step 5 (12), the commands that drive state transitions like "Draft → Published" and "trigger a FlowRun" (13), and the queries that serve FlowReport retrieval and module introspection (14).



9. Aggregates
9.1 Aggregate design principles for Nexus

An aggregate is a consistency boundary — a cluster of entities and value objects that must be kept transactionally consistent with each other, accessed and modified only through its root, and referenced from outside only by identifier. Nexus defines exactly three aggregate roots, one per the three "richest" bounded contexts identified in Section 7: Flow (Flow Authoring), FlowRun (Execution), and ModuleRegistration (Module & Plugin). Reporting & Audit's FlowReport and AuditEntry are deliberately not modeled as aggregates in the DDD sense — they are read-model projections (Section 8.5 already flagged this), with no independent consistency invariants of their own to protect; they simply reflect what the source-of-truth aggregates have already asserted.

9.2 The Flow aggregate

Root entity: Flow. Invariants this aggregate must always enforce:

A Flow in Published status is immutable — no FlowStep may be added, removed, or modified while in this status. Editing a published flow requires creating a new version (a new Flow aggregate instance sharing a FlowFamilyId but with an incremented version number), never mutating in place. This is the direct enforcement mechanism for the Customer/Supplier invariant from Section 7.3 — Execution can trust that a FlowRun referencing a specific Flow version will never see that version's steps change underneath it mid-run or after the fact.
A Flow may only transition Draft → Published if every FlowStep's (ModuleKind, action) pair resolves against a currently-registered ModuleRegistration (a cross-aggregate check performed at the application-service layer, not inside the aggregate itself, since an aggregate should never directly query another aggregate — this distinction matters and is elaborated in 9.5).
FlowStep ordering within a Flow is significant and is part of the aggregate's consistency boundary — steps are stored as an ordered list, never a set, and any operation that reorders steps must go through the aggregate root.
csharp
public class Flow : AggregateRoot<FlowId>
{
    public FlowFamilyId FamilyId { get; private set; }
    public int Version { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public FlowStatus Status { get; private set; } // Draft | Published | Archived
    public IReadOnlyList<FlowStep> Steps => _steps.AsReadOnly();
    private readonly List<FlowStep> _steps = new();
    public IReadOnlyDictionary<string, VariableDefault> DeclaredVariables { get; private set; }
    public TenantId TenantId { get; private set; }

    public void AddStep(FlowStep step)
    {
        if (Status != FlowStatus.Draft)
            throw new DomainException("Cannot modify a non-draft Flow. Create a new version instead.");
        _steps.Add(step);
    }

    public FlowPublishResult Publish(IModuleActionResolver resolver)
    {
        // Validates every step's (ModuleKind, action) against the resolver
        // (an injected port to the Module & Plugin context — see Section 9.5)
        // and transitions Status to Published only if all resolve successfully.
        // Returns a result object rather than throwing, since an unresolved
        // module reference is an expected, reportable outcome, not an
        // exceptional program state (Architecture Principle 20.4).
    }
}
9.3 The FlowRun aggregate

Root entity: FlowRun. Invariants:

A FlowRun references an immutable Flow version by FlowId (never FlowFamilyId — a run is always pinned to the exact version that was published at trigger time, so that a Flow being edited in Draft after a run started can never retroactively affect that run's behavior or reporting).
StepResults are appended in step order and are themselves immutable once created — a step that is retried produces a new RetryAttempt record plus, only on the retry sequence's conclusion, one final StepResult; it never overwrites a prior result.
Once a FlowRun's Status transitions to a terminal state (Passed, Failed, Cancelled), no further StepResults may be appended — this is the aggregate-level enforcement of fail-fast sequencing (Product Philosophy 4.2) and the continue-on-failure override's boundary (FR-ORCH-3): even continue-on-failure flows have a defined terminal point after which the aggregate is closed.
csharp
public class FlowRun : AggregateRoot<FlowRunId>
{
    public FlowId FlowId { get; private set; }          // pinned exact version
    public RunStatus Status { get; private set; }
    public RunTrigger Trigger { get; private set; }       // Api | Scheduled | Webhook
    public IReadOnlyDictionary<string, object?> Variables { get; private set; }
    public IReadOnlyList<StepResult> Results => _results.AsReadOnly();
    private readonly List<StepResult> _results = new();
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public TenantId TenantId { get; private set; }

    public void RecordStepResult(StepResult result)
    {
        if (IsTerminal(Status))
            throw new DomainException("Cannot record a step result on a completed FlowRun.");
        _results.Add(result);
        SetVariable(result); // updates Variables if the step declared a saveAs binding
        RaiseDomainEvent(new StepCompletedEvent(Id, result)); // Section 12
    }

    public void Complete()
    {
        Status = _results.Any(r => r.Status == StepStatus.Failed) ? RunStatus.Failed : RunStatus.Passed;
        FinishedAt = DateTimeOffset.UtcNow;
        RaiseDomainEvent(new FlowRunCompletedEvent(Id, Status)); // Section 12
    }
}
9.4 The ModuleRegistration aggregate

Root entity: ModuleRegistration. Invariants:

A ModuleKind value (Section 11) must be unique among currently-active registrations within a tenant's accessible module set — no two simultaneously-active modules may claim the same ModuleKind identifier, since the Flow aggregate's step-resolution logic (9.2) depends on this uniqueness to be well-defined.
An ActionSchema cannot be removed from a registered module while any Published Flow still references that action — enforced again at the application-service layer via the same cross-aggregate query pattern as Flow.Publish (9.5), since this is fundamentally a cross-aggregate check.
Trust tier (BuiltIn, Verified, Community — Section 8.4) is immutable after registration within a given version; a re-certification (e.g., a Community module promoted to Verified per Volume 0's certification process, Section 17.3) creates a new ModuleRegistration version rather than mutating trust tier in place, mirroring the Flow aggregate's versioning approach (9.2) for the same auditability reasons.
csharp
public class ModuleRegistration : AggregateRoot<ModuleRegistrationId>
{
    public ModuleKind Kind { get; private set; }
    public string DisplayName { get; private set; }
    public TrustTier Trust { get; private set; }         // BuiltIn | Verified | Community
    public IReadOnlyList<ActionSchema> Actions => _actions.AsReadOnly();
    private readonly List<ActionSchema> _actions = new();
    public SemanticVersion ContractVersion { get; private set; } // AutomationModule interface version, Architecture Principle 20.2
    public bool IsActive { get; private set; }
}
9.5 Cross-aggregate consistency — the application-service pattern

Per strict DDD discipline, aggregates never directly reference or query one another. Where a cross-aggregate invariant must be checked (e.g., Flow.Publish needing to know whether every referenced module action currently exists in some ModuleRegistration), the pattern used throughout Nexus is: the aggregate exposes a method accepting an injected port interface (IModuleActionResolver in 9.2's sketch) that the calling application service satisfies using a repository query (Part III, Section 15) against the other aggregate's persisted state. This keeps each aggregate's own code free of any direct dependency on another aggregate's repository, while still allowing the invariant to be enforced synchronously at the moment it matters (publish time) rather than deferred to eventual consistency — a deliberate choice, since "can this flow even run" is not a scenario where Nexus should tolerate eventual consistency, per Architecture Principle 20.4's fail-fast philosophy.

9.6 Why not one larger aggregate

A tempting alternative is a single Flow aggregate that also contains all of its historical FlowRuns as child entities, avoiding the cross-aggregate resolver pattern in 9.5 entirely. This is deliberately rejected: a popular, frequently-run Flow could accumulate thousands of historical runs, and loading the full aggregate for any operation (even a simple flow-metadata edit) would become increasingly expensive over time — directly working against NFR-PERF-2 (200ms p95 for flow CRUD) as run history grows, which NFR-SCALE-3 explicitly anticipates scaling to millions of records. Keeping FlowRun as its own aggregate, referencing Flow only by immutable version identifier, is what keeps the two consistency boundaries independently scalable — directly mirroring the container-separation rationale already established in Part I, Section 4.4.



10. Entities
10.1 Entities versus value objects — the dividing line applied consistently

An object is modeled as an entity in this domain when it has a lifecycle and an identity that matters independent of its current attribute values — two FlowSteps with identical parameters are still distinct steps if they occupy different positions in a flow, for example. Everything else (Section 11) is a value object. This section covers the entities that live within the three aggregates defined in Section 9 (not the aggregate roots themselves, already covered there) plus the two entities belonging to the Identity & Access and Reporting & Audit contexts.

10.2 FlowStep (within the Flow aggregate)

Identity: a FlowStepId, stable across edits to the step's own parameters within a Draft, but — per Section 9.2's immutability invariant — never mutated after the parent Flow is Published.

csharp
public class FlowStep : Entity<FlowStepId>
{
    public string Name { get; private set; }
    public ModuleKind Module { get; private set; }
    public string Action { get; private set; }
    public IReadOnlyDictionary<string, object?> Params { get; private set; }
    public string? SaveAs { get; private set; }
    public string? WhenCondition { get; private set; }
    public int Order { get; private set; }  // explicit, not inferred from list position,
                                             // so that step order survives serialization
                                             // round-trips unambiguously
}

The Params dictionary is deliberately weakly-typed (object? values) at this layer — per Section 6.4's discussion of the plugin-contract boundary, FlowStep cannot know the specific shape a given module's action expects at compile time, since that shape is defined by the ActionSchema value object (Section 11) belonging to a different aggregate (ModuleRegistration), resolved only at validation/execution time. Strong typing is recovered at the module-implementation boundary, not here.

10.3 StepResult (within the FlowRun aggregate)

Identity: implicitly positional within its parent FlowRun (a StepResultId exists for referential purposes — e.g., linking an AuditEntry back to a specific result — but StepResults are never looked up or modified independent of their owning FlowRun, consistent with the aggregate-boundary discipline from Section 9.1).

csharp
public class StepResult : Entity<StepResultId>
{
    public FlowStepId SourceStepId { get; private set; }  // which FlowStep this result is for
    public string StepName { get; private set; }           // denormalized for reporting convenience —
                                                              // see the note in 10.3.1 below
    public ModuleKind Module { get; private set; }
    public string Action { get; private set; }
    public StepStatus Status { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }
    public object? Data { get; private set; }
    public AiAttribution? AiAttribution { get; private set; }  // Section 11 — never silently null
                                                                  // when AI materially altered the step
    public IReadOnlyList<RetryAttempt> RetryHistory { get; private set; }
}

10.3.1 — Why StepName and Module/Action are denormalized onto StepResult rather than always joined from FlowStep: because FlowSteps can theoretically be edited in a Draft between when historical runs occurred and when a report is viewed (a new Flow version created per Section 9.2), and because FlowRun deliberately pins to an immutable Flow version — the denormalized fields on StepResult guarantee a report always reflects exactly what ran, not what the step currently looks like in a possibly-newer flow version. This is a direct, deliberate application of Architecture Principle 20.4 (fail loud, fail informatively) extended to historical accuracy: a report should never silently misrepresent what actually executed.

10.4 RetryAttempt (within the FlowRun aggregate, child of StepResult)
csharp
public class RetryAttempt : Entity<RetryAttemptId>
{
    public int AttemptNumber { get; private set; }
    public StepStatus Status { get; private set; }
    public TimeSpan Duration { get; private set; }
    public string? Error { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }
}

This entity exists specifically to make the platform's own flake-rate metric (Volume 0, NFR-REL-2) computable directly from the domain model — a query (Section 14) can count FlowRuns whose final StepResult is Passed but whose RetryHistory is non-empty, without needing a separate, parallel tracking mechanism bolted on outside the core execution model.

10.5 ModuleActionEntry (within the ModuleRegistration aggregate)

A thin entity wrapping each ActionSchema value object (Section 11) with registration-specific metadata not intrinsic to the schema itself:

csharp
public class ModuleActionEntry : Entity<ModuleActionEntryId>
{
    public string ActionName { get; private set; }
    public ActionSchema Schema { get; private set; }        // value object, Section 11
    public bool IsDeprecated { get; private set; }
    public string? DeprecationNotice { get; private set; }
}
10.6 User (Identity & Access context)
csharp
public class User : Entity<UserId>
{
    public string Email { get; private set; }
    public string DisplayName { get; private set; }
    public TenantId TenantId { get; private set; }
    public IReadOnlyList<RoleId> AssignedRoles { get; private set; }
    public bool IsServiceAccount { get; private set; }  // distinguishes Marcus's CI-triggered
                                                            // automation identity from human users,
                                                            // relevant to audit-trail attribution
                                                            // (Part IV, Section 27)
}

User is modeled as an entity, not an aggregate root, in this Part's scope — full aggregate treatment of the Identity & Access context (including Role and Tenant) is deferred to Part IV (Authentication, Authorization, Multi-tenancy), since Identity & Access's domain model is comparatively conventional (standard RBAC modeling) and doesn't need the same tactical-DDD depth as the Flow Authoring/Execution/Module & Plugin contexts that are Nexus's actual novel domain.

10.7 AuditEntry (Reporting & Audit context)
csharp
public class AuditEntry : Entity<AuditEntryId>
{
    public DateTimeOffset OccurredAt { get; private set; }
    public UserId ActorId { get; private set; }              // shared-kernel reference, Section 7.4
    public string ActionType { get; private set; }            // e.g., "FlowPublished", "RoleChanged"
    public string TargetType { get; private set; }            // e.g., "Flow", "User"
    public string TargetId { get; private set; }
    public IReadOnlyDictionary<string, string> Metadata { get; private set; }
    public TenantId TenantId { get; private set; }
}

Per NFR-SEC-3, AuditEntry records are append-only at the persistence layer (Part V) — this entity has no update methods at all, only a constructor, which is itself a structural (not just policy-level) enforcement of immutability.

10.8 Entity identity strategy

All entity identifiers (FlowStepId, StepResultId, UserId, etc.) are strongly-typed value-object wrappers around a Guid (not raw Guid or int primitives), following the standard DDD "strongly-typed ID" pattern. This is a deliberate, non-negotiable convention across the entire domain model, for one specific reason directly traceable to Architecture Principle 20.2: a method signature like void RecordStepResult(FlowRunId runId, StepResultId resultId) cannot have its arguments accidentally transposed at a call site the way void RecordStepResult(Guid runId, Guid resultId) could — this class of bug is eliminated at compile time, not caught in code review or, worse, in production. Volume 1 Part VI's Coding Standards section formalizes this as a mandatory pattern with a code-review checklist item.



11. Value Objects
11.1 Value object discipline

Every value object in this domain is immutable, has no identity (two instances with equal component values are interchangeable and compare equal), and validates its own invariants in its constructor — a value object that exists at all is, by construction, always valid. This section catalogs the value objects referenced throughout Sections 9 and 10.

11.2 ModuleKind
csharp
public readonly record struct ModuleKind
{
    public string Value { get; }
    private ModuleKind(string value) => Value = value;

    public static readonly ModuleKind Api = new("api");
    public static readonly ModuleKind Web = new("web");
    public static readonly ModuleKind Mobile = new("mobile");
    public static readonly ModuleKind DesktopUi = new("ui");
    public static readonly ModuleKind Logs = new("logs");
    public static readonly ModuleKind Ai = new("ai");

    public static ModuleKind Custom(string value)
    {
        // Marketplace-installed modules (Volume 0, Section 17) register additional
        // ModuleKind values at runtime — this factory validates format (lowercase,
        // no whitespace, per the same convention as the six built-in kinds) without
        // restricting the value to a fixed enum, which would directly contradict
        // FR-ORCH-6's "add a module without changing the core" requirement.
        if (string.IsNullOrWhiteSpace(value) || value != value.ToLowerInvariant())
            throw new DomainException($"Invalid ModuleKind: '{value}'");
        return new ModuleKind(value);
    }
}

This is a deliberate departure from a closed C# enum — an enum would hardcode the six built-in kinds at compile time and structurally prevent third-party modules from ever being first-class ModuleKind values, directly violating Architecture Principle 20.3 (every module, built-in or not, treated identically). The record struct with named static instances plus an open Custom factory gives built-in modules ergonomic, typo-proof references in code while keeping the type open for extension.

11.3 StepStatus and RunStatus
csharp
public enum StepStatus { Passed, Failed, Skipped }
public enum RunStatus { Pending, Running, Passed, Failed, Cancelled }

Unlike ModuleKind, these are closed enums — the set of possible outcomes for a step or run is a fixed, core piece of the orchestration model (FR-ORCH-1 through FR-ORCH-5) that no plugin should ever be able to extend, per Architecture Principle 20.3's requirement that reporting be uniform across every module. A module cannot invent a fourth StepStatus value; it can only ever report one of these three.

11.4 FlowStatus
csharp
public enum FlowStatus { Draft, Published, Archived }
11.5 AiAttribution
csharp
public sealed record AiAttribution
{
    public string Capability { get; init; }       // "selector-heal" | "flow-generation" | "log-triage"
    public double Confidence { get; init; }
    public string? OriginalValue { get; init; }     // e.g., the broken selector, for audit/review
    public string? SuggestedValue { get; init; }    // e.g., the healed selector
    public bool WasAccepted { get; init; }          // false if below-threshold and only advisory (UC-3)
    public string ModelIdentifier { get; init; }     // which provider/model produced this, for
                                                        // the evaluation-metrics tracking in
                                                        // Volume 0, Section 18.7
}

This value object is the direct, concrete implementation of FR-AI-3's requirement that every AI-assisted intervention be recorded distinguishably. Its presence (or deliberate absence) on a StepResult is itself meaningful — the type system enforces that a caller checking stepResult.AiAttribution is not null is a reliable, structurally-guaranteed way to detect AI involvement, not a convention that could be silently forgotten in one code path.

11.6 Assertion

Formalizing the assertion shapes already prototyped informally in this project's earlier exploration (status/equals/contains/exists):

csharp
public abstract record Assertion;
public sealed record StatusAssertion(int ExpectedStatus) : Assertion;
public sealed record PathEqualsAssertion(string Path, object? ExpectedValue) : Assertion;
public sealed record PathContainsAssertion(string Path, object ExpectedFragment) : Assertion;
public sealed record PathExistsAssertion(string Path) : Assertion;

Modeled as a closed hierarchy of record types (a discriminated union, C#-idiomatic via inheritance plus pattern matching) rather than a single class with nullable fields for each possible assertion kind — this is a direct application of "make illegal states unrepresentable," ensuring, for instance, that a PathEqualsAssertion can never be constructed without both a Path and an ExpectedValue, where a flat-fields design would allow a partially-specified, ambiguous assertion object to exist.

11.7 VariableExpression
csharp
public sealed record VariableExpression
{
    public string RawTemplate { get; init; }  // e.g., "{{vars.orgInfo.repos_url}}"

    public IReadOnlyList<string> ReferencedPaths { get; init; }
    // Parsed at construction time (not resolution time) so that a Flow can be
    // statically validated at publish time (Section 9.2) for referencing only
    // variables that some prior step in the same Flow actually declares via
    // saveAs — catching a whole class of "flow references a variable that will
    // never exist" errors before the Flow is ever run, per Architecture
    // Principle 20.4's fail-fast philosophy applied as early as possible in
    // the lifecycle, not just at execution time.
}
11.8 ActionSchema
csharp
public sealed record ActionSchema
{
    public string ActionName { get; init; }
    public IReadOnlyList<ParamSpec> Parameters { get; init; }
    public string? Description { get; init; }
}

public sealed record ParamSpec
{
    public string Name { get; init; }
    public ParamType Type { get; init; }        // String | Number | Boolean | Object | Array
    public bool Required { get; init; }
    public object? DefaultValue { get; init; }
}

This is the formalized version of the module-action schema referenced conceptually in Part I, Section 5.4 — it is the shared contract both the Flow Parser (validating a FlowStep's Params at publish time) and the Module Registration API (exposing available actions to the GUI, per FR-ORCH-7) depend on.

11.9 TrustTier
csharp
public enum TrustTier { BuiltIn, Verified, Community }

Directly maps to Volume 0's marketplace certification tiers (Section 17.3), now a first-class part of the runtime domain model rather than existing only as a marketplace-website concept — this is what makes an enterprise policy like "only install Verified-or-above modules" (Volume 0, Section 17.3's enterprise-tier scenario) enforceable at the ModuleRegistration aggregate level (Section 9.4), not just as marketplace UI friction.

11.10 Strongly-typed identifiers

Referenced throughout Section 10; formalized here as the value-object pattern they are:

csharp
public readonly record struct FlowId(Guid Value);
public readonly record struct FlowRunId(Guid Value);
public readonly record struct FlowStepId(Guid Value);
public readonly record struct StepResultId(Guid Value);
public readonly record struct ModuleRegistrationId(Guid Value);
public readonly record struct UserId(Guid Value);
public readonly record struct TenantId(Guid Value);
// ...and so on for every entity/aggregate identifier in Sections 9-10.

Per Section 10.8's rationale, every one of these is a distinct type, not a type alias — C#'s record struct gives value-object equality semantics and zero runtime allocation overhead relative to a raw Guid, making this pattern effectively free at runtime while providing a real compile-time safety guarantee.



12. Domain Events
12.1 Purpose and relationship to the Event Bus component

Domain events are the formalized payloads that flow through the Event Bus component introduced in Part I, Section 5.3, and are the concrete mechanism implementing the Published Language integration pattern between contexts (Section 7.3). Every domain event is immutable, past-tense named (an event describes something that already happened, never a command to make something happen — that distinction is Section 13's job), and carries only the data its known consumers (SignalR streaming, audit logging, report projection) actually need — not a full aggregate snapshot, to keep the Published Language contract minimal and stable per Architecture Principle 20.2.

12.2 Event catalog — Flow Authoring context
csharp
public sealed record FlowPublishedEvent(
    FlowId FlowId, FlowFamilyId FamilyId, int Version, TenantId TenantId, UserId PublishedBy, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record FlowArchivedEvent(
    FlowId FlowId, TenantId TenantId, UserId ArchivedBy, DateTimeOffset OccurredAt
) : IDomainEvent;
12.3 Event catalog — Execution context
csharp
public sealed record FlowRunStartedEvent(
    FlowRunId RunId, FlowId FlowId, RunTrigger Trigger, TenantId TenantId, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record StepCompletedEvent(
    FlowRunId RunId, StepResult Result, DateTimeOffset OccurredAt
) : IDomainEvent;
// This is the single highest-frequency event in the system (one per step, per run)
// and is the direct data source for SignalR's live progress streaming (Part V,
// Section 31) — its payload is deliberately the full StepResult, since the GUI
// needs the complete outcome to render immediately, not a partial summary
// requiring a follow-up query.

public sealed record StepSelfHealedEvent(
    FlowRunId RunId, StepResultId ResultId, AiAttribution Attribution, DateTimeOffset OccurredAt
) : IDomainEvent;
// Raised in addition to (never instead of) StepCompletedEvent whenever a step's
// AiAttribution is non-null, per FR-AI-3 — giving audit logging and any future
// AI-specific monitoring a dedicated event to subscribe to without needing to
// inspect every StepCompletedEvent's payload for an optional field.

public sealed record FlowRunCompletedEvent(
    FlowRunId RunId, RunStatus FinalStatus, int PassedCount, int FailedCount, int SkippedCount, TimeSpan TotalDuration, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record FlowRunFlakeDetectedEvent(
    FlowRunId RunId, StepResultId ResultId, int RetryCount, DateTimeOffset OccurredAt
) : IDomainEvent;
// Raised specifically to feed Volume 0's platform flake-rate metric (NFR-REL-2)
// as a first-class, independently queryable event stream, rather than requiring
// that metric to be computed by scanning RetryHistory after the fact.
12.4 Event catalog — Module & Plugin context
csharp
public sealed record ModuleRegisteredEvent(
    ModuleRegistrationId RegistrationId, ModuleKind Kind, TrustTier Trust, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record ModuleDeprecatedActionEvent(
    ModuleRegistrationId RegistrationId, string ActionName, string Notice, DateTimeOffset OccurredAt
) : IDomainEvent;
// Consumed by Flow Authoring (to warn flow authors whose published flows
// reference a soon-to-be-removed action) — a cross-context notification
// delivered via the Published Language mechanism (Section 7.3) rather than
// a direct query, keeping the two contexts decoupled even for this warning.
12.5 Event catalog — Identity & Access context
csharp
public sealed record UserRoleChangedEvent(
    UserId UserId, IReadOnlyList<RoleId> PreviousRoles, IReadOnlyList<RoleId> NewRoles, UserId ChangedBy, TenantId TenantId, DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CredentialRotatedEvent(
    string CredentialReference, TenantId TenantId, UserId RotatedBy, DateTimeOffset OccurredAt
) : IDomainEvent;
// Deliberately never carries the credential value itself, per NFR-SEC-3 —
// only a reference/label, since this event is consumed by the audit pipeline
// (Section 12.6) which must never persist a secret value even transiently.
12.6 Event consumers — who subscribes to what, and why
Event Consumers   Purpose
StepCompletedEvent   SignalR Hub, Audit pipeline, Report projector   Live progress (GUI), immutable audit trail, FlowReport materialization
StepSelfHealedEvent  Audit pipeline, AI evaluation-metrics collector (Volume 0, Section 18.7)   Transparent AI-attribution record, self-heal acceptance-rate tracking
FlowRunCompletedEvent   SignalR Hub, Audit pipeline, Report projector, CI webhook notifier (if configured)  Final status delivery to every downstream consumer that cares about run completion
FlowRunFlakeDetectedEvent  Platform-health metrics collector   NFR-REL-2 flake-rate computation, independent of any single flow's reporting
FlowPublishedEvent   Audit pipeline, Module & Plugin context (to track which modules/actions are actively referenced, feeding the deprecation-safety check in Section 9.4) Audit trail, cross-context deprecation safety
ModuleDeprecatedActionEvent   Flow Authoring (author-facing warnings)   Proactive breaking-change notification
UserRoleChangedEvent, CredentialRotatedEvent Audit pipeline exclusively NFR-SEC-3 compliance
12.7 Event delivery guarantees

Nexus's domain events are delivered with at-least-once semantics within a single deployment (via the Event Bus component, Part I Section 5.3, backed by an in-process publish/subscribe mechanism for GA, with the explicit option to back it with a durable message broker for larger self-hosted deployments per NFR-SCALE-2 — this specific technology choice is a Part III, Section 22 Event Bus decision, not fixed here). Every consumer listed in 12.6 is therefore required to be idempotent with respect to duplicate event delivery (e.g., the Report projector must upsert, not blindly insert, when materializing a FlowReport from a StepCompletedEvent it may have already processed). This requirement is stated here, at the domain-event-catalog level, because it constrains every consumer's design — it is a contract obligation on anyone subscribing to these events, not an implementation detail internal to the Event Bus itself.

12.8 Why domain events, not direct method calls, for cross-context communication

This is a direct, deliberate application of the Published Language integration pattern chosen in Section 7.3: if the Execution context called Reporting & Audit's projection logic directly (a synchronous method call), the two contexts would be coupled at compile time, and a change to how reports are materialized could force a redeploy of the Execution context, or — worse — a slow report-projection operation could add latency to the step-dispatch hot path, directly threatening NFR-PERF-1's 50ms p95 orchestrator-overhead budget. Publishing an event and letting Reporting & Audit consume it asynchronously keeps the Execution context's step-dispatch loop free of any dependency on reporting-specific logic or performance characteristics — a direct, load-bearing consequence of Architecture Principle 20.3's "orchestrator core has no module- or consumer-specific branching" principle, now extended to cross-context consumers generally, not just modules specifically.




13. Commands
13.1 Commands versus domain events versus queries

A command is an intent to change state, issued imperatively (present-tense-named — PublishFlow, not FlowPublished), which may be accepted or rejected by the domain model's invariants (Section 9). A domain event (Section 12) is the record that a change already happened, issued only after a command succeeds. This section catalogs Nexus's commands, organized by bounded context, and each command handler's responsibility is stated precisely: validate authorization (delegating to Identity & Access, Part IV), load the relevant aggregate via its repository (Part III, Section 15), invoke the aggregate's behavior, persist via Unit of Work (Part III, Section 16), and let the aggregate's own domain-event-raising (already shown in Section 9's code sketches) handle event publication.

13.2 Command catalog — Flow Authoring context
csharp
public sealed record CreateFlowCommand(string Name, string? Description, TenantId TenantId, UserId AuthoredBy) : ICommand<FlowId>;

public sealed record AddFlowStepCommand(FlowId FlowId, string StepName, ModuleKind Module, string Action, IReadOnlyDictionary<string, object?> Params, string? SaveAs, string? When) : ICommand;

public sealed record PublishFlowCommand(FlowId FlowId, UserId PublishedBy) : ICommand<FlowPublishResult>;
// Returns a result object, not a bare success/failure boolean, because a
// rejected publish attempt (Section 9.2's invariant — unresolved module
// reference) needs to communicate *which* step and *which* module reference
// failed resolution, per Architecture Principle 20.4's "fail informatively."

public sealed record ArchiveFlowCommand(FlowId FlowId, UserId ArchivedBy) : ICommand;

public sealed record GenerateFlowFromDescriptionCommand(string Description, IReadOnlyList<ModuleKind> AllowedModules, TenantId TenantId, UserId RequestedBy) : ICommand<FlowGenerationResult>;
// This command deliberately produces a *Draft* Flow, never a Published one —
// directly enforcing Volume 0's permanent AI boundary (Section 18.4): the
// AI module's output always lands as a command a human still has to review
// and separately issue PublishFlowCommand against, never as a bypass around
// the normal command pipeline.
13.3 Command catalog — Execution context
csharp
public sealed record TriggerFlowRunCommand(FlowId FlowId, RunTrigger Trigger, IReadOnlyDictionary<string, object?>? VariableOverrides, TenantId TenantId) : ICommand<FlowRunId>;

public sealed record CancelFlowRunCommand(FlowRunId RunId, UserId CancelledBy) : ICommand;
// Not explicitly named as a functional requirement in Volume 0's FR list,
// but required by NFR-REL-1's availability commitment in practice — a
// customer must be able to abort a run consuming resources unexpectedly,
// and this is called out here specifically so Part III's Execution Engine
// design does not treat cancellation as an afterthought.

public sealed record RequestSelectorHealCommand(FlowRunId RunId, StepResultId FailedResultId, string BrokenSelector, string Intent, string CurrentPageHtml) : ICommand<SelectorHealResult>;
// Internal command, issued by the orchestration engine itself (not directly
// by an end user) when a Web-module step fails and self-healing is enabled
// for the flow — included in this catalog because it still passes through
// the same command-handling, authorization, and audit pipeline as any
// user-issued command, per Architecture Principle 20.5 (security/compliance
// posture designed in, not bolted on) applied even to internally-originated
// commands.
13.4 Command catalog — Module & Plugin context
csharp
public sealed record RegisterModuleCommand(ModuleKind Kind, string DisplayName, TrustTier Trust, IReadOnlyList<ActionSchema> Actions, SemanticVersion ContractVersion, UserId RegisteredBy) : ICommand<ModuleRegistrationId>;

public sealed record DeprecateModuleActionCommand(ModuleRegistrationId RegistrationId, string ActionName, string Notice, UserId RequestedBy) : ICommand;

public sealed record DeactivateModuleCommand(ModuleRegistrationId RegistrationId, UserId RequestedBy) : ICommand;
13.5 Command catalog — Identity & Access context
csharp
public sealed record AssignRoleCommand(UserId UserId, RoleId RoleId, UserId AssignedBy, TenantId TenantId) : ICommand;

public sealed record RevokeRoleCommand(UserId UserId, RoleId RoleId, UserId RevokedBy, TenantId TenantId) : ICommand;

public sealed record RotateCredentialCommand(string CredentialReference, TenantId TenantId, UserId RotatedBy) : ICommand;
// The command handler for this delegates the actual secret-value rotation
// to the secrets manager integration (Vault, per Volume 0 Section 21.13) —
// this command's own payload, per Section 12.5's corresponding event,
// never carries the credential value itself.
13.6 Command validation — two distinct layers

Every command handler enforces validation at two distinct, deliberately separated layers, and this separation is stated explicitly because conflating them is a common source of bugs:

Structural/authorization validation, performed by the command handler before the aggregate is even loaded: is the command well-formed (required fields present), and is the issuing UserId authorized (per Identity & Access, Part IV) to issue this command against this TenantId's data at all. A failure here never touches the domain model.
Domain invariant validation, performed by the aggregate itself once loaded (Section 9's invariants — e.g., Flow.Publish's module-resolution check). A failure here is a legitimate domain-level rejection (like FlowPublishResult in 13.2), not a system error.

This two-layer separation is what keeps aggregates (Section 9) free of any authorization logic — an aggregate should never need to know who is asking, only whether the requested state transition is valid given the current state, keeping the domain model's testability and portability (per Architecture Principle 20.7, composable primitives) fully intact independent of the authorization system's own evolution (Part IV).

13.7 Command handler pattern (illustrative)
csharp
public class PublishFlowCommandHandler : ICommandHandler<PublishFlowCommand, FlowPublishResult>
{
    private readonly IFlowRepository _flows;          // Part III, Section 15
    private readonly IModuleActionResolver _resolver;  // the port from Section 9.5
    private readonly IUnitOfWork _unitOfWork;          // Part III, Section 16
    private readonly IAuthorizationService _authz;      // Part IV, Section 25

    public async Task<FlowPublishResult> Handle(PublishFlowCommand cmd, CancellationToken ct)
    {
        await _authz.EnsureCanPublish(cmd.PublishedBy, cmd.FlowId, ct);  // layer 1, Section 13.6

        var flow = await _flows.GetById(cmd.FlowId, ct)
            ?? throw new NotFoundException(nameof(Flow), cmd.FlowId);

        var result = flow.Publish(_resolver);  // layer 2, Section 13.6 — domain invariant, in-aggregate

        if (result.Succeeded)
        {
            await _flows.Save(flow, ct);
            await _unitOfWork.Commit(ct);       // persists state + dispatches raised domain events together
        }

        return result;
    }
}
13.8 Why commands are explicit types rather than direct service-method calls

A conventional alternative would be plain service classes with methods (FlowService.PublishFlow(flowId, userId)) rather than an explicit ICommand/ICommandHandler pattern. Nexus adopts the explicit-command pattern for three reasons directly traceable to earlier decisions: first, it gives every state-changing operation a uniform shape that the audit pipeline (Section 12.6) and the CQRS separation (Part III, Section 23) can both handle generically rather than needing bespoke logging/dispatch code per service method. Second, it makes the full inventory of "everything that can change state in this system" enumerable and reviewable as a flat catalog (this section, in its entirety) — directly supporting the security-review use case (Volume 0, UC-7) where Persona Elena benefits from a complete, auditable list of possible write operations rather than having to read through service-class implementations to discover them. Third, it composes cleanly with Volume 0's AI-generation boundary (Section 18.4) — GenerateFlowFromDescriptionCommand in 13.2 is, structurally, just another command in the same catalog, subject to the same authorization and audit treatment as any human-issued command, rather than a special-cased AI code path.



14. Queries
14.1 Queries as a distinct concept from commands

Where Section 13's commands express intent to change state, queries express intent to read state, and — per the CQRS approach this domain model anticipates (fully justified in Part III, Section 23) — queries in Nexus are free to read from denormalized, purpose-built read models rather than always reconstructing an answer from the aggregates in Section 9. This section catalogs the query surface, organized by the use case it primarily serves (tying directly back to Volume 0's Use Cases, Section 11, and Personas, Section 10) rather than strictly by bounded context, since queries — unlike commands — frequently need to read across context boundaries to answer a single question a real user actually has.

14.2 Query catalog — Flow Authoring & GUI support
csharp
public sealed record GetFlowByIdQuery(FlowId FlowId, TenantId TenantId) : IQuery<FlowDetailDto>;

public sealed record ListFlowsQuery(TenantId TenantId, FlowStatus? StatusFilter, string? SearchTerm, PageRequest Page) : IQuery<PagedResult<FlowSummaryDto>>;

public sealed record GetAvailableModuleActionsQuery(TenantId TenantId) : IQuery<IReadOnlyList<ModuleActionDto>>;
// The direct, formal query behind FR-ORCH-7's introspectable module-action
// API, and the specific query the GUI's flow-authoring surface calls to
// auto-populate available steps — this is the query-side implementation of
// Volume 0's UC-6 requirement that a newly-registered custom module's
// actions "automatically" appear in the authoring UI with no custom
// frontend work, since the GUI's rendering logic is entirely driven by
// whatever this query currently returns.

public sealed record GetFlowVersionHistoryQuery(FlowFamilyId FamilyId, TenantId TenantId) : IQuery<IReadOnlyList<FlowVersionSummaryDto>>;
14.3 Query catalog — Execution & live progress
csharp
public sealed record GetFlowRunByIdQuery(FlowRunId RunId, TenantId TenantId) : IQuery<FlowRunDetailDto>;

public sealed record ListRecentFlowRunsQuery(TenantId TenantId, FlowId? FlowIdFilter, RunStatus? StatusFilter, PageRequest Page) : IQuery<PagedResult<FlowRunSummaryDto>>;

public sealed record GetLiveRunProgressQuery(FlowRunId RunId, TenantId TenantId) : IQuery<RunProgressDto>;
// Used for the initial state fetch when a client subscribes to a SignalR
// hub (Part V, Section 31) for a run already in progress — SignalR then
// takes over for incremental updates via StepCompletedEvent (Section 12),
// so this query's role is specifically the "catch up to current state"
// case, not the ongoing streaming mechanism itself.
14.4 Query catalog — Reporting & Audit (Persona Sofia's primary surface)
csharp
public sealed record GetFlowReportQuery(FlowRunId RunId, TenantId TenantId) : IQuery<FlowReportDto>;
// Direct implementation of FR-REP-1 — returns the unified pass/fail/skip
// report for a single run.

public sealed record GetPassRateTrendQuery(TenantId TenantId, ModuleKind? ModuleFilter, DateRange Range) : IQuery<TrendSeriesDto>;
// Direct implementation of FR-REP-3/FR-REP-4 and Volume 0's UC-5 — this is
// the query backing Sofia's monthly-review dashboard trend view, required
// by NFR-PERF-3 to render in under 2 seconds at p95 for up to 10,000
// historical runs, which is the direct justification (Part III, Section 23)
// for this query reading from a materialized, pre-aggregated read model
// rather than computing trend statistics live from FlowRun aggregates on
// every request.

public sealed record GetFlakeRateQuery(TenantId TenantId, DateRange Range) : IQuery<FlakeRateDto>;
// Backs Volume 0's platform-health flake-rate metric (NFR-REL-2), sourced
// from FlowRunFlakeDetectedEvent projections (Section 12.3/12.6), not
// computed by re-scanning every FlowRun's RetryHistory per request.

public sealed record GetSelfHealAcceptanceRateQuery(TenantId TenantId, DateRange Range) : IQuery<SelfHealMetricsDto>;
// Backs Volume 0 Section 18.7's self-heal acceptance-rate quality metric.

public sealed record DrillDownFailureSignatureQuery(TenantId TenantId, ModuleKind Module, DateRange Range) : IQuery<IReadOnlyList<FailureClusterDto>>;
// Direct implementation of the drill-down capability in Volume 0's UC-5
// step 3 ("the dashboard correlates it with a specific flow and a specific
// underlying failure signature") — groups failed StepResults by normalized
// error-message signature so Sofia can go from "pass rate dropped" to
// "here's the one recurring failure causing it" in a single query.

public sealed record ExportAuditLogQuery(TenantId TenantId, DateRange Range, string? ActionTypeFilter) : IQuery<Stream>;
// Serves Persona Elena's compliance-review needs (NFR-SEC-3) — returns a
// streamed export rather than an in-memory collection, since audit exports
// can span the full 1-year minimum retention window (NFR-SEC-3) and should
// not risk memory pressure on the API Layer container.
14.5 Query catalog — Module & Plugin (Persona Raj's support)
csharp
public sealed record GetModuleRegistrationQuery(ModuleRegistrationId RegistrationId, TenantId TenantId) : IQuery<ModuleRegistrationDetailDto>;

public sealed record ListRegisteredModulesQuery(TenantId TenantId, TrustTier? TrustFilter) : IQuery<IReadOnlyList<ModuleRegistrationSummaryDto>>;
// The TrustFilter parameter directly supports the enterprise policy
// scenario from Volume 0 Section 17.3 — "restrict this org's instance to
// Verified-or-above modules only" is implementable as this query filtered
// at the API layer by an org-level policy setting, composed with this
// existing query rather than requiring a bespoke policy-aware query type.
14.6 Query catalog — Identity & Access
csharp
public sealed record GetUserPermissionsQuery(UserId UserId, TenantId TenantId) : IQuery<IReadOnlyList<Permission>>;

public sealed record ListUsersQuery(TenantId TenantId, PageRequest Page) : IQuery<PagedResult<UserSummaryDto>>;
14.7 Query handler pattern and read-model sourcing

Unlike command handlers (Section 13.7), which always load a full aggregate via its repository to enforce invariants, query handlers are explicitly permitted — and, for the reporting-heavy queries in 14.4, expected — to read from purpose-built, denormalized read models (projected asynchronously from domain events, per Section 12.6's Report projector) rather than reconstructing an answer from aggregate state on every call. This distinction is the practical essence of the CQRS pattern this domain model is designed to support, fully justified and detailed in Part III, Section 23; this section's role is only to establish that the query catalog itself already anticipates and is structured for that separation, rather than CQRS being retrofitted onto an initially command-query-symmetric design.

14.8 DTOs are a deliberate, separate concern from domain entities

Every query in this catalog returns a DTO type (FlowDetailDto, FlowRunSummaryDto, etc.), never a domain entity or aggregate directly. This is a firm rule, not a convention with exceptions: domain entities (Section 10) carry behavior and invariant-enforcement methods that have no business being exposed across the API boundary (Part V), and DTOs are free to shape data specifically for their consuming use case (e.g., FlowRunSummaryDto for a list view carries far less data than FlowRunDetailDto for a single-run deep-dive) in a way that would be awkward or invariant-violating to achieve by serializing domain entities directly. Mapping between domain entities/read-models and DTOs is the query handler's explicit responsibility, kept as a thin, well-tested mapping layer rather than mixed into either the domain model or the API controllers (Part V, Section 30).

___________________________________________________________________________________________________________________________________________________



15. Repository Pattern
15.1 Purpose within Nexus's architecture

The repository pattern is the persistence-abstraction boundary sitting directly beneath each of Part II's three aggregate roots (Flow, FlowRun, ModuleRegistration). Its job is narrow and specific: load an aggregate to full consistency, and persist an aggregate's changes atomically — nothing else. Repositories in Nexus are deliberately not generic data-access utilities; there is no IRepository<T> that every entity gets for free. Each aggregate root gets exactly one purpose-built repository interface, because a generic repository abstraction tends to leak query concerns (Part II, Section 14's queries) back into what should be a strictly write-side, consistency-boundary-respecting contract.

15.2 One repository per aggregate root — no exceptions
csharp
public interface IFlowRepository
{
    Task<Flow?> GetById(FlowId id, CancellationToken ct);
    Task<Flow?> GetLatestVersionByFamily(FlowFamilyId familyId, CancellationToken ct);
    Task Save(Flow flow, CancellationToken ct);
}

public interface IFlowRunRepository
{
    Task<FlowRun?> GetById(FlowRunId id, CancellationToken ct);
    Task Save(FlowRun run, CancellationToken ct);
}

public interface IModuleRegistrationRepository
{
    Task<ModuleRegistration?> GetById(ModuleRegistrationId id, CancellationToken ct);
    Task<ModuleRegistration?> GetByKind(ModuleKind kind, TenantId tenantId, CancellationToken ct);
    Task Save(ModuleRegistration registration, CancellationToken ct);
}

Note what is deliberately absent: no GetAll(), no Find(predicate), no pagination parameters. Per Section 14.7 of Part II, anything resembling a list, search, or filter operation is a query, served by a separate read-model-backed query handler, never by the repository. A repository that grows a Find(Expression<Func<Flow, bool>> predicate) method is a repository that has started leaking query concerns back into the write side, and should be treated as a design defect during code review, not a convenience.

15.3 Why not a generic IRepository<TAggregate, TId>

A generic repository interface is a common pattern in less domain-rich systems, and was deliberately rejected here for a reason directly traceable to Section 9.5 of Part II: each aggregate's Save operation has different consistency requirements. Flow.Save must never overwrite a Published version's steps (Section 9.2's immutability invariant) — this is naturally expressed as a specific implementation behind IFlowRepository.Save, not as a generic method that would need runtime type-checking to enforce an aggregate-specific rule. A generic interface would either force these aggregate-specific behaviors into the aggregates themselves (violating persistence-ignorance, discussed in 15.5) or into a bloated generic implementation with special-casing per aggregate type — both worse than three small, purpose-specific interfaces.

15.4 Repository implementation — illustrative sketch
csharp
public class FlowRepository : IFlowRepository
{
    private readonly NexusDbContext _db;  // EF Core DbContext, Part V, Section 32

    public async Task<Flow?> GetById(FlowId id, CancellationToken ct)
    {
        // Loads the Flow aggregate root together with its FlowStep child
        // entities in one round-trip (Include/ThenInclude, or a compiled
        // projection — the specific EF Core mechanism is an implementation
        // detail per Section 6.5 of Part II, not fixed at this level), since
        // an aggregate must always be loaded to full consistency, never
        // partially, per standard DDD repository discipline.
        return await _db.Flows
            .Include(f => f.Steps)
            .FirstOrDefaultAsync(f => f.Id == id.Value, ct)
            is { } entity ? FlowMapper.ToDomain(entity) : null;
    }

    public async Task Save(Flow flow, CancellationToken ct)
    {
        var entity = FlowMapper.ToPersistence(flow);
        _db.Flows.Update(entity);
        // Deliberately does NOT call SaveChangesAsync here — see Section 16
        // (Unit of Work). A repository's Save stages the change; committing
        // it (together with any other aggregate changes and domain event
        // dispatch in the same logical operation) is the Unit of Work's job.
    }
}
15.5 Persistence ignorance — domain entities never reference EF Core directly

The Flow, FlowStep, FlowRun, and ModuleRegistration types defined in Part II carry no Entity Framework attributes, no navigation-property conventions bending their shape to the ORM's preferences, and no dependency on NexusDbContext whatsoever. A separate, explicit mapping layer (FlowMapper in 15.4's sketch) translates between the domain model's own shape and a set of EF Core-specific persistence entities. This is a deliberate cost (an extra mapping layer to write and maintain) accepted for a specific reason: it keeps Part II's domain model — the most carefully-designed, invariant-rich part of this entire volume — fully unit-testable with zero database dependency, and fully insulated from any future decision to change persistence technology (a decision this blueprint does not anticipate needing, but Architecture Principle 20.2's contract-stability discipline suggests protecting against regardless).

15.6 Repository scope and tenant isolation

Every repository method that accepts a TenantId (directly, or implicitly via an aggregate that carries one, per Part II Section 7.4's shared-kernel design) must filter at the query level, never at the application-service level after the fact. This is stated here as a hard rule specifically because it is the concrete implementation mechanism for Part IV's multi-tenancy isolation guarantee (Section 26) — a repository that fetched a Flow without a tenant filter and relied on a calling service to check flow.TenantId == currentTenant afterward would be one missed check away from a cross-tenant data leak. Tenant filtering belongs in the repository's query itself, enforced structurally, not as an optional afterthought layered on top.

15.7 Repositories and the Module & Plugin trust boundary

IModuleRegistrationRepository has one property the other two repositories don't: its Save method must additionally enforce the TrustTier immutability invariant from Part II, Section 9.4 (a re-certification creates a new version rather than mutating trust tier in place). This is called out explicitly because it's a good illustration of Section 15.3's core argument — this is exactly the kind of aggregate-specific persistence rule that a generic repository would handle badly.

15.8 Relationship to Part V's database design

This section specifies the repository contracts (interfaces) as part of the domain/application layer's design — deliberately independent of the concrete schema. Part V, Section 32 (Database Design) specifies the actual PostgreSQL schema, table structure, and indexing strategy that FlowMapper and its siblings translate to and from. Reading this section does not require knowledge of the schema; reading the schema in Part V should make sense entirely in terms of "what does this table need to hold to satisfy the repository contracts and mappers defined here."



16. Unit of Work
16.1 Purpose

Section 15 established that a repository's Save method stages a change without committing it. The Unit of Work pattern is what actually commits — and, critically in Nexus's design, what atomically couples two things that must never happen independently: persisting an aggregate's state change, and dispatching the domain events that aggregate raised as a result of that change (Part II, Section 12). This coupling is the single most important property of Nexus's Unit of Work design, and the rest of this section explains why it's non-negotiable.

16.2 The interface
csharp
public interface IUnitOfWork
{
    Task Commit(CancellationToken ct);
}

Deliberately minimal — a single method. Nexus does not expose BeginTransaction/Rollback as separate public methods on this interface; transactional scope is managed internally by the implementation (16.3), because exposing manual transaction control to application-service code (Part II, Section 13.7's command handlers) would invite exactly the kind of ad hoc, inconsistent transaction-boundary decisions this pattern exists to prevent.

16.3 Implementation — commit and event dispatch as one atomic operation
csharp
public class UnitOfWork : IUnitOfWork
{
    private readonly NexusDbContext _db;
    private readonly IDomainEventDispatcher _dispatcher;  // publishes to the Event Bus, Section 22

    public async Task Commit(CancellationToken ct)
    {
        // 1. Collect all domain events raised by any aggregate tracked in
        //    this DbContext's change tracker during the current unit of
        //    work (every aggregate's RaiseDomainEvent calls, per Part II
        //    Section 9's code sketches, append to an internal buffer that
        //    the DbContext's change tracker exposes here).
        var pendingEvents = _db.ChangeTracker.Entries<IHasDomainEvents>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        // 2. Persist the state change and record the events transactionally
        //    in the SAME database transaction — events are written to an
        //    outbox table (Section 16.4) as part of this SaveChangesAsync
        //    call, not published directly to the Event Bus yet.
        using var transaction = await _db.Database.BeginTransactionAsync(ct);
        await _db.SaveChangesAsync(ct);
        await OutboxWriter.WritePendingEvents(_db, pendingEvents, ct);
        await transaction.CommitAsync(ct);

        // 3. Only after the transaction has successfully committed does
        //    dispatch to the in-process Event Bus occur — a separate,
        //    best-effort background process also relays any outbox
        //    entries the in-process dispatch might have missed (e.g., due
        //    to a crash between steps 2 and 3), per Section 16.4.
        foreach (var evt in pendingEvents)
            await _dispatcher.Dispatch(evt, ct);
    }
}
16.4 Why an outbox, not direct event publication

Step 2's outbox write is the specific mechanism that prevents the classic "dual-write problem": without it, a crash between saving the Flow state change and publishing its FlowPublishedEvent would leave the database correctly updated but the Reporting & Audit context (Part II, Section 12.6) never informed — a silent, hard-to-detect inconsistency directly contradicting Architecture Principle 20.4 (fail loud, fail informatively). By writing events to an outbox table in the same transaction as the state change (step 2), the two either both happen or neither does — genuine atomicity, not just "usually happens together in practice." A background outbox-relay process (mentioned in step 3's comment) then guarantees at-least-once delivery to the Event Bus even across a process crash, which is exactly the delivery guarantee Part II, Section 12.7 already commits every event consumer to handling idempotently.

16.5 Unit of Work scope — one per command, never shared across commands

Each command handler (Part II, Section 13.7) receives its own IUnitOfWork instance, scoped to the lifetime of handling that single command — never shared across multiple commands, and never held open across an await boundary that crosses into unrelated work (e.g., never held open during an outbound AI-provider call, which per NFR-PERF-4 might take up to 15 seconds; a database transaction held open that long would directly threaten NFR-SCALE-1's concurrent-execution target by holding connection-pool resources unnecessarily). Where a command handler needs an AI-provider result before it can finalize a state change (e.g., RequestSelectorHealCommand, Part II Section 13.3), the pattern is: call the AI provider first, entirely outside any transaction, then open the Unit of Work only once with the result already in hand, ready to commit quickly.

16.6 Multi-aggregate commands and Unit of Work boundaries

Most commands in Part II's catalog (Section 13) touch exactly one aggregate, which keeps Unit of Work scoping straightforward. A small number are cross-aggregate by nature — PublishFlowCommand's invariant check against ModuleRegistration (Part II, Section 9.5) is a read, not a write, so it doesn't complicate this section's concern, but a hypothetical future command that needed to atomically update both a Flow and a ModuleRegistration would require either: (a) a single Unit of Work spanning both repositories' Save calls within one transaction (acceptable for same-database aggregates, which all three of Nexus's aggregates currently are), or (b) if the aggregates ever moved to separate physical databases (not currently planned, but worth stating the fallback), a saga/process-manager pattern coordinating two separate Units of Work with compensating actions. Nexus's GA architecture relies exclusively on (a), and this fallback is documented here only so a future scaling decision doesn't have to rediscover this reasoning from scratch.

16.7 Testing implications

Because IUnitOfWork is an interface, command-handler unit tests (per Volume 5's testing standards, forward-referenced here) substitute a test double that captures committed events without touching a real database — this is a direct, practical payoff of Section 15.5's persistence-ignorance discipline extended to the Unit of Work layer: a command handler's business-logic correctness (did it call flow.Publish() and check the result correctly) is fully testable independent of whether the outbox/transaction machinery in 16.3 works correctly, which gets its own, separate integration-test coverage against a real database.



17. Dependency Injection
17.1 Purpose and scope of this section

Dependency injection in Nexus uses ASP.NET Core's built-in DI container throughout — no third-party container is introduced, since the built-in container is sufficient for every pattern this volume specifies and avoids an unnecessary dependency. This section is not a general DI tutorial; it specifies Nexus-specific conventions: service lifetime choices for each category of component defined in Sections 15–16 and Part II, and — most importantly — the specific DI design that makes the plugin architecture's dynamic module loading (Section 18) work correctly.

17.2 Lifetime conventions
Component category   Lifetime Rationale
Repositories (IFlowRepository, etc., Section 15)   Scoped   One instance per request/command-handling scope, sharing the same DbContext instance within that scope for consistent change tracking
IUnitOfWork Scoped   Must share the same DbContext instance as the repositories used within the same command, per Section 16.5's "one per command" rule
Command/query handlers  Scoped   Resolved fresh per command/query dispatch, consistent with the repositories they depend on
AutomationModule implementations (built-in)  Singleton, with explicit statelessness requirement (17.5)   Avoids per-execution allocation overhead for the six built-in modules, which are invoked at very high frequency
AutomationModule implementations (marketplace/sandboxed) Transient, scoped to a sandbox process lifetime (Section 18)   Distinct lifetime model entirely, since these run in an isolated process boundary, not the DI container's normal object graph
Domain event handlers/consumers (Section 22) Scoped or Singleton, per-consumer (see 17.6) Depends on whether the specific consumer needs per-request state (e.g., the audit pipeline, scoped) or is a long-lived background subscriber (e.g., a metrics aggregator, singleton)
NexusDbContext Scoped   Standard EF Core convention; one context per request/scope
17.3 Why the built-in DI container, not a third-party one

This is a small but explicit decision: third-party containers (Autofac, and others popular in the .NET ecosystem) offer richer features (e.g., more flexible property injection, assembly-scanning conventions) that Nexus's design does not need — every pattern in this volume is expressible with constructor injection and the built-in container's lifetime model. Adding a third-party container would be a dependency with no corresponding architectural benefit, directly contradicting Architecture Principle 20.7 (composable primitives over unnecessary complexity, applied here to the dependency graph itself).

17.4 Module registration at startup

Built-in modules are registered explicitly and statically at application startup:

csharp
services.AddSingleton<IAutomationModule, ApiModule>();
services.AddSingleton<IAutomationModule, WebModule>();
services.AddSingleton<IAutomationModule, LogsModule>();
services.AddSingleton<IAutomationModule, MobileModule>();
services.AddSingleton<IAutomationModule, DesktopUiModule>();
services.AddSingleton<IAutomationModule, AiModule>();

// Resolved as a collection by the Module Registry component (Part I, Section 5.3)
// at startup, which builds its in-memory ModuleKind -> IAutomationModule map from
// this registered set, plus dynamically-loaded marketplace modules (Section 18).
services.AddSingleton<IModuleRegistry, ModuleRegistry>();

This static registration is deliberate and appropriate specifically for built-in modules, since their set is fixed at deployment time (Volume 0's module maturity table, Section 14.10) — dynamic, runtime module loading is reserved specifically for marketplace-installed modules, where it's actually needed (Section 18.3).

17.5 Statelessness requirement for singleton-lifetime modules

Because built-in modules are registered as singletons (17.2) and Nexus targets 500 concurrent flow executions (NFR-SCALE-1), every built-in AutomationModule implementation must be safe for concurrent invocation across many simultaneous flow runs — no mutable instance fields that aren't themselves thread-safe. Per-execution state (a Playwright browser instance for the Web module, an Appium session for the Mobile module) is created and disposed within the scope of a single run() call or a single flow's setup()/teardown() lifecycle (per the AutomationModule contract's existing setup/teardown hooks, already sketched conceptually in this project's earlier exploration), never held as instance state on the singleton module object itself. This requirement is stated here, in the DI section, because it is fundamentally a consequence of the lifetime choice made in 17.2, not an independent coding-standard rule — Part VI's Coding Standards will reference this section rather than re-deriving it.

17.6 Domain event consumer registration and the outbox relay

Referenced in Section 16.4's outbox mechanism: the background outbox-relay process and the various domain-event consumers (Part II, Section 12.6 — SignalR Hub, audit pipeline, report projector, platform-health metrics collector) are registered as hosted services (IHostedService) with singleton lifetime for the relay/subscription infrastructure itself, but each individual event-handling operation creates its own scoped IServiceScope to resolve any scoped dependencies (like a fresh DbContext for the audit pipeline's write) — this is the standard, necessary pattern for background services that need to interact with scoped-lifetime infrastructure, called out explicitly here because getting it wrong (e.g., resolving a scoped DbContext directly into a singleton service's constructor) is a common and serious DI misuse that would cause subtle concurrency bugs given NFR-SCALE-1's concurrency target.

17.7 Configuration and options binding

Environment-specific configuration (database connection strings, Redis connection, LLM provider settings per Volume 0 Section 21.7's provider-abstraction requirement, secrets-manager endpoint) is bound via the standard ASP.NET Core IOptions<T> pattern, never read directly from IConfiguration inside business-logic components — this keeps every component's configuration dependency explicit and strongly-typed (echoing Section 10.8 of Part II's strongly-typed-identifier rationale, now applied to configuration), and makes the AI-optionality architecture (NFR-REL-3) concretely testable: a test can construct an AiModuleOptions with Enabled = false and verify the orchestration layer's behavior without needing to manipulate ambient configuration state.

17.8 Testability implications

Every dependency in this section's tables is injected via interface, never resolved via a static service locator or ambient context — this is the DI-layer expression of the persistence-ignorance and command-handler-testability goals already stated in Sections 15.5 and 16.7. A command handler's unit tests construct it directly with mocked/faked dependencies; DI container wiring itself is verified separately via a small set of "does the application start and can every registered service be resolved" smoke tests, per Volume 5's forthcoming CI quality-gate design.



18. Plugin Architecture
18.1 Why this section carries the most weight in Volume 1

Every prior section in this volume has, at some point, deferred a detail to "the Plugin Architecture section." That is not incidental — per Architecture Principle 20.2 (contract stability over implementation convenience) and the repeated emphasis across Volume 0 (Business Objective 4, the Module Parity Index metric, UC-6, RISK-08, RISK-12), the AutomationModule contract this section formalizes is the single most consequential interface in the entire system. Everything else in this volume is, in a real sense, built to serve this contract's stability and genericity.

18.2 The formal AutomationModule contract

Building directly on the conceptual AutomationModule interface prototyped in this project's earlier exploration phase, now formalized to production-grade precision:

csharp
public interface IAutomationModule
{
    ModuleKind Kind { get; }
    SemanticVersion ContractVersion { get; }  // the AutomationModule interface version
                                                 // this implementation targets — see 18.7

    IReadOnlyList<ActionSchema> GetSupportedActions();  // backs FR-ORCH-7's introspection

    Task Setup(ExecutionContext ctx, CancellationToken ct);   // optional lifecycle hook
    Task<StepOutcome> Run(string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct);
    Task Teardown(ExecutionContext ctx, CancellationToken ct); // optional lifecycle hook
}

public sealed record StepOutcome
{
    public StepStatus Status { get; init; }
    public string? Message { get; init; }
    public string? Error { get; init; }
    public object? Data { get; init; }
    public AiAttribution? AiAttribution { get; init; }  // Part II, Section 11.5
}

This is deliberately close to a direct C# translation of the AutomationModule interface already validated conceptually across API, Logs, Web, Mobile, UI, and AI modules earlier in this project — the formalization here adds strong typing, the explicit ContractVersion field (18.7), and the ExecutionContext parameter (18.3), but does not change the shape's fundamental simplicity, which is itself a design goal: Persona Raj (Volume 0, Section 10.5) must be able to read this interface and understand exactly what's required within minutes.

18.3 ExecutionContext — what a module is given, and what it is not given
csharp
public sealed class ExecutionContext
{
    public FlowRunId RunId { get; init; }
    public IReadOnlyDictionary<string, object?> Variables { get; init; }  // read-only view of Part II's Variable Store
    public ICredentialResolver Credentials { get; init; }  // scoped, least-privilege secret access — 18.4
    public ILogger ModuleLogger { get; init; }              // structured logging, Part IV Section 28
    public CancellationToken RunCancellation { get; init; }  // honors CancelFlowRunCommand, Part II Section 13.3
}

Deliberately absent from ExecutionContext: any reference to the Flow or FlowRun aggregates themselves, any repository interface, any ability to issue commands or queries against the rest of the system. A module receives exactly the narrow slice of context it needs to do its job and report a result — nothing that would let it reach back into the orchestration layer's internals. This is the direct architectural enforcement of Architecture Principle 20.3 (the orchestrator has no module-specific logic) applied in the reverse direction: modules have no orchestrator-specific access either. The boundary is symmetric.

18.4 Credential resolution — least privilege by design

ICredentialResolver is scoped per flow-run and per module, resolving only the specific named credentials a given FlowStep's parameters reference (e.g., {{secrets.paymentApiKey}}) — a module cannot enumerate or access any credential beyond what the flow author explicitly wired into that step's parameters. This directly implements NFR-SEC-2 and NFR-SEC-3 at the plugin-contract level: even a malicious or buggy third-party module cannot exfiltrate credentials it was never handed, because the resolver's implementation (backed by the Vault integration, Volume 0 Section 21.13) simply has no broader scope to grant.

18.5 The trust-tiered execution model

Referencing the trust boundary established in Part I, Section 2.5, and the TrustTier value object (Part II, Section 11.9), modules execute under one of two distinct runtime models:

In-process execution (Built-in and Verified-tier modules): the module assembly is loaded directly into the Orchestration Engine's process, invoked via a normal virtual method call through IAutomationModule. This is the low-overhead path, appropriate for modules that have passed the Verified-tier manual security and code-quality review (Volume 0, Section 17.3).

Sandboxed execution (Community-tier modules): the module runs in an isolated child process (or, for stronger isolation on supported platforms, a lightweight container/gVisor-style sandbox — the specific isolation technology is a deployment/infrastructure decision deferred to Volume 4, not fixed here), communicating with the Orchestration Engine via a gRPC-based RPC boundary that mirrors the IAutomationModule interface's shape exactly. Resource limits (CPU, memory, network egress policy — directly implementing Volume 0 Section 17.4's sandboxing requirement) are enforced at this process boundary. The ExecutionContext's ICredentialResolver and ILogger are proxied across this RPC boundary rather than passed as in-process object references, ensuring a sandboxed module cannot access anything beyond what the proxy explicitly exposes.

csharp
public interface IModuleExecutionStrategy
{
    Task<StepOutcome> Execute(IAutomationModule module, string action, IReadOnlyDictionary<string, object?> parameters, ExecutionContext ctx, CancellationToken ct);
}

public class InProcessExecutionStrategy : IModuleExecutionStrategy { /* direct call */ }
public class SandboxedExecutionStrategy : IModuleExecutionStrategy { /* RPC to isolated process */ }

The Module Dispatcher (Part I, Section 5.3) selects the strategy based on the resolved ModuleRegistration.Trust value (Part II, Section 9.4) — this selection logic is the only place in the entire orchestration layer that branches on trust tier, kept deliberately isolated so that Architecture Principle 20.3's "no module-specific branching in the core" is honored everywhere else.

18.6 GUI auto-surfacing — how GetSupportedActions() powers Volume 0's UC-6

The Module Registration API (Part I, Section 5.3) calls GetSupportedActions() on every registered module at startup (and on-demand when a marketplace module is newly installed) and persists the returned ActionSchema list into the ModuleRegistration aggregate (Part II, Section 9.4). The GetAvailableModuleActionsQuery (Part II, Section 14.2) then serves this data to the GUI. This is the complete, concrete mechanism by which Volume 0's UC-6 claim — "the flow-authoring GUI, without any custom frontend work from Raj, automatically surfaces his new module's actions" — is actually true rather than aspirational: the GUI's flow-step-editor component (Volume 2's responsibility) renders purely from this query's response shape, with zero hardcoded knowledge of any specific module.

18.7 Contract versioning

ContractVersion (18.2) follows semantic versioning against the IAutomationModule interface's own version, not the module's own version. A breaking change to the interface (e.g., adding a new required method) increments the interface's major version; the Module Registry (Part I, Section 5.3) refuses to register a module declaring an incompatible major version at startup, with a clear, structured error — a direct implementation of Architecture Principle 20.2 and the versioning policy Volume 0's RISK-12 flagged as required before the Phase 4 external-plugin milestone (Volume 0, Section 15.6). Minor/patch interface changes (new optional methods with default implementations, new optional ExecutionContext fields) are additive and never break existing modules — this additive-only discipline for non-major versions is a binding constraint on how this interface itself is allowed to evolve, not just a versioning label convention.

18.8 Marketplace module installation flow
An administrator (or, for Community-tier, potentially any Flow Author per tenant policy) selects a module from the marketplace registry (Volume 0, Section 17.4).
The platform downloads the module package, verifies its signature and ContractVersion compatibility (18.7).
A RegisterModuleCommand (Part II, Section 13.4) is issued, creating the ModuleRegistration aggregate with the appropriate TrustTier (18.5) already determined by the marketplace's certification metadata, not re-derived locally.
The module is loaded — in-process for Verified-tier, into a new sandboxed process for Community-tier (18.5) — and becomes immediately available for use in new flow steps, with zero orchestrator or GUI code changes, per NFR-MAINT-1.
18.9 What this section guarantees, restated plainly

A correctly-implemented IAutomationModule — whether one of the six built-in modules or a third party's custom module — is executed identically by the Flow Engine, reported on identically in the unified FlowReport, and surfaced identically in the GUI's flow-authoring interface. This is not an aspiration restated from Volume 0; it is now a concrete, auditable claim this section's design makes true by construction — the Module Parity Index (Volume 0, Section 6.3) is, at this point in the document set, a testable property of the code, not just a product goal.



19. Execution Engine
19.1 Purpose — formalizing the Flow Engine component

Part I, Section 5 introduced the Flow Engine as a component of the Orchestration Engine container, with dependencies on the Variable Store and Retry/Flake Handler. This section specifies its internal design fully: the step-execution loop, how it composes with the Plugin Architecture's execution strategies (Section 18.5), and how it satisfies FR-ORCH-1 through FR-ORCH-5 concretely.

19.2 The core execution loop
csharp
public class FlowEngine
{
    private readonly IModuleDispatcher _dispatcher;
    private readonly IVariableStore _variables;
    private readonly IRetryFlakeHandler _retryHandler;
    private readonly IUnitOfWork _unitOfWork;

    public async Task<FlowRun> Execute(Flow flow, FlowRun run, CancellationToken ct)
    {
        foreach (var step in flow.Steps.OrderBy(s => s.Order))
        {
            ct.ThrowIfCancellationRequested();  // honors CancelFlowRunCommand promptly between steps

            if (!EvaluateCondition(step.WhenCondition, run))
            {
                run.RecordStepResult(StepResult.Skipped(step));
                continue;
            }

            var interpolatedParams = _variables.Interpolate(step.Params, run.Variables);
            var outcome = await _retryHandler.ExecuteWithRetry(
                () => _dispatcher.Dispatch(step.Module, step.Action, interpolatedParams, run, ct),
                step.RetryPolicy,
                ct);

            var result = StepResult.FromOutcome(step, outcome);
            run.RecordStepResult(result);  // raises StepCompletedEvent, Part II Section 9.3

            if (step.SaveAs is not null)
                _variables.Bind(step.SaveAs, result.Data, run);

            if (result.Status == StepStatus.Failed && !flow.ContinueOnFailure)
                break;  // fail-fast default, Product Philosophy 4.2 (Volume 0)
        }

        run.Complete();  // raises FlowRunCompletedEvent, Part II Section 9.3
        await _unitOfWork.Commit(ct);
        return run;
    }
}

This loop is a direct, formalized evolution of the Orchestrator.runFlow logic already validated in this project's early prototype — every structural decision made there (fail-fast by default, saveAs/when semantics, skip-vs-fail distinction) carries forward unchanged, now expressed against the full domain model from Part II rather than the prototype's simplified types.

19.3 Step dispatch and the Module Dispatcher boundary

_dispatcher.Dispatch in 19.2 delegates to the Module Dispatcher component (Part I, Section 5.3), which resolves the target module via the Module Registry and selects the appropriate IModuleExecutionStrategy (Section 18.5) based on trust tier — the Flow Engine itself has zero knowledge of in-process versus sandboxed execution; that distinction is fully encapsulated one layer down, per Architecture Principle 20.3.

19.4 Performance engineering against NFR-PERF-1

NFR-PERF-1 (Volume 0) requires orchestrator step-dispatch overhead under 50ms at p95, excluding the module's own execution time. The execution loop in 19.2 is designed with this budget in mind at every step:

Variable interpolation (_variables.Interpolate) operates on pre-parsed VariableExpression value objects (Part II, Section 11.7) whose referenced paths were already extracted at Flow-publish time, not re-parsed from raw template strings on every step dispatch — this is the specific performance payoff of that design decision, now made explicit.
Condition evaluation (EvaluateCondition) is similarly pre-compiled where possible at publish time rather than interpreted fresh per execution.
Domain event raising (run.RecordStepResult) is an in-memory operation (appending to the aggregate's internal event buffer) — the actual persistence and dispatch cost is deferred to the Unit of Work's Commit call (Section 16), which happens once per flow run, not once per step, keeping the hot per-step loop free of I/O beyond the module dispatch itself.
The retry handler's backoff delays (Section 19.5) are explicitly not counted against the 50ms budget, since NFR-PERF-1 measures dispatch overhead, not legitimate retry-policy waiting time — this distinction should be reflected in how Volume 4's monitoring dashboards report this metric, so retries don't artificially appear to violate the SLA.
19.5 Retry and flake handling
csharp
public interface IRetryFlakeHandler
{
    Task<StepOutcome> ExecuteWithRetry(Func<Task<StepOutcome>> action, RetryPolicy policy, CancellationToken ct);
}

public sealed record RetryPolicy(int MaxAttempts, TimeSpan InitialBackoff, double BackoffMultiplier);

Implementation logic: on failure, retry up to MaxAttempts with exponential backoff; if a later attempt succeeds, the final StepOutcome is marked Passed, but the RetryAttempt history (Part II, Section 10.4) is preserved on the resulting StepResult, and a FlowRunFlakeDetectedEvent (Part II, Section 12.3) is raised — directly implementing FR-ORCH-5's requirement to distinguish "passed on retry" from "passed on first attempt," and feeding NFR-REL-2's platform flake-rate metric with zero additional bookkeeping required elsewhere in the system.

19.6 Conditional execution (when)

EvaluateCondition resolves the when expression (Part II, Section 8.2) against the current FlowRun's accumulated Variables and prior StepResults — the same resolution mechanism as saveAs-bound variable interpolation (19.4), since conditions and variable references share the same underlying expression grammar (Part II, Section 11.7). A step whose condition evaluates falsy is recorded as Skipped, never silently omitted from the FlowRun's results — this directly ensures a FlowReport (Part II, Section 8.5) always accounts for every step in the original flow definition, satisfying FR-REP-1's completeness expectation even for conditionally-skipped work.

19.7 Fail-fast versus continue-on-failure

The flow.ContinueOnFailure check in 19.2 is the direct implementation of FR-ORCH-3. This flag is set at Flow-authoring time (a property on the Flow aggregate, Part II Section 9.2, not shown in that section's abbreviated code sketch but part of its full definition) and defaults to false — fail-fast — per Product Philosophy 4.2's stated preference for short, fail-fast flows over long brittle ones. A flow author setting this to true is making a deliberate, visible choice, not relying on an implicit platform default that happens to continue past failures.

19.8 Concurrency model

Each FlowEngine.Execute invocation runs within its own async execution context, with no shared mutable state across concurrent invocations beyond what's explicitly scoped per FlowRun (the Variable Store, Section 8.3 of Part II, is instantiated fresh per run, never shared — this was already flagged as a concurrency-safety requirement in Part I, Section 5.3). The Orchestration Engine container (Part I, Section 4) hosts many concurrent FlowEngine executions simultaneously, up to and beyond the NFR-SCALE-1 target of 500, bounded in practice by a configurable worker-pool/semaphore limit (Volume 4's deployment sizing responsibility) rather than any hard limit in this engine's own design.

19.9 Relationship to the Workflow Engine and Scheduler (forward reference)

This section specifies execution of a single FlowRun from start to terminal state. Section 20 (Workflow Engine) addresses a distinct, higher-level concern: coordinating multiple related flow runs (e.g., a CI pipeline's sequence of flows, or a future saga-style multi-flow business process per Volume 0's Future Vision, Section 23) — the Execution Engine specified here is Section 20's primary building block, not a competing concept.



20. Workflow Engine
20.1 Distinguishing "Workflow Engine" from the Execution Engine

Section 19 specified how a single FlowRun executes from its first step to a terminal state. This section addresses a different, higher-level question: how multiple flow runs relate to and depend on each other as part of a larger process. The term "Workflow Engine" in this volume's scope refers specifically to this coordination layer — it is not a synonym for the Execution Engine, and the distinction matters enough to state plainly: a Flow is Nexus's unit of automation; a Workflow (in this section's sense) is an orchestrated sequence or dependency graph of Flow runs, motivated directly by Product Philosophy 4.2's guidance (Volume 0) that flows themselves should stay short, fail-fast vertical slices — with cross-flow coordination handled at this separate layer rather than by growing individual flows into long, brittle scripts.

20.2 Why this layer exists — the CI-pipeline-chaining use case

Volume 0's UC-4 (unified pipeline gate, Persona Marcus) already anticipates this: "chain several small flows in your CI pipeline instead of writing one giant one" was stated as guidance in the original framework's README, and this section formalizes the platform-native mechanism for doing exactly that, rather than leaving it entirely to external CI-tool configuration. A Workflow definition lets a customer express "run the API smoke-test flow; if it passes, run the Web regression flow and the Mobile regression flow in parallel; if both pass, run the deployment-verification flow" as a first-class Nexus concept, with unified reporting across the whole sequence — not just per individual flow.

20.3 Workflow as a thin coordination layer, not a new execution model

Critically, a Workflow does not introduce a new execution engine, a new module contract, or a new domain-event system — it is built entirely on top of the primitives Sections 19, 22 (Event Bus), and Part II's Commands/Queries already define. This is a direct application of Architecture Principle 20.7 (composable primitives over monolithic capability): a Workflow is a WorkflowDefinition (a new, lightweight aggregate — not shown in Part II's original three because it was appropriately scoped out of that Part's core domain model discussion, and is introduced here as a Part III addition since it is fundamentally an orchestration-layer concept, not a core domain concept in the same sense as Flow/FlowRun/ModuleRegistration) that references existing FlowIds and expresses dependency/parallelism relationships between them.

csharp
public sealed class WorkflowDefinition : AggregateRoot<WorkflowId>
{
    public string Name { get; private set; }
    public IReadOnlyList<WorkflowNode> Nodes { get; private set; }
    public TenantId TenantId { get; private set; }
}

public sealed record WorkflowNode(
    FlowId FlowId,
    IReadOnlyList<WorkflowNodeId> DependsOn,   // empty = can start immediately
    bool ContinueWorkflowOnFailure = false      // per-node override, distinct from
                                                   // the underlying Flow's own
                                                   // ContinueOnFailure (Section 19.7) —
                                                   // this flag controls whether the
                                                   // WORKFLOW proceeds to dependent
                                                   // nodes, not whether the FLOW
                                                   // itself continues past a failed step
);
20.4 Workflow execution semantics

A WorkflowRun (the execution-side counterpart, mirroring FlowRun's relationship to Flow) tracks the status of each node's underlying FlowRun. The Workflow Engine component:

Identifies nodes with no unsatisfied dependencies and triggers their FlowRuns concurrently via TriggerFlowRunCommand (Part II, Section 13.3) — reusing the exact same command, execution engine, and reporting pipeline as any standalone flow trigger, with zero special-casing.
Subscribes to FlowRunCompletedEvent (Part II, Section 12.3) via the Event Bus (Section 22) for each in-flight node's FlowRun.
On a node's completion, evaluates whether dependent nodes' dependencies are now satisfied (all upstream nodes passed, or the specific upstream node was flagged ContinueWorkflowOnFailure) and triggers them accordingly.
Aggregates all nodes' FlowReports into a WorkflowReport once every node has reached a terminal state.

This design means the Workflow Engine is, structurally, just another consumer of the same domain events every other Part II/Part III component already produces and consumes — it introduces no new event types beyond a WorkflowRunCompletedEvent for its own aggregate completion, and no new module contract whatsoever.

20.5 Why Workflow is scoped out of Volume 0's Must-priority requirements

This is worth stating plainly since it's a meaningful scope decision: Volume 0's functional requirements (Section 12) do not name "Workflow" explicitly as a Must-priority capability — the closest reference is FR-ORCH-3/FR-ORCH-4's flow-level conditional/continuation logic. This section's Workflow Engine is best understood as the natural, composable extension of those primitives to a multi-flow scope, consistent with how Volume 0's Roadmap (Section 15) was deliberately silent on exactly this kind of composable extension, trusting the architecture (per Architecture Principle 20.7) to make it a natural addition rather than requiring it to be pre-specified. Its priority relative to the six-module GA scope (Volume 0, Section 14.10) is a Roadmap planning decision, not an architectural one — this section specifies how it would work, cleanly, whenever product priority calls for it.

20.6 Reporting implications

A WorkflowReport is a further aggregation on top of the per-flow FlowReports already defined in Part II, Section 8.5 — not a competing reporting concept. Persona Sofia's dashboard (Volume 0, UC-5) would display workflow-level pass/fail alongside individual flow trends, using the same drill-down pattern already specified for DrillDownFailureSignatureQuery (Part II, Section 14.4), extended with one additional level: workflow → flow → step, rather than requiring an entirely separate reporting mechanism.

20.7 Cross-tenant and licensing considerations

Consistent with Volume 0 Section 16.2's licensing boundaries, Workflow-level orchestration (as a capability distinct from single-flow execution) is a reasonable candidate for the enterprise licensing tier, in the same spirit as the cross-module reporting/analytics capabilities already scoped there — this section does not make that licensing determination itself (that remains Volume 0 Part III's domain), but flags it here as the natural licensing-tier question this capability raises, consistent with this volume's practice of tracing forward to where a decision actually belongs rather than silently assuming an answer.



21. Scheduler
21.1 Purpose

The Scheduler component (introduced in Part I, Section 5.3's diagram) owns trigger mechanisms for flow (and, per Section 20, workflow) execution that originate from something other than a direct, synchronous API call — time-based (cron) schedules, inbound webhooks, and file-system-watch triggers. This section formalizes its design and, critically, specifies the invariant already asserted informally in Part I: a scheduled trigger and an API-triggered run must be indistinguishable to every downstream component.

21.2 Trigger types
csharp
public enum TriggerKind { Api, Cron, Webhook, FileWatch }

public sealed record TriggerDefinition(
    TriggerId Id,
    FlowId OrTargetWorkflowId,  // a trigger may target either a Flow or a Workflow (Section 20)
    TriggerKind Kind,
    string? CronExpression,      // populated only when Kind == Cron
    string? WebhookPath,         // populated only when Kind == Webhook
    string? WatchedPath,         // populated only when Kind == FileWatch
    bool IsEnabled,
    TenantId TenantId
);
21.3 Uniform trigger-to-execution path
csharp
public class Scheduler : IHostedService
{
    private readonly ICommandDispatcher _commands;

    // Cron-based triggers, evaluated on a periodic internal tick
    private async Task OnCronTick(TriggerDefinition trigger, CancellationToken ct)
    {
        await _commands.Send(new TriggerFlowRunCommand(
            trigger.OrTargetWorkflowId, RunTrigger.Scheduled, VariableOverrides: null, trigger.TenantId), ct);
    }

    // Webhook-based triggers, invoked from a dedicated API Layer endpoint
    // (Part V, Section 30) that resolves the incoming path to a TriggerDefinition
    // and delegates here rather than duplicating trigger logic in the API layer
    public async Task OnWebhookReceived(TriggerDefinition trigger, WebhookPayload payload, CancellationToken ct)
    {
        await _commands.Send(new TriggerFlowRunCommand(
            trigger.OrTargetWorkflowId, RunTrigger.Webhook, VariableOverrides: payload.ToVariables(), trigger.TenantId), ct);
    }
}

Every trigger path above converges on the exact same TriggerFlowRunCommand (Part II, Section 13.3) that a direct API call or CLI invocation issues — this is the concrete implementation of the design goal stated in Part I, Section 5.3: "a scheduled run and an API-triggered run are indistinguishable to every downstream component." The only observable difference anywhere in the system is the RunTrigger value recorded on the resulting FlowRun (Part II, Section 9.3), used purely for reporting/filtering (ListRecentFlowRunsQuery, Part II Section 14.3), never to alter execution behavior.

21.4 Cron scheduling implementation notes

Cron evaluation runs as a hosted background service (consistent with the IHostedService pattern already established for the outbox relay, Section 17.6), checking due triggers on a short interval (e.g., every 30 seconds) against standard cron-expression parsing. For horizontally-scaled deployments (NFR-SCALE-2), exactly-once trigger firing across multiple Orchestration Engine worker instances is coordinated via a distributed lock (backed by Redis, per Volume 0 Section 21.9) keyed per TriggerId and the current cron-tick window — this prevents the same scheduled trigger from firing multiple times simultaneously across workers, which would otherwise silently violate a customer's expectation of "this runs once per day," a subtle but real correctness bug this section deliberately flags rather than leaving implicit.

21.5 Webhook trigger security

Webhook-triggered execution is a genuine external-input entry point into the system (crossing the system boundary defined in Part I, Section 2.4) and is treated with corresponding caution: every WebhookPath is a generated, unguessable identifier (not a predictable slug), webhook requests are optionally signature-verified against a per-trigger shared secret (resolved via the same ICredentialResolver pattern used by modules, Section 18.4), and payload size/rate limits apply per Part IV's broader API security posture (Section 24). This is a direct, deliberate application of Architecture Principle 20.5 (security designed in, not bolted on) to a component that might otherwise be treated as "just an internal convenience feature."

21.6 File-watch triggers

FileWatch triggers target a specific self-hosted deployment scenario (per Architecture Principle 20.8's "self-hosted is a first-class target") where a customer's CI process drops an artifact (e.g., a newly-built application bundle) into a monitored location and wants flow execution to begin automatically without a webhook round-trip. This trigger type is scoped to self-hosted deployments specifically, since the managed/hosted deployment model (Volume 0, Section 19.5) has no equivalent concept of a customer-local filesystem to watch — a deliberate, explicit scope boundary rather than an oversight.

21.7 Trigger management as ordinary CRUD, not a special subsystem

TriggerDefinition is managed via ordinary commands (CreateTriggerCommand, EnableTriggerCommand, DisableTriggerCommand — following the same pattern established in Part II, Section 13, not separately enumerated here to avoid duplicating that catalog) and queries, subject to the same RBAC and audit treatment (Part IV) as any other state-changing operation. There is no special "scheduler admin" bypass — this is a direct, deliberate consequence of Architecture Principle 20.5 applied consistently: a component that can cause flow execution (with whatever access that execution implies, per Section 18.4's credential-scoping) must be governed by the same authorization discipline as directly triggering that execution manually would require.

21.8 Relationship to NFR-REL-1 (availability)

Because scheduled triggers are, by definition, unattended, a missed cron tick or an unprocessed webhook due to a Scheduler outage is a materially worse failure mode than an API call failing (where a human is present to notice and retry). The Scheduler's own availability is therefore held to at least the same 99.9% bar as NFR-REL-1's overall commitment, with a specific operational safeguard: on Scheduler restart (deployment, crash recovery), it reconciles against the last-known-fired timestamp per trigger and fires any cron triggers that were missed during the downtime window (bounded by a configurable maximum catch-up window, to avoid a long outage causing a flood of backlogged executions) — this reconciliation behavior is stated here explicitly because "just start fresh and skip whatever was missed" is a tempting but incorrect default that would silently violate a customer's scheduling expectations.



22. Event Bus
22.1 Purpose — formalizing the component, not re-justifying the pattern

Part II, Section 12 already catalogued every domain event and justified, in depth (Section 12.8), why cross-context communication happens via published events rather than direct calls. This section specifies the Event Bus component's technical design: how events are actually transported, the concrete technology choice deferred from Volume 0, and how the outbox pattern from Section 16.4 connects to it.

22.2 Two-tier design — in-process dispatch plus durable relay

The Event Bus is deliberately a two-tier design, not a single mechanism, directly serving two different needs simultaneously:

Tier 1 — in-process, synchronous-dispatch pub/sub, using .NET's IPublisher/ISubscriber abstractions (a thin wrapper the codebase defines over MediatR-style notification dispatch, or an equivalent lightweight in-process mechanism — the specific library is an implementation detail, not fixed here per Section 6.5 of Part II's rigor-versus-specificity guidance). This tier serves low-latency consumers within the same process — most notably the SignalR Hub (Section 22.4), which needs StepCompletedEvents delivered with minimal latency to keep the GUI's live progress view responsive, directly serving Persona Priya's authoring-loop experience.

Tier 2 — durable, outbox-backed relay, which reads the outbox table written transactionally by the Unit of Work (Section 16.4) and republishes each event reliably, at-least-once, to every registered consumer — including consumers that may be temporarily unavailable, running in a different process, or added after the event was originally raised (e.g., a newly-deployed metrics collector backfilling recent history is out of this tier's scope, but a consumer that was briefly down during the original dispatch will still receive the event via this relay).

csharp
public interface IEventBus
{
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : IDomainEvent;
    Task PublishInProcess(IDomainEvent evt, CancellationToken ct);  // Tier 1, called directly by UnitOfWork.Commit (Section 16.3, step 3)
}

public class OutboxRelayService : BackgroundService  // Tier 2
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pending = await _outboxStore.GetUnrelayedEntries(batchSize: 100, ct);
            foreach (var entry in pending)
            {
                await _eventBus.PublishInProcess(entry.Deserialize(), ct);  // reuses Tier 1's dispatch
                await _outboxStore.MarkRelayed(entry.Id, ct);
            }
            await Task.Delay(_relayInterval, ct);
        }
    }
}
22.3 Why not a full external message broker for GA

Volume 0, Section 21.14 explicitly deferred "specific message-queue technology" to this volume. The GA design specified here deliberately does not introduce an external broker (Kafka, RabbitMQ, Azure Service Bus) as a hard dependency — the two-tier in-process-plus-outbox design in 22.2 is judged sufficient for the GA scale target (NFR-SCALE-1's 500 concurrent executions) while keeping the self-hosted deployment's dependency footprint minimal, directly serving Architecture Principle 20.8 (self-hosted is a first-class target, not a downscoped afterthought) — every additional required infrastructure dependency is a real cost to a self-hosted customer's operational burden. This decision is explicitly flagged as revisitable: if a future scaling need (a much larger customer, or the Workflow Engine's cross-flow event coordination at high volume, Section 20.4) demands it, IEventBus's interface-based design (22.2) means swapping Tier 2's implementation for an external-broker-backed one is a contained change, not an architecture rewrite — directly benefiting from the same persistence-ignorance-style discipline already applied to repositories (Section 15.5).

22.4 SignalR integration
csharp
public class RunProgressNotifier
{
    private readonly IHubContext<RunProgressHub> _hub;

    public RunProgressNotifier(IEventBus bus, IHubContext<RunProgressHub> hub)
    {
        _hub = hub;
        bus.Subscribe<StepCompletedEvent>(OnStepCompleted);
        bus.Subscribe<FlowRunCompletedEvent>(OnRunCompleted);
    }

    private async Task OnStepCompleted(StepCompletedEvent evt, CancellationToken ct) =>
        await _hub.Clients.Group(evt.RunId.ToString()).SendAsync("StepUpdate", evt.Result, ct);
}

Clients subscribe to a SignalR group keyed by FlowRunId (obtained from GetLiveRunProgressQuery's initial response, Part II Section 14.3) when opening a live-progress view — this group-scoping ensures a GUI client only receives events for the specific run it's watching, not a firehose of every concurrent run across the tenant, which matters directly for NFR-PERF-1-adjacent client-side responsiveness at scale.

22.5 Consumer registration inventory

Restating Part II Section 12.6's consumer table, now mapped to which Event Bus tier each consumer subscribes through:

Consumer Tier  Rationale
SignalR Hub (RunProgressNotifier)   Tier 1 only Needs minimum latency; missing a rare in-flight notification during a brief outage is acceptable since GetLiveRunProgressQuery provides a reconciling catch-up read on reconnect
Audit pipeline Tier 1 + Tier 2 (durable)  NFR-SEC-3's audit-completeness requirement means this consumer cannot tolerate missed events — the outbox relay's at-least-once guarantee is required, not optional, here
Report projector  Tier 1 + Tier 2 (durable)  FR-REP-1's reporting completeness has the same durability requirement as audit
Platform-health metrics collector (flake rate, self-heal acceptance rate)  Tier 2 primarily  Tolerant of modest delivery latency; prioritizes completeness over immediacy
CI webhook notifier (if configured) Tier 2   Outbound webhook delivery to a customer's own systems should be retried reliably, which the durable relay's at-least-once semantics directly support
22.6 Idempotency, restated as an implementation obligation

Per Part II, Section 12.7's contract obligation, every Tier-2-subscribed consumer in 22.5's table implements idempotent handling explicitly — for the Report projector and audit pipeline specifically, this means an upsert-by-(RunId, StepResultId) pattern rather than a blind insert, so that the outbox relay's at-least-once redelivery (which will occur during normal operation, not only during failure recovery, since the relay does not guarantee exactly-once) never produces duplicate report rows or duplicate audit entries.



23. CQRS
23.1 What "adopted" means for Nexus, precisely

Volume 0's original topic list included "CQRS (if adopted)" — this section resolves that conditional explicitly: Nexus adopts CQRS in its lightweight form (separate command and query models, sharing the same underlying data store) rather than its full form (fully separate read/write databases with eventual-consistency projection for every query). This is not adopted as an architectural ideology; it is adopted because Part II's own command/query catalog (Sections 13 and 14) was already designed around this separation from the start — this section formalizes and justifies a decision that was, in practice, already made incrementally across the preceding sections, and states clearly where the boundary between "lightweight" and "full" CQRS sits.

23.2 The write side — command handlers operate on aggregates

As specified across Sections 15–19: every command (Part II, Section 13) loads a full aggregate via its repository (Section 15), invokes behavior that enforces domain invariants (Part II, Section 9), and persists via Unit of Work (Section 16). The write side is always consistent, always invariant-respecting, and always reads its own aggregate state directly — there is no separate "write model" distinct from the domain model itself. This is the "command" half of CQRS, and it is nothing more exotic than standard DDD tactical design, already fully specified.

23.3 The read side — queries operate on purpose-built projections

As specified in Part II, Section 14.7: queries are free to read from denormalized read models rather than the aggregates directly. The specific projections Nexus maintains:

Read model  Projected from Serves
FlowSummaryReadModel Flow aggregate state, updated synchronously alongside writes (not event-projected — see 23.4)   ListFlowsQuery
FlowReportReadModel  StepCompletedEvent, FlowRunCompletedEvent (async, via Event Bus Tier 2, Section 22.5)  GetFlowReportQuery, all of Part II Section 14.4's reporting queries
TrendAggregateReadModel Periodic batch aggregation over FlowReportReadModel (not per-event — see 23.5)   GetPassRateTrendQuery, GetFlakeRateQuery
ModuleActionReadModel   ModuleRegisteredEvent, ModuleDeprecatedActionEvent (async)  GetAvailableModuleActionsQuery
23.4 Why some read models are synchronous and others are event-projected

This distinction is deliberate and directly tied to each query's consistency requirements, not applied uniformly for its own sake: FlowSummaryReadModel (used for the flow-authoring GUI's list view) must reflect a just-published flow immediately — an author publishing a flow and then not seeing it in their own list a moment later would be a jarring, confusing experience directly working against NFR-UX-2's usability bar. This read model is therefore updated synchronously, within the same Unit of Work transaction as the aggregate write itself (a same-transaction denormalized projection, not an eventually-consistent one), accepting a small write-side cost in exchange for read-your-own-writes consistency exactly where a human is watching for it.

FlowReportReadModel, by contrast, is legitimately fine to lag by the Event Bus's typical delivery latency (low milliseconds for Tier 1, slightly more for Tier 2's polling interval) — nobody is watching a report update the instant a step completes with zero tolerance for lag; the SignalR live-progress path (Section 22.4) already serves that immediate-feedback need through a completely different mechanism (event-driven UI push, not a query at all). This is the direct, considered reasoning behind Nexus's "lightweight, not full" CQRS characterization from 23.1: eventual consistency is applied selectively, exactly where its latency is actually invisible to the relevant persona, never as a blanket default.

23.5 Trend aggregation — batch, not per-event

TrendAggregateReadModel, serving NFR-PERF-3's "90-day trend view in under 2 seconds at p95 for up to 10,000 historical runs," is deliberately not recomputed per individual event — recalculating a rolling 90-day statistical aggregate on every single StepCompletedEvent would be both wasteful and unnecessary, since nobody needs trend-dashboard precision at sub-second granularity. Instead, a scheduled batch job (itself just another flow-adjacent background process, conceptually similar to the Scheduler's cron handling, Section 21.4) recomputes trend aggregates on a periodic cadence (e.g., hourly), trading a bounded, well-understood staleness window for dramatically reduced computational cost — a pragmatic engineering tradeoff explicitly permitted by Section 6.2 of Part II's "adopt tactical patterns pragmatically, not dogmatically" guidance.

23.6 Consistency guarantees stated explicitly, per read model

Because CQRS's eventual-consistency tradeoffs are exactly the kind of subtlety that causes confusing bugs when left implicit, every read model's consistency guarantee is stated explicitly here as a contract, not left to be inferred from its implementation:

FlowSummaryReadModel: strongly consistent (same-transaction) — always reflects the latest committed Flow state.
FlowReportReadModel: eventually consistent, typically sub-second lag, bounded by Event Bus Tier 2's relay interval (Section 22.2) in the worst case (a missed Tier-1 delivery falling back to Tier 2's polling cadence).
ModuleActionReadModel: eventually consistent, same bound as FlowReportReadModel — acceptable since a marketplace module's newly-registered actions appearing in the GUI a moment after registration completes is not a scenario with the same immediacy expectation as FlowSummaryReadModel's read-your-own-writes case.
TrendAggregateReadModel: consistent as of the last batch run (up to the configured batch interval, e.g., 1 hour) — this is the one read model where the staleness window is measured in a unit coarser than "seconds," and this should be surfaced in the GUI itself (a "last updated" timestamp on the trend dashboard) so Persona Sofia is never misled about data freshness, directly serving Architecture Principle 20.4's fail-informatively spirit applied to staleness, not just errors.
23.7 Why not full CQRS with separate read/write databases

A fully separated read/write database (a distinct database, potentially a different technology, dedicated purely to serving queries) was considered and deliberately deferred beyond GA scope: it would add meaningful operational complexity (a second database to provision, migrate, and keep available) to every deployment, including self-hosted ones — directly working against Architecture Principle 20.8's self-hosted-first posture, for a scaling benefit not currently justified by NFR-SCALE-3's stated target (10 million stored flow-run records is comfortably within a well-indexed single PostgreSQL instance's capability, per Part V's forthcoming database design). This is explicitly flagged, per Section 6.5 of Part II's stated discipline, as a decision that should be revisited if real production scale ever approaches or exceeds that target materially — not a permanent architectural ceiling, just the appropriately-scoped GA answer.


___________________________________________________________________________________________________________________________________________________



24. Authentication
24.1 Scope and boundary with Authorization

This section covers authentication — establishing that a request is genuinely coming from who it claims to come from — as a distinct concern from authorization (Section 25 — what that established identity is permitted to do). Conflating the two in implementation is a common source of security defects; Nexus keeps them as separate middleware stages and separate service abstractions throughout, consistent with Architecture Principle 20.5 (security designed in, not bolted on) requiring each new component to state its security posture explicitly rather than inheriting an ambiguous, conflated one.

24.2 Supported authentication mechanisms
Mechanism   Used by  Notes
Username/password + session (JWT-based)   GUI (Persona Priya, Sofia) Standard interactive login; JWT access tokens with short expiry, refresh-token rotation
API key  CLI, CI service accounts (Persona Marcus) Long-lived, scoped credential, tied to a service-account User (Part II, Section 10.6's IsServiceAccount flag) rather than a human's interactive session
OIDC/SAML SSO  Enterprise-tier customers  Federated identity via the customer's own IdP (Part I, Section 2.3's external-system relationship), satisfying Volume 0's enterprise-trust requirements
mTLS (self-hosted, optional)  Service-to-service (e.g., a sandboxed plugin process authenticating back to the Orchestration Engine, per Part III Section 18.5) Not an end-user-facing mechanism; internal defense-in-depth for the trust boundary described in Part I, Section 2.5
24.3 Authentication pipeline
csharp
public class AuthenticationMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var credential = _credentialExtractor.Extract(context.Request);  // Bearer JWT, API key header, or mTLS cert
        if (credential is null)
        {
            if (_endpointRequiresAuth(context))
                throw new UnauthenticatedException();
            await next(context);
            return;
        }

        var principal = await _authenticator.Authenticate(credential, context.RequestAborted);
        context.User = principal;  // ClaimsPrincipal carrying UserId, TenantId, IsServiceAccount
        await next(context);
    }
}

Every authenticated request results in a ClaimsPrincipal carrying, at minimum, UserId and TenantId (Part II, Section 7.4's shared-kernel identifiers) as strongly-typed claims — this is the concrete mechanism by which every downstream repository's tenant-scoping (Part III, Section 15.6) and every command handler's authorization check (Part II, Section 13.6) obtain the identity they need, sourced from exactly one place in the request pipeline, never re-derived or trusted from a client-supplied header.

24.4 API key design

API keys (serving Persona Marcus's CI use case, Volume 0 UC-4) are never stored or transmitted as a raw, reusable secret without additional protection: keys are generated with a public prefix (for identification/rotation purposes in logs, without revealing the secret) and a high-entropy secret portion, stored server-side only as a salted hash (never reversibly), and validated by hash comparison — directly analogous to password storage discipline. A compromised API key can be revoked immediately by its prefix without needing to identify or invalidate the full secret value, and revocation raises the CredentialRotatedEvent (Part II, Section 12.5) that the audit pipeline durably records.

24.5 JWT design for interactive sessions

Access tokens are short-lived (target: 15 minutes) and carry the minimum claims needed for request processing (UserId, TenantId, a RoleVersion claim discussed in 24.6) — never permission lists directly in the token, for the reason given in 24.6. Refresh tokens are longer-lived, stored server-side with rotation-on-use (each refresh invalidates the previous refresh token and issues a new one), directly mitigating replay risk from a leaked refresh token, and every refresh-token use is itself subject to the same audit trail as any other authentication event.

24.6 Why permissions are never embedded in the JWT

A tempting shortcut is embedding a user's current permission set directly into the JWT at issuance time, avoiding a database round-trip on every authorization check (Section 25). This is deliberately rejected: a role change (AssignRoleCommand/RevokeRoleCommand, Part II Section 13.5) must take effect immediately, not only after the affected user's existing token expires — a security-critical requirement (revoking a compromised or terminated employee's access cannot wait 15 minutes) that a permissions-in-token design would violate. Instead, the JWT carries only a RoleVersion claim (a monotonically-incrementing counter, bumped on every role change for that user), and the Authorization service (Section 25) checks this version against the current value on each request — a cheap, cacheable check that still allows near-immediate revocation without the full permission-list round-trip on every single request.

24.7 SSO/OIDC integration

For enterprise-tier customers (Volume 0, Section 16.2's licensing scope), Nexus acts as an OIDC relying party against the customer's own identity provider (Part I, Section 2.3). On successful federation, a local User record is provisioned or matched (just-in-time provisioning, configurable per-tenant) — Nexus does not attempt to be its own identity source of truth for SSO-configured tenants, consistent with the "read from, don't replace" positioning already established as a recurring Nexus design philosophy (Volume 0, Section 14.8, applied here to identity rather than logs). Group/role claims from the IdP can optionally be mapped to Nexus roles automatically, reducing administrative burden for large enterprise rollouts, per Persona Elena's and Sofia's shared interest in low-friction, centrally-governed access management.

24.8 Authentication failure handling

Consistent with Architecture Principle 20.4 (fail loud, fail informatively) — but deliberately not at the cost of leaking information useful to an attacker: authentication failures return a generic, uniform error (not distinguishing "user doesn't exist" from "wrong password," a standard security practice to prevent user enumeration) to the caller, while the internal audit/security log (Section 27) records the specific failure reason with full detail, for legitimate incident-investigation purposes. This is a deliberate exception to the otherwise-uniform "fail informatively" principle, justified specifically because the informativeness trade-off here is a security control, not a usability one, and the principle's own intent (Volume 1, Section 20.4) was about internal diagnosability, not external-facing error verbosity.

24.9 Rate limiting and brute-force protection

Authentication endpoints specifically (login, API key validation, token refresh) are subject to tighter rate limiting than the general API surface, keyed by both source IP and target account, with progressive backoff on repeated failures — implemented using the Redis-backed rate-limiting infrastructure already established in Volume 0, Section 21.9's caching-layer decision, reused here rather than introducing a separate mechanism.



25. Authorization
25.1 The permission model — RBAC as the GA baseline

Nexus implements role-based access control (RBAC) as its GA authorization model, directly satisfying FR-SEC-4's minimum three roles (flow author, flow runner, administrator) while designed to support finer-grained custom roles as a natural extension, not a re-architecture. A Role is a named bundle of Permission values; a User (Part II, Section 10.6) holds one or more RoleId assignments, scoped per TenantId.

csharp
public enum Permission
{
    FlowCreate, FlowEdit, FlowPublish, FlowArchive, FlowExecute,
    ReportView, ReportExport,
    ModuleRegister, ModuleDeactivate,
    UserManage, RoleManage, TriggerManage,
    AuditView, CredentialManage
}

public sealed class Role : Entity<RoleId>
{
    public string Name { get; private set; }
    public IReadOnlyList<Permission> Permissions { get; private set; }
    public TenantId TenantId { get; private set; }
    public bool IsSystemDefined { get; private set; }  // the three FR-SEC-4 baseline roles
                                                            // cannot be deleted, only have
                                                            // custom roles added alongside them
}
25.2 The three baseline roles, precisely defined
Role  Permissions Corresponds to
Flow Author FlowCreate, FlowEdit, FlowPublish, FlowArchive, FlowExecute, ReportView Persona Priya (Volume 0, Section 10.2)
Flow Runner FlowExecute, ReportView only — no authoring permissions  Persona Marcus's CI service account (Volume 0, Section 10.3) — deliberately the narrowest role, since a CI credential leak should never grant flow-authoring capability
Administrator  All permissions, including UserManage, RoleManage, ModuleRegister, CredentialManage The implicit administrator actor (Part I, Section 2.2)

The Flow Runner role's deliberate narrowness in this table is worth calling out as a specific, intentional security decision rather than an oversight: Marcus's CI pipeline needs to trigger flows and read results, never to author or publish them — granting broader permissions "for convenience" to a machine-held credential would directly work against the least-privilege posture Architecture Principle 20.5 requires, and against the credential-scoping discipline already established for module execution in Part III, Section 18.4.

25.3 Authorization service — the single point of decision
csharp
public interface IAuthorizationService
{
    Task<bool> HasPermission(UserId userId, TenantId tenantId, Permission permission, CancellationToken ct);
    Task EnsurePermission(UserId userId, TenantId tenantId, Permission permission, CancellationToken ct);  // throws ForbiddenException
}

Every command handler (Part II, Section 13.7's pattern) calls EnsurePermission as its first action, per Section 13.6's "layer 1" validation — this is a hard, universal convention, not applied selectively: a command handler with no authorization check is treated as a defect at code review, full stop, regardless of how "obviously safe" the operation might seem. This uniformity is what makes the security posture auditable as a whole (directly serving Persona Elena's review process, Volume 0 UC-7) rather than requiring a case-by-case trust argument for every individual command.

25.4 Resource-level (not just operation-level) authorization

RBAC as specified in 25.1–25.3 governs operation-level permissions (can this user publish flows at all), but several of Volume 0's use cases require resource-level checks as well — a Flow Author's FlowEdit permission should not, without more, let them edit a flow belonging to a different team within the same tenant if the organization has configured team-level isolation. Nexus's authorization service supports this via an optional resource-ownership/scoping check layered on top of the base RBAC decision:

csharp
public async Task EnsureCanEdit(UserId userId, FlowId flowId, CancellationToken ct)
{
    await _authz.EnsurePermission(userId, currentTenant, Permission.FlowEdit, ct);   // operation-level
    await _authz.EnsureResourceAccess(userId, ResourceType.Flow, flowId, ct);         // resource-level, optional per-tenant policy
}

Resource-level scoping (team ownership, project-based grouping) is deliberately kept as an optional, tenant-configurable layer rather than a mandatory part of the base model — most tenants, especially smaller ones, have no need for it, and forcing it universally would add authorization-check overhead and administrative complexity with no corresponding benefit for the common case, a direct application of Architecture Principle 20.7's composability preference (add complexity only where it earns its cost).

25.5 Authorization and the RoleVersion claim

Building on Section 24.6's design: HasPermission/EnsurePermission first checks the requesting ClaimsPrincipal's RoleVersion claim against the current stored version for that user (a cheap, Redis-cached lookup keyed by UserId). If the versions match, the service proceeds to evaluate permissions from a similarly-cached role/permission set; if they differ (a role change occurred since the token was issued), the service forces a fresh permission lookup and, if the request's claimed permission is no longer valid, rejects it immediately rather than honoring a stale token's assumptions — this is the mechanism that makes the "immediate revocation" guarantee from Section 24.6 concretely true, not just a design intention.

25.6 Authorization for AI-originated and system-originated actions

Per Part II, Section 13.3's RequestSelectorHealCommand design note: even commands originating from the orchestration engine itself (not a human's direct request) pass through the same EnsurePermission check, evaluated against the identity of the FlowRun's original triggering user (or service account) — an AI-assisted self-heal inherits the authorization context of the run it's healing, never escalating privilege beyond what that run's original triggering identity already held. This closes a specific, easy-to-miss gap: without this rule, a compromised or misconfigured AI integration could not be used to perform an action beyond what the legitimate triggering identity was authorized for in the first place, directly reinforcing Volume 0's AI-boundary commitments (Section 18.4) at the authorization layer specifically, not only at the product/UX layer.

25.7 Authorization for marketplace/sandboxed modules

Referencing Part III, Section 18.5's trust-tiered execution model: a sandboxed (Community-tier) module's ICredentialResolver access (Part III, Section 18.4) is itself subject to an authorization check — the resolver verifies that the specific credential being requested was explicitly wired into the invoking FlowStep's parameters by the flow's author (who, per 25.2, held FlowEdit/FlowPublish permission when they did so), closing the loop between "who is allowed to grant a module access to a secret" (a human flow author, with real authorization) and "what a module can actually retrieve at runtime."

25.8 Authorization testing and the compliance-review use case

Because the entire authorization model in this section is designed to be exhaustively enumerable (25.1's fixed Permission enum, 25.2's baseline role table), Volume 5's testing standards will require a maintained test suite that, for every command in Part II's catalog (Section 13), asserts the exact required permission and verifies both the "authorized" and "unauthorized, rejected" paths — this exhaustive, enumerable test coverage is the concrete artifact Persona Elena's security review (Volume 0, UC-7) can be shown directly, rather than asking her to trust a prose description of the authorization model.



26. Multi-tenancy
26.1 Tenancy model — pooled by default, isolated by deployment choice

Nexus's managed/hosted deployment (Volume 0, Section 19.5) uses a pooled, shared-database multi-tenancy model: all tenants share the same application infrastructure and the same physical PostgreSQL database, isolated logically via TenantId filtering enforced at the repository layer (Part III, Section 15.6). The self-hosted deployment model, by its nature, typically serves a single tenant per deployment (a customer's own infrastructure, running their own instance) — but the domain model and codebase make no structural assumption about single-tenancy even there, so a self-hosted customer wanting to run Nexus for multiple internal business units within one deployment is fully supported without any different code path.

26.2 Why pooled rather than database-per-tenant for the hosted model

Database-per-tenant (a separate physical or logical database per customer) offers stronger isolation guarantees but was rejected as the GA default for the hosted offering for operational reasons directly tied to NFR-SCALE-2 (horizontal scaling by adding workers, without client-facing changes) and Business Objective 4's marketplace ambitions (Volume 0): schema migrations, connection-pool management, and cross-tenant aggregate reporting (a future analytics capability, per Volume 0's Future Vision Section 23.6) all become significantly more operationally complex at scale with database-per-tenant. Pooled multi-tenancy with rigorous TenantId filtering is the industry-standard tradeoff for a SaaS platform at Nexus's target scale, and is explicitly revisitable per-customer for enterprise deployments requiring stronger physical isolation (see 26.6).

26.3 Enforcement mechanism — defense in depth, not a single checkpoint

Tenant isolation is enforced at three independent layers, deliberately redundant rather than relying on any single checkpoint being perfect:

Repository-level query filtering (Part III, Section 15.6) — every aggregate query includes a TenantId predicate, sourced from the authenticated ClaimsPrincipal (Section 24.3), never from a client-supplied parameter.
Database row-level security (RLS), configured on every multi-tenant table in PostgreSQL, using the current session's TenantId (set via a connection-scoped session variable at the start of each request) as the RLS policy predicate — this is the "defense in depth" layer: even if a repository-layer bug omitted a TenantId filter, the database itself would refuse to return cross-tenant rows.
Application-service assertion, a final belt-and-suspenders check in command/query handlers that explicitly asserts a loaded aggregate's TenantId matches the requesting principal's TenantId before returning or mutating it, treating a mismatch as a serious internal error (logged at a high severity, per Section 28) rather than a normal "not found" — because a mismatch at this point indicates layers 1 and 2 both failed, which should never happen and needs immediate investigation if it does.
csharp
public class TenantScopedDbContext : NexusDbContext
{
    public TenantScopedDbContext(ITenantContextAccessor tenantAccessor, DbContextOptions options) : base(options)
    {
        // Sets the PostgreSQL session variable consumed by RLS policies (layer 2)
        Database.ExecuteSqlRaw("SET app.current_tenant_id = {0}", tenantAccessor.CurrentTenantId.Value);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Applies a global query filter (layer 1) to every multi-tenant entity type,
        // so a developer cannot accidentally write a LINQ query that forgets the
        // TenantId predicate — it's structurally impossible to omit, not just a
        // convention to remember.
        builder.Entity<FlowEntity>().HasQueryFilter(f => f.TenantId == _tenantAccessor.CurrentTenantId);
        // ... repeated for every multi-tenant entity type
    }
}
26.4 ITenantContextAccessor — resolving the current tenant safely
csharp
public interface ITenantContextAccessor
{
    TenantId CurrentTenantId { get; }
}

Implemented as a scoped service populated exclusively from the authenticated ClaimsPrincipal (Section 24.3) at the start of request processing — never from a request header, query parameter, or any other client-controllable input, which would trivially allow tenant-boundary bypass. This is stated as an absolute rule specifically because "just read the tenant from a header for convenience" is a realistic, tempting shortcut during development that must never reach production code; Volume 5's code-review checklist will include an explicit item verifying no code path derives TenantId from anything other than this accessor.

26.5 Tenant isolation and the Module & Plugin context

Referencing Part II, Section 9.4 and Part III, Section 18: ModuleRegistration is itself tenant-scoped for Community-tier marketplace modules (a module a specific tenant chooses to install is not automatically available to other tenants), while Built-in and Verified-tier modules are effectively global (available to every tenant by default, since they carry no tenant-specific trust decision). This distinction matters concretely for the enterprise policy scenario already discussed in Part II, Section 14.5 ("restrict this org's instance to Verified-or-above modules only") — that policy is itself a per-tenant configuration value, enforced at the Module Dispatcher's resolution step (Part I, Section 5.3) by checking the requesting flow's TenantId's policy before allowing dispatch to a Community-tier module, even one the tenant has technically installed.

26.6 Stronger isolation for regulated-industry customers

Directly serving Persona Elena's segment (Volume 0, Section 10.6) and Business Objective 5's regulated-industry target: for hosted customers requiring stronger isolation than the pooled model provides (a common regulated-industry requirement), Nexus's architecture supports a dedicated-instance deployment — effectively a single-tenant deployment of the same self-hosted-capable codebase, operated by Nexus on dedicated infrastructure rather than the customer's own. This is not a separate codebase or a special "enterprise fork" — it is the same application, the same multi-tenancy-capable schema (simply populated with one tenant), directly benefiting from Architecture Principle 20.8's insistence that the self-hosted deployment path be a first-class, fully-maintained target rather than an afterthought; the dedicated-instance offering is, structurally, just self-hosting operated on the customer's behalf.

26.7 Cross-tenant operations — the one deliberate exception

The only legitimate cross-tenant read in the entire system is Nexus's own internal operational tooling (e.g., a platform-health dashboard aggregating flake rates across all tenants for the Nexus engineering team's own use, distinct from any tenant-facing feature) — implemented via a separate, explicitly-named IPlatformAdminQuery interface family that bypasses the standard ITenantContextAccessor-scoped DbContext entirely, uses a distinct, heavily-audited administrative credential, and is never reachable via any tenant-facing API endpoint. This exception is named explicitly, in this section, specifically so it is never confused with or accidentally generalized into a broader "sometimes we read across tenants" pattern — it is a single, narrow, clearly-labeled escape hatch, not a precedent.



27. Audit
27.1 Purpose and relationship to AuditEntry

Part II, Section 10.7 already defined the AuditEntry entity as append-only, satisfying NFR-SEC-3. This section specifies the full audit pipeline: what triggers an audit entry, how entries flow from a command's execution to durable storage, and how the retention and export requirements (NFR-SEC-3, and Persona Elena's ExportAuditLogQuery, Part II Section 14.4) are actually satisfied end-to-end.

27.2 What gets audited — the exhaustive-by-construction approach

Rather than maintaining a manually-curated list of "important" actions to audit (which inevitably drifts out of date as new commands are added), Nexus's audit pipeline is wired to the command catalog itself (Part II, Section 13): every command handler, as a structural convention enforced by a shared base class or interceptor, emits an audit-relevant record as a natural byproduct of successful execution — not as an opt-in feature individual handlers must remember to implement.

csharp
public abstract class AuditableCommandHandler<TCommand, TResult> : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<TResult> Handle(TCommand cmd, CancellationToken ct)
    {
        var result = await HandleCore(cmd, ct);
        _auditRecorder.Record(new AuditEntry(
            actorId: _currentUser.UserId,
            actionType: typeof(TCommand).Name,
            targetType: DeriveTargetType(cmd),
            targetId: DeriveTargetId(cmd),
            metadata: DeriveMetadata(cmd, result),
            tenantId: _tenantAccessor.CurrentTenantId));
        return result;
    }

    protected abstract Task<TResult> HandleCore(TCommand cmd, CancellationToken ct);
}

This base-class pattern means a developer adding a new command (Part II, Section 13) gets audit logging for free by construction, rather than needing to remember to add it — directly closing the class of gap where a newly-added state-changing operation is accidentally left unaudited, which would otherwise be exactly the kind of silent compliance regression Architecture Principle 20.5 (security designed in, not bolted on) exists to prevent.

27.3 Audit entries persist via the same Unit of Work, same outbox

Referencing Part III, Section 16.4: the audit entry created in 27.2 is persisted within the same transaction as the command's primary state change, via the same outbox-backed Unit of Work — this guarantees an audited action and its audit record are never inconsistent (no state change without a corresponding audit entry, and no orphaned audit entry for a change that was rolled back), directly extending the atomicity guarantee already established for domain events to audit specifically, since an AuditEntry creation is, structurally, itself just another kind of domain event consumer (Part II, Section 12.6's audit-pipeline row) receiving the same underlying signal.

27.4 What gets recorded — sensitive-data handling within audit entries

Per NFR-SEC-3's "never persist secrets in plaintext" and the CredentialRotatedEvent design already established (Part II, Section 12.5): DeriveMetadata (27.2) is implemented per-command with explicit denylisting of any parameter that might carry a secret value — a RotateCredentialCommand's audit entry records that a rotation occurred, by whom, and for which named credential reference, never the credential's old or new value. This denylisting is enforced by a compile-time-checked attribute on command properties ([SensitiveValue]) that the metadata-derivation logic automatically excludes, rather than relying on each command's author to remember to exclude it manually — the same "make the safe path the automatic path" philosophy as 27.2's base-class approach, applied specifically to data sensitivity.

27.5 Immutability enforcement

Beyond the domain-model-level immutability already noted in Part II, Section 10.7 (no update methods on the entity), the database schema itself (Part V, Section 32) enforces this at the storage layer: the audit_entries table grants the application's database role INSERT and SELECT privileges only — no UPDATE or DELETE grant exists at all, even for an administrative database connection used for routine maintenance. This is a deliberate, structural (not just application-code-level) enforcement of NFR-SEC-3's immutability requirement, directly relevant to Persona Elena's compliance review (Volume 0, UC-7), which can verify this guarantee by inspecting database grants directly rather than trusting application-code discipline alone.

27.6 Retention and archival

Per NFR-SEC-3's minimum 1-year retention: audit entries are retained in the primary, queryable store for a configurable "hot" window (default: 1 year, matching the NFR floor exactly, tenant-configurable upward), after which they are archived (not deleted) to lower-cost, still-durable storage (Volume 4's infrastructure responsibility for the specific mechanism — e.g., a scheduled export to object storage), remaining available via ExportAuditLogQuery (Part II, Section 14.4) but potentially with higher retrieval latency for the archived tier. Deletion of audit data before its configured retention floor is not a supported operation anywhere in the system's command catalog — there is no DeleteAuditEntryCommand, by deliberate design, not merely by policy.

27.7 Distinguishing Audit from operational Logging

This section covers audit records specifically — an immutable, compliance-oriented record of who did what, when, to what. Section 28 (Logging) covers a related but distinct concern: operational, debugging-oriented log output from the running system, which is higher-volume, not immutable in the same compliance sense, and governed by different retention/access rules. A single underlying event (e.g., StepCompletedEvent, Part II Section 12.3) can and does feed both concerns simultaneously through separate consumers (Section 22.5's consumer table already lists the audit pipeline as one specific Event Bus subscriber, distinct from general application logging), but the two output streams — audit trail and operational log — are never merged into one system, since conflating them would compromise both: operational logs need aggressive, cost-effective retention policies and high-volume tolerance that compliance audit data should never be subject to, and audit data needs the immutability and access-control rigor that would be excessive overhead for routine debug logging.

27.8 Audit query access control

AuditView permission (Section 25.1's Permission enum) is deliberately held only by the Administrator baseline role by default (Section 25.2) — audit data itself is sensitive (it can reveal, for instance, who has been actively working with a specific credential, or the full history of a specific user's access-level changes), and its own access is therefore subject to the same authorization rigor as any other sensitive resource in the system, with every access to audit data itself optionally logged as a further audit entry — an intentional, recursive application of this section's own principle to itself, available as a tenant-configurable option for organizations with the strictest compliance requirements (Persona Elena's segment specifically).




28. Logging
28.1 Purpose and distinction from Audit and from the Logs module

A necessary disambiguation up front, since this document set uses "log" in three genuinely different senses: this section (28) covers Nexus's own operational logging — the diagnostic output the Nexus application itself produces about its own behavior. This is distinct from Section 27 (Audit), the immutable compliance trail, and distinct from the Logs module (Volume 0, Section 14.8; Part III, Section 18) — the customer-facing automation capability that reads and analyzes a target system's logs. All three are legitimately called "logs" in casual conversation, but they are three different systems with three different purposes, retention rules, and audiences, and this document set is deliberately precise about which one is meant in each section.

28.2 Structured logging as a non-negotiable convention

Every log statement in the Nexus codebase is structured (key-value/JSON-shaped, via the standard .NET ILogger structured-logging pattern), never a freeform interpolated string:

csharp
// Required convention:
_logger.LogInformation("Flow {FlowId} published by user {UserId} in tenant {TenantId}", flowId, userId, tenantId);

// Prohibited convention:
_logger.LogInformation($"Flow {flowId} published by user {userId} in tenant {tenantId}");

The difference matters operationally, not just stylistically: the structured form preserves FlowId, UserId, and TenantId as independently queryable fields in the observability backend (Section 29), enabling a query like "show me every log line for this specific FlowId across every component that touched it" — impossible to do reliably against freeform interpolated strings. Volume 5's coding standards will enforce this via static analysis (a Roslyn analyzer rejecting string-interpolated logger calls) rather than relying on code-review vigilance alone.

28.3 Log levels and their meaning in Nexus specifically
Level Meaning in Nexus's context Example
Trace Extremely granular, disabled by default even in development Raw variable-interpolation resolution steps
Debug Detailed flow, enabled in development/troubleshooting Module dispatch decisions, retry attempts
Information Normal, expected state transitions  Flow published, run started/completed
Warning  Unexpected but handled/recovered situations  A step retried and eventually passed (flake), a deprecated module action used
Error An operation failed and could not complete as requested  A step failed, a command was rejected by a domain invariant
Critical The platform itself is degraded in a way threatening NFR-REL-1's availability commitment  Database connectivity lost, outbox relay (Part III, Section 22.2) falling significantly behind

This table is the concrete operationalization of Architecture Principle 20.4 (fail loud, fail informatively) at the logging layer — a Warning for a flaky-but-passing step versus an Error for a genuinely failed step is not a cosmetic distinction; it's what lets an on-call engineer's alerting (Section 29) be tuned to page on Critical/Error without being drowned out by expected, self-recovering Warning-level noise.

28.4 Correlation — tracing one request/run across every component

Every log statement automatically carries a correlation identifier — CorrelationId for a single API request, and FlowRunId (Part II, Section 9.3) for anything occurring within the scope of a flow execution — propagated via ILogger's scope mechanism (BeginScope) rather than requiring every individual log call to manually pass these values:

csharp
using (_logger.BeginScope(new Dictionary<string, object> { ["FlowRunId"] = runId.Value }))
{
    // Every log statement within this scope, across every component the
    // execution touches (Flow Engine, Module Dispatcher, individual module
    // implementations per Part III Section 18.3's ExecutionContext.ModuleLogger,
    // which is itself just a scoped child logger), automatically carries
    // FlowRunId without each call site needing to include it explicitly.
}

This correlation design is what makes a support/debugging workflow like "a customer reports Flow Run X failed unexpectedly, reconstruct exactly what happened" tractable — every log line relevant to that run, across every layer from the API Layer's initial request handling through the Module Dispatcher's execution, is retrievable via a single FlowRunId filter in the observability backend (Section 29), without needing to separately correlate timestamps across disjoint log streams.

28.5 What must never appear in operational logs

Directly extending Section 27.4's sensitive-data denylisting principle to operational logging specifically: credential values, full request/response bodies from target systems under test (which may themselves contain a customer's own sensitive data, per Part I Section 2.5's trust-boundary analysis), and full page HTML/screenshots passed to the AI module for selector healing (Part III, Section 18) are never logged at any level, including Trace/Debug. Where a log statement needs to reference such data for debugging purposes, it references a truncated hash or a reference identifier, never the raw value — this is enforced via the same [SensitiveValue] attribute convention already established in Section 27.4, applied consistently across both the audit and operational-logging pipelines rather than as two separately-maintained denylists that could drift out of sync.

28.6 Log retention and cost management

Unlike audit entries (Section 27.6's 1-year compliance floor), operational logs follow a much shorter, cost-optimized retention policy — a tiered approach (e.g., full-fidelity retention for 14 days, downsampled/aggregated retention for 90 days, then discarded) reflecting that operational logs exist to support near-term debugging and incident response, not long-term compliance record-keeping. This retention policy is a Volume 4 (Infrastructure) configuration concern specifically; this section establishes only the principle that operational-log retention is deliberately, appropriately shorter than audit retention, and that the two must never be conflated into a single retention policy (echoing Section 27.7's distinction).

28.7 Module-level logging and the plugin trust boundary

Per Part III, Section 18.3: a module's ExecutionContext.ModuleLogger is a genuinely scoped, restricted logger — a Community-tier sandboxed module (Part III, Section 18.5) can log freely within its own execution scope for its own debugging purposes, but its log output is tagged and isolated (never able to inject fabricated log entries attributable to a different component) and is itself subject to the same sensitive-data scrubbing (28.5) applied at the boundary where the sandboxed process's output is received back into the main logging pipeline — a third-party module's log output is treated as untrusted input requiring sanitization, not as equally-trusted first-party output, consistent with the trust-boundary discipline established in Part I, Section 2.5.

28.8 Forward reference to Observability

Logging, as specified in this section, is one of the "three pillars" of observability (alongside metrics and traces) that Section 29 covers holistically — this section deliberately scopes itself to what gets logged and how it's structured, leaving how logs are collected, stored, visualized, and alerted upon to Section 29's broader observability architecture, since that architecture (built on OpenTelemetry, per Volume 0 Section 21.11) treats logs, metrics, and traces as three correlated facets of the same underlying telemetry pipeline rather than three independent systems.



29. Observability
29.1 Purpose — the platform observing itself

This section specifies how Nexus achieves the "observability of the platform itself" cross-cutting concern first flagged in Part I, Section 19.4, and directly serves the platform-health metrics established across Volume 0: flake rate (NFR-REL-2), step-dispatch overhead (NFR-PERF-1), and general availability (NFR-REL-1). This is Nexus monitoring itself, distinct in purpose from the Logs module (Volume 0, Section 14.8), which is a product capability for monitoring customers' target systems — the same distinction already drawn precisely in Section 28.1.

29.2 The three pillars, unified via OpenTelemetry

Consistent with Volume 0's locked technology decision (Section 21.11), Nexus instruments all three observability pillars — logs (Section 28), metrics, and distributed traces — through OpenTelemetry's unified SDK, exporting via OTLP to a configurable backend (Prometheus/Grafana in the reference self-hosted architecture, or a customer's own existing observability stack, per Architecture Principle 20.8's self-hosted-first posture and the same "integrate with, don't replace" philosophy already applied to the customer-facing Logs module).

csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource("Nexus.Orchestration")   // custom ActivitySource, Section 29.3
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddOtlpExporter())
    .WithMetrics(metrics => metrics
        .AddMeter("Nexus.Orchestration")     // custom Meter, Section 29.4
        .AddRuntimeInstrumentation()
        .AddOtlpExporter());
29.3 Distributed tracing — one trace per flow run

Every FlowRun (Part II, Section 9.3) is instrumented as a root OpenTelemetry trace, with each FlowStep's dispatch (Part III, Section 19.3) forming a child span — including, critically, spans that cross the sandboxed-module process boundary (Part III, Section 18.5), via W3C Trace Context propagation over the gRPC channel, so that a Community-tier module's execution time is visible as a distinct, attributable span within the parent trace rather than appearing as an opaque gap. This design directly makes NFR-PERF-1's "orchestrator step-dispatch overhead... excluding the module's own execution time" a measurable, not just specified, quantity — the trace structure itself cleanly separates orchestrator-attributable time (the Flow Engine, Module Dispatcher, Variable Store spans) from module-attributable time (the module's own span, further broken down by whatever internal spans a specific module chooses to emit, e.g., the Web module distinguishing "browser launch" from "page navigation" from "assertion evaluation").

29.4 Key platform metrics
Metric   Type  Directly implements
nexus.step.dispatch.duration  Histogram, tagged by ModuleKind  NFR-PERF-1 (50ms p95 dispatch overhead)
nexus.flow.run.duration Histogram, tagged by TenantId (aggregated, never per-tenant-identifiable in shared dashboards), RunStatus   General execution-time monitoring
nexus.flow.run.flake_detected Counter  NFR-REL-2 (platform flake-rate — numerator)
nexus.flow.run.completed   Counter, tagged by RunStatus  NFR-REL-2 (denominator)
nexus.orchestration.concurrent_executions UpDownCounter (gauge-like) NFR-SCALE-1 (500 concurrent execution target — direct capacity monitoring)
nexus.ai.request.duration  Histogram, tagged by Capability (flow-generation, self-heal, triage) NFR-PERF-4 (15s AI timeout — monitoring actual latency against the budget)
nexus.ai.request.timeout   Counter  Direct signal for AI-optionality graceful-degradation frequency (NFR-REL-3) — a rising count here indicates the AI provider relationship needs attention, independent of any single flow's outcome
nexus.outbox.relay.lag  Gauge Section 22.2's Tier 2 relay health — directly informs whether durable-consumer guarantees (audit, reporting) are being met with acceptable latency
nexus.eventbus.publish.duration  Histogram   Event Bus (Section 22) health
29.5 The flake-rate metric, computed transparently

Directly addressing Volume 0's NFR-REL-2 (platform flake rate below 0.5%, tracked as a release-gating metric): the ratio of nexus.flow.run.flake_detected to nexus.flow.run.completed, computed over a rolling window, is the exact, auditable formula behind this metric — stated explicitly here so there is no ambiguity between "platform flake rate" as a marketing/reporting figure and as a concretely-computed, dashboard-visible quantity any engineer can independently verify against the same raw counters the release-gating CI check (Volume 5) consumes.

29.6 Alerting philosophy

Consistent with Architecture Principle 20.4 (fail loud, fail informatively) applied to operational alerting specifically: alerts are configured to fire on symptom metrics (elevated nexus.step.dispatch.duration p95, rising nexus.outbox.relay.lag, dropping nexus.orchestration.concurrent_executions capacity headroom) rather than only on hard failures, so that NFR-REL-1's 99.9% availability commitment is protected proactively — an on-call engineer should generally see a degradation trend in dashboards before it becomes a customer-visible outage, not only after. Full alerting-rule configuration (specific thresholds, escalation policies) is Volume 4's infrastructure-operations responsibility; this section establishes the metric foundation those rules are built on.

29.7 Health checks versus deep observability

Distinct from the rich telemetry above, Nexus exposes standard lightweight health-check endpoints (/health/live, /health/ready) for load-balancer and orchestration-platform use (Kubernetes liveness/readiness probes, per Volume 0 Section 21.12) — these are intentionally minimal (process responsiveness, database connectivity) and are not a substitute for the OpenTelemetry-based observability described above; they answer "should traffic be routed to this instance right now," a narrower and faster question than "is the platform healthy," which the full telemetry pipeline answers.

29.8 Tenant data isolation within shared observability infrastructure

Directly extending Section 26's multi-tenancy discipline to the observability pipeline itself: metrics and traces that include a TenantId tag are aggregated for platform-health purposes (29.4's table) but are never exposed in a customer-facing view broken out by other tenants' identifiers — a customer's own operational dashboard (if Nexus ever exposes one directly, beyond the reporting dashboard already specified in Part II, Section 14.4) would filter strictly to that customer's own TenantId, enforced by the same ITenantContextAccessor-scoped access pattern (Section 26.4) applied to observability-data queries, not a separately-invented access-control mechanism.

29.9 Observability's relationship to the rest of Part IV

This section closes Part IV by tying together every prior section's operational signal into one coherent telemetry model: authentication failures (Section 24.8) and authorization rejections (Section 25) are logged (Section 28) with correlation identifiers (Section 28.4) and, where they represent a security-relevant pattern (e.g., a spike in authentication failures against one account, potentially indicating a credential-stuffing attempt), surfaced as metrics this section's alerting philosophy (29.6) can act on — Part IV's six sections are not six independent concerns bolted together, but one connected security-and-operations fabric, which is the concrete, technical fulfillment of Architecture Principle 20.5's instruction that security and compliance posture be designed in from the start, at every layer, rather than assembled from disconnected point solutions after the fact.


___________________________________________________________________________________________________________________________________________________



30. REST API
30.1 Purpose and design philosophy

The REST API is the single public write and read surface for Nexus, per Architecture Principle 20.5 restated from Part I, Section 19.2: the GUI and CLI are both clients of this API, never given a privileged internal shortcut. This section specifies the endpoint inventory (mapping directly onto Part II's command and query catalogs, Sections 13–14), request/response conventions, versioning, and error-handling design.

30.2 Resource-oriented endpoint inventory
Resource Endpoints   Backing command/query (Part II)
Flows POST /api/v1/flows   CreateFlowCommand (13.2)
   GET /api/v1/flows ListFlowsQuery (14.2)
   GET /api/v1/flows/{id}  GetFlowByIdQuery (14.2)
   POST /api/v1/flows/{id}/steps AddFlowStepCommand (13.2)
   POST /api/v1/flows/{id}/publish  PublishFlowCommand (13.2)
   POST /api/v1/flows/{id}/archive  ArchiveFlowCommand (13.2)
   GET /api/v1/flows/{familyId}/versions  GetFlowVersionHistoryQuery (14.2)
   POST /api/v1/flows/generate   GenerateFlowFromDescriptionCommand (13.2)
Flow Runs   POST /api/v1/flow-runs  TriggerFlowRunCommand (13.3)
   GET /api/v1/flow-runs   ListRecentFlowRunsQuery (14.3)
   GET /api/v1/flow-runs/{id} GetFlowRunByIdQuery (14.3)
   GET /api/v1/flow-runs/{id}/progress GetLiveRunProgressQuery (14.3)
   POST /api/v1/flow-runs/{id}/cancel  CancelFlowRunCommand (13.3)
   GET /api/v1/flow-runs/{id}/report   GetFlowReportQuery (14.4)
Modules  POST /api/v1/modules RegisterModuleCommand (13.4)
   GET /api/v1/modules  ListRegisteredModulesQuery (14.5)
   GET /api/v1/modules/{id}   GetModuleRegistrationQuery (14.5)
   GET /api/v1/modules/actions   GetAvailableModuleActionsQuery (14.2)
Reporting   GET /api/v1/reports/pass-rate-trend GetPassRateTrendQuery (14.4)
   GET /api/v1/reports/flake-rate   GetFlakeRateQuery (14.4)
   GET /api/v1/reports/failure-clusters   DrillDownFailureSignatureQuery (14.4)
Audit GET /api/v1/audit/export   ExportAuditLogQuery (14.4)
Identity POST /api/v1/users/{id}/roles AssignRoleCommand (13.5)
   DELETE /api/v1/users/{id}/roles/{roleId}  RevokeRoleCommand (13.5)
   GET /api/v1/users ListUsersQuery (14.6)
Triggers POST /api/v1/triggers   (Part III, Section 21.7's trigger-CRUD commands)
   POST /api/v1/webhooks/{triggerPath} Scheduler.OnWebhookReceived (Part III, Section 21.3)

This table is intentionally exhaustive and directly generated-from, not independently maintained against, the command/query catalog — Volume 5's API documentation tooling should treat this mapping as the source of truth, regenerating OpenAPI specs from the command/query type definitions themselves where feasible, rather than hand-maintaining two parallel descriptions that can drift.

30.3 Controller pattern — thin, no business logic
csharp
[ApiController]
[Route("api/v1/flows")]
public class FlowsController : ControllerBase
{
    private readonly ICommandDispatcher _commands;
    private readonly IQueryDispatcher _queries;

    [HttpPost("{id}/publish")]
    public async Task<ActionResult<FlowPublishResultDto>> Publish(Guid id, CancellationToken ct)
    {
        var result = await _commands.Send(new PublishFlowCommand(new FlowId(id), CurrentUser.Id), ct);
        return result.Succeeded ? Ok(result.ToDto()) : UnprocessableEntity(result.ToDto());
    }
}

Every controller action does exactly three things: construct a command or query from the request (mapping HTTP-layer concerns like route/query parameters into the strongly-typed Part II catalog types), dispatch it, and map the result to an HTTP response — no validation logic, no direct repository access, and no business rules of any kind live in a controller. This is a direct, deliberate consequence of Section 13.6 and 13.7's command-handler design (Part II) — the controller is not where "layer 1" or "layer 2" validation happens; it is purely a protocol-translation boundary.

30.4 Request/response conventions
Pagination: list endpoints accept ?page={n}&pageSize={n} and return a consistent PagedResult<T> envelope (items, totalCount, page, pageSize) — the same shape Part II, Section 14's PagedResult<T> DTOs already establish at the query layer, passed through unchanged rather than re-wrapped at the API layer.
Filtering: query-string parameters map directly to a query's optional filter fields (e.g., ?status=Published&search=checkout for ListFlowsQuery).
Idempotency: state-changing POST endpoints that are safe to retry (specifically TriggerFlowRunCommand) accept an optional Idempotency-Key header; a repeated request with the same key within a defined window returns the original result rather than triggering a second execution — directly relevant to Persona Marcus's CI use case, where a flaky network causing a retried API call must never accidentally trigger a flow run twice.
Content negotiation: JSON is the only supported representation for GA; the CI-native report formats (FR-REP-2 — JUnit XML) are served via a distinct Accept: application/xml negotiation on the report endpoint specifically, not as a general API-wide content-type concern.
30.5 Error response format
json
{
  "type": "https://docs.nexus.dev/errors/flow-publish-validation-failed",
  "title": "Flow could not be published",
  "status": 422,
  "detail": "Step 'Fetch order status' references action 'api.request' which is not registered.",
  "errors": [
    { "field": "steps[2].action", "code": "unresolved_module_action" }
  ],
  "correlationId": "c4a1f6e2-..."
}

Follows the RFC 7807 Problem Details standard, extended with a correlationId (Part IV, Section 28.4) so a customer reporting an API error can hand Nexus support a single identifier that ties directly back to the full, correlated internal trace/log data for that request — closing the loop between external-facing error reporting and internal observability (Part IV, Section 29) deliberately, not as an afterthought.

30.6 API versioning

The v1 path segment (30.2) is Nexus's API versioning mechanism — a breaking change to any endpoint's contract requires a new version segment (v2), with both versions served concurrently for a defined deprecation window (mirroring the AutomationModule contract versioning discipline already established in Part III, Section 18.7, applied here to the public API surface specifically). This is a direct, load-bearing application of Architecture Principle 20.2 (contract stability) to the API layer — Persona Marcus's CI integrations (Volume 0, UC-4) depend on this API not breaking underneath a working pipeline without warning.

30.7 Rate limiting and quota

Beyond the authentication-specific rate limiting already established in Part IV, Section 24.9, the general API surface applies tenant-scoped rate limits (requests per minute, tiered by licensing level per Volume 0 Section 16.5) to prevent any single tenant's usage pattern from degrading the shared, pooled hosted infrastructure's service quality for other tenants (Part IV, Section 26.2's pooled-multi-tenancy tradeoff made concrete here) — enforced via the same Redis-backed infrastructure already used elsewhere (Volume 0, Section 21.9).

30.8 Where SignalR fits relative to this REST surface

This section's endpoints are entirely request/response. Live, push-based updates (flow-run progress streaming) are explicitly out of this section's scope and are instead served by the SignalR hub specified next, in Section 31 — the two mechanisms are complementary, not overlapping: GET /api/v1/flow-runs/{id}/progress (this section) provides the initial/catch-up state; the SignalR connection (Section 31) provides the ongoing stream, exactly matching the design already established in Part II, Section 14.3's GetLiveRunProgressQuery documentation.



31. SignalR
31.1 Purpose

This section formalizes the SignalR real-time layer already referenced conceptually across Part I (Section 4.5's communication patterns), Part III (Section 22.4's RunProgressNotifier), and Part V, Section 30.8. Its sole responsibility is streaming live flow-run progress to connected GUI clients — it is not a general-purpose real-time messaging layer for arbitrary application features, a scope boundary stated explicitly to prevent this component from accumulating unrelated responsibilities over time.

31.2 Hub design
csharp
[Authorize]
public class RunProgressHub : Hub
{
    public async Task SubscribeToRun(Guid runId, CancellationToken ct)
    {
        var run = await _authz.EnsureCanView(Context.User, new FlowRunId(runId), ct);  // Part IV, Section 25
        await Groups.AddToGroupAsync(Context.ConnectionId, runId.ToString());
    }

    public async Task UnsubscribeFromRun(Guid runId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, runId.ToString());
    }
}

Consistent with Part IV's authorization discipline (Section 25.3): subscribing to a run's progress stream requires the same ReportView permission (or resource-level check, Section 25.4) as querying that run's report via REST — SignalR subscriptions are not a side-channel that bypasses the standard authorization pipeline, a point worth stating explicitly since real-time/WebSocket layers are a common place for authorization checks to be accidentally weaker than their REST counterparts.

31.3 Group-per-run scoping

As already established in Part III, Section 22.4: clients join a SignalR group keyed by FlowRunId, ensuring a client watching one run's progress never receives events for other concurrent runs — even other runs belonging to the same tenant. This is a deliberate, minimal-blast-radius design: a GUI rendering a single run's live progress view has no need for a tenant-wide event firehose, and scoping at the group level keeps both server-side fan-out cost and client-side message-handling logic proportional to what's actually being watched.

31.4 Connection lifecycle and reconnection
csharp
public class RunProgressConnection
{
    // Client-side (GUI, Volume 2's responsibility) connection lifecycle:
    // 1. Open SignalR connection, call SubscribeToRun(runId)
    // 2. On initial subscribe, immediately call GetLiveRunProgressQuery (REST,
    //    Part II Section 14.3) to fetch current state as of subscription time —
    //    closing any gap between "connection opened" and "first StepCompletedEvent
    //    received," per Section 30.8's REST-provides-catch-up, SignalR-provides-
    //    stream division of responsibility.
    // 3. On disconnect/reconnect (network blip, browser tab backgrounding), the
    //    client re-subscribes AND re-fetches via GetLiveRunProgressQuery again —
    //    never assumes no events were missed during the gap, since SignalR's
    //    automatic reconnection does not replay missed messages by default.
}

This reconnection design directly avoids a subtle class of bug: a naive implementation trusting SignalR's connection to always deliver every event exactly once would silently show stale progress after any network interruption, directly undermining the "live" promise that's the entire point of this component. The explicit re-fetch-on-reconnect pattern makes staleness self-healing within one round-trip, consistent with Architecture Principle 20.4's fail-informatively spirit applied to real-time UI state specifically.

31.5 Scaling SignalR across multiple Orchestration Engine instances

Per NFR-SCALE-2 (horizontal scaling by adding workers): a StepCompletedEvent (Part II, Section 12.3) might be raised by any one of several horizontally-scaled Orchestration Engine worker instances (Part I, Section 4.4's independent-scaling rationale), while the SignalR client connection for the GUI watching that run might be held by a different instance of the API Layer container entirely. This requires a SignalR backplane — Redis-backed (reusing the same Redis infrastructure from Volume 0, Section 21.9, rather than introducing a separate technology purely for this purpose) — so that a group message sent from any worker instance correctly reaches any client connection held by any API Layer instance, transparent to both the publishing and receiving code shown in Sections 22.4 and 31.2.

31.6 Message payload size and throttling

StepCompletedEvent's full StepResult payload (Part II, Section 12.3) is sent as-is for GA, but with an explicit size guard: if a step's Data field (Part II, Section 10.3) exceeds a configured threshold (e.g., a very large API response body captured by the API module), the SignalR payload truncates Data and includes a flag directing the client to fetch the full value via a separate REST call (GetFlowRunByIdQuery, Section 30.2) rather than pushing arbitrarily large payloads through the real-time channel — protecting both connection throughput and the GUI's rendering responsiveness from an unusually large single step result, without limiting what the underlying StepResult can legitimately store.

31.7 What SignalR is deliberately not used for

Restating and slightly expanding 31.1's scope boundary, since it matters for future-proofing: notifications unrelated to an actively-watched flow run (e.g., a general "your scheduled flow just failed" alert while the user isn't looking at that run's progress view) are explicitly not this hub's responsibility — those belong to a separate notification mechanism (webhook delivery, per Part III Section 21's CI-integration use case, or a future in-app notification feature) that is out of this section's scope. Keeping RunProgressHub narrowly scoped to "progress of a run the client has explicitly subscribed to" keeps its authorization model (31.2), scaling model (31.5), and lifecycle (31.4) all simple enough to reason about completely — a direct application of Architecture Principle 20.7's composable-primitives preference, resisting the temptation to grow one real-time channel into a general-purpose pub/sub bus for every future feature.




32. Database Design
32.1 Purpose and scope

This section specifies the concrete PostgreSQL schema realizing Part II's domain model (Sections 9–11) and Part IV's multi-tenancy (Section 26) and audit (Section 27) requirements — the schema Part III, Section 15's FlowMapper-style mapping layers translate to and from. Per Part II, Section 6.5's rigor-versus-specificity guidance, table definitions here are complete enough to be directly implementable, while acknowledging that final column sizing/type tuning is normal implementation-phase engineering judgment.

32.2 Core schema — Flow Authoring context
sql
CREATE TABLE flows (
    id              UUID PRIMARY KEY,
    family_id       UUID NOT NULL,
    version         INT NOT NULL,
    name            VARCHAR(200) NOT NULL,
    description     TEXT,
    status          VARCHAR(20) NOT NULL,   -- Draft | Published | Archived
    tenant_id       UUID NOT NULL REFERENCES tenants(id),
    declared_vars   JSONB NOT NULL DEFAULT '{}',
    created_by      UUID NOT NULL REFERENCES users(id),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at    TIMESTAMPTZ,
    UNIQUE (family_id, version)
);
CREATE INDEX idx_flows_tenant_status ON flows (tenant_id, status);
CREATE INDEX idx_flows_family ON flows (family_id);

CREATE TABLE flow_steps (
    id              UUID PRIMARY KEY,
    flow_id         UUID NOT NULL REFERENCES flows(id) ON DELETE CASCADE,
    step_order      INT NOT NULL,
    name            VARCHAR(200) NOT NULL,
    module_kind     VARCHAR(50) NOT NULL,   -- Part II, Section 11.2's open ModuleKind value
    action          VARCHAR(100) NOT NULL,
    params          JSONB NOT NULL DEFAULT '{}',
    save_as         VARCHAR(100),
    when_condition  TEXT,
    UNIQUE (flow_id, step_order)
);
CREATE INDEX idx_flow_steps_flow ON flow_steps (flow_id);

declared_vars and params are JSONB, directly reflecting Part II Section 6.4's observation that FlowStep.Params is deliberately weakly-typed at the domain-model layer — PostgreSQL's JSONB gives this flexibility efficient storage and, critically, indexability (32.6) without forcing a rigid, per-module-action-specific relational schema that would need to change every time a new module or action is registered.

32.3 Core schema — Execution context
sql
CREATE TABLE flow_runs (
    id              UUID PRIMARY KEY,
    flow_id         UUID NOT NULL REFERENCES flows(id),  -- pinned exact version, Part II Section 9.3
    status          VARCHAR(20) NOT NULL,   -- Pending | Running | Passed | Failed | Cancelled
    trigger_kind    VARCHAR(20) NOT NULL,   -- Api | Scheduled | Webhook
    variables       JSONB NOT NULL DEFAULT '{}',
    tenant_id       UUID NOT NULL REFERENCES tenants(id),
    started_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at     TIMESTAMPTZ
);
CREATE INDEX idx_flow_runs_tenant_status ON flow_runs (tenant_id, status);
CREATE INDEX idx_flow_runs_flow ON flow_runs (flow_id, started_at DESC);  -- supports
    -- ListRecentFlowRunsQuery's FlowIdFilter (Part II, Section 14.3) ordered by recency

CREATE TABLE step_results (
    id                  UUID PRIMARY KEY,
    flow_run_id         UUID NOT NULL REFERENCES flow_runs(id) ON DELETE CASCADE,
    source_step_id      UUID NOT NULL,
    step_order          INT NOT NULL,
    step_name           VARCHAR(200) NOT NULL,   -- denormalized, Part II Section 10.3.1
    module_kind         VARCHAR(50) NOT NULL,
    action              VARCHAR(100) NOT NULL,
    status              VARCHAR(10) NOT NULL,    -- Passed | Failed | Skipped
    duration_ms         INT NOT NULL,
    message             TEXT,
    error               TEXT,
    data                JSONB,
    ai_attribution      JSONB,   -- null unless AI materially altered this step, Part II Section 11.5
    UNIQUE (flow_run_id, step_order)
);
CREATE INDEX idx_step_results_run ON step_results (flow_run_id, step_order);
CREATE INDEX idx_step_results_module_status ON step_results (module_kind, status);  -- feeds
    -- DrillDownFailureSignatureQuery (Part II, Section 14.4)

CREATE TABLE retry_attempts (
    id                  UUID PRIMARY KEY,
    step_result_id      UUID NOT NULL REFERENCES step_results(id) ON DELETE CASCADE,
    attempt_number      INT NOT NULL,
    status              VARCHAR(10) NOT NULL,
    duration_ms         INT NOT NULL,
    error               TEXT,
    attempted_at        TIMESTAMPTZ NOT NULL
);
32.4 Core schema — Module & Plugin context
sql
CREATE TABLE module_registrations (
    id                  UUID PRIMARY KEY,
    module_kind         VARCHAR(50) NOT NULL,
    display_name        VARCHAR(200) NOT NULL,
    trust_tier          VARCHAR(20) NOT NULL,   -- BuiltIn | Verified | Community, Part II Section 11.9
    contract_version    VARCHAR(20) NOT NULL,   -- semantic version, Part III Section 18.7
    is_active           BOOLEAN NOT NULL DEFAULT true,
    tenant_id           UUID REFERENCES tenants(id),  -- NULL for global BuiltIn/Verified modules,
                                                          -- populated for tenant-scoped Community
                                                          -- installs, per Part IV Section 26.5
    UNIQUE (module_kind, tenant_id)
);

CREATE TABLE module_action_entries (
    id                      UUID PRIMARY KEY,
    module_registration_id  UUID NOT NULL REFERENCES module_registrations(id) ON DELETE CASCADE,
    action_name             VARCHAR(100) NOT NULL,
    schema                  JSONB NOT NULL,   -- ActionSchema, Part II Section 11.8
    is_deprecated           BOOLEAN NOT NULL DEFAULT false,
    deprecation_notice      TEXT,
    UNIQUE (module_registration_id, action_name)
);
32.5 Core schema — Identity, Access & Audit
sql
CREATE TABLE tenants (
    id              UUID PRIMARY KEY,
    name            VARCHAR(200) NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE users (
    id                  UUID PRIMARY KEY,
    email               VARCHAR(320) NOT NULL,
    display_name        VARCHAR(200) NOT NULL,
    tenant_id           UUID NOT NULL REFERENCES tenants(id),
    is_service_account  BOOLEAN NOT NULL DEFAULT false,
    role_version        INT NOT NULL DEFAULT 1,   -- Part IV, Section 24.6
    UNIQUE (tenant_id, email)
);

CREATE TABLE roles (
    id                  UUID PRIMARY KEY,
    name                VARCHAR(100) NOT NULL,
    tenant_id           UUID NOT NULL REFERENCES tenants(id),
    is_system_defined   BOOLEAN NOT NULL DEFAULT false,
    permissions         TEXT[] NOT NULL   -- array of Permission enum values, Part IV Section 25.1
);

CREATE TABLE user_roles (
    user_id     UUID NOT NULL REFERENCES users(id),
    role_id     UUID NOT NULL REFERENCES roles(id),
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, role_id)
);

CREATE TABLE audit_entries (
    id              UUID PRIMARY KEY,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    actor_id        UUID NOT NULL REFERENCES users(id),
    action_type     VARCHAR(100) NOT NULL,
    target_type     VARCHAR(100) NOT NULL,
    target_id       VARCHAR(100) NOT NULL,
    metadata        JSONB NOT NULL DEFAULT '{}',
    tenant_id       UUID NOT NULL REFERENCES tenants(id)
);
CREATE INDEX idx_audit_tenant_time ON audit_entries (tenant_id, occurred_at DESC);
-- No UPDATE or DELETE grant on this table for the application role, per Part IV Section 27.5
32.6 Indexing strategy against NFR-SCALE-3

NFR-SCALE-3 (Volume 0) targets 10 million stored flow-run records without an architecture change. The indexing choices above are made specifically with this target in mind: idx_flow_runs_tenant_status and idx_flow_runs_flow are both composite, leading with the highest-selectivity, most commonly-filtered columns (tenant_id, then flow_id) so that the overwhelming majority of query patterns (list runs for a tenant, list runs for a specific flow) hit an index directly rather than scanning. JSONB columns (params, data, metadata) additionally support GIN indexes where a specific query pattern demands it (e.g., if DrillDownFailureSignatureQuery's error-signature clustering, Part II Section 14.4, needs to filter on a specific field within step_results.data at scale) — not applied blanket by default, since GIN indexes carry real write-amplification cost, and should be added deliberately per proven query pattern during implementation, not speculatively for every JSONB column up front.

32.7 Row-level security policies

Directly implementing Part IV, Section 26.3's layer 2:

sql
ALTER TABLE flows ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON flows
    USING (tenant_id = current_setting('app.current_tenant_id')::UUID);
-- Repeated identically for flow_runs, module_registrations (where tenant_id IS NOT NULL),
-- users, roles, audit_entries, and every other multi-tenant table.
32.8 Migration strategy

Schema migrations are managed via EF Core Migrations, applied through a controlled deployment step (never auto-applied on application startup in production, to keep schema changes an explicit, reviewable, rollback-capable operation distinct from a routine deployment) — consistent with Architecture Principle 20.8's self-hosted-first posture, since an uncontrolled auto-migration is a materially riskier default for a self-hosted customer's production database than for a Nexus-operated hosted environment. Full migration tooling and rollout procedure is detailed in Volume 4 (Infrastructure) and Volume 5 (deployment checklist).

32.9 Forward reference

Section 33 renders this schema as a formal ER diagram; Sections 34–36 (Sequence, Class, Package diagrams) show this schema and the domain model from Part II operating together across concrete runtime scenarios and codebase organization.



33. ER Diagrams
33.1 Purpose

This section renders Section 32's schema as a formal entity-relationship diagram, grouped by bounded context (Part II, Section 7) for readability, consistent with this volume's practice of always tying a visual artifact back to the conceptual structure that motivates it rather than presenting schema in isolation.

33.2 Flow Authoring & Execution contexts
┌────────────────────┐          ┌────────────────────┐
│      tenants          │          │       users           │
├────────────────────┤          ├────────────────────┤
│ PK id                 │◄────────┤ FK tenant_id           │
│    name                │    1  n │ PK id                 │
└────────────────────┘          │    email               │
         ▲                        │    is_service_account   │
         │ 1                       │    role_version         │
         │ n                       └────────────────────┘
┌────────────────────┐                      ▲
│       flows           │                      │ created_by
├────────────────────┤                      │
│ PK id                 │──────────────────────┘
│    family_id           │
│    version              │          ┌────────────────────┐
│    status                │          │    flow_steps         │
│ FK tenant_id            │  1     n  ├────────────────────┤
│    declared_vars        │◄─────────┤ FK flow_id             │
└─────────┬──────────┘          │ PK id                 │
          │ 1                       │    step_order           │
          │ n (pinned version)      │    module_kind          │
          ▼                        │    action               │
┌────────────────────┐          │    params (jsonb)        │
│     flow_runs         │          └────────────────────┘
├────────────────────┤
│ PK id                 │          ┌────────────────────┐
│ FK flow_id             │  1     n  │    step_results        │
│    status                │◄─────────┤ FK flow_run_id         │
│    trigger_kind          │          │ PK id                 │
│ FK tenant_id            │          │    source_step_id       │
│    variables (jsonb)     │          │    step_order           │
└────────────────────┘          │    status                │
                                    │    ai_attribution (jsonb)│
                                    └─────────┬──────────┘
                                              │ 1
                                              │ n
                                              ▼
                                    ┌────────────────────┐
                                    │   retry_attempts       │
                                    ├────────────────────┤
                                    │ PK id                 │
                                    │ FK step_result_id      │
                                    │    attempt_number       │
                                    └────────────────────┘
33.3 Module & Plugin context
┌────────────────────┐          ┌────────────────────┐
│      tenants          │          │ module_registrations   │
├────────────────────┤  0..1   n ├────────────────────┤
│ PK id                 │◄─────────┤ FK tenant_id (nullable) │
└────────────────────┘  (null =  │ PK id                 │
                          global)  │    module_kind          │
                                    │    trust_tier            │
                                    │    contract_version      │
                                    └─────────┬──────────┘
                                              │ 1
                                              │ n
                                              ▼
                                    ┌────────────────────┐
                                    │ module_action_entries  │
                                    ├────────────────────┤
                                    │ PK id                 │
                                    │ FK module_registration_id│
                                    │    action_name          │
                                    │    schema (jsonb)        │
                                    │    is_deprecated         │
                                    └────────────────────┘
33.4 Identity, Access & Audit context
┌────────────────────┐          ┌────────────────────┐
│       users           │  n    n  │       roles            │
├────────────────────┤◄────────►├────────────────────┤
│ PK id                 │  via     │ PK id                 │
│ FK tenant_id            │ user_roles│ FK tenant_id           │
│    email                │          │    permissions (text[])  │
└─────────┬──────────┘          │    is_system_defined     │
          │                        └────────────────────┘
          │  1                    ┌────────────────────┐
          │  n                     │     user_roles        │
          └───────────────────────►├────────────────────┤
                                    │ PK,FK user_id           │
                                    │ PK,FK role_id           │
                                    │    assigned_at          │
                                    └────────────────────┘

┌────────────────────┐
│    audit_entries       │
├────────────────────┤
│ PK id                 │
│ FK actor_id  ──────────┼──► users.id
│    action_type          │
│    target_type          │
│    target_id             │
│ FK tenant_id            │──► tenants.id
│    metadata (jsonb)      │
└────────────────────┘
   (append-only: no UPDATE/DELETE grant, Part IV Section 27.5)
33.5 Cross-context relationship summary

The three diagrams above are deliberately drawn separately (per bounded context, Part II Section 7) rather than as one dense combined diagram, because — per the context-mapping discipline established there — the only relationships that legitimately cross context boundaries at the data level are the shared-kernel identifier references (tenant_id, created_by/actor_id referencing users.id, Part II Section 7.4). Every other relationship shown is strictly intra-context. A combined diagram would visually suggest tighter coupling between, say, flow_runs and module_registrations than actually exists at the schema level (they relate only indirectly, through the module_kind string value matched at query time by the Module Dispatcher, Part I Section 5.3 — never via a foreign key, since a FlowStep's module_kind may reference a module not yet registered at authoring time, per the publish-time-only validation established in Part II Section 9.2).

33.6 Notation key

PK = primary key, FK = foreign key, cardinality notation (1, n, 0..1) follows standard crow's-foot convention simplified for ASCII rendering. Dashed versus solid relationship lines are not distinguished in this ASCII format; all relationships shown are enforced foreign-key constraints (Section 32) unless explicitly annotated otherwise (as in 33.5's module_kind string-matched, non-FK relationship).




34. Sequence Diagrams
34.1 Purpose

This section traces three representative scenarios end-to-end through every layer specified across Parts I–V, chosen specifically because each exercises a different, architecturally significant path: a straightforward command (publish), a full execution with live streaming, and the AI self-healing flow with its permission-inheritance and transparency requirements.

34.2 Sequence — Publish a Flow (Volume 0, Persona Priya)
Priya      GUI        API Layer    PublishFlowCmd    Flow          ModuleActionResolver   FlowRepo   UnitOfWork   AuditPipeline
 │          │              │            Handler        │                    │                │            │              │
 │ click    │              │              │             │                    │                │            │              │
 │ Publish  │──POST /flows/{id}/publish──►│             │                    │                │            │              │
 │          │              │──EnsurePermission(FlowPublish)──► [Part IV §25] │                │            │              │
 │          │              │──dispatch──►│             │                    │                │            │              │
 │          │              │             │──GetById──────────────────────────────────────────►│            │              │
 │          │              │             │◄──Flow (Draft)──────────────────────────────────────│            │              │
 │          │              │             │──flow.Publish(resolver)──►│                    │                │              │
 │          │              │             │                          │──resolve each step──►│                │              │
 │          │              │             │                          │◄──all resolved OK────│                │              │
 │          │              │             │◄──FlowPublishResult(Succeeded)──│                │                │              │
 │          │              │             │──Save(flow)──────────────────────────────────────►│                │              │
 │          │              │             │──Commit()──────────────────────────────────────────────────────────►│              │
 │          │              │             │                                                                    │──write outbox──►
 │          │              │             │                                                                    │  + FlowPublishedEvent
 │          │              │             │                                                                    │──dispatch (Tier 1)──►│
 │          │              │             │                                                                                          │──AuditEntry
 │          │              │◄──200 OK, FlowPublishResultDto──│                                                                          (async)
 │◄─────────│◄─────────────│                                 
 │  sees "Published"

Notes on this trace: the ModuleActionResolver call (Section 9.5 of Part II) is a synchronous read, not a separate transaction — the whole publish operation is one atomic Unit of Work commit. The audit entry (Part IV, Section 27) is written durably within that same commit via the outbox (Part III, Section 16.4), but its dispatch to the audit pipeline consumer happens asynchronously afterward — Priya's GUI receives its 200 OK without waiting for that final async step, since nothing about her request's success depends on the audit pipeline having finished processing yet.

34.3 Sequence — Trigger and watch a Flow Run (Volume 0, UC-1 cross-domain regression)
Marcus'CI   API Layer   TriggerFlowRunCmd   FlowEngine    ModuleDispatcher   WebModule    ApiModule    LogsModule   EventBus   SignalR    GUI(Priya, separately watching)
   │           │              Handler          │                │                │            │            │           │          │              │
   │─POST /flow-runs────────►│                │                │                │            │            │           │          │              │
   │           │──dispatch──►│                │                │                │            │            │           │          │              │
   │           │             │──create FlowRun (Pending)                                                                                          │
   │           │◄──202 Accepted, FlowRunId───│                                                                                                     │
   │                          │──FlowEngine.Execute(flow, run)──►│                                                                                  │
   │                                                             │──dispatch step1 (web)──────►│                                                    │
   │                                                             │                              │──navigate,fill,click──► [Playwright]               │
   │                                                             │                              │◄──StepOutcome(Passed, data:{orderId})               │
   │                                                             │──RecordStepResult──► raises StepCompletedEvent ──────────────────────►│           │
   │                                                             │                                                                        │──push──►│──renders step 1 ✓
   │                                                             │──bind saveAs "orderInfo"──►                                                        │
   │                                                             │──dispatch step2 (api, using {{vars.orderInfo.id}})──────►│                          │
   │                                                             │                                          │◄──StepOutcome(Passed)                   │
   │                                                             │──RecordStepResult──► raises StepCompletedEvent ──────────────────────►│──push──►│──renders step 2 ✓
   │                                                             │──dispatch step3 (logs)────────────────────────────►│                              │
   │                                                             │                                                    │◄──StepOutcome(Passed)         │
   │                                                             │──RecordStepResult──► raises StepCompletedEvent ──────────────────────►│──push──►│──renders step 3 ✓
   │                                                             │──run.Complete()──► raises FlowRunCompletedEvent ─────────────────────►│──push──►│──renders "Passed"
   │                                                             │──UnitOfWork.Commit()

Notes: this trace is the concrete, full-system realization of Volume 0's UC-1 — three different modules (Web, API, Logs) dispatched in sequence within one FlowRun, with {{vars.orderInfo.id}} interpolation (Part III, Section 19.4) carrying data from step 1's result into step 2's parameters. Priya's GUI, watching via SignalR (Section 31) in parallel with Marcus's CI-triggered execution, receives live updates through the exact mechanism specified in Part III Section 22.4 and Part V Section 31 — this diagram is where those two previously-separately-specified components are shown actually operating together in one realistic scenario.

34.4 Sequence — Selector self-heal (Volume 0, UC-3, AI transparency requirement)
FlowEngine   WebModule   ModuleDispatcher   AiModule    LLM Provider   StepResult    EventBus    AuditPipeline
    │            │               │              │             │            │             │              │
    │──dispatch step (web, click)──►│           │              │            │             │              │
    │            │──selector not found (broken)──►             │            │             │              │
    │◄───────────│  StepOutcome(Failed, error:"selector not found")         │             │              │
    │  [self-healing enabled for this flow, per Volume 0 UC-3]              │             │              │
    │──issue RequestSelectorHealCommand──────────────►│                     │             │              │
    │            │                                    │──EnsurePermission (inherits triggering identity, Part IV §25.6)
    │            │                                    │──call with broken selector, intent, page HTML────►│
    │            │                                    │◄──proposed selector, confidence:0.87──────────────│
    │            │                                    │  [confidence exceeds configured threshold]
    │◄───────────────────────────────────────────────│  SelectorHealResult(newSelector, confidence:0.87)
    │──retry dispatch with healed selector──►│         
    │            │──click succeeds──►         
    │◄───────────│  StepOutcome(Passed)
    │──build StepResult WITH AiAttribution attached (Part II §11.5)─────►│
    │──RecordStepResult──► raises StepCompletedEvent AND StepSelfHealedEvent ──►│
    │                                                                            │──durable relay──►│──AuditEntry
    │                                                                                                    (records the heal,
    │                                                                                                     FR-AI-3 compliance)

Notes: this is the trace that makes FR-AI-3's "every AI-assisted intervention recorded distinguishably" concrete and verifiable — StepSelfHealedEvent (Part II, Section 12.3) fires in addition to StepCompletedEvent, never instead of it, and the resulting StepResult's non-null AiAttribution field (Part II, Section 11.5) is what a report (Part II, Section 8.5) or the audit trail (Part IV, Section 27) surfaces to a human reviewing this run later — nothing about this heal is silent, per Product Philosophy 4.4's "AI proposes, deterministic code disposes, and never silently."

34.5 Reading these diagrams alongside Parts I–IV

Each diagram above deliberately names the exact component and section from earlier in this volume responsible for each step, rather than presenting an idealized flow disconnected from the rest of this document set — the intent is that a reader arriving at this section after reading Parts I–IV in order recognizes every actor in these traces as an already-specified component, and a reader arriving here first can use these traces as a map pointing back to where each piece is fully specified.



35. Class Diagrams
35.1 Purpose

This section renders Part II's domain model (Sections 9–11) as a formal UML-style class diagram, showing aggregate boundaries, composition versus association relationships, and the plugin contract's position relative to the domain model — the visual counterpart to Part II's prose-and-code-sketch specification.

35.2 Flow Authoring & Execution aggregates
┌───────────────────────────┐
│      Flow «AggregateRoot»    │
├───────────────────────────┤
│ + FamilyId: FlowFamilyId     │
│ + Version: int                │
│ + Name: string                 │
│ + Status: FlowStatus           │
│ + TenantId: TenantId           │
├───────────────────────────┤
│ + AddStep(step)                │
│ + Publish(resolver): Result     │
└─────────────┬─────────────┘
              │ 1
              │ composes
              │ n
┌─────────────▼─────────────┐        ┌───────────────────────┐
│   FlowStep «Entity»          │◄───────┤  VariableExpression       │
├───────────────────────────┤  uses  │      «ValueObject»          │
│ + Name: string                 │  0..n  ├───────────────────────┤
│ + Module: ModuleKind            │        │ + RawTemplate: string     │
│ + Action: string                │        │ + ReferencedPaths: [string]│
│ + Params: dict                  │        └───────────────────────┘
│ + SaveAs: string?                │
│ + WhenCondition: string?         │
│ + Order: int                     │
└───────────────────────────┘

┌───────────────────────────┐
│    FlowRun «AggregateRoot»   │
├───────────────────────────┤
│ + FlowId: FlowId (pinned)      │
│ + Status: RunStatus             │
│ + Trigger: RunTrigger            │
│ + Variables: dict                │
│ + TenantId: TenantId             │
├───────────────────────────┤
│ + RecordStepResult(result)       │
│ + Complete()                      │
└─────────────┬─────────────┘
              │ 1
              │ composes
              │ n
┌─────────────▼─────────────┐        ┌───────────────────────┐
│  StepResult «Entity»          │◄───────┤  AiAttribution              │
├───────────────────────────┤  0..1  │     «ValueObject»           │
│ + SourceStepId: FlowStepId     │        ├───────────────────────┤
│ + Status: StepStatus            │        │ + Capability: string       │
│ + Duration: TimeSpan             │        │ + Confidence: double       │
│ + Data: object?                  │        │ + WasAccepted: bool        │
└─────────────┬─────────────┘        └───────────────────────┘
              │ 1
              │ composes
              │ n
┌─────────────▼─────────────┐
│ RetryAttempt «Entity»         │
├───────────────────────────┤
│ + AttemptNumber: int             │
│ + Status: StepStatus              │
└───────────────────────────┘
35.3 Module & Plugin — aggregate and contract, shown together
┌────────────────────────────┐          ┌─────────────────────────────┐
│ ModuleRegistration              │          │      IAutomationModule           │
│    «AggregateRoot»               │          │        «Interface»                │
├────────────────────────────┤ implements├─────────────────────────────┤
│ + Kind: ModuleKind                 │◄─ ─ ─ ─ ─│ + Kind: ModuleKind               │
│ + Trust: TrustTier                  │  (the    │ + ContractVersion: SemVer       │
│ + ContractVersion: SemVer           │  registered│ + GetSupportedActions()        │
├────────────────────────────┤  entity's│ + Setup(ctx)                     │
│ + composes n ActionEntry             │  Kind must│ + Run(action, params, ctx)      │
└────────────────────────────┘  match its│ + Teardown(ctx)                 │
              │ 1                         implementation's                └─────────────────────────────┘
              │ composes                  Kind, enforced                              △
              │ n                         at registration,                            │ implements
┌─────────────▼─────────────┐  Part III §18.6)          ┌──────┼──────┬──────┬──────┬──────┐
│ ModuleActionEntry «Entity»    │                         │      │      │      │      │      │
├───────────────────────────┤                        ┌───▼──┐┌─▼───┐┌─▼───┐┌─▼────┐┌─▼───┐┌─▼──┐
│ + ActionName: string           │                         │ApiMod││WebMod││MobMod││DeskUi││Logs││ AiMod│
│ + Schema: ActionSchema          │                         └──────┘└─────┘└─────┘└──────┘└─────┘└────┘
│ + IsDeprecated: bool             │                          Built-in modules (Part III §18); marketplace-
└───────────────────────────┘                          installed modules implement the same interface
                                                              (Community/Verified tier, §18.5)

The dashed "implements" line at the top of this diagram is the single most important relationship on this page: ModuleRegistration (a Part II domain aggregate, persisted in the module_registrations table, Section 32.4) and IAutomationModule (a Part III plugin-contract interface, implemented by actual running code) are two different things describing the same conceptual module from two different angles — the aggregate is data about a module; the interface is behavior a module provides. Keeping these visually and architecturally distinct (rather than merging them into one type) is what makes the Module Registration API's introspection (Part I, Section 5.3) possible without needing to instantiate or execute a module just to describe it.

35.4 Cross-aggregate relationships (association, not composition)
Flow ─ ─ ─ (referenced by FlowId, immutable pin) ─ ─ ─► FlowRun
FlowStep ─ ─ ─ (Module+Action resolved against, NOT a foreign-key/composition relationship) ─ ─ ─► ModuleRegistration
StepResult ─ ─ ─ (AiAttribution references) ─ ─ ─► (no direct link back to AiModule instance — only the value object)
User ─────(shared-kernel reference, Part II §7.4)────► every aggregate's TenantId/CreatedBy/ActorId field

Dashed arrows throughout this section denote association or reference relationships (holding an identifier, resolved at read or validation time) as distinct from the solid-line composition relationships in 35.2–35.3 (a parent aggregate directly owning and cascading lifecycle to its child entities). This distinction is not merely notational pedantry — it's the visual expression of Part III, Section 9.5's explicit rule that aggregates never directly reference one another's objects, only their identifiers, with resolution happening through an injected port at the application-service layer.

35.5 Value object cluster (selected, per Part II Section 11)
        ┌─────────────────┐
        │   Assertion         │
        │  «abstract record»  │
        └────────△────────┘
                  │
    ┌─────────────┼─────────────┬─────────────┐
    │             │             │             │
┌───▼────┐  ┌────▼─────┐  ┌───▼─────┐  ┌────▼──────┐
│ Status   │  │PathEquals │  │PathCont- │  │PathExists   │
│Assertion │  │Assertion  │  │ainsAssert│  │Assertion     │
└─────────┘  └──────────┘  └─────────┘  └────────────┘

This closed-hierarchy pattern (Part II, Section 11.6's "make illegal states unrepresentable" rationale) is shown here specifically because it's a recurring shape worth recognizing across the domain model — the same discriminated-union pattern applies equally to Assertion (shown here), and could be applied to any future closed set of mutually-exclusive value-object variants the domain model grows to need.

35.6 Relationship to Part VI's coding standards

The strongly-typed-identifier convention (Part II, Section 10.8), the aggregate/entity/value-object distinctions shown throughout this section, and the composition-versus-association discipline (35.4) are not merely descriptive here — Part VI, Section 40 (Coding Standards) elevates each of these into an enforced code-review checklist item, making this class diagram a direct, checkable specification of what correctly-structured Nexus domain code looks like, not just an illustrative aid.



36. Package Diagrams
36.1 Purpose

This section specifies the .NET solution's project/namespace structure — the concrete folder-and-assembly organization that every prior section's design maps onto. This is the direct bridge between this volume's architectural specification and Volume 5's coding standards, which will assume this structure as given.

36.2 Solution structure
Nexus.sln
│
├── src/
│   ├── Nexus.Domain/                    # Part II — pure domain model, zero external dependencies
│   │   ├── FlowAuthoring/                #   (per bounded context, Part II §7)
│   │   │   ├── Flow.cs, FlowStep.cs        (Section 9.2, 10.2)
│   │   │   └── FlowStatus.cs, VariableExpression.cs  (Section 11.4, 11.7)
│   │   ├── Execution/
│   │   │   ├── FlowRun.cs, StepResult.cs, RetryAttempt.cs  (Section 9.3, 10.3, 10.4)
│   │   │   └── RunStatus.cs, StepStatus.cs, AiAttribution.cs  (Section 11.3, 11.5)
│   │   ├── ModulePlugin/
│   │   │   ├── ModuleRegistration.cs, ModuleActionEntry.cs  (Section 9.4, 10.5)
│   │   │   └── ModuleKind.cs, ActionSchema.cs, TrustTier.cs  (Section 11.2, 11.8, 11.9)
│   │   ├── IdentityAccess/
│   │   │   └── User.cs, Role.cs  (Section 10.6, Part IV §25.1)
│   │   ├── ReportingAudit/
│   │   │   └── AuditEntry.cs  (Section 10.7)
│   │   └── SharedKernel/
│   │       ├── Ids.cs             # every strongly-typed identifier (Section 10.8, 11.10)
│   │       ├── AggregateRoot.cs, Entity.cs, IHasDomainEvents.cs
│   │       └── DomainException.cs
│   │
│   ├── Nexus.Contracts/                 # Part III §18 — the public AutomationModule interface
│   │   ├── IAutomationModule.cs, ExecutionContext.cs, StepOutcome.cs
│   │   └── (published as a standalone NuGet package — this is THE artifact a third-party
│   │        module developer, Persona Raj, actually references, per Part III §18.1's
│   │        emphasis on this contract's central importance — kept in its own project
│   │        specifically so it can be versioned and distributed independently of the
│   │        rest of the (closed-source, or open-core per Volume 0 §16.2) solution)
│   │
│   ├── Nexus.Application/               # Part II §13-14 — commands, queries, handlers
│   │   ├── Commands/                     #   organized by bounded context, mirroring Domain/
│   │   ├── Queries/
│   │   ├── EventHandlers/                # Part III §22.5's consumer implementations
│   │   └── Abstractions/                 # IUnitOfWork, I*Repository interfaces (Part III §15-16)
│   │
│   ├── Nexus.Infrastructure/             # Part III, Part IV, Part V §32 — concrete implementations
│   │   ├── Persistence/
│   │   │   ├── NexusDbContext.cs, migrations/
│   │   │   └── Repositories/              # FlowRepository, FlowRunRepository, etc. (§15.4)
│   │   ├── Modules/                      # built-in module implementations
│   │   │   ├── Api/, Web/, Mobile/, DesktopUi/, Logs/, Ai/
│   │   ├── Plugins/
│   │   │   ├── InProcessExecutionStrategy.cs, SandboxedExecutionStrategy.cs  (§18.5)
│   │   ├── Identity/                      # authentication providers, JWT issuance (Part IV §24)
│   │   ├── Observability/                 # OpenTelemetry setup (Part IV §29)
│   │   └── Messaging/                     # Event Bus Tier 1/2 implementation (§22.2)
│   │
│   ├── Nexus.Api/                        # Part V §30-31 — ASP.NET Core Web API host
│   │   ├── Controllers/
│   │   ├── Hubs/                          # RunProgressHub (§31.2)
│   │   └── Program.cs                      # DI composition root (Part III §17)
│   │
│   ├── Nexus.Orchestration/               # Part I §5, Part III §19-21 — the Orchestration
│   │   │                                     Engine container's own host process (separate
│   │   │                                     from Nexus.Api per Part I §4.4's container split)
│   │   ├── FlowEngine.cs, ModuleDispatcher.cs, ModuleRegistry.cs
│   │   ├── Scheduler.cs                    (§21)
│   │   └── Program.cs
│   │
│   └── Nexus.Cli/                        # the CLI client (Part I §2.2, Volume 0 FR-PLAT-2)
│
├── tests/
│   ├── Nexus.Domain.Tests/                # pure unit tests, no infrastructure dependency,
│   │                                         per Part III §15.5, §16.7's testability design
│   ├── Nexus.Application.Tests/
│   ├── Nexus.Infrastructure.IntegrationTests/
│   └── Nexus.Api.IntegrationTests/
│
└── plugins-sdk/
    └── Nexus.PluginSdk.Samples/          # a reference third-party module implementation,
                                              directly serving Volume 0 §15.6's "reference
                                              third-party module" Phase 4 roadmap milestone
36.3 Dependency direction — the core rule
Nexus.Api  ──────┐
Nexus.Orchestration ─┼──► Nexus.Infrastructure ──► Nexus.Application ──► Nexus.Domain
Nexus.Cli  ──────┘                                        △
                                                            │
                                                    Nexus.Contracts
                                             (referenced by Application
                                              and Infrastructure, but
                                              NOT by Domain — Domain has
                                              zero knowledge that a plugin
                                              contract even exists, per
                                              Part II §6.4's boundary)

Dependencies point strictly inward, standard Clean/Onion Architecture discipline: Nexus.Domain references nothing else in this solution (per Section 15.5's persistence-ignorance rule, extended here to a project-reference-level enforcement — a .csproj reference from Nexus.Domain to Nexus.Infrastructure is not just discouraged, it should fail CI via a dependency-direction analyzer, per Volume 5's forthcoming enforcement). Nexus.Contracts sits adjacent to, not inside, Nexus.Domain specifically because — as 36.2 notes — it needs independent versioning and distribution as a standalone package for third-party consumption, which would be awkward if it lived inside the same assembly as the rest of the (potentially not independently distributed) domain model.

36.4 Why Nexus.Api and Nexus.Orchestration are separate executable projects

Direct implementation of the container-separation decision already justified architecturally in Part I, Section 4.4: these are not just logical namespaces within one deployable but genuinely separate ASP.NET Core / .NET worker-service host projects, each with their own Program.cs composition root, independently deployable and independently scalable, both depending on the same shared Nexus.Infrastructure and Nexus.Application projects rather than duplicating logic between them.

36.5 Package-level testability mapping

Restating Part III, Section 15.5 and 16.7's testing-design goals in terms of this concrete structure: Nexus.Domain.Tests requires zero test-container/database setup (pure in-memory object construction and assertion against Part II's aggregate behavior); Nexus.Application.Tests uses mocked repository/Unit-of-Work interfaces to test command/query handler logic in isolation; only Nexus.Infrastructure.IntegrationTests and Nexus.Api.IntegrationTests require a real (or test-container-provisioned) PostgreSQL/Redis instance — this tiering directly informs Volume 5's CI pipeline design, where the fast, dependency-free domain and application test suites can run on every commit, while the slower integration suites run on a less frequent but still mandatory gate.

36.6 Closing note for Volume 1, Part V

This section completes the mapping from Part II's domain model, through Part III's engineering patterns, Part IV's cross-cutting services, and this Part's API/data specification, into an actual, buildable solution structure. Part VI (Deployment, Performance, Scalability, Coding Standards) is the final piece of Volume 1, taking this structure and specifying how it's built, tested, deployed, and maintained as an ongoing engineering practice.


___________________________________________________________________________________________________________________________________________________



37. Deployment
37.1 Purpose — from topology to concrete deployment artifacts

Volume 0, Section 19.5 established two deployment topologies (managed/hosted, self-hosted) and Section 21.12 locked Docker/Kubernetes/Helm as the deployment technology. Part I, Section 4 fixed the container boundaries. This section specifies the concrete deployment artifacts and operational procedures that realize those decisions for the solution structure fixed in Part V, Section 36.

37.2 Container images

Each independently-deployable project from Section 36.2 produces its own container image, built from a minimal multi-stage Dockerfile (SDK image for build, runtime-only image for the final artifact, keeping production image size and attack surface minimal — a direct, concrete application of Architecture Principle 20.5's security-by-design principle to the deployment artifact itself, not just application logic):

Image From project   Notes
nexus-api   Nexus.Api   Stateless, horizontally scalable per NFR-SCALE-2
nexus-orchestration  Nexus.Orchestration  Stateless at the process level (all durable state in PostgreSQL/Redis per Part III §16, §19.8), horizontally scalable
nexus-cli   Nexus.Cli   Distributed as a standalone binary/NuGet tool per Volume 0 §21.2, not typically deployed as a long-running container, but a container image is published for CI environments that prefer container-based tooling
nexus-sandbox-runner (a minimal runtime hosting IModuleExecutionStrategy's sandboxed side, Part III §18.5)  One ephemeral container instance per sandboxed Community-tier module execution, not a long-running service
37.3 Kubernetes topology (reference architecture)
┌─────────────────────────────────────────────────────────────────┐
│                         Kubernetes Cluster                          │
│                                                                     │
│  ┌───────────────┐   ┌───────────────┐   ┌────────────────┐     │
│  │  nexus-api        │   │ nexus-          │   │  nexus-           │     │
│  │  Deployment        │   │ orchestration    │   │  sandbox-runner    │     │
│  │  (HPA: 3-20 pods)  │   │ Deployment       │   │  Job/Pod (ephemeral,│     │
│  │                    │   │ (HPA: 2-15 pods) │   │  per Community-tier │     │
│  │                    │   │                  │   │  module invocation) │     │
│  └───────┬───────┘   └───────┬───────┘   └────────────────┘     │
│          │                     │                                     │
│          └──────────┬──────────┘                                     │
│                       │                                                │
│              ┌────────▼────────┐        ┌──────────────────┐       │
│              │  Redis (backplane, │        │  Outbox Relay        │       │
│              │  cache, distributed│        │  BackgroundService    │       │
│              │  locks — §21.4,    │        │  (runs within          │       │
│              │  §31.5)            │        │  nexus-orchestration,  │       │
│              └────────────────┘        │  not a separate pod)    │       │
│                                              └──────────────────┘       │
└─────────────────────────────────────────────────────────────────┘
          │                                          │
          ▼                                          ▼
   ┌─────────────┐                          ┌─────────────────┐
   │  PostgreSQL     │                          │  Observability       │
   │  (managed or     │                          │  Backend              │
   │  self-hosted per  │                          │  (OTLP → Prometheus/  │
   │  Volume 0 §21.8)  │                          │  Grafana or customer's │
   └─────────────┘                          │  own stack, §29)      │
                                                └─────────────────┘

Helm charts (one per image in 37.2, composed into an umbrella chart for the full platform) parameterize every environment-specific value (replica counts, resource limits, database/Redis connection details, AI-provider configuration per Volume 0 §21.7's provider-abstraction) — the self-hosted reference deployment uses the identical chart set as the managed/hosted environment, differing only in values files, directly implementing Architecture Principle 20.8's "self-hosted is a first-class target, not a downscoped version."

37.4 Horizontal Pod Autoscaling and NFR-SCALE-1

nexus-orchestration's HPA is configured against nexus.orchestration.concurrent_executions (Part IV, Section 29.4's custom metric, exposed via a Prometheus adapter feeding Kubernetes' custom-metrics API) rather than generic CPU/memory utilization alone — CPU-based autoscaling would react too late for a component whose actual bottleneck (concurrent flow-execution capacity, per NFR-SCALE-1's 500-execution target) is not always CPU-bound (a flow waiting on a slow target-system API response consumes minimal CPU while still occupying execution capacity). This is a direct, deliberate application of Part IV's observability design (Section 29) feeding back into the deployment layer's own scaling decisions, not two independently-designed systems.

37.5 Database migration procedure

Consistent with Part V, Section 32.8's "never auto-migrate on startup" rule: migrations run as a distinct Kubernetes Job, executed as an explicit step in the deployment pipeline before the new nexus-api/nexus-orchestration image versions are rolled out, with the Job's success gating the subsequent rollout — a failed migration halts deployment before any pod is running code that expects a schema the database doesn't yet have. Migrations are additionally required (per Volume 5's forthcoming coding-standards enforcement) to be backward-compatible for at least one deployment cycle (the old code version must still function against the new schema) to support zero-downtime rolling deployments, directly serving NFR-REL-1's 99.9% availability commitment through the deployment process itself, not only through runtime architecture.

37.6 Blue/green and canary considerations for the Orchestration Engine specifically

nexus-api, being stateless per-request, supports standard rolling/canary deployment with no special handling. nexus-orchestration requires one additional consideration: an in-flight FlowRun (Part II, Section 9.3) should not be abruptly terminated mid-execution by a pod being torn down during a rollout. The deployment configuration sets a generous terminationGracePeriodSeconds and the Nexus.Orchestration host implements graceful shutdown (stop accepting new flow-trigger dispatches, allow in-flight FlowEngine.Execute calls, Part III Section 19.2, to reach a natural step boundary before exiting) — a direct, deployment-level extension of the same CancellationToken propagation already threaded through the execution loop for the CancelFlowRunCommand use case (Part III, Section 19.2), reused here for graceful infrastructure-initiated shutdown rather than user-initiated cancellation.

37.7 Self-hosted installation path

For a self-hosted customer (Persona Elena's segment, Volume 0 Section 10.6), the installation procedure is: helm repo add, provide a values file specifying database/Redis endpoints and (optionally, per NFR-SEC-2/NFR-REL-3) AI-provider configuration or explicit AI-disable, and helm install. No outbound internet dependency is required for the core (non-AI) platform to function post-installation, per NFR-SEC-4 — a self-hosted deployment behind a fully air-gapped network boundary is a supported, tested configuration, not merely a theoretical one, with Volume 4's infrastructure documentation responsible for the fully detailed air-gapped installation procedure (image mirroring to a private registry, etc.) that this section only summarizes at the architectural level.

37.8 Rollback procedure

Every deployment is a Helm release with an associated revision; rollback is helm rollback to the prior revision, with the database-migration backward-compatibility guarantee from Section 37.5 being the specific property that makes this safe — a rollback that reverted application code to a version expecting an older schema, against a database that had already been migrated forward, would be a serious operational hazard were it not for that guarantee, so 37.5 and this section's rollback procedure should always be read and validated together, not independently.



38. Performance
38.1 Purpose — engineering practice, not restated targets

Volume 0, Section 13.2 already stated the performance NFRs (NFR-PERF-1 through NFR-PERF-4) as targets. This section specifies the concrete engineering practices across the architecture already defined in Parts I–V that are relied upon to actually hit those targets, and the measurement discipline that verifies they're being hit continuously, not just at initial launch.

38.2 NFR-PERF-1 (50ms p95 step-dispatch overhead) — where the budget is spent and protected

Referencing Part III, Section 19.4's design-time budget allocation, restated here as an operational practice: every release, the distributed tracing data (Part IV, Section 29.3) is used to compute actual p95 dispatch-overhead — measured as the span duration from FlowEngine's step-loop entry to the ModuleDispatcher.Dispatch call's start (i.e., excluding the module's own span) — as a mandatory CI/release-gate check (Volume 5's quality-gate design will consume this metric directly). A regression here is treated with the same severity as a functional test failure, not filed as a "performance backlog" ticket to address later — this reflects Architecture Principle 20.4's fail-fast philosophy applied to the release process itself.

38.3 NFR-PERF-2 (200ms p95 for flow CRUD) — the read-your-own-writes design payoff

Part V, Section 23.4's decision to keep FlowSummaryReadModel synchronously (same-transaction) updated, rather than event-projected, is the direct performance-relevant design choice underlying this NFR — a flow-list query never waits on eventual-consistency catch-up, and the write path's cost (an additional synchronous denormalized-table update within the same Unit of Work transaction, Part III Section 16) is a bounded, small addition to the already-required aggregate-save operation, not a separate round-trip.

38.4 NFR-PERF-3 (90-day trend view under 2s at p95) — the batch-aggregation tradeoff

Directly implemented by Part V, Section 23.5's TrendAggregateReadModel batch-computation design — the 2-second budget is achievable specifically because the dashboard query reads a small, pre-aggregated table rather than scanning potentially millions of step_results rows (Part V, Section 32.3) live. This section adds the operational discipline: the batch job's own execution time and staleness window (Section 23.6 of Part V) are themselves monitored metrics (extending Part IV, Section 29.4's table with a nexus.trend_aggregation.duration and nexus.trend_aggregation.staleness_seconds pair), since a silently-failing or increasingly-slow batch job would otherwise degrade this NFR's actual user-facing guarantee without any dashboard-query-side symptom to catch it.

38.5 NFR-PERF-4 (15s AI timeout, graceful fallback) — timeout enforcement at the call boundary
csharp
public class AiProviderClient
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);  // NFR-PERF-4

    public async Task<T> Call<T>(Func<CancellationToken, Task<T>> providerCall, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultTimeout);
        try
        {
            return await providerCall(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _metrics.RecordAiTimeout();  // nexus.ai.request.timeout, Part IV §29.4
            throw new AiProviderTimeoutException();  // caught by the calling command handler,
                                                          // which falls back to normal (non-AI)
                                                          // failure handling per NFR-REL-3
        }
    }
}

This is the concrete implementation of the graceful-degradation promise already specified conceptually in Part III, Section 18 and Part IV — the timeout is enforced at the narrowest possible boundary (the actual outbound provider call), not by hoping some outer layer catches a slow AI response eventually.

38.6 Database query performance discipline

Beyond the indexing strategy already specified in Part V, Section 32.6: every query handler (Part II, Section 14.7) is required, per Volume 5's forthcoming coding standard, to have its generated SQL reviewed for index utilization before merge — a query plan showing a sequential scan against a table expected to grow toward NFR-SCALE-3's 10-million-row target is treated as a defect, caught at code-review time via EXPLAIN ANALYZE output attached to the pull request, rather than discovered later as a production incident. This is a process discipline this section establishes as a binding practice, formalized fully as a checklist item in Section 40 (Coding Standards).

38.7 Caching strategy

Redis (Volume 0, Section 21.9) serves three distinct caching roles, each with a deliberately different invalidation strategy stated explicitly to avoid the class of bug where a cache's staleness assumptions are implicit and get violated by a later change:

Permission/RoleVersion cache (Part IV, Section 24.6, 25.5): invalidated explicitly on any AssignRoleCommand/RevokeRoleCommand, never time-based expiry alone — correctness-critical, so explicit invalidation is mandatory, with time-based expiry only as a defensive fallback.
ModuleActionReadModel cache (Part V, Section 23.3's table): short TTL-based expiry (e.g., 60 seconds) is acceptable, since this data's own consistency guarantee (Part V, Section 23.6) is already stated as "eventually consistent," so an additional short cache layer doesn't introduce a new class of staleness beyond what's already a documented contract.
Rate-limiting counters (Part IV, Section 24.9, Part V, Section 30.7): inherently ephemeral, Redis's native TTL/expiry mechanism used directly, no separate invalidation logic needed.
38.8 Load testing as a release gate

Volume 5's CI quality gates (forward-referenced here, fully specified there) include a mandatory load-test run against a staging environment before any GA-track release, exercising NFR-SCALE-1's 500-concurrent-execution target directly (not extrapolated from a smaller test) — this section establishes the requirement that this test exists and gates release; Volume 5 owns the specific tooling and pass/fail thresholds.

38.9 Performance regression as a first-class defect category

Consistent with the severity stance already taken in Section 38.2: this section closes by stating a general principle that governs all four NFR-PERF targets equally — a performance regression against any NFR-PERF target discovered post-release is triaged with the same urgency as a functional correctness bug, tracked in the same risk register discipline Volume 0 established (Section 22), because a platform whose own performance degrades unpredictably directly undermines the trust posture (Volume 0, Business Objective 5) this entire document set has repeatedly treated as non-negotiable.



39. Scalability
39.1 Purpose — distinguishing from Section 38

Performance (Section 38) is about how fast the system responds under normal, expected load. Scalability, this section's concern, is about how the system behaves as load grows — both how far it can scale, and by what mechanism (horizontal vs. vertical, and along which specific dimension). Volume 0's NFR-SCALE-1 through NFR-SCALE-3 are the targets; this section is the engineering account of how the architecture already specified across Parts I–V achieves them, drawn together in one place since scalability is inherently a cross-cutting property no single prior section fully owns.

39.2 NFR-SCALE-1 (500 concurrent flow executions) — the dimension that actually matters

The relevant scaling dimension is concurrent FlowEngine.Execute invocations (Part III, Section 19.8), each of which is a lightweight async task, not an OS thread — .NET's async/await model means the practical ceiling here is governed by downstream resource contention (database connection pool size, Redis connection pool, outbound HTTP client pool for the API module, browser-process pool for the Web module) far more than by raw CPU or memory per se. Section 39.3 addresses how each of these pools is sized and scaled independently.

39.3 Resource pool scaling per module category
Resource Scaling approach
Database connections (PostgreSQL)   Connection pooling via a pooler (PgBouncer or equivalent, Volume 4's specific choice) sitting in front of PostgreSQL, since NFR-SCALE-1's 500 concurrent executions would otherwise risk exceeding PostgreSQL's own reasonable direct-connection ceiling as nexus-orchestration pods scale out (Section 37.4)
Redis connections Standard client-side connection multiplexing (a single multiplexed connection per pod is sufficient for Redis's protocol design, not requiring a pool sized to 500)
Web module — browser processes   A bounded pool of headless browser instances per nexus-orchestration pod (Playwright's own browser-context reuse capability, per Volume 0 §21.4, allows many concurrent FlowRuns to share a smaller number of underlying browser processes via separate contexts, rather than requiring a 1:1 process-per-execution ratio)
Mobile module — device/emulator sessions  Inherently the tightest-constrained resource (Volume 0, Section 14.6's Partial-maturity scoping already reflects this) — session availability is bounded by actual connected devices/emulators or device-farm capacity, not something the orchestration layer's own scaling can increase; this is documented explicitly here as a scaling ceiling the architecture cannot engineer around, only queue against
Sandboxed (Community-tier) module processes  Kubernetes Job/Pod-per-invocation (Section 37.2's nexus-sandbox-runner), scaling via the cluster's own pod-scheduling capacity, bounded by cluster resource quotas configured per Volume 4
39.4 Horizontal scaling of stateless containers

Both nexus-api and nexus-orchestration are designed, per Part I Section 4.4's original container-separation rationale and Part III's persistence-ignorance/statelessness discipline (Section 15.5, 17.5), to hold zero durable in-memory state that would be lost or duplicated across horizontally-scaled instances — every piece of state that must survive or be shared across instances lives in PostgreSQL, Redis, or is coordinated via the outbox/Event Bus (Part III, Section 22). This is what makes Section 37.4's HPA-based autoscaling actually safe: a new pod added under load has zero warm-up state dependency and a terminated pod (Section 37.6's graceful shutdown aside) loses no data other than in-flight work it hands off gracefully.

39.5 NFR-SCALE-2 (horizontal scaling without client-facing changes) — verified, not assumed

This NFR is explicitly a testable claim, not just a design intention: Volume 5's release process includes a specific verification step — scale nexus-orchestration from N to 2N pods under active load and confirm zero client-visible behavior change (no dropped SignalR connections beyond the backplane's normal reconnect handling per Section 31.5, no failed flow triggers, no duplicate executions) — treated as a release gate for any change touching the Orchestration Engine's scaling-relevant code paths (the outbox relay, the distributed cron-lock mechanism from Part III Section 21.4, the SignalR backplane from Section 31.5), specifically because these are exactly the components where a subtle bug would only manifest under multi-instance operation, never in a single-developer local environment.

39.6 NFR-SCALE-3 (10 million flow-run records) — scaling the data layer specifically

Beyond Part V, Section 32.6's indexing strategy, this section adds the operational scaling mechanism for when indexing alone becomes insufficient at the far end of this target: PostgreSQL table partitioning on flow_runs and step_results, partitioned by started_at/time range (a standard, well-understood PostgreSQL scaling technique for time-series-shaped, append-heavy tables) — explicitly deferred as a "when needed, not by default at GA" mechanism, consistent with Part V Section 32.6's stated principle of adding indexing/scaling complexity deliberately per proven need rather than speculatively. The schema design in Part V, Section 32 (time-ordered primary access patterns, no cross-partition foreign-key complications since step_results always references a flow_runs row within the same or an adjacent time window) was deliberately kept compatible with a future move to partitioning without requiring a schema redesign, even though partitioning itself is not part of the GA implementation.

39.7 Scaling the Audit and Observability pipelines alongside the core

A scaling dimension easy to overlook: as nexus-orchestration scales per Section 39.4–39.5, the volume of domain events (Part II, Section 12), audit entries (Part IV, Section 27), and telemetry (Part IV, Section 29) scales proportionally. The outbox relay (Part III, Section 22.2) and observability export pipeline are therefore included in the same load-testing discipline as Section 38.8 — a load test that only verifies flow-execution throughput while ignoring whether the audit pipeline keeps pace would give a false sense of confidence, since a durable-consumer backlog (Part IV, Section 29.4's nexus.outbox.relay.lag metric) growing unboundedly under sustained load is itself a scalability failure, even if flow execution itself appears to be keeping up.

39.8 Multi-tenancy and scalability — the pooled-model implication restated

Directly connecting back to Part IV, Section 26.2's pooled-multi-tenancy decision: NFR-SCALE-1's 500-concurrent-execution target is a platform-wide figure in the pooled hosted model, not per-tenant — Section 30.7's tenant-scoped rate limiting is the mechanism that prevents one large tenant's usage from starving others' fair share of this shared capacity, and is therefore as much a scalability-fairness mechanism as it is the security/abuse-prevention mechanism it was originally introduced as in Part V — the same rate-limiting infrastructure serves both purposes simultaneously, a direct example of Architecture Principle 20.7's composable-primitives preference paying off across two different concerns from one mechanism.

39.9 Known scaling ceilings, stated honestly

Consistent with this volume's practice of stating limitations plainly rather than implying unlimited scalability: the Mobile module's device/session availability (Section 39.3) and the Desktop UI module's inherent single-machine-execution-context nature (Volume 0, Section 14.7's Minimal/Preview GA scoping) are scaling ceilings the current architecture does not attempt to engineer around — they are documented here as known, accepted constraints, consistent with Volume 0's Section 14.10 maturity table, rather than silently glossed over as if every module scaled identically to the API and Logs modules' comparatively unconstrained profile.



40. Coding Standards
40.1 Purpose — consolidating this volume's forward references

Nearly every prior section in this volume deferred a specific enforcement mechanism to "Part VI's coding standards." This section is where those deferred obligations are collected and made concrete and binding — it is not a general-purpose C# style guide (indentation, brace placement, and similar low-stakes conventions are assumed to follow standard .NET community conventions via .editorconfig and are not re-litigated here); it is specifically the set of Nexus-architecture-specific rules that keep the design integrity established across Parts I–V from eroding over time as the codebase grows and more engineers contribute to it. Volume 5's broader Engineering Playbook covers git workflow, PR process, and cross-language (React/Python) standards; this section covers only backend/C#-specific, architecture-enforcing rules.

40.2 Mandatory, statically-enforced rules

These are enforced via Roslyn analyzers or equivalent static-analysis tooling in CI — a violation fails the build, not merely a code-review comment:

Rule  Enforces Source
Every entity/aggregate identifier is a strongly-typed wrapper, never a raw Guid in a public method signature   Compile-time prevention of transposed-ID bugs   Part II, Section 10.8
No project reference from Nexus.Domain to Nexus.Application, Nexus.Infrastructure, Nexus.Api, or Nexus.Contracts  Dependency-direction discipline, persistence ignorance   Part V, Section 36.3; Part III, Section 15.5
No ILogger call using string interpolation ($"...") — structured message templates only   Queryable, correlated operational logging Part IV, Section 28.2
Every [SensitiveValue]-attributed command/event property is automatically excluded from audit metadata and log output derivation Prevents secret leakage into logs/audit trail   Part IV, Section 27.4, 28.5
Nullable reference types enabled (<Nullable>enable</Nullable>) project-wide, with warnings treated as errors   Makes optionality (e.g., AiAttribution?, Part II §11.5) a compiler-checked property, not a runtime surprise Cross-cutting
40.3 Mandatory, code-review-enforced rules

These require human judgment to apply correctly and are therefore checklist items in the PR template rather than fully automatable, but are treated as blocking, not optional, feedback:

Command handlers perform layer-1 (authorization) validation before loading any aggregate, per Part II, Section 13.6 — a reviewer checks that EnsurePermission (Part IV, Section 25.3) is the first meaningful line of every command handler's Handle method.
Every new command inherits AuditableCommandHandler (Part IV, Section 27.2) unless an explicit, documented justification for exemption is included in the PR description — an unaudited state-changing operation is the specific gap Section 27.2's base-class pattern exists to close, and an unjustified exemption is grounds for review rejection.
Aggregates never hold a direct reference to another aggregate's repository or another aggregate's instance — cross-aggregate checks go through an injected port interface (Part II, Section 9.5's IModuleActionResolver pattern), reviewed explicitly whenever a PR touches aggregate code in Nexus.Domain.
New database queries include EXPLAIN ANALYZE output in the PR description when touching a table expected to scale toward NFR-SCALE-3's target (flow_runs, step_results, audit_entries) — per Part VI, Section 38.6.
New IAutomationModule methods or ExecutionContext fields are additive only within a minor/patch contract version — a reviewer checks any change to Nexus.Contracts against Part III, Section 18.7's versioning discipline specifically, given this interface's outsized importance (Part III, Section 18.1).
Singleton-lifetime services (built-in modules, per Part III Section 17.2) hold no mutable instance state — reviewed explicitly for any new field added to a built-in module class, per Part III, Section 17.5's concurrency-safety requirement.
40.4 Naming conventions specific to this domain model
Aggregate roots, entities, and value objects use the exact ubiquitous-language term from Part II, Section 6.3's glossary — a pull request introducing a synonym (e.g., "Execution" instead of "FlowRun") is rejected on that basis alone, regardless of code quality otherwise, per Part II Section 6.3's explicit instruction that this is treated as a defect, not a style nit.
Commands are named as imperative verb phrases (PublishFlowCommand), domain events as past-tense verb phrases (FlowPublishedEvent), per Part II, Sections 12.1 and 13.1's established convention, applied without exception to any newly-added command or event.
DTOs returned from queries are suffixed Dto and never share a name with the domain entity they're derived from without that suffix (FlowDetailDto vs. Flow), per Part II, Section 14.8's explicit entity/DTO separation rule.
40.5 Async and concurrency conventions
Every I/O-bound method is async and accepts a CancellationToken as its final parameter, propagated from the originating request/execution context (Part III, Section 19.8's concurrency model, Part I Section 2's RunCancellation threading) — a method performing I/O without an accepted, honored CancellationToken is a defect, since it breaks the graceful-shutdown (Section 37.6) and run-cancellation (CancelFlowRunCommand, Part II §13.3) guarantees this volume has built on that propagation being reliable end-to-end.
No .Result or .Wait() blocking calls on Task/Task<T> anywhere in the codebase — a synchronous-over-asynchronous call is a known deadlock risk in ASP.NET Core request contexts and is flagged automatically by the same analyzer tooling as Section 40.2's rules.
Shared mutable state across concurrent flow executions is prohibited outside of explicitly-designed, thread-safe infrastructure (Redis-backed distributed locks per Part III, Section 21.4; the Variable Store's per-run scoping per Part I, Section 5.3) — any new field on a component shared across concurrent executions (any singleton-lifetime service, per Section 40.2/40.3) must be reviewed specifically for this concern.
40.6 Testing conventions

Directly extending the testability design already established in Part III, Sections 15.5, 16.7, and Part V, Section 36.5:

Nexus.Domain.Tests tests construct aggregates directly and assert on their public behavior and raised domain events — no mocking framework is needed or permitted here, since the domain layer has no external dependencies to mock, per its zero-dependency design (Section 40.2's dependency-direction rule making this structurally true, not just a testing convention).
Nexus.Application.Tests tests use hand-written or generated fakes for repository/IUnitOfWork interfaces (Part III, Sections 15 and 16), asserting both the expected aggregate method calls and the expected persisted/committed state — a test that doesn't verify IUnitOfWork.Commit was called for a state-changing command is treated as an incomplete test.
Every Permission (Part IV, Section 25.1) has at least one test asserting both the authorized-success and unauthorized-rejection path for every command it gates, per Part IV, Section 25.8's compliance-review-readiness requirement — tracked as explicit, auditable test-coverage rather than assumed.
Integration tests (Nexus.Infrastructure.IntegrationTests, Nexus.Api.IntegrationTests) run against a real, ephemeral PostgreSQL/Redis instance (test containers) — never against mocked infrastructure, since the specific concerns these tests exist to catch (row-level security policies, Part IV §26.3; outbox/transaction atomicity, Part III §16.4) are only genuinely verifiable against real infrastructure behavior.
40.7 Documentation-as-code discipline

Every IAutomationModule implementation's GetSupportedActions() return value (Part III, Section 18.2) must include a non-empty Description on every ActionSchema and ParamSpec (Part II, Section 11.8) — this is enforced by a build-time check specifically because this data is what the GUI's flow-authoring surface renders directly to end users (Part III, Section 18.6) and to Persona Raj's fellow module developers browsing the marketplace (Volume 0, Section 17) — undocumented actions are a direct, user-visible product defect, not merely a documentation gap, and are treated with that severity.

40.8 Architecture Decision Records for deviations

Any code change that deviates from a pattern specified in this volume — a new aggregate that doesn't follow Part II, Section 9's design rules; a new cross-cutting service that doesn't follow Part IV's authorization-first convention — requires an accompanying ADR (per Volume 0, Section 20.9's instruction to cite architecture principles by number) explaining the deviation's rationale, reviewed and approved by the architecture owner (Volume 0, Section 22's risk-register ownership model) before merge, not merged first and documented later. This is the concrete mechanism that keeps this 40-section volume a living, accurate description of the actual codebase rather than a document that quietly diverges from reality over time — directly addressing Volume 0's own RISK-13 concern about documentation-program staleness, now closed with a specific, binding process rather than good intentions alone.

40.9 Closing note — Volume 1 complete

This section closes Volume 1 in full: 40 sections across 6 Parts, tracing from the C4 system context (Part I) through the domain model (Part II), the engineering patterns implementing it (Part III), the cross-cutting services securing and observing it (Part IV), the API and data layer exposing and persisting it (Part V), to the operational and coding discipline that keeps it correct in production and over time (this Part). Every design decision in this volume traces back through Volume 0's business objectives, requirements, and architecture principles, and every future volume — Frontend Architecture, AI Architecture, Infrastructure Architecture, and the Development Standards & Engineering Playbook — builds on this backend specification as its foundation.


___________________________________________________________________________________________________________________________________________________



📘 ADS-002 — Frontend Architecture Design

Objective

Design a modern IDE-like frontend for creating, managing, executing, and monitoring automation.

Chapter 1 – UI Philosophy
Desktop-like experience
Minimal clicks
Productivity-first
Responsive
Accessibility



Chapter 2 – Technology Stack
React 19
TypeScript
Vite
TailwindCSS
shadcn/ui
TanStack Router
TanStack Query
Zustand
SignalR



Chapter 3 – Application Structure
Apps

Pages

Features

Components

Layouts

Services

Store

Hooks

Utilities



Chapter 4 – Routing
Dashboard
Projects
Test Suites
Workflow Designer
Reports
Plugins
AI Assistant
Settings



Chapter 5 – State Management

Global

Local

Server State

Real-time

Offline Cache

Chapter 6 – Component Libraryx

Buttons

Tables

Forms

Dialogs

Editors

Monaco

Tree View

Execution Console

Charts

Chapter 7 – Feature Modules

Dashboard

Project Explorer

Execution Monitor

Workflow Builder

Locator Manager

Object Repository

Report Viewer

Plugin Marketplace

AI Chat

Chapter 8 – UX Guidelines

Dark mode

Keyboard shortcuts

Notifications

Progress indicators

Error handling

Chapter 9 – Security

Authentication

Authorization

Session

Permissions


_________________________________________________________________________________________________________________________________________________________________


