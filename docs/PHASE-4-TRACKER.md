# Phase 4 Implementation Tracker (`docs/PHASE-4-TRACKER.md`)

This document tracks implementation progress across all 6 sub-phases of **Phase 4: Restaurant Configuration** in `restaurant-order`.

---

## 1. Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output | Commit SHA |
| :--- | :--- | :--- | :--- | :--- |
| **Phase 4.1** | Tenant-Scoped Brand & Branch Management | **Completed** | Brand & branch CRUD APIs, state transitions, concurrency tokens, ETags, RBAC | 5547926 |
| **Phase 4.2** | Secure Tenant Branding & Theme Settings | **Completed** | Secure brand appearance, branch theme overrides, inheritance, RLS, CSS token mapping | 95e0ee2 |
| **Phase 4.3** | Configuration-Driven Dynamic Admin Shell | **Completed** | Dynamic Header, Sidebar, Footer, Navigation Registry, Branding Settings Screen, Theme Provider | 2f5f24c |
| **Phase 4.4** | Branch Operating Hours & Financial Configuration | **Completed** | Branch financial settings, weekly operating hours, basis points rates, RLS, RBAC | ad99ec3 |
| **Phase 4.5** | Branch Dining Areas, Preparation Stations & Feature Controls | **Completed** | Tenant-safe Dining Areas, Preparation Stations, Type-Safe Feature Flag Catalog & Admin UI | 7a1e45f |
| **Phase 4.6** | Final Hardening, Verification & Merge Readiness | **Completed** | Concurrency lifecycle hardening, migration bundle, negative flow tests; final push and pull-request CI passed | c77588c |

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

