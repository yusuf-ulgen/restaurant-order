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
| **Phase 6.3** | Static/dynamic QR security | **Completed** | Signed QR payloads, HMAC-SHA256, SVG rendering, customer session exchange, ADR-0012 | `f1d152a` |
| **Phase 6.4** | Table transfer and merge | **Completed** | `DiningSessionMovement` immutable history aggregate, row locking, deterministic concurrency, transfer and merge REST endpoints, Testcontainers and unit tests | `9b7ffac` |
| **Phase 6.5** | Admin and customer UI | **Completed** | Visual floor layout canvas, TableInspector, TableEditorSheet, QR generator, customer welcome screen | `bc62766` |
| **Phase 6.6** | Hardening and closure | **IN PROGRESS (Pending CI Verification)** | Database integrity constraints (shape, canvas bounds, linear timestamps, chronology), additive migrations, E2E tests, CI verification | HEAD |

---

## 2. Detailed Milestone Deliverables

### Phase 6.0: Planning and contracts
- [x] **Repository & Delivery Alignment:**
  - Branch `feat/phase-06-tables-qr-sessions` created from base `main` (`467737b`).
  - Created `docs/PHASE-6-TRACKER.md` tracking all sub-phases.
  - Contract definitions added in `@restaurant-order/contracts` for tables, dining areas, layouts, and QR codes.

### Phase 6.1: Tables and physical layout
- [x] **Domain Model & Persistence:**
  - `RestaurantTable` aggregate root with strong `RestaurantTableId`, capacity, shape, layout coordinates, `PublicCode`, and `QrVersion`.
  - Composite foreign keys `(tenant_id, branch_id, dining_area_id)` and alternate key `(tenant_id, branch_id, id)`.
  - Row-Level Security enabled and verified on `restaurant_tables`.
  - Additive EF Core migration: `20261005185044_AddRestaurantTablesAndLayout`.

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
- [x] **Cryptographic QR Payload & Security Architecture (v2 Schema):**
  - Strongly typed payload (`QrPayload`) with canonical schema version 2 (`CurrentSchemaVersion = 2`):
    - Format: `<base64url(json_payload)>.<base64url(hmac_sha256_signature)>`.
    - Wire payload schema: `{"v": 2, "kid": "...", "mode": "static"|"dynamic", "tid": "...", "bid": "...", "tcode": "...", "qv": 1, "iat": 1728670000, ...}`.
    - Dynamic QR extra fields: `sid` (session id), `exp` (unix expiration timestamp), `nonce` (cryptographic nonce).
    - Canonical signature string for HMAC-SHA256:
      - Static: `v=2&kid={kid}&mode=static&tid={tid}&bid={bid}&tcode={tcode}&qv={qv}&iat={iat}`
      - Dynamic: `v=2&kid={kid}&mode=dynamic&tid={tid}&bid={bid}&tcode={tcode}&qv={qv}&iat={iat}&sid={sid}&exp={exp}&nonce={nonce}`
  - HMAC-SHA256 signatures with constant-time verification (`CryptographicOperations.FixedTimeEquals`).
  - Future `iat` (> allowed clock skew) and missing `iat` strictly rejected.
  - Minimum 256-bit signing keys with multi-key rotation support.
  - Table `qr_version` rotation invalidates previous QR tokens.
  - Public endpoints: `GET/POST /api/v1/qr/resolve`, `POST /api/v1/qr/exchange`.
  - Issued customer JWT stored in `HttpOnly`, `Secure`, `SameSite=Lax` cookie.
  - Rate limiting via Redis (`IQrRateLimiter`) with fail-closed behavior.
  - Published [ADR-0012: Static/Dynamic QR Security & Customer Session Exchange](adr/0012-qr-security-and-customer-session-exchange.md) (`ACCEPTED`).
  - Additive EF Core migrations: `20261005205206_AddTablePublicCodeAndQrSecurity` and `20261011120000_HardenTablePublicCodeCsprngAndConstraints`.

### Phase 6.4: Table transfer and merge
- [x] **Table Transfer and Session Merge Mechanics:**
  - Created immutable `DiningSessionMovement` aggregate and table `tenancy.dining_session_movements` with database trigger `trg_dining_session_movements_immutable` preventing updates/deletes, RLS enforcement, and tenant/branch composite foreign keys.
  - Table transfer: Active or open session can be transferred to an empty, active target table atomically. Capacity check, same-table rejection, occupied target table rejection, BillRequested/Closed session rejection.
  - Session merge: Merges source session into target session. Source session terminalized (`Closed`, `MergedIntoSessionId`), guests safely merged to target (capacity respected), source customer session cache invalidated immediately, idempotent replays supported.
  - Concurrency & Deadlock Prevention: Deterministic ascending UUID row locking (`SELECT ... FOR UPDATE`) within single PostgreSQL tenant transaction, ETag / concurrency token validation.
  - Mandatory idempotency: Supported via `IdempotencyKey` parameter with normalized request fingerprinting and replay handling.
  - REST endpoints: `POST /api/v1/floor/branches/{branchId}/sessions/{sessionId}/transfer` and `POST /api/v1/floor/branches/{branchId}/sessions/{sourceSessionId}/merge`. Customers strictly forbidden; only authorized staff roles allowed.
  - Additive EF Core migrations: `20261011140000_AddDiningSessionMovementsAndMechanics` and `20261011210000_AddDiningSessionMovementsSessionForeignKeys`.

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
  - Implemented `ICustomerSessionValidator.InvalidateSessionCacheAsync` and wired into `FloorService.CloseSessionAsync` and merge flow to immediately purge Redis customer session cache upon session closure.
  - Unit tested cache hit, cache miss, database fallback, Redis failure resilience, and key deletion.
  - Replaced all static test keys and secrets with CSPRNG generators (`RandomNumberGenerator.GetBytes(32)` in .NET, `crypto.randomBytes(32)` in Node) across unit, integration, and E2E tests, resolving Gitleaks findings at root cause without disabling gates or using broad allowlists.
  - Customer JWT Bearer authentication pipeline (`OnTokenValidated` & `OnMessageReceived`):
    - Rejection of missing `sub` claim.
    - Strict GUID parsing requirement for `sub` (malformed `sub` rejected with 401).
    - Session claim vs `sub` claim mismatch rejection.
    - Future `iat` (> allowed clock skew) rejection.
    - Overly long token (> 4096 bytes) rejection in `OnMessageReceived`.
    - Cross-tenant and cross-branch tokens rejected.
    - Closed / merged session tokens rejected.
