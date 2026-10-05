# Roles & Permissions Specification (`docs/ROLES-AND-PERMISSIONS.md`)

## 1. Role Definitions

`restaurant-order` enforces strict Role-Based Access Control (RBAC) across 8 distinct roles operating within specific multi-tenant boundary scopes.

| Role | Scope | Primary Responsibility | Authentication Method [Proposed] |
| :--- | :--- | :--- | :--- |
| **1. Super Admin** | Platform-wide | System health, tenant onboarding, billing & plans. | Email + Password + MFA |
| **2. Restoran Admini** | Tenant (Brand) | Organization settings, brand menus, branch rollout, financials. | Email + Password (+ MFA) |
| **3. Şube Müdürü** | Branch | Floor layout, staff shifts, menu stockouts, daily reports. | Email + Password / 6-digit PIN |
| **4. Operasyon/Kasa** | Branch | Cash drawer, POS payments, split bills, receipts, manual overrides.| 4-digit PIN |
| **5. Mutfak** | Branch (Station) | Food preparation queue, mark ready, item 86ing. | Station Device Login / PIN |
| **6. Bar** | Branch (Station) | Drink preparation queue, mark ready, beverage 86ing. | Station Device Login / PIN |
| **7. Garson** | Branch (Floor) | Order taking, table moves, service calls, payment requests. | 4-digit PIN |
| **8. Müşteri** | Table Session | QR menu browsing, order placement, service call, bill view. | Anonymous (QR Session Token) |

---

## 2. RBAC Permissions Matrix

Legend:
- `✓`: Full Access / Permitted
- `O`: Own / Assigned Scope Only (e.g., own tables, own station)
- `✗`: Strictly Prohibited