### Phase 4.2: Secure Tenant Branding & Theme Settings
- [x] **Domain Models & Value Objects:**
  - `ColorHex`: Validates short (`#RGB`) and standard (`#RRGGBB`) hex codes; normalizes to `#rrggbb`; rejects invalid lengths, invalid hex chars, named colors, and RGB/HSL functions.
  - `AssetUrl`: Validates safe relative paths (`/...`) without directory traversal (`..`, `\`, `//`) or secure HTTPS URLs (`https://...`); rejects `http:`, `data:`, `javascript:`, and file protocols.
  - `BrandAppearance`: Aggregate maintaining brand display name, logo/favicon references, controlled design tokens (primary, primaryHover, secondary, accent, surface, background), footer text, shell header/subtitle, and optimistic concurrency token. Strict anti-HTML validation on text.
  - `BranchThemeOverride`: Entity allowing branch-specific overrides (displayName, logoUrl, headerSubtitle, footerBranchInfo) with optimistic concurrency token.
- [x] **Theme Inheritance & Resolution:**
  - `EffectiveThemeDto`: Resolves theme values hierarchically—branch overrides take precedence if set, falling back seamlessly to brand appearance and system neutral defaults.
- [x] **RBAC Capabilities & Permissions:**
  - `tenant.branding.view`: View brand theme and effective branch theme (granted to all 8 roles).
  - `tenant.branding.manage`: Update brand appearance (RestaurantAdmin) or branch overrides (RestaurantAdmin full, BranchManager for own branch only).
  - Permission registry updated (`Permissions.All.Count == 33`) and document synchronization in `docs/ROLES-AND-PERMISSIONS.md`.
- [x] **Persistence & Multi-Tenancy:**
  - PostgreSQL tables `brand_appearances` and `branch_theme_overrides` with composite foreign keys (`(tenant_id, brand_id)` and `(tenant_id, branch_id)`).
  - PostgreSQL Row-Level Security (RLS) enabled and forced (`FORCE ROW LEVEL SECURITY`) with runtime isolation policies and least-privilege grants to `restaurant_app_runtime`.
  - EF Core Global Query Filter on `RestaurantOrderDbContext` enforcing `t.TenantId == CurrentTenantId`.
  - Additive, reversible migration: `20261002100000_AddBrandAppearanceAndBranchThemeOverrides`.
- [x] **Branding & Theme REST API:**
  - `GET /api/v1/restaurant-config/branding/brand/{brandId}`: Get brand theme with ETag.
  - `PUT /api/v1/restaurant-config/branding/brand/{brandId}`: Update brand appearance with concurrency check.
  - `GET /api/v1/restaurant-config/branding/branch/{branchId}/effective`: Get effective theme for customer/waiter apps.
  - `GET /api/v1/restaurant-config/branding/branch/{branchId}/override`: Get branch override.
  - `PUT /api/v1/restaurant-config/branding/branch/{branchId}/override`: Update branch override.
  - `DELETE /api/v1/restaurant-config/branding/branch/{branchId}/override`: Clear branch override.
  - Append-only security audit log recording: `BrandThemeUpdated`, `BranchThemeOverrideUpdated`, `BranchThemeOverrideCleared`.
- [x] **Frontend Shared Contracts & Token Integration:**
  - `EffectiveTenantTheme` contract in `packages/ui/src/tokens/types.ts`.
  - `mapEffectiveThemeToOverrides()` in `packages/ui/src/tokens/theme.ts` mapping effective theme into `applyTenantTheme()` CSS variables.
- [x] **Automated Tests:**
  - Value object & aggregate unit tests (`BrandingLifecycleUnitTests.cs`).
  - RBAC permission matrix unit tests updated (`PermissionRegistryMatrixTests.cs`).
  - Comprehensive integration tests (`RestaurantConfigBrandingIntegrationTests.cs`): default theme, brand update, branch override, clearing override, stale concurrency token, unauthorized roles, cross-branch modification prevention, unsafe input rejection.

### Phase 4.3: Configuration-Driven Dynamic Admin Shell
- [x] **Secure In-Code Navigation Registry:**
  - Static type-safe navigation registry (`NAVIGATION_REGISTRY`) covering 12 core areas: `dashboard`, `brand-settings`, `branch-settings`, `operating-hours`, `dining-areas`, `preparation-stations`, `feature-settings`, `staff`, `menu`, `tables`, `printers`, `reports`.
  - Real `href`, components, and required RBAC permissions (`requiredPermission`) defined strictly in code.
  - Database configuration can only override: `isVisible`, `order` (1-100), safe plain-text `labelOverride` (no HTML/script tags), and `disabled` status.
  - Feature flags / overrides cannot bypass RBAC checks (`hasNavigationAccess`). Unknown navigation IDs rejected safely.
- [x] **Dynamic Shell Provider (`AdminConfigProvider` & `useAdminConfig`):**
  - Fetches effective theme and branches after user authentication.
  - Applies design tokens (`applyTenantTheme`) to document root CSS variables (`--ro-color-*`).
  - Cleans up tokens and state on tenant change, branch change, and logout (`clearTenantTheme`).
  - Full loading spinner (`data-testid="admin-loading"`), error banner with retry (`data-testid="admin-error-retry"`), and safe fallback to default shell.
  - Branch switcher support (`selectedBranchId`, `selectBranch`) with dynamic header integration.
- [x] **Dynamic Admin Shell Components:**
  - **Header:** Brand/branch logo, dynamic shell title, branch name, role badge, branch switcher dropdown, mobile menu hamburger.
  - **Sidebar:** Navigation items driven by resolved and RBAC-filtered configuration, section grouping (`main`, `operations`, `settings`, `system`), active item indicator, desktop collapse toggle, mobile drawer dialog with focus trap.
  - **Footer:** Dynamic business text, branch info, platform copyright, verified external links, conditional visibility.
- [x] **Corporate Branding Settings Screen (`BrandingSettingsView`):**
  - Dedicated configuration screen for `RestaurantAdmin`.
  - Modular subcomponents under 450 lines limit (`LiveThemePreview`, `NavigationConfigTable`, `ThemeColorFields`, `ThemeBrandIdentityFields`, `ThemeTextHeaderFooterFields`).
  - Live preview modal (using `BottomSheet`), reset to current config, and revert branch override to brand appearance.
  - Form validation with friendly errors (e.g. hex colors, secure URLs) and 409 stale concurrency conflict messaging.
- [x] **Automated Tests:**
  - 15 comprehensive admin config shell unit & integration tests (`apps/admin-web/src/__tests__/admin-config-shell.test.tsx`): successful config loading, loading state, error & retry, safe default fallback, theme application & cleanup on logout, tenant/branch change, permission filtering, feature flag RBAC enforcement, unknown ID rejection, desktop sidebar collapse, mobile drawer, keyboard accessibility, 320px viewport overflow, save mutation, and 409 conflict handling.
  - Backend integration tests updated (`RestaurantConfigBrandingIntegrationTests.cs`) covering navigation config JSON persistence, branch inheritance, and unknown ID rejection.
  - All 37 admin-web tests, 894 backend unit tests, and 7 branding integration tests passing.

### Phase 4.4: Branch Operating Hours & Financial Configuration
- [x] **Branch Financial & Operational Settings Domain Model:**
  - `BranchSettings` aggregate root and `BranchSettingsId` strongly-typed identifier.
  - `BasisPointsRate` value object: integer basis points (0-10,000 bps for tax, 0-5,000 bps for service charge; zero floating point precision issues).
  - `SupportedLocales` value object: BCP-47 validation, uniqueness, default locale inclusion invariant.
  - Full operational fields: Timezone, Currency, Default locale, Supported locales, Tax inclusion toggle, Default tax rate, Service charge toggle & rate, Order taking toggle, Display name, Contact phone, email, address, and concurrency token.
- [x] **Weekly Operating Hours & Schedule Domain Model:**
  - `BranchOperatingHours` aggregate root and `BranchOperatingHoursId`.
  - `TimeSlot` value object: Wall-clock `TimeOnly` pairs, overnight interval detection (`CloseTime < OpenTime`), touching boundary support (`[08:00-14:00)` and `[14:00-22:00)`), intra-day and cross-day overnight spillover overlap detection.
  - `OperatingDaySchedule` & `WeeklySchedule`: Exactly 7 days, closed days invariant (must contain zero slots), wall-clock preserved in branch timezone without permanent UTC destruction.
- [x] **Database Schema, RLS & Tenant Isolation:**
  - PostgreSQL tables: `branch_settings` and `branch_operating_hours` with composite foreign keys to `branches(tenant_id, id)`.
  - PostgreSQL Row-Level Security (RLS) enabled and forced (`FORCE ROW LEVEL SECURITY`) with runtime isolation policies and least-privilege grants to `restaurant_app_runtime`.
  - EF Core Global Query Filter on `RestaurantOrderDbContext` enforcing `t.TenantId == CurrentTenantId`.
  - Additive, reversible migration: `20261002140000_AddBranchSettingsAndOperatingHours`.
- [x] **RBAC & Authorization Matrix:**
  - Canonical permissions: `branch.configuration.view` and `branch.configuration.manage`.
  - Matrix: RestaurantAdmin can manage all branches within their tenant; BranchManager can manage only their assigned branch; other roles (Cashier, Kitchen, Bar, Waiter, Customer) have read-only access to normalized read models; SuperAdmin denied.
- [x] **REST API Endpoints:**
  - `GET /api/v1/restaurant-config/branches/{branchId}/settings`: Get effective settings with ETag.
  - `PUT /api/v1/restaurant-config/branches/{branchId}/settings`: Update settings with concurrency validation and CSRF enforcement.
  - `GET /api/v1/restaurant-config/branches/{branchId}/operating-hours`: Get operating hours with ETag.
  - `PUT /api/v1/restaurant-config/branches/{branchId}/operating-hours`: Update weekly schedule with concurrency validation and CSRF enforcement.
  - Append-only security audit log recording: `BranchSettingsUpdated`, `BranchOperatingHoursUpdated`.
- [x] **Admin Web UI Components:**
  - `BranchSettingsView` tabbed configuration container for `branch-settings` and `operating-hours` navigation routes.
  - `BranchFinancialSettingsForm` and `BranchOperatingHoursForm` decomposed under 450 lines limit.
  - Unsaved changes badge, 409 concurrency conflict alert with reload button, mobile BottomSheet summary.
- [x] **Automated Tests:**
  - 18 domain unit tests (`BranchSettingsAndOperatingHoursUnitTests.cs`).
  - 290 permission matrix unit tests updated.
  - 11 frontend unit tests in `admin-web` (`branch-settings.test.tsx`).
  - 13 backend integration tests in `RestaurantConfigBranchSettingsIntegrationTests.cs` and `RestaurantConfigOperatingHoursIntegrationTests.cs`.
  - All 928 backend unit tests, 48 admin-web tests, and 230 frontend unit tests passing.

### Phase 4.5: Branch Dining Areas, Preparation Stations & Feature Controls
- [x] **Branch Dining Area Domain Model:**
  - `DiningArea` aggregate root and strongly-typed `DiningAreaId`.
  - Fields: Id, TenantId, BranchId, Name, Code/slug, AreaType (`Indoor`, `Terrace`, `Garden`, `BarArea`, `Other`), SortOrder, IsActive, CreatedAtUtc, UpdatedAtUtc, ConcurrencyToken.
  - Soft lifecycle (`Activate()`, `Deactivate()`) with idempotency; no hard delete.
  - No tables or table layouts in this phase (strictly Phase 6 scope).
- [x] **Branch Preparation Station Domain Model:**
  - `PreparationStation` aggregate root and strongly-typed `PreparationStationId`.
  - Fields: Id, TenantId, BranchId, Code (unique within branch, lowercase normalized), DisplayName, StationType (`Kitchen`, `Bar`, `Other`), SortOrder, IsActive, ConcurrencyToken.
  - Soft lifecycle (`Activate()`, `Deactivate()`) with idempotency.
  - No printer IP, ESC/POS or routing in this phase (Phase 11-12 scope).
- [x] **Type-Safe Feature Flag System:**
  - Supported catalog keys: `CustomerQrOrdering`, `CustomerServiceRequests`, `Tips`, `SplitBilling`, `OnlinePayments`, `KitchenDisplay`, `BarDisplay`, `Reservations`, `KioskMode`.
  - Strict domain validation rejects unknown or arbitrary string keys.
  - Safe defaults: unimplemented features (financial, KDS, reservations, kiosk) default to disabled.
  - Hierarchical resolution: Tenant defaults + Branch overrides -> Effective calculated configuration.
  - Clear architectural boundaries: feature flags cannot bypass RBAC, tenant isolation, or expose hidden endpoints.
- [x] **Persistence, Schema & Multi-Tenancy:**
  - PostgreSQL tables: `dining_areas`, `preparation_stations`, `tenant_feature_flags`, `branch_feature_flags` with composite foreign keys to `branches(tenant_id, id)`.
  - PostgreSQL RLS enabled and forced (`FORCE ROW LEVEL SECURITY`) with runtime isolation policies and least-privilege grants to `restaurant_app_runtime`.
  - Additive, reversible migration: `20261003140048_AddDiningAreasStationsAndFeatureFlags`.
- [x] **REST API Endpoints:**
  - Dining Areas: List, Create, Update, Reorder, Activate, Deactivate (`/api/v1/restaurant-config/branches/{branchId}/dining-areas`).
  - Preparation Stations: List, Create, Update, Reorder, Activate, Deactivate, and Kitchen/Bar runtime read model (`/stations/runtime`).
  - Feature Flags: Tenant defaults (`GET/PUT /api/v1/restaurant-config/tenant/features`), Branch overrides (`GET/PUT/DELETE /api/v1/restaurant-config/branches/{branchId}/features/override`), Effective (`GET /api/v1/restaurant-config/branches/{branchId}/features/effective`).
  - Append-only security audit log recording for all mutations.
- [x] **Admin Web UI Screens:**
  - `DiningAreasView.tsx` with list, add, edit, reorder with version/token verification, activate/deactivate, and mobile `BottomSheet`.
  - `PreparationStationsView.tsx` with list, add, edit, reorder with version/token verification, activate/deactivate, and mobile `BottomSheet`.
  - `FeatureFlagsView.tsx` with Effective Flags, Branch Overrides, and Tenant Defaults (RestaurantAdmin-only) tabs with mobile `BottomSheet`.
  - Navigation registry updated (`dining-areas`, `preparation-stations`, `feature-settings` enabled for RestaurantAdmin & BranchManager).
- [x] **Automated Tests:**
  - Domain unit tests (`DiningAreasAndStationsUnitTests.cs`).
  - RBAC permission matrix unit tests updated (`RestaurantConfigRbacMatrixUnitTests.cs`).
  - Integration tests (`RestaurantConfigDiningAreasAndStationsIntegrationTests.cs` and `RestaurantConfigConcurrencyIntegrationTests.cs`).
  - Frontend unit tests (`dining-areas-and-stations.test.tsx` with 10 tests).
  - All 973 backend unit tests, 10 architecture tests, and 58 admin-web vitest tests (240 total frontend tests) passing.

### Phase 4.6: Final Hardening, Verification & Merge Readiness
- [x] **Comprehensive Code & Architecture Review:**
  - Tenant & branch isolation verified across all configuration tables (`brand_appearances`, `branch_settings`, `dining_areas`, `preparation_stations`, `branch_feature_flags`).
  - Composite foreign keys referencing `(tenant_id, brand_id)` and `(tenant_id, branch_id)` physically prevent cross-tenant assignment.
  - PostgreSQL Row-Level Security (RLS) policies and EF Core global query filters fully aligned and enforced with `FORCE ROW LEVEL SECURITY`.
  - Full RBAC matrix enforced across 8 roles with deny-by-default behavior and RFC 7807 ProblemDetails responses.
  - Concurrency token / ETag verification prevents silent lost updates on all entities (initial create validates parent token; subsequent updates validate aggregate token; missing token yields 412, stale yields 409).
  - Dynamic shell input sanitization strictly rejects arbitrary CSS, expressions, external script injection, and unknown routes.
  - Feature flags decoupled from authorization; permissions strictly required regardless of feature toggle state.
  - Expand-contract migration rules verified non-destructive with idempotent SQL bundle validation (`deploy/migrations/latest_bundle.sql`).
  - Zero secrets or PII detected in code, git history, or application logs.
- [x] **Negative Flow Verification:**
  - Cross-tenant configuration tampering prevented.
  - Cross-branch mutation by BranchManager rejected with 403 Forbidden.
  - Unauthorized roles denied configuration writes.
  - Fail-closed behavior on missing or unresolvable tenant context.
  - Terminal closed branch immutability enforced.
  - Atomic database transactions ensure zero partial or corrupted state on failures.
  - Frontend admin shell gracefully falls back to default tokens on network/API failure.
- [x] **Documentation & Roadmap Closure:**
  - `ROADMAP.md` updated: Phase 4 marked `COMPLETED`, Phase 5 marked `NEXT`.
  - `DOMAIN.md`, `ARCHITECTURE.md`, `MULTI-TENANCY.md`, `DESIGN-SYSTEM.md`, `ROLES-AND-PERMISSIONS.md`, `SCREEN-INVENTORY.md`, `NEGATIVE-FLOWS.md`, and `PHASE-4-TRACKER.md` synchronized with active implementation.

---

## 3. Final Verification Status (Phase 4.6)

Final verification completed on 2026-10-04 for `c77588c0f842ae2191e8b97871097eb07882ff99`. Both GitHub Actions runs passed completely: [push run 37203196434](https://github.com/yusuf-ulgen/restaurant-order/actions/runs/37203196434) and [pull-request run 37203198903](https://github.com/yusuf-ulgen/restaurant-order/actions/runs/37203198903). The CI integration suite passed **201/201 tests**; CI backend unit tests passed **1001/1001**, architecture tests passed, frontend tests and coverage passed, and all build, Docker, security, and repository gates passed.

The coverage failure on `3aeee15` was caused by admin-web function coverage at 66.66%, below the 80% threshold. Added behavior-focused admin tests raised local admin-web function coverage to 82.99%; the final CI coverage gate passed on both workflows.

## 3. Earlier Verified Command Results (Phase 4.6)

| Command | Scope | Result | Details |
| :--- | :--- | :--- | :--- |
| `dotnet build RestaurantOrder.sln` | Backend Solution | **PASS** | 0 Warnings, 0 Errors |
| `dotnet test tests/unit/` | Unit Test Suite | **PASS** | 973 / 973 passed (100%) |
| `dotnet test tests/architecture/` | Architecture Suite | **PASS** | 10 / 10 passed (100%) |
| `pnpm --filter admin-web test` | Admin Web Vitest | **PASS** | 58 / 58 passed across 5 test files (100%) |
| `pnpm test:unit:frontend` | Frontend Unit Suites | **PASS** | 240 / 240 tests passed across packages/ui and 3 web apps (100%) |
| `pnpm lint` | ESLint (TS / TSX) | **PASS** | 0 Warnings, 0 Errors |
| `pnpm typecheck` | TypeScript | **PASS** | 7 / 7 workspace projects clean |
| `node scripts/check-file-size.mjs` | File Size Gate | **PASS** | 0 files exceed 600 strict ceiling |
| `node scripts/check-docs.mjs` | Doc & Links Gate | **PASS** | 52/52 docs validated, 0 broken links |
| `node scripts/check-secrets.mjs` | Security Scanner | **PASS** | 0 secrets or private keys exposed |
| `pnpm verify:gates` | Quality Gates (local environment) | **NOT PASS** | Docker daemon was unavailable for the disposable blue-green container flow; the full CI gate passed on both final Actions runs. |
| `node scripts/migration-ops.mjs validate` | Migration Rules | **PASS** | 34 / 34 migrations non-destructive |
| `node scripts/migration-ops.mjs script` | Migration Bundle | **PASS** | `deploy/migrations/latest_bundle.sql` generated |

---

## 4. Known Risks & Mitigations

1. **Risk:** BranchManager attempting to modify branch settings in multi-branch organizations.
   **Mitigation:** `tenant.branches.manage` is granted strictly to `RestaurantAdmin`. BranchManager is denied this capability and endpoints reject unauthorized callers with RFC 7807 `403 Forbidden`.
2. **Risk:** Accidental modification of permanently closed branches.
   **Mitigation:** Domain invariant in `Branch.EnsureNotClosed()` enforces immutability on terminal `Closed` status, verified by unit and integration tests.
3. **Risk:** Stale updates overwriting concurrent administrative edits.
   **Mitigation:** Optimistic concurrency tokens checked on every mutation. Missing token yields `412 Precondition Failed`; mismatched token yields `409 Conflict`.
4. **Risk:** Malicious or malformed CSS/HTML injection via branding fields.
   **Mitigation:** Controlled design tokens only (hex colors strictly validated via `ColorHex`; URLs strictly validated via `AssetUrl`; text stripped of HTML tags via domain value objects). No arbitrary CSS or HTML/JS accepted or rendered.
5. **Risk:** Cross-tenant branding data leakage.
   **Mitigation:** PostgreSQL Row-Level Security (RLS) forced on `brand_appearances` and `branch_theme_overrides`, EF Core global tenant filters, and ambient verified tenant context resolution.

---

## 5. Pending Architecture Decisions

- None for Phase 4.2. Core domain models (`Brand`, `Branch`) and PostgreSQL RLS from Phase 2 reused and extended without breaking changes.
