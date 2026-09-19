# Claude Instructions Adapter (CLAUDE.md)

> **Binding Authority:** This file is a lightweight adapter for Claude. You **must** read and adhere to the master instructions in [AGENTS.md](file:///d:/freelance/restaurant-order/AGENTS.md) before performing any operation.

## Operational Instructions

1. **Read Master Rules:** Review [AGENTS.md](file:///d:/freelance/restaurant-order/AGENTS.md) for mandatory rules including file size limits (450 lines warning, 600 lines strict ceiling), test verification policy, security standards, and blue-green deployment principles.
2. **Consult Domain Docs:** Before implementing any feature or modifying architecture, read the relevant documents in [docs/](file:///d:/freelance/restaurant-order/docs/):
   - Architecture & Structure: [docs/ARCHITECTURE.md](file:///d:/freelance/restaurant-order/docs/ARCHITECTURE.md), [docs/REPOSITORY-STRUCTURE.md](file:///d:/freelance/restaurant-order/docs/REPOSITORY-STRUCTURE.md)
   - Business Logic & States: [docs/DOMAIN.md](file:///d:/freelance/restaurant-order/docs/DOMAIN.md), [docs/STATE-MACHINES.md](file:///d:/freelance/restaurant-order/docs/STATE-MACHINES.md), [docs/NEGATIVE-FLOWS.md](file:///d:/freelance/restaurant-order/docs/NEGATIVE-FLOWS.md)
   - Roles & Permissions: [docs/ROLES-AND-PERMISSIONS.md](file:///d:/freelance/restaurant-order/docs/ROLES-AND-PERMISSIONS.md)
   - Integration & Hardware: [docs/ORDER-ROUTING-AND-PRINTING.md](file:///d:/freelance/restaurant-order/docs/ORDER-ROUTING-AND-PRINTING.md), [docs/PAYMENTS-TIPS-COMMISSIONS.md](file:///d:/freelance/restaurant-order/docs/PAYMENTS-TIPS-COMMISSIONS.md)
3. **Execution Standards:** Never report unexecuted actions or tests as PASS. Never introduce secrets, real customer data, or breaking changes without authorization.
