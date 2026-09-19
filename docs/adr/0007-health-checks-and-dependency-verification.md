# ADR-0007: Infrastructure Health Checks & Dependency Verification (`docs/adr/0007-health-checks-and-dependency-verification.md`)

- **Status:** `ACCEPTED`
- **Deciders:** Architecture Team, Yusuf Ülgen
- **Date:** 2026-09-19
- **Technical Story:** Production Liveness & Readiness Separation and Dependency Verification

---

## 1. Context and Problem Statement

A resilient containerized application requires clear separation between process liveness (is the application running) and traffic readiness (can the application serve traffic). 

Previously, `/health/ready` returned an unverified `Healthy` status without validating PostgreSQL or Redis connectivity. This created risks of routing customer traffic to unhealthy instances during database or cache outages. Additionally, health responses must never leak sensitive connection details, credentials, or host topologies.

---

## 2. Decision

We adopt a decoupled, fail-closed health check architecture:

1. **Liveness Probe (`/health/live`):**
   - Strictly monitors the internal ASP.NET Core process state.
   - Never depends on external infrastructure (PostgreSQL, Redis).
   - Always returns HTTP 200 OK as long as the HTTP pipeline functions.

2. **Readiness Probe (`/health/ready`):**
   - Validates live connectivity to PostgreSQL (via `NpgsqlDatabaseHealthCheck` executing `SELECT 1;`) and Redis (via `StackExchangeRedisHealthCheck` issuing a ping).
   - Enforces **fail-closed** behavior: in Staging and Production, any connection failure or missing configuration returns HTTP 503 Service Unavailable.
   - Enforces strict data masking: responses return generic status indicators (`Healthy` / `Unhealthy`) with zero exposure of connection strings, passwords, or host names.

3. **Package Selection & .NET 10 Compatibility:**
   - Core drivers `Npgsql` (v9.0+) and `StackExchange.Redis` (v2.8+) are used directly with timeouts (3s).
   - Avoids third-party community wrapper packages that may lag behind .NET 10 preview/release channels.

---

## 3. Consequences

### Positive
- Prevents Kubernetes / Docker reverse proxies from routing traffic to broken nodes.
- High availability during database restarts (containers are not killed by liveness probes, only taken out of rotation by readiness probes).
- Zero secret leakage in health responses or error logs.

### Negative / Trade-offs
- Readiness probes execute network calls (mitigated by 3-second timeouts and lightweight ping queries).
