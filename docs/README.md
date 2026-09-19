# Documentation Index & Architecture Blueprint (`docs/README.md`)

Welcome to the technical and operational documentation repository for the **Restaurant Order Management System** (`restaurant-order`).

---

## 1. Documentation Structure & Map

The documentation is organized into clear domains to support developers, product managers, and automated coding agents:

### 1.1. Product & Domain Specifications
- [docs/PRODUCT.md](file:///d:/freelance/restaurant-order/docs/PRODUCT.md) — Product vision, user journeys, target personas, and the 5 product surfaces.
- [docs/DOMAIN.md](file:///d:/freelance/restaurant-order/docs/DOMAIN.md) — Ubiquitous language, core entities, relationships, and bounded contexts.
- [docs/GLOSSARY.md](file:///d:/freelance/restaurant-order/docs/GLOSSARY.md) — Standardized bilingual (TR/EN) terminology dictionary.
- [docs/SCREEN-INVENTORY.md](file:///d:/freelance/restaurant-order/docs/SCREEN-INVENTORY.md) — Detailed catalog of UI screens across all surfaces.

### 1.2. Architecture & Technical Foundations
- [docs/ARCHITECTURE.md](file:///d:/freelance/restaurant-order/docs/ARCHITECTURE.md) — System architecture, bounded contexts, event model, and proposed technology stack.
- [docs/REPOSITORY-STRUCTURE.md](file:///d:/freelance/restaurant-order/docs/REPOSITORY-STRUCTURE.md) — Repository layout, module boundaries, and file size limits (450/600 lines).
- [docs/ROLES-AND-PERMISSIONS.md](file:///d:/freelance/restaurant-order/docs/ROLES-AND-PERMISSIONS.md) — RBAC matrix across the 8 system roles.
- [docs/STATE-MACHINES.md](file:///d:/freelance/restaurant-order/docs/STATE-MACHINES.md) — Formal state lifecycle models for tables, orders, tickets, and payments.
- [docs/NEGATIVE-FLOWS.md](file:///d:/freelance/restaurant-order/docs/NEGATIVE-FLOWS.md) — Failure handling, edge cases, timeouts, and race conditions.
- [docs/MULTI-TENANCY.md](file:///d:/freelance/restaurant-order/docs/MULTI-TENANCY.md) — Multi-tenant isolation architecture and data safety.
- [docs/PAYMENTS-TIPS-COMMISSIONS.md](file:///d:/freelance/restaurant-order/docs/PAYMENTS-TIPS-COMMISSIONS.md) — Payment gateway integration, split billing, tips, and platform fees.
- [docs/ORDER-ROUTING-AND-PRINTING.md](file:///d:/freelance/restaurant-order/docs/ORDER-ROUTING-AND-PRINTING.md) — Kitchen/bar routing and ESC/POS thermal printing.
- [docs/REALTIME-AND-NOTIFICATIONS.md](file:///d:/freelance/restaurant-order/docs/REALTIME-AND-NOTIFICATIONS.md) — Real-time event streams, push notifications, and chime alerts.

### 1.3. Quality, Operations & Delivery
- [docs/TESTING.md](file:///d:/freelance/restaurant-order/docs/TESTING.md) — Testing strategy, critical paths, and zero-unverified-PASS policy.
- [docs/SECURITY.md](file:///d:/freelance/restaurant-order/docs/SECURITY.md) — Security policies, credential protection, OWASP mitigations, and compliance.
- [docs/ENVIRONMENTS.md](file:///d:/freelance/restaurant-order/docs/ENVIRONMENTS.md) — Environment isolation (local, test, dev, staging, prod).
- [docs/DELIVERY.md](file:///d:/freelance/restaurant-order/docs/DELIVERY.md) — Branching workflow, SemVer, and zero-downtime database migrations.
- [docs/BLUE-GREEN-RUNBOOK.md](file:///d:/freelance/restaurant-order/docs/BLUE-GREEN-RUNBOOK.md) — Zero-downtime production blue/green release runbook.
- [docs/INCIDENT-RESPONSE.md](file:///d:/freelance/restaurant-order/docs/INCIDENT-RESPONSE.md) — Incident severity levels, escalation, and post-mortem procedures.
- [docs/ROADMAP.md](file:///d:/freelance/restaurant-order/docs/ROADMAP.md) — Development phases and milestone tracking.
- [docs/FOUNDATION-VALIDATION.md](file:///d:/freelance/restaurant-order/docs/FOUNDATION-VALIDATION.md) — Independent audit, verification matrix, and quality gate evidence.

### 1.4. ADRs, Runbooks & Templates
- [docs/adr/README.md](file:///d:/freelance/restaurant-order/docs/adr/README.md) — Architecture Decision Records index and guide.
- [docs/runbooks/README.md](file:///d:/freelance/restaurant-order/docs/runbooks/README.md) — Operational runbooks catalog.
- [docs/templates/ADR-TEMPLATE.md](file:///d:/freelance/restaurant-order/docs/templates/ADR-TEMPLATE.md) — Standard ADR template.
- [docs/templates/FEATURE-TEMPLATE.md](file:///d:/freelance/restaurant-order/docs/templates/FEATURE-TEMPLATE.md) — Standard feature specification template.
- [docs/templates/INCIDENT-TEMPLATE.md](file:///d:/freelance/restaurant-order/docs/templates/INCIDENT-TEMPLATE.md) — Standard post-mortem incident template.

---

## 2. Documentation Governance Rules

1. **Synchronized Updates:** When code behavior or an API contract changes, the corresponding document **must** be updated within the same commit/PR.
2. **Strict Line Count Limits:** All documentation files must respect the 450-line warning and 600-line strict maximum ceiling. Split long documents logically.
3. **No Phantom Decisions:** Architectural choices that have not been approved by the user must be explicitly marked as `[Proposed / ADR Required]` or `[TBD]`.
4. **Verifiable Links:** All internal links must use relative markdown links and point to existing files.
