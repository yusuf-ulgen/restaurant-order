import type { DraftCartLine } from '@restaurant-order/contracts';
import type { CartState } from './cartReducer';

export const CART_STORAGE_KEY = 'ro_customer_cart_draft';
export const CART_STORAGE_SCHEMA_VERSION = 1;

export interface PersistedCartPayload {
  version: number;
  sessionId: string;
  currency: string;
  lines: DraftCartLine[];
  savedAt: number;
}

/**
 * Validates the shape of restored cart line items.
 */
function isValidCartLine(line: unknown): line is DraftCartLine {
  if (!line || typeof line !== 'object') return false;
  const l = line as Record<string, unknown>;

  return (
    typeof l.menuItemId === 'string' &&
    (l.variantId === null || typeof l.variantId === 'string') &&
    Array.isArray(l.selectedModifierOptionIds) &&
    (l.customerNote === null || typeof l.customerNote === 'string') &&
    typeof l.quantity === 'number' &&
    Number.isInteger(l.quantity) &&
    l.quantity >= 1 &&
    typeof l.unitPriceMinorUnits === 'number' &&
    Number.isInteger(l.unitPriceMinorUnits) &&
    l.unitPriceMinorUnits >= 0 &&
    typeof l.lineTotalMinorUnits === 'number' &&
    Number.isInteger(l.lineTotalMinorUnits) &&
    l.lineTotalMinorUnits >= 0 &&
    typeof l.currency === 'string' &&
    typeof l.configurationSignature === 'string'
  );
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
