# AGENTS.md — Master Agent & Contributor Instructions

> **Notice:** This document is the **single binding source of truth** for all AI coding agents (Claude, Gemini, GPT, Muse, GitHub Copilot, etc.) and human contributors working on the `restaurant-order` codebase.
> All tool-specific configuration files (`.AGENT.md`, `CLAUDE.md`, `GEMINI.md`, `.claude/README.md`, etc.) are thin adapters referencing this document.

---

## 1. Project Purpose & Scope

`restaurant-order` is a multi-tenant, modern restaurant ordering and management system designed to support end-to-end dining experiences and operational workflows across 5 core product surfaces:

1. **QR Customer Web App:** Responsive, mobile-first web app for browsing menus, placing orders, requesting service, and bill viewing.
2. **Waiter & Operations Mobile App:** Mobile-optimized interface for waitstaff to manage tables, place/modify orders, and process payments.
3. **Kitchen / Bar KDS (Kitchen Display System):** Real-time order preparation queues for kitchen and bar stations.
4. **Restaurant Admin Panel:** Web dashboard for branch managers and restaurant admins (menu management, staff, tables, reports, printer settings).
5. **Platform Super Admin Panel:** Multi-tenant control plane for platform-wide organization, billing, and tenant lifecycle management.

---

## 2. Core Mandatory Rules

### 2.1. File Size Limits
- **450 Lines:** Warning threshold. When a human-authored source, test, or documentation file approaches 450 lines, plan modular extraction.
- **600 Lines:** Absolute strict ceiling. No human-authored file may exceed 600 lines under any circumstances.
- **Exceptions (Allowlist Only):** Only generated files, lockfiles (`package-lock.json`, etc.), vendor code, and snapshot files may exceed 600 lines, provided they are clearly documented in the project allowlist.
- **Decomposition Strategy:** Split code by feature, domain, use-case, adapter, or component responsibility—never randomly or arbitrarily.

### 2.2. Testing & Verification Policy
- Every behavior, happy path, negative path, permission check, error branch, and state machine transition **must** have corresponding automated tests.
- **Critical Paths:** Order processing, payment flows, tip distribution, refunds, tenant isolation, network printing, and concurrent operations are strictly critical.
- **Zero-Unverified-PASS Rule:** Never report an action, test, or build as `PASS`, `SUCCESS`, or `VERIFIED` unless it has actually been executed and verified in the environment. Assumptions are prohibited.
- When behavior changes, the corresponding test, API contract, and documentation **must** be updated simultaneously.

### 2.3. Security & Data Protection
- **Zero Secrets in Repository:** API keys, database credentials, JWT secrets, private keys, payment credentials, and access tokens must never be committed to git or printed to log streams.
- **No Real Customer Data (PII):** Do not commit or log real customer names, phone numbers, credit card data, or personal information. Use synthetic fixtures for testing.
- Follow OWASP Top 10 guidelines across all layers (input validation, SQL injection prevention, XSS mitigation, secure headers).
- Tenant data isolation must be enforced at every data access point.

