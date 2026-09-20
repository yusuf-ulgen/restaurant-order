# FEAT-XXX: [Feature Name] (`docs/templates/FEATURE-TEMPLATE.md`)

- **Status:** `[DRAFT | IN_REVIEW | APPROVED | IN_DEV | VERIFIED | RELEASED]`
- **Author:** [Author Name / Agent]
- **Target Surfaces:** `[QR Customer | Waiter Mobile | Kitchen/Bar KDS | Restaurant Admin | Super Admin]`
- **Target Roles:** `[Super Admin | Restoran Admini | Şube Müdürü | Operasyon/Kasa | Mutfak | Bar | Garson | Müşteri]`

---

## 1. Summary & User Value

[Brief description of the feature and the problem it solves for restaurant operations or guests.]

---

## 2. User Stories & Acceptance Criteria

### User Story 1: [Title]
> As a `[Role]`, I want to `[action]` so that `[benefit]`.

**Acceptance Criteria:**
- [ ] Criterion 1: Happy path behavior.
- [ ] Criterion 2: Error handling / negative path.
- [ ] Criterion 3: RBAC permission enforcement.

---

## 3. Domain Model & Schema Changes

- **New / Modified Entities:** [List entities affected in `docs/DOMAIN.md`]
- **Database Migrations:** [Describe expand-and-contract schema alterations]
- **Multi-Tenancy:** [Confirm `tenant_id` and `branch_id` indexing and RLS policy]

---

## 4. State Machine & Interaction Impacts

- [Identify affected state machines in `docs/STATE-MACHINES.md`]
- [Detail any new states, transitions, or guards]

---

## 5. Negative Flows & Edge Cases

- [Edge Case 1: e.g., Concurrent modifications]
- [Edge Case 2: e.g., Hardware or network disconnection]
- [User-facing error messages and recovery steps]

---

## 6. Testing & Verification Plan

- [ ] **Unit Tests:** [Domain logic, calculations, state transitions]
- [ ] **Integration Tests:** [API endpoints, DB queries, RLS isolation]
- [ ] **E2E / Manual Verification:** [Cross-surface workflow steps]

> **Reminder:** In accordance with [AGENTS.md](../../AGENTS.md), this feature must not be reported as PASS without executing and validating automated tests.
