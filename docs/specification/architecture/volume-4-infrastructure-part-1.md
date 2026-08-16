# Volume 4 — Infrastructure Architecture · Part I — Developer Environment & Containerization

> **Status note (added on import, 2026-08-16):** This document was supplied by the user as reference architecture text and renamed from a "Nexus"-branded source to Yukti's naming throughout (`Nexus.Api` → `Yukti.Api`, `nexus-orchestration` → `yukti-orchestration`, `nexus-gui` → `yukti-gui`, etc.). Two things worth stating plainly before treating this as ground truth:
>
> 1. **This describes target/aspirational infrastructure, not what exists in this repo today.** As of this session, `yukti-platform` has no `docker-compose.dev.yml`, no Dockerfiles, no Kubernetes manifests, no Helm charts, and no GitHub Actions workflows. `Yukti.Orchestration` is a class library invoked in-process by `Yukti.Api` (confirmed live this session — `Program.cs`'s `POST /flows/{id}/runs` runs `FlowEngine.Execute` inline and says so in its own comment), not yet the separate `yukti-orchestration` container/service this document describes. Only `docs/specification/product/INIT-YUKTI-BACKEND-001.md` (Volume 1's backend PRD) and the per-module specs under `docs/specs/modules/` currently exist as real architecture documents in this repo.
> 2. **Cross-references to Volume 0, Volume 2, Volume 3, and Volume 5 point to documents that do not exist in this repo.** This text cites them extensively (e.g. "Volume 0 §21.4", "Volume 2 §19.8", "Volume 3 §14.2") as if they're available for cross-checking — they aren't, here. Treat those references as provenance from wherever this text originated, not as verifiable links within this repository.
>
> The rename pass below is mechanical (naming only) — it has not been fact-checked line-by-line against the actual `yukti-platform` codebase's specific metric names, connection-string keys, or resource sizing. Numeric budgets, cited section numbers within *this* document, and structural claims (e.g. the four container images, the namespace strategy) are preserved as given.

## 1. Development Environment

### 1.1 Purpose and the dev/prod parity principle

This volume specifies how the system Volumes 1–3 designed is actually built, deployed, and operated. This opening section specifies the local development environment every engineer works in day to day, governed by one overriding principle worth stating before any tooling detail: **local development should be as structurally similar to the self-hosted production topology as practical**, directly extending Architecture Principle 20.8 (Volume 1, Section 20.8 — "self-hosted is a first-class target, not a downscoped afterthought") to development itself. An engineer's laptop is not a special, permanently-simplified environment with its own divergent architecture — it is the smallest instance of the same topology Volume 1, Part VI, Section 37 already specified.

### 1.2 Local stack — docker-compose as the development topology

```yaml
# docker-compose.dev.yml (abbreviated)
services:
  postgres:
    image: postgres:16
    environment: { POSTGRES_DB: yukti_dev }
    ports: ["5432:5432"]
  redis:
    image: redis:7
    ports: ["6379:6379"]
  yukti-api:
    build: { context: ., dockerfile: src/Yukti.Api/Dockerfile }
    depends_on: [postgres, redis]
    volumes: ["./src:/app/src"]     # hot-reload, Section 1.4
  yukti-orchestration:
    build: { context: ., dockerfile: src/Yukti.Orchestration/Dockerfile }
    depends_on: [postgres, redis]
  yukti-gui:
    build: { context: ./yukti-gui }
    ports: ["3000:3000"]
    volumes: ["./yukti-gui/src:/app/src"]
```

This directly mirrors Volume 1, Part VI, Section 37.3's container inventory — `yukti-api` and `yukti-orchestration` remain separate services even in local development, never collapsed into one process for developer convenience, specifically because Volume 1, Part I, Section 4.4 identified their independent scaling profile as architecturally significant; a developer who only ever runs them combined would never notice a bug that only manifests when they're genuinely separate processes (e.g., an accidental in-process assumption leaking across what should be a container boundary).

### 1.3 AI provider stubbing for offline/low-cost local development

Directly extending Volume 3, Part IV, Section 14.2's `AiProvider` interface: local development uses a stub implementation (`StubAiProvider`) by default, returning deterministic, schema-valid fixture responses for each of Volume 3's seven capabilities (Part III there) — never requiring a real, costed API key for routine local development, and never making a real outbound call by default. This is a direct, practical application of Volume 3's own AI-optionality architecture (Section 1.4 there) to the development workflow itself: an engineer working on, say, Volume 2's Workflow Designer UI should never need an AI provider credential just to run the application locally. A developer actively working on Volume 3's capability engines opts in to a real provider connection via a local environment variable, with Volume 3, Part IV, Section 16's cost-tracking infrastructure equally active in that mode, so even ad hoc developer experimentation is visible in cost reporting, not a blind spot.

### 1.4 Hot reload and inner-loop speed

