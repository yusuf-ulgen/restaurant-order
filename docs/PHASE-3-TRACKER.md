# Phase 3 Implementation Tracker (`docs/PHASE-3-TRACKER.md`)

This document tracks implementation progress across all 6 sub-phases of **Phase 3: Authentication & RBAC** in `restaurant-order`.

---

## Sub-Phase Status Overview

| Sub-Phase | Title | Status | Primary Output |
| :--- | :--- | :--- | :--- |
| **Phase 3.1** | IAM Architecture & Authorization Contracts | **COMPLETED** | ADR-0009, 8 Roles, Scope Models, Permission Registry, JWT Claim Models, Unit Tests |
| **Phase 3.2** | IAM Persistence, Sessions & Tenant Isolation | **COMPLETED** | EF Core IAM entities, PostgreSQL `iam` schema, RLS policies, audit logs, Hasher contracts |
| **Phase 3.3** | Password Authentication, JWT & Refresh Session Flow | **COMPLETED** | Login, refresh, logout endpoints, HttpOnly cookies, token rotation, reuse detection |
| **Phase 3.4** | Central Authorization, RBAC & Authenticated Tenant Context | **COMPLETED** | ASP.NET Core authorization handler, `RequirePermission`, resource ownership enforcement |
| **Phase 3.5** | Secure Staff PIN & Trusted Terminal Authentication | **COMPLETED** | Enrolled trusted terminal model, 4-digit peppered PIN login, brute-force backoff & lockout |
| **Phase 3.6** | Staff Identity Management & Frontend Authentication Integration | **COMPLETED** | Staff invitation, role assignment, Admin & Operations Web auth integration |
| **Phase 3.7** | Authentication & RBAC Security Hardening and Final Closure | **COMPLETED** | Security verification, distributed auth fixes, coverage closure, audit remediation |

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
- [x] User/Account entity (`iam.users`)
- [x] Tenant/Branch Membership entity (`iam.memberships`)
- [x] Auth Session entity (`iam.sessions`)
- [x] Refresh Token state with family tracking (`iam.refresh_tokens`)
- [x] Trusted Terminal entity (`iam.trusted_terminals`)
- [x] Append-Only Security Audit Log (`iam.audit_events`)
- [x] PostgreSQL Row-Level Security for `iam.*` tables
- [x] Password and peppered PIN hasher contracts

### Phase 3.3: Password Authentication, JWT & Refresh Session Flow
- [x] `POST /api/v1/auth/login` (Email + Password)
- [x] `POST /api/v1/auth/refresh` (Rotating refresh tokens)
- [x] `POST /api/v1/auth/logout` & `POST /api/v1/auth/logout-all`
- [x] `GET /api/v1/auth/session` & `GET /api/v1/auth/sessions`
- [x] HttpOnly, Secure, SameSite=Strict cookies
- [x] Token reuse detection and session family revocation

### Phase 3.4: Central Authorization, RBAC & Authenticated Tenant Context
- [x] `PermissionRequirement` & `PermissionAuthorizationHandler`
- [x] `[RequirePermission(...)]` attribute and policy provider
- [x] Authenticated tenant context resolver (from verified JWT claims only)
- [x] Resource ownership and station assignment validators

### Phase 3.5: Secure Staff PIN & Trusted Terminal Authentication
- [x] Terminal enrollment code generation and activation
- [x] 4-digit PIN authentication with server-side pepper and slow hashing
- [x] Terminal-scoped brute-force backoff and lockout protection
- [x] Terminal revocation cascade

### Phase 3.6: Staff Identity Management & Frontend Integration
- [x] Staff invitation, activation, suspension API
- [x] Admin Web login, protected routes, session restoration
- [x] Operations Web terminal activation and PIN entry bottom sheet
- [x] Single-flight refresh token queue in frontend HTTP client

### Phase 3.7: Security Hardening & Final Verification
- [x] Full RBAC matrix verification (all 31 permissions × 8 roles)
- [x] Denial-by-default and fail-closed authorization verification
- [x] RFC 7807 ProblemDetails compliance on 401 Unauthorized and 403 Forbidden
- [x] Password hasher and peppered PIN hasher security properties verified
- [x] Terminal progressive delay and brute-force lockout verified
- [x] Single-flight concurrent token refresh queue verified
- [x] Deterministic migration lifecycle and pre-auth bootstrap SECURITY DEFINER gateway
- [x] Tenant transaction fail-closed boundary and connection pool context isolation verified
- [x] Platform session and refresh token persistence in dedicated global tables (`iam.platform_sessions`, `iam.platform_refresh_tokens`), fully isolating tenant data
- [x] Distributed atomic token rotation using PostgreSQL row locks (`FOR UPDATE`) with immediate family revocation on reuse race
- [x] Distributed ephemeral auth state in Redis (login rate limiting Lua scripts, progressive terminal PIN backoff/lockout, single-use enrollment `GETDEL`)
- [x] Fail-closed Redis architecture: returns HTTP 503 rather than bypassing security gates if Redis is unavailable
- [x] Multi-instance distributed integration test suite (`PostgreSqlDistributedAuthIntegrationTests`) passing across simulated API instances
- [x] Backend combined test coverage >= 80% threshold closure (Line: 94.16%, Branch: 83.02%, Method: 96.00%)
- [x] Frontend test coverage >= 80% threshold closure across all apps and packages
- [x] Platform refresh token reuse SQL column fix and verification
- [x] Session revoke IDOR vulnerability closure and tenant/platform ownership integration tests
- [x] Atomic account lockout counter in PostgreSQL
- [x] PIN rate-limit email/userId key consistency across distributed instances
- [x] JWT session-user binding verification in iam.validate_token_session (forward migration)
- [x] Platform session least-privilege direct table access revocation
- [x] Zero files exceeding 600 lines strict ceiling
- [x] Zero secrets and clean documentation integrity verified



