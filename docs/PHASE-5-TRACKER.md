# Phase 5 Implementation Tracker (`docs/PHASE-5-TRACKER.md`)

This document tracks implementation progress across all 7 sub-phases of **Phase 5: Menu & Catalog** in `restaurant-order`.

---

## 1. Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output | Commit SHA |
| :--- | :--- | :--- | :--- | :--- |
| **Phase 5.0** | Delivery Baseline & Hardening Preparation | **Completed** | Clean baseline, safe RFC 7807 500s, correlation IDs, warning-free React test suite | `717348b` |
| **Phase 5.1** | Tenant-Scoped Menu & Category Management | **Completed** | Menu & MenuCategory aggregates, branch-scoped catalog, slug uniqueness, lifecycle, RLS, REST API | `d040db8` |
| **Phase 5.2** | Menu Items, Portions & Variant Pricing Models | **Pending** | MenuItem aggregate, ItemVariant pricing, dietary/allergen badges, station routing | - |
| **Phase 5.3** | Modifier Groups & Customization Rules | **Pending** | ModifierGroup & ModifierItem models, min/max selection rules, price deltas | - |
| **Phase 5.4** | Menu Catalog REST APIs & EF Core Persistence | **Pending** | Canonical REST endpoints, ETag/concurrency token guards, EF Core migrations, audit logs | - |
| **Phase 5.5** | Real-Time Availability & Instant 86 Stockout Engine | **Pending** | Fast 86 toggle API, branch availability overrides, real-time event publishing | - |
| **Phase 5.6** | Admin Catalog Management UI & Final Verification | **Pending** | Admin Web catalog editor, modifier configurator, 86 modal, CI quality gates closure | - |

---

## 2. Detailed Milestone Deliverables

### Phase 5.0: Delivery Baseline & Hardening Preparation
- [x] **Repository & Delivery Alignment:**
  - Verified `main` branch includes Phase 4 verification closure commit `6ded881`.
  - Created working branch `feat/phase-05-menu-catalog`.
  - Created `docs/PHASE-5-TRACKER.md` and updated `docs/ROADMAP.md` (Phase 5: IN PROGRESS).
  - Updated Phase 4 tracker frontend test counts (Admin Web: 77 tests, Total frontend: 259 tests).
- [x] **API Error Handling & Security Hardening:**
  - Removed raw `Console.Error.WriteLine` and stack trace logging from `RestaurantConfigEndpoints.cs`.
  - Configured expected 4xx domain errors to log at `Information` level rather than `Error`/critical.
  - Ensured unexpected 500 responses never leak exception messages or stack traces to callers.
  - Generates safe, standardized RFC 7807 `ProblemDetails` with `correlationId` on all 500 error responses.
  - Global middleware exception handler catches unhandled pipeline exceptions safely.
- [x] **Frontend Quality & Warning Cleanup:**
  - Fixed `border` vs `borderColor` shorthand/longhand property collision in `Button.tsx` and `IconButton.tsx`.
  - Fixed `act(...)` warning in Admin Web `ProtectedRoute` loading test.
  - Fixed `act(...)` warning in Admin Web `App.test.tsx` layout initial view tests.
  - Zero warning suppression, zero test muting, and zero coverage reduction.

### Phase 5.1: Tenant-Scoped Menu & Category Management
- [x] **Branch-Scoped Menu Aggregate Root:**
  - Strict tenant and branch scoping enforcing tenant isolation (`TenantId`, `BranchId`).
  - Menu fields: `Id`, `TenantId`, `BranchId`, `Name`, `Slug`, `Description`, `Status`, `SortOrder`, `CreatedAtUtc`, `UpdatedAtUtc`, `ConcurrencyToken`.
  - State machine: `Draft -> Active -> Archived`. `Archived` is strictly terminal (no reactivations, no mutations, no hard delete).
  - Slug uniqueness per branch, positive sort orders, HTML tag sanitization.
