# Multi-Tenancy Architecture & Isolation (`docs/MULTI-TENANCY.md`)

## 1. Overview & Hierarchy

`restaurant-order` is architected as an enterprise-grade multi-tenant platform. A single platform deployment serves multiple independent restaurant organizations while guaranteeing complete data isolation, privacy, and customized operational configurations.

```
+-------------------------------------------------------------------------+
|                       TENANT / ORGANIZATION                             |
|               (Billing entity, subscription tier, users)                |
+------------------------------------+------------------------------------+
                                     |
                    +----------------+----------------+
                    |                                 |
                    v                                 v
        +-----------------------+         +-----------------------+
        |        BRAND A        |         |        BRAND B        |
        |  (Catalog, Branding)  |         |  (Catalog, Branding)  |
        +-----------+-----------+         +-----------+-----------+
                    |                                 |
         +----------+----------+                      v
         |                     |              +---------------+
         v                     v              |   BRANCH B1   |
+-----------------+   +-----------------+     +---------------+
|    BRANCH A1    |   |    BRANCH A2    |
| (Tables, Staff, |   | (Tables, Staff, |
|  Printers, KDS) |   |  Printers, KDS) |
+-----------------+   +-----------------+
```

---

## 2. Multi-Tenancy Isolation Models

| Model | Description | Pros | Cons | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Model 1: Shared DB + Row-Level Security (RLS)** | All tenants share one database; every table includes `tenant_id`. PostgreSQL RLS enforces query filters. | Cost-effective, simple migrations, seamless cross-tenant reporting for Super Admin. | Requires rigorous RLS policy testing to prevent data leakage. | `[Proposed / ADR Required]` |
| **Model 2: Schema-per-Tenant** | Each tenant has a dedicated PostgreSQL schema within a shared database instance. | Logical schema boundary, easier per-tenant backups. | Complex migrations across hundreds of schemas; connection pooling overhead. | `[Alternative / ADR Required]` |
| **Model 3: Database-per-Tenant** | Each tenant has a physically isolated database instance. | Maximum isolation, custom backup/restore. | High infrastructure cost, difficult global analytics, migration complexity. | `[Alternative / ADR Required]` |

---

## 3. Tenant Context Propagation

For every incoming HTTP request and WebSocket connection, the tenant context is resolved and propagated across the execution stack:

```
[Client Request] 
      │
      ▼
[API Gateway / Auth Middleware]
  - Extract JWT or QR Session Token
  - Validate and resolve: tenant_id, brand_id, branch_id
      │
      ▼
[Async Context / Request Scope]
  - Store TenantContext in thread-local / async storage
      │
      ▼
[Database Connection Pool]
  - Execute: SET LOCAL app.current_tenant_id = 'tenant_xyz';
  - PostgreSQL RLS enforces WHERE tenant_id = current_setting('app.current_tenant_id')
```

---

## 4. Leakage Prevention Guardrails

1. **Mandatory Foreign Keys:** Every child entity (tables, orders, bills, payments, tickets) must maintain an indexed `tenant_id` and `branch_id`.
2. **Database-Level RLS:** Even if application code omits a `WHERE tenant_id = ...` clause, PostgreSQL Row-Level Security will automatically reject cross-tenant rows.
3. **ORM / Query Builder Wrappers:** All repository query methods automatically inject the current tenant context from the execution scope.
4. **Cache Namespace Partitioning:** All Redis cache keys must follow the pattern:
   `cache:{tenant_id}:{branch_id}:{resource}:{id}`
5. **Realtime Event Channel Isolation:** WebSocket rooms and SSE event channels are strictly scoped:
   `channel:tenant_{tenant_id}:branch_{branch_id}:kds_kitchen`

---

## 5. Multi-Tenant Testing Requirements

- **Cross-Tenant Test Suite:** Every integration test must execute with at least two test tenants (`Tenant A` and `Tenant B`).
- **Assertion:** Ensure that queries from `Tenant A` explicitly return 0 records when querying IDs belonging to `Tenant B`.
- Any vulnerability permitting cross-tenant visibility is classified as a **Sev-1 Security Incident**.
