# Payments, Tips & Platform Commissions (`docs/PAYMENTS-TIPS-COMMISSIONS.md`)

## 1. Payment Processing Overview

The billing subsystem supports multiple payment methods, flexible bill splitting, optional gratuity collection, and platform commission calculations.

---

## 2. Supported Payment Methods

| Method | Type | Description | Status |
| :--- | :--- | :--- | :--- |
| **Cash (Nakit)** | Physical | Collected by cashier or waiter; cash drawer accounting. | Supported |
| **External POS Terminal** | Physical Card | Waiter processes card on standalone bank POS terminal; records auth code in system. | Supported |
| **Integrated Digital Gateway** | Online Card | QR guest pays directly via smartphone gateway (e.g., Stripe, Iyzico). | `[Proposed / ADR Required]` |
| **Room / Tab Charge** | Account | Charged to customer house account or hotel room folio. | `[Proposed / ADR Required]` |

---

## 3. Split Billing Workflows

A bill can be settled in multiple increments using different methods:

```
[ Active Bill: $120.00 ]
           │
           ├─► Payment 1: $40.00 (Cash)        ──► Remaining Balance: $80.00
           │
           ├─► Payment 2: $40.00 (Card - POS)  ──► Remaining Balance: $40.00
           │
           └─► Payment 3: $40.00 (Card - POS)  ──► Remaining Balance: $0.00 (Bill Closed)
```

### 3.1. Split Modes
1. **Split by Amount:** The customer specifies an arbitrary amount to pay toward the balance.
2. **Equal Split:** The system divides the balance equally across $N$ guests:
   $$\text{Guest Share} = \frac{\text{Total Balance}}{N}$$
   Rounding discrepancies (e.g., $\$100.00 / 3 = \$33.33 \times 3 + \$0.01$) are allocated to the final payment.
3. **Split by Item:** Specific order items are selected and settled. Remaining items remain open on the bill.

---

## 4. Tip & Gratuity Architecture

### 4.1. Tip Collection Models
- **Percentage Presets:** QR app and mobile payment screens offer quick percentage selections (e.g., 5%, 10%, 15%, 20%) alongside a "Custom Tip" field.
- **Allocation Strategies:**
  1. **Direct-to-Server:** Tips are attributed directly to the primary waiter assigned to the table.
  2. **Pooled (Tronc):** All tips collected in a shift are pooled and distributed across kitchen, bar, and floor staff based on hours worked.
- **Reporting:** Daily Z-reports and staff shift summaries separate base sales from tips to simplify payroll and tax accounting.

---

## 5. Platform Commission Engine

For digital transactions processed via platform payment gateways, platform fees are calculated and tracked:

$$\text{Platform Fee} = (\text{Transaction Amount} \times \text{Commission Rate \%}) + \text{Fixed Fee Per Order}$$

### 5.1. Ledger & Settlement
- Every settled digital payment generates a dual-entry ledger record:
  - `credit`: Tenant Payable Account (Net amount = Gross - Commission).
  - `credit`: Platform Revenue Account (Commission fee).
- Commissions are settled weekly or monthly based on the tenant's subscription agreement.

---

## 6. Refunds & Void Governance

- **Refund Authorization:** Refunds require supervisor PIN validation. Floor waitstaff cannot execute refunds.
- **Partial vs. Full Refunds:**
  - A refund can apply to the entire payment or a specific line item.
  - The refund amount cannot exceed the original transaction total.
- **Audit Logging:** Every void and refund writes an immutable audit record containing:
  `{ timestamp, tenant_id, branch_id, bill_id, supervisor_id, reason, original_amount, refund_amount }`
