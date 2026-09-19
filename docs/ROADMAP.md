# Product & Technical Roadmap (`docs/ROADMAP.md`)

## 1. Roadmap Overview & Milestones

The `restaurant-order` system is developed in progressive phases, starting with comprehensive architecture and domain foundations, followed by core ordering operations, hardware integrations, and multi-tenant scaling.

```
+-------------------------------------------------------------------------------+
|  PHASE 0: FOUNDATION & GOVERNANCE [CURRENT]                                   |
|  - AGENTS.md binding rules & thin adapters                                    |
|  - Complete domain, product, architecture, and operational docs               |
|  - Branching strategy, templates & ADR framework                              |
+---------------------------------------+---------------------------------------+
                                        |
                                        v
+-------------------------------------------------------------------------------+
|  PHASE 1: CORE ORDERING & OPERATIONS MVP                                      |
|  - Monorepo scaffold & PostgreSQL database schema with RLS                    |
|  - Surface 1: QR Customer Web App (menu browsing, cart, order fire)           |
|  - Surface 2: Waiter Mobile App (tables, order taking, transfer)              |
|  - Surface 3: Kitchen & Bar KDS (realtime ticket queues & bump)               |
|  - Basic cashier settlement (cash & external POS card)                        |
+---------------------------------------+---------------------------------------+
                                        |
                                        v
+-------------------------------------------------------------------------------+
|  PHASE 2: HARDWARE, PAYMENTS & MULTI-TENANCY EXPANSION                        |
|  - ESC/POS network thermal printing bridge & spooler retry                    |
|  - Integrated digital online payment gateways (Stripe / Iyzico)               |
|  - Split billing, tip allocation & platform commission calculations           |
|  - Surface 4 & 5: Restaurant Admin & Platform Super Admin panels              |
+---------------------------------------+---------------------------------------+
                                        |
                                        v
+-------------------------------------------------------------------------------+
|  PHASE 3: ADVANCED ANALYTICS, INVENTORY & LOYALTY                             |
|  - Ingredient-level inventory tracking & automatic item 86ing                 |
|  - Guest loyalty, digital punch cards & CRM                                   |
|  - Multi-branch financial analytics, peak hour forecasts                      |
+-------------------------------------------------------------------------------+
```

---

## 2. Detailed Milestone Deliverables

### Phase 0: Foundation & Governance (Status: In Progress / Completing)
- [x] Establish single binding source of truth: `AGENTS.md`.
- [x] Configure thin adapter agent files (`CLAUDE.md`, `GEMINI.md`, etc.).
- [x] Comprehensive domain and architectural documentation (`docs/*`).
- [x] State machine specifications for tables, orders, tickets, and payments.
- [x] Operational runbooks for Blue/Green deployments and incident response.

### Phase 1: Core Ordering & Operations MVP (Planned)
- [ ] Initialize monorepo workspace (`apps/*`, `packages/*`).
- [ ] Implement core domain models and state machines in `packages/domain`.
- [ ] Setup PostgreSQL schema with initial migrations and Row-Level Security policies.
- [ ] Build Surface 1: QR Customer Web App.
- [ ] Build Surface 2: Waiter Mobile App.
- [ ] Build Surface 3: Kitchen & Bar KDS with audio alerts.
- [ ] Implement WebSocket / SSE realtime event hub for live ticket updates.

### Phase 2: Hardware, Payments & Multi-Tenancy Expansion (Planned)
- [ ] Implement ESC/POS printer driver and resilient spooler queue in `packages/printer-protocol`.
- [ ] Integrate online payment gateway adapters with idempotency keys.
- [ ] Implement split billing math and tip allocation engine.
- [ ] Build Surface 4: Restaurant Admin panel (menu builder, QR generator).
- [ ] Build Surface 5: Platform Super Admin panel (tenant onboarding & billing).

### Phase 3: Advanced Analytics, Inventory & Loyalty (Planned)
- [ ] Recipe management and automated inventory decrement per order item.
- [ ] Customer loyalty integration via phone number or QR wallet.
- [ ] End-of-Day Z-report generation and automated financial exports.
