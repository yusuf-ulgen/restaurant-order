/**
 * Selected modifier option in a staff order creation request.
 */
export interface StaffSelectedModifierRequest {
  modifierGroupId: string;
  modifierOptionId: string;
}

/**
 * Single line item in a staff order creation request.
 */
export interface StaffOrderItemRequest {
  menuItemId: string;
  itemVariantId?: string | null;
  quantity: number;
  customerNote?: string | null;
  selectedModifiers?: StaffSelectedModifierRequest[] | null;
  clientExpectedUnitPriceMinorUnits?: number | null;
}

/**
 * Payload for creating a staff round of ordering.
 */
export interface CreateStaffOrderRequest {
  items: StaffOrderItemRequest[];
  clientPricingPolicyFingerprint: string;
  clientExpectedTotalMinorUnits: number;
}

/**
 * Payload for cancelling or voiding an order item.
 */
export interface CancelOrderItemRequest {
  reason: string;
  supervisorPin?: string | null;
  supervisorUserId?: string | null;
  concurrencyToken?: string | null;
}

/**
 * Modifier snapshot in staff order views.
 */
export interface StaffOrderItemModifierDto {
  id: string;
  modifierGroupId: string;
  modifierGroupName: string;
  modifierOptionId: string;
  modifierOptionName: string;
  priceDeltaMinorUnits: number;
}

/**
 * Order item in staff order views.
 */
export interface StaffOrderItemDto {
  id: string;
  menuItemId: string;
  itemVariantId?: string | null;
  itemName: string;
  variantName?: string | null;
  quantity: number;
  customerNote?: string | null;
  preparationStationId?: string | null;
  unitPriceMinorUnits: number;
  modifierTotalMinorUnits: number;
  lineTotalMinorUnits: number;
  taxRateBasisPoints: number;
  status: string;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
  modifiers: StaffOrderItemModifierDto[];
}

/**
 * Staff-facing order response DTO.
 */
export interface StaffOrderResponseDto {
  id: string;
  tenantId: string;
  branchId: string;
  diningSessionId: string;
  tableId: string;
  roundNumber: number;
  orderSource: string;
  status: string;
  currency: string;
  subtotalMinorUnits: number;
  taxMinorUnits: number;
  serviceChargeMinorUnits: number;
  totalMinorUnits: number;
  idempotencyKey: string;
  concurrencyToken: string;
  createdAtUtc: string;
  submittedAtUtc?: string | null;
  updatedAtUtc?: string | null;
  cancellationReason?: string | null;
  cancelledByActorId?: string | null;
  items: StaffOrderItemDto[];
}

/**
 * Result of creating a staff order indicating replay state.
 */
export interface CreateStaffOrderResult {
  order: StaffOrderResponseDto;
  isReplay: boolean;
}
