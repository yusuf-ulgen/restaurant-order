# Product & Technical Roadmap (`docs/ROADMAP.md`)

## 1. Roadmap Overview & Phased Milestones

The `restaurant-order` platform is developed in 18 structured, sequential phases. Each phase builds upon the verified architecture, domain invariants, and quality gates established in preceding milestones.

```
+-------------------------------------------------------------------------------+
|  PHASE 0: FOUNDATION & GOVERNANCE [COMPLETED]                                 |
|  - AGENTS.md binding rules, thin adapters & quality gates                     |
|  - Monorepo architecture, Docker Compose, ASP.NET Core 10 & React 19 shells   |
|  - Distributed worker lease coordination & fail-closed blue/green engine      |
+---------------------------------------+---------------------------------------+
                                        |
                                        v
+-------------------------------------------------------------------------------+
|  PHASE 1: DESIGN SYSTEM & APPLICATION SHELLS [COMPLETED]                      |
|  - Centralized design token system (neutral palette, typography, spacing)     |
|  - Global CSS foundations, touch targets (44px), safe areas, reduced motion   |
|  - Shared UI component library & responsive application shells                |
+---------------------------------------+---------------------------------------+
                                        |
                                        v
+-------------------------------------------------------------------------------+
|  PHASE 2: DATA & MULTI-TENANCY [COMPLETED]                                    |
|  - Multi-tenant PostgreSQL 16 schema with RLS & tenant resolution             |
|  - EF Core 10 persistence selection & zero-downtime migration tooling         |
|  - Tenant, Brand, Branch domain model & strict runtime/migration role split   |
+---------------------------------------+---------------------------------------+
                                        |
                                        v
+-------------------------------------------------------------------------------+
|  PHASE 3: AUTHENTICATION & RBAC [NEXT]                                        |
|  - Identity & Access Management (IAM) module with 8 supported roles           |
|  - JWT tokens, secure cookies, refresh flows, and fast 4-digit PIN auth       |
|  - Role-Based Access Control (RBAC) authorization middleware                  |
+-------------------------------------------------------------------------------+
```

---

## 2. Detailed Milestone Deliverables

### Phase 0: Foundation & Governance (Status: Tamamlandı / Completed)
- [x] Establish single binding source of truth: `AGENTS.md` and thin adapters (`.AGENT.md`, `CLAUDE.md`, `GEMINI.md`).
- [x] Comprehensive domain, product, architecture, and operational documentation (`docs/*`).
- [x] Monorepo scaffold (`pnpm` workspaces, ASP.NET Core 10 API, .NET 10 Worker, React 19 web shells).
- [x] Distributed worker lease coordination with Redis, central active-slot state, and idempotency store.
- [x] Fail-closed Blue/Green deployment engine and automated verification suite.
- [x] Automated quality gates: file size limits (450/600 lines), secret scanning, link integrity, and 67 verified tests.

### Phase 1: Design System & Application Shells (Status: Tamamlandı / Completed)
- [x] Centralized, tenant-extensible design token system in `packages/ui` (colors, typography, spacing, radius, elevation).
- [x] Neutral, modern color palette with semantic status indicators (no decorative gradients).
- [x] Fundamental global styles: consistent box-sizing, font stacks, focus-visible, mobile overflow protection.
- [x] Minimum touch target enforcement (44px WCAG / iOS standard) and iOS safe-area support.
- [x] Reduced-motion accessibility preparation across transitions and animations.
- [x] Token helper utilities and comprehensive unit test coverage.
- [x] Accessible overlay primitives (Modal, BottomSheet, Drawer, ConfirmationDialog, Toast).
- [x] Responsive application shell layouts across Customer, Operations, and Admin surfaces.
- [x] Integration across `apps/customer-web`, `apps/operations-web`, and `apps/admin-web`.

### Phase 2: Data & Multi-Tenancy (Status: Tamamlandı / Completed)
- [x] Multi-tenant PostgreSQL 16 schema design with Row-Level Security (RLS).
- [x] Entity Framework Core 10 persistence selection ([ADR-0002](./adr/0002-persistence-selection.md) ACCEPTED).
- [x] Tenant, Brand, and Branch domain aggregate models with strict lifecycle rules and strong IDs.
- [x] PostgreSQL mapping, composite foreign keys, and initial EF Core migration (`001_initial_tenancy_schema.sql`).
- [x] Database role separation: `postgres` migration owner vs `restaurant_app_user` (NOSUPERUSER, NOBYPASSRLS) runtime role.
- [x] Fail-closed RLS policies with `tenancy.get_current_tenant_id()` session variable.
- [x] Tenant context resolution middleware, RFC 7807 ProblemDetails, and correlation ID propagation.
- [x] Background worker tenant context propagation (`ITenantWorkerJobRunner`) and cache key namespacing (`TenantCacheKeyFactory`).
- [x] Database migration tooling (`scripts/migration-ops.mjs`) and zero-downtime Blue/Green expand-contract safety.
- [x] Idempotent synthetic local development seeder (`DevDataSeeder`).
- [x] Two-tenant real PostgreSQL Testcontainers integration tests verifying strict tenant isolation.

### Phase 3: Authentication & RBAC (Status: Sıradaki / Next)
- [ ] Identity & Access Management (IAM) module with 8 supported roles.
- [ ] JWT authentication, token refresh flows, and secure cookie storage.
- [ ] Fast 4-digit PIN authentication for waiter and operations mobile terminals.
- [ ] Role-Based Access Control (RBAC) authorization middleware and permission matrix.

