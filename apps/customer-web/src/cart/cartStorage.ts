import {
  type DraftCartLine,
  CUSTOMER_NOTE_MAX_LENGTH,
  CART_STORAGE_KEY,
  CART_STORAGE_SCHEMA_VERSION,
} from '@restaurant-order/contracts';
import type { CartState } from './cartReducer';
import { generateConfigurationSignature } from '../utils/customizationUtils';

export { CART_STORAGE_KEY, CART_STORAGE_SCHEMA_VERSION };

export interface PersistedCartPayload {
  version: number;
  sessionId: string;
  currency: string;
  lines: DraftCartLine[];
  savedAt: number;
}

/**
 * Validates the shape and integrity of restored cart line items.
 * Strictly verifies modifier string array, safe integers, mathematical consistency,
 * customer note bounds, and canonical configuration signature.
 */
function isValidCartLine(line: unknown): line is DraftCartLine {
  if (!line || typeof line !== 'object') return false;
  const l = line as Record<string, unknown>;

  // Basic types
  if (typeof l.menuItemId !== 'string' || l.menuItemId.trim() === '') return false;
  if (l.variantId !== null && typeof l.variantId !== 'string') return false;
  if (typeof l.currency !== 'string' || l.currency.trim() === '') return false;
  if (typeof l.configurationSignature !== 'string') return false;

  // Selected modifiers must be an array composed strictly of strings
  if (!Array.isArray(l.selectedModifierOptionIds)) return false;
  if (!l.selectedModifierOptionIds.every((id) => typeof id === 'string' && id.trim() !== '')) {
    return false;
  }

  // Customer note length and type
  if (l.customerNote !== null) {
    if (typeof l.customerNote !== 'string') return false;
    if (l.customerNote.length > CUSTOMER_NOTE_MAX_LENGTH) return false;
  }

  // Quantities and prices must be safe integers
  if (
    typeof l.quantity !== 'number' ||
    !Number.isSafeInteger(l.quantity) ||
    l.quantity < 1 ||
    l.quantity > 99
  ) {
    return false;
  }

  if (
    typeof l.unitPriceMinorUnits !== 'number' ||
    !Number.isSafeInteger(l.unitPriceMinorUnits) ||
    l.unitPriceMinorUnits < 0
  ) {
    return false;
  }

  if (
    typeof l.lineTotalMinorUnits !== 'number' ||
    !Number.isSafeInteger(l.lineTotalMinorUnits) ||
    l.lineTotalMinorUnits < 0
  ) {
    return false;
  }

  // Exact mathematical integrity: lineTotal === unitPrice * quantity
  if (l.lineTotalMinorUnits !== l.unitPriceMinorUnits * l.quantity) {
    return false;
  }

  // Canonical configuration signature verification
  const expectedSig = generateConfigurationSignature({
    menuItemId: l.menuItemId,
    variantId: l.variantId as string | null,
    selectedModifierOptionIds: l.selectedModifierOptionIds as string[],
    customerNote: l.customerNote as string | null,
  });

  return l.configurationSignature === expectedSig;
}

/**
 * Loads and validates a persisted cart draft from sessionStorage for the active session.
 * Safely removes invalid, corrupted, old schema, or mismatched session drafts.
 */
export function loadCartFromStorage(activeSessionId: string): CartState | null {
  if (!activeSessionId) return null;

  try {
    if (typeof window === 'undefined' || !window.sessionStorage) {
      return null;
    }

    const raw = window.sessionStorage.getItem(CART_STORAGE_KEY);
    if (!raw) return null;

    const parsed = JSON.parse(raw) as unknown;
    if (!parsed || typeof parsed !== 'object') {
      window.sessionStorage.removeItem(CART_STORAGE_KEY);
      return null;
    }

    const payload = parsed as Record<string, unknown>;

    // Schema version check
    if (payload.version !== CART_STORAGE_SCHEMA_VERSION) {
      window.sessionStorage.removeItem(CART_STORAGE_KEY);
      return null;
    }

    // Session binding check
    if (payload.sessionId !== activeSessionId) {
      window.sessionStorage.removeItem(CART_STORAGE_KEY);
      return null;
    }

    // Lines array check
    if (!Array.isArray(payload.lines)) {
      window.sessionStorage.removeItem(CART_STORAGE_KEY);
      return null;
    }

    const validatedLines: DraftCartLine[] = [];
    for (const item of payload.lines) {
      if (!isValidCartLine(item)) {
        // Manipulated or broken line item; fail safe by clearing storage
        window.sessionStorage.removeItem(CART_STORAGE_KEY);
        return null;
      }
      validatedLines.push(item);
    }

    return {
      sessionId: activeSessionId,
      lines: validatedLines,
      currency: typeof payload.currency === 'string' ? payload.currency : 'TRY',
    };
  } catch {
    // Memory fallback: if sessionStorage fails, clean up if possible and return null
    try {
      window.sessionStorage.removeItem(CART_STORAGE_KEY);
    } catch {
      // Ignore quota or security errors
    }
    return null;
  }
}

/**
 * Persists the non-sensitive cart state into sessionStorage.
 * Never stores auth tokens, customer names, phones, or PII.
 */
export function saveCartToStorage(state: CartState): void {
  if (!state.sessionId) return;

  if (!state.lines || state.lines.length === 0) {
    clearCartFromStorage();
    return;
  }

  try {
    if (typeof window === 'undefined' || !window.sessionStorage) {
      return;
    }

    const payload: PersistedCartPayload = {
      version: CART_STORAGE_SCHEMA_VERSION,
      sessionId: state.sessionId,
      currency: state.currency,
      lines: state.lines,
      savedAt: Date.now(),
    };

    window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(payload));
  } catch {
    // Graceful fallback: storage full or private browsing quota exceeded; operates in-memory
  }
}

/**
 * Clears any persisted cart draft from storage.
 */
export function clearCartFromStorage(): void {
  try {
    if (typeof window !== 'undefined' && window.sessionStorage) {
      window.sessionStorage.removeItem(CART_STORAGE_KEY);
    }
  } catch {
    // Ignore storage errors
  }
}
