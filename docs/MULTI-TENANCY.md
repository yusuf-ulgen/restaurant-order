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
   - Child entities maintain an indexed `tenant_id` and `brand_id` / `branch_id`.
   - `branches` enforces a composite foreign key `(tenant_id, brand_id)` referencing `brands(tenant_id, id)` with `DeleteBehavior.Restrict` to physically prevent cross-tenant brand assignment.
   - `brand_appearances` enforces `(tenant_id, brand_id)` referencing `brands(tenant_id, id)`.
   - `branch_theme_overrides`, `branch_settings`, `branch_operating_hours`, `dining_areas`, `preparation_stations`, and `branch_feature_flags` all enforce `(tenant_id, branch_id)` composite foreign keys referencing `branches(tenant_id, id)` with `DeleteBehavior.Restrict` / `Cascade`.
   - `dining_areas` enforces branch-scoped unique code: `(tenant_id, branch_id, code)`.
   - `preparation_stations` enforces branch-scoped unique code: `(tenant_id, branch_id, code)`.
2. **Database-Level Row-Level Security (RLS):**
   - RLS is enabled and forced (`FORCE ROW LEVEL SECURITY`) across all tenancy and configuration tables: `tenancy.tenants`, `tenancy.brands`, `tenancy.branches`, `tenancy.brand_appearances`, `tenancy.branch_theme_overrides`, `tenancy.branch_settings`, `tenancy.branch_operating_hours`, `tenancy.dining_areas`, `tenancy.preparation_stations`, and `tenancy.branch_feature_flags`.
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

## 5. Security & Role Architecture

### 5.1. NOLOGIN Runtime Group Role vs LOGIN Role
To enforce strict zero-secret compliance in source control and database migrations:
1. **`restaurant_app_runtime` (NOLOGIN Group Role):**
   - Provisioned via privileged bootstrap script (`deploy/bootstrap/001_create_runtime_login_role.sql`).
   - Configured with `NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`.
   - Granted DML (`SELECT`, `INSERT`, `UPDATE`, `DELETE`) and sequence usage on schema `tenancy` by EF Core migrations (`20260920182029_AddTenantRowLevelSecurity.cs`).
   - Has `CREATE` revoked on schema `tenancy`.
   - Migration verifies presence of this group role and fails fast with a descriptive error if missing.
   - Cannot log in directly and contains NO passwords.
2. **`restaurant_app_user` (Production LOGIN Role):**
   - Created exclusively by deployment pipelines / secret managers / DBAs prior to application startup using `deploy/bootstrap/001_create_runtime_login_role.sql`.
   - Ingests password securely via `psql \getenv app_runtime_password APP_RUNTIME_PASSWORD` and `format(%L)`.
   - Configured with `LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS`.
   - Granted membership in `restaurant_app_runtime` (`GRANT restaurant_app_runtime TO restaurant_app_user`).
   - Receives a cryptographically random, high-entropy password injected via environment variables (`DATABASE_URL`).
   - Supports idempotent in-place credential rotation without interrupting schema grants.
   - Never committed to git, migrations, test fixtures, or container images.

### 5.2. Credential Provisioning Across Environments
- **Local Development:** Developers use docker-compose with local credentials defined in uncommitted `.env` files.
- **Integration Tests:** The test fixture (`TestcontainersFixture`) provisions `restaurant_app_runtime` and creates a unique, ephemeral login role (e.g. `test_rt_<random_suffix>`) with a 256-bit cryptographically secure random password per test run, grants membership in `restaurant_app_runtime`, and drops the role on disposal.
- **Staging & Production:** Managed cloud identity or vault-generated credentials injected via environment secrets into the application container.

### 5.3. Deployment & Migration Sequence
All production releases follow this deterministic 6-step sequence:
1. **Privileged DBA Bootstrap:**
   Execute `deploy/bootstrap/001_create_runtime_login_role.sql` as a privileged user (e.g., `postgres` / DBA) with secure environment secret injection (`APP_RUNTIME_PASSWORD`) to provision `restaurant_app_runtime` and `restaurant_app_user`.
2. **Pre-Cutover Schema Migration:**
   Execute migrations (`dotnet ef database update` or `scripts/migration-ops.mjs`) as the migration owner role before starting the new application release.
3. **Runtime Credential Secret Injection:**
   Inject the production database connection string (with the runtime login user credentials) into the inactive (Green) slot container configuration via the deployment secret manager / vault.
4. **Runtime Connection & RLS Smoke Verification:**
   Verify `restaurant_app_user` can connect, fail-closed RLS returns 0 records without tenant context, and returns only authorized tenant records with context.
5. **Green Application Slot Startup:**
   Start candidate slot containers (`apps/api`), warming up connection pools and background services.
6. **Health Verification & Blue/Green Cutover:**
   Execute synthetic health checks (`/health/live`, `/health/ready`), verify error rates, and perform traffic cutover via the Nginx ingress router.

### 5.4. Connection Pooling Security Assumptions
1. **Transaction-Local Setting Scope:**
   `set_config('app.current_tenant_id', @tenantId, true)` is strictly scoped to the active database transaction (`is_local => true`). When the transaction commits, rolls back, or fails due to an exception, PostgreSQL automatically purges the setting.
2. **Session Cleanup Guarantee:**
   `RestaurantOrderDbContext.ClearTenantSessionAsync()` explicitly resets `app.current_tenant_id` to an empty value to guarantee no ambient leakage when physical connections return to the Npgsql pool.
3. **Non-Owner Enforcement:**
   Runtime connections strictly execute under the non-owner runtime role where `FORCE ROW LEVEL SECURITY` prevents bypass. The role lacks `BYPASSRLS` and `SUPERUSER`.
4. **Fail-Closed Default:**
   If `app.current_tenant_id` is missing, empty string, malformed, or references a nonexistent tenant, `tenancy.get_current_tenant_id()` yields `NULL`, causing RLS policies to evaluate false and return 0 rows.

### 5.5. Incident Response & Credential Rotation Note
If runtime database credentials are ever exposed or suspected compromised:
1. Generate a new high-entropy password in the deployment secret manager / vault.
2. Execute `ALTER ROLE restaurant_app_user WITH PASSWORD '<NEW_STRONG_PASSWORD>';` via DBA connection.
3. Update connection strings across deployment slots and perform an immediate rolling restart.
4. Verify audit logs in PostgreSQL for anomalous queries during the exposure window.

---

## 6. Multi-Tenant Testing Requirements

- **Cross-Tenant Test Suite:** Every integration test executes with at least two test tenants (`Tenant A` and `Tenant B`).
- **Assertion:** Ensure that queries from `Tenant A` explicitly return 0 records when querying IDs belonging to `Tenant B`.
- **Runtime Role Requirement:** Integration tests execute with the non-owner runtime role to prevent false-positive PASS reports.
- **Fail-Closed Verification:** Explicitly verify that missing tenant context, empty string, invalid UUID, and nonexistent tenant UUID return 0 rows.
- Any vulnerability permitting cross-tenant visibility is classified as a **Sev-1 Security Incident**.
