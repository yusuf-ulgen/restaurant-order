# Environments & Configuration Management (`docs/ENVIRONMENTS.md`)

## 1. Environment Topology

The `restaurant-order` platform maintains 5 strictly isolated environments:

```
[ LOCAL ] ─────────► [ TEST (CI) ] ─────────► [ DEVELOPMENT ]
(Dev Machine)         (Automated Gates)       (Shared Integration)
                                                       │
                                                       ▼
[ PRODUCTION (Blue / Green) ] ◄───────────────── [ STAGING ]
(Live Restaurant Traffic)                         (Production Mirror)
```

| Environment | Purpose | Database | External Integrations | Access Restrictions |
| :--- | :--- | :--- | :--- | :--- |
| **Local** | Developer feature building | Local Docker / SQLite / Postgres | Mocked gateways & printers | Developer only |
| **Test** | Automated CI unit & integration tests | Ephemeral test DB (reset per run) | Mocked gateways & simulated socket | CI runner only |
| **Development** | Internal team verification & previews | Dedicated Dev Postgres instance | Sandbox payment & test printers | Internal dev team |
| **Staging** | Pre-production testing, UAT, load tests| Mirrored staging DB (synthetic data)| Live sandbox payment gateways | Team & beta testers |
| **Production** | Live restaurant guest & staff traffic | Multi-AZ High-Availability Postgres | Live payment gateways & hardware | Public / Role-gated |

---

## 2. Configuration & Secrets Governance (12-Factor App)

- **Configuration via Environment:** All environment-specific behaviors (database URLs, log levels, payment keys) must be injected via environment variables.
- **Zero Hardcoded URLs/Credentials:** Hardcoded endpoints, IP addresses, or secrets are strictly forbidden.
- **Config Validation on Startup:** The application must validate all required environment variables at boot time and fail fast if any required key is missing or malformed.

### 2.1. Environment Variable Reference (Planned)

| Variable Name | Purpose | Example Value | Required In |
| :--- | :--- | :--- | :--- |
| `NODE_ENV` | Runtime environment | `production` / `development` | All |
| `DATABASE_URL` | PostgreSQL connection string | `postgresql://user:pass@host:5432/db` | All |
| `REDIS_URL` | Redis pub/sub and cache | `redis://default:pass@host:6379` | All |
| `JWT_SECRET` | Secret for auth signing | `[Secured in Secret Manager]` | All |
| `PAYMENT_GATEWAY_KEY`| Digital payment processor key | `[Secured in Secret Manager]` | Staging, Prod |
| `LOG_LEVEL` | Application logging verbosity | `info` / `debug` / `warn` | All |

---

## 3. Strict Cross-Environment Isolation Rules

1. **No Production Data Downstream:** Production database dumps must **never** be restored into `local`, `test`, or `development` environments without full anonymization.
2. **Network Isolation:** Lower environments cannot initiate network requests to production databases or payment processor live endpoints.
3. **Dedicated Encryption Keys:** Each environment must utilize distinct cryptographic keys and certificates.
