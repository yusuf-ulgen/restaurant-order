# Blue-Green Deployment Runbook (`docs/BLUE-GREEN-RUNBOOK.md`)

## 1. Overview & Architecture

To guarantee 99.99% availability during peak lunch and dinner services, production releases use a **Blue-Green Deployment** model with two identical production slots:

```
[ Ingress / Nginx Reverse Proxy ]
            │
            ├────── (Active 100% Traffic) ─────► [ SLOT BLUE  (API: 5000, Web: 8080) ]
            │                                    (via container DNS: restaurant-order-*-blue)
            │
            └────── (Idle / Internal Smoke) ───► [ SLOT GREEN (API: 5000, Web: 8080) ]
                                                 (via container DNS: restaurant-order-*-green)
```

Both slots run **immutable container image digests**, validating all 5 individual component digests during preflight:
- `API_IMAGE_DIGEST`
- `WORKER_IMAGE_DIGEST`
- `CUSTOMER_WEB_IMAGE_DIGEST`
- `OPERATIONS_WEB_IMAGE_DIGEST`
- `ADMIN_WEB_IMAGE_DIGEST`

To prevent Docker Compose container or network collisions during simultaneous blue-green execution, independent Compose project names are strictly enforced:
- Blue Project: `restaurant-order-blue`
- Green Project: `restaurant-order-green`
- Ingress Network: `restaurant_order_ingress` bridge network providing internal DNS routing to containers.

---

## 2. Worker Safety & Central State Management

When Blue and Green containers run concurrently during deployment overlap, background workers must **not** duplicate work:

1. **Central Authoritative State in Redis:**
   - The central Redis key `restaurant-order:active-slot` serves as the single source of truth for the active slot.
   - `RedisWorkerActivationGuard` continuously queries Redis. If the slot matches, the worker enters `Active` state; otherwise, it stays in `Standby`.
   - In catastrophic Redis downtime during rollbacks, the `--emergency-override` flag permits operational traffic recovery by reverting Nginx routing to the previous slot. However, because Redis cannot be updated, this enters `CRITICAL_INCONSISTENT_STATE`: traffic is restored (`trafficRestored: true`), but Redis reconciliation is NOT verified (`redisReconciled: false`). Workers remain guarded in standby, `EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED` is logged to the deployment journal, and manual reconciliation is strictly required via the returned reconciliation command once Redis recovers.
2. **Distributed Lease Contract (`RedisWorkerLeaseManager`):**
   - Active workers acquire an exclusive distributed lease (`restaurant-order:lease:worker-leadership`) via atomic `SET NX PX` with periodic renewal.
   - If lease renewal fails or the slot is demoted, consumption immediately halts (fail-closed).
3. **Idempotency Requirement (`RedisIdempotencyStore`):**
   - All background jobs (order state transitions, billing updates, push notifications) enforce idempotency keys to guarantee at-most-once side effects across blue/green instances.
4. **Fail-Closed Behavior:**
   - In Staging and Production, if Redis is unreachable or credentials/slots are invalid, workers immediately fail closed (`Status = Error`) and refuse to process queues.

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
3. **Incident Declaration:** Follow [docs/INCIDENT-RESPONSE.md](./INCIDENT-RESPONSE.md).

### 4.2. Emergency Override & Manual Redis Reconciliation
When Redis is unreachable during a rollback, traffic reversion would normally fail closed to avoid split-brain. In catastrophic outages, the operator can force traffic restoration:

```bash
# Emergency rollback when Redis is unreachable (shifts Nginx traffic only)
node scripts/blue-green/rollback.mjs --execute --confirm-rollback --emergency-override
```

**Consequences & State Guarantees:**
- Nginx traffic is restored to the previous safe slot (`trafficRestored: true`).
- The operation returns `success: false` with status `CRITICAL_INCONSISTENT_STATE`.
- The state file is **NOT** updated with unverified slot data.
- `EMERGENCY_TRAFFIC_RESTORED_REDIS_UNVERIFIED` is written to `deploy/blue-green-state/journal.log`.
- Background workers remain guarded and refuse to process queues in unverified state.

**Mandatory Manual Reconciliation Step:**
Once Redis connectivity is restored, the operator **must** reconcile the central state immediately:
```bash
# Execute the reconciliation command output by rollback.mjs:
redis-cli -u $REDIS_URL SET restaurant-order:active-slot <restored_slot>
```
Verify the active slot value:
```bash
redis-cli -u $REDIS_URL GET restaurant-order:active-slot
```
