# Operations Runbooks Index (`docs/runbooks/README.md`)

## 1. Overview

Runbooks provide operational procedures, diagnostic checklists, and recovery steps for operating the `restaurant-order` platform in staging and production environments.

---

## 2. Core Runbooks

| Runbook | Purpose | Target Audience |
| :--- | :--- | :--- |
| [docs/BLUE-GREEN-RUNBOOK.md](../BLUE-GREEN-RUNBOOK.md) | Zero-downtime production deployment, health checks & traffic cutover. | DevOps / Release Leads |
| [docs/INCIDENT-RESPONSE.md](../INCIDENT-RESPONSE.md) | Incident severity classification, escalation paths & post-mortem workflows. | On-Call Engineers / Managers |
| [docs/runbooks/database-migrations.md](./database-migrations.md) | Zero-downtime Expand-Migrate-Contract database migrations and rollback workflows. | DBA / Release Leads |

---

## 3. Planned Operational Runbooks (Phase 1 & 2)

The following runbooks will be established as corresponding features are implemented:

1. **`RUNBOOK-001: Database Backup & Restore`**
   - Automated nightly backups, Point-in-Time Recovery (PITR) verification, and disaster recovery drills.
2. **`RUNBOOK-002: Branch Thermal Printer Troubleshooting`**
   - Diagnosing network printer connectivity, ESC/POS socket timeouts, and paper spooler queue recovery.
3. **`RUNBOOK-003: Tenant Onboarding & Domain Binding`**
   - Provisioning new restaurant organizations, setting up custom subdomains, and configuring initial branches.
4. **`RUNBOOK-004: Secret & API Key Rotation`**
   - Zero-downtime rotation of JWT secrets, database credentials, and payment gateway keys.

---

## 4. Runbook Authoring Standards

All runbooks in this directory must adhere to the following structure:
1. **Prerequisites & Permissions:** Required credentials, tools, and access levels.
2. **Step-by-Step Execution:** Numbered, copy-pasteable commands with expected outputs.
3. **Verification Step:** Explicit commands to verify successful completion.
4. **Failure & Rollback Procedure:** Immediate actions if any step fails.
