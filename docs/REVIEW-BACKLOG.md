# Repository Review Backlog

Review date: 2026-10-08. Baseline: `467737b` (Phase 5 merged).
Tracking issue: [#8](https://github.com/yusuf-ulgen/restaurant-order/issues/8).

This preserves the review suggestions. Confirmed defects, documentation mismatches, and proposed product decisions are distinct. Proposed items are not approved architecture or implemented features. Create a focused issue from an item when starting its implementation, and link its tests/ADR/PR here.

| ID | Priority / status | Finding and next action |
| :--- | :--- | :--- |
| R01 | P0 / Fixed on task branch; awaiting review | Equal splitting can produce negative debt (0.02 / 4). Distribute remaining cents one per guest, reject fractional-cent inputs, and test conservation/nonnegativity/fairness. See pricing regression tests. |
| R02 | P1 / Proposed; ADR required | Table status mixes occupancy, preparation, and settlement. Separate physical table, dining session, order-item preparation, and bill settlement; derive floor badges. Specify multiple rounds, partial serving, bill requests during preparation, empty-session closure, moves, and merges before Phase 6 implementation. |
| R03 | P1 / Documentation corrections on task branch; product decision deferred | State examples conflict with RBAC on customer cancellation, kitchen serving, and payment collection. Preserve existing grants as the current policy. A customer cancellation-request workflow is a separate proposed capability; it must not silently grant direct cancellation. |
| R04 | P1 / Proposed | Deliver one vertical dining workflow: open session, browse, order, prepare, serve, record cash/external-terminal payment, close. Reconcile customer Phase 7 dependencies on order Phase 8 and billing Phase 13/payment Phase 16 before revising roadmap milestones. Keep advanced payments, printing, tips, and reporting out of the first pilot milestone. |
| R05 | P1 / Proposed security design | Static QR signatures prevent tampering but not copying. Specify menu-view access versus current-session access, expiry/revocation, multi-guest permissions, and behavior after session close/move/merge. RBAC already requires customer tokens bound to table_session_id; define the issuance/join policy. |
| R06 | P1 / Open implementation gap | Contracts policy requires OpenAPI generation, but current TypeScript contracts are manually maintained and generation is not wired. Add reproducible generation and CI drift detection. Remove broad `literal union | string` declarations only with an explicit unknown-value policy and client tests. |
| R07 | P1 / Proposed reliability design | Redis IN_PROGRESS TTL reservations alone do not guarantee at-most-once external effects after crashes or lease expiry. Define durable tenant-scoped command/job keys, request hashes, ownership/fencing, provider idempotency, and reconciliation. Printing must expose uncertain outcomes and explicit reprints. Worker job execution is still planned; this is not evidence of duplicate production payments. |
| R08 | P1 / Open coverage gap | Current E2E tests are two HTTP health probes. Add a real browser dining workflow as features land, plus concurrent submissions, cancellation/preparation races, frozen order prices, partial payment balance, and stale-session rejection. |
| R09 | P1 / Proposed money integration | Catalog PriceAmount uses integer minor units; the legacy pricing helper accepts decimals and assumes two decimal places. Preserve its signature for this fix; define a shared money boundary, supported currency precision, conversion, tax rounding, and basis-point rates before integrating orders/billing. |
| R10 | P2 / Open developer-experience gap | Local setup needed private launch/proxy helpers. Checked-in frontend settings do not use VITE_API_URL as suggested; copying .env conflicts with the secret gate; integration auth fixtures assume a development key and Docker probes can time out under load. Provide a reproducible checked-in setup with secrets outside Git and self-contained test configuration. |
| R11 | P1 / Proposed settlement model | Documentation collapses refunds into one REFUNDED bill state despite partial refunds. Define immutable payment/refund records, cumulative refundable amounts, bill adjustments, remaining balance, and whether a refund affects a closed session. Also reconcile order Accepted/Paid/Closed states in ROADMAP with preparation states in STATE-MACHINES. |
| R12 | P2 / Workflow added on task branch | Existing delivery rules lacked an issue-first process and durable handoff. Add CONTRIBUTING-WORKFLOW, CURRENT-STATE, task template, and PR handoff fields; connect them to AGENTS.md. |

## Keep

Keep the modular monolith, current stack, PostgreSQL RLS and tenant constraints, nonprivileged runtime access, centralized RBAC, optimistic concurrency, transactional outbox, and existing automated test foundation. Improve the business contracts and one complete restaurant workflow before expanding deployment complexity.

## Recommended next decision

Choose a single-branch pilot and settle R02, R03, R05, R09, and R11 in a focused specification/ADR before implementing Phase 6. Do not treat the remainder of this backlog as an instruction to rewrite completed phases.
