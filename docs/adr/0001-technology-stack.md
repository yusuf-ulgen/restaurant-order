# ADR-0001: Core Technology Stack & Monorepo Foundation (`docs/adr/0001-technology-stack.md`)

- **Status:** `ACCEPTED`
- **Deciders:** Architecture Team, Yusuf Ülgen
- **Date:** 2026-09-19
- **Technical Story:** Project Foundation Initialization

---

## 1. Context and Problem Statement

The `restaurant-order` platform is a multi-tenant restaurant management system spanning 5 user-facing surfaces (Customer QR Web, Waiter Mobile, Kitchen/Bar KDS, Restaurant Admin, Super Admin), physical hardware printing, realtime order routing, and financial transactions.

A coherent, high-performance, and unified technology stack is required to support rapid feature delivery, high reliability during peak service hours, low operational latency, and clean modular boundaries.

---

## 2. Decision Drivers

- **Sub-Second Realtime Latency:** Instant ticket updates across floor staff, kitchen KDS, and guests.
- **Strict Modularity without Microservice Complexity:** Fast development and single-deployment simplicity while keeping clean domain boundaries.
- **Type Safety & Developer Ergonomics:** End-to-end type safety across backend APIs and frontend clients.
- **Multi-Tenant Data Isolation:** Robust database-level isolation and relational transaction integrity.
- **Zero-Downtime Reliability:** Support for Blue/Green deployments and separable background workers.

---

## 3. Decision Outcome

**Chosen Architecture and Stack:**

1. **Workspace & Repository:**
   - **Monorepo Architecture:** Single unified repository managing all applications and packages.
   - **Package Manager:** `pnpm` with workspaces (`pnpm-workspace.yaml`) and a single root lockfile (`pnpm-lock.yaml`).
2. **Frontend Applications:**
   - **Framework & Tooling:** React 19, Vite, TypeScript in strict mode.
   - **Form Factor:** Responsive PWA-first architecture for seamless mobile, tablet, and desktop experiences without mandatory app store distribution.
3. **Backend Architecture:**
   - **Platform & Runtime:** .NET 10 ASP.NET Core.
   - **Architectural Style:** Modular Monolith (domain-driven design, clean vertical slices; microservices explicitly rejected for this phase).
   - **API Protocol:** Versioned REST endpoints with OpenAPI / Swagger specifications.
   - **Realtime Transport:** ASP.NET Core SignalR with Redis backplane.
4. **Data Persistence & Coordination:**
   - **Primary Relational Database:** PostgreSQL (multi-tenant isolation with Row-Level Security).
   - **Distributed Cache & Coordination:** Redis for caching, distributed locking, and SignalR scale-out.
   - **Background Processing:** Dedicated Worker host (`apps/worker`), architecturally separable from the API host (`apps/api`).
5. **Containerization & Deployment:**
   - **Containers:** Docker and Docker Compose for local development dependencies and production packaging.

---

## 4. Consequences & Trade-offs

### Positive Consequences
- **Unified Tooling:** Single repository coordinates frontend packages, backend services, and test suites.
- **High Throughput:** .NET 10 provides industry-leading raw HTTP and WebSocket/SignalR performance.
- **Operational Simplicity:** A modular monolith avoids network partition failures, distributed transaction overhead, and deployment complexity inherent to microservices.
- **PWA Portability:** Immediate deployment to guest smartphones and staff devices without app store delays.

### Negative Consequences / Trade-offs
- **Multi-Language Monorepo:** Managing both .NET and Node/pnpm in a single repository requires dual toolchains on developer machines and in CI/CD.
  - *Mitigation:* Clear root scripts (`pnpm verify`) and Docker Compose environments orchestrate both stacks seamlessly.

---

## 5. Verification & Test Plan

- Root `pnpm install`, `pnpm build`, `pnpm test`, and `dotnet build`, `dotnet test` must execute cleanly.
- Health check endpoints (`/health/live`, `/health/ready`) validated via automated integration tests.
