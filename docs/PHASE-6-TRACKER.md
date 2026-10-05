# Phase 6 Implementation Tracker (`docs/PHASE-6-TRACKER.md`)

This document tracks implementation progress across all 7 sub-phases of **Phase 6: Tables, QR & Sessions** in `restaurant-order`.

> **Scope Boundary & Architectural Principle:**
> Phase 6 establishes the core physical table layout, QR identity, and dining session lifecycle infrastructure.
> Order placement and lifecycle, kitchen/bar KDS routing, billing settlement, payment processing, and service calls belong strictly to subsequent phases (Phase 7: Customer Experience, Phase 8: Order Core, Phase 9: Realtime & Notifications, Phase 10: Billing & Payments).

---

## 1. Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output | Commit Ref |
| :--- | :--- | :--- | :--- | :--- |
| **Phase 6.0** | Planning and contracts | **Completed** | Implementation tracker, contracts, architecture alignment | `feat(floor)` |
| **Phase 6.1** | Tables and physical layout | **Completed** | `RestaurantTable` aggregate, composite FKs, RLS, REST APIs, Testcontainers tests | `feat(floor)` |
| **Phase 6.2** | Dining session lifecycle | **Planned** | Session state machine (Open -> Active -> Bill Requested -> Closed), concurrency tokens | Pending |
| **Phase 6.3** | Static/dynamic QR security | **Planned** | Cryptographic QR payload, signature verification, version bumping, anti-tamper | Pending |
| **Phase 6.4** | Table transfer and merge | **Planned** | Atomic table session transfer, merge validation, audit event logging | Pending |
| **Phase 6.5** | Admin/customer UI | **Planned** | Visual floor layout canvas, table status indicator, table modal, QR generator preview | Pending |
| **Phase 6.6** | Hardening and closure | **Planned** | Full regression suite, gates verification, file size checks, CI verification | Pending |

---

## 2. Detailed Milestone Deliverables

### Phase 6.0: Planning and contracts
- [x] **Repository & Delivery Alignment:**
  - Branch created from latest `main` with Phase 5 verification merged (`467737b`).
  - Created `docs/PHASE-6-TRACKER.md` tracking all 7 sub-phases.
  - Updated `docs/ROADMAP.md` setting Phase 6 status to `IN PROGRESS`.
  - Clarified architectural scope: tables, layout, QR identity, and dining session infrastructure; orders, KDS, payments, and service calls belong to future phases.
- [x] **Contract Definitions:**
  - Floor & Table TypeScript contracts in `packages/contracts/src/floor.ts`.
  - Application DTOs in `packages/application/Floor/`.
  - `IFloorService` interface for table management and layout operations.

### Phase 6.1: Tables and physical layout
- [x] **Domain Model & Invariants (`RestaurantTable`):**
  - Strongly-typed identifier: `RestaurantTableId`.
  - Scoping fields: `TenantId`, `BranchId`, `DiningAreaId`.
  - Table code/number: `TableNumber` (unique per branch within tenant).
  - Display name: `Name` (sanitized plain-text, max 100 characters).
  - Capacity: `Capacity` (positive bounded integer: 1..100).
  - Spatial coordinates: `PositionX`, `PositionY` (bounded integers: 0..10000).
  - Dimensions: `Width`, `Height` (positive bounded integers: 10..5000).
  - Orientation: `RotationDegrees` (bounded integer: 0..359).
  - Geometric shape: `TableShape` enum (`Square`, `Round`, `Rectangle`).
  - Active lifecycle: `IsActive` (boolean, default true).
  - QR identity version: `QrVersion` (integer, initial value at least 1).
  - Optimistic concurrency token: `ConcurrencyToken` (Guid).
  - Auditing timestamps: `CreatedAtUtc`, `UpdatedAtUtc`.
  - Invariant validation: HTML/script tag rejection on table number and name; inactive DiningArea assignment blocked; cross-tenant/cross-branch DiningArea assignment strictly rejected; physical deletion forbidden (activate/deactivate lifecycle).
  - Zero fake operational states (e.g., `Occupied`, `Preparing`, `Paid`); live operational status is derived dynamically from sessions and orders in subsequent phases.