- [x] **MenuCategory Entity & Ordering:**
  - Hierarchical grouping scoped to Tenant, Branch, and parent Menu.
  - Fields: `Id`, `TenantId`, `BranchId`, `MenuId`, `Name`, `Slug`, `Description`, `SortOrder`, `IsActive`, `ConcurrencyToken`, timestamps.
  - Display order index with atomic reorder capabilities and strict concurrency token validation across all categories.
  - Unique slug per parent Menu scope.
  - Active/Inactive toggle with concurrency verification.
  - Mutation guard: changes blocked if parent Menu is archived or Branch is closed/suspended.
- [x] **Persistence & Row-Level Security:**
  - EF Core configurations (`MenuConfiguration`, `MenuCategoryConfiguration`) in `tenancy` schema.
  - Composite foreign keys (`tenant_id`, `branch_id`) to `branches` and (`tenant_id`, `menu_id`) to `menus`.
  - PostgreSQL Row Level Security enabled and forced (`FORCE ROW LEVEL SECURITY`).
  - Tenant isolation policies using `tenancy.get_current_tenant_id()`.
  - Runtime permissions granted to `restaurant_app_runtime`.
  - EF Core migration `20261004140337_AddMenusAndCategories` generated via official EF tooling and validated with `migration-ops.mjs`.
- [x] **REST API & RBAC Matrix:**
  - Endpoints at `/api/v1/catalog/branches/{branchId}/menus/...` for Menus and Categories.
  - Permissions: `menu.catalog.view` (all roles including Customer), `menu.catalog.manage` (RestaurantAdmin, BranchManager).
  - BranchManager access strictly verified against assigned `BranchId` (cross-branch returns 403).
  - SuperAdmin prevented from bypassing tenant context (cross-tenant returns 403/404).
  - Concurrency token checked via request body or `If-Match` header (missing -> 412 Precondition Failed, stale -> 409 Conflict).
  - Standardized RFC 7807 ProblemDetails returned on all failure branches.
  - Audit logging via `SecurityAuditEvent` on all mutations (`menu_created`, `menu_updated`, `menu_activated`, `menu_archived`, `menu_category_created`, etc.).
- [x] **Verification & Test Coverage:**
  - Domain unit tests in `MenuAndCategoryUnitTests.cs` (lifecycle, terminal state, input validation, sort order).
  - Endpoint & RBAC unit tests in `CatalogEndpointsUnitTests.cs` and `CatalogEndpointsHandlerUnitTests.cs`.
  - Global query filter tests in `GlobalTenantQueryFilterTests.cs`.
  - End-to-end integration tests in `CatalogIntegrationTests.cs` and `CatalogIntegrationTests.Categories.cs`.
  - 1051 unit tests, 10 architecture tests, 11 integration tests all verified passing.


### Phase 5.2: Menu Items, Portions & Variant Pricing Models
- [ ] **MenuItem Aggregate Root:**
  - Name, description, image asset URL validation (`AssetUrl`), preparation station ID.
  - Dietary flags: Vegetarian, Vegan, Gluten-Free, Halal, Kosher, Dairy-Free.
  - Allergen indicators: Peanuts, Tree Nuts, Shellfish, Dairy, Eggs, Gluten, Soy, Fish, etc.
  - Spice level indicator (0 to 3 flames).
- [ ] **ItemVariant Entity & Monetary Pricing:**
  - Portions/sizes (e.g., "Regular", "Large", "200g", "300g").
  - Base price represented as integer minor currency units (cents/kuruş) to eliminate floating-point drift.
  - Tax and service charge applicability based on branch financial settings.

### Phase 5.3: Modifier Groups & Customization Rules
- [ ] **ModifierGroup Model:**
  - Name (e.g., "Meat Doneness", "Side Choice", "Extra Toppings").
  - Selection rules: `min_selections` (e.g., 1 for mandatory) and `max_selections` (e.g., 3).
  - Multi-select vs single-select invariant enforcement.
- [ ] **ModifierItem Model:**
  - Option name and price delta (zero for included, positive integer for paid additions).
  - Availability status and station routing notes.

### Phase 5.4: Menu Catalog REST APIs & EF Core Persistence
- [ ] **Persistence & Multi-Tenancy:**
  - PostgreSQL schema tables: `menus`, `menu_categories`, `menu_items`, `item_variants`, `modifier_groups`, `modifier_items`.
  - Composite foreign keys enforcing `tenant_id` consistency across all relations.
  - PostgreSQL Row-Level Security (`FORCE ROW LEVEL SECURITY`) with runtime isolation policies.
  - Additive, reversible EF Core migrations following expand-contract principles.
