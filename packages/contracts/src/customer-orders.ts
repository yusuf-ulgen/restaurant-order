/**
 * Customer order contracts and DTOs for Phase 8.3/8.4 QR ordering.
 */

export interface CustomerSelectedModifierRequest {
  modifierGroupId: string;
  modifierOptionId: string;
}

export interface CustomerOrderItemRequest {
  menuItemId: string;
  itemVariantId: string | null;
  quantity: number;
  customerNote: string | null;
  selectedModifiers: CustomerSelectedModifierRequest[] | null;
  clientExpectedUnitPriceMinorUnits: number | null;
}

export interface CreateCustomerOrderRequest {
  items: CustomerOrderItemRequest[];
  clientPricingPolicyFingerprint?: string | null;
  clientExpectedTotalMinorUnits?: number | null;
}

export interface CustomerOrderItemModifierDto {
  id: string;
  modifierGroupId: string;
  modifierGroupName: string;
  modifierOptionId: string;
  modifierOptionName: string;
  priceDeltaMinorUnits: number;
}

export interface CustomerOrderItemDto {
  id: string;
  menuItemId: string;
  itemVariantId: string | null;
  itemName: string;
  variantName: string | null;
  quantity: number;
  customerNote: string | null;
  unitPriceMinorUnits: number;
  modifierTotalMinorUnits: number;
  lineTotalMinorUnits: number;
  taxRateBasisPoints: number;
  status: 'Draft' | 'Submitted' | 'InPreparation' | 'Ready' | 'Served' | 'Cancelled' | string;
  modifiers: CustomerOrderItemModifierDto[];
}

export interface CustomerOrderResponseDto {
  id: string;
  tenantId: string;
  branchId: string;
  diningSessionId: string;
  tableId: string;
  roundNumber: number;
  orderSource: 'Customer' | 'Staff' | string;
  status: 'Draft' | 'Submitted' | 'InPreparation' | 'Ready' | 'Served' | 'Cancelled' | string;
  currency: string;
  subtotalMinorUnits: number;
  taxMinorUnits: number;
  serviceChargeMinorUnits: number;
  totalMinorUnits: number;
  idempotencyKey: string;
  concurrencyToken: string;
  createdAtUtc: string;
  submittedAtUtc: string | null;
  items: CustomerOrderItemDto[];
}

export type CustomerOrderErrorCode =
  | 'ITEM_OUT_OF_STOCK'
  | 'ORDER_PRICE_CHANGED'
  | 'INVALID_MODIFIER_SELECTION'
  | 'DUPLICATE_MODIFIER'
  | 'INVALID_VARIANT'
  | 'ITEM_NOT_ACTIVE'
  | 'ORDER_TAKING_DISABLED'
  | 'SESSION_NOT_ACTIVE'
  | 'IDEMPOTENCY_CONFLICT'
  | 'DISTRIBUTED_SECURITY_STATE_UNAVAILABLE'
  | 'CONCURRENCY_CONFLICT'
  | 'CURRENCY_MISMATCH'
  | string;

export interface CustomerOrderProblemDetails {
  type?: string;
  title?: string;
  status: number;
  detail: string;
  instance?: string;
  extensions?: {
    errorCode?: CustomerOrderErrorCode;
    correlationId?: string;
    previousPriceMinorUnits?: number;
    currentPriceMinorUnits?: number;
    menuItemId?: string;
    itemVariantId?: string;
    [key: string]: unknown;
  };
}
