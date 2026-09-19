# Order Routing & ESC/POS Network Printing (`docs/ORDER-ROUTING-AND-PRINTING.md`)

## 1. Station Routing Architecture

When an order containing both food and beverages is placed, the system automatically splits the order into dedicated station tickets based on menu item category mappings:

```
[ Customer / Waiter Order: 2x Burgers, 1x Pasta, 2x Cocktails ]
                            │
              ┌─────────────┴─────────────┐
              ▼                           ▼
    [ KITCHEN STATION ]            [ BAR STATION ]
    - 2x Burgers                   - 2x Cocktails
    - 1x Pasta                                │
              │                               │
       ┌──────┴──────┐                 ┌──────┴──────┐
       ▼             ▼                 ▼             ▼
  Kitchen KDS   Kitchen ESC/POS     Bar KDS       Bar ESC/POS
    Display         Printer         Display         Printer
```

---

## 2. ESC/POS Thermal Printing Protocol

Network thermal printers communicate over raw TCP sockets, typically on port `9100`.

### 2.1. Standard ESC/POS Command Sequences
- **Initialize Printer:** `ESC @` (`\x1B\x40`)
- **Text Alignment:** `ESC a n` (`\x1B\x61\x00` left, `\x01` center, `\x02` right)
- **Text Emphasis (Bold):** `ESC E n` (`\x1B\x45\x01` on, `\x00` off)
- **Character Sizing (Double Width/Height):** `GS ! n` (`\x1D\x21\x11`)
- **Line Feed & Cut Paper:** `GS V m` (`\x1D\x56\x41\x03` full cut with 3-line feed)

### 2.2. Print Ticket Anatomy
A standard kitchen ticket contains:
1. **Header:** Branch Name, Station Name (KITCHEN / BAR), Order Number, Table Number.
2. **Meta:** Timestamp, Waiter Name, Order Round (e.g., Round 2).
3. **Item Lines:** Quantity, Item Name (Bold), Modifiers indented (e.g., *No Onions*, *Medium Rare*).
4. **Special Notes:** Customer/Waiter notes printed with highlighted borders.
5. **Footer:** Barcode / QR code of the `order_id` for quick scanner lookup.

---

## 3. Printing Architecture Options

| Architecture Model | Description | Pros | Cons | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Model 1: Direct Cloud-to-Printer TCP** | Cloud backend opens direct TCP socket to public/VPN printer IP. | No local software needed. | Requires static public IPs or VPN tunnels per branch; firewall hurdles. | `[Alternative / ADR]` |
| **Model 2: Local Branch Print Bridge** | Lightweight daemon running on a local mini-PC/Raspberry Pi inside branch LAN. | Secure, no public IP needed; polls or receives local websocket commands. | Requires a local host machine in each branch. | `[Proposed / ADR Required]` |
| **Model 3: Browser Web-Print API** | Client app prints via browser print dialog. | Zero setup. | Inefficient; cannot silently trigger kitchen printers from customer phones. | Discarded |

---

## 4. Spooler Resilience & Error Handling

1. **Non-Blocking Execution:** Thermal printer failures **must never** delay database order commitment or KDS ticket display.
2. **Spooler State Machine:**
   - `PENDING` -> `SENDING` -> `SUCCESS`
   - If socket error or timeout (>3000ms): -> `FAILED_RETRYING`
   - Exponential backoff retry (3 attempts over 60 seconds).
   - If all retries fail: -> `OFFLINE_ALERT`.
3. **Manual Reprint Action:** Waiters and Cashiers can tap "Reprint Ticket" from the Table Detail screen at any time to re-queue the raw ESC/POS payload.