### Phase 4: Restaurant Configuration (Status: Planlandı / Planned)
- [ ] Brand and branch configuration data models.
- [ ] Operating hours, service charge settings, and tax rate configuration.
- [ ] Branch dining areas (Indoor, Terrace, Garden) and station definitions.

### Phase 5: Menu & Catalog (Status: Planlandı / Planned)
- [ ] Menu categories, items, and variant pricing models.
- [ ] Modifier groups (required single-select, optional multi-select, free/paid).
- [ ] Dietary, allergen, and spicy badges.
- [ ] Real-time item 86ing (mark out-of-stock) data flow.

### Phase 6: Tables, QR & Sessions (Status: Planlandı / Planned)
- [ ] Table numbering, capacity, and physical layout positioning.
- [ ] Dynamic and static QR code generation with cryptographic signature.
- [ ] Dining session lifecycle state machine (Open -> Active -> Bill Requested -> Closed).
- [ ] Table transfer and table merge mechanics.

### Phase 7: Customer Experience (Status: Planlandı / Planned)
- [ ] Surface 1: QR Customer Web App full implementation.
- [ ] Responsive menu browsing, allergen filtering, and item search.
- [ ] Interactive item modifier configuration modal.
- [ ] Cart management, tax calculation, and order submission.
- [ ] Service call requests ("Call Waiter", "Request Wet Wipes", "Request Bill").

### Phase 8: Order Core (Status: Planlandı / Planned)
- [ ] Order entity lifecycle state machine (Draft -> Submitted -> Accepted -> Preparing -> Ready -> Served -> Paid -> Closed).
- [ ] Order line item immutability and audit logging.
- [ ] Order price calculation engine (base, modifiers, taxes, service fees).
- [ ] Race condition prevention on simultaneous table orders.

### Phase 9: Realtime & Notifications (Status: Planlandı / Planned)
- [ ] ASP.NET Core SignalR hub partitioned by `tenant_id` and `branch_id`.
- [ ] Real-time order propagation (<500ms) from guests/waiters to KDS and operations.
- [ ] Chime audio alert dispatch and mobile push notifications.
- [ ] Connection resilience, heartbeat monitoring, and automatic reconnection.

### Phase 10: Waiter & Operations (Status: Planlandı / Planned)
- [ ] Surface 2: Waiter & Operations Mobile App full implementation.
- [ ] Interactive floor plan with color-coded table states.
- [ ] Rapid handheld order entry and modifier selection.
- [ ] Notification drawer for guest service calls and ready food alerts.
- [ ] Table transfer and bill settlement triggers.

### Phase 11: KDS & Routing (Status: Planlandı / Planned)
- [ ] Surface 3: Kitchen & Bar KDS full implementation.
- [ ] Station-specific ticket routing (Food -> Kitchen, Beverage -> Bar).
- [ ] Visual prep timer cards (Green <10m, Amber 10–20m, Red >20m).
- [ ] One-tap ticket bumping (In-Prep -> Ready) and ticket recall modal.
- [ ] One-tap item 86ing directly from KDS screen.

### Phase 12: Printing (Status: Planlandı / Planned)
- [ ] ESC/POS thermal printing engine for kitchen slips and guest bills.
- [ ] Network printer spooler with socket timeout handling and exponential retry.
- [ ] Category-to-printer routing rules.
- [ ] Failover print buffering and manual reprint drawer.

### Phase 13: Billing Engine (Status: Planlandı / Planned)
- [ ] Bill generation, itemized order summaries, and tax breakdown.
- [ ] Bill splitting math (split equally, split by item, split custom amounts).
- [ ] Tip allocation engine and server shift tip tracking.
- [ ] End-of-Day Z-report generation and audit reconciliation.

### Phase 14: Admin & Analytics (Status: Planlandı / Planned)
- [ ] Surface 4: Restaurant Admin Panel full implementation.
- [ ] Menu catalog and modifier group editor with drag-and-drop.
- [ ] Staff directory, role assignment, and PIN management.
- [ ] Operations dashboard: live turnover, station latency, and top-selling items.
- [ ] Thermal printer IP configuration and routing management.

### Phase 15: Platform Super Admin (Status: Planlandı / Planned)
- [ ] Surface 5: Platform Super Admin Panel full implementation.
- [ ] Tenant organization lifecycle (onboard, configure custom domains, suspend).
- [ ] Subscription tier management and platform commission tracking.
- [ ] Global audit logging, health monitoring, and system metrics.

### Phase 16: Payments (Status: Planlandı / Planned)
- [ ] Multi-provider payment gateway integration (Stripe, Iyzico) ([ADR-0006](./adr/README.md)).
- [ ] Idempotency key enforcement on all financial payment endpoints.
- [ ] POS card reader integration and cash drawer reconciliation.
- [ ] Refund workflows and partial payment settlements.

### Phase 17: Production Release (Status: Planlandı / Planned)
- [ ] End-to-end multi-surface dining lifecycle validation.
- [ ] Blue/Green zero-downtime release rehearsal on staging.
- [ ] Performance and load testing under peak simulated restaurant volume.
- [ ] Production security audit, penetration testing, and go-live sign-off.
