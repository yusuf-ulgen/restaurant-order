# Phase 3 Implementation Tracker (`docs/PHASE-3-TRACKER.md`)

This document tracks implementation progress across all 6 sub-phases of **Phase 3: Authentication & RBAC** in `restaurant-order`.

---

## Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output |
| :--- | :--- | :--- | :--- |
| **Phase 3.1** | IAM Architecture & Authorization Contracts | **COMPLETED** | ADR-0009, 8 Roles, Scope Models, Permission Registry, JWT Claim Models, Unit Tests |
| **Phase 3.2** | IAM Persistence, Sessions & Tenant Isolation | Planned / Pending | EF Core IAM entities, PostgreSQL `iam` schema, RLS policies, audit logs, Hasher contracts |
| **Phase 3.3** | Password Authentication, JWT & Refresh Session Flow | Planned / Pending | Login, refresh, logout endpoints, HttpOnly cookies, token rotation, reuse detection |
| **Phase 3.4** | Central Authorization, RBAC & Authenticated Tenant Context | Planned / Pending | ASP.NET Core authorization handler, `RequirePermission`, resource ownership enforcement |
| **Phase 3.5** | Secure Staff PIN & Trusted Terminal Authentication | Planned / Pending | Enrolled trusted terminal model, 4-digit peppered PIN login, brute-force backoff & lockout |
| **Phase 3.6** | Staff Identity Management & Frontend Authentication Integration | Planned / Pending | Staff invitation, role assignment, Admin & Operations Web auth integration |

---

## Detailed Milestone Checklist

### Phase 3.1: IAM Architecture & Authorization Contracts
- [x] **ADR-0009 Accepted:** [docs/adr/0009-authentication-and-session-strategy.md](adr/0009-authentication-and-session-strategy.md)
- [x] **8 Roles Defined:** `SuperAdmin`, `RestaurantAdmin`, `BranchManager`, `Cashier`, `Kitchen`, `Bar`, `Waiter`, `Customer`
- [x] **Principal & Scope Contracts:** `PrincipalType`, `AuthenticationMethod`, `AuthorizationScopeType`, `AuthorizationScope`, `AuthenticatedPrincipal`
- [x] **Permission Registry:** Machine-readable constants for all 31 permissions in [docs/ROLES-AND-PERMISSIONS.md](ROLES-AND-PERMISSIONS.md) with 100% matrix coverage
- [x] **Deny-by-Default Enforcement:** Unknown roles, unknown permissions, and missing scopes strictly denied
- [x] **Resource Ownership Abstraction:** `IResourceOwnershipRequirement` preventing automatic full access for `OwnOrAssigned` (`O`) permissions
- [x] **JWT Claim Contracts:** `JwtClaimNames`, `JwtClaimModel`, and `JwtClaimPrincipalParser` with fail-closed validation
- [x] **Matrix & Scope Tests:** Unit tests covering all 31 permissions across all 8 roles and negative scope/claim scenarios

### Phase 3.2: IAM Persistence, Sessions & Tenant Isolation
- [ ] User/Account entity (`iam.users`)
- [ ] Tenant/Branch Membership entity (`iam.memberships`)
- [ ] Auth Session entity (`iam.sessions`)
- [ ] Refresh Token state with family tracking (`iam.refresh_tokens`)
- [ ] Trusted Terminal entity (`iam.trusted_terminals`)
- [ ] Append-Only Security Audit Log (`iam.audit_events`)
- [ ] PostgreSQL Row-Level Security for `iam.*` tables
- [ ] Password and peppered PIN hasher contracts

### Phase 3.3: Password Authentication, JWT & Refresh Session Flow
- [ ] `POST /api/v1/auth/login` (Email + Password)
- [ ] `POST /api/v1/auth/refresh` (Rotating refresh tokens)
- [ ] `POST /api/v1/auth/logout` & `POST /api/v1/auth/logout-all`
- [ ] `GET /api/v1/auth/session` & `GET /api/v1/auth/sessions`
- [ ] HttpOnly, Secure, SameSite=Strict cookies
- [ ] Token reuse detection and session family revocation

### Phase 3.4: Central Authorization, RBAC & Authenticated Tenant Context
- [ ] `PermissionRequirement` & `PermissionAuthorizationHandler`
- [ ] `[RequirePermission(...)]` attribute and policy provider
- [ ] Authenticated tenant context resolver (from verified JWT claims only)
- [ ] Resource ownership and station assignment validators

### Phase 3.5: Secure Staff PIN & Trusted Terminal Authentication
- [ ] Terminal enrollment code generation and activation
- [ ] 4-digit PIN authentication with server-side pepper and slow hashing
- [ ] Terminal-scoped brute-force backoff and lockout protection
- [ ] Terminal revocation cascade

### Phase 3.6: Staff Identity Management & Frontend Integration
- [ ] Staff invitation, activation, suspension API
- [ ] Admin Web login, protected routes, session restoration
- [ ] Operations Web terminal activation and PIN entry bottom sheet
- [ ] Single-flight refresh token queue in frontend HTTP client
