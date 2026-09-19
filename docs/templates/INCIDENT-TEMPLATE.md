# INC-YYYYMMDD-XXX: [Incident Short Title] (`docs/templates/INCIDENT-TEMPLATE.md`)

- **Incident Date:** [YYYY-MM-DD]
- **Severity Level:** `[Sev-1 | Sev-2 | Sev-3 | Sev-4]`
- **Incident Commander:** [Name]
- **Status:** `[RESOLVED | MONITORING]`
- **Affected Services / Surfaces:** `[e.g., Kitchen KDS, Payment Processing, QR Menu]`

---

## 1. Executive Summary & Impact

- **Duration of Outage:** [X hours Y minutes]
- **Affected Tenants / Branches:** [List or count of affected organizations/branches]
- **Financial / Operational Impact:** [Estimated lost orders, delayed tickets, or failed transactions]

---

## 2. Chronological Timeline (UTC)

- `HH:MM` — Anomaly detected via automated alert or user report.
- `HH:MM` — Incident Commander declared Sev-X; war room initiated.
- `HH:MM` — Root cause identified in service/component X.
- `HH:MM` — Mitigation action executed (e.g., Blue-Green rollback).
- `HH:MM` — Health metrics stabilized; traffic confirmed healthy.
- `HH:MM` — Incident formally declared resolved.

---

## 3. Root Cause Analysis (5 Whys)

1. **Why did the failure occur?** [Direct cause]
2. **Why?** [Underlying technical factor]
3. **Why?** [Process or architectural gap]
4. **Why?** [Testing or verification gap]
5. **Why?** [Systemic organizational or design factor]

---

## 4. Post-Incident Review

### 4.1. What Went Well
- [Quick rollback execution, clear communication, etc.]

### 4.2. What Went Poorly
- [Alerting latency, unclear logging, etc.]

### 4.3. Where We Got Lucky
- [Happened during off-peak hours, etc.]

---

## 5. Preventative Action Items

| Action Item | Type | Owner | Due Date | Status |
| :--- | :--- | :--- | :--- | :--- |
| Add automated integration test for edge case | Test | [Name] | [YYYY-MM-DD] | `TODO` |
| Update health check readiness threshold | Config | [Name] | [YYYY-MM-DD] | `TODO` |
| Refine alert thresholds in monitoring | Ops | [Name] | [YYYY-MM-DD] | `TODO` |
