# @restaurant-order/contracts

## 1. Purpose & Boundary

This package defines the contract boundary between backend APIs (`apps/api`) and frontend client applications (`apps/*-web`).

In accordance with project architecture guidelines:
- **No Manually Fabricated API Types:** All client contract types must be automatically generated from the ASP.NET Core OpenAPI JSON specification.
- **Single Source of Truth:** The backend OpenAPI metadata is the authoritative contract schema.
- **Generation Workflow:** Once domain endpoints are added to `apps/api`, a code generation script (e.g., `openapi-typescript`) will populate this package directly from the running or compiled OpenAPI document.

---

## 2. Starter Health Contract

During this foundation phase, the only active endpoints are `/health/live` and `/health/ready`.
