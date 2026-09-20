# ADR-0002: Data Persistence Library Selection (`docs/adr/0002-persistence-selection.md`)

- **Status:** `ACCEPTED`
- **Deciders:** Architecture Team, Yusuf Ülgen
- **Date:** 2026-09-20
- **Technical Story:** Data Access & Persistence Strategy Evaluation (Phase 2)

---

## 1. Context and Problem Statement

`restaurant-order` relies on PostgreSQL as its primary transactional database. The system requires strong relational consistency for financial ledgers, billing calculations, deterministic state transitions for orders and tickets, and strict row-level tenant isolation across organizations, brands, and branches.

A persistence library must bridge the domain model with PostgreSQL while satisfying the following requirements:
1. Strict domain model encapsulation (Rich Domain Entities, Value Objects, private setters).
2. Automated schema migration lifecycle integrated into CI/CD.
3. Multi-tenancy isolation boundary backed by PostgreSQL Row-Level Security (RLS).
4. Predictable performance and resource utilization during peak dining periods.

---

## 2. Decision Drivers

- **Domain Model Integrity:** Rich domain entities with encapsulated state machines and private setters without leaking ORM attributes into the domain.
- **Migration & Schema Safety:** Robust schema evolution supporting zero-downtime expand-and-contract deployments.
- **Multi-Tenancy & Row-Level Security (RLS):** Compatibility with PostgreSQL session variables (`SET LOCAL app.current_tenant_id`) and connection pooling.
- **Developer Productivity & Maintainability:** Type-safe query abstractions, compile-time query verification, and maintainable unit/integration testing.
- **Performance & Extensibility:** High throughput for standard CRUD/aggregates, with escape hatches for high-performance raw SQL when measured and justified.

---

## 3. Decision

We accept **Entity Framework Core 10 (EF Core 10)** with the **Npgsql EF Core Provider (`Npgsql.EntityFrameworkCore.PostgreSQL`)** as the primary ORM and schema migration tool for `restaurant-order`.

### Key Tenets of this Decision:
1. **Primary ORM & Migrations:** EF Core 10 is the single source of truth for schema migrations and relational mapping.
2. **Security Boundary — PostgreSQL Row-Level Security (RLS):** The definitive multi-tenant security boundary is PostgreSQL RLS. Every database connection sets `app.current_tenant_id` at the connection/transaction level.
3. **Defense-in-Depth Query Filters:** EF Core Global Query Filters will be applied to all tenant-scoped entities as a secondary defense-in-depth layer, not as a replacement for PostgreSQL RLS.
4. **Controlled Raw SQL Escape Hatch:** For complex analytical or high-throughput queries where LINQ translation introduces measurable overhead, controlled raw SQL (via `FromSqlInterpolated` or `ExecuteSqlInterpolatedAsync`) may be used following documented profiling.
5. **Rejection of Marten / Event Sourcing for MVP:** Marten and full event sourcing are rejected for the MVP. The business domain (restaurant dining, table turns, split billing) requires immediate relational consistency and straightforward reporting across aggregates. Event sourcing adds operational and projection complexity that is disproportionate for the current phase.
6. **Rejection of Dapper as Primary Persistence Layer:** Dapper is rejected as the primary persistence layer due to the absence of automated change tracking, lack of native schema migration tooling, and increased boilerplate for mapping rich domain aggregates. Dapper may be considered in future phases solely for specialized, performance-measured read projections if needed.

---

## 4. Consequences and Trade-offs

### Positive Consequences
- **Robust Domain Mapping:** EF Core 10 supports private constructors, backing fields, owned entities (Value Objects), and complex property conversions without polluting domain models with framework attributes.
- **Zero-Downtime Migration Tooling:** Native EF Core migrations provide deterministic SQL generation, rollbacks, and idempotency scripts for Blue/Green deployment pipelines.
- **Multi-Tenant Safety:** Global Query Filters combined with database RLS provide dual-layer defense against cross-tenant data leakage.
- **Rich Ecosystem & Testcontainers Support:** First-class compatibility with Npgsql, PostgreSQL 16, and Testcontainers for integration testing.

### Negative Consequences / Trade-offs & Mitigations
- **Allocation Overhead:** EF Core change tracking incurs higher memory overhead than micro-ORMs.  
  *Mitigation:* Use `.AsNoTracking()` for all read-only queries.
- **LINQ Translation Traps:** Complex multi-join queries can generate suboptimal SQL if unmonitored.  
  *Mitigation:* Enable `ThrowIdentityMappingWarning` / `QuerySplittingBehavior`, log slow queries, and utilize controlled raw SQL when justified by profiling.

---

## 5. References

- [ADR-0001: Technology Stack](./0001-technology-stack.md)
- [docs/ARCHITECTURE.md](../ARCHITECTURE.md)
- [docs/MULTI-TENANCY.md](../MULTI-TENANCY.md)
