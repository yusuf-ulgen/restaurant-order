# Environments & Configuration Management (`docs/ENVIRONMENTS.md`)

## 1. Environment Topology

The `restaurant-order` platform maintains 6 strictly isolated operational environments:

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
| **Staging** | Pre-production testing, UAT, load tests | Mirrored staging DB (synthetic data)| Live sandbox payment gateways | Team & beta testers |
| **Production Blue** | Active/standby live slot (port 5001) | Multi-AZ High-Availability Postgres | Live payment gateways & hardware | Public / Role-gated |
| **Production Green**| Active/standby live slot (port 5002) | Multi-AZ High-Availability Postgres | Live payment gateways & hardware | Public / Role-gated |

---

## 2. Configuration & Secrets Governance (12-Factor App)

- **Configuration via Environment:** All environment-specific behaviors (database URLs, log levels, payment keys) must be injected via environment variables.
- **Zero Hardcoded URLs/Credentials:** Hardcoded endpoints, IP addresses, or secrets are strictly forbidden.
- **Fail-Fast Startup Validation:** In Staging and Production, `ConfigurationValidator` (API) and `WorkerConfigurationValidator` (Worker) inspect all required variables during startup. If any critical configuration is missing or malformed, the process terminates immediately with an informative fatal log (without leaking secret values).
- **Frontend Security Boundary:** Only environment variables prefixed with `VITE_` are bundled into client-side code. Server secrets, database credentials, and private signing keys must never be exposed to frontend apps (`apps/*-web`).

### 2.1. Environment Variable Contract Reference

| Variable Name | Purpose | Required In | Secret | Example / Default |
| :--- | :--- | :--- | :--- | :--- |
| `NODE_ENV` | Runtime environment mode | All | No | `development` / `production` |
| `ASPNETCORE_ENVIRONMENT` | ASP.NET Core hosting mode | All | No | `Development` / `Staging` / `Production` |
| `DEPLOYMENT_COLOR` | Container slot color (`blue` / `green`)| Staging, Prod | No | `blue` / `green` |
| `ACTIVE_DEPLOYMENT_SLOT`| Active slot authorized for traffic/work | Staging, Prod | No | `blue` / `green` |
| `DATABASE_URL` | PostgreSQL connection string | All | Yes (Prod) | `Host=localhost;Port=5432;...` |
| `REDIS_URL` | Redis host:port connection string | All | Yes (Prod) | `localhost:6379` |
| `JWT_SECRET` | Cryptographic symmetric key for JWTs | Staging, Prod | Yes | `[Secured in Secret Manager]` |
| `API_PORT` | Backend HTTP API listening port | Optional | No | `5000` |
| `API_PORT_BLUE` | Ingress port for Slot Blue API | Optional | No | `5001` |
| `API_PORT_GREEN` | Ingress port for Slot Green API | Optional | No | `5002` |
| `VITE_API_URL` | Public API endpoint for web clients | All Web Apps | No | `http://localhost:5000` |
| `IMAGE_DIGEST` | Immutable container image SHA256 digest | Staging, Prod | No | `sha256:...` |
| `LOG_LEVEL` | Application logging verbosity | Optional | No | `Information` |
| `Tenancy:AllowDevHeaderOverride` | Opt-in for X-Tenant-Id headers | Dev only | No | `false` |
| `BACKUP_VERIFIED` | Verified DB backup prerequisite for migrations | Staging, Prod | No | `false` |

---

## 3. Container Hardening & Network Isolation

All containerized workloads adhere to strict operational security guidelines:

1. **Non-Root Execution:**
   - API & Worker containers run under an unprivileged user (`appuser`, UID `10001`).
   - Web frontend containers run under unprivileged `nginx` (UID `101`).
2. **Minimal Writable Filesystem:**
   - Container root filesystems are mounted read-only (`read_only: true`).
   - Temporary file operations are restricted to memory-backed tmpfs (`/tmp`).
3. **Network Isolation:**
   - Services communicate over an internal bridge network (`app_internal`).
   - In Staging and Production, PostgreSQL and Redis containers do **not** expose public host ports.
4. **Production Image Purity:**
   - Multi-stage Docker builds ensure zero development compilers, SDKs, or development dependencies remain in final production images.

---

## 4. Strict Cross-Environment Isolation Rules

1. **No Production Data Downstream:** Production database dumps must **never** be restored into `local`, `test`, or `development` environments without full anonymization.
2. **Network Isolation:** Lower environments cannot initiate network requests to production databases or live payment processor endpoints.
3. **Dedicated Encryption Keys:** Each environment must utilize distinct cryptographic keys and certificates.
4. **Migration & Seeding Separation:** Automatic database migrations on web API startup are strictly prohibited in Staging and Production. Staging/production migrations are executed as an independent pre-cutover pipeline step using idempotent scripts. Synthetic seeding (`DevDataSeeder`) is strictly restricted to `Development` and throws fail-closed exceptions if executed in Staging or Production.
