# ADR-0012: Static/Dynamic QR Security, Cryptographic Signatures, and Customer Session Exchange

- **Status:** `ACCEPTED`
- **Date:** 2026-10-06
- **Deciders:** Architecture Team, Security Team, Backend Leads
- **Consulted:** [AGENTS.md](../../AGENTS.md), [docs/SECURITY.md](../SECURITY.md), [docs/MULTI-TENANCY.md](../MULTI-TENANCY.md), [docs/adr/0009-authentication-and-session-strategy.md](./0009-authentication-and-session-strategy.md)
- **Supersedes:** N/A

---

## 1. Context & Problem Statement

In `restaurant-order`, dining guests interact with restaurant tables by scanning printed or dynamic QR codes on their mobile devices.
If table QR codes simply embed raw URLs with predictable sequential IDs (e.g. `https://order.platform.local/branches/1/tables/5`):
1. **Enumeration & Impersonation:** Attackers can trivially enumerate tables and branches to place unauthorized orders, inspect bills, or flood dining sessions.
2. **Token Exposure:** Directly embedding customer JWT access tokens inside printed QR codes creates severe replay and credential theft vulnerabilities, as anyone photographing the physical table gains permanent access.
3. **Session Replay & Desynchronization:** When physical tables turn over or guests leave, previous QR scans must not grant access to newly seated guests' sessions.
4. **Denial of Service:** Publicly exposed scan endpoints are prime targets for automated scraping, token exhaustion, and denial of service.

We need a tamper-proof, versioned, cryptographically signed QR architecture with secure session exchange and strictly scoped customer principal delegation.

---

## 2. Threat Model & Mitigations

### Threat 1: Table & Tenant Enumeration (IDOR)
- **Risk:** Malicious users modify table/tenant IDs in URLs to hijack orders or spy on other tables.
- **Mitigation:**
  - Raw internal database IDs (`id`) are NEVER exposed in QR payloads or URLs.
  - Each table is assigned a 128-bit cryptographically secure opaque public identifier (`public_code`).
  - QR payloads are bound to `(tenant_id, branch_id, public_code)` and cryptographically signed.

### Threat 2: Payload Tampering & Forgery
- **Risk:** Attackers alter payload parameters (e.g., expiry, table ID, session ID) or forge arbitrary QR codes.
- **Mitigation:**
  - All QR payloads use a strict canonical format versioned as Schema v2:
    - Static: `v=2&kid={key_id}&mode=static&tid={tenant}&bid={branch}&tcode={public_code}&qv={qr_version}&iat={issued_at}`
    - Dynamic: `v=2&kid={key_id}&mode=dynamic&tid={tenant}&bid={branch}&tcode={public_code}&qv={qr_version}&iat={issued_at}&sid={session_id}&exp={exp_unix}&nonce={nonce}`
  - Payloads are signed with HMAC-SHA256 using a Base64-encoded signing key providing at least 32 decoded bytes (256 bits) of entropy.
  - Future-issued tokens beyond clock-skew tolerance (default 30s) are rejected immediately.
  - Oversized tokens exceeding `MaxTokenLength` (default 2048 chars) are rejected prior to decoding.
  - Signatures are verified using constant-time comparison (`CryptographicOperations.FixedTimeEquals`) to eliminate timing attacks.
  - Public error responses return generic `ProblemDetails` without leaking internal cryptographic exception messages.

### Threat 3: Static QR Physical Vandalism & Table Reassignment
- **Risk:** A printed QR code on a physical table is photographed or compromised, and needs to be revoked without re-registering the entire table.
- **Mitigation:**
  - Each table maintains a monotonic `qr_version` counter and regenerated `public_code`.
  - Calling the `/tables/{id}/qr/rotate` endpoint bumps `qr_version` and regenerates `public_code` using PostgreSQL CSPRNG (`gen_random_bytes(16)` via `pgcrypto`), immediately invalidating all previously printed physical QR codes.
  - Corrective database migrations enforce `NOT NULL` and `ck_restaurant_tables_public_code` check constraints, removing insecure defaults.

### Threat 4: Dynamic QR Replay After Session Close
- **Risk:** A guest or bystander captures a dynamic QR code for a bill/ordering session and uses it after settlement.
- **Mitigation:**
  - Dynamic QR codes have short-lived expiry (default 15 minutes) and are bound to a specific `table_session_id`.
  - Token validation strictly verifies that the session exists and is NOT closed (`status <> 4`).
  - Closed session dynamic QR exchanges fail-closed with HTTP 410 Gone / 400 Bad Request.

