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

### 1.2. Main Branch Protection Rules
- Direct commits to `main` are strictly prohibited (with the exception of the initial repository setup commit).
- All changes must merge into `main` via Pull Requests with passing automated tests and mandatory peer review.
- Force pushes (`git push --force`) and destructive history rewrites on `main` are permanently disabled.

---

## 2. Release Versioning (SemVer 2.0.0)

`restaurant-order` follows Semantic Versioning (`MAJOR.MINOR.PATCH`):

1. **MAJOR (`vX.0.0`):** Incompatible API changes, breaking database schema overhauls, or breaking surface redesigns.
2. **MINOR (`vx.Y.0`):** New functionality added in a backward-compatible manner (e.g., adding a new payment gateway, adding allergen badges).
3. **PATCH (`vx.y.Z`):** Backward-compatible bug fixes, performance improvements, and documentation enhancements.

---

## 3. Zero-Downtime Database Migration Guidelines

Because restaurants operate across extended hours without downtime windows, all database schema migrations must follow the **Expand and Contract (Parallel Run)** pattern:

```
Step 1: EXPAND                    Step 2: MIGRATE DATA              Step 3: CONTRACT
(Add new column nullable)        (Backfill existing rows)          (Deprecate old column)
[ table: old_col, new_col ]  ──►  [ new_col populated ]        ──►  [ drop old_col ]
```

### 3.1. Rules for Safe Migrations
1. **Never Rename Columns Directly:** Add the new column, write to both in application code, backfill data, and only drop the old column in a subsequent release.
2. **Non-Null Columns Must Have Defaults:** When adding a `NOT NULL` column to an existing table, provide a safe default value or make it nullable during the expand phase.
3. **Index Creation:** Create indexes concurrently (`CREATE INDEX CONCURRENTLY`) to prevent locking production tables during peak ordering hours.
