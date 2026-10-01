# Security & Compliance Policy (`docs/SECURITY.md`)

## 1. Zero-Secrets Policy

The `restaurant-order` repository operates under an absolute zero-secrets policy:

- **Forbidden in Code & Git:** Database passwords, JWT signing keys, payment gateway API keys, webhook signing secrets, encryption keys, and third-party credentials must **never** be committed to source control.
- **Log Sanitation:** Sensitive credentials, authorization headers, credit card data, and customer PII must be scrubbed before writing to stdout or log aggregation systems.
- **Automated Scanning:** Pre-commit hooks and CI security linters will scan for potential secrets and reject any commit containing suspicious tokens.

---

## 2. PCI-DSS & Payment Security Boundaries

To minimize PCI-DSS compliance scope, `restaurant-order` enforces the following boundaries:

1. **Zero Raw Card Data Storage:** The platform **never** receives, processes, or stores raw Primary Account Numbers (PAN), expiration dates, or CVV/CVC security codes.
2. **Tokenized Processing:** All digital payments use client-side tokenization or iframe/redirect hosted fields provided by certified PCI-DSS Level 1 payment processors (e.g., Stripe, Iyzico).
3. **External Card POS:** Standalone physical POS terminals used by waitstaff handle card authorization externally; only the transaction authorization code and amount are recorded.

---

## 3. OWASP Top 10 Mitigations

| Vulnerability Category | Platform Mitigation Strategy |
| :--- | :--- |
| **A01: Broken Access Control** | Centralized RBAC enforcement middleware. Every query is filtered by `tenant_id` via PostgreSQL Row-Level Security (RLS). Prevents Insecure Direct Object References (IDOR). |
| **A02: Cryptographic Failures** | TLS 1.3 enforced for all client-server and inter-service communication. Sensitive customer data encrypted at rest with AES-256. |
| **A03: Injection** | Strict use of parameterized SQL queries and typed query builders. Zero raw string concatenation in SQL queries. |
| **A04: Insecure Design** | Rate limiting on QR order endpoints and staff PIN login attempts to prevent brute-force attacks. |
| **A05: Security Misconfiguration** | Hardened security headers (HSTS, CSP, X-Content-Type-Options, X-Frame-Options). Permissive CORS strictly prohibited. |
| **A06: Vulnerable Components** | Automated dependency vulnerability audits (`npm audit`, Dependabot). |
| **A07: Identification & Auth Failures** | Short-lived JWTs, secure HttpOnly cookies, and mandatory password complexity for administrative roles. |
| **A08: Software & Data Integrity** | Signed releases, validated webhook signatures from payment providers. |
| **A09: Security Logging Failures** | Immutable audit logging for sensitive actions (price overrides, voids, refunds, role changes). |
| **A10: Server-Side Request Forgery (SSRF)**| Outbound webhooks and printer socket connections restricted to validated IP ranges. |

---

## 4. Immutable Audit Trail

All high-impact actions generate an immutable audit log record in the database:
- User role assignment or privilege changes
- Price overrides, bill discounts, and payment refunds
- Order cancellations on items already in preparation
- Tenant configuration modifications

Audit log entries cannot be modified or deleted by any application role, including `Restoran Admini`.

---

## 5. IAM & Authentication Architecture (Phase 3 Implementation)

Phase 3 implements comprehensive authentication and authorization hardening across all product surfaces:

### 5.1. Password Security
- **Password Hasher:** ASP.NET Core `IPasswordHasher<T>` utilizing PBKDF2 with HMAC-SHA512 and configurable production iteration count (100,000 iterations default).
- **Constant-Time Verification:** Password verification and hash upgrade checking via `CryptographicOperations.FixedTimeEquals` to mitigate timing attacks.
- **Account Protection:** Consecutive failed login attempt tracking, exponential backoff, and automatic account lockout after 5 failed attempts.