### 2.4. Environment Separation
- Environments are strictly isolated: `local`, `test`, `development`, `staging`, and `production`.
- Environment configurations must be handled via environment variables with zero hardcoded cross-environment dependencies.
- Production is designed to operate with **Blue/Green** deployment slots to ensure zero-downtime releases and rapid rollbacks. See [docs/BLUE-GREEN-RUNBOOK.md](file:///d:/freelance/restaurant-order/docs/BLUE-GREEN-RUNBOOK.md).

### 2.5. Scope & Decision Governance
- **No Premature Decisions:** Open architecture and technology decisions must be labeled as `[TBD]`, `[Proposed]`, or `[Requires ADR]`. Do not treat unconfirmed proposals as final.
- **No Unauthorized Scope Changes:** Do not alter scope, introduce new frameworks, or change existing architectural agreements without explicit user approval.
- **Respect User Work:** Never touch unrelated files or overwrite uncommitted user modifications.

---

## 3. Supported Roles (RBAC)

All authorization checks throughout the application must adhere to these 8 defined roles (detailed in [docs/ROLES-AND-PERMISSIONS.md](file:///d:/freelance/restaurant-order/docs/ROLES-AND-PERMISSIONS.md)):

1. **Super Admin:** Platform owner; oversees all tenants, billing, and platform-wide configuration.
2. **Restoran Admini (Restaurant Admin):** Brand/tenant owner; manages brands, branches, high-level financials, and users.
3. **Şube Müdürü (Branch Manager):** Branch lead; manages physical layouts, staff shifts, menus, and branch reports.
4. **Operasyon/Kasa (Operations/Cashier):** POS operator; manages register, cash drawer, split payments, receipts, and order overrides.
5. **Mutfak (Kitchen):** Kitchen display station; tracks food tickets, prep states, and ingredient stockouts.
6. **Bar (Bar):** Beverage station; tracks drink tickets, prep states, and beverage stockouts.
7. **Garson (Waiter):** Service staff; manages assigned tables, takes orders, calls service, and requests bills.
8. **Müşteri (Customer):** Dining guest; scans table QR, browses menu, places orders, and views bill.

---

## 4. Documentation Index & Workflow

Agents must consult the relevant document in `docs/` before implementing any feature:

| Domain Area | Governing Document |
| :--- | :--- |
| Overall Overview & Standards | [docs/README.md](file:///d:/freelance/restaurant-order/docs/README.md) |
| Product Vision & Scope | [docs/PRODUCT.md](file:///d:/freelance/restaurant-order/docs/PRODUCT.md) |
| Domain Models & Concepts | [docs/DOMAIN.md](file:///d:/freelance/restaurant-order/docs/DOMAIN.md) |
| Shared Terminology | [docs/GLOSSARY.md](file:///d:/freelance/restaurant-order/docs/GLOSSARY.md) |
| Architecture & Bounded Contexts | [docs/ARCHITECTURE.md](file:///d:/freelance/restaurant-order/docs/ARCHITECTURE.md) |
| Repository Organization & Rules | [docs/REPOSITORY-STRUCTURE.md](file:///d:/freelance/restaurant-order/docs/REPOSITORY-STRUCTURE.md) |
| Access Control & RBAC | [docs/ROLES-AND-PERMISSIONS.md](file:///d:/freelance/restaurant-order/docs/ROLES-AND-PERMISSIONS.md) |
| UI & Screen Inventory | [docs/SCREEN-INVENTORY.md](file:///d:/freelance/restaurant-order/docs/SCREEN-INVENTORY.md) |
| State Machines & Lifecycles | [docs/STATE-MACHINES.md](file:///d:/freelance/restaurant-order/docs/STATE-MACHINES.md) |
| Negative Flows & Failures | [docs/NEGATIVE-FLOWS.md](file:///d:/freelance/restaurant-order/docs/NEGATIVE-FLOWS.md) |
| Multi-Tenancy & Isolation | [docs/MULTI-TENANCY.md](file:///d:/freelance/restaurant-order/docs/MULTI-TENANCY.md) |
| Payments, Tips & Commissions | [docs/PAYMENTS-TIPS-COMMISSIONS.md](file:///d:/freelance/restaurant-order/docs/PAYMENTS-TIPS-COMMISSIONS.md) |
| Order Routing & ESC/POS Printing | [docs/ORDER-ROUTING-AND-PRINTING.md](file:///d:/freelance/restaurant-order/docs/ORDER-ROUTING-AND-PRINTING.md) |
| Realtime Events & Notifications | [docs/REALTIME-AND-NOTIFICATIONS.md](file:///d:/freelance/restaurant-order/docs/REALTIME-AND-NOTIFICATIONS.md) |
| Testing Standards & Verification | [docs/TESTING.md](file:///d:/freelance/restaurant-order/docs/TESTING.md) |
| Security & Compliance | [docs/SECURITY.md](file:///d:/freelance/restaurant-order/docs/SECURITY.md) |
| Environments & Configuration | [docs/ENVIRONMENTS.md](file:///d:/freelance/restaurant-order/docs/ENVIRONMENTS.md) |
| Delivery & Versioning | [docs/DELIVERY.md](file:///d:/freelance/restaurant-order/docs/DELIVERY.md) |
| Blue-Green Deployment Runbook | [docs/BLUE-GREEN-RUNBOOK.md](file:///d:/freelance/restaurant-order/docs/BLUE-GREEN-RUNBOOK.md) |
| Incident Response & Post-Mortems | [docs/INCIDENT-RESPONSE.md](file:///d:/freelance/restaurant-order/docs/INCIDENT-RESPONSE.md) |
| Product Roadmap | [docs/ROADMAP.md](file:///d:/freelance/restaurant-order/docs/ROADMAP.md) |
| Architecture Decision Records | [docs/adr/README.md](file:///d:/freelance/restaurant-order/docs/adr/README.md) |
| Operations Runbooks | [docs/runbooks/README.md](file:///d:/freelance/restaurant-order/docs/runbooks/README.md) |
| Templates | [docs/templates/](file:///d:/freelance/restaurant-order/docs/templates/) |
