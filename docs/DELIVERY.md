# Delivery & Release Engineering (`docs/DELIVERY.md`)

## 1. Branching Strategy

The repository follows a clean, trunk-based feature branch workflow designed for continuous integration:

```
[ feat/feature-name ] ──────┐
                             ▼
                    [ PR / Code Review ]
                    (Tests Pass + Lint)
                             │
                             ▼
[ main (Protected) ] ─────────────────────────► [ Staging ] ──► [ Production ]
```

### 1.1. Branch Naming Conventions
- `feat/<feature-slug>`: New user-facing or platform features (e.g., `feat/kds-recall-ticket`).
- `fix/<issue-slug>`: Bug fixes and defect resolutions (e.g., `fix/printer-spooler-timeout`).
- `docs/<doc-slug>`: Documentation-only additions or updates (e.g., `docs/add-adr-002`).
- `refactor/<refactor-slug>`: Code refactoring without behavioral alterations.

### 1.2. Main Branch Protection & Quality Gates
- Direct commits to `main` are strictly prohibited (with the exception of the initial repository setup commit).
- All changes must merge into `main` via Pull Requests with passing automated tests and mandatory peer review.
- Force pushes (`git push --force`) and destructive history rewrites on `main` are permanently disabled.
- **Automated CI Quality Gates (Mandatory Order):**
  1. `Gate 1:` Repository Policy — File size limits (450 warning / 600 strict ceiling via `scripts/check-file-size.mjs`).
  2. `Gate 2:` Documentation & Link Integrity (`scripts/check-docs.mjs`).
  3. `Gate 3:` Secret & Credential Scanning (`scripts/check-secrets.mjs`).
  4. `Gate 4:` Quality Gate & Blue-Green Scripts Unit Tests (`node --test scripts/tests/`).
  5. `Gate 5:` ESLint 9 Flat Linting (`pnpm lint`).
  6. `Gate 6:` TypeScript Strict Typecheck (`pnpm typecheck`).
  7. `Gate 7:` .NET Architecture Boundary Tests (`dotnet test tests/architecture/...`).
  8. `Gate 8:` Unit & Integration Tests (.NET & React component tests).
  9. `Gate 9:` Production Builds (.NET Release build + Vite React builds).
  10. `Gate 10:` Docker Compose Configuration Validation (`docker compose -f compose.yml -f compose.dev.yml config` and all environment overrides).
- **No `continue-on-error`:** A failure at any gate halts the pipeline immediately.
- **Local Pre-Flight Command:** Developers and agents must run `pnpm verify` before creating a pull request.

---

## 2. Release Versioning (SemVer 2.0.0)

`restaurant-order` follows Semantic Versioning (`MAJOR.MINOR.PATCH`):

1. **MAJOR (`vX.0.0`):** Incompatible API changes, breaking database schema overhauls, or breaking surface redesigns.
2. **MINOR (`vx.Y.0`):** New functionality added in a backward-compatible manner.
3. **PATCH (`vx.y.Z`):** Backward-compatible bug fixes, performance improvements, and documentation enhancements.

---

## 3. Blue-Green Release Pipeline Stages

Production deployments follow a standardized, provider-independent 10-stage pipeline:

1. **Preflight:** Validates active slot (`blue`/`green`), target idle slot, and image digest parity.
2. **Config Validation:** Verifies Compose overrides and `.env.example` contracts.
3. **Database Migration Safety:** Verifies Expand-Migrate-Contract compliance and backup readiness.
4. **Deploy Inactive Slot:** Launches target slot containers without affecting live traffic.
5. **Health Probes:** Probes `/health/live` and `/health/ready` on the inactive slot.
6. **Endpoint Warmup:** Exercises endpoints to pre-warm caches, connection pools, and runtime JIT.
7. **Automated Smoke:** Runs non-mutating smoke tests (PIN auth, KDS display, printer spooler).
8. **Traffic Cutover:** Switches reverse proxy / ingress upstream pool to the new slot.
9. **Post-Cutover Observation:** Monitors error rates (`< 0.05%` threshold) and worker activation.
10. **Drain Retired Slot:** Drains in-flight connections and stops the retired slot.

---

## 4. Zero-Downtime Database Migration Guidelines

All database schema changes must adhere to the **Expand-Migrate-Contract** pattern:

```
Step 1: EXPAND                    Step 2: MIGRATE DATA              Step 3: CONTRACT
(Add new column nullable)        (Backfill existing rows)          (Deprecate old column)
[ table: old_col, new_col ]  ──►  [ new_col populated ]        ──►  [ drop old_col ]
```

### 4.1. Invariants for Safe Migrations
1. **No Destructive DDL Pre-Cutover:** `DROP TABLE`, `DROP COLUMN`, `RENAME COLUMN`, and `TRUNCATE` are strictly prohibited before cutover.
2. **Backward Compatibility:** Both Blue and Green must be compatible with the database schema simultaneously.
3. **Non-Null Defaults:** New non-nullable columns must have safe default values.
4. **Concurrent Indexing:** Indexes must be created concurrently (`CREATE INDEX CONCURRENTLY`).
5. **Rollback Compatibility:** If cutover fails, the database must remain fully compatible with the previous application version.
6. **Operational Tooling:**
   - `pnpm migration:validate`: Validates migration files against destructive DDL patterns.
   - `pnpm migration:script`: Generates audited, idempotent deployment SQL scripts.
   - `pnpm migration:apply:dev`: Applies migrations to local development database only.
   - Production API startup never applies migrations automatically. Migrations are executed as a dedicated pre-cutover pipeline step.
