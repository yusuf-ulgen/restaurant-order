# Repository Structure & Coding Standards (`docs/REPOSITORY-STRUCTURE.md`)

## 1. Planned Repository Layout (Monorepo Architecture)

The repository will be structured as a modular workspace cleanly separating core domain packages, backend services, and frontend applications across the 5 surfaces:

```
restaurant-order/
├── AGENTS.md                          # Master binding agent & contributor instructions
├── README.md                          # Repository overview & quick start
├── docs/                              # Architecture, domain, product & operations docs
│   ├── adr/                           # Architecture Decision Records
│   ├── runbooks/                      # Operations & deployment runbooks
│   └── templates/                     # ADR, feature, and incident templates
├── apps/                              # [Planned / Phase 1] Surface applications
│   ├── customer-qr-web/               # Surface 1: Mobile-first QR guest web app
│   ├── waiter-mobile/                 # Surface 2: Waiter & floor operations web app
│   ├── kitchen-kds/                   # Surface 3: Kitchen & Bar display system
│   ├── restaurant-admin/              # Surface 4: Restaurant & branch management portal
│   └── super-admin/                   # Surface 5: Platform multi-tenant control plane
├── packages/                          # [Planned / Phase 1] Shared libraries
│   ├── domain/                        # Core domain models, state machines & validation
│   ├── database/                      # DB schema, migrations & RLS policies
│   ├── api-client/                    # Typed API client SDK & contracts
│   ├── ui-components/                 # Shared design system components & styles
│   ├── realtime-events/               # Shared event types & payload schemas
│   └── printer-protocol/              # ESC/POS command builder & network drivers
└── tools/                             # [Planned / Phase 1] Local dev & verification tools
```

> **Phase 0 Notice:** In this initial foundation phase, no application code, package manifests (`package.json`), Docker configurations, or CI workflows are created.

---

## 2. File Size & Decomposition Rules

To prevent unmaintainable monolithic files, the following boundaries are strictly enforced across all human-authored source, test, and documentation files:

### 2.1. Thresholds
- **450 Lines (Warning):** When any file approaches 450 lines, authors must plan decomposition.
- **600 Lines (Strict Maximum Ceiling):** No human-authored file may exceed 600 lines under any circumstances. CI gates and automated pre-commit scripts will reject commits violating this rule.

### 2.2. Allowlist Policy (Exceptions)
The 600-line ceiling applies strictly to human-authored code. The only permitted exceptions are:
1. **Package Lockfiles:** `package-lock.json`, `pnpm-lock.yaml`, `yarn.lock`.
2. **Generated Database Artifacts:** Auto-generated database schema dumps or migration snapshots (if machine-generated).
3. **API Documentation Outputs:** OpenAPI/Swagger JSON/YAML generated files.
4. **Third-Party Vendor Bundles:** Third-party libraries checked into vendor directories.
5. **Snapshot Test Files:** Machine-generated Jest/Vitest snapshot files.

Any file exceeding 600 lines that is not on an explicit allowlist is considered a critical defect.

### 2.3. Decomposition Guidelines
Files must **never** be split arbitrarily (e.g., `file_part1.ts`, `file_part2.ts`). Instead, split by:
- **Feature / Sub-Domain:** Group related domain logic together (e.g., `order-lifecycle.ts`, `order-pricing.ts`).
- **Use-Case / Interactor:** Separate application use-cases into individual handlers (e.g., `PlaceOrderHandler.ts`, `CancelOrderItemHandler.ts`).
- **Port / Adapter:** Separate business logic from external protocols (e.g., `EscPosPrinterAdapter.ts`, `StripePaymentAdapter.ts`).
- **Component Responsibility:** Break UI screens into atomic, focused components (e.g., `KdsTicketCard.tsx`, `KdsTimerBadge.tsx`).

---

## 3. Modular Boundary Rules

1. **Unidirectional Dependencies:** Shared packages (`packages/domain`) must never import from application surfaces (`apps/*`).
2. **Domain Purity:** The domain package must remain free of framework dependencies (React, Express, NestJS, etc.) and ORM annotations.
3. **No Cross-Surface Imports:** Applications (e.g., `apps/kitchen-kds`) cannot directly import files from sibling applications (e.g., `apps/waiter-mobile`). Shared code must reside in `packages/*`.
4. **Tenant Context Encasement:** All database access layer functions must mandate `tenant_id` and `branch_id` arguments.
