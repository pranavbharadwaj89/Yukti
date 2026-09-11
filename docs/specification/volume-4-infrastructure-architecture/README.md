# Volume 4 — Infrastructure Architecture · Part I — Developer Environment & Containerization


## Table of Contents

  - [Section 1 — Development Environment](#1-development-environment)
  - [Section 2 — Docker](#2-docker)
  - [Section 3 — Kubernetes](#3-kubernetes)
  - [Section 4 — Helm](#4-helm)
  - [Section 5 — CI/CD](#5-ci-cd)
  - [Section 1 — Lint & Static Analysis](#1-lint-static-analysis)
  - [Section 2 — Unit & Component Tests](#2-unit-component-tests)
  - [Section 3 — Build & Image Creation](#3-build-image-creation)
  - [Section 4 — Security & Vulnerability Scanning](#4-security-vulnerability-scanning)
  - [Section 5 — Integration Tests](#5-integration-tests)
  - [Section 6 — End-to-End Tests](#6-end-to-end-tests)
  - [Section 7 — Deploy to Staging](#7-deploy-to-staging)
  - [Section 8 — Staging Verification](#8-staging-verification)
  - [Section 9 — Promote to Production](#9-promote-to-production)
  - [Section 6 — GitHub Actions](#6-github-actions)
  - [Section 7 — Monitoring](#7-monitoring)
  - [Section 8 — OpenTelemetry](#8-opentelemetry)
  - [Section 9 — Prometheus](#9-prometheus)
  - [Section 10 — Grafana](#10-grafana)
  - [Section 11 — Elasticsearch](#11-elasticsearch)
  - [Section 12 — Redis](#12-redis)
  - [Section 13 — PostgreSQL](#13-postgresql)
  - [Section 14 — Secrets](#14-secrets)
  - [Section 15 — Vault](#15-vault)
  - [Section 16 — TLS](#16-tls)
  - [Section 17 — Disaster Recovery](#17-disaster-recovery)
  - [Section 18 — High Availability](#18-high-availability)
  - [Section 19 — Scaling](#19-scaling)
  - [Section 20 — Backup Strategy](#20-backup-strategy)
  - [Section 21 — Cost Optimization](#21-cost-optimization)

---

## 1. Development Environment

### 1.1 Purpose and the dev/prod parity principle

This volume specifies how the system Volumes 1–3 designed is actually built, deployed, and operated. This opening section specifies the local development environment every engineer works in day to day, governed by one overriding principle worth stating before any tooling detail: **local development should be as structurally similar to the self-hosted production topology as practical**, directly extending Architecture Principle 20.8 (Volume 1, Section 20.8 — "self-hosted is a first-class target, not a downscoped afterthought") to development itself. An engineer's laptop is not a special, permanently-simplified environment with its own divergent architecture — it is the smallest instance of the same topology Volume 1, Part VI, Section 37 already specified.

### 1.2 Local stack — docker-compose as the development topology

```yaml
# docker-compose.dev.yml (abbreviated)
services:
  postgres:
    image: postgres:16
    environment: { POSTGRES_DB: nexus_dev }
    ports: ["5432:5432"]
  redis:
    image: redis:7
    ports: ["6379:6379"]
  nexus-api:
    build: { context: ., dockerfile: src/Nexus.Api/Dockerfile }
    depends_on: [postgres, redis]
    volumes: ["./src:/app/src"]     # hot-reload, Section 1.4
  nexus-orchestration:
    build: { context: ., dockerfile: src/Nexus.Orchestration/Dockerfile }
    depends_on: [postgres, redis]
  nexus-gui:
    build: { context: ./nexus-gui }
    ports: ["3000:3000"]
    volumes: ["./nexus-gui/src:/app/src"]
```

This directly mirrors Volume 1, Part VI, Section 37.3's container inventory — `nexus-api` and `nexus-orchestration` remain separate services even in local development, never collapsed into one process for developer convenience, specifically because Volume 1, Part I, Section 4.4 identified their independent scaling profile as architecturally significant; a developer who only ever runs them combined would never notice a bug that only manifests when they're genuinely separate processes (e.g., an accidental in-process assumption leaking across what should be a container boundary).

### 1.3 AI provider stubbing for offline/low-cost local development

Directly extending Volume 3, Part IV, Section 14.2's `AiProvider` interface: local development uses a stub implementation (`StubAiProvider`) by default, returning deterministic, schema-valid fixture responses for each of Volume 3's seven capabilities (Part III there) — never requiring a real, costed API key for routine local development, and never making a real outbound call by default. This is a direct, practical application of Volume 3's own AI-optionality architecture (Section 1.4 there) to the development workflow itself: an engineer working on, say, Volume 2's Workflow Designer UI should never need an AI provider credential just to run the application locally. A developer actively working on Volume 3's capability engines opts in to a real provider connection via a local environment variable, with Volume 3, Part IV, Section 16's cost-tracking infrastructure equally active in that mode, so even ad hoc developer experimentation is visible in cost reporting, not a blind spot.

### 1.4 Hot reload and inner-loop speed

`nexus-api` and `nexus-gui` (1.2) mount source directories as volumes with framework-native hot-reload (`dotnet watch` for the API, Vite's dev server for the React GUI, Volume 2 Section 4) — `nexus-orchestration`, given its background-worker nature and the incremental-commit execution loop specified in Volume 1, Part III, Section 19.2, restarts on change rather than hot-reloading, since a mid-flow-execution hot-reload would risk exactly the kind of ambiguous, hard-to-reason-about state Volume 1's per-step commit design (Section 16.7 there) was built to avoid — a deliberate, narrow exception to blanket hot-reload, justified by that specific component's execution semantics.

### 1.5 Seed data

A deterministic seed script populates local PostgreSQL with a representative dataset — a handful of `Flow`s spanning multiple modules, a few completed `FlowRun`s with realistic `StepResult` histories (including at least one self-healed step, ensuring `AiAttribution` rendering, Volume 2 Section 9.3, is exercisable locally without a real AI provider call, using Section 1.3's stub), and baseline RBAC roles (Volume 1, Part IV, Section 25.2) — giving every new engineer a working, realistic environment within minutes of first setup, directly serving the same onboarding-speed concern Volume 0's NFR-UX-2 targeted for end users, now applied to engineer onboarding specifically.

### 1.6 TLS in local development

Local development uses locally-trusted, self-signed certificates (via `mkcert` or equivalent) rather than plain HTTP — not because local traffic faces real external threats, but because Section 16 (TLS, later in this volume) specifies TLS as universally required in every environment including self-hosted (per NFR-SEC-1), and a development environment that's structurally different in this specific respect risks an engineer's mental model of "how Nexus handles TLS" being subtly wrong, or a TLS-dependent bug (mixed-content issues, cookie `Secure` flag behavior per Volume 2, Section 14.2) going undetected until a staging or production environment surfaces it.

### 1.7 Developer environment as versioned, reviewed configuration

The full `docker-compose.dev.yml` and its accompanying setup scripts are versioned in the same repository as application code, reviewed via the same pull-request process (Volume 5's eventual responsibility to formalize in full) as any other change — a modification to the local development topology is treated with the same rigor as a change to the production Helm charts (Section 4), since Section 1.1's parity principle only holds if both are kept deliberately, continuously synchronized rather than allowed to drift independently over time.

### 1.8 What local development deliberately does not replicate

Consistent with this document set's practice of stating scope honestly (Volume 2, Section 16.1's model): local development does not replicate Volume 1, Part VI, Section 37.3's horizontal autoscaling, the Redis-backed SignalR backplane's multi-instance behavior (Volume 1, Part V, Section 31.5 — a single local `nexus-api` instance is sufficient for local UI development), or Volume 3, Part IV, Section 14.7's circuit-breaker behavior under real provider degradation. These are explicitly reserved for the staging environment (Section 5, CI/CD, will specify the deployment pipeline that promotes code there) — local development optimizes for fast inner-loop iteration on business logic, not for validating infrastructure-scale behavior, which is a deliberate, different environment's job.



2. Docker
2.1 Purpose — the image-build specification behind Volume 1's container inventory

Volume 1, Part VI, Section 37.2 named the four container images (nexus-api, nexus-orchestration, nexus-cli, nexus-sandbox-runner) at the architectural level. This section specifies how each is actually built — base images, layer strategy, and the security scanning that gates promotion.

2.2 Multi-stage build pattern

Every image follows the same multi-stage structure Volume 1, Part VI, Section 37.2 already committed to in principle ("SDK image for build, runtime-only image for the final artifact"), specified here concretely:

dockerfile
# nexus-api/Dockerfile (representative structure)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Nexus.Domain/*.csproj src/Nexus.Domain/
COPY src/Nexus.Application/*.csproj src/Nexus.Application/
COPY src/Nexus.Infrastructure/*.csproj src/Nexus.Infrastructure/
COPY src/Nexus.Api/*.csproj src/Nexus.Api/
RUN dotnet restore src/Nexus.Api/Nexus.Api.csproj    # dependency layer, cached
                                                          # independently of source changes
COPY src/ src/
RUN dotnet publish src/Nexus.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
RUN addgroup -S nexus && adduser -S nexus -G nexus    # non-root, Section 2.5
WORKDIR /app
COPY --from=build /app .
USER nexus
ENTRYPOINT ["dotnet", "Nexus.Api.dll"]

The .csproj-only copy before the full source copy is a deliberate Docker-layer-caching optimization directly serving CI build-time performance (Part II, Section 5's CI/CD pipeline speed) — a source-only change doesn't invalidate the (comparatively slow) dotnet restore layer, since only a dependency change would have changed the .csproj files that layer depends on.

2.3 Base image selection

Alpine-based runtime images (aspnet:8.0-alpine, and for nexus-gui's static-asset-serving image, nginx:alpine) are preferred over full Debian-based images specifically to minimize image size and attack surface — directly serving both Volume 1, Part VI, Section 37.2's "minimal image size and attack surface" goal and this volume's later disaster-recovery concerns (Part V — smaller images mean faster image pulls during a scale-out or recovery event). The one deliberate exception: nexus-sandbox-runner (Volume 1, Part III, Section 18.5's Community-tier module isolation) uses a purpose-built minimal image with an even more restrictive package set than Alpine's default, reviewed specifically for this image's elevated security requirements (running untrusted third-party module code) as part of Section 2.5's scanning discipline.

2.4 Layer strategy for the nexus-orchestration image specifically

nexus-orchestration's image additionally bundles Playwright's browser binaries (Volume 0, Section 21.4's locked Web-module technology) as a distinct, cached layer separate from the application code layer — since browser binaries are large and change far less frequently than application code, this ordering keeps routine application deployments from re-downloading/re-layering multi-hundred-megabyte browser binaries on every release, a meaningful CI/CD and registry-storage cost optimization directly relevant to this Part's later Cost Optimization section (21).

2.5 Image scanning and security gates

Every built image is scanned (Trivy or equivalent) for known CVEs in both OS packages and application dependencies before being eligible for deployment — this scan is a mandatory CI gate (Part II, Section 5), not an advisory report reviewed after the fact, directly extending Volume 1, Part VI, Section 40's "security designed in, not bolted on" discipline (Architecture Principle 20.5) to the container-image layer specifically. Every runtime image runs as a non-root user (2.2's USER nexus directive) with a read-only root filesystem where the application's own design permits it (true for nexus-api; nexus-orchestration requires limited writable scratch space for the Web module's temporary browser-profile data, explicitly scoped and documented as the one exception to this rule).

2.6 Image tagging and registry strategy

Images are tagged with the full Git commit SHA (immutable, traceable to exact source) as the primary tag, with a mutable latest tag reserved strictly for local development convenience (2.2's compose file) and never used in any deployed environment — deploying by SHA-pinned tag is a hard requirement (enforced by Helm chart validation, Section 4), directly serving Volume 1, Part VI, Section 37.8's rollback procedure: a rollback to a specific prior Helm revision must deterministically resolve to the exact same image that was actually running at that revision, which a mutable tag could not guarantee.

2.7 Registry architecture for self-hosted, air-gapped deployments

Directly extending Volume 1, Part VI, Section 37.7's air-gapped installation path: images are published to a public registry for the standard managed/hosted and internet-connected self-hosted cases, with an explicit, documented image-mirroring procedure (docker save/docker load, or a private-registry-sync tool) for air-gapped customers — this section is where that Volume 1-referenced "fully detailed air-gapped installation procedure (image mirroring to a private registry)" is actually specified, closing a forward reference Volume 1 deliberately left to this volume.

2.8 Local build parity with CI builds

The exact same Dockerfiles specified in this section build both local development images (Section 1.2's compose file) and CI/production images — no separate "dev Dockerfile" diverging from what actually ships, directly reinforcing Section 1.1's dev/prod parity principle at the image-build level specifically, and eliminating an entire class of "works locally, breaks in the built image" bug that a divergent local build process would otherwise risk.

2.9 Image build performance budget

Consistent with Volume 1, Part VI, Section 38's performance-as-release-gate discipline, applied here to build infrastructure itself: full CI image builds (all four images, cold cache) are budgeted at under 10 minutes total, with warm-cache incremental builds (the common case, per 2.2's layer-caching design) budgeted under 3 minutes — a build-time regression beyond these budgets is investigated with the same seriousness Volume 1 and Volume 2 both established for their respective performance regressions, since slow CI builds directly degrade every engineer's inner-loop velocity, not just one team's.



3. Kubernetes
3.1 Purpose — deepening Volume 1's reference topology

Volume 1, Part VI, Section 37.3 established the reference Kubernetes topology at the architectural level — which containers exist, how they relate. This section specifies the operational Kubernetes detail beneath that: namespace strategy, resource governance, and cluster-level policies that make the topology actually production-ready, not just architecturally correct.

3.2 Namespace strategy
nexus-system          # nexus-api, nexus-orchestration, outbox relay (Volume 1 Part III §22.2)
nexus-data             # PostgreSQL, Redis (self-hosted deployments only — managed
                          deployments typically use external managed services here instead)
nexus-observability     # Prometheus, Grafana, OpenTelemetry Collector (Section 8-10)
nexus-sandbox           # ephemeral nexus-sandbox-runner pods (Volume 1 Part III §18.5),
                           isolated into its own namespace specifically so its distinct,
                           stricter network and resource policies (3.5) don't need to be
                           reasoned about alongside nexus-system's trusted workloads

For multi-tenant managed/hosted deployments (Volume 1, Part IV, Section 26.1's pooled model), this namespace structure is per-cluster, not per-tenant — tenant isolation is enforced at the application/database layer exactly as Volume 1, Part IV, Section 26.3 specified, not by Kubernetes namespace boundaries, consistent with that section's explicit rejection of database-per-tenant (and, by extension, cluster-or-namespace-per-tenant) as the GA isolation model. For dedicated-instance deployments (Volume 1, Part IV, Section 26.6), this entire namespace structure is deployed once per dedicated cluster, giving that isolation model genuine infrastructure-level separation matching its stronger isolation promise.

3.3 Resource requests and limits

Every container specifies both CPU/memory requests (guaranteed minimum, used for scheduling) and limits (hard ceiling), sized per Volume 1's own performance specification — nexus-orchestration pods are sized with headroom for Volume 1, Part VI, Section 39.3's browser-process-pool resource profile (the Web module's Playwright instances being the dominant memory consumer per that section's resource-pool table), while nexus-api pods are sized for its comparatively lightweight, stateless request-handling profile. Requests are set conservatively (never zero, avoiding the Kubernetes anti-pattern of unbounded best-effort scheduling that risks node-level resource contention) and validated periodically against actual observed usage (Section 7's monitoring stack) rather than set once at initial deployment and never revisited.

3.4 Pod Disruption Budgets and graceful shutdown

Directly implementing Volume 1, Part VI, Section 37.6's graceful-shutdown requirement at the Kubernetes-policy level: a PodDisruptionBudget for nexus-orchestration ensures voluntary disruptions (node draining during a cluster upgrade, for instance) never take down more than a configured fraction of running orchestration pods simultaneously, giving Volume 1's terminationGracePeriodSeconds and in-flight-execution-draining logic (Section 37.6 there) room to actually complete rather than being raced by an aggressive, unbudgeted rolling node replacement.

3.5 Network policies — the sandbox namespace's stricter isolation

Directly implementing Volume 1, Part III, Section 18.5's sandboxed-execution security model at the Kubernetes-network level: nexus-sandbox namespace pods (running potentially-untrusted Community-tier module code) are governed by a default-deny NetworkPolicy, with explicit, narrow allow-rules only for the specific gRPC callback path back to nexus-orchestration (Volume 1, Part III, Section 18.5's communication design) — no sandboxed pod can initiate a connection to nexus-data, the public internet, or any other namespace, directly enforcing at the cluster-network layer the same trust boundary Volume 1, Part I, Section 2.5 established conceptually and Volume 1, Part III, Section 18.3's narrow ExecutionContext enforced at the application layer — this is defense-in-depth's third layer for that specific boundary, network policy joining application-layer scoping and process isolation.

3.6 Ingress and TLS termination

A single Ingress controller (NGINX Ingress or a cloud-provider-native equivalent for managed deployments) routes external traffic to nexus-api (REST and SignalR, Volume 1, Part V, Sections 30–31) with TLS termination at the ingress layer — full detail of the TLS/certificate strategy is Section 16 (TLS) of this Part's later coverage; this section notes only that Ingress-level termination, rather than per-pod TLS, is the chosen architecture, re-encrypting internally only where Section 16's zero-trust posture requires it for the self-hosted regulated-industry deployment profile (Persona Elena's segment, Volume 0 Section 10.6).

3.7 Horizontal Pod Autoscaling — restating and completing Volume 1's design

Volume 1, Part VI, Section 37.4 already specified that nexus-orchestration's HPA scales against the custom nexus.orchestration.concurrent_executions metric (Volume 1, Part IV, Section 29.4) rather than generic CPU/memory. This section adds the concrete Kubernetes mechanics: a PrometheusAdapter (or equivalent custom-metrics-API adapter) exposes that Prometheus-collected metric to Kubernetes' HPA controller, with scale-up/scale-down thresholds and cooldown windows tuned to avoid flapping (rapid, oscillating scale up/down driven by a bursty metric) — a specific, named operational concern this section adds beyond Volume 1's architectural commitment.

3.8 Node pools and workload separation

Where cluster size justifies it (typically larger managed/hosted deployments, per Volume 1, Part IV, Section 26.2's pooled-scale reasoning), nexus-orchestration (compute- and memory-intensive, per 3.3) and nexus-api (lightweight, high-request-rate) run on separate node pools sized appropriately for each workload's profile, with nexus-sandbox on a third, more restrictively-configured pool (smaller instances, since individual sandbox executions are bounded, per Volume 1, Part III, Section 18.5's resource-limit requirement) — a cost-and-performance optimization available to larger deployments, while smaller self-hosted deployments (Architecture Principle 20.8) run everything on a single, undifferentiated node pool without needing this separation to function correctly, only to scale most efficiently.

3.9 Cluster-level RBAC — Kubernetes RBAC, distinct from Volume 1's application RBAC

A clarification worth stating precisely, given Volume 1, Part IV, Section 25 already specified an application-level RBAC model: Kubernetes' own RBAC (governing which humans/service accounts can modify cluster resources — deployments, secrets, network policies) is a distinct, infrastructure-layer permission system, never conflated with Volume 1's Permission enum (Volume 1, Part IV, Section 25.1). A Nexus platform administrator (Volume 1's Administrator role) does not thereby gain Kubernetes cluster-admin access — the two systems are deliberately unconnected, each governing its own layer, consistent with this document set's recurring practice of keeping distinctly-scoped concerns architecturally separate rather than collapsing them for convenience (the same discipline Volume 1, Part IV, Section 27.7 applied to Audit versus Logging, now applied here to application RBAC versus infrastructure RBAC).



4. Helm
4.1 Purpose — the packaging and deployment mechanism

Volume 1, Part VI, Section 37.3 committed to Helm charts as the deployment mechanism for both managed/hosted and self-hosted topologies, from one identical chart set. This section specifies that chart's actual structure and the values-file strategy that lets one chart set serve genuinely different deployment scales and environments without forking.

4.2 Chart structure — umbrella chart over per-component subcharts
nexus-helm/
├── Chart.yaml                    # umbrella chart
├── values.yaml                    # platform-wide defaults
├── values.managed-hosted.yaml      # overrides for Nexus-operated infrastructure
├── values.self-hosted.yaml         # overrides for customer-operated, internet-connected
├── values.air-gapped.yaml          # overrides for Volume 1 §37.7's air-gapped path
├── charts/
│   ├── nexus-api/
│   ├── nexus-orchestration/
│   ├── nexus-sandbox-runner/
│   ├── postgresql/                 # bundled subchart, used only when values specify
│   │                                  self-hosted-without-managed-database (4.4)
│   └── redis/                      # same conditional-inclusion pattern
└── templates/
    ├── networkpolicy.yaml           # Section 3.5
    ├── ingress.yaml                 # Section 3.6
    └── hpa.yaml                     # Section 3.7

The umbrella-chart-with-subcharts structure directly mirrors Volume 1, Part V, Section 36.2's solution structure decision (per-component projects under one solution) — the same organizational philosophy applied one layer up, at the deployment-packaging level, for the same reason: each component (nexus-api, nexus-orchestration, nexus-sandbox-runner) has its own independently-versionable chart, composed by the umbrella chart, rather than one monolithic template set that couples every component's deployment configuration together.

4.3 Values-file hierarchy and precedence

values.yaml establishes safe, sensible defaults; environment-specific files (values.managed-hosted.yaml, etc.) override only what genuinely differs — replica counts, resource sizing (Section 3.3), and which subcharts are enabled (4.4) — never duplicating the full configuration surface per environment. A customer's actual deployment additionally supplies a final, thin values.customer.yaml layer (database connection strings, AI-provider configuration per Volume 3, Part IV, Section 14, license key) — this three-tier layering (platform defaults → environment profile → customer specifics) is what lets Volume 1, Part VI, Section 37.3's "identical chart set, differing only in values files" claim hold precisely, with each tier's role kept distinct and minimal.

4.4 Conditional subchart inclusion — managed database vs. bundled
yaml
# values.self-hosted.yaml (excerpt)
postgresql:
  enabled: true      # bundled subchart deploys PostgreSQL within the cluster
redis:
  enabled: true

# values.managed-hosted.yaml (excerpt)
postgresql:
  enabled: false     # Nexus-operated managed database service used instead;
                        connection details supplied via values.customer.yaml-equivalent
redis:
  enabled: false

This conditional pattern is what allows a resource-constrained self-hosted customer (Architecture Principle 20.8) to helm install a single, complete, self-contained deployment with zero external dependencies beyond the cluster itself, while a larger managed deployment or a self-hosted customer with existing database infrastructure points at external, already-managed PostgreSQL/Redis instances (Volume 0, Section 21.8–21.9's technology choices, deployment-topology-agnostic by design) — the same chart, genuinely different operational posture, purely through values configuration.

4.5 Chart versioning and release correlation

Chart versions follow semantic versioning independent of, but correlated with, the application image tags they reference (Section 2.6's SHA-based tagging) — a chart version bump accompanies any change to the deployment topology itself (a new NetworkPolicy rule, a changed resource default), while an image-only update (a routine application release with no infrastructure change) can reuse the same chart version with only the image tag values changed, keeping the two versioning concerns — "how is this deployed" versus "what code is running" — appropriately decoupled, mirroring the same decoupling Volume 3, Part I, Section 3.7 established between prompt-version releases and Planning Engine code releases.

4.6 Chart testing

Every chart change is validated via helm lint, a helm template dry-run diffed against the previous version (catching unintended resource changes), and a full deployment test against an ephemeral test cluster (spinning up the complete umbrella chart, running Volume 2, Section 19.9's E2E smoke-test subset against it) before being eligible for release — directly extending Volume 1, Part VI, Section 37.5's migration-gates-rollout principle to the chart level generally: infrastructure changes are verified before they reach any real environment, never validated for the first time against staging or, worse, production.

4.7 Self-hosted installation experience — restating Volume 1's procedure with full detail

Directly completing Volume 1, Part VI, Section 37.7's installation summary: helm repo add nexus https://charts.nexus.dev (or, for air-gapped deployments per Section 2.7 of this Part, a locally-mirrored chart repository), then helm install nexus nexus/nexus-helm -f values.self-hosted.yaml -f values.customer.yaml — a single command producing a complete, running deployment, with helm upgrade following the same migration-gated pattern Volume 1, Part VI, Section 37.5 specified for schema changes (the chart's upgrade hooks trigger the migration Job before the application Deployment rolls out the new image version).

4.8 Secrets in Helm values — what never appears in a values file

Directly extending Volume 1, Part IV, Section 24.4 and NFR-SEC-2's secret-handling discipline to the deployment-configuration layer: no values.yaml file, at any tier (4.3), ever contains a raw secret value (database password, AI-provider API key, TLS private key) — these are referenced by name/path against Section 15's Secrets management and Section 15's Vault integration, with Helm's role limited to specifying which secret reference to use, never the secret's actual value, keeping every values file safe to commit to version control (4.9) without exception.

4.9 Chart source control and review

The Helm chart repository is versioned and reviewed with the same rigor as application source code (Section 1.7's principle restated at the deployment-packaging layer specifically) — a chart change is a pull request, reviewed against Section 4.6's automated checks plus human review for anything touching Section 3.5's network policies or Section 3.9's RBAC configuration specifically, those being the highest-security-impact categories of change this chart can express, warranting the same elevated review attention Volume 1, Part VI, Section 40.3 gave to security-sensitive backend code changes.


____________________________________________________________________________________________________________________________________________________



5. CI/CD
5.1 Purpose — the pipeline that consolidates every quality gate this document set has specified

This section is a deliberate synthesis point: across four volumes, this document set has specified more than a dozen distinct quality gates — Volume 1's coding-standard analyzers and load-testing gate, Volume 2's bundle-size budgets and Lighthouse CI checks, Volume 3's evaluation-set release gates, Volume 4's own image scanning and chart testing. None of these are useful in isolation; they only function as a coherent quality bar when assembled into one pipeline every change passes through identically. This section is where that assembly is specified, referencing every prior gate by its originating section rather than re-deriving any of them.

5.2 Pipeline stages
1. Lint & Static Analysis
   - Backend: Roslyn analyzers (Volume 1, Part VI §40.2) — strongly-typed IDs,
     dependency-direction, structured-logging-only rules
   - Frontend: ESLint import-boundary rules (Volume 2, Section 5.3), TypeScript
     strict-mode compilation (Volume 2, Section 4.7)
   - Infrastructure: helm lint (Section 4.6), Dockerfile linting (hadolint)

2. Unit & Component Tests
   - Backend: Nexus.Domain.Tests, Nexus.Application.Tests (Volume 1, Part VI §40.6)
   - Frontend: Vitest unit tier, React Testing Library component tier (Volume 2,
     Section 19.2)
   - AI: prompt template offline evaluation sets (Volume 3, Section 3.6, 19.3)

3. Build & Image Creation
   - dotnet publish, Vite build, Docker multi-stage builds (Section 2.2)
   - Bundle-size budget check (Volume 2, Section 17.2) — build-breaking on regression

4. Security & Vulnerability Scanning
   - Image CVE scanning (Section 2.5)
   - Dependency vulnerability scanning (npm audit / NuGet equivalent)
   - AI guardrail adversarial test suite (Volume 3, Section 17.9)

5. Integration Tests
   - Nexus.Infrastructure.IntegrationTests, Nexus.Api.IntegrationTests against
     ephemeral test-container PostgreSQL/Redis (Volume 1, Part VI §40.6)
   - Frontend integration tier with MSW (Volume 2, Section 19.6)

6. End-to-End Tests
   - Full Playwright/Nexus-self-testing E2E suite (Volume 2, Section 19.8) against
     a fully-deployed ephemeral environment (Section 4.6's test-cluster pattern)

7. Deploy to Staging
   - Migration Job gate (Volume 1, Part VI §37.5) before application rollout
   - Helm chart deployment (Section 4.7)

8. Staging Verification
   - E2E smoke subset (Volume 2, Section 19.9)
   - Load test against NFR-SCALE-1's target (Volume 1, Part VI §38.8)
   - Lighthouse CI performance gate (Volume 2, Section 17.9)
   - AI evaluation-set regression check (Volume 3, Section 19.3)

9. Promote to Production
   - Requires all above stages green, plus explicit approval gate (Section 5.5)

Every stage after stage 1 depends on the previous stage's success — this is a strictly sequential pipeline, not a parallel-everything race to the fastest possible feedback, because several later stages (integration tests, E2E) are expensive enough that running them against a build that hasn't even passed linting would waste real compute cost for no benefit, directly consistent with Volume 3, Section 17.8's "reject cheaply before doing anything expensive" reasoning, applied here to the pipeline's own structure.

5.3 Environments
Environment Purpose Deployment trigger
Local Individual developer inner loop (Section 1) Manual
Ephemeral (per-PR)  Isolated environment per pull request, torn down after merge/close, used for stage 6's E2E run and reviewer manual verification Automatic, per PR
Staging Pre-production verification (stages 7–8), production-topology-equivalent  Automatic, on merge to main
Production  Live customer traffic (managed/hosted)  Manual approval (5.5) after staging verification

Self-hosted customer deployments (Volume 1, Part VI, Section 37.7) are a distinct concept from this internal environment progression — a self-hosted customer's own environment is their production, reached via the release artifact this pipeline produces (a specific, versioned Helm chart + image set), not a stage this pipeline directly deploys to.

5.4 The ephemeral per-PR environment — why it exists

A full, isolated deployment per open pull request (rather than a shared, persistent staging environment alone) directly serves two goals: it lets stage 6's E2E suite (Volume 2, Section 19.8) run against exactly the code under review, with zero risk of interference from a concurrently-merged, unrelated change also deploying to a shared staging environment at the same time — and it gives a human reviewer a genuinely live, clickable environment to manually verify a UI change against, rather than reviewing only static code diffs. This is judged worth its infrastructure cost specifically because Volume 2's UX-quality bar (Section 1, UX Principles) is difficult to fully verify from a code diff alone — seeing the actual rendered Workflow Designer (Volume 2, Section 11) change is a meaningfully better review signal than reading the React code that produces it.

5.5 Production promotion — manual gate, automated everything else

Every stage through 5.2's stage 8 (Staging Verification) is fully automated with no human approval required — a change that passes every gate is, by this pipeline's own definition, ready. Stage 9 (Promote to Production) still requires an explicit human approval click, not because the automated gates are distrusted, but because production promotion timing itself is a business/operational decision (avoiding a deploy immediately before a weekend, coordinating with a customer-communication plan for a significant change) distinct from whether the change is correct — a deliberate, narrow separation of "is this safe to ship" (fully automated) from "is this the right moment to ship it" (human judgment), consistent with Volume 1, Architecture Principle 20.4's fail-fast philosophy not being about removing human judgment entirely, only about removing it from the correctness determination specifically.

5.6 Rollback integration

A failed stage-8 verification, or a post-promotion incident, triggers the exact rollback procedure Volume 1, Part VI, Section 37.8 already specified (helm rollback to the prior revision) — this pipeline's role in rollback is limited to making that prior-revision Helm release readily available (every successful pipeline run's artifacts are retained per Section 20's backup/retention policy, this Part's later coverage) rather than reinventing rollback mechanics this volume already owns elsewhere.

5.7 Pipeline observability — the pipeline monitors itself

Consistent with Volume 1, Part IV, Section 29.1's "the platform observing itself" principle, extended here to development infrastructure: pipeline stage duration, failure rate per stage, and flake rate for the E2E suite specifically (a flaky E2E test undermines trust in the entire gate it belongs to) are themselves tracked metrics, feeding the same OpenTelemetry-based observability stack (Section 8, later in this Part) the running application uses — a pipeline that's slow or unreliable is treated as an infrastructure quality problem worth the same monitoring rigor as the production system it exists to protect.



6. GitHub Actions
6.1 Purpose — the concrete implementation of Section 5's pipeline

Section 5 specified the pipeline's stages and philosophy in tool-agnostic terms. This section specifies the actual GitHub Actions implementation — the technology choice implied by Volume 0, Section 21.14's reference to "GitHub Actions" as effectively locked, formally confirmed and detailed here.

6.2 Workflow structure — composite actions over duplicated YAML
yaml
# .github/workflows/pr-validation.yml (structure, not complete)
name: PR Validation
on: pull_request

jobs:
  lint:
    uses: ./.github/workflows/_reusable-lint.yml
  backend-tests:
    needs: lint
    uses: ./.github/workflows/_reusable-backend-tests.yml
  frontend-tests:
    needs: lint
    uses: ./.github/workflows/_reusable-frontend-tests.yml
  build-images:
    needs: [backend-tests, frontend-tests]
    uses: ./.github/workflows/_reusable-build-images.yml
  security-scan:
    needs: build-images
    uses: ./.github/workflows/_reusable-security-scan.yml
  deploy-ephemeral:
    needs: security-scan
    uses: ./.github/workflows/_reusable-deploy-ephemeral.yml
  e2e:
    needs: deploy-ephemeral
    uses: ./.github/workflows/_reusable-e2e.yml

Every stage from Section 5.2 is implemented as a reusable workflow (_reusable-*.yml), never duplicated inline across the several top-level workflow files that need it (pr-validation.yml, a separate main-branch-deploy.yml for the staging/production promotion path, a scheduled nightly-full-regression.yml running the complete Volume 2/Volume 3 evaluation suites beyond what every-PR feedback-speed budgets allow) — this is the CI-pipeline-level application of the same "define once, reuse everywhere" discipline this document set has applied consistently since Volume 2 (Sections 9.4, 10.6, 12.5 there).

6.3 backend-tests implementation detail
yaml
# _reusable-backend-tests.yml (excerpt)
jobs:
  test:
    runs-on: ubuntu-latest
    services:
      postgres: { image: postgres:16, ports: ["5432:5432"] }
      redis: { image: redis:7, ports: ["6379:6379"] }
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '8.0.x' }
      - name: Restore (cached)
        run: dotnet restore
      - name: Domain & Application tests (fast tier)
        run: dotnet test --filter "Category=Unit" --logger trx
      - name: Infrastructure & API integration tests
        run: dotnet test --filter "Category=Integration" --logger trx
        env:
          ConnectionStrings__Default: "Host=localhost;Database=nexus_test;..."

GitHub Actions' native services: block provisions ephemeral PostgreSQL/Redis containers scoped to this job's lifetime, directly serving Volume 1, Part VI, Section 40.6's requirement that integration tests run against real infrastructure, never mocked — the CI environment satisfies this without requiring a persistent shared test database that could develop cross-run state contamination.

6.4 Caching strategy

Dependency caching (actions/cache, keyed by packages.lock.json/package-lock.json hash) covers both dotnet restore and npm install outputs, directly complementing Section 2.2's Docker-layer caching (the two caching layers — CI dependency cache and Docker build cache — serve different parts of the pipeline and are both necessary, not redundant: dependency caching speeds up the test stages that run before any image is built at all). Cache invalidation is automatic and hash-based, never manually managed, avoiding the class of stale-cache bug that manually-keyed caching strategies are prone to.

6.5 Matrix builds — where they're used, and where they're deliberately not

A build matrix runs the frontend test suite (Volume 2, Section 19.2) across the minimum supported browser set for E2E purposes (since Volume 0, Section 21.4 locked Playwright specifically for its multi-browser support, and that capability should be exercised in CI, not just claimed architecturally) — but backend tests do not run a multi-OS or multi-.NET-version matrix, since Volume 0, Section 21.2 locked a single .NET version and Linux-container-only deployment target (Section 2.3's Alpine base images), making a broader compatibility matrix pure CI cost with no corresponding product requirement it would actually validate.

6.6 Secrets management in CI — Vault integration, never GitHub Secrets alone for production credentials

Directly extending Section 4.8's "no raw secret in a values file" discipline to the pipeline itself: CI-time secrets needed only within the pipeline's own ephemeral execution (a test database password scoped to that single job run, per 6.3) use native GitHub Actions encrypted secrets, appropriate for their narrow, ephemeral scope. Secrets needed for actual deployment (Section 5.2 stage 7's staging deployment, and production promotion) are retrieved at deploy-time directly from Section 15's Vault infrastructure via a short-lived, workflow-scoped OIDC token (GitHub's native OIDC federation with Vault, avoiding any long-lived Vault credential ever being stored as a GitHub secret at all) — this is a deliberate, stronger posture than simply storing a Vault token as a GitHub secret, since a short-lived, workflow-run-scoped credential minimizes the blast radius of any single compromised workflow run.

6.7 Required status checks and branch protection

The main branch requires every job in Section 6.2's pr-validation.yml to pass before merge is permitted (GitHub's native branch-protection required-status-checks feature), with no administrator override path enabled — directly enforcing that Section 5's "every stage automated, human judgment reserved only for production-promotion-timing" philosophy (Section 5.5) cannot be quietly bypassed under time pressure, which is exactly the scenario such a bypass exists to be tempting during and exactly the scenario this document set's repeated "fail loud, never silently skip a check" discipline (Volume 1, Architecture Principle 20.4, applied consistently through every subsequent volume) argues most strongly against permitting.

6.8 Workflow-run cost and the nightly/PR split

Consistent with Volume 2, Section 19.9's smoke/full E2E split and Volume 3, Section 16's cost-optimization discipline, applied here to CI compute cost specifically: every-PR workflows run Volume 2's E2E smoke subset and a representative (not exhaustive) slice of Volume 3's evaluation sets, budgeted to keep PR feedback latency within a target window (e.g., under 20 minutes total, directly serving developer inner-loop velocity per Section 2.9's build-time-budget reasoning extended to the full pipeline); the complete E2E suite (every Volume 0 use case, Section 11) and full evaluation-set regression run on a nightly schedule against the current main branch, catching anything the necessarily-narrower PR-time checks didn't, with any nightly failure treated as a release-blocking incident requiring immediate triage, not a routine report reviewed at leisure.

6.9 Closing note for this Part

Sections 5 and 6 together complete the pipeline that enforces every quality standard this document set has specified across four volumes — from Volume 1's coding-standard analyzers through Volume 3's AI evaluation gates to this Part's own infrastructure checks. The next Part (Observability Stack) specifies how the running system — not just the pipeline that ships it — is continuously monitored once deployed.


____________________________________________________________________________________________________________________________________________________




7. Monitoring
7.1 Purpose — the strategy layer above Sections 8–11's tooling

Volume 1, Part IV, Section 29 already established observability's three pillars (logs, metrics, traces) and Volume 0, Section 21.11 locked the OpenTelemetry/Prometheus/Grafana stack. This section specifies the monitoring strategy those tools serve — SLIs/SLOs derived directly from the NFRs this document set has specified since Volume 0, and the alerting/escalation philosophy governing how a threshold breach becomes human attention. Sections 8–11 then specify the concrete tooling implementing this strategy.

7.2 Service Level Indicators — derived, not invented

Every SLI this section defines is a direct restatement of an NFR already committed to in Volume 0 or Volume 1 — this section adds no new quality targets, only operationalizes existing ones as continuously-monitored indicators:

SLI Derived from  Measurement
API availability  NFR-REL-1 (99.9%) /health/live synthetic checks + real request success rate
Step-dispatch latency (p95) NFR-PERF-1 (50ms) nexus.step.dispatch.duration (Volume 1, Part IV §29.4)
Platform flake rate NFR-REL-2 (<0.5%) nexus.flow.run.flake_detected / nexus.flow.run.completed ratio (Volume 1, Part IV §29.5)
Concurrent execution headroom NFR-SCALE-1 (500) nexus.orchestration.concurrent_executions against configured capacity
Frontend Core Web Vitals  NFR-UX-2 (30-min onboarding, indirectly)  LCP/INP/CLS via RUM (Volume 2, Section 17.7)
AI request latency  NFR-PERF-4 (15s timeout)  nexus.ai.request.duration (Volume 1, Part IV §29.4)
Self-heal acceptance rate Volume 0 §6.2 (≥70%)  Volume 3, Section 10.9's computed metric
7.3 Service Level Objectives — the threshold that triggers action

Each SLI in 7.2 has a corresponding SLO — the specific threshold and time window that, when breached, constitutes an incident. SLOs are set with deliberate headroom below the NFR's own hard commitment (e.g., an internal SLO alerting at p95 dispatch latency exceeding 40ms, before NFR-PERF-1's 50ms commitment is actually violated) — directly extending Volume 1, Part IV, Section 29.6's "alert on symptom metrics before they become customer-visible" philosophy into a formal, numeric early-warning margin, so the team has time to respond before a customer-facing NFR is actually breached, not only after.

7.4 Error budgets

Each SLO (7.3) implies an error budget — the amount of acceptable deviation within a rolling window before it's treated as fully consumed. Consistent with Volume 1, Part VI, Section 38.9's "performance regression treated with the same severity as a functional bug" principle, an exhausted error budget for any SLO triggers a deliberate, visible policy: new feature deployment for the affected component pauses (not a hard technical block, but a required, explicit decision to proceed anyway, made visible to the same stakeholders Volume 0's risk register, Section 22, already involves) until the budget recovers or the underlying regression is fixed — directly operationalizing the tension between shipping velocity and reliability commitments as a conscious, tracked tradeoff rather than an implicit one.

7.5 Alerting severity and escalation
Critical  → pages on-call immediately (NFR-REL-1 breach in progress, security
             incident per Volume 3 §18.7, data-loss risk)
Warning   → notifies the responsible team's channel, next-business-day triage
             acceptable (SLO trending toward breach, per 7.3's early-warning margin)
Info      → dashboard-visible only, no notification (routine capacity/usage trends)

This three-tier severity model directly mirrors Volume 3, Section 17.7's guardrail-severity model (block/warn/flag-for-review) — the same "not every deviation deserves the same urgency" principle, applied here to operational alerting rather than AI guardrails, a deliberate consistency choice across this document set's various severity-tiering decisions (Volume 1, Part IV, Section 24.8's authentication-failure handling used a similar graduated-response philosophy).

7.6 On-call and ownership

Every SLI in 7.2's table has a named owning team (mirroring Volume 3, Section 19.7's per-metric ownership model exactly) — the Orchestration Engine's dispatch-latency SLI is owned by the backend team that owns Volume 1, Part III's execution engine; the AI-latency SLI is owned by the team owning Volume 3's Planning Engine; frontend Core Web Vitals are owned by the Volume 2 frontend team. A Critical alert (7.5) pages the specific owning team first, with a documented escalation path to a broader on-call rotation only if the primary owner doesn't acknowledge within a defined window — avoiding the common anti-pattern of a single, undifferentiated on-call rotation that lacks the specific context Volume 1 through Volume 3's deep, specialized architecture actually requires to diagnose quickly.

7.7 Runbooks — connecting an alert to a specific, known response

Every Critical and Warning alert links to a maintained runbook — for the platform flake-rate SLI specifically, for instance, a runbook referencing Volume 1, Part III, Section 19.5's retry/flake-handling design and Section 22's Event Bus health metrics, giving the responding engineer a starting investigation path grounded in this document set's own architecture rather than requiring them to rediscover it under incident pressure. Runbooks are versioned alongside the code they describe (mirroring Volume 1, Part VI, Section 40.7's documentation-as-code discipline) and are a required deliverable for any new SLI added to 7.2's table — an alert with no runbook is treated as an incomplete monitoring implementation, not shipped as-is.

7.8 Monitoring the monitoring — meta-observability

Directly extending Volume 4, Section 5.7's "the pipeline monitors itself" principle one layer further: the observability stack's own health (Prometheus scrape success rate, OpenTelemetry Collector queue depth, Grafana dashboard load latency — Sections 8–10's tooling) is itself monitored, with a dedicated, minimal, independently-hosted "is monitoring itself healthy" check that doesn't depend on the same infrastructure it's monitoring — avoiding the specific failure mode where an observability-stack outage is invisible precisely because the tool that would normally surface it is the thing that's down.



8. OpenTelemetry
8.1 Purpose — the unified collection layer

Volume 1, Part IV, Section 29.2 committed to OpenTelemetry as the unified instrumentation layer for logs, metrics, and traces. This section specifies the Collector architecture that actually receives, processes, and routes that telemetry — the infrastructure component sitting between every instrumented source (Volume 1's backend, Volume 2's frontend RUM, Volume 4's own CI pipeline) and the storage backends Sections 9–11 specify.

8.2 Collector architecture — gateway pattern
┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ nexus-api      │  │ nexus-        │  │ nexus-gui       │  │ CI pipeline    │
│ (Volume 1)     │  │ orchestration  │  │ (Volume 2, RUM) │  │ (Volume 4 §5.7)│
└──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘
       │ OTLP/gRPC        │ OTLP/gRPC        │ OTLP/HTTP        │ OTLP/HTTP
       └──────────────────┴─────────┬───────┴──────────────────┘
                                     ▼
                        ┌─────────────────────────┐
                        │  OTel Collector (Gateway)  │
                        │  - Batch processor          │
                        │  - Tail sampling (8.5)       │
                        │  - PII/secret scrubbing (8.6)│
                        │  - Resource attribution       │
                        └───┬──────────┬──────────┬───┘
                            │          │          │
                     traces │  metrics │      logs │
                            ▼          ▼          ▼
                     [Section 9:  [Section 9:  [Section 11:
                      trace store  Prometheus]  Elasticsearch]
                      or Prometheus-
                      exemplar linked]

Every application component (Volume 1's backend, Volume 2's frontend, and — per Section 5.7 of this volume — the CI pipeline itself) exports via the standard OTLP protocol to this single Collector gateway, rather than each source shipping directly to Prometheus/Elasticsearch independently — this gateway pattern is what allows Section 8.5's sampling and Section 8.6's scrubbing to apply uniformly, exactly once, regardless of which of the many instrumented sources produced a given piece of telemetry.

8.3 Instrumentation libraries — restating and completing Volume 1's setup

Directly implementing the AddOpenTelemetry() configuration Volume 1, Part IV, Section 29.2 sketched: the ASP.NET Core and Entity Framework Core auto-instrumentation packages cover the majority of nexus-api's baseline tracing (HTTP request spans, database query spans) automatically; Volume 1, Part I, Section 5's custom ActivitySource ("Nexus.Orchestration") and Meter provide the domain-specific spans and metrics (per-FlowRun trace structure, per Volume 1, Part IV, Section 29.3) that no auto-instrumentation could produce, since they require semantic knowledge of Nexus's own domain model that a generic library instrumentation cannot have.

8.4 Frontend RUM ingestion

Volume 2, Section 17.7 specified that Core Web Vitals are "exported through the same OpenTelemetry pipeline Volume 1... established" — this section specifies the mechanism: the browser-based OpenTelemetry JS SDK exports via OTLP/HTTP (browsers cannot use gRPC directly) to this same Collector gateway (8.2), with a dedicated, rate-limited ingestion path distinct from the backend's OTLP/gRPC path, since browser-originated telemetry has a fundamentally different trust and volume profile (many more, smaller, less-trusted sources) than server-originated telemetry — the Collector's configuration treats these as two distinct pipelines converging on the same processing stages (8.2's batch/sampling/scrubbing), not one undifferentiated input.

8.5 Sampling strategy

Full-fidelity trace retention for every request is neither necessary nor economical at scale — a tail-based sampling policy (sampling decisions made after a trace completes, based on its actual characteristics) retains 100% of traces exhibiting error status, unusually high latency (beyond the SLO thresholds Section 7.3 defines), or any AI-capability involvement (Volume 3's capability engines, given their standing evaluation and forensic-trail value per Volume 3, Section 18.7), while sampling routine, fast, successful traces at a lower rate (e.g., 5-10%) — directly consistent with Volume 1, Part IV, Section 29.5's flake-rate computation needing complete data (flake-relevant traces are, by definition, non-routine and therefore always retained under this policy) while avoiding the cost of exhaustively storing every uneventful, successful request trace.

8.6 PII and secret scrubbing at the Collector

Directly extending Volume 1, Part IV, Section 28.5's "never logged, even at Trace level" denylist and Volume 3, Section 17.3's input-stage PII detection to the telemetry-collection layer specifically: the Collector applies a scrubbing processor (pattern-matching the same categories Volume 1, Section 28.5 and Volume 3, Section 17.3 already define) to every span attribute and log record before export to any storage backend — a defense-in-depth layer catching anything that might have slipped past application-level discipline, directly consistent with Volume 1, Part IV, Section 26.3's "defense in depth, not a single checkpoint" philosophy, now applied to telemetry scrubbing specifically as a third enforcement point beyond the two application-level ones already specified.

8.7 Resource attribution and multi-tenancy

Every span, metric, and log record is tagged with resource attributes including tenant_id where applicable (Volume 1, Part IV, Section 29.8's tenant-scoped observability requirement), service.name (distinguishing nexus-api from nexus-orchestration from nexus-sandbox-runner), and deployment.environment (local/staging/production, Section 5.3) — this consistent tagging is what makes Section 9's Prometheus queries and Section 10's Grafana dashboards able to filter and aggregate correctly across every one of this document set's cross-cutting dimensions without each dashboard needing bespoke, inconsistent labeling logic.

8.8 Collector deployment topology

The Collector gateway runs as its own Kubernetes Deployment within the nexus-observability namespace (Section 3.2), horizontally scaled independently of nexus-api/nexus-orchestration — a Collector outage or backlog must never block or slow the primary application (telemetry export is fire-and-forget from the application's perspective, buffered and retried by the OTel SDK's own export queue, never a synchronous dependency of request handling), directly consistent with Volume 1, Part I, Section 4.5's original design note that "a telemetry-export failure must never cause a flow-execution failure."

8.9 Relationship to Sections 9–11

This section specifies collection and routing; Sections 9 (Prometheus), 10 (Grafana), and 11 (Elasticsearch) specify what happens to the three telemetry types after this Collector routes them to their respective storage and visualization backends.



9. Prometheus
9.1 Purpose — metrics storage and the HPA feedback loop

This section specifies Prometheus's role as the metrics-storage backend receiving the Collector's metric export (Section 8.2), and — critically, since this is the one piece of the observability stack that feeds back into the running system's own behavior, not just human-facing dashboards — the custom-metrics-to-HPA pipeline Volume 1, Part VI, Section 37.4 and Volume 4, Section 3.7 both already referenced.

9.2 Metrics ingestion — remote-write from the Collector, not direct scraping

Rather than Prometheus directly scraping each application pod (the more traditional Prometheus pattern), the OpenTelemetry Collector (Section 8.2) exports metrics to Prometheus via remote-write — this is a deliberate choice consistent with Section 8.2's gateway architecture: every metric passes through the Collector's uniform processing (batching, resource attribution per Section 8.7) before reaching storage, rather than Prometheus scraping raw, unprocessed metrics directly from application pods and bypassing that processing layer entirely.

9.3 Retention and downsampling

Raw-resolution metrics are retained for 15 days (sufficient for the incident-investigation and SLO-tracking windows Section 7.3–7.4 specify), with downsampled (5-minute-aggregate) data retained for 13 months — directly enabling Volume 0's Success Metrics (Part I, Section 6) to be tracked as genuine year-over-year trends, not just recent-window snapshots, while keeping raw-resolution storage cost bounded. This tiered retention directly mirrors the same short-hot/long-cold pattern Volume 1, Part IV, Section 28.6 established for operational logs, applied here to metrics for the analogous cost/utility reasoning.

9.4 The custom-metrics-to-HPA pipeline — completing Volume 1 and Volume 4's forward references
nexus-orchestration → OTel SDK → Collector (Section 8) → Prometheus (remote-write)
                                                                    │
                                                                    ▼
                                                        prometheus-adapter
                                                     (implements Kubernetes
                                                      custom.metrics.k8s.io API)
                                                                    │
                                                                    ▼
                                                        HPA controller reads
                                                        nexus.orchestration.
                                                        concurrent_executions
                                                                    │
                                                                    ▼
                                                        scales nexus-orchestration
                                                        Deployment replica count

This is the concrete, final implementation of the mechanism Volume 1, Part VI, Section 37.4 named ("a PrometheusAdapter... exposes that Prometheus-collected metric to Kubernetes' HPA controller") and Volume 4, Section 3.7 restated — prometheus-adapter's configuration maps the specific nexus.orchestration.concurrent_executions metric (Volume 1, Part IV, Section 29.4) to the Kubernetes custom-metrics API surface the HPA controller queries, with the scale-up/down thresholds and cooldown windows Section 3.7 already flagged as needing tuning to avoid flapping specified as adapter configuration, not application code — keeping this autoscaling policy's tuning fully within infrastructure-team ownership (Section 7.6's ownership model), separate from the Volume 1 backend team's application-code concerns.

9.5 Recording rules — pre-computing the SLIs

Every SLI in Section 7.2's table that requires a computed ratio (platform flake rate — a ratio of two counters, per Volume 1, Part IV, Section 29.5) is defined as a Prometheus recording rule, pre-computing the ratio on a regular interval rather than requiring every dashboard query (Section 10) or alert rule (Section 7.5) to independently recompute it from raw counters — this is both a query-performance optimization and a correctness safeguard: the flake-rate formula is defined exactly once, as a recording rule, and every consumer (dashboards, alerts) references that single, authoritative computed series, eliminating the risk of two different dashboards computing "flake rate" with subtly different, drifted formulas.

9.6 Alert rules and Alertmanager

Prometheus's rule evaluation engine, paired with Alertmanager for routing and deduplication, implements Section 7.5's three-tier severity model directly — a Critical-severity alert rule (e.g., API availability SLO breach) routes to the paging integration for the owning team (Section 7.6); Warning-severity rules route to team chat channels; Info-level conditions are deliberately not alert rules at all, remaining dashboard-only (Section 10) per Section 7.5's explicit "no notification" design for that tier. Alertmanager's grouping and inhibition rules prevent alert storms during a genuine incident (e.g., suppressing redundant per-pod alerts when the underlying cause is a single, already-alerted cluster-level issue) — a specific, deliberate mechanism protecting the on-call escalation model (Section 7.6) from being undermined by notification noise during exactly the moments focus matters most.

9.7 Multi-cluster considerations — federation, scoped to actual need

For the dedicated-instance deployment model (Volume 1, Part IV, Section 26.6), each customer's cluster runs its own independent Prometheus instance, with no cross-cluster federation to any Nexus-operated central instance by default — directly consistent with that deployment model's stronger isolation promise and Architecture Principle 20.8's self-hosted-first posture; a dedicated-instance customer's metrics remain within their own infrastructure boundary exactly as their data does (Volume 1, Part IV, Section 26.6). For Nexus's own managed/hosted pooled deployment (Volume 1, Part IV, Section 26.2), a limited, specifically-scoped federation aggregates only the platform-health metrics Volume 3, Section 19.6 already named as Nexus-team-facing (never tenant-specific data) into a central operational view — federation is applied narrowly, for a named purpose, not as a default "aggregate everything centrally" posture.

9.8 Query performance and cardinality management

High-cardinality labels (a raw flow_run_id as a metric label, for instance) are explicitly avoided in Prometheus metric definitions — Volume 1, Part IV, Section 29's custom metrics (nexus.step.dispatch.duration, etc.) are labeled only by bounded-cardinality dimensions (ModuleKind, RunStatus, TenantId for aggregate reporting per Section 9.7's isolation note), with per-run detail available instead through Section 8.5's trace data, where fine-grained, high-cardinality identification is the entire point and traces are architecturally suited to carrying it — a deliberate division of labor between metrics (aggregate, low-cardinality, cheap to query broadly) and traces (individual, high-cardinality, queried by specific ID) that this section maintains discipline around rather than letting metrics cardinality creep upward over time as new dimensions seem convenient to add.



10. Grafana
10.1 Purpose — the visualization layer, internal and tenant-facing

This section specifies Grafana's dashboard architecture — both the internal, Nexus-team-facing operational dashboards implementing Section 7's SLI/SLO monitoring, and the narrower tenant-facing views this document set has referenced at several points (Volume 3, Section 16.8's cost visibility, Section 19.6's quality visibility) without yet specifying their common technical foundation.

10.2 Dashboard-as-code

Every dashboard is defined as versioned JSON (Grafana's native dashboard-as-code format) stored in the same repository as the application code it monitors, provisioned automatically on deployment (Section 4's Helm chart includes a Grafana provisioning subchart) rather than manually clicked-together in the Grafana UI and never version-controlled — directly extending Volume 1, Part VI, Section 40.7's documentation-as-code discipline to observability dashboards specifically: a dashboard that visualizes a specific metric is treated as a first-class artifact tied to that metric's definition, reviewed via the same pull-request process as the metric-emitting code itself, never a separate, informally-maintained side artifact that can silently drift out of sync with what it's supposed to represent.

10.3 Dashboard inventory — internal
Dashboard Audience  Content
Platform Health On-call, all engineering teams  Section 7.2's full SLI table, current SLO status (7.3), error-budget consumption (7.4)
Orchestration Engine Deep-Dive  Backend team (Volume 1, Part III owners)  Step-dispatch latency breakdown by module (Volume 1, Part IV §29.4), concurrent-execution headroom, outbox relay lag
AI Capability Health  AI team (Volume 3 owners) Per-capability latency and evaluation metrics (Volume 3, Section 19.6's dashboard, technically hosted here), guardrail trigger rates (Volume 3, Section 17.2)
Frontend RUM  Frontend team (Volume 2 owners) Core Web Vitals trends (Volume 2, Section 17.7), bundle-size trend over releases
CI/CD Pipeline Health All teams Stage duration and failure rate (Section 5.7), E2E flake rate
Cost Overview Engineering leadership  Infrastructure cost (Section 21) alongside AI provider cost (Volume 3, Section 16.2), correlated
10.4 Dashboard inventory — tenant-facing (enterprise tier)

Directly implementing Volume 3, Section 16.8 and Section 19.6's previously-named-but-unspecified tenant-facing views: a scoped Grafana instance (or Grafana's native organization/folder-permission isolation, configured per-tenant) exposes exactly two dashboards to enterprise-tier tenants — AI Cost (Volume 3, Section 16.8) and AI Quality (Volume 3, Section 19.6) — both filtered, at the data-source query level, to that tenant's own TenantId exclusively, using the identical tenant-scoped-query enforcement Volume 1, Part IV, Section 26.3 established for the application's own data layer, now applied to the observability layer for the first time with a genuinely tenant-facing (not just internal) consumer. This is the concrete technical mechanism closing the gap those two Volume 3 sections named but left unspecified — resolved here, in this volume, exactly where the rest of this document set's infrastructure specification lives.

10.5 Grafana Alerting vs. Prometheus Alertmanager — division of responsibility

Consistent with Section 9.6's Alertmanager-based routing already specified: Grafana's own native alerting feature is used narrowly, only for alert conditions that are more naturally expressed against a dashboard visualization than a raw Prometheus query (a visual anomaly-detection rule against the Frontend RUM dashboard's trend shape, for instance) — the primary, authoritative alerting path for every SLI in Section 7.2's table remains Prometheus rules plus Alertmanager (Section 9.6), avoiding two independent, potentially-inconsistent alerting systems both claiming ownership of the same underlying SLOs.

10.6 Access control

Internal dashboards (10.3) are gated by the observability stack's own authentication (SSO-integrated, reusing the same OIDC provider integration Volume 1, Part IV, Section 24.7 established for the application itself, rather than a separate credential set engineers must manage) with role-based folder permissions matching Section 7.6's per-SLI ownership model — a team's dashboards are editable by that team, viewable platform-wide. Tenant-facing dashboards (10.4) are embedded within Volume 2's reporting-audit feature module (Volume 2, Section 6.5) via Grafana's embedding/iframe capability, authenticated through the same session Volume 2, Section 14 already establishes for the rest of the application — a tenant user never has separate Grafana credentials to manage, consistent with UX Principle 1.2's "one consistent, real state" applied to authentication specifically.

10.7 Dashboard performance and the query-cost discipline

Every dashboard panel's underlying query is reviewed against Section 9.8's cardinality-management discipline before merge (10.2's PR-review process) — a dashboard panel that would require a high-cardinality, expensive Prometheus query is redesigned to query a recording rule (Section 9.5) instead, keeping dashboard load time itself within a reasonable budget (directly relevant to Section 7.8's meta-observability concern: a slow-loading Platform Health dashboard during an active incident is itself a monitoring-system failure worth preventing proactively).

10.8 Annotations — correlating deployments with metric changes

Every production deployment (Section 5.2, stage 9) automatically creates a Grafana annotation marking that exact timestamp across every dashboard — directly serving incident investigation and the general "did this recent change cause this metric shift" question that's otherwise surprisingly hard to answer precisely without this correlation being automatic rather than requiring an engineer to manually cross-reference a deployment log against a metric timestamp by hand.



11. Elasticsearch
11.1 Purpose and precise scope — operational logs only

A scope clarification worth stating immediately, given how many times this document set has had to disambiguate the word "log": this section covers storage for Volume 1, Part IV, Section 28's operational logging exclusively — the diagnostic output the Nexus application produces about its own behavior. It has no relationship to the customer-facing Logs module (Volume 0, Section 14.8; Volume 1, Part III, Section 18), which reads a target system's logs as a product capability, and no relationship to Volume 1, Part IV, Section 27's Audit trail, which is stored in PostgreSQL's audit_entries table specifically because it needs the immutability and access-control rigor Volume 1, Part IV, Section 27.5 specified — a rigor level Elasticsearch's typical operational-log usage pattern doesn't need and Volume 1, Part IV, Section 27.7 explicitly required staying architecturally separate from.

11.2 Log shipping — from the Collector, not a separate agent

Operational logs reach Elasticsearch via the same OpenTelemetry Collector gateway (Section 8.2) already routing traces and metrics — the Collector's log-export pipeline (a standard OTel Collector exporter for Elasticsearch) is used rather than deploying a separate log-shipping agent (e.g., Filebeat) alongside it, keeping Section 8.6's PII/secret scrubbing and Section 8.7's resource attribution (correlation IDs, tenant_id, service.name) applied uniformly to logs exactly as they are to traces and metrics, rather than requiring a second, independently-configured scrubbing and tagging layer that could drift out of sync with the primary one.

11.3 Index strategy

Indices are time-based (daily rolling indices, nexus-logs-YYYY.MM.DD), with an Index Lifecycle Management (ILM) policy directly implementing the exact retention tiers Volume 1, Part IV, Section 28.6 already specified: full-fidelity, fast-query "hot" tier for 14 days, a downsampled/lower-resources "warm" tier for the remaining balance of a 90-day window, then deletion — this section is the concrete infrastructure realization of a policy Volume 1 named as a principle ("a tiered approach... reflecting that operational logs exist to support near-term debugging... not long-term compliance record-keeping") without specifying the mechanism, closed here exactly as this volume has closed several other Volume 1 forward references (Section 2.7's air-gapped registry mirroring, Section 4.7's install command).

11.4 Correlation with traces — the unified investigation experience

Every log record carries the same CorrelationId/FlowRunId scope Volume 1, Part IV, Section 28.4 established via ILogger.BeginScope — indexed as a searchable field in Elasticsearch, and, critically, the same identifier is attached as a span attribute on the corresponding OpenTelemetry trace (Section 8.3) reaching the trace backend. This is what makes Grafana (Section 10) able to offer trace-to-logs correlation natively — an engineer viewing a slow or failed trace in the Orchestration Engine Deep-Dive dashboard (Section 10.3) can pivot directly to the exact log lines sharing that trace's FlowRunId, without manually re-entering an identifier into a separate search interface — directly realizing the investigative workflow Volume 1, Part IV, Section 28.4 described in principle ("reconstruct exactly what happened") as an actual, one-click Grafana feature.

11.5 Index sizing and cardinality

Given operational logging's inherently higher volume than metrics (Section 9.8's cardinality discipline doesn't directly apply to log records the same way, since Elasticsearch is architecturally suited to high-cardinality, full-text-searchable data in a way Prometheus is not), index sharding is sized based on observed daily volume per environment (production sized meaningfully larger than staging, per Section 5.3's environment tiers), with shard count reviewed periodically against actual growth rather than fixed permanently at initial deployment — mirroring Volume 4, Section 3.3's "sized with headroom, validated periodically against actual observed usage" discipline, applied here to log storage specifically.

11.6 Query access and Kibana

Kibana (Elasticsearch's native query/visualization UI) is available to engineers for ad hoc log investigation beyond what Grafana's trace-to-logs pivot (11.4) covers — access-gated identically to Section 10.6's internal-dashboard SSO integration, with query access scoped by the same per-team ownership model (Section 7.6) where relevant (a team can freely query logs from services they own; cross-team log access for incident investigation follows the same escalation path Section 7.6 already established for alert ownership, not a separate, independently-designed access policy).

11.7 Self-hosted deployment — bundled or external, mirroring Section 4.4's pattern

Directly extending Section 4.4's conditional-subchart pattern (already established for PostgreSQL and Redis) to Elasticsearch: a self-hosted, resource-constrained deployment can enable a bundled, single-node Elasticsearch subchart for a genuinely complete, self-contained installation, while a larger self-hosted or managed deployment points at an external, properly-clustered Elasticsearch service — the same values-file-driven choice (Volume 4, Section 4.3–4.4) already established for the other stateful dependencies this platform relies on, applied consistently here rather than introducing a differently-patterned configuration approach just because this is a different underlying technology.

11.8 Air-gapped deployment considerations

For air-gapped self-hosted deployments (Volume 1, Part VI, Section 37.7; Volume 4, Section 2.7's image-mirroring procedure), Elasticsearch's own container images are included in the same mirroring procedure as every other component this document set has specified — no operational-logging capability is lost in an air-gapped deployment, since (unlike Volume 3's AI capabilities, which are the one deliberately, permanently AI-provider-dependent piece of this platform per Volume 1's NFR-SEC-4) operational logging has no external-network dependency of any kind once the images themselves are mirrored, consistent with Architecture Principle 20.8's insistence that the self-hosted, air-gapped path be a fully-capable deployment target, not a degraded one.

11.9 Closing note for this Part

This section closes Part III's observability specification: Section 7 established the strategy (SLIs, SLOs, alerting philosophy), Section 8 established unified collection, and Sections 9–11 specified the three storage/visualization backends that strategy and collection layer feed. Part IV (Data & Secrets) now turns to the data layer's own infrastructure specifics — PostgreSQL and Redis operational detail, and the secrets/credential infrastructure (Vault, TLS) this Part's own OTel Collector, Prometheus, and Elasticsearch deployments all themselves depend on for their own credentials, closing a dependency loop this Part deliberately left to the next.


____________________________________________________________________________________________________________________________________________________



12. Redis
12.1 Purpose — operationalizing three distinct roles

Volume 1, Part VI, Section 38.7 already named three distinct roles Redis serves — permission/RoleVersion cache, rate-limiting counters, and cross-worker execution coordination — plus Volume 1, Part V, Section 31.5's SignalR backplane role. This section specifies the operational deployment supporting all four roles, including where their genuinely different consistency and durability needs lead to different configuration choices rather than one-size-fits-all defaults.

12.2 Deployment topology — Redis Sentinel for HA

Redis is deployed with Sentinel-managed replication (a primary plus at least two replicas, with Sentinel processes monitoring and orchestrating automatic failover) rather than a single, unreplicated instance — directly serving NFR-REL-1's 99.9% availability commitment, since Volume 1, Part IV, Section 24.6's RoleVersion cache and Section 31.5's SignalR backplane are both load-bearing, active dependencies whose unavailability would degrade the running platform, not merely a convenience layer that could tolerate a single point of failure. Redis Cluster (sharded, as opposed to Sentinel's replicated-single-primary model) is explicitly not used for GA — Nexus's Redis working set (session/permission cache, rate-limit counters, backplane state) does not approach the scale where sharding's added operational complexity would be justified, consistent with this document set's repeated "add complexity only where it earns its cost" discipline (Volume 1, Architecture Principle 20.7).

12.3 Persistence configuration — differentiated by role

A single Redis deployment serves all four roles (12.1), but persistence configuration is chosen for the role with the strictest requirement, since Redis's persistence setting is deployment-wide: AOF (Append-Only File) persistence with everysec fsync policy is enabled, favoring durability over the small latency cost, because the RoleVersion permission cache (Volume 1, Part IV, Section 24.6) losing state on a crash would incorrectly allow a just-revoked permission to appear valid until the next natural cache-invalidation event — an acceptable risk for pure ephemeral data (rate-limit counters, SignalR backplane state, both fully self-healing after a restart with no correctness impact) but not for the permission-cache role specifically. This is a deliberate, single, documented tradeoff — accepting AOF's modest overhead platform-wide to correctly serve the one role that needs it, rather than attempting a more complex per-key persistence policy Redis doesn't natively support well.

12.4 Memory eviction policy

noeviction is configured rather than an LRU-based eviction policy — Redis reaching its configured memory limit should surface as an explicit, alertable error (Section 7.5's Critical severity) rather than silently evicting keys, since silent eviction of, for instance, in-flight SignalR backplane coordination state (Volume 1, Part V, Section 31.5) would produce confusing, hard-to-diagnose downstream symptoms (a client mysteriously missing live-progress events) rather than a clear, actionable capacity alert. This is a direct, deliberate application of Architecture Principle 20.4 (fail loud, fail informatively) to a memory-management policy specifically — Redis running out of memory is treated as an infrastructure-capacity incident to be alerted and resolved, never as a condition the system should silently paper over by discarding data.

12.5 Connection pooling and multiplexing

Both nexus-api and nexus-orchestration use a multiplexed Redis client connection (a single, long-lived TCP connection handling many concurrent logical requests, standard for the .NET StackExchange.Redis client Volume 1's stack implies) rather than a traditional connection-pool-of-many-connections pattern — directly consistent with Volume 2, Section 15.5's frontend-facing note that "Redis client-side connection multiplexing is sufficient... not requiring a pool sized to 500," now specified as the backend's own connection strategy for the same underlying reason: Redis's single-threaded command-processing model makes connection multiplexing, not connection-count scaling, the correct approach to handling NFR-SCALE-1's concurrency target.

12.6 Rate-limiting counter implementation

Directly implementing Volume 1, Part IV, Section 24.9 and Volume 1, Part V, Section 30.7's rate-limiting requirements: sliding-window counters are implemented via Redis sorted sets (a standard, well-understood pattern — timestamped entries with periodic trimming) rather than simple fixed-window counters, avoiding the boundary-edge burst-traffic problem fixed windows are prone to, keeping the rate-limiting behavior's actual enforcement precision matching what those two Volume 1 sections' stated intent implies, not merely an approximation.

12.7 SignalR backplane configuration

Directly completing Volume 1, Part V, Section 31.5's deployment note: the backplane uses Redis Pub/Sub (not Redis Streams, which would offer replay capability the backplane's design explicitly doesn't need, per Volume 2, Section 15.4's client-side reconciliation already handling the "what did I miss" problem at the REST-catch-up layer rather than expecting the transport layer to solve it) — a deliberately simple choice matching Volume 1, Part V, Section 31.7's narrow scope for this hub ("progress of a run the client has explicitly subscribed to," nothing more).

12.8 Monitoring Redis itself

Redis exports its own operational metrics (memory usage, connected clients, command latency, replication lag between primary and Sentinel-monitored replicas) through the standard Redis Exporter, ingested via Section 8's OpenTelemetry Collector pipeline alongside every other platform metric — Redis health is a named row in the Platform Health dashboard (Section 10.3), with replication lag specifically alerted on at Warning severity (Section 7.5) since sustained lag threatens the failover guarantee 12.2's Sentinel configuration exists to provide.

12.9 Self-hosted and dedicated-instance deployment

Following the same conditional-subchart pattern already established for PostgreSQL (Section 4.4) and Elasticsearch (Section 11.7): a self-hosted deployment can enable a bundled Redis-with-Sentinel subchart for a complete, self-contained installation, or point at an external managed Redis service — the dedicated-instance deployment model (Volume 1, Part IV, Section 26.6) always runs its own isolated Redis instance, never shared across tenants, consistent with that model's stronger-isolation promise extended to every stateful dependency this platform has, not just the primary database.



13. PostgreSQL
13.1 Purpose — operationalizing Volume 1's schema and scaling design

Volume 1, Part V, Section 32 specified PostgreSQL's schema in full; Volume 1, Part VI, Section 39.3 and 39.6 named connection pooling and future partitioning as scaling mechanisms without fully specifying their operational deployment. This section closes those references and specifies the complete operational posture: high availability, connection management, and maintenance.

13.2 High availability — streaming replication with automated failover

PostgreSQL runs with at least one synchronous streaming replica (ensuring zero data loss on primary failover, at the cost of slightly higher write latency — an accepted tradeoff given NFR-SEC-3's audit-data durability requirement and Volume 1, Part III, Section 16.7's per-step-commit durability guarantee both depending on writes genuinely being durable, not just fast) plus additional asynchronous replicas for read-scaling where warranted. Automated failover is managed by a dedicated PostgreSQL HA controller (Patroni, the standard choice for this pattern) coordinating leader election through the same distributed-consensus mechanism (etcd, or Kubernetes-native equivalents) rather than a custom-built failover script — directly serving NFR-REL-1's 99.9% availability commitment for the component every other piece of this platform ultimately depends on.

13.3 Connection pooling — PgBouncer, closing Volume 1's forward reference

Directly implementing Volume 1, Part VI, Section 39.3's named-but-undetailed connection pooler: PgBouncer runs in transaction-pooling mode (not session-pooling), sitting between every application connection (nexus-api, nexus-orchestration, and critically, Section 16.7's IUnitOfWorkFactory-created short-lived contexts, Volume 1, Part III) and PostgreSQL itself — transaction-pooling mode is specifically chosen because Volume 1, Part III, Section 16.7's incremental per-step commit design creates many short-lived transactions per FlowRun rather than one long-lived one, a connection-usage pattern transaction-pooling mode is architecturally suited to (returning a connection to the pool the instant a transaction commits, not holding it for a client's full session), directly serving NFR-SCALE-1's 500-concurrent-execution target without requiring 500 concurrent raw PostgreSQL connections, which would exceed a reasonably-sized PostgreSQL instance's own connection ceiling.

13.4 Row-level security performance considerations

Volume 1, Part IV, Section 26.3's row-level security policies (enforced on every multi-tenant table) are indexed to ensure the RLS predicate itself (tenant_id = current_setting('app.current_tenant_id')) resolves via the same composite indexes Volume 1, Part V, Section 32.6 already specified with tenant_id as the leading column — this section adds the operational verification: RLS policy performance is included in Section 5.2's staging-verification load-testing stage specifically, since an RLS predicate that silently forces a sequential scan (if an index doesn't actually cover it the way intended) would be a serious, easy-to-miss performance regression given RLS applies to literally every query against a protected table.

13.5 Partitioning — implementing Volume 1's deferred mechanism

Volume 1, Part VI, Section 39.6 named table partitioning (flow_runs, step_results, partitioned by started_at time range) as the mechanism for scaling toward NFR-SCALE-3's 10-million-row target, explicitly deferred as "when needed, not by default at GA." This section specifies the concrete trigger and implementation: partitioning is activated (via pg_partman or an equivalent automated partition-management extension, rather than hand-rolled partition-creation scripts) when a tenant's flow_runs table crosses a defined row-count threshold (e.g., 5 million rows, chosen with headroom below the 10-million target) — monitored as a named metric (Section 9) feeding a Warning-severity alert (Section 7.5) well before the threshold is reached, giving the team time to activate partitioning deliberately rather than reactively under pressure. Because Volume 1, Part V, Section 32.6 already confirmed the schema was "kept compatible with a future move to partitioning without requiring a schema redesign," this activation is purely an operational/DBA action at the point it's needed, not a code or schema change.

13.6 Vacuum and maintenance

Autovacuum is tuned more aggressively than PostgreSQL's conservative defaults specifically for the step_results and audit_entries tables (Volume 1, Part V, Section 32.3, 32.5) — both are high-insert-volume, append-heavy tables where default autovacuum settings would let table and index bloat accumulate meaningfully given this platform's expected write patterns (every flow-run step, per Volume 1, Part III, Section 16.7's per-step commit design, is an insert). Tuned autovacuum thresholds are reviewed as part of the same periodic capacity-validation discipline Section 3.3 already established for Kubernetes resource sizing, not set once at initial deployment and forgotten.

13.7 Backup integration — forward reference

Full backup strategy (frequency, retention, restore-testing procedure) is specified in this Part's Section 20 (Backup Strategy) — this section notes only the mechanism PostgreSQL-specifically supports: continuous WAL (Write-Ahead Log) archiving alongside periodic base backups, enabling point-in-time recovery to any moment within the retention window, not merely restoration to the most recent full-backup snapshot — the specific capability Section 20's disaster-recovery objectives (Section 17) depend on PostgreSQL's own backup mechanism providing.

13.8 Migration execution — restating Volume 1's gate with infrastructure detail

Directly completing Volume 1, Part VI, Section 37.5's migration-gating design: the Kubernetes Job running EF Core migrations (Volume 1, Part V, Section 32.8) connects directly to PostgreSQL's primary, bypassing PgBouncer's transaction-pooling mode (which is incompatible with some DDL operations' session-level requirements) via a dedicated, narrowly-scoped migration connection string — a specific, easy-to-overlook operational detail included here precisely because it's the kind of thing that works fine in casual local testing (Section 1's docker-compose setup, which doesn't include PgBouncer at all) and only surfaces as a problem against the full production topology, exactly the class of environment-parity gap Section 1.1's dev/prod parity principle exists to minimize, with this specific, documented exception flagged rather than silently discovered later.

13.9 Self-hosted and dedicated-instance deployment

Following the same conditional pattern established throughout this Part (Sections 4.4, 11.7, 12.9): a resource-constrained self-hosted deployment can run a bundled, single-instance PostgreSQL (accepting reduced availability guarantees, explicitly documented as a tradeoff for that deployment profile, consistent with Architecture Principle 20.8's "self-hosted is first-class, not assumed to always match managed-hosting's full HA posture") or point at an existing, already-HA-configured customer database — the dedicated-instance model always provisions its own full Patroni-managed HA cluster (13.2), matching that model's stronger commitment.



14. Secrets
14.1 Purpose — the general architecture, before Section 15's specific tool

This section specifies secrets-management architecture at the conceptual level — what counts as a secret, how ICredentialResolver (Volume 1, Part III, Section 18.4) is actually backed, and the rotation discipline governing every credential this platform touches. Section 15 then specifies HashiCorp Vault as the concrete implementation of the architecture this section defines.

14.2 What counts as a secret — an exhaustive classification
Secret category Examples  Consumer
Platform infrastructure credentials PostgreSQL password, Redis auth, Elasticsearch credentials  Application startup configuration (Section 4.8 — never in Helm values directly)
Tenant-supplied target-system credentials Credentials a flow author wires into a FlowStep to authenticate against the system under test ICredentialResolver, scoped per Volume 1, Part III, Section 18.4's least-privilege design
AI provider API keys  Volume 3, Part IV, Section 14.8's provider credentials  AiProvider implementations, resolved identically to module credentials
TLS private keys  Ingress and internal mTLS certificates  Section 16 (TLS) — a distinct lifecycle from other secret types, detailed there
Signing/encryption keys JWT signing key (Volume 1, Part IV §24.5), envelope-encryption data-encryption keys (14.5)  Application runtime, never exposed outside the resolving process's memory

This table is the complete enumeration this volume works from — any new credential type introduced by a future feature must be classified into one of these rows (or justify a new row, reviewed with the same rigor Section 4.9 already established for security-sensitive chart changes) before implementation, never handled ad hoc outside this classification.

14.3 ICredentialResolver — the backend Volume 1 deferred to this volume

Volume 1, Part III, Section 18.4 specified ICredentialResolver's interface and scoping behavior (narrow, per-flow-step, least-privilege) without specifying its backing implementation — deliberately, since that's an infrastructure concern belonging to this volume. The implementation resolves a credential reference (never a raw value, per Volume 1, Part II, Section 13's RotateCredentialCommand design already establishing this pattern) against Section 15's Vault instance at the moment a flow step actually executes, not earlier — a credential value is fetched just-in-time for use and never cached beyond the single step's execution scope, directly minimizing the window during which a resolved secret value exists in application memory at all.

14.4 Envelope encryption — implementing NFR-SEC-2

Volume 0's NFR-SEC-2 required "envelope encryption at rest, with the encryption key managed outside the primary application database." This section specifies the mechanism: tenant-supplied credentials (14.2's second row) are encrypted with a per-tenant Data Encryption Key (DEK) before being written to PostgreSQL; the DEK itself is encrypted ("wrapped") by a Key Encryption Key (KEK) that never leaves Section 15's Vault instance (via Vault's Transit secrets engine, specified fully in Section 15.4) — PostgreSQL therefore only ever stores a doubly-encrypted value it has no ability to decrypt on its own, satisfying NFR-SEC-2's "outside the primary application database" requirement precisely: the KEK genuinely never resides in or is derivable from anything PostgreSQL holds.

14.5 Credential rotation

Every credential category in 14.2's table has a defined rotation cadence and procedure: platform infrastructure credentials rotate on a scheduled cadence (e.g., quarterly) via Section 15's Vault-native rotation capability where available (the database secrets engine, Section 15.5, natively supports this for PostgreSQL credentials specifically); tenant-supplied target-system credentials rotate at the tenant's own discretion via Volume 1, Part II, Section 13.5's RotateCredentialCommand, with Nexus never imposing a forced rotation schedule on credentials it doesn't own; AI provider keys rotate per that provider's own security recommendations, tracked as an operational calendar item. Every rotation event — regardless of category — raises Volume 1, Part II, Section 12.5's CredentialRotatedEvent, durably audited (Volume 1, Part IV, Section 27) exactly as already specified there, this section adding only the operational cadence driving when that event fires for platform-owned credentials specifically.

14.6 Break-glass access — the deliberate, audited exception

For genuine emergency scenarios (a security incident requiring immediate credential revocation outside the normal rotation cadence, per Section 18.7's incident-response process referenced from Volume 3), a narrowly-scoped "break-glass" access procedure exists — a small number of designated administrators can trigger emergency rotation or revocation outside Vault's normal policy-gated access flow, with every break-glass action generating an immediate, unsuppressable Critical-severity alert (Section 7.5) and a mandatory post-incident review, ensuring this necessary emergency capability is never usable quietly or routinely, only as a genuinely exceptional, fully-visible action.

14.7 Secrets in transit — never logged, restated as an infrastructure guarantee

Directly restating and closing the loop on Volume 1, Part IV, Section 28.5's "credential values never logged, at any level" rule and Volume 4, Section 8.6's Collector-level scrubbing: this volume's infrastructure adds a final, structural guarantee — ICredentialResolver's resolved values are typed with a wrapper type (SecretValue<T>) whose ToString() override always returns a redacted placeholder rather than the actual value, meaning even an accidental future logging statement referencing a resolved credential cannot leak its actual value — a compile-time-adjacent safeguard directly analogous to Volume 1, Part II, Section 10.8's strongly-typed-identifier philosophy ("eliminated at the type-system level, not just by convention"), now applied to secret values specifically as this volume's closing contribution to a discipline every prior volume has reinforced in its own way.

14.8 Auditing secret access itself

Every resolution of a tenant-supplied credential (14.2's second row) — not just its rotation — generates an audit-adjacent record (distinct from, but correlated with, Volume 1, Part IV, Section 27's AuditEntry for the triggering flow-run event) noting which flow step accessed which named credential reference and when, satisfying Persona Elena's likely compliance-review expectation (Volume 0, UC-7) that credential usage, not just credential changes, be traceable — a deliberate extension beyond Volume 1's originally-specified audit scope, added here because this volume's infrastructure-level visibility into actual secret resolution calls is the first point in this document set where such fine-grained usage auditing becomes practically implementable.



15. Vault
15.1 Purpose — the concrete implementation of Section 14's architecture

Volume 0, Section 21.13 locked HashiCorp Vault as the reference secrets-management implementation. This section specifies its concrete deployment: authentication methods, secret engines, and the high-availability configuration this platform's entire credential architecture (Section 14) ultimately depends on.

15.2 Deployment topology — Vault HA with integrated storage

Vault runs in HA mode using its integrated Raft storage backend (avoiding an external storage dependency like Consul, keeping this platform's total infrastructure footprint smaller — directly consistent with Architecture Principle 20.8's self-hosted-first posture, where every additional required infrastructure dependency is a real cost to a self-hosted customer's operational burden, the exact reasoning Volume 1, Part III, Section 22.3 already applied when rejecting an external message broker for GA) — at least three Vault nodes participating in Raft consensus, tolerating a single node failure without service interruption, directly serving NFR-REL-1's availability commitment extended to this platform's most security-critical dependency.

15.3 Authentication methods
Auth method Used by Detail
Kubernetes auth nexus-api, nexus-orchestration pods Each pod authenticates using its Kubernetes service-account token, scoped by namespace (Section 3.2) — nexus-sandbox namespace pods are deliberately granted no Vault authentication path at all, directly enforcing Section 3.5's network-policy isolation at the credential layer too: a compromised sandboxed module cannot even attempt to authenticate to Vault, not merely blocked by network policy from reaching it
OIDC (CI) GitHub Actions workflows  Volume 4, Section 6.6's short-lived, workflow-scoped federation — no long-lived Vault token ever stored as a CI secret
OIDC (human)  Break-glass access (Section 14.6) Federated through the same enterprise SSO integration Volume 1, Part IV, Section 24.7 established, ensuring break-glass access is tied to a real, individually-accountable human identity, never a shared credential
15.4 Secret engines — KV and Transit

KV (Key-Value) v2 engine stores platform infrastructure credentials (Section 14.2's first row) as versioned static secrets, with version history enabling rollback if a rotation (Section 14.5) introduces a problem.

Transit engine implements Section 14.4's envelope-encryption design directly — the platform's per-tenant Key Encryption Keys (KEKs) live exclusively within Transit, which performs encrypt/decrypt operations on demand without ever exposing the KEK's raw key material to the calling application at all (Transit's defining property: the application sends plaintext or ciphertext to Vault and receives the opposite, but the key itself never leaves Vault) — this is a stronger guarantee than Section 14.4's "KEK never resides in PostgreSQL" alone; the KEK never resides in application memory either, at any point, closing a gap a naive envelope-encryption implementation (fetching the KEK into application memory to perform encryption locally) would leave open.

15.5 Database secrets engine — dynamic, short-lived database credentials

Beyond static KV storage, Vault's Database secrets engine generates dynamic, short-lived PostgreSQL credentials on demand for administrative/operational tooling (Section 13's DBA-adjacent maintenance operations, Section 6's CI migration-job execution) — each request produces a unique username/password pair with a defined TTL (e.g., 1 hour), automatically revoked by Vault at expiry, meaning no long-lived, shared administrative database credential exists anywhere in this platform's operational tooling at all. The application's own runtime database connections (via PgBouncer, Section 13.3) use a longer-lived, but still Vault-managed and rotatable, service credential — dynamic per-request credentials are specifically for human/tooling operational access, not the high-frequency application connection path, where the rotation cadence Section 14.5 already specified is the appropriate mechanism instead.

15.6 Unsealing strategy

Vault's unseal mechanism uses auto-unseal via a cloud KMS (for managed/hosted deployments) or, for self-hosted deployments without a cloud KMS available (the air-gapped case specifically, Volume 1, Part VI, Section 37.7), Shamir's Secret Sharing with a defined threshold (e.g., 3-of-5 key shares distributed among designated administrators) — auto-unseal is strongly preferred where available since it enables Vault to recover automatically after a restart without requiring manual key-share entry, directly serving NFR-REL-1's availability commitment; the Shamir fallback for air-gapped deployments is explicitly documented as carrying a real operational tradeoff (a Vault restart requires manual intervention from key-share holders) that self-hosted, air-gapped customers accept as part of that deployment profile's inherent constraints, consistent with this document set's practice of stating such tradeoffs honestly rather than glossing over them.

15.7 Policy design — least privilege per consumer

Every Vault authentication identity (15.3) is bound to a narrowly-scoped policy granting access only to the specific secret paths that consumer needs — nexus-api's Kubernetes-auth identity can read the JWT signing key (Volume 1, Part IV, Section 24.5) and platform database credentials, but has no access to Transit's tenant KEKs (which only nexus-orchestration's credential-resolution path, Section 14.3, needs); the CI OIDC identity can read only the specific deployment-time secrets Section 6.6 named, nothing broader. This is the direct, Vault-policy-level implementation of the least-privilege principle Volume 1, Part III, Section 18.4 established conceptually for ExecutionContext's scoping, now applied uniformly to every Vault-authenticating identity in this platform, not just module execution specifically.

15.8 Vault monitoring and the meta-observability principle

Consistent with Section 7.8's "monitoring the monitoring" principle, extended to this platform's most security-critical dependency: Vault's own health, seal status, and audit log (Vault's native audit-logging feature, distinct from but complementary to Volume 1, Part IV, Section 27's application-level audit trail) are monitored independently, with seal-status specifically alerted at Critical severity (Section 7.5) — a sealed Vault instance means every credential-resolution path across this entire platform stops working simultaneously, making this the single highest-severity infrastructure dependency failure this volume's monitoring strategy needs to catch fastest.

15.9 Self-hosted and air-gapped Vault

Following this Part's now-consistent conditional-deployment pattern: a self-hosted deployment bundles its own Vault instance (15.2's HA configuration, resource permitting, or a single-node instance for smaller deployments with the same explicitly-documented availability tradeoff Section 13.9 already established for a single-instance PostgreSQL choice); air-gapped deployments use Section 15.6's Shamir-based unsealing and require zero external network dependency for Vault's own operation, consistent with NFR-SEC-4's "no mandatory outbound internet dependency for core functionality" — Vault itself, being the very foundation this platform's secrets architecture rests on, is held to that requirement without exception.



16. TLS
16.1 Purpose — completing Volume 1's NFR-SEC-1 and Volume 4's forward references

Volume 1's NFR-SEC-1 required TLS 1.2+ for all data in transit; Volume 4, Section 3.6 named Ingress-level TLS termination and flagged "re-encrypting internally only where Section 16's zero-trust posture requires it" as this section's responsibility. This section specifies the complete certificate lifecycle — external termination, internal mTLS, rotation, and the air-gapped certificate-authority scenario.

16.2 External TLS — cert-manager and automated issuance

External-facing TLS (the Ingress layer, Section 3.6) uses cert-manager, automated against a public ACME-compatible certificate authority (Let's Encrypt or an equivalent) for the managed/hosted deployment and standard internet-connected self-hosted deployments — certificates are automatically issued and renewed well before expiry (cert-manager's default renewal window, typically 30 days before a 90-day certificate's expiry), with renewal failure alerting at Warning severity (Section 7.5) escalating to Critical as expiry actually approaches, ensuring certificate expiry — a historically common, embarrassingly preventable cause of production outages — is caught by this platform's own monitoring discipline (Section 7) well before it could threaten NFR-REL-1's availability commitment.

16.3 Internal mTLS — the zero-trust posture for the sandbox boundary

Directly implementing Volume 1, Part III, Section 18.5's sandboxed-module gRPC communication design and Section 3.5's network-policy isolation: the gRPC channel between nexus-orchestration and nexus-sandbox-runner pods (Section 3.2) uses mutual TLS — both ends present and verify certificates, not just the client verifying the server — issued by an internal certificate authority (cert-manager's internal CA issuer, distinct from the external ACME-issued certificates in 16.2) and automatically rotated on a short cadence (e.g., 24 hours, since these are short-lived, frequently-reissued certificates for internal service identity rather than long-lived external-facing certificates). This mTLS requirement is the concrete implementation of "zero-trust" specifically for this one boundary: even though Section 3.5's network policy already restricts which pods can reach nexus-orchestration's sandbox-callback endpoint, mTLS additionally ensures the connecting pod is cryptographically verified to be a legitimate sandbox runner, not merely a pod that happened to be scheduled within the allowed network path — a meaningful additional layer given this boundary's elevated risk profile (Volume 1, Part I, Section 2.5).

16.4 Internal TLS elsewhere — encryption without full mTLS

Every other internal service-to-service connection (nexus-api/nexus-orchestration to PostgreSQL via PgBouncer, to Redis, to Vault, to the OTel Collector) uses TLS for encryption-in-transit but not full mutual TLS with per-connection certificate verification — a deliberate, narrower posture than Section 16.3's sandbox-boundary treatment, justified by the same risk-proportionate reasoning this document set has applied consistently (Volume 1, Part III, Section 18.5's trust-tiered execution model itself being the original template for "not every boundary needs the same level of defense"): these are trusted, first-party components within the same security domain, where TLS's encryption property (protecting against network-level eavesdropping, relevant even within a cluster given Kubernetes' own network model) is the primary requirement, and Section 3.5's network policies already provide the connection-restriction property mTLS would additionally add.

16.5 Certificate rotation and zero-downtime reload

Every TLS-terminating component (the Ingress controller, nexus-api for any direct TLS it terminates itself, Vault per Section 15.2) supports certificate hot-reload — a renewed certificate (16.2's automated renewal, or 16.3's 24-hour internal rotation) is picked up without requiring a pod restart or connection interruption, directly serving NFR-REL-1's availability commitment by ensuring routine certificate rotation is never itself a source of downtime, a specific, concrete instance of this volume's general "routine operational events must not threaten availability commitments" principle already applied to Kubernetes node draining (Section 3.4) and database failover (Section 13.2).

16.6 Air-gapped deployments — the self-signed internal CA scenario

For air-gapped self-hosted deployments (Volume 1, Part VI, Section 37.7), Section 16.2's public-ACME-CA approach is unavailable by definition (no outbound internet access to reach Let's Encrypt or any public CA) — these deployments use a customer-provided or Nexus-generated self-signed internal Certificate Authority instead, with cert-manager configured against that internal CA rather than an external ACME issuer. This requires the customer's own client machines (for the GUI, Volume 2) and any external system integrations to trust that internal CA explicitly — a documented, expected part of the air-gapped deployment's setup procedure (extending Section 4.7's self-hosted installation documentation), not an oversight; this is the one meaningful operational difference between an air-gapped and an internet-connected self-hosted deployment's TLS posture, called out explicitly here because it's a genuine, unavoidable consequence of the air-gapped constraint itself, not a gap in this volume's design.

16.7 TLS and the frontend — cookie security depending on TLS being genuinely universal

Directly connecting back to Volume 2, Section 14.2's Secure cookie flag (the refresh-token cookie, HttpOnly/Secure/SameSite=Strict) — that flag's entire security value depends on TLS being genuinely, universally present on every environment the GUI could ever be served from, including local development (Section 1.6's mkcert-based local TLS, specified precisely so this dependency holds even in the least security-critical environment) — this section's universal-TLS posture is what makes Volume 2's cookie-security design actually correct in every deployment context, not merely correct in the specific environments someone happened to test.

16.8 Closing note for this Part

This section closes Part IV: Redis (Section 12) and PostgreSQL (Section 13) specified the data layer's operational posture; Secrets (Section 14) and Vault (Section 15) specified the credential architecture protecting that data layer and everything else; TLS (this section) specified the encryption-in-transit layer protecting all of it in motion. Part V (Resilience & Operations) now turns to how this complete infrastructure picture behaves under failure — disaster recovery, high availability synthesis across every component this Part has specified individually, scaling, backup strategy, and cost optimization for the infrastructure layer specifically, completing this volume.


____________________________________________________________________________________________________________________________________________________



17. Disaster Recovery
17.1 Purpose — the failure scale beyond Part III's individual-component alerting

Part III's monitoring strategy (Section 7) and Part IV's per-component HA configurations (Sections 12, 13, 15) address individual-component failure. This section addresses the harder, less frequent scenario: a failure severe enough to threaten the platform's overall availability commitment — a full cluster loss, a region-level outage, or a catastrophic data-corruption event — and specifies the recovery objectives and tested procedures this document set's NFR-REL-1 commitment ultimately rests on.

17.2 Recovery objectives — RTO and RPO, derived from NFR-REL-1
Scenario  RTO (Recovery Time Objective) RPO (Recovery Point Objective)
Single-region cluster loss (managed/hosted) 4 hours 5 minutes (WAL-streaming lag, Section 13.7)
PostgreSQL catastrophic corruption  1 hour (via Patroni failover, Section 13.2, if replica-level; 4 hours if requiring full restore)  5 minutes
Vault data loss 2 hours (Raft snapshot restore, Section 15.2) 15 minutes (snapshot frequency, Section 20.4)
Self-hosted customer's own infrastructure failure Customer-owned — Nexus provides the tooling (this section's procedures), the customer's own infrastructure team owns execution and timeline 

These targets are derived directly from NFR-REL-1's 99.9% availability commitment — a 4-hour RTO for the rare, severe full-cluster-loss scenario is mathematically compatible with a 99.9% annual availability target (roughly 8.7 hours of acceptable downtime per year) only if such severe events are genuinely rare, which is precisely why Section 18's HA design exists to prevent the vast majority of failures from ever escalating to this section's DR scenarios in the first place — this section is the deliberate, tested fallback for the failure modes HA alone cannot fully prevent, not the primary availability strategy.

17.3 DR posture — pooled managed/hosted vs. dedicated-instance vs. self-hosted

Directly extending Volume 1, Part IV, Section 26's three deployment models to disaster recovery specifically: the pooled managed/hosted deployment (Volume 1, Part IV, Section 26.2) maintains a warm-standby capability in a secondary region, with the RTO in 17.2 reflecting the time to redirect traffic and promote that standby, not a cold rebuild. The dedicated-instance model (Volume 1, Part IV, Section 26.6) — consistent with its stronger isolation promise extending to DR posture as well — maintains an equivalent per-customer secondary-region capability where the customer's contract specifies it, at a cost tier reflecting that dedicated redundancy (a direct, natural extension of Volume 0, Section 16.5's pricing principles). Self-hosted deployments (Architecture Principle 20.8) own their own DR posture entirely — Nexus provides the backup/restore tooling (Section 20) and this section's documented procedures, but execution, RTO/RPO targets, and secondary-region infrastructure (if any) are the customer's own responsibility, consistent with the self-hosted model's fundamental operational-ownership boundary.

17.4 The DR runbook

A maintained, versioned runbook (Section 7.7's documentation-as-code discipline extended to this platform's most severe failure scenarios) specifies, step by step: how to detect that a DR scenario (as opposed to a routine, HA-absorbed failure) is actually in progress; the decision authority for declaring a DR event (avoiding a premature, costly full-DR response to what might resolve as a routine incident); the exact sequence of actions (secondary-region promotion, DNS/traffic cutover, Vault unsealing at the secondary per Section 15.6's procedure, verification steps confirming the recovered environment is genuinely healthy before declaring recovery complete) — this runbook is the concrete artifact Section 17.5's game-day exercises validate, and the artifact an actual incident is executed against, never improvised in the moment.

17.5 Failure testing — game days

Scheduled, deliberate DR exercises (quarterly, at minimum) simulate each scenario in 17.2's table against a genuinely isolated test environment (never production) — a full secondary-region failover drill, a Vault Raft-snapshot restore drill, a PostgreSQL point-in-time-recovery drill — measuring actual RTO/RPO achieved against the targets in 17.2 and updating the runbook (17.4) with any gap discovered. This is a direct, deliberate application of Volume 1, Part VI, Section 38.8's "load testing as a release gate" philosophy extended to disaster recovery specifically: an RTO/RPO target that has never been actually exercised is an untested claim, not a verified capability, and this document set has consistently treated untested claims about critical properties as inadequate (the same reasoning behind Volume 2, Section 19.11's adversarial plugin-sandbox testing and Volume 3, Section 17.9's adversarial guardrail testing).

17.6 Communication during a DR event

The DR runbook (17.4) includes a defined customer-communication procedure — status-page updates at defined intervals during an active DR event, a post-incident report within a committed timeframe after resolution — directly serving Volume 0's Business Objective 5 trust posture: a DR event handled competently but silently, with customers discovering the outage only through their own monitoring, would undermine exactly the enterprise trust this entire document set has repeatedly prioritized, even if the technical recovery itself met every RTO/RPO target in 17.2.

17.7 AI capability during DR

Directly extending Volume 3's AI-optionality architecture (NFR-REL-3) to the DR scenario specifically: a DR event affecting the primary region does not need to restore AI capability (Volume 3's Planning Engine, Section 2 there) with the same urgency as core platform capability (flow execution, Volume 1's Orchestration Engine) — AI capabilities gracefully degrade to unavailable (Volume 3, Section 2.5's AiUnavailableError path) exactly as they would during a routine AI-provider outage, meaning the DR runbook's priority ordering restores core execution capability first, with AI capability restoration following as a lower-priority, non-blocking subsequent step — a direct, practical payoff of Volume 3's entire AI-optionality architecture being tested under genuine disaster conditions, not just routine operation.

17.8 Relationship to Section 18

This section specified recovery from severe failure. Section 18 (High Availability) specifies the complementary, preventive side — the synthesis of every component's individual HA design across this volume into one coherent picture of what failure this platform absorbs without ever escalating to this section's DR procedures at all.



18. High Availability
18.1 Purpose — the synthesis section

Every prior section in this volume specified one component's HA design individually — Kubernetes' Pod Disruption Budgets (Section 3.4), Redis Sentinel (Section 12.2), PostgreSQL Patroni-managed replication (Section 13.2), Vault Raft consensus (Section 15.2). This section is the deliberate synthesis: one table showing what failure this platform survives without customer-visible impact, and what failure escalates to Section 17's disaster-recovery procedures instead — the single artifact that answers "how available is Nexus, really" comprehensively, rather than requiring that answer to be assembled by reading every component section individually.

18.2 The failure/survival matrix
Failure scenario  Survives without impact?  Mechanism Escalates to DR if...
Single nexus-api pod crash  Yes Kubernetes restarts pod; other replicas serve traffic (Section 3.3's multi-replica baseline)  Never — routine, self-healing
Single nexus-orchestration pod crash mid-run  Yes, run-level  Volume 1, Part III §16.7's per-step commits mean only the in-flight step is lost, not the run; Kubernetes reschedules the pod Never — routine, and specifically the scenario Volume 1's Unit of Work fix (this project's own mid-session correction) was designed to handle correctly
Single Kubernetes node failure  Yes Pod rescheduling to healthy nodes; PDB (Section 3.4) ensures this doesn't cascade Only if node loss rate exceeds cluster's spare capacity
PostgreSQL primary failure  Yes, brief (seconds to low minutes) Patroni-orchestrated automatic failover to synchronous replica (Section 13.2) If failover itself fails or replica is also unavailable
Redis primary failure Yes, brief  Sentinel-orchestrated failover (Section 12.2) If all replicas are simultaneously unavailable
Vault node failure (1 of 3+)  Yes Raft consensus tolerates minority node loss (Section 15.2)  If quorum is lost (majority of nodes down)
Single availability zone failure (managed/hosted) Yes, if multi-AZ deployed Node pools (Section 3.8) and stateful components (13.2, 12.2, 15.2) spread across AZs If the affected AZ hosts a disproportionate, unreplicated share of stateful components
AI provider outage  Yes Volume 3's AiProvider abstraction (Part IV §14) + fallback chains (§15.6) + graceful AiUnavailableError degradation (NFR-REL-3) Never for core platform; AI capability itself is unavailable until provider recovers, by design
Full region loss  No  — Always — Section 17's DR procedures
Correlated multi-component failure (e.g., a bad deployment breaking multiple services simultaneously) Depends Section 5.6's rollback procedure, if caught before widespread impact  If rollback itself is insufient or the root cause is data corruption, not just bad code
18.3 Reading this matrix — what "survives without impact" actually means

Every "Yes" in this table means specifically: no customer-visible availability impact beyond a brief, sub-SLO-threshold blip (the failover windows Section 13.2 and 12.2 specify are measured in seconds to low minutes, well within NFR-REL-1's error budget for routine operational events) — it does not mean zero internal disruption. A PostgreSQL failover, for instance, briefly increases connection errors at the PgBouncer layer (Section 13.3) during the failover window itself; this is expected, monitored (Section 7's alerting), and does not constitute an SLO breach given its brief duration, but it is not literally invisible at the infrastructure level, only at the customer-experience level this matrix is scoped to describe.

18.4 Multi-AZ as the load-bearing assumption behind most "Yes" answers

Section 18.2's table makes clear that a large fraction of this platform's HA posture depends on multi-AZ deployment specifically (18.2's AZ-failure row, and implicitly, every stateful-component row above it, since Patroni/Sentinel/Raft's automatic failover only protects against AZ-level failure if the replicas they failover to are genuinely in a different AZ, not just a different node in the same AZ). This is stated explicitly here because it's a deployment-configuration decision (Section 3.8's node-pool design, and the corresponding Helm values, Section 4.3) that a cost-conscious or smaller self-hosted deployment might reasonably choose not to replicate — Architecture Principle 20.8's self-hosted-first posture means this volume documents multi-AZ as the recommended, HA-complete configuration while being honest that a smaller, single-AZ self-hosted deployment is a supported, lower-cost alternative with a correspondingly weaker AZ-failure row in this matrix, a tradeoff that deployment profile's operators knowingly accept.

18.5 HA testing — continuous, not just Section 17's periodic game days

Distinct from Section 17.5's quarterly DR game days (which test full-scale recovery from severe failure): the routine HA mechanisms in this section's matrix (pod rescheduling, database failover, Redis failover) are exercised far more frequently, semi-continuously, via chaos-engineering practices — scheduled, automated fault injection (randomly terminating a non-critical pod, briefly partitioning a node) run against staging (Section 5.3) on a regular cadence, verifying that the "Yes, survives without impact" claims in Section 18.2's table remain true as the system evolves, rather than being verified once at initial design time and assumed to hold indefinitely afterward.

18.6 HA and the cost tradeoff — forward reference

Every "Yes" in Section 18.2's table has a real infrastructure cost (running replicated PostgreSQL/Redis/Vault instances, multi-AZ node pools, standby capacity) — this tradeoff is not hidden or free, and Section 21 (Cost Optimization) specifically addresses how this cost is managed and right-sized per deployment tier without silently eroding the HA guarantees this section documents; the two sections are companion pieces, not independent concerns, consistent with Volume 3, Section 16.9's explicit refusal to treat cost optimization as free everywhere this document set has confronted the same tension.

18.7 Closing note

This matrix is the artifact Persona Elena's security/reliability review (Volume 0, UC-7) and Sofia's leadership reporting (Volume 0, UC-5, extended to infrastructure trust questions a customer's own leadership might raise) can both be shown directly — the concrete, comprehensive answer to "what happens when X fails" that this entire volume's component-by-component specification was building toward.



19. Scaling
19.1 Purpose — the capacity-planning synthesis

Volume 1, Part VI, Section 39 specified scaling architecture — which dimensions scale and by what mechanism. This section specifies the operational capacity-planning practice: how this volume's concrete mechanisms (HPA, PgBouncer, partitioning) are actually sized, monitored, and adjusted over time against Volume 1's NFR-SCALE targets, and — consistent with this document set's practice of honesty about limits — restates Volume 1, Section 39.9's known scaling ceilings without softening them.

19.2 Scaling mechanism inventory — mapping NFR-SCALE targets to this volume's concrete infrastructure
NFR-SCALE target  Volume 1's architectural mechanism  This volume's concrete implementation
NFR-SCALE-1 (500 concurrent executions) Horizontal pod scaling on custom metric Section 3.7, 9.4's HPA + prometheus-adapter pipeline
NFR-SCALE-2 (horizontal scaling, zero client-facing change) Stateless containers, Redis-coordinated state Section 12.7's SignalR backplane, Section 3.3's resource sizing
NFR-SCALE-3 (10M flow-run records)  Indexing, then partitioning when needed Section 13.5's partitioning activation trigger

Every NFR-SCALE target Volume 1 committed to now has a traceable, concrete infrastructure mechanism specified somewhere in this volume — this table exists so that traceability is visible in one place rather than requiring a reader to independently discover which of this volume's many sections implements which Volume 1 commitment.

19.3 Capacity planning cadence

Resource requests/limits (Section 3.3), HPA thresholds (Section 3.7), PgBouncer pool sizing (Section 13.3), and Redis memory allocation (Section 12.4) are reviewed against actual observed usage on a monthly cadence — directly extending Section 3.3's "sized with headroom, validated periodically" principle into a formal, scheduled practice rather than an ad hoc one, with findings feeding both this section's capacity documentation and, where a genuine scaling-mechanism gap is found, a proposal to adjust the mechanism itself (not merely its configuration) — the same escalation path Volume 1, Part VI, Section 39.6 already established for when indexing alone becomes insufficient and partitioning must actually activate.

19.4 Growth projection and headroom

Capacity planning maintains a rolling 6-month growth projection (derived from Volume 0's Roadmap phasing, Part III Section 15, and observed tenant onboarding rate) against every mechanism in 19.2's table — HPA's maximum replica ceiling, PgBouncer's maximum pool size, the Kubernetes cluster's own node-pool capacity ceiling — ensuring headroom exists before growth demands it, not discovered reactively when a scaling mechanism's own configured ceiling becomes the actual bottleneck rather than the underlying resource it was meant to manage. This is the concrete operational practice behind Section 7.4's error-budget philosophy applied to capacity specifically: a capacity ceiling approaching its limit is a Warning-severity signal (Section 7.5), not a surprise discovered only when NFR-SCALE-1's 500-execution target is actually exceeded and requests start queuing or failing.

19.5 Load testing cadence — restating and scheduling Volume 1's gate

Volume 1, Part VI, Section 38.8 established load testing as a pre-release gate (Section 5.2, stage 8, of this volume's CI/CD pipeline). This section adds the complementary, non-release-triggered practice: a scheduled, standalone load test (independent of any specific release) runs quarterly against a full-scale staging replica, specifically to validate that 19.4's growth-projection assumptions still hold against the current actual system, not just against the system as it existed whenever the most recent release happened to trigger the pipeline's own gate — catching a slow, cumulative capacity drift (many small, individually-passing releases each adding a small amount of overhead) that no single release's own gate would individually flag.

19.6 Known scaling ceilings — restated honestly, not softened

Directly restating Volume 1, Part VI, Section 39.9's explicit, deliberate disclosure: the Mobile module's device/session availability and the Desktop UI module's single-machine-execution-context nature remain genuine scaling ceilings this volume's infrastructure does not attempt to engineer around — no Kubernetes mechanism, no database partitioning strategy, and no HPA configuration changes that constraint, because it is inherent to what those two modules automate (a physical or emulated device, a single OS session), not an infrastructure limitation this volume's tooling could address even in principle. This section adds one further, infrastructure-specific ceiling worth naming with the same honesty: the nexus-sandbox-runner pod pool (Volume 1, Part III, Section 18.5; this volume's Section 3.2, 3.8) has a genuine, real ceiling on concurrent Community-tier module executions, bounded by the sandbox node pool's own capacity (Section 3.8) — a tenant running an unusually large number of concurrent Community-tier-module-dependent flows could, in principle, encounter sandbox-capacity queuing that a Verified-tier or Built-in-module-only flow would never experience, a direct, honest consequence of the trust-tiered execution model's own architecture (Volume 1, Part III, Section 18.5) that this volume's infrastructure correctly implements rather than papers over.

19.7 Scaling and multi-tenancy fairness — restating Volume 1's rate-limiting role

Directly restating Volume 1, Part VI, Section 39.8's observation that Volume 1's tenant-scoped rate limiting (Volume 1, Part V, Section 30.7) serves double duty as both an abuse-prevention and a scaling-fairness mechanism in the pooled managed/hosted model — this section adds the infrastructure-configuration detail: rate-limit thresholds (enforced via the Redis-backed sliding-window implementation, Section 12.6) are themselves reviewed as part of 19.3's monthly capacity-planning cadence, ensuring one large tenant's legitimate, growing usage doesn't silently consume a disproportionate share of the platform's shared capacity ceiling (19.4) at the expense of other tenants' fair access to the same shared infrastructure.

19.8 Scaling documentation as a sales/procurement artifact

Consistent with Section 18.7's observation that the HA matrix serves Persona Elena's and Sofia's review needs directly: this section's capacity-planning documentation (19.2's mechanism table, 19.6's honestly-disclosed ceilings) is maintained in a form suitable for direct use in enterprise sales and procurement conversations — a prospective enterprise customer asking "what happens at our expected scale" receives this section's actual, tested answer, including its honestly-disclosed limits, rather than an unsubstantiated assurance — directly reinforcing Volume 0, Section 8.6's "credibility tax" risk mitigation: a platform that's honest about where its ceilings are is more credible, not less, to a sophisticated enterprise buyer evaluating a genuine infrastructure commitment.



20. Backup Strategy
20.1 Purpose — the concrete mechanism behind Section 17's RPO targets

Section 17.2 stated RPO targets (5 minutes for PostgreSQL, 15 minutes for Vault) without specifying the backup mechanism producing them. This section specifies that mechanism precisely, per component, plus the retention policy and — consistent with Section 17.5's "untested claims are inadequate" discipline — the restore-testing cadence that keeps these backups genuinely usable, not merely present.

20.2 PostgreSQL — continuous WAL archiving plus periodic base backups

Directly implementing Section 13.7's forward reference: continuous WAL (Write-Ahead Log) shipping to object storage (S3-compatible, chosen for broad availability across both managed-cloud and self-hosted-with-MinIO-or-equivalent scenarios) runs constantly, with a full base backup taken daily — this combination is what enables point-in-time recovery to any moment within the retention window (20.5), not merely restoration to the most recent daily snapshot, directly producing the 5-minute RPO Section 17.2 committed to (bounded by WAL-shipping lag, monitored per Section 7 as a named metric with Warning-severity alerting if lag exceeds a defined threshold).

20.3 Redis — persistence as backup, not a substitute for it

Section 12.3's AOF persistence configuration provides crash-recovery durability for a single Redis restart, but is explicitly not treated as a substitute for genuine backup — Redis's RDB snapshots are additionally exported to object storage on a scheduled cadence (hourly), specifically because AOF alone protects against process crash but not against a corrupted or accidentally-flushed dataset, a distinct failure mode a point-in-time RDB snapshot in separate, immutable object storage protects against that AOF's continuously-rewritten log file does not.

20.4 Vault — Raft snapshots

Vault's integrated Raft storage (Section 15.2) is backed up via Vault's native snapshot capability, taken every 15 minutes (directly producing Section 17.2's 15-minute Vault RPO) and shipped to the same object storage backend as PostgreSQL and Redis backups — critically, Vault snapshots themselves contain encrypted data (Vault's own storage-layer encryption), meaning the snapshot's own security posture doesn't depend on the backup storage location's access control alone, a deliberate defense-in-depth property directly relevant given what Vault snapshots actually contain (this platform's entire secrets and envelope-encryption key material, Section 14.4).

20.5 Retention policy
Data type Hot retention Cold retention  Rationale
PostgreSQL WAL + base backups 7 days (immediate PITR window)  90 days (compressed, less-frequent access tier) Balances immediate-recovery flexibility against long-tail incident investigation needs
Redis RDB snapshots 24 hours (hourly snapshots) 7 days (daily rollup) Redis holds ephemeral/cache-adjacent data (Section 12.1's roles) — even the permission-cache role, the most durability-sensitive of the three, self-heals from PostgreSQL as source of truth within one cache-refresh cycle, so long-tail retention has limited marginal value
Vault Raft snapshots  7 days (every 15 minutes) 30 days (daily rollup)  Matches the elevated but not indefinite retention need for this platform's most security-critical backup

This tiered structure directly mirrors the hot/cold pattern already established for operational logs (Volume 1, Part IV, Section 28.6) and Elasticsearch's ILM policy (Section 11.3) — the same cost/utility reasoning applied consistently across every retained-data category this platform manages, rather than each backup type inventing its own independent retention philosophy.

20.6 Cross-region backup storage

For the managed/hosted deployment's secondary-region DR posture (Section 17.3), backups are replicated cross-region as part of the object storage layer's own configuration (standard cross-region replication, not a Nexus-built mechanism) — ensuring a full primary-region loss doesn't also take the backups needed to recover from that loss with it, a specific, easy-to-overlook failure mode (backups stored only in the same region/failure domain as the data they protect) this section deliberately guards against explicitly.

20.7 Restore testing — the discipline that makes a backup genuinely trustworthy

Directly extending Section 17.5's game-day philosophy to backups specifically, at a higher frequency given how foundational backup integrity is to every other resilience claim in this volume: an automated, weekly restore-test pipeline provisions a fresh, isolated environment, restores the most recent PostgreSQL base backup plus WAL replay to a specific recent point in time, restores the most recent Vault snapshot, and runs Volume 2, Section 19.9's E2E smoke-test subset against the restored environment — verifying not just that the restore operation completes without error, but that the restored data is actually usable by the actual application, the specific, stronger verification this document set's "untested claims are inadequate" discipline (Section 17.5) requires rather than accepting "the restore command exited successfully" as sufficient evidence of a genuinely working backup.

20.8 Backup encryption and access control

Every backup artifact (PostgreSQL, Redis, Vault) is encrypted at rest in object storage using a key managed independently of the backup storage location itself (mirroring Section 14.4's envelope-encryption principle — the key protecting the backup is never stored alongside the backup it protects), with access to the backup storage location itself gated by the same least-privilege Vault-issued dynamic credentials (Section 15.5) used for other operational database access — a backup is exactly as sensitive as the data it contains, and this section's access-control posture treats it with that seriousness rather than as a lower-scrutiny operational afterthought.

20.9 Self-hosted backup responsibility

Directly extending Section 17.3's DR-posture distinction to backups specifically: self-hosted deployments receive the same backup tooling this section specifies (the WAL-archiving configuration, the Vault snapshot schedule, the restore-test pipeline definition, all shipped as part of the Helm chart, Section 4) but execution — where backups are actually stored, cross-region replication if any, and the restore-testing cadence — is the customer's own operational responsibility, consistent with Architecture Principle 20.8's self-hosted operational-ownership boundary already established throughout this volume. Nexus's documentation (this section) specifies the recommended, tested configuration; a self-hosted customer's own infrastructure team decides whether and how closely to follow it for their own environment.



21. Cost Optimization
21.1 Purpose and scope — infrastructure cost, distinct from Volume 3's AI cost

A scope clarification worth stating precisely, given this is the second "cost optimization" section this document set has specified: Volume 3, Section 16 addressed AI provider cost (tokens, model tiers, routing policy) — a cost driven by usage of a third-party service this platform doesn't control the pricing of. This section addresses infrastructure cost — the Kubernetes cluster, database, storage, and networking spend this platform's own architecture directly determines through its resource-sizing and deployment-topology choices. The two are related (Section 21.7 shows exactly how) but are governed by genuinely different levers, which is why this document set treats them as two distinct sections rather than one merged discussion.

21.2 Compute right-sizing — closing the loop on Section 3.3 and 19.3

Directly extending Section 3.3's resource-request/limit sizing and Section 19.3's monthly capacity-review cadence with an explicit cost lens: the same periodic review that validates capacity headroom (19.3) also flags over-provisioning — a component whose actual observed resource usage sits persistently well below its configured requests represents pure wasted spend, not just unused headroom, and is right-sized downward with the same rigor 19.4's growth-projection process applies to sizing upward. This bidirectional discipline (review can trigger either a scale-up or scale-down recommendation) is stated explicitly because capacity-planning processes commonly develop an asymmetric bias toward only ever adding headroom, never reclaiming it — this section's review process is designed against that specific bias.

21.3 Storage tiering

Directly extending Section 9.3's Prometheus retention tiers, Section 11.3's Elasticsearch ILM policy, and Section 20.5's backup retention tiers with the cost rationale underlying all three: hot-tier storage (fast, expensive, low-latency) is scoped as narrowly as each data type's genuine access-pattern needs require (Section 9.3's 15-day raw-metric window, Section 11.3's 14-day log hot-tier), with automatic transition to cold-tier (cheaper, higher-latency) storage for the remainder of each retention window — this is not a new policy introduced here; this section's contribution is naming the cost rationale that was implicit in those three sections' retention designs from the start, now made explicit as this volume's unifying cost principle rather than three independently-justified tiering decisions that happened to look similar.

21.4 Image size as a cost lever, not just a security one

Directly restating Section 2.3's Alpine-base-image and Section 2.4's layer-caching decisions through a cost lens Section 2 itself only partially addressed: smaller images mean lower registry storage cost, faster (cheaper) CI build and image-pull time (Section 2.9's build-time budget), and reduced data-transfer cost for multi-region image distribution (relevant to Section 17.3's secondary-region DR posture, which requires images available in a second region too) — Section 2's security-driven minimal-image-size decision and this section's cost-driven interest in the same property are two independent justifications converging on the identical engineering practice, worth noting explicitly as a case where security and cost optimization align rather than compete, a useful contrast to Volume 3, Section 16.9's more common case of cost and quality genuinely trading off against each other.

21.5 Reserved capacity vs. on-demand

For the managed/hosted deployment's baseline, predictable capacity (Section 19.4's growth-projection floor), reserved/committed-use infrastructure pricing (available from cloud providers at a meaningful discount relative to on-demand pricing, in exchange for a committed spend term) covers the steady-state baseline, while HPA-driven burst capacity (Section 3.7, 9.4) above that baseline uses on-demand pricing — this hybrid approach directly serves cost efficiency for the predictable majority of load while preserving Section 3.7's autoscaling responsiveness for genuine demand spikes, rather than either over-committing to reserved capacity that would sit idle most of the time or paying on-demand rates for load that's actually entirely predictable.

21.6 Sandbox execution cost — the trust-tiered model's cost dimension

Directly extending Volume 1, Part III, Section 18.5's trust-tiered execution model with its cost consequence, a dimension that section didn't itself address: Community-tier sandboxed module execution (Section 3.2, 3.8's dedicated nexus-sandbox node pool) carries meaningfully higher per-execution infrastructure cost than in-process Built-in/Verified-tier execution, given the dedicated pod-per-invocation isolation model (Volume 1, Part III, Section 18.5) — this cost difference is a legitimate input to Volume 0, Section 16's licensing/pricing considerations for marketplace-heavy usage patterns, flagged here as a concrete infrastructure-cost data point that pricing strategy (owned by Volume 0) should account for, consistent with this document set's practice of surfacing cross-volume-relevant findings explicitly rather than leaving them for another volume to independently rediscover.

21.7 The unified Cost Overview — infrastructure and AI cost, correlated

Directly completing Section 10.3's "Cost Overview" dashboard, named there but not detailed: infrastructure cost (this section) and AI provider cost (Volume 3, Section 16.2's AiCostRecord data) are surfaced on the same dashboard, correlated by time and, where meaningful, by tenant (for the enterprise cost-visibility view, Volume 3, Section 16.8, extended here to include the infrastructure-cost-per-tenant estimate the pooled model's resource-attribution tagging, Section 8.7, makes computable) — giving engineering leadership (this dashboard's primary internal audience, per Section 10.3's table) one place to answer "what does it cost us to run Nexus, in total" rather than needing to separately consult this volume's infrastructure billing and Volume 3's AI provider billing as two disconnected data sources.

21.8 Cost as a release consideration, not just an operational afterthought

Consistent with Volume 3, Section 16.1's framing of cost governance as "a first-class product capability, not an operational afterthought," extended here to infrastructure specifically: a proposed architectural change with a material infrastructure-cost impact (a new stateful dependency, a significant resource-sizing increase) is required to include a cost-impact estimate in its design review, using the same ADR-for-deviations process Volume 1, Part VI, Section 40.8 established — cost is treated as a design constraint reviewed at decision time, not a bill discovered after the fact and addressed reactively.

21.9 Closing note — Volume 4 complete

This section closes Volume 4 in full: Part I specified the developer and containerization foundation, Part II the pipeline that ships changes safely, Part III the observability stack that watches the running system, Part IV the data and secrets infrastructure protecting it, and this Part — Disaster Recovery, High Availability, Scaling, Backup Strategy, and now Cost Optimization — the resilience and operational discipline that keeps all of it running reliably and affordably over time. Every infrastructure decision in this volume traces back through Volume 1's NFRs and architecture principles to Volume 0's original business objectives, completing the fourth of six volumes in this document set. Volume 5 (Development Standards & Engineering Playbook) now turns to the human and process discipline — git workflow, code review, release management, and team practices — that governs how the system this and the prior three volumes specified is actually built and maintained day to day.

______________________________________________________________________________________________________________________________________________________________________


