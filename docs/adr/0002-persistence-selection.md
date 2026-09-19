# ADR-0002: Data Persistence Library Selection (`docs/adr/0002-persistence-selection.md`)

- **Status:** `PROPOSED`
- **Deciders:** Architecture Team, Yusuf Ülgen
- **Date:** 2026-09-19
- **Technical Story:** Data Access & Persistence Strategy Evaluation

---

## 1. Context and Problem Statement

`restaurant-order` relies on PostgreSQL as its primary transactional database. The system requires strong relational consistency for financial ledgers and billing, deterministic state transitions for orders and tickets, and row-level tenant isolation.

A persistence library must be evaluated to bridge the domain model with PostgreSQL. **In this phase, no persistence library is definitively selected.** This ADR documents the comparative evaluation between Entity Framework Core, Dapper, and Marten to guide the subsequent decision.

---

## 2. Decision Drivers

- **Domain Model Integrity:** Rich domain entities with encapsulated state machines and private setters.
- **Query Performance & Overhead:** Minimal allocation overhead and high query throughput during peak restaurant dining hours.
- **Migration & Schema Safety:** Support for zero-downtime expand-and-contract migrations.
- **Multi-Tenancy & Row-Level Security (RLS):** Seamless integration with PostgreSQL session variables (`SET LOCAL app.current_tenant_id`).
- **Developer Productivity & Maintainability:** Refactoring safety and readable query abstractions.

---

## 3. Considered Options

### Option A: Entity Framework Core (EF Core 10)
- **Description:** Full-featured Object-Relational Mapper (ORM) for .NET.
- **Pros:**
  - Rich mapping capabilities (Value Objects, owned entities, backing fields).
  - Built-in Global Query Filters for automated multi-tenant `tenant_id` filtering.
  - Robust migration engine with SQL script generation.
  - Change tracking and optimistic concurrency tokens (`RowVersion`) out-of-the-box.
- **Cons:**
  - Higher memory allocation compared to micro-ORMs for complex bulk queries.
  - Potential performance pitfalls with complex LINQ-to-SQL joins if unoptimized.

### Option B: Dapper
- **Description:** Lightweight, high-performance micro-ORM executing raw SQL queries with fast object mapping.
- **Pros:**
  - Near-raw ADO.NET performance with negligible allocation overhead.
  - Complete control over SQL statements, execution plans, and PostgreSQL-specific features.
  - Simple, predictable query behavior.
- **Cons:**
  - No automated change tracker; update statements must be written manually.
  - No built-in migration engine (requires external tools like DbUp or Flyway).
  - Multi-tenant query filtering must be manually included in every query or enforced strictly via DB RLS.

### Option C: Marten (.NET Transactional Document DB & Event Store on PostgreSQL)
- **Description:** PostgreSQL-backed document database and event sourcing framework.
- **Pros:**
  - Excellent fit for event-driven order lifecycles (Order Placed, Item Prepared, Payment Succeeded).
  - Built-in aggregate snapshotting and event store projections.
  - Schema evolution for flexible menu modifier configurations.
- **Cons:**
  - Relational reporting and cross-aggregate financial queries require asynchronous projection tables.
  - Steeper learning curve for developers unfamiliar with event sourcing.
  - Complex integration with standard relational BI / reporting tools.

---

## 4. Current Status & Next Steps

- **Status:** `PROPOSED` (No persistence library adopted in Phase 0/Foundation).
- **Next Steps:** Benchmark EF Core vs Dapper vs Marten with a prototype order lifecycle slice before committing to a final choice in Phase 1.
