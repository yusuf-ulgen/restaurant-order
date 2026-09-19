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

## 3. Pending & Planned ADRs

| ADR ID | Title | Status | Primary Focus |
| :--- | :--- | :--- | :--- |
| `ADR-001` | Architecture Pattern: Modular Monolith vs Microservices | `PROPOSED` | System modularity & deployment boundaries |
| `ADR-002` | Backend Language & Runtime Selection | `PROPOSED` | Node.js (TypeScript) vs Go |
| `ADR-003` | Multi-Tenant Data Isolation Strategy | `PROPOSED` | PostgreSQL Row-Level Security vs Schema-per-tenant |
| `ADR-004` | Frontend Web & Mobile Application Framework | `PROPOSED` | React/Next.js vs Vite SPA across 5 surfaces |
| `ADR-005` | Realtime Event Transport Architecture | `PROPOSED` | WebSockets vs Server-Sent Events (SSE) |
| `ADR-006` | Distributed Caching & Pub/Sub Broker | `PROPOSED` | Redis vs In-Memory Pub/Sub |
| `ADR-007` | ESC/POS Thermal Printing Integration Pattern | `PROPOSED` | Local Branch Print Agent vs Cloud Direct Socket |
| `ADR-008` | Digital Payment Gateway Integration Strategy | `PROPOSED` | Multi-gateway abstraction layer |

---

## 4. Creating a New ADR

To propose a new architecture decision:
1. Copy the template from [docs/templates/ADR-TEMPLATE.md](file:///d:/freelance/restaurant-order/docs/templates/ADR-TEMPLATE.md).
2. Save it as `docs/adr/ADR-XXX-<decision-title>.md`.
3. Set initial status to `PROPOSED` and submit via Pull Request.
