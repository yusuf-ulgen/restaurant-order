## Description

<!-- Provide a concise summary of the change and the problem it addresses. -->

---

## Type of Change

- [ ] `feat`: New feature or user capability
- [ ] `fix`: Bug fix or defect resolution
- [ ] `docs`: Documentation addition or update
- [ ] `refactor`: Code refactoring without behavior changes
- [ ] `ci`: CI/CD workflow or quality gate update
- [ ] `chore`: Dependency updates or build tooling

---

## Architectural & Governance Checks

- [ ] **Architecture Decision Record (ADR):**
  - [ ] This change requires an ADR (new framework, state change, storage, or protocol).
  - [ ] ADR is created under `docs/adr/` in `PROPOSED` or `ACCEPTED` status.
  - [ ] No ADR required for this change.
- [ ] **File Size Limits:**
  - [ ] All human-authored files are under 450 lines (or strictly < 600 lines).
  - [ ] Any file exceeding 600 lines is documented in `scripts/file-size-allowlist.json`.
- [ ] **Zero Secrets & PII:**
  - [ ] No API keys, credentials, JWT secrets, or real customer data are committed or logged.
- [ ] **Zero-Unverified-PASS Policy:**
  - [ ] All automated tests have been executed and verified in the environment.

---

## Test Evidence

<!-- Paste command output proving that all tests and quality checks passed. -->

```bash
# Example: pnpm verify / dotnet test
```

---

## Documentation Synchronization

- [ ] Relevant documentation under `docs/` updated in sync with this change.
- [ ] Relative markdown links verified with `node scripts/check-docs.mjs`.

---

## Database & Migration Impact

- [ ] **No database impact.**
- [ ] **Database changes included:**
  - [ ] Migration follows the **Expand and Contract** zero-downtime pattern (see `docs/DELIVERY.md`).
  - [ ] Indexes are created concurrently (`CREATE INDEX CONCURRENTLY`).
  - [ ] Multi-tenant `tenant_id` and `branch_id` filters are enforced via RLS.
