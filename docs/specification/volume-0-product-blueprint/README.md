# Volume 0 — 

Product Blueprint

## Table of Contents

- **Part I — Vision**
  - [Section 1 — Executive Summary](#1-executive-summary)
  - [Section 2 — Vision Statement](#2-vision-statement)
  - [Section 3 — Mission](#3-mission)
  - [Section 4 — Product Philosophy](#4-product-philosophy)
  - [Section 5 — Business Objectives](#5-business-objectives)
  - [Section 6 — Success Metrics](#6-success-metrics)
  - [Section 7 — Problem Statement](#7-problem-statement)
  - [Section 8 — Market Analysis](#8-market-analysis)
  - [Section 9 — Competitor Analysis](#9-competitor-analysis)
  - [Section 10 — User Personas](#10-user-personas)
  - [Section 11 — Use Cases](#11-use-cases)
  - [Section 12 — Functional Requirements](#12-functional-requirements)
  - [Section 13 — Non-functional Requirements](#13-non-functional-requirements)
  - [Section 14 — Platform Modules](#14-platform-modules)
  - [Section 15 — Product Roadmap](#15-product-roadmap)
  - [Section 16 — Licensing Strategy](#16-licensing-strategy)
  - [Section 17 — Plugin Marketplace Vision](#17-plugin-marketplace-vision)
  - [Section 18 — AI Strategy](#18-ai-strategy)
  - [Section 19 — High-Level Architecture](#19-high-level-architecture)
  - [Section 20 — Architecture Principles](#20-architecture-principles)
  - [Section 21 — Technology Decisions](#21-technology-decisions)
  - [Section 22 — Risks](#22-risks)
  - [Section 23 — Future Vision](#23-future-vision)

---

## Part I — Vision

---

## 1. Executive Summary

Enterprise quality engineering and operations teams today assemble their automation capability from five to eight disconnected tools: Postman or similar for API testing, Selenium or Playwright for web UI, Appium for mobile, an RPA product for desktop automation, a log/observability stack (ELK, Datadog, Splunk), and increasingly a scattering of point AI tools bolted onto one or two of the above. Each tool has its own configuration language, its own reporting format, its own CI integration story, its own credential store, and its own learning curve. A single end-to-end business scenario — "a customer places an order on the website, the mobile app reflects it, the payment API confirms it, and no error appears in the logs" — cannot be expressed as *one* automated check anywhere in this stack. It has to be stitched together by a human, across tools, by hand, every time.

Nexus is proposed as a unified automation platform that treats API, Web, Mobile, Desktop UI, Log/Observability, and AI-driven automation as six capability modules sitting behind **one orchestration core, one flow language, and one reporting model**. A single flow can open a browser, submit a form, call the resulting order-confirmation API, tail the application log for errors in the same time window, and — where a step doesn't yet exist — ask the AI module to generate it from a plain-English description. The engineering value is not "one more automation tool"; it is the elimination of the integration tax that currently sits between five or six best-of-breed tools that were never designed to talk to each other.

This document (Volume 0) is the product blueprint: the vision, the market and user reasoning behind it, the platform's modular shape, and the architectural principles that Volumes 1 through 5 will each expand into an implementation-grade specification. Volume 0 answers "what are we building and why." The volumes that follow answer "how, precisely."

The remainder of Part I sets out the vision and mission that every subsequent architectural decision in this document set should be traceable back to, the philosophy that should break ties when two valid engineering approaches compete, the business objectives that justify the investment, and the metrics by which we will know, concretely, whether the product is succeeding.

---

## 2. Vision Statement

> **Nexus exists so that any automatable behavior of a software system — however it is exposed, whatever layer it lives at — can be described once, in one language, and executed, monitored, and maintained from one platform.**

Unpacked, this vision makes three specific claims, each of which constrains everything that follows in this document set:

**"Any automatable behavior."** The platform is not a web-testing tool with API support bolted on, nor an API tool with a UI plugin. All six capability domains — API, Web, Mobile, Desktop UI, Logs, AI — are first-class citizens of the architecture from the ground up. No module should be structurally privileged over another; the same orchestrator, the same flow schema, the same reporting pipeline serves all of them identically. This is the single hardest constraint in the entire document set, because it is far easier to build a great web-automation tool with bolted-on extras than a genuinely uniform platform — and it is the difference that justifies Nexus's existence over the incumbents.

**"Described once, in one language."** A flow author should never need to context-switch languages or mental models mid-scenario. Whether a step drives a browser or asserts on a log pattern, it is expressed in the same declarative flow syntax, with the same variable-passing and conditional semantics. This is what makes cross-domain scenarios (the order-placement example above) expressible at all.

**"Executed, monitored, and maintained from one platform."** Execution is necessary but not sufficient. A platform is only genuinely unifying if the *maintenance* burden — updating a broken selector, adjusting an assertion, understanding why last night's run failed — is also unified. This is where the AI layer earns a permanent, architectural role rather than a marketing footnote: self-healing selectors, failure root-cause summarization, and flow generation from English are maintenance-cost reducers first, novelty features second.

---

## 3. Mission

While the vision describes the end state, the mission describes what the team building Nexus commits to doing, continuously, to get there and stay there:

- **Build the orchestration core before any single module is "finished."** The temptation on any team building a multi-module product is to make one module excellent and defer the others. Nexus's mission is the opposite discipline: the orchestrator, the flow schema, and the module interface contract are treated as the product, and each capability module is treated as a (replaceable, extensible) implementation of that contract. This ordering is why Volume 1 (Backend Architecture) leads with the execution engine and plugin architecture before it ever discusses a specific module.

- **Never let AI become a required dependency for core functionality.** Every AI-assisted capability (flow generation, self-healing, triage) must have a manual, deterministic fallback path. A team without API-key budget, or in a regulated environment where AI-assisted test generation is not permitted, must still have a fully functional automation platform. AI is a force-multiplier layered on top of a complete deterministic core — never the core itself.

- **Treat the CLI and the API as the primary interface, and any GUI as a client of that API.** This ordering (elaborated in Volume 2) is what keeps Nexus embeddable in CI/CD pipelines, scriptable, and free of the common enterprise-tool failure mode where automation can only be triggered by clicking a button in someone's browser.

- **Keep every module's contract small enough to be re-implemented by a third party in an afternoon.** The plugin architecture (Volume 1, Part on Domain-Driven Design & Plugin Architecture) is only real if it is genuinely easy to add a seventh module — a database automation module, a message-queue automation module — without touching the core. The mission is to keep proving this by dogfooding it internally.

---

## 4. Product Philosophy

Four philosophical commitments recur throughout every volume in this set. Where a design decision in Volumes 1–5 seems to need justification, it should trace back to one of these four:

### 4.1 Composability over completeness
A platform that tries to anticipate every possible automation need up front will always be one enterprise-specific requirement behind its users. Nexus instead optimizes for making six or seven *small, composable primitives* (an HTTP request step, a log-rule step, a click step) that can be assembled into arbitrarily complex flows, rather than trying to ship a "checkout automation" feature or a "login automation" feature as a monolithic capability. Composability is also what makes the plugin architecture viable — a new module only needs to expose primitives, not anticipate every use of them.

### 4.2 Flows are short, vertical slices — not brittle end-to-end scripts
The industry's default automation pattern is the long end-to-end script: forty steps, one failure halfway through invalidates the whole run, and a maintainer has to re-run the entire thing to find out if step 41 would have passed. Nexus's philosophy is fail-fast, short flows (this is a concrete architectural decision, not just a suggestion — see Volume 1's orchestrator design) chained together at the CI-pipeline level rather than the flow level. A ten-step flow that fails at step 3 tells you exactly what broke in under a second. A 40-step flow that fails at step 3 wastes 37 steps of execution time finding that out.

### 4.3 The maintenance cost of automation is the real cost center
Writing an automated check is cheap. Keeping five hundred automated checks passing as the underlying application changes weekly is where automation programs actually die — this is the industry's most consistent failure pattern (see Market Analysis, Section 8). Every architectural decision that trades a small amount of authoring convenience for a large reduction in maintenance burden (self-healing selectors, declarative assertions instead of imperative scripts, chained variables instead of copy-pasted values) is preferred by default.

### 4.4 Transparent over magical
Where the AI layer makes a decision — generating a flow, healing a selector, triaging a log — the platform always shows its reasoning and its confidence, and always allows a human to accept, reject, or edit the suggestion before it is persisted. Nexus's AI philosophy (elaborated fully in Volume 3) is "AI proposes, deterministic code disposes" — the AI module never silently mutates a flow definition on its own authority.

---

## 5. Business Objectives

The vision and mission above are the "why" for engineering. This section is the "why" for the business sponsoring the engineering effort. Five objectives, each with the timeframe and rationale that Volume 0 Part III (Roadmap) will decompose into milestones:

| # | Objective | Rationale |
|---|---|---|
| 1 | **Reduce the number of distinct automation tools a QA/DevOps org must license, learn, and integrate from 5–8 down to 1** | This is the core value proposition and the primary driver of both cost savings and adoption speed for a new customer. |
| 2 | **Cut the average time-to-first-passing-flow for a new team from weeks to under a day** | Tool-sprawl's biggest hidden cost is onboarding time. A unified flow language with AI-assisted generation directly targets this. |
| 3 | **Reduce automation maintenance effort (hours/week spent fixing broken checks) by a target of 40%+ within two release cycles of adoption** | Directly addresses the industry's most common automation-program failure mode (checks rot faster than teams can fix them). Self-healing and better failure diagnostics are the primary levers. |
| 4 | **Establish a plugin marketplace as a second revenue and retention channel within 18 months of GA** | Long-term defensibility does not come from the six built-in modules alone — it comes from a thriving ecosystem that makes switching costs high in the customer's favor, not just the vendor's. |
| 5 | **Achieve enterprise-grade trust (SOC2-track security posture, on-prem/self-hosted deployment option, no mandatory AI dependency) from the first GA release, not retrofitted later** | Enterprise buyers evaluating a *replacement* for existing critical infrastructure will not tolerate "security is on the roadmap." This must be true at GA, which is why Volume 4 (Infrastructure) and Volume 5 (Engineering Playbook) are peers of the product architecture volumes, not afterthoughts. |

---

## 6. Success Metrics

Business objectives without measurable success metrics are aspirations. This section defines the concrete, trackable metrics for each objective above, plus platform-health metrics that apply regardless of business objective.

### 6.1 Adoption & consolidation metrics
- **Tool consolidation ratio**: average number of legacy automation tools a customer decommissions within 6 months of Nexus adoption. Target: ≥3.
- **Time-to-first-passing-flow**: median wall-clock time from account creation to first green flow run in the customer's own environment. Target: < 4 hours for API-only flows, < 1 business day for flows spanning 3+ modules.
- **Cross-module flow adoption rate**: percentage of active customer flows that use 2 or more modules in a single flow (the number that validates the "unification" thesis specifically, as opposed to Nexus simply being used as a single-purpose tool by habit). Target: ≥25% within the first year of a customer's usage.

### 6.2 Maintenance-burden metrics
- **Selector/locator self-heal acceptance rate**: percentage of AI-proposed selector fixes that a human accepts without further edits. Target: ≥70% (below this, the feature creates more review burden than it saves).
- **Mean time to diagnose a failed run**: from failure notification to a human understanding root cause, with vs. without AI-assisted triage. Target: 50%+ reduction versus the customer's prior toolchain, self-reported via onboarding survey baseline.
- **Flake rate**: percentage of flow re-runs (identical inputs, identical target environment) that produce a different pass/fail result. This is a platform-quality metric, not just a customer metric — a high flake rate anywhere is a defect in Nexus's own execution engine, not "normal for automation." Target: < 2% across all modules at GA, trending toward < 0.5%.

### 6.3 Platform health metrics
- **Module parity index**: for a fixed checklist of orchestrator-level capabilities (retries, conditionals, variable interpolation, reporting, AI-flow-generation), the percentage of those capabilities that work identically across all six modules. Target: 100% — any module-specific gap is treated as a P1 defect against the "any automatable behavior" vision claim, not a backlog item.
- **Plugin development time**: wall-clock time for an experienced engineer unfamiliar with the specific new capability domain to build a minimally viable seventh module against the published `AutomationModule` interface. Target: < 1 day, validated quarterly by actually doing it (dogfooding the plugin architecture, per the Mission section above).
- **AI-optionality compliance**: percentage of platform capabilities (excluding the AI module's own features) that remain fully functional with zero AI/LLM API calls configured. Target: 100%, tested as a release gate (see Volume 5, CI/CD Quality Gates).

These metrics are the ones every subsequent architectural and product decision in this document set will be checked against. Where Volume 1 proposes a specific execution-engine design, or Volume 3 proposes a specific AI-guardrail approach, the test is: does this measurably move one of the numbers above, or does it just feel like good engineering in the abstract.

---

*End of Part I. Part II — Product (Problem Statement through Non-functional Requirements) continues the blueprint by grounding this vision in specific market evidence, personas, and requirements.*

_____________________________________________________________________________________________________________________________________________________________

7. Problem Statement

7.1 The core problem

Enterprise software delivery organizations need to verify that systems behave correctly across every layer those systems expose — API, web UI, mobile app, desktop client, and the operational logs that record what actually happened at runtime. No single commercial or open-source tool today does this. Instead, organizations assemble a portfolio of single-purpose automation tools, and the cost of assembling, integrating, and maintaining that portfolio has grown to rival or exceed the cost of the automation work itself.

This is not a hypothetical inefficiency. It is the single most consistent complaint surfaced in QA leadership surveys, DevOps maturity assessments, and engineering-productivity retrospectives across the industry (see Section 8, Market Analysis, for sourced figures). The problem decomposes into five concrete, independently-verifiable sub-problems.

7.2 Sub-problem 1 — Tool fragmentation prevents cross-layer scenarios

A realistic business scenario almost never lives entirely inside one layer. "A user completes checkout" touches the web UI (or mobile app), the order and payment APIs, and should leave a clean trail in the application logs with no unexpected errors. Today, verifying this scenario end-to-end requires:

A Selenium/Playwright/Cypress script for the UI portion
A separate Postman/RestAssured/custom-HTTP-client script for the API portion
A separate log-query (Splunk search, CloudWatch Insights query, grep-over-SSH) for the observability portion
A human, or a fragile shell script, gluing the three together and reconciling three different pass/fail formats into one verdict

Each of the three tools was designed in isolation, has no awareness of the others' state, and cannot pass data between them (e.g., the order ID generated by the UI step, needed by both the API check and the log query) without custom glue code that itself becomes a maintenance liability. The result: cross-layer scenarios are disproportionately rare in most automation suites relative to how common they are as actual user journeys — not because they're unimportant, but because they're disproportionately expensive to build and maintain given the tooling available.

7.3 Sub-problem 2 — Maintenance cost grows faster than the team's capacity to absorb it

Automated checks are not fire-and-forget. Every UI change can break a selector. Every API contract change can break an assertion. Every log format change can break a pattern rule. Industry data consistently shows that the ongoing cost of maintaining an existing automation suite exceeds the cost of writing it within the first one to two release cycles, and that this ratio only worsens as the suite grows (Section 8 cites specific figures). Because today's tools are fragmented, a UI change that also changes an API contract requires two separate maintenance actions in two separate tools, often performed by two different people with two different skill sets, discovered by two different failure notifications at two different times. Nothing correlates the two failures as "the same underlying change" for the maintainer.

7.4 Sub-problem 3 — No unified signal for "is the system healthy"

Because the tools don't share a reporting model, there is no single dashboard, single report, or single CI gate that says "here is the current health of the system across every layer we test." Organizations approximate this with manually-assembled status pages or weekly meetings that synthesize five separate tools' outputs by hand. This is slow, error-prone, and — critically — not available at the speed of a CI pipeline, where a single unified pass/fail signal is what actually gates a deployment.

7.5 Sub-problem 4 — AI assistance is arriving piecemeal, not architecturally

Self-healing selectors, AI-generated test cases, and AI-assisted log triage are all real, valuable, increasingly available capabilities — but each is being retrofitted into a single existing single-purpose tool (a web-testing tool gets AI selector healing; a log tool gets AI-assisted anomaly summarization). None of the incumbent tools were architected with a cross-domain AI layer in mind, so a customer who wants AI assistance for both UI maintenance and log triage today needs two different AI-enabled products from two different vendors, each with its own AI cost model, guardrails, and data-handling policy. There is a market opening for a platform where the AI layer is architected once, across all modules, from the start — which is precisely Nexus's Volume 3 mandate.

7.6 Sub-problem 5 — Cost and licensing sprawl compounds the technical problem

Each point tool in the fragmented stack carries its own commercial license, its own vendor relationship, its own renewal cycle, its own compliance/security review, and often its own infrastructure footprint (a device farm subscription here, a log-ingestion tier there). Beyond the direct license cost, this creates a hidden tax in procurement overhead, vendor-risk assessment, and integration engineering that scales with the number of tools, not with the amount of automation actually being performed. A platform that consolidates six capability domains under one commercial relationship directly removes this multiplier — this is the business case underpinning Business Objective 1 in Part I.

7.7 Why this problem is tractable now, and wasn't five years ago

Three shifts make solving this problem viable today in a way it was not a product cycle or two ago:

Modern browser and OS automation protocols have converged. Playwright's cross-browser protocol, Appium's W3C-standardized mobile automation, and mature accessibility-tree tooling for desktop mean that "one abstraction over many automation backends" is now an engineering-tractable problem rather than a research problem.
LLMs have crossed a real capability threshold for structured-output generation and code-adjacent reasoning. Turning "check that /health returns 200 and status: ok" into structurally valid flow JSON, or looking at a diff between a broken selector's context and a new DOM and proposing a fix, are both squarely within current-generation LLM capability — this was not reliably true even two to three years prior.
The economics of API-based LLM access make an AI layer viable as a platform feature rather than a moonshot R&D bet. Per-call cost and latency for the tasks in Section 7.5 above are now low enough to be a normal part of a CI pipeline's cost budget, not a special "AI experiment" line item — provided (per the Product Philosophy AI-optionality principle in Part I) that the platform never makes them mandatory.
7.8 Problem statement, summarized

Organizations cannot express, execute, or maintain a single business scenario that spans API, UI, mobile, desktop, and log layers without stitching together multiple disconnected tools by hand — and the resulting maintenance and integration burden grows faster than most teams' capacity to absorb it. No current platform treats these layers as peers under one orchestration model with a unified, architected AI layer.

Every subsequent section of this blueprint — market analysis, personas, use cases, and functional requirements — exists to test and substantiate this statement, and every module in Volumes 1–5 exists to solve one clause of it.



8. Market Analysis

8.1 Purpose and method of this section

This section frames the market opportunity in terms of the buying categories Nexus intersects, the structural trends favoring a unified platform, and the assumptions this blueprint is making about market size and willingness to pay. Where this section cites a figure, it is marked either as an industry-observed pattern (a widely-corroborated directional trend, not a single sourced statistic) or an internal planning assumption (a number this team is using for roadmap and pricing planning that must be validated against real discovery calls and analyst data before being used in any external-facing materials). This distinction matters: a product blueprint that quietly launders assumptions into false-precision statistics produces bad downstream decisions. Volume 0 should not do that.

8.2 The buying categories Nexus intersects

Nexus does not create a new budget line; it competes for, and aims to consolidate, spend that already exists across several established categories:

Test automation / QA tooling (web and API test automation, historically the largest of these categories)
Mobile test automation (frequently a separate budget and team from web QA, even within the same organization)
RPA (robotic process automation) for desktop/legacy UI automation, typically bought by an operations or business-process team rather than engineering
Observability / log management, typically bought by a platform or SRE team, rarely by QA at all
Emerging "AI-native testing" tools, a fast-growing sub-category layered on top of one of the above rather than standing alone

The core market thesis is that these five categories, while sold and budgeted separately today, are converging in practitioner intent even where they have not yet converged in procurement structure. The people who own web QA, mobile QA, and increasingly observability, are frequently the same engineering organization, often the same individuals, forced to operate five separate tools because no vendor has offered the consolidated alternative.

8.3 Industry-observed patterns supporting the consolidation thesis
Tool sprawl is a named, recurring pain point in QA and platform-engineering community discussion (conference talks, practitioner surveys, vendor-neutral engineering blogs) independent of any single vendor's marketing. "We have too many testing tools that don't talk to each other" recurs across organizations of very different sizes and industries, which suggests a structural problem rather than a company-specific process failure.
AI-assisted test maintenance features are being added to nearly every major point tool (self-healing selectors in web-testing tools; AI-assisted assertion generation in API tools) within a similar time window across competitors — a pattern consistent with genuine, broadly-felt maintenance-cost pain rather than a single vendor's differentiation strategy.
Observability and testing budgets are increasingly co-owned as organizations adopt SRE and platform-engineering models, where the same team is accountable for both "does it work" (testing) and "is it working right now" (observability) — a structural shift that supports Nexus's thesis that logs/observability belongs as a peer module, not a bolt-on, to test automation.
8.4 Internal planning assumptions (require validation)

The following are stated explicitly as assumptions, not researched facts, because Volume 0 is being written before a formal market-sizing exercise. They should be replaced with sourced analyst figures (Gartner, Forrester, or a commissioned study) before appearing in any investor or customer-facing document:

Assumption	Current planning value	Validation needed before external use
Mid-to-large engineering orgs (200+ engineers) run 4+ distinct automation tools concurrently	Working assumption for persona and roadmap planning	Primary research (discovery interviews) with 15–20 target accounts
Average enterprise automation tooling spend across the fragmented categories in 8.2	Used only as a directional pricing anchor internally	Third-party market sizing report
Time engineers spend on cross-tool "glue" work (scripts stitching one tool's output into another's input)	Assumed material but currently unquantified	Time-tracking study or survey during design-partner phase

Volume 0's roadmap and pricing sections (Part III) should be read with this caveat attached: they represent a defensible starting hypothesis, not validated market sizing.

8.5 Market timing

Section 7.7 (Problem Statement) already covered the technical timing case (protocol convergence, LLM capability threshold, AI cost economics). The market timing case rests on two additional, independently-observable shifts:

Platform engineering and internal developer platform (IDP) initiatives are actively looking for consolidation opportunities. Where five years ago each team optimized its own tool choice independently, platform engineering teams are now explicitly mandated to reduce the number of tools an organization operates, for exactly the cost and integration reasons in Section 8.3. This creates a receptive buyer that did not structurally exist at the same scale previously.
AI-native competitors are entering from the "green field" direction (new startups building AI-first testing tools with no legacy single-domain product to defend), which validates the market's appetite for AI-assisted automation but, per Section 7.5, are generally not solving the cross-domain unification problem — they are AI-first but still domain-narrow. This leaves the specific intersection Nexus targets (unified orchestration and architected AI layer) contested but not yet won by an incumbent.
8.6 Market risks

A market analysis that only lists tailwinds is not a serious one. Three risks are tracked explicitly here and revisited in Section 22 (Risks) with mitigation owners:

Point-tool incumbents may bolt on cross-domain capability faster than Nexus can build a moat. A well-funded web-testing incumbent adding a "logs" tab and an "API" tab is a plausible fast-follow response, even if their underlying architecture (Section 7.5) makes doing this well structurally harder for them.
Buyer inertia and switching cost. Organizations with years of investment in existing test suites (Selenium scripts, Postman collections) face real migration cost to move to Nexus's flow language, regardless of the long-run maintenance savings case. The roadmap (Part III) needs an explicit migration/import story, not just a green-field authoring story.
The "unified platform" pitch has a credibility tax. Buyers have been burned before by tools that claimed to "do everything" and did each thing shallowly. Nexus's go-to-market and product design both need to actively counter this — the Module Parity Index success metric (Part I, Section 6.3) exists specifically so the product can credibly demonstrate, not just claim, that no module is a second-class citizen.



9. Competitor Analysis


9.1 Competitive landscape framing

Nexus does not have a single direct competitor because, per the Problem Statement (Section 7), no existing product occupies the exact intersection it targets: unified orchestration across API/Web/Mobile/Desktop-UI/Logs with an architected, cross-domain AI layer. Instead, Nexus competes against four distinct competitor archetypes, each of which is a strong incumbent in a narrower slice of the same problem space. This section analyzes each archetype rather than naming specific commercial products, since specific competitive claims about named products should be sourced and legally reviewed before publication — the structural analysis below is what should drive architecture and roadmap decisions regardless of which specific named vendors a sales team later benchmarks against.

9.2 Archetype A — Single-domain web/API test automation tools

Profile: Mature, well-adopted tools focused primarily on web UI automation, often with API testing bolted on as a secondary feature. Typically the incumbent in a QA team's toolchain, with years of accumulated test scripts as a switching-cost moat.

Strengths: Deep, polished capability within their primary domain (web UI). Large community, extensive documentation, mature CI/CD integrations. Often free or low-cost open-source cores with paid enterprise tiers.

Weaknesses relative to Nexus's thesis: Mobile, desktop, and log automation are either absent or clearly secondary/bolted-on, with different configuration paradigms than the core web capability. AI features, where present, are typically scoped to the web-automation domain only (e.g., visual-diff-based self-healing) and don't extend to logs or cross-domain flows. Orchestration across domains is not a first-class concept — these tools assume the test is the unit of work, not a flow that might span domains.

Competitive response to watch for: The most credible threat from this archetype is a well-funded incumbent adding API and log "modules" as product-line extensions. Nexus's defensibility against this specific move rests on the Module Parity Index (Part I, Section 6.3) — a bolted-on module that doesn't share the incumbent's original execution engine will structurally struggle to reach parity quickly, but this needs to be continuously verified, not assumed.

9.3 Archetype B — Mobile-specific test automation tools

Profile: Tools and frameworks built specifically around Appium or platform-native automation (XCUITest, UiAutomator2), often used by a mobile QA team that is organizationally separate from web QA.

Strengths: Deep platform-specific capability (gesture handling, device-farm integration, app-store-adjacent workflows) that a generalist platform will always need to work hard to match.

Weaknesses relative to Nexus's thesis: Almost never integrate meaningfully with web or API testing, reinforcing the organizational silo between mobile and web QA teams rather than bridging it. Log/observability integration is essentially absent from this archetype's product surface.

Competitive response to watch for: Mobile-specific tools are less likely to pursue horizontal expansion into web/API/logs than Archetype A, because their core technical investment (deep Appium/native automation expertise) doesn't directly transfer. This makes Archetype B a lower-urgency competitive threat to the unification thesis specifically, though still a strong incumbent to displace for the mobile module's feature depth.

9.4 Archetype C — RPA (robotic process automation) platforms

Profile: Enterprise RPA tools, typically bought by operations/business-process teams rather than engineering, focused on desktop and legacy-application automation, often via image-matching or OS-level automation similar in spirit to Nexus's UI module.

Strengths: Strong presence in regulated, legacy-heavy enterprises (financial services, insurance, healthcare back-office) where desktop automation is the dominant need. Often includes workflow/orchestration capability that is more mature than a typical QA tool's, since "orchestrating a business process" is RPA's core value proposition already.

Weaknesses relative to Nexus's thesis: Positioned and sold as a business-process-automation tool, not a software-testing/quality tool — different buyer, different budget, different success metrics (cost/labor savings vs. defect prevention). API and modern web automation capability is typically present but shallow compared to purpose-built testing tools. AI features tend to focus on process mining and document understanding rather than test generation or selector healing.

Competitive response to watch for: RPA vendors moving "down" into QA-flavored automation is plausible given their existing orchestration strength, but crosses a buyer-category boundary (ops vs. engineering) that is a genuine go-to-market obstacle for them, not just a technical one. Nexus's UI module (Volume 1) should study RPA orchestration patterns for the desktop-automation use case specifically, since this archetype has the most mature engineering in exactly that one area.

9.5 Archetype D — AI-native, narrow-domain testing startups

Profile: Newer entrants building AI-first products, usually focused tightly on one domain (most commonly web UI testing, sometimes API), that lead with natural-language test authoring and self-healing as the primary pitch rather than a secondary feature.

Strengths: No legacy architecture to defend, so AI capability is genuinely built into the core execution model rather than retrofitted (validating Nexus's own Volume 3 approach). Fast iteration speed, strong developer-experience focus, often better default authoring ergonomics than the older incumbents in Archetype A.

Weaknesses relative to Nexus's thesis: Narrow domain scope, same as Archetype A's core weakness — an AI-native web-testing tool is still just a web-testing tool with better AI, not a cross-domain orchestration platform. Being early-stage, these products often lack the enterprise trust posture (Section 6.4 in Part I: security, on-prem deployment, SOC2-track compliance) that a large-enterprise buyer requires at initial evaluation, which is a real Nexus differentiation opportunity if Business Objective 5 (Part I) is executed on schedule.

Competitive response to watch for: This is the archetype most likely to expand toward Nexus's exact target position (unified + AI-native) fastest, because their architecture already assumes AI as core rather than bolted-on — the main gap they need to close is domain breadth, which is a scope problem, not an architecture problem. This is the single most important competitor category to track post-launch.

9.6 Positioning summary
Archetype	Nexus's structural advantage	Nexus's structural gap to close
A — Web/API incumbents	True cross-domain orchestration from day one, not bolted on	Years of accumulated web-automation feature depth and ecosystem
B — Mobile specialists	Mobile is a peer module in one platform, not a silo	Deep native-mobile gesture/device-farm expertise
C — RPA platforms	Purpose-built for software QA/engineering buyers and workflows	Mature business-process orchestration patterns for pure desktop use cases
D — AI-native narrow tools	Cross-domain breadth + enterprise trust posture at GA	Their AI-first execution model may already rival Volume 3's design; must not assume an AI-capability lead by default

The clearest takeaway for the roadmap (Part III): Nexus's differentiated position is real but not permanent, and the archetype most capable of closing the gap fastest (D) is exactly the one whose architecture philosophy is closest to Nexus's own. Speed of execution on breadth (adding modules 2 through 6 credibly, not just module 1) matters as much as the AI capability itself.



10. User Personas
10.1 Purpose of this section

Five personas are defined below, each representing a distinct role that interacts with Nexus differently, has different success criteria, and — critically for Volume 2 (Frontend Architecture) — needs a different primary interface into the platform. Every functional requirement in Section 12 should be traceable to at least one persona's need; a requirement that serves no persona here should be questioned before being built.

10.2 Persona 1 — Priya, QA Automation Engineer

Role context: Mid-level to senior engineer on a dedicated QA/SDET team at a mid-to-large enterprise. Owns the automated regression suite for one or more product areas. Comfortable writing code (typically C#, JavaScript, or Python) but is not a backend platform engineer.

Current pain: Maintains separate Playwright and Postman-equivalent suites today. Spends what feels like (to her) a disproportionate fraction of every sprint fixing broken selectors and assertions rather than adding new coverage. Has no good way to express "this UI flow's result should also be visible in the API and show no errors in the log" without hand-rolling a script that calls three different tools' CLIs.

What she needs from Nexus: A flow language expressive enough to replace her existing suite without a capability regression. Confidence that self-healing selectors won't silently mask a real regression (transparency, per Product Philosophy 4.4). A CI integration story that's at least as good as what she has today (JUnit-style output, parallel execution, flaky-test quarantine).

Primary interface: The flow-authoring UI (Volume 2) day to day; the CLI for CI integration and local debugging. Rarely touches raw JSON/YAML directly except when reviewing AI-generated flows.

Success looks like: Fewer hours per sprint on maintenance, not necessarily more raw test count. She would consider Nexus successful even with the same number of automated checks if the time she spends keeping them green drops materially.

10.3 Persona 2 — Marcus, DevOps / Platform Engineer

Role context: Owns CI/CD pipeline infrastructure and, increasingly, observability tooling for the whole engineering organization. Not primarily a test author — his relationship to automation is "does the pipeline gate correctly and fast," not "did we write a good test."

Current pain: Each QA/mobile/RPA tool the various product teams use needs its own CI runner configuration, its own secrets management, its own report aggregation into whatever dashboard leadership looks at. He is the one who fields the "why did the pipeline say green when the app was actually broken" escalation, usually because two tools' reports disagreed and nobody reconciled them.

What he needs from Nexus: One CI plugin, one report format, one secrets/credentials model to administer instead of five. Fine-grained control over what blocks a deploy vs. what's advisory. Observability into the platform itself (is Nexus's own execution slow/flaky, independent of the systems under test) — this is why Section 6.2's flake-rate metric in Part I is a platform metric, not just a customer-facing one.

Primary interface: The CLI and the REST API almost exclusively; the GUI only occasionally, for setup or investigating an unusual pipeline failure. He is the primary reason Volume 1's "API and CLI as primary interface, GUI as a client of that API" mission commitment exists.

Success looks like: Fewer distinct CI integrations to maintain, and a pipeline gate he trusts enough to not manually double-check.

10.4 Persona 3 — Sofia, Engineering Manager / QA Lead

Role context: Manages Priya's team (and possibly a separate mobile QA team). Reports automation coverage and quality trends upward to a VP of Engineering. Rarely writes automation herself anymore but reviews reports, sets priorities, and owns the tooling budget decision.

Current pain: Cannot get a single, trustworthy answer to "what's our automated coverage and how healthy is it" without manually synthesizing multiple tools' dashboards ahead of every leadership review. Struggles to make the business case for automation investment because the ROI story (time saved, defects caught) is scattered across disconnected reporting.

What she needs from Nexus: A single, credible cross-module reporting view (this is the direct product requirement behind the "unified signal for system health" sub-problem in Section 7.4). Trend data over time, not just the latest run's pass/fail. Enough confidence in the platform's security and reliability posture to defend the purchasing decision to her own leadership and to procurement/security review.

Primary interface: The reporting/dashboard views in the GUI almost exclusively. She is the persona most likely to never touch the flow-authoring surface or the CLI at all.

Success looks like: A leadership review she can walk into without a week of manual report-assembly beforehand, and a defensible year-over-year story of reduced maintenance burden and/or increased coverage.

10.5 Persona 4 — Raj, Backend/Platform Engineer building a custom module

Role context: Not a QA person at all — a platform engineer at an organization with an unusual automation need (e.g., automating a proprietary message-queue system, or a domain-specific hardware-in-the-loop test rig) that none of Nexus's six built-in modules cover.

Current pain: Every existing automation tool he's evaluated is a closed system; extending it to a novel domain means either abandoning the tool for that use case or hacking around its plugin model (where one exists at all) with unsupported workarounds.

What he needs from Nexus: A genuinely well-documented, genuinely small AutomationModule interface (Volume 1) that he can implement without needing deep familiarity with Nexus's internals. Confidence that his custom module gets the same orchestration, reporting, and flow-language treatment as the built-in six — this persona is the direct human referent for the Module Parity Index metric and the "plugin development time" success metric in Part I.

Primary interface: Source code and SDK documentation directly; the CLI to test his module locally; rarely the GUI except to verify his module's steps show up correctly in the flow-authoring surface.

Success looks like: Shipping a working custom module in less time than he initially budgeted, and it continuing to work without special-case maintenance after a Nexus core upgrade.

10.6 Persona 5 — Elena, Security / Compliance Reviewer

Role context: Not a hands-on user of Nexus at all in the day-to-day sense — she is the gatekeeper who must approve Nexus for use inside a regulated or security-conscious organization before Priya, Marcus, or Sofia are allowed to adopt it.

Current pain: Evaluating five separate point tools for security posture, each with its own compliance story, is a recurring, expensive process every time the organization adds or renews an automation tool. AI-enabled tools specifically raise new questions (does test data get sent to a third-party LLM provider, is that covered by existing data-processing agreements) that she has to evaluate fresh for each new AI feature that shows up in any of the point tools.

What she needs from Nexus: A clear, centralized security and data-handling story covering all six modules and the AI layer at once, rather than six separate stories. Explicit, auditable confirmation that AI features are optional and that the platform is fully functional with zero data leaving the organization's environment if AI is disabled — this is the direct human referent for the AI-optionality compliance metric in Part I, Section 6.3, and for Business Objective 5.

Primary interface: Documentation (security whitepaper, architecture diagrams, SOC2/compliance artifacts) and, for self-hosted deployments, the infrastructure configuration itself (Volume 4). She will very rarely, if ever, open the product's GUI.

Success looks like: A single review process that covers the whole platform, completed once, rather than repeated per module or per AI feature added over time.

10.7 Persona-to-interface summary
Persona	Primary interface	Volume most responsible for serving them
Priya (QA Engineer)	Flow-authoring GUI, CLI	Volume 2 (Frontend), Volume 1 (flow schema)
Marcus (DevOps/Platform)	CLI, REST API	Volume 1 (API design), Volume 4 (CI/CD integration)
Sofia (Eng Manager/QA Lead)	Reporting/dashboard GUI	Volume 2 (Frontend reporting views), Volume 1 (reporting pipeline)
Raj (Module builder)	SDK, source, docs	Volume 1 (plugin architecture), Volume 5 (contribution standards)
Elena (Security/Compliance)	Documentation, infra config	Volume 3 (AI guardrails), Volume 4 (infrastructure/security)

This table is the traceability anchor for the rest of the document set: any volume that cannot point to which persona(s) a given design decision serves should treat that as a signal to revisit the decision.



11. Use Cases
11.1 Purpose and format

Each use case below follows a consistent structure: primary actor (from Section 10), trigger, preconditions, main flow, and the specific cross-module or platform capability it exercises. Use cases are deliberately written to expose requirements, not to be exhaustive user stories — Section 12 (Functional Requirements) converts the capabilities named here into testable requirement statements.

11.2 UC-1 — Cross-domain regression flow (Priya)

Trigger: A nightly CI pipeline run.

Preconditions: A flow exists that was authored to validate the checkout journey end-to-end.

Main flow:

The flow navigates the web UI through product selection and checkout (Web module).
On order confirmation, the UI step saves the generated order ID as a flow variable.
A subsequent API step calls the order-status endpoint using the saved order ID and asserts the response reflects "confirmed" status (API module, variable chaining from Section 4.1's composability principle).
A subsequent Logs step tails the application log for the same time window and asserts no ERROR-level entries reference that order ID (Logs module).
The orchestrator produces one unified report showing all three steps' results under one flow run.

Capability exercised: Cross-module variable passing; unified reporting; fail-fast step sequencing (Product Philosophy 4.2).

11.3 UC-2 — AI-generated flow from a plain-English requirement (Priya)

Trigger: Priya receives a new acceptance criterion from a product manager: "the password reset endpoint should reject reused passwords from the last 5."

Preconditions: Priya has access to the AI module with a configured LLM provider.

Main flow:

Priya types the acceptance criterion into the flow-generation interface.
The AI module proposes a flow: an API call attempting a password reset with a previously-used password, asserting a 4xx response with a specific error code.
Priya reviews the proposed flow, edits the expected error code (the AI guessed a plausible but incorrect one), and accepts it.
The flow is saved and immediately runnable; the edit Priya made is preserved and does not get silently reverted on a future AI suggestion (Product Philosophy 4.4 — AI proposes, deterministic code disposes).

Capability exercised: AI flow generation; mandatory human review step before persistence; graceful handling of AI output that's structurally valid but semantically imperfect.

11.4 UC-3 — Self-healing selector after a UI change (Priya, unattended)

Trigger: A nightly flow run fails because a developer renamed a CSS class, breaking a selector that previously targeted the "Submit" button.

Preconditions: Self-healing is enabled for this flow (opt-in, per AI-optionality).

Main flow:

The Web module step fails with "selector not found."
Before marking the step failed and halting the flow, the AI module is invoked with the broken selector, the step's stated intent ("click submit"), and the current page's DOM.
The AI module proposes a replacement selector with a confidence score.
If confidence exceeds a configured threshold, the flow proceeds using the healed selector and flags the run report with "selector auto-healed, human review recommended" rather than silently succeeding with no trace.
If confidence is below threshold, the flow fails normally and the proposed (low-confidence) selector is attached to the failure report as a suggestion for the human who investigates.

Capability exercised: Self-healing with a confidence threshold; transparent flagging of AI intervention even on success (never silent); graceful degradation to normal failure reporting.

11.5 UC-4 — Unified pipeline gate (Marcus)

Trigger: A pull request triggers CI.

Preconditions: The repository's CI configuration invokes the Nexus CLI as a required check.

Main flow:

The Nexus CLI runs a defined set of flows spanning API, Web, and Logs modules for the affected service.
Results are emitted in a CI-native format (e.g., JUnit XML) that the existing pipeline tooling already knows how to parse and gate on — no unified Nexus-specific dashboard is required for this use case to succeed, since Marcus's need is pipeline-native, not human-dashboard-native.
On failure, the CLI output includes enough structured detail (which module, which step, which assertion) that a developer can diagnose without opening a separate GUI.
On flake (a step that fails, then passes on an automatic retry within policy), the run is marked accordingly rather than silently reported as a clean pass — this data feeds the platform-level flake-rate metric (Part I, Section 6.2).

Capability exercised: CI-native reporting formats; structured failure detail sufficient for CLI-only diagnosis; flake detection and honest reporting.

11.6 UC-5 — Cross-module executive report (Sofia)

Trigger: Sofia is preparing a monthly quality review for her VP.

Preconditions: At least one full month of flow-run history exists across API, Web, Mobile, and Logs modules.

Main flow:

Sofia opens the reporting dashboard and selects a date range.
The dashboard shows pass rate trends per module, flake rate trends, and a maintenance-burden proxy metric (count and rate of self-healed selectors, as a leading indicator of UI churn) — all without her needing to export and manually merge data from separate tools.
Sofia drills into a specific regression (a sustained pass-rate drop in the Mobile module starting on a specific date) and the dashboard correlates it with a specific flow and a specific underlying failure signature.
Sofia exports a summary suitable for a leadership deck directly from the dashboard.

Capability exercised: Cross-module trend reporting; drill-down from aggregate to root cause; export for external consumption.

11.7 UC-6 — Custom module development (Raj)

Trigger: Raj's organization needs to automate interactions with a proprietary message-queue system that has no existing Nexus module.

Preconditions: Raj has read the AutomationModule interface documentation (Volume 1) and has development access to a Nexus instance.

Main flow:

Raj implements a new module conforming to the AutomationModule interface, exposing actions like publishMessage and assertMessageReceived.
He registers the module with a local orchestrator instance and writes a test flow using it.
The flow-authoring GUI (Volume 2), without any custom frontend work from Raj, automatically surfaces his new module's actions as available flow steps, because the GUI is driven by the same module-contract metadata the orchestrator uses (this is a specific, testable architectural requirement, not just a nice-to-have — it recurs in Volume 1 and Volume 2 as a shared contract).
Raj's module's step results appear in the same unified report format as the six built-in modules, with no special-casing required.

Capability exercised: Plugin architecture end-to-end, including automatic GUI surfacing and uniform reporting — the concrete proof-point for the Module Parity Index metric.

11.8 UC-7 — Security review and AI-disabled operation (Elena)

Trigger: Elena is reviewing Nexus for approval ahead of a regulated-industry customer's adoption.

Preconditions: A self-hosted Nexus instance is available for review, with AI features disabled at the configuration level.

Main flow:

Elena reviews the architecture documentation (Volume 4) confirming no outbound network calls occur to any LLM provider when the AI module is disabled.
She runs a representative flow (API + Web + Logs) against the AI-disabled instance and confirms full functionality — every non-AI capability works identically to the AI-enabled configuration, per the AI-optionality compliance metric.
She reviews the credential/secrets handling architecture (Volume 1, Volume 4) to confirm no plaintext secrets are stored or logged.
She approves Nexus for adoption with AI features left disabled pending a separate, later review specifically scoped to the AI module's data handling.

Capability exercised: Verifiable AI-optionality; security review completed without requiring simultaneous approval of the AI layer — decoupling the two approval decisions is itself a product requirement (Section 12).

11.9 Use-case-to-requirement traceability

These seven use cases are the direct source for the functional requirements in Section 12. Where a functional requirement cannot be traced back to at least one use case here, it should be treated as a hypothesis to validate with a design partner rather than a committed scope item for the initial release.




12. Functional Requirements


12.1 Requirement notation

Each requirement is identified as FR-<area>-<number>, stated as a single testable capability, and tagged with the use case(s) from Section 11 that motivate it and the persona(s) from Section 10 it serves. Priority is MoSCoW (Must/Should/Could/Won't for this release). This numbering scheme carries forward into Volume 1, where each FR should map to at least one concrete component or API contract.

12.2 Orchestration core requirements
ID	Requirement	Source use case(s)	Persona(s)	Priority
FR-ORCH-1	The system shall execute a flow defined as an ordered sequence of steps, each targeting exactly one registered module.	UC-1	Priya, Marcus	Must
FR-ORCH-2	The system shall support passing the output of any step as an input to any later step in the same flow via variable interpolation.	UC-1	Priya	Must
FR-ORCH-3	The system shall halt flow execution on the first failed step by default (fail-fast), with an explicit per-flow override to continue-on-failure for flows that require it.	UC-1, UC-4	Priya, Marcus	Must
FR-ORCH-4	The system shall support conditional step execution based on the evaluated truthiness of an expression referencing prior step results or flow variables.	UC-3	Priya	Should
FR-ORCH-5	The system shall support automatic retry of a failed step, with a configurable retry count and backoff, and shall distinguish and report "passed on retry" (flake) from "passed on first attempt" in the flow report.	UC-4	Marcus, Sofia	Must
FR-ORCH-6	The system shall allow a new capability domain to be added by implementing a documented module interface, without modifying the orchestrator core.	UC-6	Raj	Must
FR-ORCH-7	The system shall expose the set of available modules and, per module, the set of available actions and their parameter schemas, via an introspectable API.	UC-6	Raj, Priya	Must
12.3 Module-specific requirements (representative, not exhaustive)

Full per-module requirement catalogs belong in Volume 1's component specifications; the entries below are the cross-cutting representative set that establishes the pattern every module must follow.

ID	Requirement	Source use case(s)	Persona(s)	Priority
FR-API-1	The API module shall support GET, POST, PUT, PATCH, and DELETE requests with configurable headers, body, and timeout.	UC-1	Priya	Must
FR-API-2	The API module shall support declarative assertions on response status code and on arbitrary JSON body paths (equality, contains, existence).	UC-1, UC-2	Priya	Must
FR-WEB-1	The Web module shall support standard interaction actions (navigate, click, fill, hover, select, upload) against a real browser instance.	UC-1, UC-3	Priya	Must
FR-WEB-2	The Web module shall support visibility- and text-based assertions, including a "contains" mode distinct from exact match.	UC-1	Priya	Must
FR-MOB-1	The Mobile module shall support tap, type, and swipe gestures against a connected device or emulator session.	UC-5	Priya	Should
FR-LOG-1	The Logs module shall support declarative rule-based checks (pattern, severity, maximum allowed occurrence count) against a supplied log source.	UC-1	Priya	Must
FR-LOG-2	The Logs module shall support statistical anomaly detection over time-bucketed error rates, with a configurable sensitivity threshold.	UC-5	Sofia	Should
FR-AI-1	The AI module shall generate a structurally valid flow definition from a natural-language description, scoped to a specified subset of available modules.	UC-2	Priya	Must
FR-AI-2	The AI module shall propose a replacement selector for a failed Web-module step given the broken selector, the step's stated intent, and current page markup, along with a confidence score.	UC-3	Priya	Should
FR-AI-3	Every AI module output that is used to alter flow execution (e.g., a self-heal) shall be recorded in the flow report as AI-assisted, distinguishably from a step that required no AI intervention.	UC-3	Priya, Sofia, Elena	Must
12.4 Reporting and observability requirements
ID	Requirement	Source use case(s)	Persona(s)	Priority
FR-REP-1	The system shall produce a single unified report per flow run covering every module used in that flow, with a consistent pass/fail/skip status model across modules.	UC-1, UC-4	Priya, Marcus, Sofia	Must
FR-REP-2	The system shall support exporting flow reports in at least one CI-native format (e.g., JUnit XML) in addition to its native JSON/HTML formats.	UC-4	Marcus	Must
FR-REP-3	The system shall retain historical flow-run results sufficient to compute pass-rate and flake-rate trends over a rolling window of at least 90 days.	UC-5	Sofia	Must
FR-REP-4	The reporting dashboard shall support drill-down from an aggregate trend (e.g., module pass rate over time) to the specific flow run(s) and step(s) responsible for a change in that trend.	UC-5	Sofia	Should
12.5 Platform, security, and administration requirements
ID	Requirement	Source use case(s)	Persona(s)	Priority
FR-SEC-1	The system shall support disabling the AI module entirely via configuration, with all non-AI functionality (per Section 12.2–12.4) remaining fully operational when disabled.	UC-7	Elena	Must
FR-SEC-2	When the AI module is disabled, the system shall make zero outbound network calls to any LLM provider, verifiably (e.g., via network-policy enforcement in the self-hosted deployment, not just application-level configuration).	UC-7	Elena	Must
FR-SEC-3	The system shall never persist or log credential/secret values in plaintext, in flow definitions, in reports, or in application logs.	UC-7	Elena, Marcus	Must
FR-SEC-4	The system shall support role-based access control distinguishing at minimum: flow author, flow runner (CI service account), and administrator roles.	(cross-cutting, all use cases)	All	Must
FR-PLAT-1	The system shall expose all core functionality (flow CRUD, flow execution, report retrieval) via a documented REST API, with the GUI implemented strictly as a client of that API.	UC-4, UC-6	Marcus, Raj	Must
FR-PLAT-2	The system shall provide a CLI capable of executing a flow and producing a report without requiring the GUI or a running server process beyond what the CLI itself starts.	UC-4	Marcus	Must
12.6 Requirements explicitly out of scope for this release

Stated exclusions are as important as inclusions for a blueprint of this scope, since they prevent Volumes 1–5 from silently expanding scope during detailed design:

Full RPA-style business-process orchestration (approvals, human-in-the-loop workflow steps unrelated to software testing) — Nexus automates verification of systems, not general business processes. Revisit only if Archetype C competitive pressure (Section 9.4) makes this strategically necessary.
Native support for automation targets outside the six defined modules (e.g., database-level automation, message-queue automation) as built-in capability — these are explicitly the plugin architecture's job (FR-ORCH-6), not core scope, per UC-6.
A hosted, multi-tenant SaaS AI model that pools customer data for model improvement — conflicts directly with the AI-optionality and data-handling commitments in Section 12.5 and Part I's Product Philosophy; any future consideration of this requires an explicit, separate security and legal review, not a default assumption.




13. Non-functional Requirements
13.1 Purpose and relationship to Volume 4

Non-functional requirements (NFRs) here are stated at the product-blueprint level of precision — measurable targets and the rationale behind them. Volume 4 (Infrastructure Architecture) is responsible for the concrete engineering design (scaling architecture, deployment topology, monitoring stack) that satisfies each NFR below; this section defines the bar Volume 4 must clear, not how it clears it.

13.2 Performance
ID	Requirement	Rationale
NFR-PERF-1	Orchestrator step-dispatch overhead (time from one step completing to the next step's module receiving its run() call) shall not exceed 50ms at p95, excluding the module's own execution time.	The orchestrator must never be a meaningful contributor to flow duration — its job is to get out of the way.
NFR-PERF-2	The REST API shall respond to flow-definition CRUD operations within 200ms at p95 under nominal load.	Directly affects Priya's authoring-loop experience (Persona 3, Section 10.2) — a sluggish authoring UI actively discourages iteration.
NFR-PERF-3	The reporting dashboard shall render a 90-day trend view (FR-REP-3) in under 2 seconds at p95 for an account with up to 10,000 historical flow runs.	Directly affects Sofia's monthly-review workflow (UC-5) — a report that's too slow to explore interactively defeats its own purpose.
NFR-PERF-4	AI module calls (flow generation, self-healing) shall have a defined timeout (default 15 seconds) after which the system falls back to normal (non-AI) failure handling rather than blocking flow execution indefinitely.	AI latency is inherently variable and outside Nexus's direct control; the platform must degrade gracefully, consistent with AI-optionality as an architectural principle, not just a configuration toggle.
13.3 Scalability
ID	Requirement	Rationale
NFR-SCALE-1	The system shall support at least 500 concurrent flow executions per deployment without step-dispatch latency (NFR-PERF-1) degrading beyond its stated target.	Enterprise CI pipelines fan out heavily at peak (e.g., end-of-day merge trains); the platform must not become the bottleneck.
NFR-SCALE-2	The system's execution capacity shall scale horizontally by adding worker instances, without requiring changes to existing flow definitions or client integrations.	Directly informs Volume 4's choice of execution-worker architecture (queue-based dispatch, not a single monolithic executor).
NFR-SCALE-3	Historical flow-run data storage (supporting NFR-PERF-3 and FR-REP-3) shall scale to at least 10 million stored flow-run records per deployment without requiring an architecture change, only a capacity/infrastructure scale-up.	Sets the data-model and storage-engine bar for Volume 1's persistence design — this is a "design for it now, don't retrofit later" requirement.
13.4 Reliability and availability
ID	Requirement	Rationale
NFR-REL-1	The core orchestration and execution path (flow trigger through report generation) shall achieve 99.9% availability for self-hosted deployments following the reference architecture in Volume 4, excluding availability of the systems under test themselves.	This is a CI-blocking dependency for customers (UC-4); Nexus being down is equivalent to blocking every deployment across the organization, which is a materially higher-stakes failure mode than a typical SaaS tool being briefly unavailable.
NFR-REL-2	Flake rate attributable to the platform itself (not the system under test) shall be below 0.5% of step executions, measured continuously as a release-gating metric (this is the platform-health metric first introduced in Part I, Section 6.2, restated here as a formal NFR).	A platform whose own instability is indistinguishable from the target system's instability is not trustworthy as a quality gate.
NFR-REL-3	The system shall support graceful degradation such that a failure or unavailability of the AI module never causes failure of a flow that does not use an AI-dependent step.	Direct restatement of AI-optionality (Product Philosophy 4, Business Objective 5) as a reliability requirement, not just a security/compliance one.
13.5 Security
ID	Requirement	Rationale
NFR-SEC-1	All data in transit (client-to-server, server-to-module-target, server-to-LLM-provider when AI is enabled) shall be encrypted via TLS 1.2 or higher.	Baseline enterprise expectation; explicit here because Volume 4 must treat it as non-negotiable across every deployment topology, including self-hosted.
NFR-SEC-2	Secrets and credentials referenced by flows shall be stored using envelope encryption at rest, with the encryption key managed outside the primary application database (e.g., via a dedicated secrets manager or vault integration).	Directly satisfies FR-SEC-3 at the infrastructure level; Volume 4 (Vault/Secrets section) owns the concrete design.
NFR-SEC-3	The system shall maintain an immutable audit log of all flow-definition changes, execution triggers, and administrative actions (role changes, credential updates), retained for a minimum of 1 year.	Required for Elena's compliance review use case (UC-7) and for enterprise audit requirements generally; informs Volume 1's Audit section directly.
NFR-SEC-4	The self-hosted deployment option (required by Business Objective 5, Part I) shall have no mandatory outbound internet dependency for core (non-AI) functionality.	Regulated/air-gapped environments are a named target segment (Section 10.6, Elena persona); this is a hard constraint on Volume 4's architecture, not a nice-to-have.
13.6 Usability and accessibility
ID	Requirement	Rationale
NFR-UX-1	The flow-authoring GUI shall conform to WCAG 2.1 AA accessibility standards.	Enterprise procurement in many sectors (public sector, large regulated enterprises) requires accessibility conformance as a baseline purchasing condition, independent of any individual user's needs.
NFR-UX-2	A user with prior experience in at least one existing single-domain automation tool (per the competitor archetypes in Section 9) shall be able to author a basic single-module flow within 30 minutes of first exposure to Nexus's flow-authoring UI, without external training, measured via usability testing during the design-partner phase.	Directly supports the "time-to-first-passing-flow" success metric (Part I, Section 6.1) and is testable independent of engineering completion — this is a UX research deliverable, not just a code deliverable.
13.7 Maintainability and extensibility
ID	Requirement	Rationale
NFR-MAINT-1	A new capability module conforming to the documented AutomationModule interface shall require no changes to the orchestrator core, the persistence layer, or the reporting pipeline to function.	Direct restatement of FR-ORCH-6 as an architectural constraint rather than a feature requirement — this is what Volume 1's plugin architecture section must be designed to guarantee, not just support as a best effort.
NFR-MAINT-2	The codebase shall maintain automated test coverage sufficient to catch regressions in the orchestration core and all built-in modules before release, as a formal CI quality gate (elaborated fully in Volume 5).	Nexus is a testing platform; a testing platform with an inadequately tested core is a direct credibility risk (see the "credibility tax" market risk in Section 8.6).
13.8 Compliance and data residency
ID	Requirement	Rationale
NFR-COMP-1	The self-hosted deployment shall support full data residency within a customer-controlled environment, with no data (including flow definitions, run history, or captured screenshots/logs) transmitted outside that environment unless the AI module is explicitly enabled and configured to do so.	Baseline requirement for the regulated-industry segment this blueprint explicitly targets (Business Objective 5, Persona Elena).
NFR-COMP-2	The architecture shall be designed to support a SOC 2 Type II audit without requiring fundamental redesign, even if the audit itself is not completed by initial GA.	"Architected for it, not retrofitted" — consistent with the explicit Business Objective 5 commitment in Part I that trust posture must be true at GA, not promised as a roadmap item.


13.9 Traceability note
Every NFR in this section should be revisited explicitly in Volume 4's own executive summary, with a stated design decision showing how each is satisfied. An NFR that Volume 4 cannot point to a concrete architectural answer for should be escalated back to this blueprint as a gap, not silently dropped during detailed design.
_____________________________________________________________________________________________________________________________________________________________


14. Platform Modules
14.1 Purpose of this section

Part II established why Nexus needs to exist and what capabilities it must have (Section 12's functional requirements). This section formalizes how the product is shaped around those capabilities — the module boundaries, what belongs inside each module versus in the orchestration core, and each module's maturity trajectory. This is the direct bridge between the six-module concept discussed at the outset of this project and the concrete component specifications Volume 1 will define. Nothing in this section should introduce a new capability that isn't already traceable to a Part II functional requirement; this section's job is organization and boundary-drawing, not new scope.

14.2 The seventh module: the orchestration core is not a module, it's the platform

Before describing the six capability modules, one structural point has to be stated precisely, because it is the single most important architectural distinction in the entire product: the orchestration core is not "module zero." It is the platform. The six capability modules — API, Web, Mobile, Desktop UI, Logs, AI — are all peers, all optional (a deployment could theoretically run with only one enabled), and all conform to the same AutomationModule interface introduced conceptually in Section 11.7 (UC-6) and formalized in Volume 1. The orchestration core is the only piece of the system that is never optional and never pluggable. This distinction directly enforces Product Philosophy 4.1 (Composability over completeness, Part I) and is what FR-ORCH-6 and NFR-MAINT-1 (Part II) exist to guarantee architecturally, not just aspirationally.

14.3 Module boundary principle

Every module's scope is defined by one governing question: "does this action require a specialized automation backend (a browser, a device, a screen, a log source, an LLM), or does it belong to flow logic that should be backend-agnostic?" Anything backend-agnostic (variable interpolation, conditional branching, retries) lives in the orchestration core and is available identically to every module. Anything backend-specific lives inside exactly one module. This principle is what prevents scope creep in both directions — modules staying thin and backend-focused, and the core staying free of any single backend's assumptions.

14.4 Module 1 — API

Scope: HTTP/REST (GA), with GraphQL and gRPC as later-phase extensions (Section 15). Owns request construction, response parsing, and declarative assertion evaluation against status/headers/body.

Explicitly not in scope for this module: Performance/load testing at scale (concurrent user simulation) is a distinct capability with different infrastructure needs; it may become an eighth module or a specialized mode of this module in a future phase, but is out of scope per Section 12.6's exclusions until a specific business case justifies it.

Maturity target at GA: Full (this is, per the roadmap in Section 15, the first module to reach production-grade maturity, since it has zero external environment dependencies — no browser, no device, no display — making it the fastest path to a reliable, demoable core).

14.5 Module 2 — Web

Scope: Browser-based UI automation via a modern multi-browser automation protocol (Playwright, per the Volume 1 technology decision). Owns navigation, element interaction, visual/text assertions, and network interception.

Explicitly not in scope for this module: Visual regression testing (pixel-level screenshot diffing across builds) is a related but distinct capability with its own infrastructure needs (baseline image storage, diff-tolerance tuning); it is a Should-priority extension of this module for a later phase, not a GA requirement.

Maturity target at GA: Full, immediately following API module maturity — this is the second module to reach production grade because it is the highest-volume use case among the design-partner personas (Priya, Section 10.2) and the one most existing competitors (Archetype A, Section 9.2) will be directly compared against.

14.6 Module 3 — Mobile

Scope: iOS and Android automation via Appium/WebdriverIO. Owns gesture actions, app lifecycle management, and device/emulator session handling.

Explicitly not in scope for this module: App-store submission automation, push-notification delivery testing infrastructure, and physical device-farm provisioning are all adjacent but distinct capabilities; Nexus's Mobile module automates against a device or emulator that already exists (self-provisioned or via a third-party device-farm integration), it does not provision the device farm itself.

Maturity target at GA: Partial — mobile automation has meaningfully higher environment-setup complexity than Web (Section 9.3 notes this is exactly why mobile specialists exist as a separate competitor archetype), so this blueprint targets a Should-priority, design-partner-validated release for Mobile rather than a Must-priority GA commitment. This is a deliberate, explicit scoping decision, not an oversight — overcommitting to Mobile parity at GA risks the credibility-tax risk named in Section 8.6.

14.7 Module 4 — Desktop UI

Scope: OS-level mouse/keyboard/screen automation via image-based and coordinate-based interaction (nut.js, per Volume 1). Owns the "last resort" automation layer for applications with no accessibility tree or DOM to hook into.

Explicitly not in scope for this module: General-purpose RPA business-process orchestration (Section 12.6 exclusion) — this module automates verification of a desktop application's behavior for quality-engineering purposes, not end-to-end business-process execution for operational cost savings. That distinction is what keeps Nexus positioned as a testing/quality platform (Archetype C analysis, Section 9.4) rather than drifting into RPA's buyer category.

Maturity target at GA: Minimal/Preview — this is explicitly the lowest-priority module for initial release. It exists in the platform's architecture (module contract, orchestrator support) from day one to prove the plugin model's genericity (Section 14.2), but a fully-featured Desktop UI module is a post-GA roadmap item.

14.8 Module 5 — Logs

Scope: Log ingestion (file, stream, common log-platform integrations), rule-based pattern gating, and statistical anomaly detection, per FR-LOG-1 and FR-LOG-2 (Part II).

Explicitly not in scope for this module: Full observability-platform capability (long-term log storage and search at scale, distributed tracing, metrics dashboards) — Nexus's Logs module is an automation and gating layer that can read from an organization's existing observability stack (Datadog, ELK, CloudWatch), not a replacement for that stack. This boundary is important for the competitive positioning in Section 8.2 — Nexus consolidates automation tooling, it does not attempt to also become an observability vendor.

Maturity target at GA: Full — like API, this module has comparatively low environment-setup complexity (reading text, not driving a browser/device) and directly serves the cross-domain flow use case (UC-1) that is the product's core differentiation thesis, so it is prioritized alongside API and Web.

14.9 Module 6 — AI

Scope: Flow generation from natural language, selector self-healing, log triage/summarization — the specific capabilities in FR-AI-1 through FR-AI-3 (Part II). Full strategic treatment is in Section 18 of this Part, with deep architecture in Volume 3.

Explicitly not in scope for this module as currently defined: Autonomous, unsupervised flow execution or modification (the AI module never runs a flow or persists a change without a human-reviewed step, per Product Philosophy 4.4 and FR-AI-3) — this boundary is a permanent product commitment, not a temporary GA limitation to be relaxed later without a deliberate, separately-reviewed decision.

Maturity target at GA: Partial, capability-by-capability — flow generation and log triage (lower-risk, higher-confidence capabilities) target Must-priority at GA; selector self-healing (higher-risk, since it can mask real regressions if the confidence-threshold design in UC-3 is wrong) targets Should-priority with the option-to-disable requirement (FR-SEC-1) treated as non-negotiable regardless of maturity level.

14.10 Module maturity summary at GA
Module	GA maturity target	Primary rationale
API	Full	Lowest environment complexity, fastest path to a reliable core
Logs	Full	Low environment complexity, core to the cross-domain differentiation thesis
Web	Full	Highest-volume persona need, most directly competed-on capability
AI	Partial (capability-dependent)	Strategic differentiator, but risk-scoped by capability per Product Philosophy 4.4
Mobile	Partial	Higher environment complexity; validated with design partners before full commitment
Desktop UI	Minimal/Preview	Proves plugin genericity; full build-out is explicitly post-GA

This maturity ordering is the direct input to the Product Roadmap (Section 15) and should be revisited at each roadmap milestone rather than treated as fixed once written.



15. Product Roadmap
15.1 Roadmap philosophy

This roadmap is phased around the module maturity ordering established in Section 14.10, not around a calendar. Each phase heading below includes an indicative sequencing relationship to the prior phase, but this blueprint deliberately does not assign fixed dates — dates belong in a living planning document (informed by the design-partner validation the Market Analysis in Section 8.4 flagged as required), not in an architecture blueprint that other volumes will reference for years. What is fixed here is sequencing logic and the specific exit criteria for each phase, expressed in terms of the success metrics from Part I, Section 6, and the functional requirements from Part II, Section 12.

15.2 Phase 0 — Foundation

Goal: Prove the orchestration core and plugin architecture with the minimum viable module set, before investing in module breadth or polish.

Scope:

Orchestration core: flow execution, variable interpolation, conditional steps, retry/flake handling (FR-ORCH-1 through FR-ORCH-5)
Plugin architecture and module contract (FR-ORCH-6, FR-ORCH-7), validated by actually building two structurally different modules against it (API and Logs — chosen specifically because they are maximally different backends, which is a stronger validation of genericity than building two similar modules)
API module and Logs module at Full maturity (Section 14.10)
CLI and REST API (FR-PLAT-1, FR-PLAT-2) — no GUI yet
Basic unified reporting (FR-REP-1, FR-REP-2) in JSON and JUnit XML formats

Exit criteria: UC-1 (cross-domain regression flow) and UC-4 (unified pipeline gate) both fully demonstrable end-to-end using only the API and Logs modules, via CLI, with a real CI pipeline integration proven against at least one design-partner's actual pipeline — not just an internal demo environment.

Explicitly deferred: Any GUI work at all. Phase 0 validates the engine; Persona Priya's authoring-UI needs (Section 10.2) are addressed starting Phase 1. This ordering is a direct application of the Mission commitment (Part I, Section 3) to build the orchestration core before any module is polished.

15.3 Phase 1 — Web module and initial GUI

Goal: Bring the highest-volume persona need (Web automation) to Full maturity, and introduce the flow-authoring GUI as a client of the Phase 0 REST API.

Scope:

Web module at Full maturity (FR-WEB-1, FR-WEB-2)
Flow-authoring GUI (Volume 2 scope begins here), consuming the introspectable module-action API (FR-ORCH-7) so that Web-module actions appear automatically, proving the GUI genuinely is a thin client rather than hardcoded to specific modules
Reporting dashboard, initial version (FR-REP-3, FR-REP-4) sufficient for Persona Sofia's UC-5 workflow
RBAC foundation (FR-SEC-4)

Exit criteria: UC-2's authoring workflow (minus AI generation, which is Phase 2) and UC-5's reporting workflow both fully demonstrable via GUI, not just CLI. Time-to-first-passing-flow success metric (Part I, Section 6.1) measured for the first time against real design-partner users, per NFR-UX-2's usability-testing requirement.

15.4 Phase 2 — AI layer (flow generation and log triage)

Goal: Introduce the AI module's lower-risk capabilities, proving the "AI proposes, deterministic code disposes" philosophy (Product Philosophy 4.4) in production before attempting the higher-risk self-healing capability.

Scope:

AI-assisted flow generation (FR-AI-1), scoped initially to the API and Web modules only (the two Full-maturity modules from Phases 0–1)
AI-assisted log triage, per the Logs module's AI-adjacent capability
AI-optionality infrastructure (FR-SEC-1, FR-SEC-2, NFR-REL-3) built and verified from the start of this phase, not retrofitted after — this is a direct, deliberate sequencing decision given how central AI-optionality is to Business Objective 5 and Persona Elena's adoption path
Transparent AI-attribution in reports (FR-AI-3)

Exit criteria: UC-2 fully demonstrable end-to-end including the AI generation step. UC-7 (security review with AI disabled) demonstrable against this same release, proving optionality was built in, not bolted on. AI module cost and latency behavior (NFR-PERF-4) validated under realistic design-partner usage volume.

15.5 Phase 3 — Mobile module and self-healing

Goal: Extend module breadth (Mobile) and AI capability depth (self-healing) in parallel, since they serve different personas and carry independent risk profiles.

Scope:

Mobile module to Partial maturity (Section 14.6), validated with design partners who have an active mobile QA need
Selector self-healing (FR-AI-2), with the confidence-threshold mechanism from UC-3 built and tuned against real Web-module usage data accumulated since Phase 1
Cross-module flows extended to include Mobile (extending UC-1's pattern)

Exit criteria: UC-3 (self-healing) fully demonstrable with the confidence-threshold and transparent-flagging behavior verified against real broken-selector incidents from design-partner usage, not synthetic test cases only. Module Parity Index (Part I, Section 6.3) computed for the first time across four modules (API, Web, Logs, Mobile) and tracked as an ongoing release metric from this phase forward.

15.6 Phase 4 — Desktop UI module and plugin marketplace foundation

Goal: Complete the originally-scoped six-module set, and open the plugin architecture externally (Section 17) now that it has been proven internally across five different built-in modules.

Scope:

Desktop UI module built out from Minimal/Preview to a genuinely usable maturity level (exact target to be set based on Phase 0–3 learnings about how much of the plugin-architecture investment already covers this module "for free")
Public plugin SDK documentation and a reference third-party module (built by a design partner or internal team acting as an external developer, directly exercising UC-6's Raj persona)
Plugin marketplace infrastructure (Section 17) — listing, discovery, and initial certification process, though not yet a monetized marketplace

Exit criteria: A genuinely external party (not the core engineering team) successfully builds and ships a working custom module using only public documentation, within the plugin-development-time target set in Part I, Section 6.3.

15.7 Phase 5 — Enterprise trust hardening and marketplace monetization

Goal: Complete the enterprise trust posture that Business Objective 5 requires to be true "from the first GA release" — this phase's existence in the roadmap reflects that trust hardening is treated as a first-class, resourced initiative, not an assumed byproduct of the earlier phases.

Scope:

SOC 2 Type II audit process (NFR-COMP-2) completed, not just architecturally supported
Self-hosted deployment reference architecture (Volume 4) hardened and documented for regulated-industry adoption (Persona Elena's UC-7 taken to its full conclusion)
Plugin marketplace monetization model activated (Section 17), building on Phase 4's foundation
Licensing tiers (Section 16) fully implemented in product (license enforcement, tier-gated features) rather than only defined on paper

Exit criteria: At least one design partner in a regulated industry completes a full security/compliance review (UC-7) and adopts Nexus in production. First external, revenue-generating plugin marketplace transaction completed.

15.8 Roadmap-to-business-objective traceability
Phase	Primary business objective(s) served (Part I, Section 5)
0 — Foundation	Objective 2 (time-to-first-flow), foundation for all others
1 — Web + GUI	Objective 1 (consolidation), Objective 2
2 — AI (generation, triage)	Objective 3 (maintenance reduction), Objective 5 (trust, via optionality)
3 — Mobile + self-healing	Objective 1 (consolidation), Objective 3 (maintenance reduction)
4 — Desktop UI + marketplace foundation	Objective 4 (marketplace)
5 — Trust hardening + monetization	Objective 5 (enterprise trust), Objective 4 (marketplace revenue)

Every phase above should be re-validated against this table before detailed planning begins — a phase whose scope has drifted away from serving its stated objective is a signal to re-scope, not a signal to update the table after the fact.




16. Licensing Strategy
16.1 Strategic goals a licensing model must serve

A licensing strategy for Nexus has to satisfy four goals simultaneously, and the tension between them is the actual design problem this section resolves:

Drive bottom-up adoption the way the AI-native and open-source-rooted competitors (Archetype D, Section 9.5) do — a closed, sales-led-only model would concede exactly the adoption-speed advantage those competitors currently hold.
Preserve the enterprise trust and revenue case that Business Objective 5 and the RBAC/audit/compliance NFRs (Part II, Section 13.5) depend on — a purely open-source model with no commercial tier undermines the funding needed to build and maintain the enterprise trust posture at all.
Avoid the "open-core bait-and-switch" trust failure that has damaged adoption for other infrastructure products when core functionality is quietly moved behind a paywall after a community has invested in the open version — this is a direct extension of the "credibility tax" risk from Section 8.6, applied to licensing specifically rather than product breadth.
Keep the plugin/module architecture (Section 14.2) commercially neutral — a third-party module builder (Persona Raj, UC-6) should never be structurally disadvantaged by the licensing model relative to Nexus's own built-in modules, or the Plugin Marketplace Vision (Section 17) has no credible foundation.
16.2 Recommended model — open core with a clearly-drawn, permanent line

Nexus should ship as open core: the orchestration engine, the plugin/module contract, and a defined set of built-in modules are open source under a permissive license (MIT, consistent with the reference framework built during this project's early prototyping); enterprise capabilities are commercially licensed as a separate, additive layer.

What stays open source, permanently, as an explicit and published commitment (not a policy that can quietly change):

The orchestration core (execution engine, flow language, reporting pipeline)
The AutomationModule plugin interface/contract
The API and Logs modules at Full functional maturity (chosen specifically because these are the two Phase 0 modules, Section 15.2 — the ones that prove the platform's core thesis, and gatekeeping them commercially would undermine adoption at the exact moment a new user is evaluating whether the platform's core claims are real)
The CLI

What is commercially licensed, as the enterprise tier:

RBAC beyond a single-admin default (FR-SEC-4's full role model)
Audit logging and compliance reporting (NFR-SEC-3, NFR-COMP-1, NFR-COMP-2)
The reporting dashboard's cross-module trend and drill-down capability (FR-REP-3, FR-REP-4) beyond a basic single-run report — the open-source CLI/API always produces a full single-run report; the persistent historical analytics layer is the paid capability
Managed AI-provider integration and cost-governance tooling (Section 18) — self-hosted users can always bring their own LLM API key to the open-source AI module, but fleet-wide cost controls, provider routing, and guardrail policy management are enterprise features
SSO/enterprise identity integration
Official support SLAs and the self-hosted enterprise reference architecture hardening (Volume 4)

Why the Web and Mobile modules are a deliberate gray area rather than assigned outright: Unlike API/Logs (kept open to prove the core thesis) or the clearly-enterprise capabilities above, Web and Mobile module licensing should be finalized after Phase 1–3 (Section 15) usage data is available, weighing bottom-up adoption value against the fact that these are the modules most directly competitive with existing open-source tools (Archetype A/B, Section 9.2–9.3) that a purely open Web module would need to differentiate against on capability alone, not licensing. This is stated explicitly as an open decision, not resolved prematurely by this blueprint.

16.3 Why not fully open source

A fully open-source model with no commercial tier would fail Business Objective 5 (enterprise trust funded by sustainable revenue) and would leave no clear mechanism to fund the trust-hardening work in Roadmap Phase 5 (Section 15.7) — SOC 2 audits, dedicated security engineering, and enterprise support SLAs are not typically sustained by a purely community-funded model at the reliability bar Persona Elena's use case (UC-7) requires.

16.4 Why not fully closed / proprietary

A fully closed model forfeits the single biggest structural advantage available against the AI-native narrow competitors (Archetype D): they are, in several cases, more open and more bottom-up-adoptable than the legacy incumbents (Archetype A), and a closed Nexus would be competing on trust and polish alone against competitors who can win on both community trust and AI-native architecture. The market timing analysis (Section 8.5) explicitly notes that platform engineering teams are actively evaluating and often prefer tools they can inspect, self-host, and contribute to.

16.5 Pricing structure principles (detailed pricing deferred to a commercial planning document)

This blueprint does not set specific price points — that is a commercial decision requiring the market-sizing validation flagged in Section 8.4, not an architecture decision. It does establish the structural principles the eventual pricing model must follow:

Pricing scales with organizational usage (seats, or flow-execution volume, or both), not with module count. A customer should never be penalized commercially for adopting more of the "unified platform" value proposition (using five modules instead of two) — that would directly undermine Business Objective 1 (consolidation) and the cross-module flow adoption success metric (Part I, Section 6.1).
AI usage cost is transparent and separately metered from platform licensing, consistent with AI-optionality — a customer should be able to see and control AI-related cost independent of their base platform tier, never bundled opaquely.
The plugin marketplace's commercial terms (Section 17) are independent of the core platform's licensing tier — a customer on the open-source core should still be able to purchase and use commercial third-party plugins, and a plugin developer's revenue share should not depend on which tier the purchasing customer is on.
16.6 Governance of the open/closed boundary itself

Because Section 16.2's "permanently open" commitment is only credible if it is actually enforced over time, this blueprint recommends the boundary between open-source and commercial capability be governed by a published, versioned policy document (maintained alongside, but distinct from, this blueprint) that requires an explicit, public change-log entry for any capability that moves from open to commercial — never a silent change. This is the direct, concrete mechanism for avoiding the "bait-and-switch" trust failure named as Goal 3 in Section 16.1, and should be treated as a hard requirement on the eventual governance process, not just a stated intention.



17. Plugin Marketplace Vision
17.1 Why a marketplace, and why it is a Part III platform decision, not a Part IV afterthought

Business Objective 4 (Part I, Section 5) states the marketplace must be established "within 18 months of GA... as a second revenue and retention channel." This section exists in Part III, alongside the module architecture and licensing strategy, rather than being treated as a later go-to-market initiative, for one specific reason: the marketplace's viability is entirely determined by decisions this blueprint is making now — the plugin interface's genuine simplicity (FR-ORCH-6, NFR-MAINT-1), the licensing model's neutrality toward third-party modules (Section 16.1, Goal 4), and the Module Parity Index metric (Part I, Section 6.3) that makes "third-party modules are true peers" a verifiable claim rather than a marketing statement. A marketplace bolted onto a platform that wasn't architected for it fails; this section is where that architectural intent gets stated as product strategy.

17.2 What the marketplace distributes

Three distinct artifact types, each with a different trust and review bar:

Capability modules — new AutomationModule implementations extending Nexus to a domain none of the six built-in modules cover (database automation, message-queue automation, IoT device automation, and so on — Persona Raj's UC-6 is the direct referent). Highest trust bar, since these execute with the same orchestrator-level privileges as built-in modules.
Flow templates — pre-built, parameterizable flows for common scenarios within existing modules (e.g., "OAuth2 login flow template," "Stripe webhook verification template"). Lower trust bar since these compose existing, already-reviewed module actions rather than introducing new execution capability.
AI prompt/guardrail packs — domain-specific prompt libraries and guardrail configurations for the AI module (e.g., a healthcare-specific PHI-redaction guardrail pack, elaborated further in Volume 3). Distinct trust bar again, since these affect AI behavior and data handling specifically, and require review aligned with the AI Strategy's guardrail principles (Section 18.6).
17.3 The certification model

A three-tier certification model, directly modeled on the differentiated trust bars above, and directly serving Persona Elena's need (Section 10.6) for a review process that doesn't have to be reinvented per plugin:

Tier	Review bar	Who can publish	Marketplace treatment
Community	Automated scan only (dependency vulnerabilities, license compliance, interface-conformance test suite passes)	Anyone	Listed with a clear "community, not vetted" badge; installable but with an explicit friction step (confirmation dialog) before use in a flow
Verified	Automated scan plus a manual security and code-quality review by the Nexus team or an approved partner reviewer	Vetted developers/organizations	Listed with a "verified" badge; no extra friction to install
Enterprise-certified	Verified tier requirements plus a documented data-handling and compliance review (aligned with NFR-COMP-1/NFR-COMP-2's standards)	Established vendors/partners	Eligible for inclusion in enterprise-tier deployments where an organization's policy (Persona Elena's domain) restricts installation to this tier only

This tiering is also the direct mechanism for satisfying Elena's need in UC-7 at organizational scale: an enterprise administrator should be able to configure a policy restricting their organization's Nexus instance to only install Enterprise-certified (or Verified-and-above) plugins, enforced at the platform level, not just as a marketplace UI suggestion.

17.4 Marketplace mechanics
Discovery and installation happens through the same flow-authoring GUI that surfaces built-in module actions (Section 14.2's genericity principle extends directly to marketplace-installed modules — there should be no separate "marketplace module" UI paradigm distinct from how built-in modules appear).
Versioning and compatibility follows semantic versioning against the published AutomationModule interface version, with the orchestrator core enforcing compatibility checks at install time rather than allowing a broken plugin to fail silently at flow-execution time.
Sandboxing and resource limits apply to marketplace-installed modules by default (CPU/memory/network policy), with built-in modules held to the same limits where technically applicable — this is a direct extension of NFR-SEC-2/NFR-SEC-3's security posture to third-party code specifically, and should be treated as a hard requirement given the elevated risk of executing third-party code inside an enterprise's automation pipeline.
17.5 Commercial model

Per Licensing Strategy Section 16.5, marketplace commercial terms are independent of the purchasing customer's own platform tier. The proposed structure:

Free plugins (most Community-tier and many Verified-tier modules and templates) — no revenue share, since the value to Nexus is ecosystem breadth and the Module Parity Index proof-point, not direct monetization.
Paid plugins — a revenue share between the plugin developer and Nexus (specific percentage is a commercial decision deferred to a planning document, not fixed here), modeled on established marketplace precedents (app stores, IDE plugin marketplaces) rather than invented from scratch.
Enterprise-certified plugins may carry a support/SLA component priced separately from the plugin itself, reflecting the additional compliance review investment required to reach that tier.
17.6 Risks specific to the marketplace, and mitigations
Risk: a low-quality Community-tier module damages trust in the platform generally, even though the tiering system (Section 17.3) is designed to make quality expectations legible. Mitigation: the install-time friction step for Community-tier modules and clear badge differentiation are non-negotiable UI requirements, not optional polish — a user should never be able to mistake an unvetted community module for a Nexus-maintained one.
Risk: marketplace fragmentation undermines the "one platform" value proposition if too many overlapping, low-quality modules exist for the same use case with no clear guidance on which to use. Mitigation: deliberate curation (featured/recommended listings) balanced against not gatekeeping Community-tier publishing entirely — this tension should be revisited as an explicit design review once real marketplace usage data exists (tied to Roadmap Phase 4, Section 15.6).
Risk: security review capacity becomes a bottleneck as Verified/Enterprise-certified submissions grow, creating pressure to either slow down growth or cut review corners. Mitigation: the approved-partner-reviewer model in Section 17.3 is designed specifically to scale review capacity beyond the core Nexus team alone, and should be resourced as part of Roadmap Phase 5 (Section 15.7), not treated as solvable by the core team's headcount alone.
17.7 Success measurement

The marketplace's health should be tracked against Business Objective 4 using metrics parallel in spirit to Part I, Section 6: number of Verified-or-above modules available at each roadmap phase gate, percentage of active Nexus customers using at least one marketplace-sourced module or template, and marketplace revenue as a percentage of total platform revenue by the end of Roadmap Phase 5. These specific targets should be set once Phase 4's initial marketplace launch (Section 15.6) provides real baseline data, rather than being estimated in this blueprint without evidence — consistent with this document's general discipline (Section 8.4) of not laundering unvalidated guesses into false-precision targets.




18. AI Strategy
18.1 Scope of this section versus Volume 3

This section states the product and business strategy for AI within Nexus — positioning, differentiation, the principles that govern where AI is and is not applied, and the cost/provider strategy. It deliberately does not specify prompt engineering, RAG architecture, or model-routing implementation details — those belong to Volume 3 (AI Architecture) in full. Every principle stated here is a binding constraint that Volume 3's technical design must satisfy, in the same traceability relationship Section 13.9 established between this blueprint and Volume 4.

18.2 The strategic thesis, restated precisely

Section 7.5 (Problem Statement) identified that AI-assisted automation capability is arriving piecemeal, retrofitted into single-domain point tools, while Nexus's structural opportunity is to architect an AI layer once, across all modules, from the start. This is Nexus's single clearest source of differentiation against Archetype A/B/C competitors (Section 9) and its single greatest point of convergence with Archetype D — meaning the AI Strategy is not really competing on "having AI," which will be table stakes across the whole market within the roadmap's timeframe, but on AI that is genuinely cross-domain, genuinely optional, and genuinely transparent (Product Philosophy 4.4). Those three qualifiers are the actual strategy; "we have AI" is not a strategy.

18.3 Where AI creates value — the three-capability framework

Every current and future AI capability in Nexus should map to one of exactly three value mechanisms, a discipline that prevents the "AI feature sprawl" failure mode where capabilities get added because they're technically possible rather than because they serve a real cost or quality lever:

Authoring acceleration — reducing the time to go from intent to a working flow (flow generation, FR-AI-1). Value mechanism: directly serves the time-to-first-passing-flow success metric (Part I, Section 6.1).
Maintenance reduction — reducing the ongoing human effort to keep existing automation working as the target system changes (selector self-healing, FR-AI-2). Value mechanism: directly serves the maintenance-burden success metrics (Part I, Section 6.2), which Section 7.3's problem statement identified as the industry's most consistent automation-program failure point.
Diagnostic acceleration — reducing the time to understand why something failed (log triage, future failure-analysis/root-cause capabilities referenced in Volume 3's scope). Value mechanism: directly serves Persona Sofia's UC-5 reporting workflow and the mean-time-to-diagnose metric (Part I, Section 6.2).

A proposed AI capability that does not clearly map to one of these three mechanisms should be treated as a research exploration, not a roadmap commitment, until it can be mapped to a mechanism and a metric.

18.4 Where AI does not go — permanent boundaries

Three boundaries are stated as permanent product commitments, not phase-1 limitations to be relaxed later without a deliberate, explicit, separately-reviewed decision (this qualification, consistent with Section 14.9's module-scope statement, exists specifically so a future team cannot silently cross these lines under roadmap pressure):

AI never executes or persists a change autonomously. Every AI output that affects flow definitions or flow execution passes through a human-reviewable step (UC-2, UC-3) or, at minimum, a transparent, reversible flag in the report (FR-AI-3). This is Product Philosophy 4.4 made into an unconditional product boundary.
AI is never a required dependency for core platform functionality. Restated here from NFR-REL-3 and FR-SEC-1 as a strategic, not just technical, commitment — this boundary is what makes the regulated-industry market segment (Persona Elena) addressable at all, and abandoning it would materially shrink the addressable market described in Section 8, not just create an engineering inconvenience.
AI does not become a data-pooling mechanism across customers. Explicitly excluded already in Part II, Section 12.6; restated here because it is as much a strategic commitment (trust posture is a stated competitive differentiator, Section 9.6) as a technical or legal one.
18.5 Model provider strategy
Provider abstraction, not lock-in. Volume 3 must design the AI module against a provider-agnostic interface, even though a specific default provider will be used for the reference implementation and hosted offering. This serves two goals at once: enterprise customers with existing model-provider relationships or compliance constraints can substitute their preferred provider, and Nexus is not strategically exposed to a single provider's pricing or availability changes.
Self-hosted/open-weight model support is a Should-priority, not Must-priority, capability for GA, but the provider-abstraction requirement above should be designed so that adding this support later is a configuration change, not an architecture change — directly consistent with NFR-MAINT-1's "extend without core changes" principle applied to the AI module specifically.
Cost governance is a first-class product capability, not an operational afterthought, per the enterprise-tier scoping in Licensing Strategy Section 16.2 (managed AI-provider integration and cost-governance tooling). This reflects that AI API cost is a genuinely variable, usage-scaling cost in a way that the rest of the platform's cost structure is not, and customers will require visibility and control over it as a condition of adoption at scale.
18.6 Guardrails as product strategy

Guardrails (the specific technical mechanisms are Volume 3's scope: prompt-injection resistance, output validation, PII/sensitive-data handling in prompts) are treated here as a strategic differentiator specifically because Section 8.3 identified that AI-assisted features are being retrofitted into point tools without a unified architecture — a unified guardrail strategy across all six modules' AI touchpoints is something a retrofitted competitor structurally cannot easily replicate. The AI prompt/guardrail packs described in the Plugin Marketplace Vision (Section 17.2) are the direct product mechanism for extending this guardrail strategy into domain-specific and regulated-industry contexts without the core Nexus team needing to anticipate every compliance regime up front.

18.7 Evaluation and quality strategy

AI capability quality is tracked as a first-class, ongoing product concern, not a one-time launch validation:

Self-heal acceptance rate (Part I, Section 6.2) is the primary ongoing quality signal for the self-healing capability specifically, and should gate the confidence-threshold tuning referenced in Roadmap Phase 3 (Section 15.5).
Flow-generation structural validity rate (the percentage of AI-generated flows that parse as valid flow definitions without requiring a retry, referenced in the AI module's technical design in Volume 3) is the primary quality signal for authoring acceleration.
Both metrics should be tracked per model/provider configuration, not just in aggregate, so that the provider-abstraction strategy (Section 18.5) is backed by real comparative data when a customer or the platform team considers switching providers.
18.8 Competitive AI positioning summary

Restating Section 9.6's competitive table through the specific lens of AI strategy: Nexus's AI differentiation is not raw model capability (which is substantially a function of the underlying provider, not Nexus's own engineering, and is not a durable moat by itself) but the combination of cross-domain breadth, architected optionality, and unified guardrails — three properties that require the platform-level architectural commitments made throughout this Part III to exist prior to any specific AI feature being built. This is why the AI Strategy is positioned in Volume 0 as a peer to the Platform Modules and Licensing Strategy sections, rather than as a bolt-on feature list: the strategy's credibility depends entirely on the platform decisions around it, not on the AI capabilities in isolation.

_____________________________________________________________________________________________________________________________________________________________



19. High-Level Architecture
19.1 Purpose of this section

Part III formalized the product's shape (modules, roadmap, licensing, AI strategy). This section takes the first step from product shape to system shape — a high-level view of the major components and how they relate, precise enough to hand to Volume 1 as a starting contract, but deliberately not so detailed that it pre-empts Volume 1's own component-, class-, and sequence-level design work. Everything below should be read as "the shape Volume 1 must be consistent with," not "the design Volume 1 should copy without further engineering judgment."

19.2 The five-layer model

Nexus's system architecture is organized into five layers, each with a single, clearly-bounded responsibility. This layering is the architectural expression of the module-boundary principle established in Section 14.3, applied to the whole system rather than just the module layer.

Client layer — the GUI (React, per the locked technology decision) and the CLI. Both are, without exception, clients of the API layer below; neither contains business logic that the API layer doesn't also enforce independently (this is FR-PLAT-1's "GUI as a client of the API" requirement, restated as an architectural layering rule, not just an interface-design preference).
API layer — the REST API (ASP.NET Core, per the locked .NET/C# stack) that exposes flow CRUD, execution triggering, report retrieval, and administration. Owns authentication, authorization (FR-SEC-4's RBAC), and input validation. Contains no direct knowledge of how a specific module executes a step — it delegates to the orchestration layer.
Orchestration layer — the execution engine described conceptually in Section 14.2: flow parsing, variable interpolation, conditional evaluation, retry/flake handling, and dispatch to the correct module. This layer is the direct architectural home of FR-ORCH-1 through FR-ORCH-7.
Module layer — the six capability modules (Section 14.4–14.9) plus any marketplace-installed third-party modules (Section 17), each implementing the common AutomationModule contract. This layer is intentionally the most "pluggable" layer in the system — the orchestration layer above it and the persistence layer below it should both be able to treat every module, built-in or third-party, identically.
Persistence and integration layer — flow definitions, execution history, audit logs (relational store), plus integration points outward: the LLM provider (AI module only, optional per NFR-REL-3), external log sources (Logs module), browser/device/OS automation backends (Web/Mobile/Desktop UI modules), and target systems under test (all modules, by definition).
19.3 Component diagram (C4 Container level)
                          ┌─────────────────────────────┐
                          │        Client Layer          │
                          │  ┌──────────┐  ┌──────────┐  │
                          │  │   GUI    │  │   CLI    │  │
                          │  │ (React)  │  │ (.NET)   │  │
                          │  └────┬─────┘  └────┬─────┘  │
                          └───────┼─────────────┼────────┘
                                  │   REST/HTTPS │
                          ┌───────▼─────────────▼────────┐
                          │          API Layer            │
                          │   ASP.NET Core Web API         │
                          │   AuthN / AuthZ / Validation   │
                          └───────────────┬────────────────┘
                                          │
                          ┌───────────────▼────────────────┐
                          │      Orchestration Layer         │
                          │  Flow Engine · Variable Store     │
                          │  Retry/Flake Handler · Dispatcher │
                          └───────────────┬────────────────┘
                                          │  AutomationModule contract
              ┌────────────┬─────────────┼─────────────┬────────────┬────────────┐
              ▼            ▼             ▼             ▼            ▼            ▼
          ┌───────┐   ┌───────┐     ┌───────┐     ┌───────┐   ┌───────┐   ┌───────────┐
          │  API  │   │  Web  │     │Mobile │     │Desktop│   │ Logs  │   │    AI     │
          │Module │   │Module │     │Module │     │  UI   │   │Module │   │  Module   │
          └───┬───┘   └───┬───┘     └───┬───┘     └───┬───┘   └───┬───┘   └─────┬─────┘
              │           │             │             │           │             │
              ▼           ▼             ▼             ▼           ▼             ▼
        [Target APIs] [Browsers]   [Devices/   [OS/Screen]  [Log       [LLM Provider,
                       (Playwright) Emulators]  (nut.js)     Sources]   optional]
                                    (Appium)

                          ┌────────────────────────────────┐
                          │   Persistence & Integration      │
                          │   PostgreSQL · Secrets Vault      │
                          │   Audit Log · Report Store        │
                          └────────────────────────────────┘

This diagram is intentionally simplified relative to a true C4 Container diagram (Volume 1 will produce the formal version with deployment cardinality, protocols, and data-flow direction fully annotated); its purpose here is to fix the layer boundaries and module symmetry before Volume 1's detailed design begins.

19.4 Cross-cutting architectural concerns

Four concerns don't belong to any single layer above and are called out explicitly because Volume 1 must design them as horizontal capabilities, not features of any one layer:

Observability of the platform itself (NFR-REL-2's flake-rate requirement, and general operational monitoring) — instrumented at the orchestration and module layers, aggregated and exposed via the infrastructure stack Volume 4 defines (OpenTelemetry, per the technology decisions in Section 21).
Security boundary enforcement — authentication and coarse-grained authorization live in the API layer (19.2, layer 2), but fine-grained execution-time enforcement (e.g., a module respecting a credential-access policy) must be enforced again at the module layer, not assumed to be fully handled upstream. This defense-in-depth posture is a direct response to the elevated risk of executing third-party plugin code (Section 17.4).
AI optionality — architecturally, this means the orchestration layer must be able to route around the AI module cleanly (a flow with no AI-dependent steps must have zero code-path dependency on the AI module being reachable), which is why AI-optionality is listed here as a cross-cutting concern rather than "module 6's problem alone."
Extensibility of the module layer — the AutomationModule contract (Section 19.2, layer 4) is the single most important interface in the whole system, since NFR-MAINT-1 requires new modules to require zero changes to layers 2, 3, or 5. Volume 1 should treat this contract's stability as equivalent in importance to a public API contract, versioned and change-managed accordingly.
19.5 Deployment topology (high-level)

Two supported deployment models, both required by Business Objective 5 and NFR-SEC-4:

Managed/hosted: Nexus-operated infrastructure, multi-tenant at the infrastructure level with strict tenant data isolation (detailed in Volume 4), offered for customers who prefer not to self-host.
Self-hosted: the full five-layer stack deployable inside a customer-controlled environment (Kubernetes/Helm, per Section 21's technology decisions), with the AI module optionally and verifiably disabled (FR-SEC-2) and zero mandatory outbound dependency for non-AI functionality (NFR-SEC-4).

Both deployment models run the identical codebase and layer architecture described in Section 19.2 — there is no separate "enterprise architecture" and "hosted architecture." Licensing tier (Section 16) gates feature availability, not architecture; this is a deliberate simplification that keeps the engineering team maintaining one system, not two.

19.6 What this section deliberately does not specify

Consistent with this section's stated purpose (19.1), the following are explicitly deferred to Volume 1 and are not fixed by this blueprint: internal module-layer sub-architecture (bounded contexts, aggregates, DDD structure), specific database schema, specific message/event patterns between orchestration and module layers, and specific scaling/worker-pool design for NFR-SCALE-1/2. Part IV, Section 21 (Technology Decisions) fixes the platform choices (languages, frameworks, key libraries); Volume 1 fixes everything built on top of those choices.



20. Architecture Principles
20.1 Purpose

Section 19 described the system's shape. This section states the principles that should resolve design disputes Volume 1–5 will inevitably encounter, where two technically valid approaches compete and the deciding factor should be architectural philosophy, not individual preference. Each principle below is traced back to its origin earlier in this blueprint, because a principle with no traceable rationale is just an opinion, and this document set has been explicit throughout about not doing that.

20.2 Principle: Contract stability over implementation convenience

Statement: The AutomationModule interface, the flow-definition schema, and the REST API's public surface are treated as the most expensive things in the system to change, and any implementation convenience gained by breaking one of these contracts must be weighed against that cost explicitly, not assumed to be free.

Origin: Direct consequence of NFR-MAINT-1 and the plugin architecture's centrality to Business Objective 4 (marketplace) and the Module Parity Index metric. A marketplace whose plugin contract changes incompatibly across releases has no viable third-party ecosystem.

Practical implication for Volume 1: Contract changes require a versioning strategy (semantic versioning, deprecation windows) from the first release, not retrofitted once external plugin developers exist.

20.3 Principle: Every module is executed the same way, observed the same way, and reported on the same way

Statement: The orchestration layer must not contain module-specific branching logic anywhere in its core dispatch, retry, or reporting code paths. Module-specific behavior lives entirely inside the module implementation, behind the common contract.

Origin: Direct restatement of Section 14.2's "orchestration core is not module zero" principle and the Module Parity Index success metric (Part I, Section 6.3), now elevated to a binding architectural rule rather than a product aspiration.

Practical implication for Volume 1: Any proposed orchestrator feature that only makes sense for one module (e.g., "browser-specific retry logic" living in the orchestrator rather than the Web module) should be rejected at design review, not merged with a comment to "clean up later."

20.4 Principle: Fail loud, fail fast, fail informatively

Statement: Every layer, when it encounters an error it cannot recover from, must surface that error with enough structured context (which layer, which component, what was attempted, what failed) for the layer above to make an informed decision — never swallow an error silently, and never surface a generic failure message when a specific one is available.

Origin: Direct architectural expression of Product Philosophy 4.2 (fail-fast, short flows) and 4.4 (transparent over magical), and the concrete source of NFR-REL-2's flake-rate distinguishability requirement and FR-AI-3's AI-attribution-in-reports requirement.

Practical implication for Volume 1: Exception handling and error-type design (Volume 1's coding-standards section will formalize this, and Volume 5's engineering playbook will enforce it) must treat "what do we tell the layer above" as a first-class design question for every component, not an implementation detail decided ad hoc.

20.5 Principle: Security and compliance posture is designed in, not bolted on

Statement: Every new component or capability proposed in Volumes 1–5 must state, at design time, how it satisfies (or is out of scope for) the security NFRs from Part II Section 13.5 and the AI-optionality boundary from Section 18.4 — this determination happens during design, not during a pre-release security review.

Origin: Direct consequence of Business Objective 5's explicit requirement that enterprise trust posture be true "from the first GA release," and Persona Elena's structural role (Section 10.6) as a gatekeeper whose review must be satisfiable without special-casing.

Practical implication for Volume 1: Design documents and ADRs (Architecture Decision Records) for new components should include a short, mandatory security/compliance-impact statement as a template section, not an optional appendix.

20.6 Principle: Optionality is a load-bearing architectural property, not a configuration flag

Statement: Where the product strategy states a capability is optional (AI, specific modules, specific deployment integrations), the architecture must make that capability's absence a normal, tested, first-class code path — not an untested edge case that happens to work because the flag exists.

Origin: Direct consequence of NFR-REL-3 and the repeated emphasis throughout Parts I–III that AI-optionality is a permanent commitment (Section 18.4), not a phase-1 limitation. The distinction between "a flag exists" and "the flag's off-state is genuinely first-class" is the practical difference between a real commitment and a superficial one.

Practical implication for Volume 1 and Volume 5: CI quality gates (Volume 5) should include automated tests that run the full non-AI test suite with the AI module entirely absent from the deployment, not just disabled via configuration, to catch accidental coupling early.

20.7 Principle: Short, composable primitives over large, monolithic capabilities

Statement: When a new capability could be built either as one large, purpose-specific feature or as a composition of smaller, reusable primitives already available in the orchestration and module layers, the composable approach is preferred by default, even when it requires somewhat more authoring effort per use case.

Origin: Direct restatement of Product Philosophy 4.1 (Composability over completeness) as an architectural, not just product-philosophy, principle.

Practical implication for Volume 1: Component design reviews should explicitly ask "could this be expressed as a composition of existing primitives" before approving a new, purpose-built primitive.

20.8 Principle: The self-hosted deployment is a first-class target, not a downscoped version of the hosted one

Statement: Every architectural decision must be validated against both deployment topologies from Section 19.5 before being finalized; a design that only works well in the managed/hosted environment (e.g., assuming persistent outbound internet access, or a specific managed-database service) is not acceptable as a default architecture.

Origin: Direct consequence of NFR-SEC-4 and Business Objective 5's regulated-industry target segment; also protects against the common architectural drift where a "self-hosted option" quietly becomes an afterthought relative to the vendor's own hosted environment.

Practical implication for Volume 4 especially: Reference architectures, Helm charts, and infrastructure documentation must be maintained with the same rigor for self-hosted as for the managed offering, not treated as a lower-priority deliverable.

20.9 How these principles should be used

Each principle above should be cited by number (20.2 through 20.8) in Architecture Decision Records throughout Volumes 1–5 wherever it materially influenced a decision — this is the direct mechanism that keeps a document set this large internally consistent over the months it will take to write and the years it will take to maintain, rather than each volume's author independently reinventing (and potentially contradicting) the same judgment calls.



21. Technology Decisions
21.1 Purpose and decision format

This section fixes the platform-level technology choices — the ones expensive enough to change later that they belong in Volume 0 rather than being left to Volume 1–4's discretion. Each decision states what was chosen, the alternatives considered, and the rationale, in a lightweight ADR format. Volumes 1–4 own all technology decisions below this level (specific libraries within the chosen framework, specific ORM configuration, and so on).

21.2 Backend language and framework — .NET / C#, ASP.NET Core

Decision: The backend (API layer, orchestration layer, and built-in modules) is implemented in C# on .NET, with ASP.NET Core as the web framework. Status: locked, per explicit direction at the start of this document set.

Alternatives considered: Node.js/TypeScript (the language the original prototype framework built earlier in this project used); Go; Java/Kotlin on the JVM.

Rationale: .NET provides a mature, single-vendor-supported, strongly-typed platform with first-class support for the specific technologies the original ToC already anticipated (SignalR for real-time GUI updates, strong async/concurrency primitives well-suited to an orchestration engine dispatching across many concurrent module executions). Strong typing throughout the orchestration and module-contract layers directly supports Architecture Principle 20.2 (contract stability) by making contract violations a compile-time, not runtime, concern in the primary implementation language. This decision does not preclude modules or plugins being implemented in other languages via a process/RPC boundary (relevant to the Plugin Marketplace's third-party module support, Section 17) — that cross-language plugin story is a Volume 1 design question, not foreclosed by this decision.

21.3 Frontend framework — React

Decision: The GUI (flow-authoring surface, reporting dashboard) is implemented in React. Status: locked.

Alternatives considered: Vue, Angular, Svelte.

Rationale: React has the deepest available ecosystem for the two specific, non-trivial UI capabilities the original ToC calls out by name — a code/flow editor (Monaco integration) and a visual flow designer (React Flow integration) — both of which have mature, actively-maintained React bindings. This is a pragmatic ecosystem-fit decision more than an ideological framework preference, and Volume 2 should treat it as such.

21.4 Browser automation backend — Playwright

Decision: The Web module's automation backend is Playwright.

Alternatives considered: Selenium WebDriver; Cypress.

Rationale: Playwright's single-protocol, multi-browser architecture (Chromium, Firefox, WebKit from one API) reduces the Web module's internal complexity relative to Selenium's more fragmented driver model, and its native support for network interception and modern web-app patterns (auto-waiting, shadow-DOM piercing) directly reduces the flake sources that NFR-REL-2's flake-rate budget has to account for. This choice was already validated in the early prototype framework built during this project's initial exploration (Volume-agnostic prior work referenced informally; Volume 1 should treat that prototype's WebModule design as a starting point, not a final one).

21.5 Mobile automation backend — Appium / WebdriverIO

Decision: The Mobile module's automation backend is Appium (protocol) with WebdriverIO as the Node-based client library the module wraps, consistent with the standard industry pairing.

Alternatives considered: Native platform-specific frameworks only (XCUITest and Espresso directly, with no cross-platform abstraction layer).

Rationale: Appium's W3C-standardized protocol is the only widely-adopted option that lets one module abstract over both iOS and Android without maintaining two structurally separate implementations — directly serving Architecture Principle 20.3 (every module executed and reported on the same way) at the Mobile-module level specifically. The cost (Appium's comparatively higher setup complexity, noted already in Section 9.3's competitive analysis and Section 14.6's Partial-maturity scoping for Mobile at GA) is an accepted tradeoff given the alternative's much higher maintenance burden of two independent native implementations.

21.6 Desktop UI automation backend — nut.js

Decision: The Desktop UI module's automation backend is nut.js (or a maintained fork/successor, subject to Volume 1 confirming current maintenance status at implementation time).

Alternatives considered: SikuliX; native OS automation APIs accessed directly per-platform.

Rationale: nut.js provides a maintained, cross-platform (Windows/macOS/Linux) TypeScript-native API for the mouse/keyboard/screen-image-matching primitives this module requires, avoiding the need to hand-maintain three platform-specific automation backends for what is already, deliberately, the lowest-maturity-target module at GA (Section 14.7). Given this module's Minimal/Preview GA scope, this decision carries the lowest switching cost of any in this section and should be re-validated, not assumed permanent, when Desktop UI is built out further in Roadmap Phase 4 (Section 15.6).

21.7 AI provider integration — provider-abstracted, LLM-agnostic

Decision: The AI module integrates with LLM providers through an abstraction layer rather than a hardcoded single-provider SDK dependency, per the provider strategy already stated in Section 18.5.

Rationale: Restated here as a technology decision because it constrains Volume 3's implementation choices directly — no component outside the AI module's provider-adapter layer should have any provider-specific dependency (e.g., a specific vendor's SDK type) leaking into the orchestration or module-contract layers.

21.8 Primary data store — PostgreSQL

Decision: Flow definitions, execution history, audit logs, and report data are persisted in PostgreSQL.

Alternatives considered: SQL Server (a natural default given the .NET stack); a NoSQL document store (given flow definitions are naturally JSON-shaped).

Rationale: PostgreSQL's mature JSONB support handles the naturally document-shaped flow-definition and step-result data (avoiding a rigid relational schema for inherently variable per-module params/data payloads, per the types.ts design already proven in the early prototype) while still providing full relational and transactional guarantees for execution history, audit logs, and RBAC data where strong consistency matters. It is also free of per-core licensing cost, which matters directly for the self-hosted deployment model (Section 19.5) and the open-core licensing strategy (Section 16) not wanting to impose a paid database dependency on the open-source tier.

21.9 Caching and ephemeral state — Redis

Decision: Redis is used for session state, rate-limiting counters, and short-lived orchestration state (e.g., in-flight flow-run coordination across horizontally-scaled workers, per NFR-SCALE-2).

Rationale: Standard, well-understood choice for this role; no notable alternative was seriously considered given how uncontroversial this layer is relative to the decisions above.

21.10 Real-time client updates — SignalR

Decision: Live flow-execution progress (step-by-step status updates streamed to the GUI during a run, referenced implicitly by Persona Priya's authoring-loop experience) is delivered via SignalR.

Rationale: Native .NET real-time framework, avoiding an additional cross-language real-time stack (e.g., a separate WebSocket/Socket.IO layer) given the backend's locked .NET choice (Section 21.2).

21.11 Observability stack — OpenTelemetry, Prometheus, Grafana

Decision: Platform-internal observability (the "observability of the platform itself" cross-cutting concern from Section 19.4) is instrumented via OpenTelemetry, with Prometheus for metrics collection and Grafana for visualization, in the reference self-hosted architecture.

Rationale: OpenTelemetry's vendor-neutral instrumentation avoids locking the platform's own observability into a specific backend, which matters directly for the self-hosted deployment model where a customer may already have a preferred observability stack (echoing the Logs module's own "read from existing stacks, don't replace them" positioning in Section 14.8, now applied reflexively to Nexus's own operational observability). Full detail is Volume 4's responsibility.

21.12 Deployment and orchestration — Docker, Kubernetes, Helm

Decision: Both deployment topologies (Section 19.5) are containerized via Docker, with Kubernetes as the reference orchestration platform and Helm charts as the primary self-hosted installation mechanism.

Rationale: Industry-standard choice that directly serves NFR-SCALE-2's horizontal-scaling requirement and provides the most broadly portable self-hosted story across the range of enterprise infrastructure Persona Elena's segment (Section 10.6) is likely to already operate. Full detail is Volume 4's responsibility; this decision is recorded here only because it is expensive enough to reverse that it belongs at the Volume 0 locked-decision level.

21.13 Secrets management — Vault (or equivalent)

Decision: Credential and secret storage (NFR-SEC-2's envelope-encryption requirement) integrates with HashiCorp Vault as the reference implementation, with the architecture designed to support equivalent alternatives (cloud-provider-native secret managers) for managed/hosted deployments.

Rationale: Directly satisfies NFR-SEC-2's requirement that the encryption key be managed outside the primary application database; Vault is the most broadly-adopted, deployment-agnostic option consistent with the self-hosted-first posture of Architecture Principle 20.8.

21.14 Decisions explicitly deferred to later volumes

To keep this section from over-reaching into Volume 1–4 territory: ORM/data-access library choice, specific message-queue technology (if the orchestration layer's worker-dispatch design in Volume 1 requires one beyond Redis), API versioning mechanics, specific CI/CD tooling beyond GitHub Actions (already implied by the original ToC and treated as effectively locked, but formally owned by Volume 5), and frontend state-management library choice within React are all deferred to their respective volumes.




22. Risks
22.1 Purpose and format

This section consolidates risks raised throughout Volume 0 into a single register, adds architecture- and execution-specific risks not previously covered, and assigns each a likelihood/impact assessment and mitigation owner. Risks already detailed in earlier sections are referenced back rather than re-explained in full; this section's value is consolidation and ongoing tracking, not duplication.

22.2 Risk register
ID	Risk	Source	Likelihood	Impact	Mitigation	Owner
RISK-01	A well-funded point-tool incumbent adds cross-domain modules faster than Nexus can establish its moat	Section 8.6	Medium	High	Prioritize speed of credible module breadth (Roadmap Phases 0–4) over any single module's polish; track Module Parity Index continuously as the concrete counter-evidence to "shallow bolt-on" claims	Product/Roadmap owner
RISK-02	Buyer switching cost and inertia slow adoption despite a strong long-run maintenance-savings case	Section 8.6	High	Medium	Build an explicit migration/import story (e.g., importing existing Postman collections or Selenium scripts as a starting point) as an early roadmap item, not an afterthought	Product/Roadmap owner
RISK-03	"Unified platform" positioning triggers buyer skepticism from past bad experiences with shallow all-in-one tools	Section 8.6	Medium	Medium	Lead go-to-market with the Module Parity Index and transparent module-maturity disclosure (Section 14.10) rather than overclaiming uniform depth before it's real	Product/Marketing
RISK-04	An AI-native narrow competitor (Archetype D) closes the domain-breadth gap faster than Nexus closes the enterprise-trust gap	Section 9.5	Medium	High	Treat Roadmap Phase 5 (trust hardening) as non-negotiable and resourced from the start, not a "nice to have once we have time" phase	Executive sponsor
RISK-05	Market-sizing and buyer-behavior assumptions (Section 8.4) turn out to be materially wrong once validated	Section 8.4	Medium	High	Complete primary research (design-partner discovery interviews) before finalizing Roadmap Phase 1+ resourcing commitments; treat Section 8.4's figures as provisional until then	Product/Research owner
RISK-06	Self-healing selectors mask real regressions if confidence-threshold tuning is wrong, undermining trust in the entire AI layer	UC-3, Section 18.7	Medium	High	Ship self-healing with conservative default thresholds, mandatory transparent flagging (FR-AI-3) even on "successful" heals, and track self-heal acceptance rate as a release-gating metric before loosening defaults	AI/Volume 3 owner
RISK-07	AI provider cost or availability changes materially affect unit economics or reliability	Section 18.5	Medium	Medium	Provider-abstraction architecture (Section 21.7) is a hard requirement, not optional, specifically to keep this risk mitigable by a configuration change rather than a re-architecture	Engineering/AI owner
RISK-08	Third-party plugin code introduces a security incident (data exfiltration, privilege escalation) inside a customer's automation pipeline	Section 17.4, 17.6	Low-Medium	High	Mandatory sandboxing/resource limits for all marketplace-installed modules from Phase 4 onward, tiered certification (Section 17.3), and defense-in-depth enforcement at the module layer (Architecture Principle 20.5), not just at install-time review	Security owner
RISK-09	Marketplace security-review capacity becomes a bottleneck as submissions scale	Section 17.6	Medium	Medium	Approved-partner-reviewer model resourced ahead of Phase 5, not reactively once a backlog exists	Marketplace/Trust owner
RISK-10	Open-core boundary erodes over time (capabilities quietly move from open to commercial), damaging community trust	Section 16.6	Low-Medium	High	Published, versioned open/closed policy with mandatory public change-log entries for any boundary change; treated as a governance hard requirement, not a soft norm	Product/Licensing owner
RISK-11	Mobile and Desktop UI modules' lower GA maturity (Section 14.10) creates a credibility gap with prospects who need those modules on day one	Section 14.6, 14.7	Medium	Medium	Transparent maturity disclosure in sales/marketing materials rather than overselling; explicit design-partner validation path (Roadmap Phase 3) before broader GTM claims about Mobile specifically	Product/Sales enablement
RISK-12	Contract instability in the AutomationModule interface (breaking changes without adequate versioning) fractures the plugin ecosystem before it establishes trust	Architecture Principle 20.2	Low-Medium	High	Semantic versioning and deprecation-window policy defined and published before the Phase 4 external-plugin milestone, not after the first breaking change is already needed	Engineering/Architecture owner
RISK-13	The scope of this documentation program itself (six volumes, ~550 pages) outpaces the team's capacity to keep it consistent and current as the real system evolves	Cross-cutting, this project	Medium	Medium	Traceability discipline already established (business objectives → requirements → NFRs → architecture principles → ADRs) must be actively maintained as living documentation, with an explicit owner and review cadence, not treated as a one-time authoring exercise	Documentation/Architecture owner
RISK-14	Team scaling and hiring across six volumes' worth of specialized expertise (backend, frontend, AI/ML, infrastructure, security) does not keep pace with roadmap ambition	Cross-cutting	Medium	High	Roadmap phasing (Section 15) is explicitly sequenced so that team scaling can follow module priority rather than needing all specialties staffed simultaneously from Phase 0	Executive sponsor/Hiring
RISK-15	Regulatory or legal treatment of AI-assisted software (e.g., liability questions around AI-generated test flows or AI-healed selectors) shifts in ways that affect the AI Strategy's assumptions	Section 18	Low	Medium	Maintain the human-review boundary (Section 18.4) as a permanent architectural commitment specifically because it is also a risk-mitigating legal posture, not purely a UX/trust choice; revisit if regulatory guidance changes materially	Legal/Compliance liaison
22.3 Risk review cadence

Given RISK-13's explicit acknowledgment that this document set itself is a live artifact, this risk register should be reviewed at the close of each Roadmap phase (Section 15) at minimum — likelihood and impact re-assessed against what was actually learned during that phase, closed risks marked as such with the resolving evidence noted, and new risks added as they're identified rather than waiting for a scheduled full-document revision.




23. Future Vision
23.1 Purpose — and a deliberate discipline

Every prior section of Volume 0 has been disciplined about separating validated commitment from open hypothesis (Section 8.4's assumption-labeling, Section 16.2's explicit gray areas, Section 22's living risk register). This closing section maintains that same discipline in the opposite direction: it describes what becomes possible once the Roadmap (Section 15) reaches Phase 5, without pretending any of it is committed scope. Nothing in this section carries an FR or NFR identifier, and nothing here should be cited by Volumes 1–5 as a current requirement — it exists to make sure the architecture being locked in now (Sections 19–21) doesn't accidentally foreclose these directions later.

23.2 Beyond six modules — the plugin ecosystem as primary growth engine

Once the Plugin Marketplace (Section 17) is established and the Module Parity Index has real, sustained evidence behind it, the long-run vision is that most new automation-domain coverage comes from the ecosystem, not from the core team building modules 7, 8, and 9 itself — database automation, message-queue automation, IoT/embedded-device automation, and infrastructure-as-code validation are all plausible domains a third party could credibly build, given the architecture Sections 14.2 and 20.3 already commit to. The core team's long-run focus shifts from "how many modules do we build" to "how good is the platform the modules run on" — a natural maturation of Architecture Principle 20.7's composability preference, applied at the whole-ecosystem scale.

23.3 Deeper AI capability, still inside the permanent boundary

Section 18.4's boundary (AI never executes or persists a change autonomously) is stated as permanent, and this vision section does not propose relaxing it. What it does anticipate, within that boundary, is AI capability moving further up the value chain described in Section 18.3:

Autonomous exploration and coverage suggestion — an AI capability that crawls a target application and proposes a set of candidate flows for human review, rather than requiring a human to know what to ask for in the first place (extending "authoring acceleration" from single-flow generation to whole-suite scaffolding).
Cross-flow pattern learning — using the accumulated history of self-heals, flake patterns, and failure diagnostics across a customer's own flow history (never pooled across customers, per Section 18.4's data-pooling boundary) to make future suggestions increasingly specific to that customer's actual application patterns.
Natural-language querying of the reporting layer — extending diagnostic acceleration (Section 18.3) so Persona Sofia's UC-5 workflow could include asking a question like "why did mobile pass rate drop last week" in plain English and receiving an answer grounded in the same underlying trend and drill-down data the dashboard already exposes structurally.

Each of these remains squarely inside "AI proposes, human disposes" — they are more ambitious proposals, not a change to who approves them.

23.4 Industry-specific editions

Once the guardrail-pack mechanism (Section 17.2, Section 18.6) has real usage data, a plausible future direction is formally curated, Nexus-maintained (not just marketplace-community) editions for specific regulated industries — a healthcare edition with HIPAA-aligned guardrail packs and Enterprise-certified module curation pre-configured, a financial-services edition similarly curated for relevant compliance regimes. This is explicitly downstream of, and dependent on, the trust-hardening work in Roadmap Phase 5 (Section 15.7) being genuinely complete first, not a parallel initiative competing for the same resourcing.

23.5 Beyond verification — toward continuous system understanding

The furthest-out extension of the platform's thesis is a shift in framing from "Nexus verifies that a system behaves correctly when triggered" to "Nexus continuously understands how a system behaves in production and flags divergence from expectation automatically" — effectively extending the Logs module's anomaly-detection capability (Section 14.8) from a CI-time gate into a standing, continuous capability that blurs the line between test automation and production observability. This is stated cautiously and explicitly flagged as tension with the deliberate boundary drawn in Section 14.8 ("Nexus reads from observability stacks, it does not become one") — any move in this direction would need a full re-examination of that boundary, including its competitive positioning rationale (Section 8.2's buying-category analysis), not an assumed natural extension.

23.6 Multi-region and data-residency-aware architecture

As the managed/hosted deployment model (Section 19.5) scales into regions with distinct data-residency requirements, the future architecture may need region-aware data placement (flow definitions, execution history, and AI-provider routing all respecting a configured residency boundary) beyond the single-region assumption implicit in this Volume 0's current architecture sections. This is noted here specifically so Volume 4's infrastructure design considers extensibility toward this direction even though it is not GA scope.

23.7 What this vision deliberately does not include

Consistent with the boundaries set throughout this blueprint (Section 12.6, Section 14's per-module scope statements, Section 18.4), this future vision explicitly does not include: general business-process RPA expansion (still out of scope regardless of time horizon, per the Archetype C positioning analysis in Section 9.4), autonomous AI-driven flow execution without human review (permanent boundary, Section 18.4), or a shift away from the open-core licensing commitment (Section 16.2's "permanently open" pieces stay permanently open regardless of how far the roadmap extends). A future vision that quietly walks back Volume 0's foundational commitments is not a vision worth having; it is scope creep wearing a vision's clothing, and this document set has been built, deliberately, to make that distinction checkable rather than a matter of interpretation.

23.8 Closing note for Volume 0

This section closes Part IV, and with it, Volume 0 in full. Every subsequent volume — Backend Architecture, Frontend Architecture, AI Architecture, Infrastructure Architecture, and the Development Standards & Engineering Playbook — now has a complete, internally-traceable foundation to build on: a vision and mission (Part I), a validated (and honestly-labeled) problem and requirement set (Part II), a formalized platform shape (Part III), and a locked architectural foundation with explicit principles, technology decisions, and a living risk register (Part IV). Volume 1 begins from here.



___________________________________________________________________________________________________________________________________________________________________

ADS-001 — Backend Architecture Design

Objective

Design the entire backend platform that orchestrates automation execution, plugin lifecycle, workflows, reporting, AI integration, and persistence.

Chapter 1 – Vision
Purpose
Goals
Non-goals
Functional Requirements
Non-functional Requirements


Chapter 2 – Architectural Principles
Domain-Driven Design (DDD)
Clean Architecture
SOLID
Plugin-first
Event-driven
Dependency Injection
CQRS (optional)
Open/Closed Principle


Chapter 3 – High-Level Architecture
Client

↓

REST API

↓

Automation Orchestrator

↓

Execution Engine

↓

Plugin Manager

↓

Plugins


Chapter 4 – Core Domain Model

Entities

Project
Workspace
Test Suite
Test Case
Test Step
Workflow
Execution
Environment
Browser
Device
Plugin
User
Role
Artifact
Report
Schedule

Value Objects

BrowserType
DeviceType
Locator
URL
Credentials

Aggregates

Project
Execution
Workflow



Chapter 5 – Modules
Core
Automation
Execution
Workflow
Plugin
Reporting
Authentication
Scheduling
Notification
AI Gateway


Chapter 6 – Plugin Framework
Plugin lifecycle
Registration
Discovery
Versioning
Dependency management
Isolation
Security



Chapter 7 – Execution Engine
Scheduler
Queue
Parallel execution
Retry policy
Timeout
Cancellation
Resume
Recovery


Chapter 8 – Event Bus

Events

ExecutionStarted
TestStarted
StepStarted
StepPassed
ScreenshotCaptured
ExecutionCompleted
PluginLoaded



Chapter 9 – Database Design

ER Diagram

Tables

Projects
Executions
Reports
Plugins
Users
Roles
Environments
Devices



Chapter 10 – APIs

REST

WebSocket / SignalR

Authentication

Authorization

Chapter 11 – Security

JWT

OAuth

Secrets

Encryption

Audit

Chapter 12 – Deployment

Docker

Kubernetes

Scaling

Health Checks


__________________________________________________________________________________________________________________________________________________________________

