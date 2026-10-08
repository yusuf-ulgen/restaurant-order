# Current State & Handoff

Updated: 2026-10-08. Refresh this file from Git/GitHub when resuming; historical phase trackers are linked rather than overwritten.

## Product baseline

- Main baseline: `467737b`, merged Phase 5 menu/catalog work.
- Phases 0-5 are recorded complete; Phase 6 tables/sessions is next in [ROADMAP.md](./ROADMAP.md).
- See [PHASE-5-TRACKER.md](./PHASE-5-TRACKER.md) for the completed phase and [REVIEW-BACKLOG.md](./REVIEW-BACKLOG.md) for review findings and proposed decisions.

## Active task

- Issue: [#8 — Fix equal bill splitting and document contributor handoffs](https://github.com/yusuf-ulgen/restaurant-order/issues/8).
- Branch: `fix/pricing-and-contributor-handoff`.
- Status: implemented and locally verified; awaiting PR peer review; not merged or deployed.
- PR: to be linked after creation; discover by branch if resuming before this note is refreshed.
- Scope: fix equal splitting, align contradictory permission examples with current RBAC, save review suggestions, and formalize issue/PR/handoff workflow.
- Exclusions: no new grants, schema changes, payment gateway, contract generator, or implementation of the proposed table/session redesign.

## Verification and environment

- Original defect reproduced by running PricingService: 0.02 / 4 returned 0.01, 0.01, 0.01, -0.01.
- Regression evidence: all 5 new tests failed on the original implementation; all 36 targeted pricing tests passed after the fix. One invariant test exercises 50,000 total/guest combinations.
- Full `pnpm verify` passed on 2026-10-08: 1,228 backend unit tests, 10 architecture tests, 295 frontend tests, 249 integration tests, 115 gate tests, and 2 HTTP E2E health probes (1,899 total). Lint, type checks, file/link/secret gates, and production builds passed. Backend Release build: zero warnings/errors.
- Test process used local infrastructure settings, no custom JWT_SECRET, and `DOTNET_PROCESSOR_COUNT=2` to constrain integration concurrency. Initial targeted build hit a running local API executable lock; stopping the task-owned API/worker resolved it.
- Existing nonblocking warnings: one migration exceeds the 450-line warning threshold; operations/admin bundles exceed Vite's 500 kB advisory. No tests were skipped in the reported suites.
- Local prerequisites: .NET 10, Node with pinned pnpm 11.10.0, PostgreSQL 16, Redis 7, and Docker for integration fixtures.
- This machine uses ignored `.local` launch helpers and external secret storage. Those helpers are not a portable setup contract; R10 tracks the reproducibility gap.
- Integration auth fixtures currently expect the repository development JWT key: unset a custom JWT_SECRET for the test process while retaining local infrastructure settings. Constrained test parallelism may be needed for Docker probes. Never unset production settings or commit local secrets.
- The existing E2E suite contains health probes only; full browser workflows remain R08.

## Next action

Review the task PR and its CI results; do not self-merge. After merge, refresh this handoff and settle the proposed Phase 6 business contracts before starting implementation. Follow [CONTRIBUTING-WORKFLOW.md](./CONTRIBUTING-WORKFLOW.md).
