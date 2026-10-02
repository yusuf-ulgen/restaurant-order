# Phase 4 Implementation Tracker (`docs/PHASE-4-TRACKER.md`)

This document tracks implementation progress across all 6 sub-phases of **Phase 4: Restaurant Configuration** in `restaurant-order`.

---

## 1. Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output | Commit SHA |
| :--- | :--- | :--- | :--- | :--- |
| **Phase 4.1** | Tenant-Scoped Brand & Branch Management | **Completed** | Brand & branch CRUD APIs, state transitions, concurrency tokens, ETags, RBAC | (Pending commit) |
| **Phase 4.2** | Operating Hours & Weekly Schedules | **Not Started** | Day-of-week operating hours, shift windows, holiday overrides | - |
| **Phase 4.3** | Service Charges, Gratuity & Default Tips | **Not Started** | Percentage/fixed service fees, auto-gratuity rules, tip presets | - |
| **Phase 4.4** | Tax Rates, Tax Categories & Pricing Mode | **Not Started** | Tax rate definitions, tax inclusive/exclusive configurations | - |
| **Phase 4.5** | Branch Dining Areas & Station Definitions | **Not Started** | Physical areas (Terrace, Indoor), prep stations (Kitchen, Bar) | - |
| **Phase 4.6** | Admin Web Config Integration & Security Closure | **Not Started** | Admin panel configuration UI, audit verification, coverage gate | - |

---

## 2. Detailed Milestone Deliverables

### Phase 4.1: Tenant-Scoped Brand & Branch Management
- [x] **Brand Management Application & API:**
  - `GET /api/v1/restaurant-config/brands`: List brands in current tenant (`tenant.brands.manage`).
  - `GET /api/v1/restaurant-config/brands/{brandId}`: Get single brand details with ETag.
  - `POST /api/v1/restaurant-config/brands`: Create brand with unique slug within tenant.
  - `PUT /api/v1/restaurant-config/brands/{brandId}`: Update name with optimistic concurrency token.
  - `POST /api/v1/restaurant-config/brands/{brandId}/activate`: Activate inactive brand.
  - `POST /api/v1/restaurant-config/brands/{brandId}/deactivate`: Deactivate active brand.
- [x] **Branch Management Application & API:**
  - `GET /api/v1/restaurant-config/branches`: List branches in tenant, optionally filtered by `brandId`.
  - `GET /api/v1/restaurant-config/branches/{branchId}`: Get branch details with ETag.
  - `POST /api/v1/restaurant-config/branches`: Create branch attached to tenant and brand (`tenant.branches.manage`).
  - `PUT /api/v1/restaurant-config/branches/{branchId}`: Update name, timezone, currency with concurrency check.
  - `POST /api/v1/restaurant-config/branches/{branchId}/activate`: Activate suspended branch.
  - `POST /api/v1/restaurant-config/branches/{branchId}/suspend`: Suspend active branch.
  - `POST /api/v1/restaurant-config/branches/{branchId}/close`: Permanently close branch.
- [x] **Security, Isolation & Lifecycle Constraints:**
  - Strict tenant scoping from verified JWT principal and ambient tenant context (no body/query bypass).
  - BranchManager restrictions: cannot create brands/branches, cannot modify branches, can only view assigned branch.
  - SuperAdmin restriction: cannot bypass tenant permissions; platform onboarding handled in Phase 15.
  - Concurrency token / ETag enforcement: stale token returns `409 Conflict`, missing token on update returns `412 Precondition Failed`.
  - Terminal closed state: closed branches cannot be modified, activated, or suspended (`400 Bad Request`).
  - Duplicate slug prevention within same tenant (`409 Conflict`), while different tenants can share identical slugs.
  - Append-only security audit log recording for brand and branch lifecycle operations.
  - Full RFC 7807 `application/problem+json` compliance across all 400, 401, 403, 404, 409, and 412 responses.
