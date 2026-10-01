# ADR-0009: Authentication, Session & Multi-Tenant Authorization Strategy (`docs/adr/0009-authentication-and-session-strategy.md`)

- **Status:** `ACCEPTED`
- **Deciders:** Architecture Team, Yusuf Ülgen
- **Date:** 2026-10-01
- **Technical Story:** Phase 3 — Authentication & RBAC Foundation

---

## 1. Context and Problem Statement

`restaurant-order` is a multi-tenant restaurant management platform serving 5 user surfaces:
1. QR Customer Web App
2. Waiter & Operations Mobile App
3. Kitchen & Bar KDS Stations
4. Restaurant Admin Panel
5. Platform Super Admin Control Plane

The system requires an identity and access management (IAM) architecture that satisfies conflicting demands:
- High-security administrative access (Super Admin, Restaurant Admin, Branch Manager) requiring credential complexity, password hashing, and session auditing.
- Fast, high-frequency staff switching on the restaurant floor (Waiters, Cashiers, Line Cooks) requiring low-friction entry during busy service shifts.
- Ephemeral, anonymous guest dining sessions (Customer QR) tied exclusively to a specific table session.
- Strict multi-tenant isolation where tenant credentials and sessions can never cross organizational boundaries.
- Distributed token security without committing secrets or private keys to source control.

---

## 2. Decision Drivers

- **Zero-Secret Compliance:** JWT signing keys, refresh tokens, and passwords must never be stored in source control or printed in logs.
- **Fail-Closed Multi-Tenant Boundaries:** Cross-tenant token substitution or scope tampering must be impossible.
- **Defense-in-Depth:** Tokens and sessions must carry verifiable tenant/branch scopes, and the database must enforce PostgreSQL Row-Level Security (RLS) as the boundary of last resort.
- **Fast Floor Operations with High Security:** 4-digit PINs are convenient for waiters but dangerous on the public internet; they must be cryptographically bounded.
- **Immediate Revocation:** Role alterations or security version bumps must immediately invalidate active sessions.
- **Framework Independence & Clean Architecture:** Domain and Application layers must define contracts without coupling to external identity providers.

---

## 3. Decision Outcome

### 3.1. Separation of Identities: Staff Accounts vs. Ephemeral QR Sessions
- **Staff Accounts (Identity & Membership):** Registered users with global accounts linked to tenant and branch memberships. Authenticated via Email + Password (management) or Staff PIN (floor staff within trusted terminals).
- **Customer QR Sessions:** Completely separate anonymous principal type (`PrincipalType.Customer`). Bound strictly to a single `table_session_id`. Customer sessions cannot access staff endpoints. Full cryptographic QR session token generation belongs to Phase 6; in Phase 3, only the customer principal contract is established.

### 3.2. Authentication Methods & Credentials
1. **Email + Password:**
   - Primary login for Super Admin, Restaurant Admin, and Branch Managers.
   - Passwords hashed using battle-tested ASP.NET Core `PasswordHasher<T>` (PBKDF2 with HMAC-SHA512 / 100,000+ iterations). Custom cryptography is strictly forbidden.
2. **Staff 4-Digit PIN within Trusted Terminal Context:**
   - A 4-digit PIN alone is NEVER an internet-facing credential (vulnerable to trivial brute-force).
   - PIN authentication is strictly permitted **only** from an enrolled, cryptographically authenticated **Trusted Terminal** bound to a specific Branch.
   - PINs are hashed using salted slow hashing plus a server-side pepper injected from environment/secret manager. If the pepper is missing, production/staging fail-fast.

### 3.3. Token & Session Architecture
1. **Short-Lived JWT Access Tokens:**
   - 10-minute lifetime (environment-configurable).
   - Contains strictly validated claims: `sub`, `sid`, `jti`, `principal_type`, `role`, `tenant_id`, `branch_id`, `security_version`, `auth_method`, `iss`, `aud`, `iat`, `nbf`, `exp`.
   - Signing keys (`HMAC-SHA256` or `RSA-SHA256`) injected from environment/secret manager; absent in source control.
