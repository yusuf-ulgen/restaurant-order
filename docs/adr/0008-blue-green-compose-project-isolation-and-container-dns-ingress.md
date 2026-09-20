# ADR-0008: Blue-Green Compose Project Isolation & Container DNS Ingress Routing

**Status:** ACCEPTED  
**Date:** 2026-09-19  
**Context:** Blue-Green Deployment Infrastructure Hardening

---

## Context

The initial blue-green deployment design used a single Docker Compose project for both slots. This caused a critical collision:

> When `docker compose -f compose.yml -f compose.prod.blue.yml up -d` is run, Docker identifies services by project+service name. Running it again with `compose.prod.green.yml` targeting the same project causes green containers to **replace** (recreate) blue containers because Docker treats them as the same service identity — even if `container_name` differs.

Additionally, the cutover script called `nginx -t` and `nginx -s reload` against the **host process**, not the ingress container. This means:
- The config tested by `nginx -t` could differ from the config actually loaded on reload.
- There was no guarantee a host nginx process existed.
- Host-port-based upstreams (`127.0.0.1:5001`) required both slots to bind to the host, which conflicts with read-only container filesystems.

## Decision

### 1. Separate Docker Compose Projects per Slot

Each deployment slot runs as an independent Compose project:

```
docker compose -p restaurant-order-blue  -f compose.yml -f compose.prod.blue.yml  up -d --force-recreate
docker compose -p restaurant-order-green -f compose.yml -f compose.prod.green.yml up -d --force-recreate
```

This guarantees:
- `restaurant-order-blue` and `restaurant-order-green` are completely separate Docker namespaces.
- `--force-recreate` on green does not touch blue containers.
- Volume and network names are slot-scoped.

### 2. Shared External Docker Network for Ingress Routing

Both slot projects attach to a shared external network `restaurant_order_ingress`:

```bash
docker network create restaurant_order_ingress
docker compose -p restaurant-order-ingress -f compose.ingress.yml up -d
```

The ingress Nginx container joins this network and resolves slot containers by Docker DNS container name:
- `restaurant-order-api-blue:5000`
- `restaurant-order-api-green:5000`
- `restaurant-order-customer-web-blue:8080`
- etc.

**No host port bindings are required** for slot containers to be accessible to the ingress.

### 3. Cutover via `docker exec` on the Ingress Container

The cutover script now:
1. Writes a **candidate** upstream config locally (via the bind-mount `deploy/nginx/conf.d/`).
2. Validates it: `docker exec restaurant-order-ingress nginx -t`.
3. Atomically reloads: `docker exec restaurant-order-ingress nginx -s reload`.
4. If validation or reload fails, reverts the config file and re-executes reload.

This guarantees the **tested config is identical to the reloaded config** — they are the same file.

### 4. Ingress as an Independent Project

The ingress Compose project (`restaurant-order-ingress`) is independent of slot projects:
- Recreating the blue or green project does **not** restart or affect the ingress.
- The ingress runs continuously across deployments.

### 5. All 5 Image Digests Required at Preflight

Production manifests must include immutable sha256 digests for all 5 images:
`api`, `worker`, `customer-web`, `operations-web`, `admin-web`.

The known empty-content hash (`sha256:e3b0c44298...`) is explicitly rejected — it indicates the image was never built.

## Consequences

**Positive:**
- Blue and green can run fully concurrently with zero interference.
- Cutover is safer: tested config === loaded config.
- No host nginx process dependency.
- Secrets are not exposed via host port bindings.

**Negative:**
- Operators must create the shared network before first deploy:
  `docker network create restaurant_order_ingress`
- Ingress must be started separately before slot deployments.
- The `INGRESS_CONTAINER` name is a convention that must be maintained.

## Invariants

- `ACTIVE: docker compose -p restaurant-order-blue` containers are never recreated by a green deploy.
- Nginx config validated in the ingress container is the same file that gets reloaded.
- A failed Redis state update after Nginx reload triggers `CRITICAL_INCONSISTENT_STATE`, never `PASS`.