### Threat 5: Key Leakage & Key Lifecycle
- **Risk:** Signing keys leak into git repositories, logs, client bundles, or API responses.
- **Mitigation:**
  - Signing keys are loaded exclusively from secure environment variables (`QrSecurity:Keys:<key_id>`).
  - Keys must be Base64-encoded strings that decode to minimum 32 random bytes. String length is NOT accepted as entropy.
  - Malformed Base64 or keys decoding to < 32 bytes are rejected at startup.
  - Staging and production fail-fast during startup via `IValidateOptions<QrSecurityOptions>` and `ValidateOnStart()`.
  - The configuration supports multiple named keys (`key_id`) for zero-downtime key rotation.
  - Raw keys, raw tokens, signatures, and public table codes are strictly excluded from logging and telemetry; only SHA-256 fingerprints may be logged.

### Threat 6: Public Endpoint Abuse & Redis Failures
- **Risk:** Public resolve and exchange endpoints are flooded, exhausting server resources or database connections.
- **Mitigation:**
  - Distributed sliding-window rate limiting is enforced per client IP (default 30 requests/minute).
  - Rate limiting fail-closed: If Redis becomes unreachable, requests are rejected (HTTP 429 / 503) rather than silently failing open.
  - Rate-limit cache keys use SHA-256 fingerprints, never raw tokens.

---

## 3. Decision

1. **Cryptographic QR Payload & URL Structure:**
   - Web application URL schema: `https://{domain}/qr?token={url_safe_base64_payload}.{url_safe_base64_signature}`.
   - The token combines canonical UTF-8 JSON wire payload (Schema v2 with `iat`) and an HMAC-SHA256 signature separated by a period.
2. **QR Image Generation Library:**
   - Selected **`Net.Codecrete.QrCodeGenerator`** version `3.2.1` (pinned).
   - Rationale: High-performance, mature, 100% managed C# implementation with zero external native dependencies, published under MIT license, and native support for clean vector SVG output.
3. **Public API Contract:**
   - `GET /api/v1/qr/resolve?token={token}`: Validates signature and returns sanitized welcome metadata (`tenantName`, `branchName`, `tableNumber`, `mode`, `hasActiveSession`). Zero internal secrets or sensitive IDs returned.
   - `POST /api/v1/qr/exchange`: Validates signature and state, ensures or creates an active `DiningSession` atomically, and issues a Customer JWT access token.
4. **Customer Session Management:**
   - Issued customer token is stored in an `HttpOnly`, `Secure`, `SameSite=Lax` cookie (`restaurant_customer_session`).
   - Customer principal carries: `PrincipalType.Customer`, `role=Customer`, `tenant_id`, `branch_id`, and `table_session_id`.
   - Token validated on every request via `ICustomerSessionValidator` which checks `tenancy.dining_sessions.status <> 4`. When a dining session closes, customer access immediately terminates (fail-closed).
   - Customer principal is strictly forbidden from staff operations and foreign dining sessions.
5. **Race Condition Prevention on Static QR Exchange:**
   - Inside the verified tenant transaction, exchange executes `SELECT id FROM tenancy.restaurant_tables WHERE id = @tableId FOR UPDATE` to serialize concurrent requests deterministically.
   - If concurrent requests arrive simultaneously, they block on the row-level lock. The first creates the session, while subsequent requests observe the committed session and join it seamlessly.
   - Bounded retry loop (max 3 attempts): on unique constraint collision, the aborted transaction is cleanly rolled back (`tx.RollbackAsync`) and retried in a fresh transaction, preventing PostgreSQL `25P02` (transaction aborted) errors.
   - PostgreSQL partial unique index `ix_dining_sessions_tenant_branch_table_active` remains as database defense-in-depth.

---

## 4. Consequences

### Positive
- Fully tamper-proof QR tokens resistant to forged parameters, timing attacks, and IDOR enumeration.
- Instant table-level revocation via `BumpQrVersion` without touching other tables.
- Short-lived dynamic QR codes protect turnover and prevent post-checkout session hijack.
- True zero-downtime key rotation via `key_id`.
- Vector SVG rendering provides pixel-perfect print quality at any DPI.

### Negative / Trade-Offs
- Printed static QR codes must be re-printed if an admin triggers QR rotation for a table.
- Rate-limiting requires Redis in production; fail-closed behavior will block QR scanning if Redis is fully down.
