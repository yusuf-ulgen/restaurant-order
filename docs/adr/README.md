# Architecture Decision Records (ADR) Index (`docs/adr/README.md`)

## 1. Overview & Purpose

Architecture Decision Records (ADRs) capture significant technical and architectural choices made in `restaurant-order`, along with their context, trade-offs, and consequences.

All architectural decisions begin in the `PROPOSED` state and require user or team consensus before being marked `ACCEPTED`.

---

## 2. ADR Lifecycle

```
[ PROPOSED ] ──► [ ACCEPTED ] ──► [ SUPERSEDED (by ADR-XXX) ]
     │
     └──► [ REJECTED ]
```

- **PROPOSED:** An architectural change under consideration and review.
- **ACCEPTED:** Decision approved and binding for all current implementations.
- **SUPERSEDED:** An earlier decision replaced by a newer ADR.
- **REJECTED:** A proposed decision evaluated and decided against.

---

## 3. Decision Records

| ADR ID | Title | Status | Primary Focus |
| :--- | :--- | :--- | :--- |
| [ADR-0001](file:///d:/freelance/restaurant-order/docs/adr/0001-technology-stack.md) | Core Technology Stack & Monorepo Foundation | `ACCEPTED` | React 19, .NET 10, Modular Monolith, SignalR, Redis, Postgres |
| [ADR-0002](file:///d:/freelance/restaurant-order/docs/adr/0002-persistence-selection.md) | Data Persistence Library Selection | `PROPOSED` | Comparative evaluation of EF Core, Dapper, and Marten |
| `ADR-0003` | Multi-Tenant Data Isolation Strategy | `PROPOSED` | PostgreSQL Row-Level Security vs Schema-per-tenant |
| `ADR-0004` | Realtime Event Transport Architecture | `PROPOSED` | SignalR Hubs & Redis Backplane Design |
| `ADR-0005` | ESC/POS Thermal Printing Integration Pattern | `PROPOSED` | Local Branch Print Agent vs Cloud Direct Socket |
| `ADR-0006` | Digital Payment Gateway Integration Strategy | `PROPOSED` | Multi-gateway abstraction layer |

---

## 4. Creating a New ADR

To propose a new architecture decision:
1. Copy the template from [docs/templates/ADR-TEMPLATE.md](file:///d:/freelance/restaurant-order/docs/templates/ADR-TEMPLATE.md).
2. Save it as `docs/adr/ADR-XXX-<decision-title>.md`.
3. Set initial status to `PROPOSED` and submit via Pull Request.
