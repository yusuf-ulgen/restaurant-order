# Realtime Events & Notifications (`docs/REALTIME-AND-NOTIFICATIONS.md`)

## 1. Overview & Transport Strategy

Sub-second real-time communication connects customer smartphones, waiter handhelds, kitchen/bar display systems, and administrative dashboards.

### 1.1. Realtime Transport Comparison

| Criteria | WebSockets (WS) | Server-Sent Events (SSE) |
| :--- | :--- | :--- |
| **Communication** | Full Duplex (Bi-directional) | Simplex (Server to Client only) |
| **Protocol** | `ws://` / `wss://` | Standard HTTP/2 or HTTP/1.1 |
| **Firewall / Proxy** | Sometimes blocked or dropped by aggressive proxies | Passes cleanly through standard HTTP proxies |
| **Best Fit In System**| Interactive KDS and Waiter handhelds | Customer QR status stream & Read-Only Dashboards |
| **Status** | `[Proposed / ADR Required]` | `[Proposed / ADR Required]` |

---

## 2. Event Taxonomy & Schemas

All event payloads are formatted as JSON and adhere to a standardized event envelope:

```json
{
  "event_id": "evt_01HXYZ...",
  "event_type": "order.created",
  "timestamp": "2026-09-19T14:55:00Z",
  "tenant_id": "org_abc123",
  "branch_id": "brn_xyz789",
  "data": {}
}
```

### 2.1. Core System Events

| Event Type | Producer | Consumers | Payload Highlights |
| :--- | :--- | :--- | :--- |
| `order.created` | Customer QR, Waiter | KDS, Cashier, Admin | `order_id`, `table_id`, items, modifiers, round. |
| `order.item_status_changed`| KDS, Waiter | Customer QR, Waiter | `order_id`, `item_id`, `new_status` (`IN_PREP`, `READY`). |
| `table.service_requested` | Customer QR | Waiter App | `table_id`, `request_type` (`WAITER`, `WIPES`, `BILL`). |
| `kds.ticket_bumped` | KDS | Waiter App | `ticket_id`, `table_id`, station (`KITCHEN`/`BAR`). |
| `menu.item_86ed` | KDS, Admin | Customer QR, Waiter | `item_id`, `is_available: false`. |
| `table.session_closed` | Cashier, Waiter | Customer QR, Admin | `table_id`, `session_id`, `settled_at`. |

---

## 3. Channel & Room Partitioning

To guarantee tenant isolation and bandwidth efficiency, clients join isolated rooms:

- **Customer QR Room:** `tenant:{tenant_id}:branch:{branch_id}:table:{table_id}`
  - Receives only updates relevant to the current dining table.
- **Kitchen KDS Room:** `tenant:{tenant_id}:branch:{branch_id}:station:kitchen`
  - Receives food prep tickets and kitchen 86 events.
- **Bar KDS Room:** `tenant:{tenant_id}:branch:{branch_id}:station:bar`
  - Receives beverage prep tickets and bar 86 events.
- **Floor Staff Room:** `tenant:{tenant_id}:branch:{branch_id}:floor`
  - Receives service calls, food ready alerts, and table status changes.

---

## 4. Audio Chimes & Notification UX

- **KDS Audio Chime:** A distinct high-frequency audio chime plays on KDS tablets when a new ticket arrives, ensuring cooks hear the order even in a noisy environment.
- **Waiter Vibration & Alert:** Waiter mobile devices vibrate and display a persistent banner upon customer service requests ("Table 14 - Waiter Call").
- **Automatic Reconnection:**
  - Clients implement an exponential backoff reconnect policy (1s, 2s, 4s, up to 15s max).
  - While disconnected, clients display a non-blocking offline pill.
  - Upon reconnection, clients fetch a missed-events delta via an HTTP synchronization endpoint.
