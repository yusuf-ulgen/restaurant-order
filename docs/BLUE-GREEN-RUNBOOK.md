# Blue-Green Deployment Runbook (`docs/BLUE-GREEN-RUNBOOK.md`)

## 1. Overview & Architecture

To guarantee 99.99% availability during peak lunch and dinner services, production releases use a **Blue-Green Deployment** model with two identical production slots:

```
[ Ingress / Load Balancer ]
            │
            ├────── (Active 100% Traffic) ─────► [ SLOT BLUE  (v1.2.0 - Port 5001) ]
            │
            └────── (Idle / Internal Smoke) ───► [ SLOT GREEN (v1.3.0 - Port 5002) ]
```

Both slots run the **exact same immutable container image digest** (`IMAGE_DIGEST`), isolating state via environment variables.

---

## 2. Worker Safety & Concurrent Execution Guard

When Blue and Green containers run concurrently during deployment overlap, background workers must **not** duplicate work:

1. **Activation Guard (`IWorkerActivationGuard`):**
   - Each worker instance receives its `DEPLOYMENT_COLOR` and the current `ACTIVE_DEPLOYMENT_SLOT`.
   - If `DEPLOYMENT_COLOR == ACTIVE_DEPLOYMENT_SLOT`, the worker enters `Active` state and processes queues.
   - If `DEPLOYMENT_COLOR != ACTIVE_DEPLOYMENT_SLOT`, the worker enters `Standby` state: queue consumption, cron schedules, and thermal print spools are paused.
2. **Fail-Closed Behavior:**
   - In Staging and Production, if `DEPLOYMENT_COLOR` or `ACTIVE_DEPLOYMENT_SLOT` is unconfigured or invalid, the worker immediately fails closed (`Status = Error`) and refuses to execute work.
3. **Distributed Lease Contract (`IWorkerLeaseManager`):**
   - Dynamic lease management across worker replicas requires an external distributed lock provider (`[Requires Distributed Lease Provider: Redis Redlock or Postgres Advisory Lock]`).
   - If lease renewal fails, the worker fails closed immediately.
4. **Idempotency Requirement:**
   - All background jobs (order state transitions, billing updates, push notifications) must enforce idempotency keys to guarantee at-most-once side effects.

---

## 3. Automated Blue-Green Operational Commands

All operational commands run in **dry-run mode by default**. No live traffic is shifted without explicit operator confirmation flags (`--confirm-cutover`, `--execute`).

### 3.1. Complete Pipeline (Orchestrator)

```bash
# Safe dry-run check (Recommended before any deployment)
pnpm blue-green:check
# Or directly:
node scripts/blue-green/orchestrator.mjs --dry-run

# Live production execution (Requires operator confirmation)
node scripts/blue-green/orchestrator.mjs --execute --confirm-cutover
```

### 3.2. Individual Command Boundaries

| Stage | Command | Description |
| :--- | :--- | :--- |
| **1. Preflight** | `node scripts/blue-green/preflight.mjs` | Validates slots, compose files, and image digest |
| **2. Config Validate**| `node scripts/blue-green/config-validate.mjs` | Validates environment contracts and security rules |
| **3. Migration Check**| `node scripts/blue-green/migration-check.mjs` | Verifies Expand-Migrate-Contract compliance |
| **4. Deploy Inactive**| `node scripts/blue-green/deploy-inactive.mjs` | Launches idle slot containers (dry-run default) |
| **5. Health Check** | `node scripts/blue-green/health-check.mjs` | Probes `/health/live` and `/health/ready` |
| **6. Warmup** | `node scripts/blue-green/warmup.mjs` | Exercises endpoints to warm JIT & connection pools |
| **7. Smoke** | `node scripts/blue-green/smoke.mjs` | Runs non-mutating smoke tests on idle slot |
| **8. Cutover** | `node scripts/blue-green/cutover.mjs --confirm-cutover` | Shifts live traffic upstream to new slot |
| **9. Observe** | `node scripts/blue-green/observe.mjs` | Monitors post-cutover metrics (< 0.05% error rate) |
| **10. Drain Old** | `node scripts/blue-green/drain-old.mjs` | Drains connections and stops retired slot |

---

## 4. Immediate Emergency Rollback (< 60 Seconds)

If post-cutover observation reveals errors exceeding `0.05%` or KDS disruption:

```bash
# Safe dry-run rollback plan
node scripts/blue-green/rollback.mjs --dry-run

# Live emergency rollback
node scripts/blue-green/rollback.mjs --execute --confirm-rollback
```

### 4.1. Rollback Invariants
1. **Immediate Ingress Switch:** Reverts traffic back to the previous safe slot within 60 seconds.
2. **Forensic Preservation:** The failed slot container is **retained in isolated mode** for memory dump extraction and log analysis.
3. **Incident Declaration:** Follow [docs/INCIDENT-RESPONSE.md](file:///d:/freelance/restaurant-order/docs/INCIDENT-RESPONSE.md).