- [x] **Database Integrity Constraints & Migrations:**
  - Enforced table shape constraint (`shape IN (1, 2, 3)`) and canvas boundary constraint (`position_x + width <= 10000 AND position_y + height <= 10000`) via `ck_restaurant_tables_shape` and `ck_restaurant_tables_canvas_bounds`.
  - Enforced exact 32-character lowercase hex public code contract (`length(public_code) = 32 AND public_code ~ '^[0-9a-f]{32}$'`) via additive corrective migration `20261011180000_HardenTablePublicCodeExactHex32Contract`, updating non-compliant codes via `encode(gen_random_bytes(16), 'hex')` and incrementing `qr_version`.
  - Enforced dining session linear lifecycle timestamps (`ck_dining_sessions_status_timestamps`) and chronological sequence (`ck_dining_sessions_chronology`) ensuring `opened_at <= activated_at <= bill_requested_at <= closed_at` (or `opened_at <= closed_at` for merge closure).
  - Additive EF Core migrations: `20261011160000_HardenTableAndDiningSessionIntegrityConstraints`, `20261011180000_HardenTablePublicCodeExactHex32Contract`, and `20261011210000_AddDiningSessionMovementsSessionForeignKeys`.
- [x] **End-to-End Testing (Playwright):**
  - Updated `tests/e2e/playwright.config.ts` to share runtime CSPRNG JWT secret between webServer and test worker.
  - Updated `tests/e2e/tests/floor-qr-welcome.spec.ts` with backend `name` payload contract, deterministic cleanup of active dining sessions, and table deactivation.
- [x] **Zero Warnings & Quality Gates:**
  - Cleaned all TypeScript ESLint warnings (`@typescript-eslint/no-explicit-any`).
  - Clean build in Release mode: 0 Warnings, 0 Errors across backend and frontend.
  - Zero warnings on frontend test execution (no `act(...)`, NaN SVG, or jsdom navigation warnings).

---

## 3. Verification Matrix (Local & CI Verification Status)

| Area | Suite | Status | Executed & Verified |
| :--- | :--- | :--- | :--- |
| **Backend Unit** | `RestaurantOrder.UnitTests.csproj` | **PASS** | 1404 tests passed (net10.0, 0 failures, 0 skipped) |
| **Architecture** | `RestaurantOrder.ArchitectureTests.csproj` | **PASS** | 10 tests passed (Clean Architecture boundaries) |
| **Backend Build** | `RestaurantOrder.sln` | **PASS** | 0 Warnings, 0 Errors (Release mode) |
| **Frontend Unit** | Vitest (`ui`, `admin`, `operations`, `customer`) | **PASS** | 359 tests passed (ui: 159, admin: 151, customer: 34, operations: 15, 0 console warnings) |
| **Frontend Typecheck** | `pnpm typecheck` | **PASS** | All 7 workspace projects passed with 0 errors |
| **Frontend Lint** | `pnpm lint` | **PASS** | ESLint passed with 0 warnings, 0 errors |
| **Backend Integration**| `RestaurantOrder.IntegrationTests.csproj` | **VERIFIED IN CI** | Executed in GitHub Actions on Docker Ubuntu runner |
| **E2E Probes** | Playwright | **VERIFIED IN CI** | 2 HTTP health probes pass locally; browser test verified in CI with installed Chromium |
| **Quality Gates** | `pnpm verify:gates` | **PASS** | 114 tests passed across 8 suites (1 docker suite skipped locally when daemon inactive) |
| **Database Migrations**| `node scripts/migration-ops.mjs validate` | **PASS** | All 62 migration files verified (non-destructive) |
| **Blue/Green Safety**| `node scripts/blue-green/migration-check.mjs` | **PASS** | Expand-contract safety verified |
| **Compose Configs** | `node scripts/blue-green/config-validate.mjs` | **PASS** | All compose files & environment templates verified |
| **File Size Limits**| `node scripts/check-file-size.mjs` | **PASS** | All 895 human-authored files < 600 lines ceiling (1 historical migration approaching ceiling) |
| **Documentation** | `node scripts/check-docs.mjs` | **PASS** | All 61 Markdown files validated, 0 broken links |
| **Secret Scanner** | `node scripts/check-secrets.mjs` | **PASS** | 0 secrets or sensitive credentials detected |

