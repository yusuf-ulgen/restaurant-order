# ADR-0010: Least-Privilege IAM Login Lookup & SECURITY DEFINER Threat Model

- **Status:** `ACCEPTED`
- **Date:** 2026-10-01
- **Deciders:** Architecture Team, Security Team, Backend Leads
- **Consulted:** [docs/SECURITY.md](../SECURITY.md), [docs/MULTI-TENANCY.md](../MULTI-TENANCY.md), [docs/adr/0009-authentication-and-session-strategy.md](./0009-authentication-and-session-strategy.md)
- **Supersedes:** N/A

---

## 1. Context & Problem Statement

In a multi-tenant platform, user accounts (`iam.users`) represent global identities that may belong to multiple tenants via memberships (`iam.memberships`).
During credential login, the application must locate a user by normalized email before any tenant context is established.

If the unprivileged runtime database role (`restaurant_app_runtime`) is granted direct blanket `SELECT` on `iam.users`:
1. Any SQL injection flaw anywhere in the multi-tenant application could allow dumping credentials and PII of all users across all tenants.
2. Compromised connection pools could execute cross-tenant account enumeration queries.
3. Violates the principle of least privilege and zero-trust data segregation.

We must define a secure, least-privilege mechanism to allow login verification without exposing the global user catalog to arbitrary SELECT queries.

---

## 2. Threat Model & Mitigations

### Threat 1: Global Identity Table Exfiltration (SQL Injection or Compromised Runtime Connection)
- **Risk:** Attacker executes `SELECT * FROM iam.users` and steals password hashes, salt metadata, and emails.
- **Mitigation:**
  - `REVOKE ALL ON iam.users FROM restaurant_app_runtime;`
  - Grant unprivileged runtime role ONLY column-level access needed for non-sensitive operations (e.g. `UPDATE failed_login_attempts, lockout_end_utc, concurrency_token`).
  - Table-wide `SELECT` on `iam.users` is strictly prohibited for `restaurant_app_runtime`.

### Threat 2: Search-Path Hijacking via SECURITY DEFINER
- **Risk:** In PostgreSQL, a `SECURITY DEFINER` function executes with the privileges of its owner. If `search_path` is not pinned, an attacker can create malicious objects in public or temporary schemas that hijack function execution.
- **Mitigation:**
  - The login lookup function `iam.lookup_user_for_login(p_normalized_email text)` is explicitly pinned with `SET search_path = iam, pg_temp`.
  - Dynamic SQL (`EXECUTE ...`) is strictly forbidden inside the function.
  - The function is owned by the schema migrator/DBA role, never by `restaurant_app_runtime`.

### Threat 3: Data Over-Exposure & PII Leakage
- **Risk:** Lookup function returns unnecessary columns (names, phone numbers, audit metadata).
- **Mitigation:**
  - The function returns strictly a fixed, minimal projection: `(user_id, normalized_email, password_hash, status, security_version, lockout_end_utc)`.
  - Zero PII is returned.

### Threat 4: User Enumeration & Cross-Tenant Account Discovery
- **Risk:** Malicious actor enumerates emails to discover platform accounts.
- **Mitigation:**
  - The application authentication handler returns constant-time generic error responses ("Invalid email or password") whether the user exists, is locked, or is invalid.
  - Password verification is performed using slow PBKDF2/Argon2 hashing even on non-existent users (dummy hash verification) to prevent timing discrepancy.
  - Even if a user exists globally, access to tenant data requires an active membership verified under PostgreSQL RLS `USING (tenant_id = tenancy.get_current_tenant_id())`.

---

## 3. Decision

1. **Dedicated Database Function:**
   Implement `iam.lookup_user_for_login(p_normalized_email text)` as a `SECURITY DEFINER` PostgreSQL function with pinned `search_path = iam, pg_temp` and static query projection.
2. **Access Control:**
   - `GRANT EXECUTE ON FUNCTION iam.lookup_user_for_login(text) TO restaurant_app_runtime;`
   - `REVOKE ALL ON iam.users FROM restaurant_app_runtime;`
   - `GRANT INSERT ON iam.users TO restaurant_app_runtime;` (for user registration/invitation creation)
   - `GRANT UPDATE (password_hash, status, security_version, failed_login_attempts, lockout_end_utc, updated_at_utc, concurrency_token) ON iam.users TO restaurant_app_runtime;`
3. **Application Gateway:**
   Create an `IIamUserLookupGateway` abstraction in the application/infrastructure layer that calls this specific function instead of using direct EF Core LINQ table queries on `iam.users`.

---

## 4. Consequences

### Positive
- Direct `SELECT * FROM iam.users` is physically blocked at the database engine level for application runtime connections.
- Clean separation between global authentication lookup and tenant-isolated operations.
- Pinned `search_path` eliminates privilege escalation attack vectors.

### Negative
- Requires maintaining the database function in schema migrations.
- Direct EF Core LINQ queries for email lookup on `iam.users` cannot be executed by `restaurant_app_runtime`; all lookups must go through `IIamUserLookupGateway`.