- [x] **Persistence & Multi-Tenancy:**
  - `RestaurantTableConfiguration` in `tenancy` schema (`restaurant_tables`).
  - Primary key: `id` (`restaurant_table_id`).
  - Alternate key: `(tenant_id, branch_id, id)` for downstream foreign keys.
  - Unique index: `(tenant_id, branch_id, table_number)` preventing duplicate table numbers within a branch.
  - Composite foreign key to `dining_areas (tenant_id, branch_id, id)` with `DeleteBehavior.Restrict`.
  - Alternate key on `dining_areas (tenant_id, branch_id, id)`.
  - Check constraints for capacity, position, dimensions, rotation, and QR version.
  - PostgreSQL Row-Level Security enabled and forced (`FORCE ROW LEVEL SECURITY`).
  - Tenant isolation policy via `tenancy.get_current_tenant_id()`.
  - Runtime permissions granted to `restaurant_app_runtime` (SELECT, INSERT, UPDATE, DELETE).
  - Additive EF Core migration validated with blue-green expand-contract checks.
- [x] **REST API & Granular RBAC:**
  - Route root: `/api/v1/floor/branches/{branchId}`.
  - Endpoints:
    - `GET /tables`: List tables in branch (optional filters: `diningAreaId`, `isActive`).
    - `GET /tables/{tableId}`: Get table details.
    - `POST /tables`: Create new table.
    - `PUT /tables/{tableId}`: Update table metadata (number, name, capacity, diningAreaId).
    - `PUT /tables/{tableId}/layout`: Update table canvas layout (position, dimensions, rotation, shape).
    - `POST /tables/{tableId}/activate`: Activate table.
    - `POST /tables/{tableId}/deactivate`: Deactivate table.
  - Permissions:
    - Mutation: `branch.tables.manage` (authorized for `RestaurantAdmin` and `BranchManager`).
    - Floor viewing: `floor.status.view` (authorized for `RestaurantAdmin`, `BranchManager`, `Cashier`, `Waiter`).
  - Fail-closed service RBAC and route/payload branch validation.
  - Concurrency: `If-Match` / ETag support, 412 on missing token, 409 on stale token.
  - Atomic security audit logging within the same transaction.

### Phase 6.2: Dining session lifecycle (PLANNED)
- Dining session state machine (Open -> Active -> Bill Requested -> Closed).
- Session creation on guest arrival or table seating.
- Session balance and itemized ordering links.

### Phase 6.3: Static/dynamic QR security (PLANNED)
- Cryptographic signature for table QR codes.
- Dynamic vs. static QR tokens.
- QR version bumping and invalidation.

### Phase 6.4: Table transfer and merge (PLANNED)
- Table transfer mechanics.
- Table merge and session aggregation.

### Phase 6.5: Admin/customer UI (PLANNED)
- Visual canvas for table layout editor.
- Floor plan view for staff.

### Phase 6.6: Hardening and closure (PLANNED)
- End-to-end regression testing and quality gates.

---

## 3. Verification Matrix (Phase 6.0 & 6.1 Results)

| Area | Suite | Status | Executed & Verified |
| :--- | :--- | :--- | :--- |
| **Domain** | `RestaurantTableUnitTests.cs` | **PASS** | 1282 tests passed (net10.0) |
| **RBAC Matrix** | `FloorRbacMatrixUnitTests.cs` | **PASS** | 8 roles verified (`branch.tables.manage`, `floor.status.view`) |
| **Endpoints** | `FloorEndpointsUnitTests.cs` | **PASS** | RFC 7807 mappings, 412/409 concurrency, ETag headers |
| **Architecture** | `RestaurantOrder.ArchitectureTests` | **PASS** | 10 tests passed (Clean architecture, domain boundaries) |
| **Integration** | `FloorTableIntegrationTests.cs` | **PASS** | 259 integration tests passed (Testcontainers PostgreSQL, transactions, audits) |
| **PostgreSQL RLS** | `FloorTablePostgreSqlRlsIntegrationTests.cs` | **PASS** | Verified runtime role (`restaurant_app_user`), RLS isolation, composite FKs |
| **Frontend Unit** | Vitest (`ui`, `admin`, `operations`, `customer`) | **PASS** | 295 tests passed |
| **E2E Probes** | Playwright | **PASS** | 2 tests passed |
| **Quality Gates** | `pnpm verify:gates` | **PASS** | 115 tests passed across 8 suites |
| **Database Migrations**| `node scripts/migration-ops.mjs validate` | **PASS** | All 48 migration files verified (non-destructive) |
