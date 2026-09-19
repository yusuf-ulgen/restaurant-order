# Restaurant Order Management System (`restaurant-order`)

A modern, multi-tenant restaurant ordering and operations management platform designed to streamline the dining experience from QR-based table ordering to kitchen preparation, service dispatch, and administrative analytics.

---

## 1. Overview

`restaurant-order` is engineered to provide an integrated operational ecosystem for single-location restaurants and multi-branch restaurant chains. The platform bridges the gap between dining guests, floor staff, kitchen teams, and management through real-time communication, automated order routing, and robust multi-tenancy.

### Core Objectives
- **Frictionless Guest Experience:** QR-code menu browsing, order placement, service call, and bill viewing without app installation.
- **Operational Speed:** Mobile-optimized order taking for waitstaff and instant kitchen/bar display queue updates.
- **Reliable Kitchen Routing:** Automated ticket splitting across kitchen and bar stations with physical ESC/POS thermal printing failover.
- **Enterprise Multi-Tenancy:** Secure data isolation across organizations, brands, and branches.

---

## 2. Product Surfaces

The platform encompasses 5 distinct user-facing surfaces:

1. **QR Customer Web App:** Lightweight, responsive, mobile-first guest application accessible via table QR codes.
2. **Waiter & Operations Mobile App:** Touch-first web interface for waitstaff to manage tables, modify orders, and handle table transfers.
3. **Kitchen / Bar KDS (Kitchen Display System):** Large-format, color-coded preparation queue interface with audio alerts and station filters.
4. **Restaurant Admin Panel:** Comprehensive management portal for menus, inventory stockouts, staff permissions, tables, and branch analytics.
5. **Platform Super Admin Panel:** Multi-tenant control plane for platform administration, tenant onboarding, subscription management, and system health.

---

## 3. User Roles (RBAC)

The system enforces strict Role-Based Access Control (RBAC) across 8 roles:

- **Super Admin:** Platform owner; oversees all tenants and global platform configurations.
- **Restoran Admini (Restaurant Admin):** Brand/tenant owner; manages brands, branches, financial reports, and organization settings.
- **Şube Müdürü (Branch Manager):** Branch lead; manages physical layouts, staff shifts, menu availability, and daily branch performance.
- **Operasyon/Kasa (Operations/Cashier):** POS operator; manages cash registers, split bill payments, manual receipts, and order overrides.
- **Mutfak (Kitchen):** Kitchen display station; tracks food tickets, preparation stages, and food item stockouts.
- **Bar (Bar):** Beverage station; tracks drink tickets, preparation stages, and beverage stockouts.
- **Garson (Waiter):** Service staff; manages assigned dining tables, takes orders, calls service, and requests bills.
- **Müşteri (Customer):** Dining guest; scans QR code, browses menu, places orders, and views bill.

