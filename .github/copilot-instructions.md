# GitHub Copilot Instructions (.github/copilot-instructions.md)

> **Binding Authority:** This file is a lightweight adapter for GitHub Copilot. You **must** read and adhere to the master instructions in [AGENTS.md](../AGENTS.md) before performing any operation or code generation.

## Operational Instructions

1. **Master Rules:** All coding rules, file line limits (450 lines warning, 600 lines strict ceiling), test verification policies, and security guardrails are governed by [AGENTS.md](../AGENTS.md).
2. **Domain Documents:** Read relevant functional and architectural specifications in [docs/](../docs/) based on your assigned task scope.
3. **Execution Guardrails:** Never introduce secrets or real customer data into the repo. Enforce zero-unverified-PASS policy. Mark open architectural decisions as `[Proposed / ADR Required]`.