- [x] **Automated Tests:**
  - Brand lifecycle unit tests (`BrandLifecycleUnitTests.cs`).
  - Branch lifecycle unit tests (`BranchLifecycleUnitTests.cs`).
  - RBAC permission matrix unit tests (`RestaurantConfigRbacMatrixUnitTests.cs`).
  - Integration tests for brand & branch workflows (`RestaurantConfigIntegrationTests.cs`).
  - Integration tests for RBAC, scope, tenant isolation, and CSRF (`RestaurantConfigRbacAndIsolationIntegrationTests.cs`).

### Phase 4.2: Operating Hours & Weekly Schedules
- [ ] Backend data models and migration for weekly operating hours.
- [ ] Special holiday and temporary closure schedule overrides.
- [ ] Active hours evaluation service (`IsOpenAt(DateTimeUtc)`).
- [ ] Unit and integration tests for schedule calculations and timezone offsets.

### Phase 4.3: Service Charges, Gratuity & Default Tips
- [ ] Branch service charge rate configuration entity and persistence.
- [ ] Minimum party size auto-gratuity threshold settings.
- [ ] Configurable tip suggestion presets (e.g., 10%, 15%, 20%).
- [ ] Currency and rounding validation unit tests.

### Phase 4.4: Tax Rates, Tax Categories & Pricing Mode
- [ ] Value-Added Tax (VAT) rate category entities (Standard, Reduced, Zero).
- [ ] Tax inclusive vs. exclusive pricing model setting per branch.
- [ ] Line item tax calculation contracts for menu pricing.
- [ ] Unit and integration tests.

### Phase 4.5: Branch Dining Areas & Station Definitions
- [ ] Dining area aggregate (Indoor, Terrace, Garden, Bar Area).
- [ ] Station routing definitions (Kitchen, Bar, Bakery, Service Station).
- [ ] Relationship mapping to branches and printer stations.
- [ ] Concurrency and RBAC validation.

### Phase 4.6: Admin Web Config Integration & Security Closure
- [ ] Admin Web brand management screens and modals.
- [ ] Admin Web branch settings screens (operating hours, taxes, fees).
- [ ] Operations Web and Customer Web config consumption.
- [ ] Security audit remediation, coverage closure (>= 80%), and final sign-off.

---

## 3. Verified Command Results (Phase 4.1)

| Command | Scope | Result | Details |
| :--- | :--- | :--- | :--- |
| `dotnet build RestaurantOrder.sln` | Backend Solution | **PASS** | 0 Warnings, 0 Errors |
| `dotnet test tests/unit/` | Unit Test Suite | **PASS** | 821 / 821 passed (100%) |
| `dotnet test tests/architecture/` | Architecture Guard | **PASS** | 10 / 10 passed (100%) |
| `pnpm test` | All Tests (C# & TS) | **PASS** | 821 backend, 10 arch, 204 frontend passed |
| `pnpm lint` | ESLint (TS / TSX) | **PASS** | 0 Warnings, 0 Errors |
| `pnpm typecheck` | TypeScript | **PASS** | 7 / 7 workspace projects clean |
| `pnpm build` | Production Build | **PASS** | Backend DLLs + 3 Vite web apps built cleanly |
| `pnpm verify:gates` | Quality Gates | **PASS** | File sizes, doc integrity, secrets, blue-green tests |

---

## 4. Known Risks & Mitigations

1. **Risk:** BranchManager attempting to modify branch settings in multi-branch organizations.  
   **Mitigation:** `tenant.branches.manage` is granted strictly to `RestaurantAdmin`. BranchManager is denied this capability and endpoints reject unauthorized callers with RFC 7807 `403 Forbidden`.
2. **Risk:** Accidental modification of permanently closed branches.  
   **Mitigation:** Domain invariant in `Branch.EnsureNotClosed()` enforces immutability on terminal `Closed` status, verified by unit and integration tests.
3. **Risk:** Stale updates overwriting concurrent administrative edits.  
   **Mitigation:** Optimistic concurrency tokens checked on every mutation. Missing token yields `412 Precondition Failed`; mismatched token yields `409 Conflict`.

---

## 5. Pending Architecture Decisions

- None for Phase 4.1. Core domain models (`Brand`, `Branch`) and PostgreSQL RLS from Phase 2 reused and extended without breaking changes.