`yukti-api` and `yukti-gui` (1.2) mount source directories as volumes with framework-native hot-reload (`dotnet watch` for the API, Vite's dev server for the React GUI, Volume 2 Section 4) — `yukti-orchestration`, given its background-worker nature and the incremental-commit execution loop specified in Volume 1, Part III, Section 19.2, restarts on change rather than hot-reloading, since a mid-flow-execution hot-reload would risk exactly the kind of ambiguous, hard-to-reason-about state Volume 1's per-step commit design (Section 16.7 there) was built to avoid — a deliberate, narrow exception to blanket hot-reload, justified by that specific component's execution semantics.

### 1.5 Seed data

A deterministic seed script populates local PostgreSQL with a representative dataset — a handful of `Flow`s spanning multiple modules, a few completed `FlowRun`s with realistic `StepResult` histories (including at least one self-healed step, ensuring `AiAttribution` rendering, Volume 2 Section 9.3, is exercisable locally without a real AI provider call, using Section 1.3's stub), and baseline RBAC roles (Volume 1, Part IV, Section 25.2) — giving every new engineer a working, realistic environment within minutes of first setup, directly serving the same onboarding-speed concern Volume 0's NFR-UX-2 targeted for end users, now applied to engineer onboarding specifically.

### 1.6 TLS in local development

Local development uses locally-trusted, self-signed certificates (via `mkcert` or equivalent) rather than plain HTTP — not because local traffic faces real external threats, but because Section 16 (TLS, later in this volume) specifies TLS as universally required in every environment including self-hosted (per NFR-SEC-1), and a development environment that's structurally different in this specific respect risks an engineer's mental model of "how Yukti handles TLS" being subtly wrong, or a TLS-dependent bug (mixed-content issues, cookie `Secure` flag behavior per Volume 2, Section 14.2) going undetected until a staging or production environment surfaces it.

### 1.7 Developer environment as versioned, reviewed configuration

The full `docker-compose.dev.yml` and its accompanying setup scripts are versioned in the same repository as application code, reviewed via the same pull-request process (Volume 5's eventual responsibility to formalize in full) as any other change — a modification to the local development topology is treated with the same rigor as a change to the production Helm charts (Section 4), since Section 1.1's parity principle only holds if both are kept deliberately, continuously synchronized rather than allowed to drift independently over time.

### 1.8 What local development deliberately does not replicate

Consistent with this document set's practice of stating scope honestly (Volume 2, Section 16.1's model): local development does not replicate Volume 1, Part VI, Section 37.3's horizontal autoscaling, the Redis-backed SignalR backplane's multi-instance behavior (Volume 1, Part V, Section 31.5 — a single local `yukti-api` instance is sufficient for local UI development), or Volume 3, Part IV, Section 14.7's circuit-breaker behavior under real provider degradation. These are explicitly reserved for the staging environment (Section 5, CI/CD, will specify the deployment pipeline that promotes code there) — local development optimizes for fast inner-loop iteration on business logic, not for validating infrastructure-scale behavior, which is a deliberate, different environment's job.

## 2. Docker

### 2.1 Purpose — the image-build specification behind Volume 1's container inventory

Volume 1, Part VI, Section 37.2 named the four container images (`yukti-api`, `yukti-orchestration`, `yukti-cli`, `yukti-sandbox-runner`) at the architectural level. This section specifies how each is actually built — base images, layer strategy, and the security scanning that gates promotion.

### 2.2 Multi-stage build pattern

Every image follows the same multi-stage structure Volume 1, Part VI, Section 37.2 already committed to in principle ("SDK image for build, runtime-only image for the final artifact"), specified here concretely:

```dockerfile
# yukti-api/Dockerfile (representative structure)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/Yukti.Domain/*.csproj src/Yukti.Domain/
COPY src/Yukti.Application/*.csproj src/Yukti.Application/
COPY src/Yukti.Infrastructure/*.csproj src/Yukti.Infrastructure/
COPY src/Yukti.Api/*.csproj src/Yukti.Api/
RUN dotnet restore src/Yukti.Api/Yukti.Api.csproj    # dependency layer, cached
                                                          # independently of source changes
COPY src/ src/
RUN dotnet publish src/Yukti.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
RUN addgroup -S yukti && adduser -S yukti -G yukti    # non-root, Section 2.5
WORKDIR /app
COPY --from=build /app .
USER yukti
ENTRYPOINT ["dotnet", "Yukti.Api.dll"]
```

The `.csproj`-only copy before the full source copy is a deliberate Docker-layer-caching optimization directly serving CI build-time performance (Part II, Section 5's CI/CD pipeline speed) — a source-only change doesn't invalidate the (comparatively slow) `dotnet restore` layer, since only a dependency change would have changed the `.csproj` files that layer depends on.

### 2.3 Base image selection

Alpine-based runtime images (`aspnet:8.0-alpine`, and for `yukti-gui`'s static-asset-serving image, `nginx:alpine`) are preferred over full Debian-based images specifically to minimize image size and attack surface — directly serving both Volume 1, Part VI, Section 37.2's "minimal image size and attack surface" goal and this volume's later disaster-recovery concerns (Part V — smaller images mean faster image pulls during a scale-out or recovery event). The one deliberate exception: `yukti-sandbox-runner` (Volume 1, Part III, Section 18.5's Community-tier module isolation) uses a purpose-built minimal image with an even more restrictive package set than Alpine's default, reviewed specifically for this image's elevated security requirements (running untrusted third-party module code) as part of Section 2.5's scanning discipline.

### 2.4 Layer strategy for the yukti-orchestration image specifically

`yukti-orchestration`'s image additionally bundles Playwright's browser binaries (Volume 0, Section 21.4's locked Web-module technology) as a distinct, cached layer separate from the application code layer — since browser binaries are large and change far less frequently than application code, this ordering keeps routine application deployments from re-downloading/re-layering multi-hundred-megabyte browser binaries on every release, a meaningful CI/CD and registry-storage cost optimization directly relevant to this Part's later Cost Optimization section (21).

### 2.5 Image scanning and security gates

Every built image is scanned (Trivy or equivalent) for known CVEs in both OS packages and application dependencies before being eligible for deployment — this scan is a mandatory CI gate (Part II, Section 5), not an advisory report reviewed after the fact, directly extending Volume 1, Part VI, Section 40's "security designed in, not bolted on" discipline (Architecture Principle 20.5) to the container-image layer specifically. Every runtime image runs as a non-root user (2.2's `USER yukti` directive) with a read-only root filesystem where the application's own design permits it (true for `yukti-api`; `yukti-orchestration` requires limited writable scratch space for the Web module's temporary browser-profile data, explicitly scoped and documented as the one exception to this rule).

### 2.6 Image tagging and registry strategy

Images are tagged with the full Git commit SHA (immutable, traceable to exact source) as the primary tag, with a mutable `latest` tag reserved strictly for local development convenience (2.2's compose file) and never used in any deployed environment — deploying by SHA-pinned tag is a hard requirement (enforced by Helm chart validation, Section 4), directly serving Volume 1, Part VI, Section 37.8's rollback procedure: a rollback to a specific prior Helm revision must deterministically resolve to the exact same image that was actually running at that revision, which a mutable tag could not guarantee.

### 2.7 Registry architecture for self-hosted, air-gapped deployments

Directly extending Volume 1, Part VI, Section 37.7's air-gapped installation path: images are published to a public registry for the standard managed/hosted and internet-connected self-hosted cases, with an explicit, documented image-mirroring procedure (`docker save`/`docker load`, or a private-registry-sync tool) for air-gapped customers — this section is where that Volume 1-referenced "fully detailed air-gapped installation procedure (image mirroring to a private registry)" is actually specified, closing a forward reference Volume 1 deliberately left to this volume.

### 2.8 Local build parity with CI builds

The exact same Dockerfiles specified in this section build both local development images (Section 1.2's compose file) and CI/production images — no separate "dev Dockerfile" diverging from what actually ships, directly reinforcing Section 1.1's dev/prod parity principle at the image-build level specifically, and eliminating an entire class of "works locally, breaks in the built image" bug that a divergent local build process would otherwise risk.

### 2.9 Image build performance budget

Consistent with Volume 1, Part VI, Section 38's performance-as-release-gate discipline, applied here to build infrastructure itself: full CI image builds (all four images, cold cache) are budgeted at under 10 minutes total, with warm-cache incremental builds (the common case, per 2.2's layer-caching design) budgeted under 3 minutes — a build-time regression beyond these budgets is investigated with the same seriousness Volume 1 and Volume 2 both established for their respective performance regressions, since slow CI builds directly degrade every engineer's inner-loop velocity, not just one team's.

## 3. Kubernetes

### 3.1 Purpose — deepening Volume 1's reference topology

Volume 1, Part VI, Section 37.3 established the reference Kubernetes topology at the architectural level — which containers exist, how they relate. This section specifies the operational Kubernetes detail beneath that: namespace strategy, resource governance, and cluster-level policies that make the topology actually production-ready, not just architecturally correct.

### 3.2 Namespace strategy

```
yukti-system          # yukti-api, yukti-orchestration, outbox relay (Volume 1 Part III §22.2)
yukti-data             # PostgreSQL, Redis (self-hosted deployments only — managed
                          deployments typically use external managed services here instead)
yukti-observability     # Prometheus, Grafana, OpenTelemetry Collector (Section 8-10)
yukti-sandbox           # ephemeral yukti-sandbox-runner pods (Volume 1 Part III §18.5),
                           isolated into its own namespace specifically so its distinct,
                           stricter network and resource policies (3.5) don't need to be
                           reasoned about alongside yukti-system's trusted workloads
```

For multi-tenant managed/hosted deployments (Volume 1, Part IV, Section 26.1's pooled model), this namespace structure is per-cluster, not per-tenant — tenant isolation is enforced at the application/database layer exactly as Volume 1, Part IV, Section 26.3 specified, not by Kubernetes namespace boundaries, consistent with that section's explicit rejection of database-per-tenant (and, by extension, cluster-or-namespace-per-tenant) as the GA isolation model. For dedicated-instance deployments (Volume 1, Part IV, Section 26.6), this entire namespace structure is deployed once per dedicated cluster, giving that isolation model genuine infrastructure-level separation matching its stronger isolation promise.

### 3.3 Resource requests and limits

Every container specifies both CPU/memory requests (guaranteed minimum, used for scheduling) and limits (hard ceiling), sized per Volume 1's own performance specification — `yukti-orchestration` pods are sized with headroom for Volume 1, Part VI, Section 39.3's browser-process-pool resource profile (the Web module's Playwright instances being the dominant memory consumer per that section's resource-pool table), while `yukti-api` pods are sized for its comparatively lightweight, stateless request-handling profile. Requests are set conservatively (never zero, avoiding the Kubernetes anti-pattern of unbounded best-effort scheduling that risks node-level resource contention) and validated periodically against actual observed usage (Section 7's monitoring stack) rather than set once at initial deployment and never revisited.

### 3.4 Pod Disruption Budgets and graceful shutdown

Directly implementing Volume 1, Part VI, Section 37.6's graceful-shutdown requirement at the Kubernetes-policy level: a PodDisruptionBudget for `yukti-orchestration` ensures voluntary disruptions (node draining during a cluster upgrade, for instance) never take down more than a configured fraction of running orchestration pods simultaneously, giving Volume 1's `terminationGracePeriodSeconds` and in-flight-execution-draining logic (Section 37.6 there) room to actually complete rather than being raced by an aggressive, unbudgeted rolling node replacement.

### 3.5 Network policies — the sandbox namespace's stricter isolation

Directly implementing Volume 1, Part III, Section 18.5's sandboxed-execution security model at the Kubernetes-network level: `yukti-sandbox` namespace pods (running potentially-untrusted Community-tier module code) are governed by a default-deny NetworkPolicy, with explicit, narrow allow-rules only for the specific gRPC callback path back to `yukti-orchestration` (Volume 1, Part III, Section 18.5's communication design) — no sandboxed pod can initiate a connection to `yukti-data`, the public internet, or any other namespace, directly enforcing at the cluster-network layer the same trust boundary Volume 1, Part I, Section 2.5 established conceptually and Volume 1, Part III, Section 18.3's narrow `ExecutionContext` enforced at the application layer — this is defense-in-depth's third layer for that specific boundary, network policy joining application-layer scoping and process isolation.

### 3.6 Ingress and TLS termination

A single Ingress controller (NGINX Ingress or a cloud-provider-native equivalent for managed deployments) routes external traffic to `yukti-api` (REST and SignalR, Volume 1, Part V, Sections 30–31) with TLS termination at the ingress layer — full detail of the TLS/certificate strategy is Section 16 (TLS) of this Part's later coverage; this section notes only that Ingress-level termination, rather than per-pod TLS, is the chosen architecture, re-encrypting internally only where Section 16's zero-trust posture requires it for the self-hosted regulated-industry deployment profile (Persona Elena's segment, Volume 0 Section 10.6).

### 3.7 Horizontal Pod Autoscaling — restating and completing Volume 1's design

Volume 1, Part VI, Section 37.4 already specified that `yukti-orchestration`'s HPA scales against the custom `yukti.orchestration.concurrent_executions` metric (Volume 1, Part IV, Section 29.4) rather than generic CPU/memory. This section adds the concrete Kubernetes mechanics: a PrometheusAdapter (or equivalent custom-metrics-API adapter) exposes that Prometheus-collected metric to Kubernetes' HPA controller, with scale-up/scale-down thresholds and cooldown windows tuned to avoid flapping (rapid, oscillating scale up/down driven by a bursty metric) — a specific, named operational concern this section adds beyond Volume 1's architectural commitment.

### 3.8 Node pools and workload separation

Where cluster size justifies it (typically larger managed/hosted deployments, per Volume 1, Part IV, Section 26.2's pooled-scale reasoning), `yukti-orchestration` (compute- and memory-intensive, per 3.3) and `yukti-api` (lightweight, high-request-rate) run on separate node pools sized appropriately for each workload's profile, with `yukti-sandbox` on a third, more restrictively-configured pool (smaller instances, since individual sandbox executions are bounded, per Volume 1, Part III, Section 18.5's resource-limit requirement) — a cost-and-performance optimization available to larger deployments, while smaller self-hosted deployments (Architecture Principle 20.8) run everything on a single, undifferentiated node pool without needing this separation to function correctly, only to scale most efficiently.

### 3.9 Cluster-level RBAC — Kubernetes RBAC, distinct from Volume 1's application RBAC

A clarification worth stating precisely, given Volume 1, Part IV, Section 25 already specified an application-level RBAC model: Kubernetes' own RBAC (governing which humans/service accounts can modify cluster resources — deployments, secrets, network policies) is a distinct, infrastructure-layer permission system, never conflated with Volume 1's `Permission` enum (Volume 1, Part IV, Section 25.1). A Yukti platform administrator (Volume 1's Administrator role) does not thereby gain Kubernetes cluster-admin access — the two systems are deliberately unconnected, each governing its own layer, consistent with this document set's recurring practice of keeping distinctly-scoped concerns architecturally separate rather than collapsing them for convenience (the same discipline Volume 1, Part IV, Section 27.7 applied to Audit versus Logging, now applied here to application RBAC versus infrastructure RBAC).

## 4. Helm

### 4.1 Purpose — the packaging and deployment mechanism

Volume 1, Part VI, Section 37.3 committed to Helm charts as the deployment mechanism for both managed/hosted and self-hosted topologies, from one identical chart set. This section specifies that chart's actual structure and the values-file strategy that lets one chart set serve genuinely different deployment scales and environments without forking.

### 4.2 Chart structure — umbrella chart over per-component subcharts

```
yukti-helm/
├── Chart.yaml                    # umbrella chart
├── values.yaml                    # platform-wide defaults
├── values.managed-hosted.yaml      # overrides for Yukti-operated infrastructure
├── values.self-hosted.yaml         # overrides for customer-operated, internet-connected
├── values.air-gapped.yaml          # overrides for Volume 1 §37.7's air-gapped path
├── charts/
│   ├── yukti-api/
│   ├── yukti-orchestration/
│   ├── yukti-sandbox-runner/
│   ├── postgresql/                 # bundled subchart, used only when values specify
│   │                                  self-hosted-without-managed-database (4.4)
│   └── redis/                      # same conditional-inclusion pattern
└── templates/
    ├── networkpolicy.yaml           # Section 3.5
    ├── ingress.yaml                 # Section 3.6
    └── hpa.yaml                     # Section 3.7
```

The umbrella-chart-with-subcharts structure directly mirrors Volume 1, Part V, Section 36.2's solution structure decision (per-component projects under one solution) — the same organizational philosophy applied one layer up, at the deployment-packaging level, for the same reason: each component (`yukti-api`, `yukti-orchestration`, `yukti-sandbox-runner`) has its own independently-versionable chart, composed by the umbrella chart, rather than one monolithic template set that couples every component's deployment configuration together.

### 4.3 Values-file hierarchy and precedence

`values.yaml` establishes safe, sensible defaults; environment-specific files (`values.managed-hosted.yaml`, etc.) override only what genuinely differs — replica counts, resource sizing (Section 3.3), and which subcharts are enabled (4.4) — never duplicating the full configuration surface per environment. A customer's actual deployment additionally supplies a final, thin `values.customer.yaml` layer (database connection strings, AI-provider configuration per Volume 3, Part IV, Section 14, license key) — this three-tier layering (platform defaults → environment profile → customer specifics) is what lets Volume 1, Part VI, Section 37.3's "identical chart set, differing only in values files" claim hold precisely, with each tier's role kept distinct and minimal.

### 4.4 Conditional subchart inclusion — managed database vs. bundled

```yaml
# values.self-hosted.yaml (excerpt)
postgresql:
  enabled: true      # bundled subchart deploys PostgreSQL within the cluster
redis:
  enabled: true

# values.managed-hosted.yaml (excerpt)
postgresql:
  enabled: false     # Yukti-operated managed database service used instead;
                        connection details supplied via values.customer.yaml-equivalent
redis:
  enabled: false
```

This conditional pattern is what allows a resource-constrained self-hosted customer (Architecture Principle 20.8) to `helm install` a single, complete, self-contained deployment with zero external dependencies beyond the cluster itself, while a larger managed deployment or a self-hosted customer with existing database infrastructure points at external, already-managed PostgreSQL/Redis instances (Volume 0, Section 21.8–21.9's technology choices, deployment-topology-agnostic by design) — the same chart, genuinely different operational posture, purely through values configuration.

### 4.5 Chart versioning and release correlation

Chart versions follow semantic versioning independent of, but correlated with, the application image tags they reference (Section 2.6's SHA-based tagging) — a chart version bump accompanies any change to the deployment topology itself (a new NetworkPolicy rule, a changed resource default), while an image-only update (a routine application release with no infrastructure change) can reuse the same chart version with only the image tag values changed, keeping the two versioning concerns — "how is this deployed" versus "what code is running" — appropriately decoupled, mirroring the same decoupling Volume 3, Part I, Section 3.7 established between prompt-version releases and Planning Engine code releases.

### 4.6 Chart testing

Every chart change is validated via `helm lint`, a `helm template` dry-run diffed against the previous version (catching unintended resource changes), and a full deployment test against an ephemeral test cluster (spinning up the complete umbrella chart, running Volume 2, Section 19.9's E2E smoke-test subset against it) before being eligible for release — directly extending Volume 1, Part VI, Section 37.5's migration-gates-rollout principle to the chart level generally: infrastructure changes are verified before they reach any real environment, never validated for the first time against staging or, worse, production.

### 4.7 Self-hosted installation experience — restating Volume 1's procedure with full detail

Directly completing Volume 1, Part VI, Section 37.7's installation summary: `helm repo add yukti https://charts.yukti.dev` (or, for air-gapped deployments per Section 2.7 of this Part, a locally-mirrored chart repository), then `helm install yukti yukti/yukti-helm -f values.self-hosted.yaml -f values.customer.yaml` — a single command producing a complete, running deployment, with `helm upgrade` following the same migration-gated pattern Volume 1, Part VI, Section 37.5 specified for schema changes (the chart's upgrade hooks trigger the migration Job before the application Deployment rolls out the new image version).

### 4.8 Secrets in Helm values — what never appears in a values file

Directly extending Volume 1, Part IV, Section 24.4 and NFR-SEC-2's secret-handling discipline to the deployment-configuration layer: no `values.yaml` file, at any tier (4.3), ever contains a raw secret value (database password, AI-provider API key, TLS private key) — these are referenced by name/path against Section 15's Secrets management and Section 15's Vault integration, with Helm's role limited to specifying which secret reference to use, never the secret's actual value, keeping every values file safe to commit to version control (4.9) without exception.

### 4.9 Chart source control and review

The Helm chart repository is versioned and reviewed with the same rigor as application source code (Section 1.7's principle restated at the deployment-packaging layer specifically) — a chart change is a pull request, reviewed against Section 4.6's automated checks plus human review for anything touching Section 3.5's network policies or Section 3.9's RBAC configuration specifically, those being the highest-security-impact categories of change this chart can express, warranting the same elevated review attention Volume 1, Part VI, Section 40.3 gave to security-sensitive backend code changes.

## 5. CI/CD

### 5.1 Purpose — the pipeline that consolidates every quality gate this document set has specified

This section is a deliberate synthesis point: across four volumes, this document set has specified more than a dozen distinct quality gates — Volume 1's coding-standard analyzers and load-testing gate, Volume 2's bundle-size budgets and Lighthouse CI checks, Volume 3's evaluation-set release gates, Volume 4's own image scanning and chart testing. None of these are useful in isolation; they only function as a coherent quality bar when assembled into one pipeline every change passes through identically. This section is where that assembly is specified, referencing every prior gate by its originating section rather than re-deriving any of them.

### 5.2 Pipeline stages

```
1. Lint & Static Analysis
   - Backend: Roslyn analyzers (Volume 1, Part VI §40.2) — strongly-typed IDs,
     dependency-direction, structured-logging-only rules
   - Frontend: ESLint import-boundary rules (Volume 2, Section 5.3), TypeScript
     strict-mode compilation (Volume 2, Section 4.7)
   - Infrastructure: helm lint (Section 4.6), Dockerfile linting (hadolint)

2. Unit & Component Tests
   - Backend: Yukti.Domain.Tests, Yukti.Application.Tests (Volume 1, Part VI §40.6)
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
   - Yukti.Infrastructure.IntegrationTests, Yukti.Api.IntegrationTests against
     ephemeral test-container PostgreSQL/Redis (Volume 1, Part VI §40.6)
   - Frontend integration tier with MSW (Volume 2, Section 19.6)

6. End-to-End Tests
   - Full Playwright/Yukti-self-testing E2E suite (Volume 2, Section 19.8) against
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
```

Every stage after stage 1 depends on the previous stage's success — this is a strictly sequential pipeline, not a parallel-everything race to the fastest possible feedback, because several later stages (integration tests, E2E) are expensive enough that running them against a build that hasn't even passed linting would waste real compute cost for no benefit, directly consistent with Volume 3, Section 17.8's "reject cheaply before doing anything expensive" reasoning, applied here to the pipeline's own structure.

### 5.3 Environments

| Environment | Purpose | Deployment trigger |
|---|---|---|
| Local | Individual developer inner loop (Section 1) | Manual |
| Ephemeral (per-PR) | Isolated environment per pull request, torn down after merge/close, used for stage 6's E2E run and reviewer manual verification | Automatic, per PR |
| Staging | Pre-production verification (stages 7–8), production-topology-equivalent | Automatic, on merge to main |
| Production | Live customer traffic (managed/hosted) | Manual approval (5.5) after staging verification |

Self-hosted customer deployments (Volume 1, Part VI, Section 37.7) are a distinct concept from this internal environment progression — a self-hosted customer's own environment is their production, reached via the release artifact this pipeline produces (a specific, versioned Helm chart + image set), not a stage this pipeline directly deploys to.

### 5.4 The ephemeral per-PR environment — why it exists

A full, isolated deployment per open pull request (rather than a shared, persistent staging environment alone) directly serves two goals: it lets stage 6's E2E suite (Volume 2, Section 19.8) run against exactly the code under review, with zero risk of interference from a concurrently-merged, unrelated change also deploying to a shared staging environment at the same time — and it gives a human reviewer a genuinely live, clickable environment to manually verify a UI change against, rather than reviewing only static code diffs. This is judged worth its infrastructure cost specifically because Volume 2's UX-quality bar (Section 1, UX Principles) is difficult to fully verify from a code diff alone — seeing the actual rendered Workflow Designer (Volume 2, Section 11) change is a meaningfully better review signal than reading the React code that produces it.

### 5.5 Production promotion — manual gate, automated everything else

Every stage through 5.2's stage 8 (Staging Verification) is fully automated with no human approval required — a change that passes every gate is, by this pipeline's own definition, ready. Stage 9 (Promote to Production) still requires an explicit human approval click, not because the automated gates are distrusted, but because production promotion timing itself is a business/operational decision (avoiding a deploy immediately before a weekend, coordinating with a customer-communication plan for a significant change) distinct from whether the change is correct — a deliberate, narrow separation of "is this safe to ship" (fully automated) from "is this the right moment to ship it" (human judgment), consistent with Volume 1, Architecture Principle 20.4's fail-fast philosophy not being about removing human judgment entirely, only about removing it from the correctness determination specifically.

### 5.6 Rollback integration

A failed stage-8 verification, or a post-promotion incident, triggers the exact rollback procedure Volume 1, Part VI, Section 37.8 already specified (`helm rollback` to the prior revision) — this pipeline's role in rollback is limited to making that prior-revision Helm release readily available (every successful pipeline run's artifacts are retained per Section 20's backup/retention policy, this Part's later coverage) rather than reinventing rollback mechanics this volume already owns elsewhere.

### 5.7 Pipeline observability — the pipeline monitors itself

Consistent with Volume 1, Part IV, Section 29.1's "the platform observing itself" principle, extended here to development infrastructure: pipeline stage duration, failure rate per stage, and flake rate for the E2E suite specifically (a flaky E2E test undermines trust in the entire gate it belongs to) are themselves tracked metrics, feeding the same OpenTelemetry-based observability stack (Section 8, later in this Part) the running application uses — a pipeline that's slow or unreliable is treated as an infrastructure quality problem worth the same monitoring rigor as the production system it exists to protect.

## 6. GitHub Actions

### 6.1 Purpose — the concrete implementation of Section 5's pipeline

Section 5 specified the pipeline's stages and philosophy in tool-agnostic terms. This section specifies the actual GitHub Actions implementation — the technology choice implied by Volume 0, Section 21.14's reference to "GitHub Actions" as effectively locked, formally confirmed and detailed here.

### 6.2 Workflow structure — composite actions over duplicated YAML

```yaml
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
```

Every stage from Section 5.2 is implemented as a reusable workflow (`_reusable-*.yml`), never duplicated inline across the several top-level workflow files that need it (`pr-validation.yml`, a separate `main-branch-deploy.yml` for the staging/production promotion path, a scheduled `nightly-full-regression.yml` running the complete Volume 2/Volume 3 evaluation suites beyond what every-PR feedback-speed budgets allow) — this is the CI-pipeline-level application of the same "define once, reuse everywhere" discipline this document set has applied consistently since Volume 2 (Sections 9.4, 10.6, 12.5 there).

### 6.3 backend-tests implementation detail

```yaml
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
          ConnectionStrings__Default: "Host=localhost;Database=yukti_test;..."
```

GitHub Actions' native `services:` block provisions ephemeral PostgreSQL/Redis containers scoped to this job's lifetime, directly serving Volume 1, Part VI, Section 40.6's requirement that integration tests run against real infrastructure, never mocked — the CI environment satisfies this without requiring a persistent shared test database that could develop cross-run state contamination.

### 6.4 Caching strategy

Dependency caching (`actions/cache`, keyed by `packages.lock.json`/`package-lock.json` hash) covers both `dotnet restore` and `npm install` outputs, directly complementing Section 2.2's Docker-layer caching (the two caching layers — CI dependency cache and Docker build cache — serve different parts of the pipeline and are both necessary, not redundant: dependency caching speeds up the test stages that run before any image is built at all). Cache invalidation is automatic and hash-based, never manually managed, avoiding the class of stale-cache bug that manually-keyed caching strategies are prone to.

### 6.5 Matrix builds — where they're used, and where they're deliberately not

A build matrix runs the frontend test suite (Volume 2, Section 19.2) across the minimum supported browser set for E2E purposes (since Volume 0, Section 21.4 locked Playwright specifically for its multi-browser support, and that capability should be exercised in CI, not just claimed architecturally) — but backend tests do not run a multi-OS or multi-.NET-version matrix, since Volume 0, Section 21.2 locked a single .NET version and Linux-container-only deployment target (Section 2.3's Alpine base images), making a broader compatibility matrix pure CI cost with no corresponding product requirement it would actually validate.

### 6.6 Secrets management in CI — Vault integration, never GitHub Secrets alone for production credentials

Directly extending Section 4.8's "no raw secret in a values file" discipline to the pipeline itself: CI-time secrets needed only within the pipeline's own ephemeral execution (a test database password scoped to that single job run, per 6.3) use native GitHub Actions encrypted secrets, appropriate for their narrow, ephemeral scope. Secrets needed for actual deployment (Section 5.2 stage 7's staging deployment, and production promotion) are retrieved at deploy-time directly from Section 15's Vault infrastructure via a short-lived, workflow-scoped OIDC token (GitHub's native OIDC federation with Vault, avoiding any long-lived Vault credential ever being stored as a GitHub secret at all) — this is a deliberate, stronger posture than simply storing a Vault token as a GitHub secret, since a short-lived, workflow-run-scoped credential minimizes the blast radius of any single compromised workflow run.

### 6.7 Required status checks and branch protection

The main branch requires every job in Section 6.2's `pr-validation.yml` to pass before merge is permitted (GitHub's native branch-protection required-status-checks feature), with no administrator override path enabled — directly enforcing that Section 5's "every stage automated, human judgment reserved only for production-promotion-timing" philosophy (Section 5.5) cannot be quietly bypassed under time pressure, which is exactly the scenario such a bypass exists to be tempting during and exactly the scenario this document set's repeated "fail loud, never silently skip a check" discipline (Volume 1, Architecture Principle 20.4, applied consistently through every subsequent volume) argues most strongly against permitting.

### 6.8 Workflow-run cost and the nightly/PR split

Consistent with Volume 2, Section 19.9's smoke/full E2E split and Volume 3, Section 16's cost-optimization discipline, applied here to CI compute cost specifically: every-PR workflows run Volume 2's E2E smoke subset and a representative (not exhaustive) slice of Volume 3's evaluation sets, budgeted to keep PR feedback latency within a target window (e.g., under 20 minutes total, directly serving developer inner-loop velocity per Section 2.9's build-time-budget reasoning extended to the full pipeline); the complete E2E suite (every Volume 0 use case, Section 11) and full evaluation-set regression run on a nightly schedule against the current main branch, catching anything the necessarily-narrower PR-time checks didn't, with any nightly failure treated as a release-blocking incident requiring immediate triage, not a routine report reviewed at leisure.

### 6.9 Closing note for this Part

Sections 5 and 6 together complete the pipeline that enforces every quality standard this document set has specified across four volumes — from Volume 1's coding-standard analyzers through Volume 3's AI evaluation gates to this Part's own infrastructure checks. The next Part (Observability Stack) specifies how the running system — not just the pipeline that ships it — is continuously monitored once deployed.

## 7. Monitoring

### 7.1 Purpose — the strategy layer above Sections 8–11's tooling

Volume 1, Part IV, Section 29 already established observability's three pillars (logs, metrics, traces) and Volume 0, Section 21.11 locked the OpenTelemetry/Prometheus/Grafana stack. This section specifies the monitoring strategy those tools serve — SLIs/SLOs derived directly from the NFRs this document set has specified since Volume 0, and the alerting/escalation philosophy governing how a threshold breach becomes human attention. Sections 8–11 then specify the concrete tooling implementing this strategy.

### 7.2 Service Level Indicators — derived, not invented

Every SLI this section defines is a direct restatement of an NFR already committed to in Volume 0 or Volume 1 — this section adds no new quality targets, only operationalizes existing ones as continuously-monitored indicators:

| SLI | Derived from | Measurement |
|---|---|---|
| API availability | NFR-REL-1 (99.9%) | `/health/live` synthetic checks + real request success rate |
| Step-dispatch latency (p95) | NFR-PERF-1 (50ms) | `yukti.step.dispatch.duration` (Volume 1, Part IV §29.4) |
| Platform flake rate | NFR-REL-2 (<0.5%) | `yukti.flow.run.flake_detected` / `yukti.flow.run.completed` ratio (Volume 1, Part IV §29.5) |
| Concurrent execution headroom | NFR-SCALE-1 (500) | `yukti.orchestration.concurrent_executions` against configured capacity |
| Frontend Core Web Vitals | NFR-UX-2 (30-min onboarding, indirectly) | LCP/INP/CLS via RUM (Volume 2, Section 17.7) |
| AI request latency | NFR-PERF-4 (15s timeout) | `yukti.ai.request.duration` (Volume 1, Part IV §29.4) |
| Self-heal acceptance rate | Volume 0 §6.2 (≥70%) | Volume 3, Section 10.9's computed metric |

> Metric name note: this session confirmed `Yukti.Orchestration`'s actual OpenTelemetry meter is named `Yukti.Orchestration/1.0.0` (see `OrchestrationTelemetry.cs`), and metrics observed live include `step.dispatch.duration` — whether the full dotted namespace is literally `yukti.step.dispatch.duration` as written above hasn't been checked against the code; treat the exact metric names in this table as this document's intent, not a verified fact.

### 7.3 Service Level Objectives — the threshold that triggers action

Each SLI in 7.2 has a corresponding SLO — the specific threshold and time window that, when breached, constitutes an incident. SLOs are set with deliberate headroom below the NFR's own hard commitment (e.g., an internal SLO alerting at p95 dispatch latency exceeding 40ms, before NFR-PERF-1's 50ms commitment is actually violated) — directly extending Volume 1, Part IV, Section 29.6's "alert on symptom metrics before they become customer-visible" philosophy into a formal, numeric early-warning margin, so the team has time to respond before a customer-facing NFR is actually breached, not only after.

### 7.4 Error budgets

Each SLO (7.3) implies an error budget — the amount of acceptable deviation within a rolling window before it's treated as fully consumed. Consistent with Volume 1, Part VI, Section 38.9's "performance regression treated with the same severity as a functional bug" principle, an exhausted error budget for any SLO triggers a deliberate, visible policy: new feature deployment for the affected component pauses (not a hard technical block, but a required, explicit decision to proceed anyway, made visible to the same stakeholders Volume 0's risk register, Section 22, already involves) until the budget recovers or the underlying regression is fixed — directly operationalizing the tension between shipping velocity and reliability commitments as a conscious, tracked tradeoff rather than an implicit one.

### 7.5 Alerting severity and escalation

```
Critical  → pages on-call immediately (NFR-REL-1 breach in progress, security
             incident per Volume 3 §18.7, data-loss risk)
Warning   → notifies the responsible team's channel, next-business-day triage
             acceptable (SLO trending toward breach, per 7.3's early-warning margin)
Info      → dashboard-visible only, no notification (routine capacity/usage trends)
```

This three-tier severity model directly mirrors Volume 3, Section 17.7's guardrail-severity model (block/warn/flag-for-review) — the same "not every deviation deserves the same urgency" principle, applied here to operational alerting rather than AI guardrails, a deliberate consistency choice across this document set's various severity-tiering decisions (Volume 1, Part IV, Section 24.8's authentication-failure handling used a similar graduated-response philosophy).

### 7.6 On-call and ownership

Every SLI in 7.2's table has a named owning team (mirroring Volume 3, Section 19.7's per-metric ownership model exactly) — the Orchestration Engine's dispatch-latency SLI is owned by the backend team that owns Volume 1, Part III's execution engine; the AI-latency SLI is owned by the team owning Volume 3's Planning Engine; frontend Core Web Vitals are owned by the Volume 2 frontend team. A Critical alert (7.5) pages the specific owning team first, with a documented escalation path to a broader on-call rotation only if the primary owner doesn't acknowledge within a defined window — avoiding the common anti-pattern of a single, undifferentiated on-call rotation that lacks the specific context Volume 1 through Volume 3's deep, specialized architecture actually requires to diagnose quickly.

### 7.7 Runbooks — connecting an alert to a specific, known response

Every Critical and Warning alert links to a maintained runbook — for the platform flake-rate SLI specifically, for instance, a runbook referencing Volume 1, Part III, Section 19.5's retry/flake-handling design and Section 22's Event Bus health metrics, giving the responding engineer a starting investigation path grounded in this document set's own architecture rather than requiring them to rediscover it under incident pressure. Runbooks are versioned alongside the code they describe (mirroring Volume 1, Part VI, Section 40.7's documentation-as-code discipline) and are a required deliverable for any new SLI added to 7.2's table — an alert with no runbook is treated as an incomplete monitoring implementation, not shipped as-is.

### 7.8 Monitoring the monitoring — meta-observability

Directly extending Volume 4, Section 5.7's "the pipeline monitors itself" principle one layer further: the observability stack's own health (Prometheus scrape success rate, OpenTelemetry Collector queue depth, Grafana dashboard load latency — Sections 8–10's tooling) is itself monitored, with a dedicated, minimal, independently-hosted "is monitoring itself healthy" check that doesn't depend on the same infrastructure it's monitoring — avoiding the specific failure mode where an observability-stack outage is invisible precisely because the tool that would normally surface it is the thing that's down.

## 8. OpenTelemetry

### 8.1 Purpose — the unified collection layer

Volume 1, Part IV, Section 29.2 committed to OpenTelemetry as the unified instrumentation layer for logs, metrics, and traces. This section specifies the Collector architecture that actually receives, processes, and routes that telemetry — the infrastructure component sitting between every instrumented source (Volume 1's backend, Volume 2's frontend RUM, Volume 4's own CI pipeline) and the storage backends Sections 9–11 specify.

### 8.2 Collector architecture — gateway pattern

> **Truncated at source.** The text supplied to me cut off mid-Section 8.2 (at a diagram/architecture description) due to a 50,000-character limit on the paste. Sections 8.2 onward, and any Parts after this one, are not included here. Paste the remainder and I'll append it to this same file.