### 5.2. Fast Staff PIN on Trusted Terminals
- **No Standalone PIN:** 4-digit PIN is strictly prohibited from authenticating from arbitrary or public clients. PIN entry is only valid from an enrolled `TrustedTerminal`.
- **Peppered PIN Hashing:** Two-layer security: server-side pepper via HMAC-SHA256 (`PIN_PEPPER_SECRET`) followed by PBKDF2 slow hashing. Even a database dump cannot brute-force 4-digit PINs without the environment pepper.
- **Terminal Rate Limiting & Lockout:** Terminal-scoped rate limiting with progressive delay (1s after 3 failures, 2s after 4 failures) and 15-minute lockout after 5 consecutive failed attempts.
- **Trusted Terminal Enrollment:** Two-step enrollment using high-entropy single-use pairing codes. Terminals receive a device secret whose SHA-256 digest is stored at rest. Terminal revocation immediately cascades to all staff sessions on that device.

### 5.3. Token Architecture & Refresh Session Lifecycle
- **Short-Lived Access Tokens:** Signed JWT access tokens with 15-minute expiration, containing verified tenant and role claims.
- **Opaque Refresh Tokens:** 256-bit cryptographically secure random tokens stored only as SHA-256 hashes in `iam.refresh_tokens`.
- **HttpOnly Cookies:** Refresh tokens are transmitted exclusively in `HttpOnly`, `Secure`, `SameSite=Strict` cookies.
- **Rotation & Reuse Detection:** Every refresh rotates the token. If an already-consumed token is presented (replay attack), the entire session family is instantly revoked.
- **Frontend Single-Flight Queue:** Browser client coordinates simultaneous 401 responses into a single refresh request (`SingleFlightRefreshQueue`), preventing concurrent refresh collisions.

### 5.4. Fail-Closed RBAC & Tenant Isolation
- **Deny-by-Default:** Centralized `PermissionAuthorizationHandler` enforcing 31 machine-readable capabilities across 8 roles. Unregistered permissions or unmapped scopes result in strict 403 Forbidden with RFC 7807 ProblemDetails.
- **PostgreSQL RLS:** Defense-in-depth isolation: all `iam.*` tables enforce Row-Level Security policies tied to `app.current_tenant_id`.

### 5.5. Distributed State & Multi-Instance Security
- **Platform Session Isolation:** Platform-level SuperAdmin sessions and refresh tokens reside in dedicated PostgreSQL global tables (`iam.platform_sessions`, `iam.platform_refresh_tokens`). `Guid.Empty` is never injected into tenant-scoped tables.
- **Atomic Rotation with PostgreSQL Locks:** Multi-instance refresh token rotation uses explicit PostgreSQL row-level locks (`SELECT ... FOR UPDATE`), eliminating race conditions across distributed API instances.
- **Distributed Redis Rate Limiting & Fail-Closed Gate:** Login rate limiting (atomic Lua scripts) and terminal PIN progressive delays/lockouts are coordinated across instances via Redis. If Redis becomes unreachable, security policies fail closed: requests are rejected with HTTP 503 (Service Unavailable) rather than bypassing protection.
- **Immediate Distributed Revocation:** JWT access token validation in `OnTokenValidated` queries `iam.validate_token_session` with short-lived Redis caching. Immediate cache invalidation on `LogoutAll` revokes tokens across all instances simultaneously without waiting for expiration.

### 5.6. Production Hardening & Fail-Closed Transport Security
- **End-to-End CSRF Architecture:** Double-submit cookie with timing-safe HMAC validation via centralized `fetchWithCsrf` client. Strict validation on mutation methods (`POST`/`PUT`/`PATCH`/`DELETE`). Client cookies cleared upon logout.
- **Fail-Closed CORS Governance:** In Staging and Production, `Cors:AllowedOrigins` (or `CORS_ALLOWED_ORIGINS`) is mandatory. Wildcard `*` and `localhost` origins trigger fail-fast startup termination.
- **Reverse Proxy & Forwarded Headers:** Configured `ForwardedHeadersOptions` with trusted private CIDRs (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`) and configurable proxy IPs ensure accurate `Request.IsHttps` and client IP resolution behind TLS-terminating proxies.
- **Strict Cookie Security:** `Secure` flag is unconditionally enforced on all authentication and terminal credential cookies in Staging and Production.
- **Unmapped Test Scaffolding in Production:** Test endpoints such as `/api/v1/test/tenant-scope` and `/api/v1/dev/seed` are strictly unmapped in Staging and Production environments.

