# Blue-Green Deployment Runbook (`docs/BLUE-GREEN-RUNBOOK.md`)

## 1. Overview & Architecture

To guarantee 99.99% availability during peak lunch and dinner services, production releases use a **Blue-Green Deployment** model with two identical production slots:

```
[ Ingress / Load Balancer ]
            │
            ├────── (Active 100% Traffic) ─────► [ SLOT BLUE (v1.2.0) ]
            │
            └────── (Idle / Internal Smoke) ───► [ SLOT GREEN (v1.3.0) ]
```

---

## 2. Step-by-Step Deployment Procedure

### Phase 1: Pre-Deployment Preparation
1. Verify the current active slot (e.g., `Blue` is serving production traffic).
2. Confirm the database is backward-compatible with the incoming version (see [docs/DELIVERY.md](file:///d:/freelance/restaurant-order/docs/DELIVERY.md)).
3. Check system health dashboards: ensure error rates are `< 0.05%` and DB connection pools are healthy.

### Phase 2: Deploy to Idle Slot (Green)
1. Deploy the release artifact to the idle slot (`Green`).
2. Run automated container health checks:
   - `GET /health/liveness` -> `200 OK`
   - `GET /health/readiness` -> `200 OK` (verifies DB, Redis, and socket listeners).

### Phase 3: Internal Smoke Testing on Idle Slot
Execute automated smoke tests against the private internal URL of the idle slot (`https://green.internal.restaurant-order`):
- [ ] Authenticate staff member with PIN.
- [ ] Create a test order and verify KDS ticket emission.
- [ ] Verify test ESC/POS print job spooling.
- [ ] Verify bill calculation and tax breakdown.

### Phase 4: Traffic Cutover
1. Update the load balancer / reverse proxy upstream pool to route 100% of production traffic to `Green`.
2. Confirm traffic shift on ingress metrics within 30 seconds.

### Phase 5: Post-Cutover Observation (15 Minutes)
- Monitor real-time error rates, WebSocket connection count, and payment gateway responses.
- If error rate exceeds `0.5%` or KDS tickets fail to load, initiate immediate rollback.

---

## 3. Immediate Rollback Runbook (< 60 Seconds)

If any critical failure occurs post-cutover:

1. **Switch Ingress Immediately:**
   Revert the load balancer upstream configuration back to `Blue` with one command.
2. **Verify Blue Traffic:**
   Confirm production requests are hitting `Blue` and returning `200 OK`.
3. **Isolate Green:**
   Keep `Green` alive for forensic log analysis and memory dump collection.
4. **Declare Incident:**
   Follow [docs/INCIDENT-RESPONSE.md](file:///d:/freelance/restaurant-order/docs/INCIDENT-RESPONSE.md) for Sev-1 / Sev-2 notifications.