For the full permissions matrix, refer to [docs/ROLES-AND-PERMISSIONS.md](file:///d:/freelance/restaurant-order/docs/ROLES-AND-PERMISSIONS.md).

---

## 4. Documentation Map

Detailed technical and domain documentation is organized under the [`docs/`](file:///d:/freelance/restaurant-order/docs/) directory:

- **Product & Domain:**
  - [docs/PRODUCT.md](file:///d:/freelance/restaurant-order/docs/PRODUCT.md) — Product vision, user journeys, and feature scope.
  - [docs/DOMAIN.md](file:///d:/freelance/restaurant-order/docs/DOMAIN.md) — Domain entities, relationships, and ubiquitous language.
  - [docs/GLOSSARY.md](file:///d:/freelance/restaurant-order/docs/GLOSSARY.md) — Terminology dictionary (TR / EN).
  - [docs/SCREEN-INVENTORY.md](file:///d:/freelance/restaurant-order/docs/SCREEN-INVENTORY.md) — UI screen catalog across all 5 surfaces.
- **Architecture & Technical Design:**
  - [docs/ARCHITECTURE.md](file:///d:/freelance/restaurant-order/docs/ARCHITECTURE.md) — System architecture, bounded contexts, and proposed technology stack.
  - [docs/REPOSITORY-STRUCTURE.md](file:///d:/freelance/restaurant-order/docs/REPOSITORY-STRUCTURE.md) — Codebase structure and modularity rules.
  - [docs/STATE-MACHINES.md](file:///d:/freelance/restaurant-order/docs/STATE-MACHINES.md) — State lifecycles for tables, orders, tickets, and payments.
  - [docs/NEGATIVE-FLOWS.md](file:///d:/freelance/restaurant-order/docs/NEGATIVE-FLOWS.md) — Failure scenarios, edge cases, and recovery strategies.
  - [docs/MULTI-TENANCY.md](file:///d:/freelance/restaurant-order/docs/MULTI-TENANCY.md) — Tenant isolation and data security.
  - [docs/PAYMENTS-TIPS-COMMISSIONS.md](file:///d:/freelance/restaurant-order/docs/PAYMENTS-TIPS-COMMISSIONS.md) — Payment workflows, tips, and platform fees.
  - [docs/ORDER-ROUTING-AND-PRINTING.md](file:///d:/freelance/restaurant-order/docs/ORDER-ROUTING-AND-PRINTING.md) — Station routing and ESC/POS thermal printing.
  - [docs/REALTIME-AND-NOTIFICATIONS.md](file:///d:/freelance/restaurant-order/docs/REALTIME-AND-NOTIFICATIONS.md) — Real-time event transport and notification dispatch.
- **Engineering & Operations:**
  - [docs/TESTING.md](file:///d:/freelance/restaurant-order/docs/TESTING.md) — Testing requirements and verification protocols.
  - [docs/SECURITY.md](file:///d:/freelance/restaurant-order/docs/SECURITY.md) — Security policies, PII handling, and secret governance.
  - [docs/ENVIRONMENTS.md](file:///d:/freelance/restaurant-order/docs/ENVIRONMENTS.md) — Environment isolation (local, test, dev, staging, prod).
  - [docs/DELIVERY.md](file:///d:/freelance/restaurant-order/docs/DELIVERY.md) — Release process and migration safety.
  - [docs/BLUE-GREEN-RUNBOOK.md](file:///d:/freelance/restaurant-order/docs/BLUE-GREEN-RUNBOOK.md) — Zero-downtime blue/green deployment instructions.
  - [docs/INCIDENT-RESPONSE.md](file:///d:/freelance/restaurant-order/docs/INCIDENT-RESPONSE.md) — Incident severity classification and runbooks.
  - [docs/ROADMAP.md](file:///d:/freelance/restaurant-order/docs/ROADMAP.md) — Phased development milestones.
  - [docs/adr/](file:///d:/freelance/restaurant-order/docs/adr/) — Architecture Decision Records.
  - [docs/runbooks/](file:///d:/freelance/restaurant-order/docs/runbooks/) — Operational runbooks.
  - [docs/templates/](file:///d:/freelance/restaurant-order/docs/templates/) — ADR, feature, and incident templates.

---

## 5. Automated Quality Gates & Verification Commands

The repository enforces strict, non-negotiable automated quality gates before any code is merged:

```bash
# 1. Run all automated quality gates (file size, doc links, secret scanning, gate tests)
pnpm verify:gates

# 2. Run TypeScript strict typecheck across all workspace packages and apps
pnpm typecheck

# 3. Run ESLint across all frontend applications and packages
pnpm lint

# 4. Run all backend and frontend unit/integration tests
pnpm test

# 5. Build all frontend packages and applications
pnpm build

# 6. Run full verification pipeline (gates + lint + typecheck + test + build)
pnpm verify

# 7. Validate local Docker Compose configuration
docker compose -f deploy/docker-compose.yml config
```

---

## 6. Contributor & Agent Guidelines

All AI agents and developers working on this project must strictly comply with [AGENTS.md](file:///d:/freelance/restaurant-order/AGENTS.md). 

Key mandatory guidelines:
- **Line Count Limits:** Warning at 450 lines, strict hard ceiling at 600 lines for any human-authored file (verified via `node scripts/check-file-size.mjs`).
- **Verification First:** Never report a task as PASS without executing and validating tests.
- **Zero Secrets:** No secrets, credentials, or real customer data in code or logs (verified via `node scripts/check-secrets.mjs`).
- **No Premature Assumptions:** Architectural and stack choices remain marked as `[Proposed / ADR Required]` until officially adopted.
