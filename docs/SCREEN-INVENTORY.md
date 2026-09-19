# Screen Inventory across All 5 Surfaces (`docs/SCREEN-INVENTORY.md`)

## 1. Surface 1: QR Customer Web App (Mobile-First Guest App)

| Screen ID | Screen Name | Access Role | Primary Actions | Key Data Displayed |
| :--- | :--- | :--- | :--- | :--- |
| `CUST-01` | **Table Welcome / Landing** | Müşteri | Scan QR, view branch & table info, enter menu. | Branch name, table number, welcome note, active language selector. |
| `CUST-02` | **Digital Menu & Categories** | Müşteri | Browse categories, search items, filter by dietary/allergens. | Category bar, item cards (photo, title, price, 86 badge). |
| `CUST-03` | **Item Customizer Modal** | Müşteri | Select variants, choose required/optional modifiers, add notes. | Base price, modifier groups, dynamic total price, "Add to Cart" CTA. |
| `CUST-04` | **Cart & Order Review** | Müşteri | Adjust quantities, review modifiers, submit order to kitchen. | Line items, selected modifiers, subtotal, tax estimate, submit button. |
| `CUST-05` | **Live Order Tracker** | Müşteri | Track preparation stages, review past orders in session. | Status timeline (Received -> Preparing -> Served), estimated prep time. |
| `CUST-06` | **Service Request Modal** | Müşteri | Request waiter, ask for wet wipes, or request bill. | Action buttons ("Call Waiter", "Request Bill"), request confirmation. |

---

## 2. Surface 2: Waiter & Operations Mobile App (Handheld Web App)

| Screen ID | Screen Name | Access Role | Primary Actions | Key Data Displayed |
| :--- | :--- | :--- | :--- | :--- |
| `WAIT-01` | **Fast PIN Lockscreen** | Garson, Kasa, Müdür | Enter 4-digit PIN, switch user, select branch shift. | Number pad, staff name, branch selector. |
| `WAIT-02` | **Floor Plan & Table Grid** | Garson, Kasa, Müdür | Select table, filter by area (Terrace, Hall), view table status. | Color-coded tables (Empty, Seated, Order Pending, Bill Requested). |
| `WAIT-03` | **Table Session Detail** | Garson, Kasa, Müdür | View placed orders, add items, request bill, move table. | Table number, seated duration, itemized order list, total balance. |
| `WAIT-04` | **Rapid Order Entry** | Garson, Kasa | Fast category tap, modifier selection, custom note, fire order. | Compact menu grid, quick modifier popover, order staging drawer. |
| `WAIT-05` | **Table Transfer / Merge** | Garson, Müdür | Transfer order to another table, merge two tables. | Source table, target table selector, merge confirmation. |
| `WAIT-06` | **Mobile Bill & Settlement**| Garson, Kasa | Select payment method, split bill, record cash/card payment. | Balance due, split by amount or item, tip input, print receipt CTA. |
| `WAIT-07` | **Notification Drawer** | Garson | Acknowledge customer calls, view ready food notifications. | Service call alerts (Table 12: Waiter Call), KDS ready alerts. |

---

## 3. Surface 3: Kitchen & Bar KDS (Station Display System)

| Screen ID | Screen Name | Access Role | Primary Actions | Key Data Displayed |
| :--- | :--- | :--- | :--- | :--- |
| `KDS-01` | **Station Ticket Queue** | Mutfak, Bar, Müdür | Filter station (Kitchen vs Bar), bump ticket (Prep -> Ready). | Grid of order cards with elapsed timers (green/yellow/red). |
| `KDS-02` | **Ticket Detail Card** | Mutfak, Bar | Mark individual items complete, view special allergy notes. | Order #, Table #, item names, modifiers highlighted, waiter name. |
| `KDS-03` | **Recall Tickets Modal** | Mutfak, Bar, Müdür | Review last 20 bumped tickets, restore accidentally cleared ticket.| List of completed tickets with timestamp and bump history. |
| `KDS-04` | **Quick 86 Stockout Modal** | Mutfak, Bar, Müdür | Search item, toggle out-of-stock (86) status with one tap. | Menu item list with active toggle switches. |

---

## 4. Surface 4: Restaurant Admin Panel (Desktop / Tablet Web)

| Screen ID | Screen Name | Access Role | Primary Actions | Key Data Displayed |
| :--- | :--- | :--- | :--- | :--- |
| `ADM-01` | **Operations Dashboard** | Restoran Admini, Müdür | View live sales, active tables, open orders, station prep latency. | KPI cards, live floor widget, hourly sales chart, top sellers. |
| `ADM-02` | **Menu Catalog Editor** | Restoran Admini, Müdür | Create/edit categories, items, prices, descriptions, images. | Category tree, item list, drag-and-drop ordering, stock toggles. |
| `ADM-03` | **Modifier Groups Manager** | Restoran Admini, Müdür | Create modifier groups, set min/max selections, assign to items. | Modifier groups table, linked items, modifier pricing. |
| `ADM-04` | **Floor & Table Layout** | Restoran Admini, Müdür | Create dining areas, add/position tables, assign table numbers. | Visual canvas or grid, table capacity, area tabs. |
| `ADM-05` | **QR Code Generator** | Restoran Admini, Müdür | Generate table QR codes, download print-ready PDF/SVG batch. | Table list, QR preview, batch download CTA, custom branding options. |
| `ADM-06` | **Staff & Roles Directory** | Restoran Admini, Müdür | Invite staff, assign roles, set/reset 4-digit PINs. | User list, assigned roles, active status, branch assignment. |
| `ADM-07` | **Printers & Routing** | Restoran Admini, Müdür | Add ESC/POS network printers, map categories to stations. | Printer IP/port, station mapping (Bar, Kitchen, Cashier), test print. |
| `ADM-08` | **Financial & Z-Reports** | Restoran Admini, Müdür | Export daily sales, end-of-day Z-report, tax summaries. | Daily turnover, payment breakdown (Cash, Card), discount totals. |

---

## 5. Surface 5: Platform Super Admin Panel (Desktop Web)

| Screen ID | Screen Name | Access Role | Primary Actions | Key Data Displayed |
| :--- | :--- | :--- | :--- | :--- |
| `SPAD-01`| **Tenant Directory** | Super Admin | Search, filter, onboard, or suspend restaurant organizations. | Tenant list, subscription status, active branches, creation date. |
| `SPAD-02`| **Tenant Onboarding Form**| Super Admin | Create tenant, assign initial admin, configure custom domains. | Org details, brand name, initial branch, billing plan picker. |
| `SPAD-03`| **Subscription & Billing** | Super Admin | Manage platform tiers, commission rates, view aggregated volume.| Plan pricing, active tenant counts, monthly platform revenue. |
| `SPAD-04`| **Platform Audit Logs** | Super Admin | Filter audit events by tenant, actor, or action type. | Timestamp, tenant ID, actor, event action, IP address. |
| `SPAD-05`| **System Health Monitor** | Super Admin | Check DB latency, realtime socket connections, worker queues. | Status indicators, error rate charts, active connection counters. |
