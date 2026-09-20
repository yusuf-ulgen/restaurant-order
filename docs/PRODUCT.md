# Product Specification & Vision (`docs/PRODUCT.md`)

## 1. Executive Summary

`restaurant-order` is an integrated, multi-tenant digital dining and operational platform. It replaces legacy, fragmented POS terminals, paper tickets, and static PDF menus with a synchronized, real-time ecosystem connecting guests, waitstaff, kitchen personnel, and restaurant operators.

---

## 2. Problem Statement & Value Proposition

### 2.1. Problems Solved
- **High Table Turnover Latency:** Guests waiting for menus, waitstaff attention, or bill delivery spend 15–20 minutes in non-dining idle time.
- **Order Transcription Errors:** Verbal orders or handwritten paper tickets lead to misplaced modifiers, food waste, and customer dissatisfaction.
- **Kitchen-Floor Disconnect:** Floor staff lack visibility into kitchen prep progress, leading to repeated trips to the kitchen pass.
- **Multi-Branch Management Overhead:** Multi-location brands struggle with centralized menu rollouts, disparate pricing, and fragmented reporting.

### 2.2. Core Value Proposition
- **For Guests:** Instant menu access via QR, real-time item availability, visual modifier selection, and transparent bill tracking.
- **For Waitstaff:** Handheld mobile ordering, instant table transfer, quick modifier selection, and automated service alerts.
- **For Kitchen & Bar:** Digital KDS with intelligent station routing (food to kitchen, drinks to bar), prep timer color-coding, and one-tap item 86ing (marking out-of-stock).
- **For Restaurant Operators:** Centralized menu and branch administration, live floor status, and detailed revenue analytics.
- **For Platform Admins:** Full tenant lifecycle control, organization hierarchy management, and platform billing.

---

## 3. The 5 Product Surfaces

```
+-------------------------------------------------------------------------+
|                       PLATFORM SUPER ADMIN PANEL                        |
|        (Multi-tenant onboarding, tenant billing, system health)         |
+------------------------------------+------------------------------------+
                                     |
                                     v
+-------------------------------------------------------------------------+
|                         RESTAURANT ADMIN PANEL                          |
|         (Menu catalog, branch layout, staff RBAC, reports, printers)    |
+------------------------------------+------------------------------------+
                                     |
          +--------------------------+--------------------------+
          |                                                     |
          v                                                     v
+------------------------+  +------------------------+  +-----------------+
|  QR CUSTOMER WEB APP   |  |   WAITER MOBILE APP    |  |  KITCHEN / BAR  |
| (Table QR, Menu, Cart, |  | (Tables, Quick Order,  |  |       KDS       |
|  Service Call, Bill)   |  |  Table Move, Payment)  |  |  (Station Queues|
|                        |  |                        |  |   & Prep Timer) |
+------------------------+  +------------------------+  +-----------------+
```

### 3.1. Surface 1: QR Customer Web App
- **Form Factor:** Mobile-first, highly responsive web application. No app store installation required.
- **Activation:** Initiated by scanning a dynamic or static table QR code encoding `tenant_id`, `branch_id`, and `table_id`.
- **Key Capabilities:**
  - Dynamic digital menu with high-resolution imagery, allergen tags, and dietary badges.
  - Interactive modifier configuration (e.g., meat doneness, extra sauces, removals).
  - Cart management with live total and tax breakdown.
  - Table-linked order submission into the branch's active order stream.
  - Service requests ("Call Waiter", "Request Wet Wipes", "Request Bill").
  - Live order tracking (Submitted -> Preparing -> Served).

### 3.2. Surface 2: Waiter & Operations Mobile App
- **Form Factor:** Touch-optimized mobile web application designed for smartphones and handheld rugged POS devices.
- **Key Capabilities:**
  - Interactive floor plan with table statuses (Empty, Seated, Order Pending, Served, Bill Requested).
  - Rapid order entry and modifier customization for walk-in guests or assisted ordering.
  - Table management: merge tables, split tables, transfer orders between tables.
  - Real-time waiter notifications (guest service calls, food ready at kitchen pass).
  - POS payment collection integration (cash, external card terminal, or digital split).

### 3.3. Surface 3: Kitchen & Bar KDS (Kitchen Display System)
- **Form Factor:** Landscape tablet and commercial touchscreen display optimized for harsh kitchen environments.
- **Key Capabilities:**
  - Station-specific ticket filtering: Kitchen station displays culinary tickets; Bar station displays beverage tickets.
  - Visual time tracking: Color-coded cards (Green: <10m, Amber: 10–20m, Red: >20m overdue).
  - Ticket progression: "Mark In-Prep" -> "Mark Ready" -> "Recall Ticket".
  - One-tap item 86ing (mark out-of-stock instantly across all customer menus and waiter apps).
  - Audio chimes for new incoming orders and urgent waiter calls.

### 3.4. Surface 4: Restaurant Admin Panel
- **Form Factor:** Desktop and tablet web dashboard for Restaurant Admins and Branch Managers.
- **Key Capabilities:**
  - Menu engineering: categories, items, variant pricing, modifier groups, and combo meals.
  - Branch layout editor: dining areas (Indoor, Terrace, Garden), table numbering, and QR code generation/export.
  - Staff management: user invites, role assignment (RBAC), and PIN-code management for quick mobile login.
  - Hardware configuration: network thermal printer setup (ESC/POS), station routing rules.
  - Operations reporting: sales summaries, peak hour analysis, item popularity, and staff performance.

### 3.5. Surface 5: Platform Super Admin Panel
- **Form Factor:** Desktop web application for platform operators.
- **Key Capabilities:**
  - Tenant lifecycle: organization creation, suspension, custom domain binding.
  - Subscription & billing: tier management, platform commission rate configuration, invoice generation.
  - System-wide audit logging and operational health monitoring.

---

## 4. Architectural & Technology Status

All architecture and technology choices remain **provisional** until validated through formal ADRs:

- **Frontend Technology:** Responsive Web SPA / PWA `[Proposed / ADR Required]`
- **Backend Technology:** Modular Monolith or Microservices `[Proposed / ADR Required]`
- **Realtime Transport:** WebSockets / Server-Sent Events (SSE) `[Proposed / ADR Required]`
- **Database & Multitenancy:** PostgreSQL with row-level security or schema isolation `[Proposed / ADR Required]`
- **Offline / Hardware Proxy:** Local printer bridge service for ESC/POS network printing `[Proposed / ADR Required]`
