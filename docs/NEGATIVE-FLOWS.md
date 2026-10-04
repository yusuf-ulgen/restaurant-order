# Negative Flows & Edge Cases (`docs/NEGATIVE-FLOWS.md`)

## 1. Overview

Reliable restaurant operations depend on graceful failure recovery. This document details system behavior during operational failures, network disruptions, hardware faults, and concurrent conflicts.

---

## 2. Failure Scenarios & Recovery Strategies

### 2.1. Item Stockout During Checkout (The "86 Race Condition")
- **Scenario:** A guest adds the last portion of "Ribeye Steak" to their cart. While they review their order, the kitchen staff marks Ribeye as 86 (out of stock).
- **Detection:** Pre-commit validation hook checks active inventory / availability status inside the database transaction.
- **System Response:**
  1. Transaction aborts with `ITEM_OUT_OF_STOCK` error code.
  2. The cart flags the specific unavailable item with a clear visual warning.
  3. The guest is prompted: *"Sorry, 'Ribeye Steak' just ran out! Please remove it to proceed with the rest of your order."*
  4. The rest of the cart items remain intact.

### 2.2. Concurrent Orders on the Same Table
- **Scenario:** Two guests at Table 4 scan the QR code and tap "Submit Order" at the exact same second.
- **Detection:** Database optimistic locking on `table_session_id` or sequential order queueing per table session.
- **System Response:**
  1. Both orders are accepted as distinct, incremental rounds under the same `table_session_id`.
  2. Each round is assigned a unique `order_id` (e.g., Round 1, Round 2) to prevent item overwrites.
  3. Both guests receive confirmation screens with their respective order numbers.
  4. Station tickets display both rounds clearly (e.g., "Table 4 - Round 2").

### 2.3. Thermal Printer Offline / Paper Out
- **Scenario:** An order is submitted, but the kitchen ESC/POS thermal printer runs out of paper, loses network connectivity, or is powered off.
- **Detection:** Socket connection timeout (default 3000ms) or printer status byte indicating `OFFLINE / PAPER_EMPTY`.
- **System Response:**
  1. **Order Progression Never Blocked:** The order and KDS tickets are successfully created in the database. Digital operations continue without interruption.
  2. The failed print job is placed in a `printer_spooler` queue with status `FAILED_RETRYING`.
  3. An alert banner appears on the Waiter App and Restaurant Admin panel: *"Kitchen Printer Offline - Please check paper."*
  4. The cashier or waiter can trigger a manual reprint with one tap once the printer is restored.

### 2.4. Payment Gateway Failure / Card Declined
- **Scenario:** A customer or waiter initiates a digital or external card payment, and the transaction is declined or times out.
- **Detection:** Gateway webhook or direct response returns `PAYMENT_DECLINED` or `GATEWAY_TIMEOUT`.
- **System Response:**
  1. The bill status remains `OPEN` or `PARTIALLY_PAID`.
  2. No funds are deducted, and an idempotent transaction record logs the failure.
  3. Waiter/Customer screen displays a friendly message: *"Card declined by bank. Please try another card or choose Cash."*
  4. In the event of a timeout, the system queries the gateway transaction status before allowing a retry to prevent double charges.

### 2.5. Cancellation of Items Already in Preparation
- **Scenario:** A guest changes their mind and attempts to cancel an item via the QR web app, but the kitchen has already started cooking (`IN_PREPARATION`).
- **Detection:** State machine check enforces that `CANCEL_ITEM` is only allowed from `SUBMITTED` state (see [docs/STATE-MACHINES.md](./STATE-MACHINES.md)).
- **System Response:**
  1. The customer's cancellation request is rejected with message: *"The kitchen has already started preparing this dish. Please speak with your waiter."*
  2. The guest can use the "Call Waiter" button.
  3. A waiter or manager can override and cancel using their supervisor PIN, which logs a `WASTE_CANCEL` audit record for inventory tracking.

### 2.6. Network Disconnect on KDS / Mobile App
- **Scenario:** Wi-Fi drops out on a waiter's smartphone or kitchen KDS tablet while taking an order.
- **Detection:** Heartbeat ping failure on the WebSocket / SSE realtime transport.
- **System Response:**
  1. Client immediately shows an unobtrusive status banner: *"Offline — Reconnecting..."*
  2. The client buffers locally drafted orders in local storage.
  3. Upon reconnection, the client automatically syncs state, verifies session validity, and offers to transmit buffered orders.
  4. Audio chime sounds on KDS when the connection is re-established and queue refreshed.

### 2.7. Unauthorized Access Attempt
- **Scenario:** A customer alters the URL or API payload to attempt reading another table's bill or accessing `/admin`.
- **Detection:** Request gateway inspects the cryptographic token claim (`table_session_id`, `role`).
- **System Response:**
  1. Returns `403 Forbidden` immediately.
  2. The incident is recorded in the platform security audit log with IP, timestamp, and attempted resource.
  3. The client application redirects to the table welcome page.

