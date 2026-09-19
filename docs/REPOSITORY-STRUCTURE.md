# Repository Structure & Coding Standards (`docs/REPOSITORY-STRUCTURE.md`)

## 1. Planned Repository Layout (Monorepo Architecture)

The repository will be structured as a modular workspace cleanly separating core domain packages, backend services, and frontend applications across the 5 surfaces:

```
restaurant-order/
├── AGENTS.md                          # Master binding agent & contributor instructions
├── README.md                          # Repository overview & quick start
├── pnpm-workspace.yaml                # Monorepo workspace configuration
├── package.json                       # Root scripts and dev tooling
├── pnpm-lock.yaml                     # Single unified lockfile
├── RestaurantOrder.sln                # .NET 10 unified solution
├── global.json                        # .NET 10 SDK pin
├── .editorconfig                      # Multi-language formatting standards
├── .gitignore                         # Git exclusion rules
├── .env.example                       # Local environment variables template
├── docs/                              # Architecture, domain, product & operations docs
│   ├── adr/                           # Architecture Decision Records (ADR-0001, ADR-0002)
│   ├── runbooks/                      # Operations & deployment runbooks
│   └── templates/                     # ADR, feature, and incident templates
├── apps/                              # Surface applications & backend hosts
│   ├── customer-web/                  # Surface 1: Responsive PWA QR guest web app (React 19 + Vite)
│   ├── operations-web/                # Surface 2: Waiter & operations web app (React 19 + Vite)
│   ├── admin-web/                     # Surface 4 & 5: Restaurant & Super Admin portal (React 19 + Vite)
│   ├── api/                           # ASP.NET Core 10 Modular Monolith REST API
│   └── worker/                        # .NET 10 Background Worker Host
├── packages/                          # Shared monorepo packages
│   ├── ui/                            # Design tokens & core shared UI components (React 19)
│   ├── contracts/                     # OpenAPI contract boundary & shared DTO types
│   └── config/                        # Shared TypeScript, ESLint & toolchain configurations
├── tests/                             # Quality & verification suites
│   ├── architecture/                  # .NET architecture boundary tests
│   ├── integration/                   # ASP.NET Core integration tests (/health endpoints)
│   └── e2e/                           # Playwright end-to-end test suite
├── deploy/                            # Containerization & local infrastructure
│   ├── docker-compose.yml             # Local PostgreSQL 16 & Redis 7 services
│   └── docker/                        # Multi-stage Dockerfiles (api, worker, web)
└── scripts/                           # Tooling & verification scripts
    ├── verify.ps1                     # Full monorepo verification pipeline
    └── dev.ps1                        # Local development environment launcher
```

> **Foundation Status:** The monorepo technical foundation is scaffolded with React 19 frontend shells, ASP.NET Core 10 API starter endpoints (`/health/live`, `/health/ready`), .NET 10 background worker host, and shared TypeScript configuration and UI packages. No business domain logic or premature database schema has been added yet.

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
