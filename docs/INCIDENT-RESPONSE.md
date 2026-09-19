# Incident Response & Post-Mortem Process (`docs/INCIDENT-RESPONSE.md`)

## 1. Incident Classification & Severity Levels

| Severity | Definition & Impact | Target Response | Target Resolution |
| :--- | :--- | :--- | :--- |
| **Sev-1 (Critical)** | Total platform outage, cross-tenant data leakage, widespread order placement failure, or complete payment processing outage across all branches. | `< 15 Minutes` | `< 2 Hours` |
| **Sev-2 (Major)** | Major capability outage for a branch (e.g., KDS screens frozen, all printers offline, or all card payments failing in a location). | `< 30 Minutes` | `< 4 Hours` |
| **Sev-3 (Moderate)** | Non-blocking degradation (e.g., delayed push notifications, intermittent reporting export timeout, or single item 86 toggle failing). | `< 4 Hours` | `< 24 Hours` |
| **Sev-4 (Minor)** | Cosmetic defect, minor UI styling glitch, or non-functional typo in administrative settings. | `< 1 Business Day` | Next Sprint |

---

## 2. Incident Response Lifecycle

```
[ 1. DETECTION ] ──► [ 2. TRIAGE ] ──► [ 3. CONTAINMENT ] ──► [ 4. REMEDIATION ] ──► [ 5. POST-MORTEM ]
 (Alert / Report)     (Assign Sev)     (Rollback / Stop Bleed) (Fix & Verify)         (Blameless Review)
```

### 2.1. On-Call Roles & Responsibilities
- **Incident Commander (IC):** Drives the response, coordinates team actions, and owns escalation decisions.
- **Technical Lead / Operations:** Investigates root cause, runs diagnostics, and executes remediation or rollback.
- **Communications Lead:** Updates status pages and communicates directly with affected restaurant owners/managers.

### 2.2. Incident War Room & Escalation
- For **Sev-1** and **Sev-2** incidents, an emergency audio/chat bridge is opened immediately.
- If an active release caused the incident, the immediate default action is **Blue-Green Rollback** (see [docs/BLUE-GREEN-RUNBOOK.md](file:///d:/freelance/restaurant-order/docs/BLUE-GREEN-RUNBOOK.md)).

---

## 3. Blameless Post-Mortem Policy

Every Sev-1 and Sev-2 incident requires a formal, blameless post-mortem within **48 hours** of resolution:

1. **Focus on Systems, Not People:** The goal is to understand what systemic, architectural, or procedural gaps allowed the failure to occur.
2. **Timeline Analysis:** Detailed second-by-second chronology of events from initial detection to full recovery.
3. **Action Items:** Tracked, assigned tasks with deadlines to prevent recurrence (e.g., adding automated tests, improving alerts).
4. **Standard Template:** Use the standardized template in [docs/templates/INCIDENT-TEMPLATE.md](file:///d:/freelance/restaurant-order/docs/templates/INCIDENT-TEMPLATE.md).