| Permission / Resource | Machine-Readable Capability | Super Admin | Restoran Admini | Şube Müdürü | Operasyon / Kasa | Mutfak | Bar | Garson | Müşteri |
| :--- | :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Platform Management** | | | | | | | | | |
| Create / Suspend Tenant | `platform.tenants.manage` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Configure Platform Fees | `platform.fees.manage` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| View System Audit Logs | `platform.audit.view` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Brand & Branch Config** | | | | | | | | | |
| Create / Edit Brands | `tenant.brands.manage` | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Create / Edit Branches | `tenant.branches.manage` | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| View Corporate Branding | `tenant.branding.view` | `✗` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` |
| Manage Branding & Themes | `tenant.branding.manage` | `✗` | `✓` | `O` | `✗` | `✗` | `✗` | `✗` | `✗` |
| View Branch Configuration | `branch.configuration.view` | `✗` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` |
| Manage Branch Configuration | `branch.configuration.manage` | `✗` | `✓` | `O` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Configure Printers / Network | `branch.printers.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Edit Dining Area & Tables | `branch.tables.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Menu & Catalog** | | | | | | | | | |
| Create / Edit Categories & Items | `menu.catalog.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Set Item Prices & Variants | `menu.pricing.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Quick 86 (Mark Out-of-Stock) | `menu.inventory.quick86` | `✗` | `✓` | `✓` | `✓` | `✓` | `✓` | `✗` | `✗` |
| View Menu & Availability | `menu.catalog.view` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` |
| **Floor & Table Operations** | | | | | | | | | |
| Open / Close Table Session | `floor.sessions.manage` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `O` |
| Merge / Move Tables | `floor.tables.move` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| View Floor Status | `floor.status.view` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| **Order & Ticket Lifecycle** | | | | | | | | | |
| Place Order via QR | `orders.qr.create` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✓` |
| Place Order for Table (Staff) | `orders.staff.create` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Cancel Order Item (Pre-Prep) | `orders.items.cancel_pre_prep` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Void Order Item (In-Prep/Ready)| `orders.items.void_in_prep` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **KDS Preparation** | | | | | | | | | |
| View Kitchen Food Queue | `kds.kitchen.view` | `✗` | `✓` | `✓` | `✗` | `✓` | `✗` | `✗` | `✗` |
| View Bar Drink Queue | `kds.bar.view` | `✗` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` | `✗` |
| Update Ticket State (Prep/Ready)| `kds.ticket.update` | `✗` | `✗` | `✓` | `✗` | `O` | `O` | `✗` | `✗` |
| Recall Completed Ticket | `kds.ticket.recall` | `✗` | `✗` | `✓` | `✗` | `O` | `O` | `✗` | `✗` |
| **Billing & Payments** | | | | | | | | | |
| Request Bill from Table | `billing.bill.request` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✓` | `✓` |
| Collect Cash Payment | `billing.payment.cash` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` |
| Process External POS Card Pay | `billing.payment.pos_card` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `O` | `✗` |
| Split Bill by Amount or Item | `billing.bill.split` | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Apply Order Discount | `billing.discount.apply` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Authorize Refund | `billing.refund.authorize` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Staff & Analytics** | | | | | | | | | |
| Manage Staff & Assign Roles | `branch.staff.manage` | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| View Daily Branch Revenue | `reports.branch.revenue` | `✗` | `✓` | `✓` | `O` | `✗` | `✗` | `✗` | `✗` |
| View Multi-Branch Reports | `reports.tenant.multi_branch` | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |

---

## 3. Security Invariants for Authorization

1. **Deny-by-Default:** Any unmapped action or missing permission claim must return `403 Forbidden`.
2. **Strict Multi-Tenant Boundary:** A user with `Restoran Admini` in Tenant A can never query or manipulate data belonging to Tenant B under any circumstance.
3. **Session Scoping for Customers:** A customer session token is cryptographically bound to a single `table_session_id`. It cannot view or place orders on another table.
4. **Manager Override for Critical Actions:** Voids on items already in preparation, bill discounts, and payment refunds require supervisor PIN verification.
5. **Central Registry Alignment:** All capability identifiers in code (`RestaurantOrder.Application.Auth.Permissions`) must match the machine-readable strings defined in Section 2 above with zero deviation.
6. **User Membership Lifecycle & Tenant Isolation (`UserMembershipStatus`):** Staff status (`Active`, `Suspended`, `Disabled`) is scoped strictly per tenant membership. Suspending a user in Tenant A leaves any memberships in Tenant B unaffected. Transitioning to `Suspended` or `Disabled` instantly revokes all active sessions for that user across all instances. `Şube Müdürü` (Branch Manager) can only manage staff within their assigned branch; cross-branch actions return `403 Forbidden`.
7. **Atomic Single-Statement Token Consumption:** Staff invitation and password reset tokens are consumed via atomic database functions (`UPDATE ... WHERE is_consumed = FALSE RETURNING ...`) guaranteeing exactly one concurrent redemption. Raw tokens are never returned in API payloads and are delivered strictly out-of-band.
8. **Feature Flags are Not Authorization:** Feature flags toggle operational functionality (e.g., `order_acceptance`, `service_charge`) but never confer or bypass RBAC permissions. A user lacking `branch.configuration.manage` cannot update branch configuration or toggles, regardless of flag values.
9. **Branch Scoping Invariant:** A Branch Manager (`Şube Müdürü`) possesses management permissions strictly scoped to their assigned branch (`O`). Any attempt to read or mutate another branch's configuration, dining areas, stations, or operating hours fails with `403 Forbidden`.
## Phase 5 Catalog Authorization

Catalog viewing uses the menu.catalog.view permission; structure changes use menu.catalog.manage; price and modifier price changes use menu.pricing.manage; stockout/restock uses menu.inventory.quick86. These API checks remain authoritative when the admin UI hides controls. BranchManager access is restricted to the assigned branch. Kitchen and Bar quick-86 operations are restricted to their respective preparation station assignments. Customer/runtime viewing does not grant catalog mutation rights.
