# State Machines & Entity Lifecycles (`docs/STATE-MACHINES.md`)

## 1. Overview

Deterministic state transitions prevent invalid business states (e.g., paying for an empty table, serving a cancelled item, or cancelling an already prepared dish without authorization). All state mutations must be validated against these state machines.

---

## 2. Table State Machine

```
   [ AVAILABLE ]  <----------------------------------------------------+
         |                                                             |
         | (Guest arrives / QR scanned / Waiter seats)                 |
         v                                                             |
    [ SEATED ]                                                         |
         |                                                             |
         | (Order submitted)                                           |
         v                                                             |
[ ORDER_PENDING ]                                                      |
         |                                                             |
         | (KDS begins prep)                                           |
         v                                                             |
  [ PREPARING ]                                                        |
         |                                                             |
         | (All items served to table)                                 |
         v                                                             |
   [ SERVED ]  <------------------+                                    |
         |                        | (Subsequent order placed)          |
         | (Guest requests bill)  |                                    |
         v                        |                                    |
[ BILL_REQUESTED ] ---------------+                                    |
         |                                                             |
         | (All payments settled)                                      |
         v                                                             |
     [ PAID ]                                                          |
         |                                                             |
         | (Table cleared & sanitized)                                 |
         +-------------------------------------------------------------+
```

| Current State | Event / Action | Next State | Authorized Roles | Invariants & Guards |
| :--- | :--- | :--- | :--- | :--- |
| `AVAILABLE` | `SEAT_GUESTS` | `SEATED` | Garson, Kasa, Müdür, Müşteri | Table must have no active session. |
| `SEATED` | `SUBMIT_ORDER` | `ORDER_PENDING` | Müşteri, Garson, Kasa | Order contains at least 1 valid item. |
| `ORDER_PENDING`| `START_PREPARATION`| `PREPARING` | Mutfak, Bar, System | At least 1 station ticket marked in-prep. |
| `PREPARING` | `MARK_ALL_SERVED` | `SERVED` | Garson, Mutfak, Bar | All station tickets marked `SERVED`. |
| `SERVED` | `REQUEST_BILL` | `BILL_REQUESTED` | Müşteri, Garson, Kasa | Active session has open balance. |
| `SERVED` | `SUBMIT_NEW_ORDER`| `PREPARING` | Müşteri, Garson | Adds new items to existing session. |
| `BILL_REQUESTED`| `SETTLE_PAYMENT`| `PAID` | Kasa, Garson, Müdür | Remaining bill balance must equal `0.00`. |
| `PAID` | `CLEAR_TABLE` | `AVAILABLE` | Garson, Kasa, Müdür | Session closed, table marked ready for guests. |

---

## 3. Order & OrderItem State Machine

```
   [ DRAFT ] ------------------------> [ CANCELLED ]
       |                                     ^
       | (Submit order)                      |
       v                                     |
  [ SUBMITTED ] -----------------------------+ (Cancelled before prep)
       |                                     |
       | (Received by station)               |
       v                                     |
[ IN_PREPARATION ] --------------------------+ (Voided with supervisor PIN)
       |                                     ^
       | (Station marks ready)               |
       v                                     |
    [ READY ] <--- (Recall Ticket)           |
       |                                     |
       | (Delivered to table)                |
       v                                     |
   [ SERVED ] -------------------------------+ (Comped / Voided by Admin)
```

| Current State | Event / Trigger | Next State | Authorized Roles | Notes / Guards |
| :--- | :--- | :--- | :--- | :--- |
| `DRAFT` | `SUBMIT_ORDER` | `SUBMITTED` | Müşteri, Garson, Kasa | Cart validated, prices frozen. |
| `SUBMITTED` | `ACKNOWLEDGE_TICKET` | `IN_PREPARATION`| Mutfak, Bar | Appears on KDS; prep timer starts. |
| `SUBMITTED` | `CANCEL_ITEM` | `CANCELLED` | Müşteri, Garson, Kasa | Safe cancellation before prep starts. |
| `IN_PREPARATION`| `BUMP_READY` | `READY` | Mutfak, Bar | Waiter notified for floor delivery. |
| `IN_PREPARATION`| `SUPERVISOR_VOID` | `CANCELLED` | Restoran Admini, Müdür | Requires supervisor PIN & reason. |
| `READY` | `RECALL_TICKET` | `IN_PREPARATION`| Mutfak, Bar | Accidentally bumped ticket restored. |
| `READY` | `DELIVER_TO_TABLE` | `SERVED` | Garson | Waiter confirms delivery. |
| `SERVED` | `POST_SERVICE_VOID` | `CANCELLED` | Restoran Admini, Müdür | Requires formal audit log & reason. |

---

## 4. Bill & Payment State Machine

```
   [ OPEN ] (New orders aggregate here)
       |
       | (Partial payment received)
       v
[ PARTIALLY_PAID ] <-----+
       |                 | (Additional partial payment)
       |                 +--+
       | (Final balance settled)
       v
 [ FULLY_PAID ]
       |
       | (Supervisor refund authorized)
       v
  [ REFUNDED ]
```

| Current State | Event / Trigger | Next State | Guards & Rules |
| :--- | :--- | :--- | :--- |
| `OPEN` | `RECEIVE_PARTIAL_PAYMENT`| `PARTIALLY_PAID` | Payment amount > 0 and < remaining balance. |
| `OPEN` | `RECEIVE_FULL_PAYMENT` | `FULLY_PAID` | Payment amount equals remaining balance. |
| `PARTIALLY_PAID`| `RECEIVE_REMAINING` | `FULLY_PAID` | Remaining balance reaches 0.00. |
| `FULLY_PAID` | `AUTHORIZE_REFUND` | `REFUNDED` | Requires supervisor PIN, audit trail recorded. |
| `OPEN` | `VOID_SESSION` | `VOIDED` | Only if all orders are cancelled; balance is 0.00. |

---

## 5. Concurrency & State Invariants

1. **Optimistic Locking:** Entity records (`orders`, `table_sessions`, `bills`) must carry a `version` column. State transitions must check `WHERE version = :expected_version`.
2. **Atomic Payment Settlement:** When splitting bills across multiple cards/cash, each transaction is logged independently with a running balance check.
3. **No Phantom Cancellations:** If a customer attempts to cancel an item via QR while the kitchen marks it `IN_PREPARATION`, the kitchen status wins and the customer cancellation is rejected with an explanatory message.
