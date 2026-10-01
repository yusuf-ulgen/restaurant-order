# Database Migrations & Zero-Downtime Schema Evolution (`docs/runbooks/database-migrations.md`)

## 1. Purpose & Scope

This runbook establishes operational procedures, safety guardrails, and rollback workflows for evolving the PostgreSQL 16 database schema in `restaurant-order`. It governs both CI/CD automated validation and production manual execution under the Blue/Green deployment model.

---

## 2. Core Invariants & Safety Guardrails

1. **Out-of-Process Execution:**
   - Production and staging web applications (`apps/api`) MUST NOT automatically run migrations on startup.
   - Migrations are executed as a distinct, tracked pre-cutover operational step using idempotent SQL scripts or CLI migration runners.
2. **Expand-Migrate-Contract Compliance:**
   - All schema changes must support concurrent execution of both active (Blue) and candidate (Green) slots against the shared database.
   - Phase 1 (Expand): Add new nullable columns, tables, or non-breaking constraints.
   - Phase 2 (Migrate): Dual-write or backfill data without breaking the older application version.
   - Phase 3 (Contract): Deprecate and safely remove obsolete columns only after the old slot is completely drained and retired.
3. **No Destructive DDL Pre-Cutover:**
   - `DROP TABLE`, `DROP COLUMN`, `RENAME COLUMN`, `ALTER TABLE ... DROP`, and `TRUNCATE` are strictly forbidden before cutover.
4. **Idempotency Guarantee:**
   - All migration scripts must check `__EFMigrationsHistory` and existence guards (`CREATE SCHEMA IF NOT EXISTS`, `IF NOT EXISTS`) to ensure repeat execution does not corrupt state or abort.
5. **Verified Backup Prerequisite:**
   - Before executing migrations in production, backup/snapshot readiness must be verified (`BACKUP_VERIFIED=true`).

---

## 3. Step-by-Step Execution Workflow

### 3.1. Pre-Deployment Validation (Dry-Run)

Validate that all migration files and scripts are non-destructive and backward-compatible:

```bash
# Run migration safety check
node scripts/blue-green/migration-check.mjs
```

### 3.2. Script Generation

Generate an idempotent SQL script for review and audit:

```bash
dotnet ef migrations script \
  --project packages/infrastructure/RestaurantOrder.Infrastructure.csproj \
  --startup-project apps/api/RestaurantOrder.Api.csproj \
  --idempotent \
  --output deploy/migrations/001_initial_tenancy_schema.sql
```

### 3.3. Staging / Production Deployment Sequence

All staging and production deployments follow this strictly ordered, zero-downtime 6-step procedure:

1. **Step 1: Privileged Role & Bootstrap Preparation (DBA / Operator)**
   Execute `deploy/bootstrap/001_create_runtime_login_role.sql` as a privileged user (`postgres` / DBA) with secure environment secret injection. The script idempotently provisions:
   - `restaurant_app_runtime` group role (`NOLOGIN`, `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE`, `NOBYPASSRLS`).
   - `restaurant_app_user` login role (`LOGIN`, `PASSWORD`, `NOSUPERUSER`, `NOCREATEDB`, `NOCREATEROLE`, `NOBYPASSRLS`).
   - Grants membership of `restaurant_app_runtime` to `restaurant_app_user`.

   The script securely ingests credentials via `psql \getenv app_runtime_password APP_RUNTIME_PASSWORD` and `format(%L)` to prevent plaintext secrets from appearing in process listings or logs:
   ```bash
   export APP_RUNTIME_PASSWORD="<STRONG_CRYPTOGRAPHIC_PASSWORD>"
   psql -v ON_ERROR_STOP=1 "$DBA_CONNECTION_URL" -f deploy/bootstrap/001_create_runtime_login_role.sql
   unset APP_RUNTIME_PASSWORD
   ```

2. **Step 2: Pre-Cutover Schema Migration (Migration Owner / CI)**
   Apply EF Core migrations as the privileged migration owner role before warming the candidate slot. The migration enforces a fail-fast prerequisite check ensuring `restaurant_app_runtime` exists, creates RLS helper functions, policies, and grants DML/sequence permissions on schema `tenancy`:
   ```bash
   export DATABASE_URL="postgresql://${MIGRATION_USER}:${MIGRATION_PASSWORD}@${DB_HOST}:5432/restaurant_order_prod"
   export BACKUP_VERIFIED="true"

   dotnet ef database update \
     --project packages/infrastructure/RestaurantOrder.Infrastructure.csproj \
     --startup-project apps/api/RestaurantOrder.Api.csproj
   ```

3. **Step 3: Runtime Credential Secret Injection (Candidate Slot)**
   Inject the runtime connection string (using `restaurant_app_user` and the provisioned password) into the candidate slot (`Green`) container environment via the deployment secret manager / vault (e.g. AWS Secrets Manager, HashiCorp Vault, Kubernetes Secret).

4. **Step 4: Runtime Connection & RLS Smoke Verification**
   Verify the unprivileged runtime role connects cleanly and that Row-Level Security fail-closed protections are enforced:
   - Unauthenticated / missing tenant context: Queries return 0 rows (`SELECT COUNT(*) FROM tenancy.tenants` yields 0).
   - Authenticated tenant context: Queries return only records belonging to `app.current_tenant_id`.

5. **Step 5: Green Application Slot Startup**
   Launch candidate slot containers (`apps/api`), warming up EF Core connection pools and background workers against the migrated database.

6. **Step 6: Health Verification & Blue/Green Cutover**
   Execute synthetic health checks (`/health/live`, `/health/ready`), verify error rates, and perform traffic cutover via the Nginx ingress router (`node scripts/blue-green/cutover.mjs --execute`).

### 3.4. Idempotent Credential Rotation
If runtime credentials are ever suspected compromised or due for routine rotation:
1. Generate new high-entropy credentials in the deployment secret manager / vault.
2. Re-run `deploy/bootstrap/001_create_runtime_login_role.sql` with the new `APP_RUNTIME_PASSWORD` exported in the DBA environment. The script idempotently updates the password via `ALTER ROLE restaurant_app_user WITH PASSWORD %L` without altering existing schema privileges or group memberships.
3. Update connection strings across deployment slots and perform rolling container restarts.
4. Review PostgreSQL audit logs for unauthorized queries during the incident window.

---

## 4. Rollback & Down-Migration Strategy

1. **Non-Destructive Safety Window:**
   - Because expand-stage migrations add only additive, non-breaking elements (new tables, new nullable columns, composite foreign keys), the previous application slot continues functioning uninterrupted even if the new release is aborted.
2. **Aborted Deployment (Prior to Cutover):**
   - If health probes or smoke tests fail on the inactive slot, traffic remains on the active slot.
   - Additive schema changes remain dormant and do not need to be rolled back immediately.
3. **Emergency Rollback (Post-Cutover):**
   - Traffic is shifted back to the safe slot via Nginx ingress within 60 seconds (`node scripts/blue-green/rollback.mjs --execute --confirm-rollback`).
   - If schema rollback is strictly required after incident stabilization:
     ```bash
     dotnet ef database update <PreviousMigrationName> \
       --project packages/infrastructure/RestaurantOrder.Infrastructure.csproj \
       --startup-project apps/api/RestaurantOrder.Api.csproj
     ```
4. **Forensic Preservation:**
   - Never run destructive down-migrations while investigating an ongoing incident.
