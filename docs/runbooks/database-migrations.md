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

All staging and production deployments follow this strictly ordered, zero-downtime procedure:

1. **Step 1: Privileged Role & Bootstrap Preparation (DBA / Operator)**
   Execute `deploy/bootstrap/001_create_runtime_login_role.sql` as a privileged user (`postgres` / DBA) with secret injection to ensure runtime login users exist and belong to the `restaurant_app_runtime` group:
   ```bash
   psql -v ON_ERROR_STOP=1 "$DBA_CONNECTION_URL" -f deploy/bootstrap/001_create_runtime_login_role.sql
   ```

2. **Step 2: Pre-Cutover Schema Migration (Migration Owner)**
   Apply migrations as the migration owner role before warming the candidate slot:
   ```bash
   # Set connection string and backup verification flag
   export DATABASE_URL="postgresql://${MIGRATION_USER}:${MIGRATION_PASSWORD}@${DB_HOST}:5432/restaurant_order_prod"
   export BACKUP_VERIFIED="true"

   dotnet ef database update \
     --project packages/infrastructure/RestaurantOrder.Infrastructure.csproj \
     --startup-project apps/api/RestaurantOrder.Api.csproj
   ```

3. **Step 3: Runtime Login Role Membership & Secret Injection**
   Verify the unprivileged application user inherits permissions from `restaurant_app_runtime`. Inject the runtime connection string into the candidate slot (`Green`) container environment via the deployment secret manager / vault.

4. **Step 4: Application Slot Startup & Health Verification**
   Launch candidate slot containers, perform synthetic health checks (`/health/ready`), and complete cutover.

### 3.4. Emergency Credential Rotation
If runtime credentials are ever suspected compromised:
1. Generate new high-entropy credentials in the deployment secret manager / vault.
2. Execute `ALTER ROLE restaurant_app_user WITH PASSWORD '<NEW_PASSWORD>';` as DBA.
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
