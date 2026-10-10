# Phase 6 Implementation Tracker (`docs/PHASE-6-TRACKER.md`)

This document tracks implementation progress across all sub-phases of **Phase 6: Tables, QR & Sessions** in `restaurant-order`.

> **Scope Boundary & Architectural Principle:**
> Phase 6 establishes the core physical table layout, QR identity, and dining session lifecycle infrastructure.
> Order placement and lifecycle, kitchen/bar KDS routing, billing settlement, payment processing, and service calls belong strictly to subsequent phases:
> - Phase 7: Customer Experience (Menu browsing, ordering, cart) — **Not yet implemented**
> - Phase 8: Order Core (Order lifecycle, line items, price calculation) — **Not yet implemented**
> - Phase 9: Realtime & Notifications (SignalR hub, order dispatch) — **Not yet implemented**
> - Phase 13: Billing Engine (Bill generation, bill splitting, tips) — **Not yet implemented**
> - Phase 16: Payments (Payment gateway integration, card readers) — **Not yet implemented**

---

## 1. Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output | Commit Ref |
| :--- | :--- | :--- | :--- | :--- |
| **Phase 6.0** | Planning and contracts | **Completed** | Implementation tracker, contracts, architecture alignment | `48cfae6` |
| **Phase 6.1** | Tables and physical layout | **Completed** | `RestaurantTable` aggregate, composite FKs, RLS, REST APIs, Testcontainers tests | `48cfae6` |
| **Phase 6.2** | Dining session lifecycle | **Completed** | `DiningSession` aggregate, PostgreSQL partial unique index, state machine, RBAC & REST APIs | `b875eeb` |
| **Phase 6.3** | Static/dynamic QR security | **Completed** | Signed QR payloads, HMAC-SHA256, SVG rendering, customer session exchange, ADR-0011 | `f1d152a` |
| **Phase 6.4** | Table transfer and merge | **Deferred** | Deferred to Phase 10 (Waiter & Operations) where order and bill settlement lifecycles are integrated | `[Deferred to Phase 10]` |
| **Phase 6.5** | Admin and customer UI | **Completed** | Visual floor layout canvas, TableInspector, TableEditorSheet, QR generator, customer welcome screen | `bc62766` |
| **Phase 6.6** | Hardening and closure | **Completed** | Cache invalidation on session close, comprehensive unit/coverage suites, gates verification | HEAD |

---

## 2. Detailed Milestone Deliverables

### Phase 6.0: Planning and contracts
- [x] **Repository & Delivery Alignment:**
  - Branch created from latest `main` with Phase 5 verification merged (`467737b`).
  - Created `docs/PHASE-6-TRACKER.md` tracking all sub-phases.
  - Contract definitions added in `@restaurant-order/contracts` for tables, dining areas, layouts, and QR codes.

### Phase 6.1: Tables and physical layout
- [x] **Domain Model & Persistence:**
  - `RestaurantTable` aggregate root with strong `RestaurantTableId`, capacity, shape, layout coordinates, `PublicCode`, and `QrVersion`.
  - Composite foreign keys `(tenant_id, branch_id, dining_area_id)` and alternate key `(tenant_id, branch_id, id)`.
  - Row-Level Security enabled and verified on `restaurant_tables`.
  - Additive EF Core migration: `20261005182103_AddRestaurantTablesAndPhysicalLayout`.

### Phase 6.2: Dining session lifecycle
- [x] **Dining Session Aggregate & State Machine:**
  - `DiningSession` aggregate root with strict state machine: `Open -> Active -> BillRequested -> Closed (terminal)`.
  - PostgreSQL partial unique index `ix_dining_sessions_tenant_branch_table_active` enforcing at most one non-closed session per table.
  - Database check constraints on status, guest count, and chronological UTC timestamps.
  - Additive EF Core migration: `20261005203226_AddDiningSessionsAndLifecycle`.
- [x] **REST API & Granular RBAC:**
  - Endpoints under `/api/v1/floor/branches/{branchId}`: `GET /status`, `GET /tables/{tableId}/session`, `POST /tables/{tableId}/session`, `POST /sessions/{sessionId}/activate`, `POST /sessions/{sessionId}/request-bill`, `POST /sessions/{sessionId}/close`.
  - Customer own-session authorization enforcement; customer prohibited from closing sessions.
  - Concurrency token / ETag verification on all mutations.

### Phase 6.3: Static/dynamic QR security
- [x] **Cryptographic QR Payload & Security Architecture:**
  - Strongly typed payload (`QrPayload`) with canonical pipe-delimited format:
    - Static: `v=1|kid={key_id}|mode=1|t={tenant}|b={branch}|tc={public_code}|qv={qr_version}`
    - Dynamic: `v=1|kid={key_id}|mode=2|t={tenant}|b={branch}|tc={public_code}|qv={qr_version}|sid={session_id}|exp={exp_unix}|nce={nonce}`
  - HMAC-SHA256 signatures with constant-time verification (`CryptographicOperations.FixedTimeEquals`).
  - Minimum 256-bit signing keys with multi-key rotation support.
  - Public endpoints: `GET/POST /api/v1/qr/resolve`, `POST /api/v1/qr/exchange`.
  - Issued customer JWT stored in `HttpOnly`, `Secure`, `SameSite=Lax` cookie.
  - Rate limiting via Redis (`IQrRateLimiter`) with fail-closed behavior.
  - Published [ADR-0011: Static/Dynamic QR Security & Customer Session Exchange](adr/0011-qr-security-and-customer-session-exchange.md) (`ACCEPTED`).