---

## 3. Restaurant Configuration & Multi-Tenant Negative Flows (Phase 4)

### 3.1. Cross-Tenant Configuration Access (Tenant A vs Tenant B)
- **Scenario:** Tenant A attempts to read or mutate settings, themes, dining areas, or stations of Tenant B.
- **Handling:** PostgreSQL RLS and EF Core query filters enforce strict isolation. Missing/cross-tenant IDs evaluate to non-existent (`404 Not Found`), completely preventing unauthorized disclosure or modification.

### 3.2. Cross-Branch Mutation by Branch Manager
- **Scenario:** Branch Manager of Branch 1 attempts to update configuration, dining areas, or feature flags of Branch 2 within the same tenant.
- **Handling:** Branch authorization handler compares the caller's assigned branch against the target resource. Mismatch immediately rejects with `403 Forbidden`.

### 3.3. Unauthorized Role Configuration Mutation
- **Scenario:** Non-administrative roles (`Garson`, `Operasyon/Kasa`, `Mutfak`, `Bar`, `Müşteri`) invoke configuration write endpoints.
- **Handling:** Deny-by-default RBAC middleware requires `branch.configuration.manage` or `tenant.branding.manage`. Unauthorized requests receive `403 Forbidden`.

### 3.4. Missing or Invalid Tenant Context (Fail-Closed)
- **Scenario:** API request arrives without tenant identification or with an unresolvable domain.
- **Handling:** Middleware fails closed, terminating the pipeline immediately with `400 Bad Request` or `401 Unauthorized` (RFC 7807 ProblemDetails). Database session variable defaults to `NULL`, returning 0 rows.

### 3.5. Stale Concurrency Token (Lost Update Prevention)
- **Scenario:** Two managers update the same branch settings, dining area, or station concurrently with a stale ETag/token.
- **Handling:** Optimistic concurrency check detects version mismatch and aborts with `409 Conflict` (RFC 7807), preventing silent overwrites.

### 3.6. Invalid Theme, URL, Tax, or Operating Hours Input
- **Scenario:** Client submits invalid hex colors, CSS expression injection, malformed URLs, tax rates > 10,000 basis points (100%), negative service charges, or invalid time formats.
- **Handling:** FluentValidation / model validators intercept before persistence and return `400 Bad Request` with structured validation errors.

### 3.7. Mutation on Closed / Suspended Branch
- **Scenario:** Manager attempts to create dining areas or update settings on an inactive/suspended branch.
- **Handling:** Application service verifies branch active status. Inactive branches reject operational mutations with `400 Bad Request` or `409 Conflict`.

### 3.8. Feature Flags Cannot Bypass RBAC
- **Scenario:** A feature flag is enabled (e.g., `order_acceptance`), but the calling user lacks the required permission.
- **Handling:** Feature flags control application feature logic only. Authorization checks occur first; requests without permission claims are rejected with `403 Forbidden`.

### 3.9. Atomic Transaction Rollback on Failure
- **Scenario:** Database failure occurs halfway through creating a dining area or updating complex operating hours.
- **Handling:** Operations run within explicit database transactions (`BeginTransactionAsync`). Any exception triggers complete rollback, guaranteeing no partial or orphaned state.

### 3.10. Admin Shell Graceful Fallback on API Failure
- **Scenario:** Branding or settings API returns 500 or network times out while rendering admin web shell.
- **Handling:** Frontend shell catches errors gracefully, displaying standard design system tokens and default navigation without crashing or showing blank screens.

### 3.11. Tenant Theme Purging on Switch
- **Scenario:** Super Admin or multi-tenant staff switches between Tenant A and Tenant B.
- **Handling:** Theme injector cleanses all dynamic CSS variables from `:root` before mounting the new tenant's theme, preventing visual bleed.
## Phase 5 Catalog Negative Flows

- Cross-tenant and cross-branch object identifiers are scoped at service queries and database RLS/composite constraints; unauthorized scope is denied or appears absent.
- BranchManager cross-branch writes, and Kitchen/Bar station mismatches, are denied.
- Negative/overflow prices, duplicate slugs/codes, invalid modifier bounds, unsupported tags and dietary/allergen contradictions fail validation.
- Missing concurrency token returns 412; stale token, duplicate availability creation and concurrent reorder conflicts return 409.
- Archived menu mutations are rejected. Inactive menus/categories/items/variants/groups/options are excluded from runtime menu.
- Database SaveChanges writes catalog changes and audit records in one EF unit of work; failed saves do not dispatch availability events. Unexpected API failures return generic RFC 7807 500 content with correlation ID.
- Admin network failures retain retry; 409 offers reload/retry and 412 explains refreshing the concurrency token.
