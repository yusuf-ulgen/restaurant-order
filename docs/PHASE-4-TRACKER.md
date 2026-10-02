# Phase 4 Implementation Tracker (`docs/PHASE-4-TRACKER.md`)

This document tracks implementation progress across all 6 sub-phases of **Phase 4: Restaurant Configuration** in `restaurant-order`.

---

## 1. Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output | Commit SHA |
| :--- | :--- | :--- | :--- | :--- |
| **Phase 4.1** | Tenant-Scoped Brand & Branch Management | **Completed** | Brand & branch CRUD APIs, state transitions, concurrency tokens, ETags, RBAC | (Pending commit) |
| **Phase 4.2** | Secure Tenant Branding & Theme Settings | **Completed** | Secure brand appearance, branch theme overrides, inheritance, RLS, CSS token mapping | (Pending commit) |
| **Phase 4.3** | Configuration-Driven Dynamic Admin Shell | **Completed** | Dynamic Header, Sidebar, Footer, Navigation Registry, Branding Settings Screen, Theme Provider | (Pending commit) |
| **Phase 4.4** | Operating Hours & Weekly Schedules | **Not Started** | Day-of-week operating hours, shift windows, holiday overrides | - |
| **Phase 4.5** | Service Charges, Gratuity & Default Tips | **Not Started** | Percentage/fixed service fees, auto-gratuity rules, tip presets | - |
| **Phase 4.6** | Tax Rates, Tax Categories & Pricing Mode | **Not Started** | Tax rate definitions, tax inclusive/exclusive configurations | - |
| **Phase 4.7** | Branch Dining Areas & Station Definitions | **Not Started** | Physical areas (Terrace, Indoor), prep stations (Kitchen, Bar) | - |
| **Phase 4.8** | Admin Web Config Integration & Security Closure | **Not Started** | Admin panel configuration UI, audit verification, coverage gate | - |

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

### Phase 4.4: Operating Hours & Weekly Schedules
- [ ] Backend data models and migration for weekly operating hours.
- [ ] Special holiday and temporary closure schedule overrides.
- [ ] Active hours evaluation service (`IsOpenAt(DateTimeUtc)`).
- [ ] Unit and integration tests for schedule calculations and timezone offsets.

### Phase 4.5: Service Charges, Gratuity & Default Tips
- [ ] Branch service charge rate configuration entity and persistence.
- [ ] Minimum party size auto-gratuity threshold settings.
- [ ] Configurable tip suggestion presets (e.g., 10%, 15%, 20%).
- [ ] Currency and rounding validation unit tests.

### Phase 4.6: Tax Rates, Tax Categories & Pricing Mode
- [ ] Value-Added Tax (VAT) rate category entities (Standard, Reduced, Zero).
- [ ] Tax inclusive vs. exclusive pricing model setting per branch.
- [ ] Line item tax calculation contracts for menu pricing.
- [ ] Unit and integration tests.

### Phase 4.7: Branch Dining Areas & Station Definitions
- [ ] Dining area aggregate (Indoor, Terrace, Garden, Bar Area).
- [ ] Station routing definitions (Kitchen, Bar, Bakery, Service Station).
- [ ] Relationship mapping to branches and printer stations.
- [ ] Concurrency and RBAC validation.

### Phase 4.8: Admin Web Config Integration & Security Closure
- [ ] Admin Web brand management screens and modals.
- [ ] Admin Web branch settings screens (operating hours, taxes, fees).
- [ ] Operations Web and Customer Web config consumption.
- [ ] Security audit remediation, coverage closure (>= 80%), and final sign-off.

---

## 3. Verified Command Results (Phase 4.3)

| Command | Scope | Result | Details |
| :--- | :--- | :--- | :--- |
| `dotnet build RestaurantOrder.sln` | Backend Solution | **PASS** | 0 Warnings, 0 Errors |
| `dotnet test tests/unit/` | Unit Test Suite | **PASS** | 894 / 894 passed (100%) |
| `pnpm --filter admin-web test` | Admin Web Vitest | **PASS** | 37 / 37 passed across 3 test files (100%) |
| `pnpm test:unit:frontend` | All Frontend Suites | **PASS** | 219 / 219 passed across UI, admin, ops, customer |
| `pnpm lint` | ESLint (TS / TSX) | **PASS** | 0 Warnings, 0 Errors |
| `pnpm typecheck` | TypeScript | **PASS** | 7 / 7 workspace projects clean |
| `node scripts/check-file-size.mjs` | File Size Gate | **PASS** | 0 files exceed 600 strict ceiling |
| `node scripts/check-docs.mjs` | Doc & Links Gate | **PASS** | 52/52 docs validated, 0 broken links |
| `node scripts/check-secrets.mjs` | Security Scanner | **PASS** | 0 secrets or private keys exposed |

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
