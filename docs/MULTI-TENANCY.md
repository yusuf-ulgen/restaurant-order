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
| **Model 1: Shared DB + Row-Level Security (RLS)** | All tenants share one database; every table includes `tenant_id`. PostgreSQL RLS enforces query filters. | Cost-effective, simple migrations, seamless cross-tenant reporting for Super Admin. | Requires rigorous RLS policy testing to prevent data leakage. | `[Implemented / Active]` (ADR-0002) |
| **Model 2: Schema-per-Tenant** | Each tenant has a dedicated PostgreSQL schema within a shared database instance. | Logical schema boundary, easier per-tenant backups. | Complex migrations across hundreds of schemas; connection pooling overhead. | `[Alternative / Rejected for MVP]` |
| **Model 3: Database-per-Tenant** | Each tenant has a physically isolated database instance. | Maximum isolation, custom backup/restore. | High infrastructure cost, difficult global analytics, migration complexity. | `[Alternative / Rejected for MVP]` |

---

## 3. Tenant Context Propagation

For every incoming request, the tenant context is resolved and propagated across the execution stack:

```
[Client Request] 
      │
      ▼
[TenantContextMiddleware] [Implemented]
  - Extract X-Correlation-Id (or generate new RFC 4122 GUID)
  - Resolve ITenantContext via ITenantContextResolver
  - Enforce [RequireTenant] metadata on protected endpoints (RFC 7807 ProblemDetails on failure)
  - Guarantee ambient context cleanup via AsyncLocal on request completion
      │
      ▼
[API Gateway / Auth Middleware] [Phase 3 - Planned]
  - Extract JWT or QR Session Token
  - Validate and resolve: tenant_id, brand_id, branch_id
      │
      ▼
[Async Context / Request Scope] [Implemented]
  - Store ITenantContext (TenantContext) in scoped DI & AsyncLocal
  - Enforce fail-closed validation: RequireTenantId()
      │
      ▼
[Database Connection Pool / Session] [Implemented]
  - Execute: SELECT set_config('app.current_tenant_id', @tenantId, true);
  - PostgreSQL RLS enforces: USING (tenant_id = tenancy.get_current_tenant_id())
  - Transaction-local scope automatically resets on transaction commit/rollback
```

---

## 4. Leakage Prevention Guardrails

1. **Mandatory Foreign Keys & Composite Constraints:**
   - Child entities maintain an indexed `tenant_id` and `brand_id`.
   - `branches` enforces a composite foreign key `(tenant_id, brand_id)` referencing `brands(tenant_id, id)` with `DeleteBehavior.Restrict` to physically prevent cross-tenant brand assignment.
2. **Database-Level Row-Level Security (RLS):**
   - RLS is enabled and forced (`FORCE ROW LEVEL SECURITY`) on `tenancy.tenants`, `tenancy.brands`, and `tenancy.branches`.
   - Access is evaluated via `tenancy.get_current_tenant_id()`. If the session setting is missing, empty, or invalid, it returns `NULL`, causing queries to fail-closed (0 rows returned; inserts/updates rejected).
3. **Dedicated Database Roles:**
   - **Schema Owner / Migrator:** Owns schema DDL, manages migrations, and defines RLS policies.
   - **Runtime Application Role (`restaurant_app_user`):** Granted DML (`SELECT`, `INSERT`, `UPDATE`, `DELETE`) on `tenancy.*`. Strictly configured with `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE` and no `CREATE` permission on schema `tenancy`. Cannot disable RLS or alter schema.
4. **Defense-in-Depth Query Filters:**
   - EF Core Global Query Filters provide secondary in-app filtering.
   - Calling `IgnoreQueryFilters()` or executing raw SQL cannot bypass PostgreSQL RLS.
5. **Connection Pool Isolation:**
   - Uses transaction-local `set_config('app.current_tenant_id', ..., is_local => true)` ensuring settings are reverted when transactions end.
   - `ClearTenantSessionAsync` clears session variables before connections return to the pool.
6. **Worker Tenant Context Propagation [Implemented]:**
   - `ITenantWorkerJobRunner` runs background jobs within an explicit, validated `ITenantJobEnvelope` tenant context with guaranteed cleanup.
7. **Cache Namespace Partitioning [Implemented]:**
   - `TenantCacheKeyFactory` strictly formats keys as `cache:{tenant_id}:{branch_id}:{resource}:{id}` with delimiter injection protection.
8. **Realtime Event Channel Isolation [Phase 9 - Proposed]:**
   - `channel:tenant_{tenant_id}:branch_{branch_id}:kds_kitchen`

---

## 5. Multi-Tenant Testing Requirements

- **Cross-Tenant Test Suite:** Every integration test executes with at least two test tenants (`Tenant A` and `Tenant B`).
- **Assertion:** Ensure that queries from `Tenant A` explicitly return 0 records when querying IDs belonging to `Tenant B`.
- **Runtime Role Requirement:** Integration tests execute with the non-owner `restaurant_app_user` role to prevent false-positive PASS reports.
- Any vulnerability permitting cross-tenant visibility is classified as a **Sev-1 Security Incident**.
