# Domain Model & Bounded Contexts (`docs/DOMAIN.md`)

## 1. Domain Overview

The `restaurant-order` platform operates in the restaurant hospitality and operations domain. The ubiquitous language is standardized across all product surfaces, API contracts, and database models.

---

## 2. Bounded Contexts

```
+-------------------------------------------------------------------------------+
|                           MULTI-TENANT CONTROL PLANE                          |
|             [Tenant / Organization] ---> [Brand] ---> [Branch]                |
+---------------------------------------+---------------------------------------+
                                        |
        +-------------------------------+-------------------------------+
        |                               |                               |
        v                               v                               v
+------------------+          +------------------+          +-------------------+
|  CATALOG & MENU  |          |  TABLE & FLOOR   |          |    ORDER & KDS    |
| - Menu           |          | - Dining Area    |          | - Order           |
| - Category       |          | - Table          |          | - Order Item      |
| - MenuItem       |          | - QR Code        |          | - Station Ticket  |
| - Variant        |          | - Table Session  |          | - Ticket Item     |
| - ModifierGroup  |          +------------------+          +---------+---------+
+------------------+                                                  |
                                                                      v
                                                            +-------------------+
                                                            | BILLING & PAYMENT |
                                                            | - Bill / Receipt  |
                                                            | - Payment / Split |
                                                            | - Tip             |
                                                            | - Refund          |
                                                            | - Commission      |
                                                            +-------------------+
```

---

## 3. Core Entities & Relationships

### 3.1. Tenant & Organization Hierarchy
- **Tenant / Organization:** The top-level legal business entity (e.g., "Gourmet Group Inc."). Holds subscriptions, billing contracts, and platform settings.
- **Brand:** A distinct restaurant concept under an organization (e.g., "Gourmet Burger", "Gourmet Pizza"). Menus can be scoped to a brand.
- **Branch:** A physical restaurant location belonging to a brand (e.g., "Gourmet Burger - Kadıköy Branch"). Holds tables, printers, staff, and localized inventory.

### 3.2. Table & Floor Management
- **DiningArea:** A physical zone within a branch (e.g., "Terrace", "Main Hall", "Bar Area", "Garden").
- **Table:** A numbered dining table or seat with a unique identifier within the branch. Associated with a persistent QR code.
- **TableSession:** An active dining session created when guests are seated or place their first order. Closed upon bill settlement.

### 3.3. Menu & Catalog Management
- **Menu:** A curated collection of food and beverage offerings. Can be scoped to specific branches or time schedules (e.g., "Breakfast Menu", "All-Day Menu").
- **MenuCategory:** A logical grouping of items (e.g., "Starters", "Main Courses", "Cocktails", "Desserts").
- **MenuItem:** A specific culinary or beverage offering (e.g., "Cheeseburger", "Espresso").
- **ItemVariant:** Size or portion options with distinct pricing (e.g., "Single (150g)", "Double (300g)", "Small", "Large").
- **ModifierGroup:** A set of choices or customizations attached to an item (e.g., "Meat Doneness", "Choose Side", "Extra Toppings").
  - `min_selections`: Minimum number of options required (e.g., 1 for meat doneness).
  - `max_selections`: Maximum allowed choices.
- **ModifierItem:** An individual customization option (e.g., "Well Done", "Truffle Fries", "Extra Cheddar +$1.50").

### 3.4. Order & Ticket Lifecycle
- **Order:** A dining order tied to a `branch_id`, `table_session_id`, and `order_source` (Customer QR, Waiter Mobile, or POS).
- **OrderItem:** An instance of a `MenuItem` or `ItemVariant` within an order, with associated selected modifiers and special guest notes.
- **StationTicket (KitchenTicket / BarTicket):** A sub-order routed to a specific preparation station (e.g., Kitchen or Bar). Contains items assigned to that station.
- **TicketItem:** An item within a station ticket with its individual prep state (Queued, In-Prep, Ready, Served, Cancelled).

### 3.5. Billing, Payments & Tips
- **Bill:** The financial invoice for a `TableSession`, aggregating all non-cancelled order items, applied taxes, discounts, and service fees.
- **Payment:** A financial transaction against a bill. Can be full or partial (split bill).
  - `payment_method`: Cash, External POS Credit Card, Digital Online Gateway `[Proposed / ADR Required]`.
- **Tip:** An optional gratuity added to a payment, allocated either to the specific waiter or pooled across staff.
- **PlatformCommission:** The transaction fee retained by the platform provider based on the tenant's tier.
- **Refund:** A full or partial reversal of a settled payment, requiring supervisor authorization and audit logging.

### 3.6. Identity & Access Control
- **User:** A human actor authenticated to the system (staff member or platform operator).
- **Role:** One of the 8 standard roles defined in [docs/ROLES-AND-PERMISSIONS.md](file:///d:/freelance/restaurant-order/docs/ROLES-AND-PERMISSIONS.md).
- **UserBranchAssignment:** Maps staff members to specific branches with an optional quick-access PIN.
- **AuditLog:** Immutable ledger recording critical actions (order cancellations, discounts, refunds, price overrides).

---

## 4. Entity Cardinality Summary

| Relationship | Cardinality | Description |
| :--- | :--- | :--- |
| Organization to Brand | 1 : N | One organization owns one or more restaurant brands. |
| Brand to Branch | 1 : N | One brand operates one or more physical branches. |
| Branch to DiningArea | 1 : N | A branch contains multiple dining areas. |
| DiningArea to Table | 1 : N | An area contains multiple tables. |
| Table to TableSession | 1 : N (1 active) | A table has one active session at any time. |
| TableSession to Order | 1 : N | Multiple rounds of orders can be placed within one session. |
| Order to OrderItem | 1 : N | An order contains multiple line items. |
| OrderItem to Modifier | N : M | An order item can have multiple selected modifiers. |
| Order to StationTicket | 1 : N | An order splits into station tickets (Kitchen, Bar). |
| TableSession to Bill | 1 : 1 (active) | A session consolidates into a single bill. |
| Bill to Payment | 1 : N | A bill can be settled via multiple split payments. |
