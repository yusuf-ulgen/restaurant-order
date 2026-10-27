import {
  computeOrderPayloadSignature,
  DeterministicOrderItemSignature,
} from '@restaurant-order/contracts';

export interface StaffDraftSelectedModifier {
  modifierGroupId: string;
  modifierOptionId: string;
  name: string;
  priceDeltaMinorUnits: number;
}

export interface StaffDraftLine {
  id: string;
  menuItemId: string;
  itemName: string;
  variantId: string | null;
  variantName: string | null;
  selectedModifierOptionIds: string[];
  selectedModifiers: StaffDraftSelectedModifier[];
  quantity: number;
  customerNote: string | null;
  unitPriceMinorUnits: number;
  lineTotalMinorUnits: number;
}

export interface StaffOrderDraft {
  tenantId: string;
  branchId: string;
  sessionId: string;
  staffUserId: string;
  items: StaffDraftLine[];
  idempotencyKey: string;
  lastPayloadSignature: string;
  isPendingSubmission?: boolean;
  updatedAt: number;
}

function generateIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }
  return `staff-ord-${Date.now()}-${Math.random().toString(36).slice(2, 11)}`;
}

export function getStaffDraftStorageKey(
  tenantId: string,
  branchId: string,
  sessionId: string,
  staffUserId: string
): string {
  return `ro_staff_order_draft_${tenantId}_${branchId}_${sessionId}_${staffUserId}`;
}

export function loadStaffOrderDraft(
  tenantId: string,
  branchId: string,
  sessionId: string,
  staffUserId: string
): StaffOrderDraft | null {
  try {
    const key = getStaffDraftStorageKey(tenantId, branchId, sessionId, staffUserId);
    const raw = localStorage.getItem(key);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as StaffOrderDraft;
    if (
      parsed.tenantId !== tenantId ||
      parsed.branchId !== branchId ||
      parsed.sessionId !== sessionId ||
      parsed.staffUserId !== staffUserId
    ) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function saveStaffOrderDraft(draft: StaffOrderDraft): void {
  try {
    const key = getStaffDraftStorageKey(
      draft.tenantId,
      draft.branchId,
      draft.sessionId,
      draft.staffUserId
    );
    localStorage.setItem(key, JSON.stringify(draft));
  } catch {
    // Storage kotası veya erişim hatası durumunda sessizce devam edilir
  }
}

export function clearStaffOrderDraft(
  tenantId: string,
  branchId: string,
  sessionId: string,
  staffUserId: string
): void {
  try {
    const key = getStaffDraftStorageKey(tenantId, branchId, sessionId, staffUserId);
    localStorage.removeItem(key);
  } catch {
    // İhmal edilir
  }
}

/**
 * Computes deterministic signature for the current staff order lines and pricing policy.
 */
export function calculateStaffOrderPayloadSignature(
  items: StaffDraftLine[],
  pricingPolicyFingerprint: string,
  expectedTotalMinorUnits: number
): string {
  const deterministicItems: DeterministicOrderItemSignature[] = items.map((item) => ({
    menuItemId: item.menuItemId,
    itemVariantId: item.variantId,
    quantity: item.quantity,
    customerNote: item.customerNote,
    clientExpectedUnitPriceMinorUnits: item.unitPriceMinorUnits,
    selectedModifiers: item.selectedModifiers.map((m) => ({
      modifierGroupId: m.modifierGroupId,
      modifierOptionId: m.modifierOptionId,
    })),
  }));

  return computeOrderPayloadSignature({
    clientPricingPolicyFingerprint: pricingPolicyFingerprint,
    clientExpectedTotalMinorUnits: expectedTotalMinorUnits,
    items: deterministicItems,
  });
}

/**
 * Ensures idempotency key is preserved across retries of the exact same payload,
 * but refreshed whenever the payload content or pricing changes.
 */
export function resolveStaffOrderIdempotencyKey(
  existingDraft: StaffOrderDraft | null,
  newSignature: string
): { idempotencyKey: string; isReplayAttempt: boolean } {
  if (
    existingDraft &&
    existingDraft.idempotencyKey &&
    existingDraft.lastPayloadSignature === newSignature
  ) {
    return {
      idempotencyKey: existingDraft.idempotencyKey,
      isReplayAttempt: true,
    };
  }

  return {
    idempotencyKey: generateIdempotencyKey(),
    isReplayAttempt: false,
  };
}
