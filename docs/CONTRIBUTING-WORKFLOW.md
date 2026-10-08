# Contributor & Agent Workflow

This workflow applies to humans and AI agents. [AGENTS.md](../AGENTS.md) remains the binding rule source; [DELIVERY.md](./DELIVERY.md) governs branches, verification, and releases.

## Start or resume

1. Read AGENTS.md, [CURRENT-STATE.md](./CURRENT-STATE.md), the relevant phase tracker, and the governing domain documents. Inspect the actual branch, working-tree changes, recent commits, open issue, and PR; a handoff is context, not proof of current state.
2. Create or reuse a GitHub issue before implementation. Record the problem, reproducible example, acceptance criteria, scope exclusions, and required decisions. Search existing issues to avoid duplicates. If GitHub is unavailable, record a local task using [TASK-TEMPLATE.md](./templates/TASK-TEMPLATE.md) and explicitly mark it unsynced.
3. Create a short-lived `fix/`, `feat/`, `docs/`, or `refactor/` branch from current main, or resume the branch linked to the issue. Never overwrite unrelated work or commit directly to main.
4. Link the issue and branch in CURRENT-STATE.md. Mark architecture proposals as proposed; use an ADR for changes to architectural agreements. An issue or suggestion alone does not approve an architectural decision.

## Implement and verify

1. Reproduce a defect first. Add regression tests that fail on the old behavior and pass on the fix; include relevant negative paths and invariants.
2. Keep commits focused. Update behavior, tests, affected contracts, and documentation together. Preserve tenant isolation and authorization boundaries.
3. Run targeted tests during development, then `pnpm verify` before creating the PR, as required by DELIVERY.md. Record the actual commands, environment assumptions, counts, failures, and unrun checks. Do not equate health probes with browser workflow coverage.
4. Inspect the final diff and working tree. Check for secrets, unrelated edits, generated output, and stale documentation. A passing test does not replace review of business rules.
5. Use descriptive commits: `fix(pricing): distribute bill remainder without negative shares`, for example. Include `Refs #<issue>` in the commit body. Do not commit machine-specific helpers, credentials, or test logs.

## Submit for review

1. Push the branch and open a PR using [the PR template](../.github/pull_request_template.md). Include `Closes #<issue>`, the user-visible problem and result, exact validation evidence, remaining risks, and links to relevant decisions/backlog items.
2. Prefer a draft PR if required checks are failing or work is incomplete. Clearly state the blocker; never imply that opening a PR means the work is merged or deployed.
3. Inspect CI and resolve failures within scope. Mandatory peer review and passing checks are required before merge. Do not self-merge or deploy unless explicitly authorized.

## Leave a durable handoff

Update CURRENT-STATE.md before stopping, including when blocked or interrupted:

- Issue, branch, PR, and base commit; distinguish local, pushed, review-ready, merged, and deployed states.
- Completed changes and actual verification evidence with dates.
- Remaining work, known limitations, unresolved decisions, and the next concrete action.
- Environment prerequisites, without secrets or real customer data.

For larger work, link a phase tracker or task record instead of copying its full history. Use [REVIEW-BACKLOG.md](./REVIEW-BACKLOG.md) for deferred review findings. After merge, the next task should refresh CURRENT-STATE.md from Git and GitHub before proceeding. Do not require a commit to contain its own hash; Git and the PR provide the authoritative revision history.
