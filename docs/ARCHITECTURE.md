# System Architecture & Bounded Contexts (`docs/ARCHITECTURE.md`)

## 1. High-Level Architecture

`restaurant-order` is designed around domain-driven design (DDD) principles to ensure modularity, tenant isolation, and low-latency synchronization across dining floors, kitchens, and administrative back-offices.

```
+-----------------------------------------------------------------------------------+
|                               CLIENT APPLICATIONS                                 |
|  +-------------------+  +-------------------+  +-------------------------------+  |
|  | QR Guest Web App  |  | Waiter Mobile App |  | Kitchen & Bar KDS Displays    |  |
|  +---------+---------+  +---------+---------+  +---------------+---------------+  |
|            |                      |                            |                  |
|  +---------+---------+  +---------+---------+                  |                  |
|  | Restaurant Admin  |  | Platform Super-   |                  |                  |
|  | Web Dashboard     |  | Admin Dashboard   |                  |                  |
|  +---------+---------+  +---------+---------+                  |                  |
+------------|----------------------|----------------------------|------------------+
             |                      |                            |
             v                      v                            v
+-----------------------------------------------------------------------------------+
|                        API GATEWAY / BFF LAYER [Proposed]                         |
|  - Request authentication & tenant context resolution                             |
|  - Rate limiting & input validation                                               |
|  - WebSocket / SSE Realtime Hub [Proposed / ADR Required]                         |
+------------------------------------+----------------------------------------------+
                                     |
                                     v
+-----------------------------------------------------------------------------------+
|                     CORE DOMAIN SERVICES (Modular Monolith)                       |
|  +------------------+  +------------------+  +------------------+                 |
|  | IAM & RBAC       |  | Menu & Catalog   |  | Table & Floor    |                 |
|  +------------------+  +------------------+  +------------------+                 |
|  +------------------+  +------------------+  +------------------+                 |
|  | Order & Ticket   |  | Billing & Pay    |  | Station Printer  |                 |
|  +------------------+  +------------------+  +------------------+                 |
+------------------------------------+----------------------------------------------+
                                     |
                                     v
+-----------------------------------------------------------------------------------+
|                        DATA PERSISTENCE & HARDWARE LAYER                          |
|  +-------------------------------------+  +------------------------------------+  |
|  | Multi-Tenant Relational Database    |  | Branch Network ESC/POS Printers    |  |
|  | (PostgreSQL 16 + RLS) [ACCEPTED]    |  | (Thermal Print Bridge) [Proposed]  |  |
|  +-------------------------------------+  +------------------------------------+  |
+-----------------------------------------------------------------------------------+
```

---

## 2. Core Architectural Pillars

### 2.1. Bounded Context Modularity
The system is partitioned into autonomous domain modules. Cross-module communication occurs via explicit service interfaces or domain events:
1. **Identity & Access Management (IAM):** User auth, role assignment, branch context resolution.
2. **Catalog & Menu:** Menu hierarchies, variant pricing, modifier rules, 86/stockout management.
3. **Table & Floor Management:** Areas, tables, QR session tracking, table merges and transfers.
4. **Order & Ticket Management:** Order lifecycle, ticket splitting, KDS state transitions.
5. **Billing & Payments:** Bill generation, split payments, tips, platform commission ledger.
6. **Hardware & Printing:** ESC/POS formatting, network socket dispatch, printer spooler and retry queue.
7. **Multi-Tenant Administration:** Tenant onboarding, subscriptions, platform configuration.

### 2.2. Realtime Event Synchronization
- The system requires sub-second order propagation from customer/waiter devices to KDS screens.
- **Proposed Technology:** WebSockets or Server-Sent Events (SSE) with an in-memory or Redis pub/sub broker `[Proposed / ADR Required]`.
- Event streams are strictly partitioned by `tenant_id` and `branch_id` to prevent cross-tenant data leaks.

### 2.3. Data Storage & Multi-Tenancy Isolation
- Relational integrity is paramount for financial transactions, bill calculations, and state machines.
- **Isolation Strategy:** Shared database with Row-Level Security (RLS) enforcing `tenant_id` filters on every query `[ACCEPTED / ADR-0002]`.
- Alternative: Schema-per-tenant for enterprise isolation `[Rejected for MVP / ADR-0002]`.

### 2.4. Hardware & Printing Integration
- Restaurants rely on physical thermal receipt printers (kitchen tickets, customer bills).
- **Communication Protocol:** ESC/POS commands over TCP/IP (Ethernet/Wi-Fi).
- **Architecture Pattern:** A lightweight local print agent or backend spooler handles socket connections, timeouts, and paper-out errors `[Proposed / ADR Required]`.

---

## 3. Technology Stack Decisions (Status: ADR Governed)

| Component | Selected / Proposed Technology | Decision Status | ADR Reference |
| :--- | :--- | :--- | :--- |
| **Monorepo & Package Manager**| `pnpm` workspaces + single root lockfile | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Backend Architecture** | Modular Monolith (Clean Architecture) | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Backend Runtime** | .NET 10 ASP.NET Core | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Frontend Framework** | React 19 + Vite (TypeScript strict, PWA-first)| `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Realtime Transport** | ASP.NET Core SignalR | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Cache & Realtime Broker** | Redis 7 | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Database Engine** | PostgreSQL 16 (Multi-tenant RLS) | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Background Processing** | Separate Worker Host (`apps/worker`) | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Containers & Local Dev** | Docker & Docker Compose | `ACCEPTED` | [ADR-0001](./adr/0001-technology-stack.md) |
| **Data Persistence Library**| EF Core 10 (Npgsql Provider) | `ACCEPTED` | [ADR-0002](./adr/0002-persistence-selection.md) |
| **Thermal Printing Bridge** | Node/Go local socket daemon or direct IP | `[Proposed / ADR Required]` | `ADR-0005` (Pending) |
| **Payment Gateway** | Multi-provider adapter (Stripe, Iyzico, etc.) | `[Proposed / ADR Required]` | `ADR-0006` (Pending) |

---

## 4. Architectural Invariants

1. **No Shared Mutable State Across Tenants:** Every query, cache key, and event payload must carry explicit tenant context.
2. **Deterministic State Transitions:** Orders, tickets, and payments must only transition via verified state machines (see [docs/STATE-MACHINES.md](./STATE-MACHINES.md)).
3. **Idempotency on Financial Operations:** Payment processing, bill closing, and refunds must enforce idempotency keys to prevent double-charging.
4. **Resilient Hardware Decoupling:** Printer failures must never block KDS progression or order placement.
