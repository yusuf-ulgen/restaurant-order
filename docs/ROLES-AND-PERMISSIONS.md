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

| Permission / Resource | Super Admin | Restoran Admini | Şube Müdürü | Operasyon / Kasa | Mutfak | Bar | Garson | Müşteri |
| :--- | :---: | :---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **Platform Management** |
| Create / Suspend Tenant | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Configure Platform Fees | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| View System Audit Logs | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Brand & Branch Config** |
| Create / Edit Brands | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Create / Edit Branches | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Configure Printers / Network | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Edit Dining Area & Tables | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Menu & Catalog** |
| Create / Edit Categories & Items | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Set Item Prices & Variants | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Quick 86 (Mark Out-of-Stock) | `✗` | `✓` | `✓` | `✓` | `✓` | `✓` | `✗` | `✗` |
| View Menu & Availability | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` | `✓` |
| **Floor & Table Operations** |
| Open / Close Table Session | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `O` |
| Merge / Move Tables | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| View Floor Status | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| **Order & Ticket Lifecycle** |
| Place Order via QR | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✓` |
| Place Order for Table (Staff) | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Cancel Order Item (Pre-Prep) | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Void Order Item (In-Prep/Ready)| `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **KDS Preparation** |
| View Kitchen Food Queue | `✗` | `✓` | `✓` | `✗` | `✓` | `✗` | `✗` | `✗` |
| View Bar Drink Queue | `✗` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` | `✗` |
| Update Ticket State (Prep/Ready)| `✗` | `✗` | `✓` | `✗` | `O` | `O` | `✗` | `✗` |
| Recall Completed Ticket | `✗` | `✗` | `✓` | `✗` | `O` | `O` | `✗` | `✗` |
| **Billing & Payments** |
| Request Bill from Table | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` | `✓` | `✓` |
| Collect Cash Payment | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` |
| Process External POS Card Pay | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `O` | `✗` |
| Split Bill by Amount or Item | `✗` | `✓` | `✓` | `✓` | `✗` | `✗` | `✓` | `✗` |
| Apply Order Discount | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| Authorize Refund | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| **Staff & Analytics** |
| Manage Staff & Assign Roles | `✗` | `✓` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` |
| View Daily Branch Revenue | `✗` | `✓` | `✓` | `O` | `✗` | `✗` | `✗` | `✗` |
| View Multi-Branch Reports | `✗` | `✓` | `✗` | `✗` | `✗` | `✗` | `✗` | `✗` |

---

## 3. Security Invariants for Authorization

1. **Deny-by-Default:** Any unmapped action or missing permission claim must return `403 Forbidden`.
2. **Strict Multi-Tenant Boundary:** A user with `Restoran Admini` in Tenant A can never query or manipulate data belonging to Tenant B under any circumstance.
3. **Session Scoping for Customers:** A customer session token is cryptographically bound to a single `table_session_id`. It cannot view or place orders on another table.
4. **Manager Override for Critical Actions:** Voids on items already in preparation, bill discounts, and payment refunds require supervisor PIN verification.