### Phase 6.4: Table transfer and merge (Deferred)
- **Architectural Scope Decision:** Table transfer and session merge operations operate directly upon active orders, unserved kitchen tickets, and split bills. Implementing table transfer in Phase 6 without order and billing aggregates would result in a throwaway stub. Transfer and merge mechanics are formally deferred to **Phase 10: Waiter & Operations** as documented in [docs/ROADMAP.md](ROADMAP.md).

### Phase 6.5: Admin and customer UI
- [x] **ADM-04 Floor & Table Layout (Admin Web):**
  - Dining area tabs with table counts.
  - Visual floor canvas (`FloorCanvas`) with SVG grid, keyboard accessibility (Arrow keys), and drag repositioning.
  - Numeric coordinate inspector (`TableInspector`) with X/Y, dimensions, rotation, and shape controls.
  - Responsive modal/bottom sheet (`TableEditorSheet`) for table creation, updates, and active/passive toggling.
  - Atomic batch layout save with concurrency token verification.
- [x] **QR Management & Print Preview (Admin Web):**
  - Static vs dynamic QR view with SVG preview and metadata (`QrGeneratorView`).
  - QR version revocation and rotation with confirmation dialog.
  - Batch print preview for all tables without external PDF dependencies.
- [x] **CUST-01 Table Welcome & Session Landing (Customer Web):**
  - QR token resolve (`/q/:token`) with sanitization and XSS protection (`TableWelcomeView`).
  - Clean URL replaceState stripping raw cryptographic tokens from browser history.
  - In-memory session store (zero tokens in `localStorage` or `sessionStorage`).
  - Multilingual support (Turkish / English).
  - Clear error states: invalid QR (400), revoked QR (410), inactive table (400), rate limit (429), server error (500).

### Phase 6.6: Hardening and closure
- [x] **Token Invalidation & Security Hardening:**
  - Implemented `ICustomerSessionValidator.InvalidateSessionCacheAsync` and wired into `FloorService.CloseSessionAsync` to immediately purge Redis customer session cache upon session closure.
  - Unit tested cache hit, cache miss, database fallback, Redis failure resilience, and key deletion.
- [x] **Zero Warnings & Quality Gates:**
  - Cleaned all TypeScript ESLint warnings (`@typescript-eslint/no-explicit-any`).
  - Removed whitespace issues flagged by `git diff --check`.
  - Verified 100% passing test suites across backend and frontend.

---

## 3. Verification Matrix (Phase 6 Final Closure Results)

| Area | Suite | Status | Executed & Verified |
| :--- | :--- | :--- | :--- |
| **Backend Unit** | `RestaurantOrder.UnitTests.csproj` | **PASS** | 1366 tests passed (net10.0, 0 failures, 0 skipped) |
| **Architecture** | `RestaurantOrder.ArchitectureTests.csproj` | **PASS** | 10 tests passed (Clean Architecture boundaries) |
| **Backend Build** | `RestaurantOrder.sln` | **PASS** | 0 Warnings, 0 Errors (Release mode) |
| **Frontend Unit** | Vitest (`ui`, `admin`, `operations`, `customer`) | **PASS** | 359 tests passed (ui: 159, admin: 151, customer: 34, operations: 15) |
| **Frontend Coverage** | Vitest V8 Coverage (`admin-web`, `customer-web`, `operations-web`, `ui`) | **PASS** | Met global thresholds (lines >= 80%, funcs >= 80%, stmts >= 80%, branch >= 80%) |
| **Frontend Build** | `pnpm build:frontend` | **PASS** | All 7 projects built successfully with zero errors |
| **E2E Probes** | Playwright | **PASS** | 2 passed, 1 skipped (live backend seed dependent) |
| **Quality Gates** | `pnpm verify:gates` | **PASS** | 115 tests passed across 8 suites |
| **Database Migrations**| `node scripts/migration-ops.mjs validate` | **PASS** | All 52 migration files verified (non-destructive) |
| **Blue/Green Safety**| `node scripts/blue-green/migration-check.mjs` | **PASS** | Expand-contract safety verified |
| **Compose Configs** | `node scripts/blue-green/config-validate.mjs` | **PASS** | All compose files & environment templates verified |
| **File Size Limits**| `node scripts/check-file-size.mjs` | **PASS** | All 863 human-authored files < 600 lines ceiling |
| **Documentation** | `node scripts/check-docs.mjs` | **PASS** | All 55 Markdown files validated, 0 broken links |
| **Secret Scanner** | `node scripts/check-secrets.mjs` | **PASS** | 0 secrets or sensitive credentials detected |