2. **Opaque Rotating Refresh Tokens:**
   - 32-byte cryptographically secure random opaque strings.
   - Raw refresh tokens are NEVER stored in the database. Only their SHA-256 hash is persisted.
   - Stored in `HttpOnly`, `Secure`, `SameSite=Strict` cookies (never `localStorage` or `sessionStorage`).
   - Every refresh operation rotates the token: the old token is marked revoked and replaced with a new one.
   - **Reuse Detection:** If an already-consumed refresh token is presented, the entire session family is instantly revoked as a suspected compromise.
3. **Session & Security Version Invalidation:**
   - Each user membership maintains a `security_version`.
   - Changing a user's role, password, or status increments `security_version`, causing immediate token rejection across all distributed nodes.

### 3.4. Multi-Tenant Role & Scope Hierarchies
The platform supports exactly 8 roles with strict scope constraints:
1. `SuperAdmin`: Platform scope only. Cannot carry `tenant_id` or `branch_id`.
2. `RestaurantAdmin`: Tenant scope. Bound to `tenant_id`; `branch_id` is null/optional.
3. `BranchManager`: Branch scope. Bound to `(tenant_id, branch_id)`.
4. `Cashier`: Branch scope. Bound to `(tenant_id, branch_id)`.
5. `Kitchen`: Branch scope. Bound to `(tenant_id, branch_id)`.
6. `Bar`: Branch scope. Bound to `(tenant_id, branch_id)`.
7. `Waiter`: Branch scope. Bound to `(tenant_id, branch_id)`.
8. `Customer`: Table session scope. Bound to `(tenant_id, branch_id, table_session_id)`.

Cross-tenant combinations (e.g. Tenant A token with Tenant B branch) are rejected fail-closed at creation and parsing.

### 3.5. Central Capability Registry (Deny-by-Default)
- Authorization checks are driven by machine-readable capability strings (e.g., `menu.catalog.manage`, `orders.staff.create`), NEVER ad-hoc `if (role == "Waiter")` checks.
- Roles map to permissions via `PermissionRegistry` based on `docs/ROLES-AND-PERMISSIONS.md`.
- **Own / Assigned Scope (`O`):** Permissions marked as own-scope (e.g., Waiter card payments, Cashier daily revenue, KDS station tickets) do NOT grant full access. They require evaluation by an explicit `IResourceOwnershipRequirement`.

---

## 4. Consequences & Trade-offs

### Positive Consequences
- **Elimination of Token Theft Vectors:** HttpOnly cookies prevent XSS theft; opaque hashed refresh tokens prevent database leakage from compromising sessions.
- **Floor Staff Usability:** Waiters can switch on trusted terminals with fast PINs without exposing the restaurant to internet-wide brute force.
- **Deterministic Multi-Tenancy:** Clear separation between platform, tenant, branch, and customer scopes prevents accidental cross-tenant data access.
- **Immediate Role Revocation:** `security_version` prevents stale JWT access tokens from continuing after role removal.

### Negative Consequences / Trade-offs
- **Stateful Invalidation Check:** Validating `security_version` requires cached session lookups on high-risk operations.
- **Terminal Management Overhead:** Branches must enroll terminals before staff can use PIN logins.

---

## 5. Verification & Test Plan

- **Matrix Coverage Unit Tests:** 100% of the 31 permissions across all 8 roles verified.
- **Negative Scope Tests:** SuperAdmin with tenant claim, Waiter without branch, and Tenant A/B cross-scoping must throw validation errors.
- **Claim Parser Tests:** Malformed GUIDs, missing claims, duplicate claims, and expired tokens must fail-closed.
- **Resource Ownership Tests:** Own-scope permissions must be rejected without valid ownership context.