- [ ] **REST API Endpoints:**
  - Full CRUD under `/api/v1/menu` with RFC 7807 ProblemDetails and ETag / concurrency token guards.
  - RBAC enforcement: `menu.catalog.manage`, `menu.pricing.manage`, `menu.catalog.view`.
  - Append-only security audit log recording for catalog modifications.

### Phase 5.5: Real-Time Availability & Instant 86 Stockout Engine
- [ ] **Quick 86 APIs:**
  - One-tap out-of-stock toggle endpoint (`/api/v1/menu/items/{id}/stockout`).
  - Branch-level availability overrides without modifying parent brand catalog.
  - Permitted roles: Kitchen, Bar, BranchManager, RestaurantAdmin (`menu.inventory.quick86`).
- [ ] **Realtime Event Dispatch:**
  - Event payload `menu.item_86ed` emitted with `tenant_id`, `branch_id`, and `item_id`.
  - Cart checkout race condition prevention (rejecting orders containing 86ed items).

### Phase 5.6: Admin Catalog Management UI & Final Verification
- [ ] **Admin Web Catalog Management:**
  - Category list with drag-and-drop reordering.
  - Item editor modal with photo upload URL, variant matrix, allergen toggles, and modifier picker.
  - Quick 86 inventory toggle controls with instant visual feedback.
- [ ] **Verification & Quality Gates:**
  - Backend unit, domain, and architecture test suites passing.
  - Frontend Vitest suites passing with >= 80% coverage on all touched modules.
  - File size gate (< 600 strict line ceiling, < 450 warning threshold).
  - Integration suite and CI workflow green.

---

## 3. Verification Status (Phase 5.0 Baseline)

| Command | Scope | Result | Details |
| :--- | :--- | :--- | :--- |
| `dotnet build RestaurantOrder.sln` | Backend Solution | **PASS** | 0 Warnings, 0 Errors |
| `dotnet test tests/unit/` | Unit Test Suite | **PASS** | 1002 / 1002 passed (100%) |
| `dotnet test tests/architecture/` | Architecture Suite | **PASS** | 10 / 10 passed (100%) |
| `pnpm --filter admin-web test` | Admin Web Vitest | **PASS** | 77 / 77 passed across 7 test files (100%) |
| `pnpm test:unit:frontend` | Frontend Unit Suites | **PASS** | 259 / 259 passed across packages/ui and 3 web apps (100%) |
| `pnpm lint` | ESLint (TS / TSX) | **PASS** | 0 Warnings, 0 Errors |
| `pnpm typecheck` | TypeScript | **PASS** | 7 / 7 workspace projects clean |
| `node scripts/check-file-size.mjs` | File Size Gate | **PASS** | 0 files exceed 600 strict ceiling |
| `node scripts/check-docs.mjs` | Doc & Links Gate | **PASS** | Validated, 0 broken links |
| `git diff --check` | Whitespace & Formatting | **PASS** | 0 whitespace or formatting anomalies |

---

## 4. Known Risks & Mitigations

1. **Risk:** Floating-point rounding errors in variant pricing and modifier additions.
   **Mitigation:** All prices are strictly stored as integer minor currency units (cents/kuruş). Tax and service charge percentages use integer basis points (`BasisPointsRate`).
2. **Risk:** Stockout (86) race conditions between customer order submission and kitchen stockout toggle.
   **Mitigation:** Transactional pre-commit availability validation in order submission pipeline, failing fast with `ITEM_OUT_OF_STOCK` error code.
3. **Risk:** Stale catalog updates overwriting concurrent administrative modifications.
   **Mitigation:** Optimistic concurrency tokens and `If-Match` ETags on all catalog mutation endpoints.
4. **Risk:** Information disclosure via 500 error responses.
   **Mitigation:** Standard RFC 7807 `ProblemDetails` with correlation IDs returned; exception details and stack traces stripped in all production environments.
