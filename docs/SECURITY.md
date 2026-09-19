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
