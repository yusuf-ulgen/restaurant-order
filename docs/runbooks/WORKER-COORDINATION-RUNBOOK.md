# Worker Coordination & Distributed Lease Runbook (`docs/runbooks/WORKER-COORDINATION-RUNBOOK.md`)

## 1. Overview & Architecture

In our Blue-Green deployment model, both Blue and Green worker containers run concurrently during release verification. To prevent duplicate execution of queue events, thermal receipt printing, and scheduled jobs:

1. **Central Active Slot State (`restaurant-order:active-slot`):** Stored in Redis. Only the slot matching this key is authorized to process work.
2. **Distributed Leadership Lease (`restaurant-order:lease:worker-leadership`):** Managed via `RedisWorkerLeaseManager` using atomic `SET NX + TTL` and Lua compare-and-expire/delete scripts.
3. **Idempotency Guard (`IIdempotencyStore`):** Even with lease protection, all background tasks enforce idempotency keys to guarantee at-most-once processing.

---

## 2. Split-Brain Symptoms & Detection

A split-brain condition occurs if both Blue and Green workers simultaneously process jobs.

### 2.1. Key Indicators
- **Duplicate Thermal Prints:** The same ticket/receipt is printed multiple times at kitchen or bar stations.
- **Concurrent Worker Active Logs:** Logs from both `restaurant-order-worker-blue` and `restaurant-order-worker-green` display `[WORKER ACTIVE]` simultaneously.
- **Multiple Lease Holders:** Inconsistent key ownership detected in Redis.

### 2.2. Inspection Commands
```bash
# Check current centralized active slot
redis-cli GET restaurant-order:active-slot

# Check which worker instance currently holds the leadership lease
redis-cli GET restaurant-order:lease:worker-leadership

# Check remaining TTL on the leadership lease (in seconds)
redis-cli TTL restaurant-order:lease:worker-leadership

# Inspect active Nginx upstream routing
cat deploy/nginx/conf.d/upstream.conf
```

---

## 3. Redis Outage & Fail-Closed Behavior

When Redis is partitioned or unreachable:
1. **Immediate Fail-Closed:** `RedisWorkerActivationGuard.IsActiveSlotAsync` returns `false`.
2. **Immediate Lease Loss:** `RedisWorkerLeaseManager.RenewLeaseAsync` returns `false`.
3. **Consumption Halt:** Worker loops immediately pause queue consumption, cron schedules, and thermal print spooling.
4. **Queue Buffering:** In-flight queue messages remain buffered in persistent storage until Redis recovers. No data is lost.

---

## 4. Manual Worker Emergency Halt

If an errant worker instance continues consuming jobs after cutover or during split-brain investigation:

### 4.1. Stop Specific Worker Container
```bash
# Stop green worker immediately
docker compose -f compose.yml -f compose.prod.green.yml stop worker

# Or stop blue worker immediately
docker compose -f compose.yml -f compose.prod.blue.yml stop worker
```

### 4.2. Force-Release Stale Distributed Lease
If a worker crashed without releasing its lease and you must reassign leadership immediately:
```bash
# Remove stale lease key so idle replica can acquire immediately
redis-cli DEL restaurant-order:lease:worker-leadership
```

---

## 5. Safe Recovery Workflow

Follow these steps to recover from worker coordination disruption:

1. **Verify Redis Health:**
   ```bash
   redis-cli PING
   # Expected: PONG
   ```

2. **Reconcile Central Active Slot with Ingress:**
   Check Nginx upstream port (5001 = blue, 5002 = green) and align Redis:
   ```bash
   # If Nginx routes to 5001 (Blue):
   redis-cli SET restaurant-order:active-slot blue

   # If Nginx routes to 5002 (Green):
   redis-cli SET restaurant-order:active-slot green
   ```

3. **Verify Worker Logs:**
   Ensure only the active slot logs `[WORKER ACTIVE]` and acquires the lease:
   ```bash
   docker logs --tail 50 -f restaurant-order-worker-blue
   docker logs --tail 50 -f restaurant-order-worker-green
   ```

---

## 6. Coordinated Worker Rollback Procedure

When an emergency rollback is initiated:

1. **Execute Rollback Command:**
   ```bash
   node scripts/blue-green/rollback.mjs --execute --confirm-rollback
   ```

2. **Automated Steps Executed:**
   - Ingress upstream reverts to safe slot (e.g., Blue on port 5001).
   - Validated via `nginx -t` and reloaded with `nginx -s reload`.
   - Redis key `restaurant-order:active-slot` is updated to restored slot (`blue`).
   - Failed slot worker notices state change and immediately releases its lease.
   - Restored slot worker detects active status, acquires lease, and resumes consumption.
   - Failed slot container remains online for forensic analysis.
