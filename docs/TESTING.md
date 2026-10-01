# Testing Strategy & Verification Standards (`docs/TESTING.md`)

## 1. Quality Philosophy: Zero-Unverified-PASS Policy

In `restaurant-order`, quality is an absolute constraint. The **Zero-Unverified-PASS Policy** is non-negotiable for all human contributors and AI coding agents:

> **Core Rule:** Never report an implementation, bugfix, or test suite as `PASS`, `SUCCESS`, or `VERIFIED` unless the corresponding test or build command has actually been executed in the environment and returned an exit code of `0`. Assumptions, theoretical claims, or partial verifications are strictly prohibited.

---

## 2. The Testing Pyramid

```
                / \
               /   \
              / E2E \          (Cross-surface user journeys, Playwright)
             /-------\
            /  Integ  \        (API, Database RLS, Printer Spooler, Gateway)
           /-----------\
          /  Unit/State \      (Domain entities, State machines, Math, RBAC)
         /---------------\
```

### 2.1. Unit Tests (Fast, Isolated, In-Memory)
- **Domain Entities & Calculations:** Order line item subtotaling, modifier pricing, tax rates, split bill math, and commission fee formulas.
- **State Machine Transitions:** Testing every valid transition and asserting that every invalid transition throws a deterministic error.
- **RBAC Policy Checks:** Verifying that each of the 8 roles is strictly authorized or denied for each system capability.

### 2.2. Integration Tests (Database & Service Adapters)
- **PostgreSQL Row-Level Security (RLS):**
  - Explicit cross-tenant query tests verifying that `Tenant A` cannot read, update, or delete `Tenant B` data.
  - Runtime application role (`restaurant_app_user`) configured strictly with `NOSUPERUSER NOBYPASSRLS`.
  - Fail-closed verification: when `tenancy.get_current_tenant_id()` is unset or invalid, queries return 0 rows.
  - Bypass resistance: `IgnoreQueryFilters()` and raw SQL queries cannot bypass database-level RLS.
- **Transactional Consistency & Connection Pool Cleanup:**
  - Testing connection pool tenant session variable reset across sequential and concurrent pool reuse.
  - Clean migration application verified against a fresh PostgreSQL 16 instance.
- **Tenant Context Propagation & Middleware:**
  - HTTP middleware tenant resolution, RFC 7807 ProblemDetails on missing context, and correlation ID propagation.
  - Worker tenant context propagation with guaranteed ambient context cleanup.
- **Testcontainers Fail-Closed Guard:**
  - `TestcontainersGuard` strictly enforces that Docker is present in CI; tests fail-closed if Docker is missing.
  - Local developers without Docker can explicitly pass `SKIP_TESTCONTAINERS=true` to run non-container tests without false-positive container PASS reports.

### 2.3. End-to-End (E2E) Tests
- **Full Dining Lifecycle:**
  1. Customer scans QR and adds items to cart.
  2. Order is fired and appears on Kitchen KDS.
  3. Kitchen marks ticket `READY`.
  4. Waiter receives notification and delivers to table.
  5. Cashier splits bill and records payment.
  6. Table session closes and returns to `AVAILABLE`.

---

## 3. Mandatory Critical Test Paths

The following features are classified as **Critical Paths**. Any PR touching these paths without comprehensive automated tests will be rejected:

1. **Order Processing & Pricing:** Correctness of modifier additions, discounts, and item tax calculations.
2. **Financial Operations:** Bill splitting, tip allocations, payment balance assertions, and refund audit trails.
3. **Tenant Isolation:** Multi-tenant security tests asserting zero cross-tenant leakage.
4. **Hardware Spooler:** Printer failure resilience and manual reprint queues.
5. **Concurrency & Race Conditions:** Two guests ordering at the same instant; customer cancelling while kitchen is preparing.

---

## 4. Test Data & Synthetic Fixtures

- **Strict Zero-PII Rule:** Real customer names, phone numbers, credit card numbers, or live payment credentials must **never** be used in test files.
- Tests must utilize synthetic data factories (e.g., deterministic faker fixtures) with realistic restaurant domain data.

---

## 5. Branch & Invariant Coverage Requirements

For all critical business logic, the following automated coverage thresholds are enforced in configuration and CI gates:
- **Domain Entities & Calculations:** Minimum **90% branch coverage** and **95% statement coverage**.
- **State Machines & Transitions:** **100% transition coverage** (every valid transition and every guarded invalid transition must have explicit tests).
- **RBAC Matrix Enforcement:** **100% role-permission coverage** across all 8 roles.

---

## 6. Verification Commands Quick Reference

```bash
# Run all quality gates (file size, doc links, secret scanning, gate tests)
pnpm verify:gates

# Run backend unit tests
dotnet test tests/unit/RestaurantOrder.UnitTests.csproj

# Run backend architecture boundary tests
dotnet test tests/architecture/RestaurantOrder.ArchitectureTests.csproj

# Run backend integration tests
dotnet test tests/integration/RestaurantOrder.IntegrationTests.csproj

# Run frontend component tests
pnpm --filter @restaurant-order/ui test

# Run all test suites across the monorepo
pnpm test

# Run full verification pipeline
pnpm verify
```
