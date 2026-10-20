import { describe, it, expect, beforeEach } from 'vitest';
import type { DraftCartLine } from '@restaurant-order/contracts';
import {
  cartReducer,
  computeCartTotals,
  initialCartState,
  type CartState,
} from '../cart/cartReducer';
import {
  loadCartFromStorage,
  saveCartToStorage,
  clearCartFromStorage,
  CART_STORAGE_KEY,
  CART_STORAGE_SCHEMA_VERSION,
} from '../cart/cartStorage';

describe('Cart Reducer & Storage Unit Tests', () => {
  const sampleLine1: DraftCartLine = {
    menuItemId: 'item-1',
    variantId: 'var-1',
    selectedModifierOptionIds: ['opt-a', 'opt-b'],
    customerNote: 'Az tuzlu',
    quantity: 2,
    unitPriceMinorUnits: 15000,
    lineTotalMinorUnits: 30000,
    currency: 'TRY',
    configurationSignature: 'item-1:var-1:opt-a,opt-b:Az tuzlu',
  };

  const sampleLine2: DraftCartLine = {
    menuItemId: 'item-2',
    variantId: null,
    selectedModifierOptionIds: [],
    customerNote: null,
    quantity: 1,
    unitPriceMinorUnits: 5000,
    lineTotalMinorUnits: 5000,
    currency: 'TRY',
    configurationSignature: 'item-2:none::',
  };

  describe('cartReducer', () => {
    it('handles ADD_LINE for distinct items', () => {
      let state = cartReducer(initialCartState, { type: 'ADD_LINE', line: sampleLine1 });
      expect(state.lines).toHaveLength(1);
      expect(state.lines[0]?.configurationSignature).toBe(sampleLine1.configurationSignature);

      state = cartReducer(state, { type: 'ADD_LINE', line: sampleLine2 });
      expect(state.lines).toHaveLength(2);
    });

    it('merges quantities when adding line with identical configuration signature', () => {
      let state = cartReducer(initialCartState, { type: 'ADD_LINE', line: sampleLine1 });
      expect(state.lines[0]?.quantity).toBe(2);
      expect(state.lines[0]?.lineTotalMinorUnits).toBe(30000);

      // Add identical item with quantity 3
      state = cartReducer(state, {
        type: 'ADD_LINE',
        line: { ...sampleLine1, quantity: 3, lineTotalMinorUnits: 45000 },
      });

      expect(state.lines).toHaveLength(1);
      expect(state.lines[0]?.quantity).toBe(5);
      expect(state.lines[0]?.lineTotalMinorUnits).toBe(75000); // 15000 * 5
    });

    it('clamps quantity to 99 when adding exceeds maximum', () => {
      let state = cartReducer(initialCartState, {
        type: 'ADD_LINE',
        line: { ...sampleLine1, quantity: 95, lineTotalMinorUnits: 95 * 15000 },
      });

      state = cartReducer(state, {
        type: 'ADD_LINE',
        line: { ...sampleLine1, quantity: 10, lineTotalMinorUnits: 10 * 15000 },
      });

      expect(state.lines[0]?.quantity).toBe(99);
      expect(state.lines[0]?.lineTotalMinorUnits).toBe(99 * 15000);
    });

    it('updates quantity and recalculates line total', () => {
      let state = cartReducer(initialCartState, { type: 'ADD_LINE', line: sampleLine1 });

      state = cartReducer(state, {
        type: 'UPDATE_QUANTITY',
        signature: sampleLine1.configurationSignature,
        quantity: 4,
      });

      expect(state.lines[0]?.quantity).toBe(4);
      expect(state.lines[0]?.lineTotalMinorUnits).toBe(60000);
    });

    it('removes line when updated quantity is zero or negative', () => {
      let state = cartReducer(initialCartState, { type: 'ADD_LINE', line: sampleLine1 });

      state = cartReducer(state, {
        type: 'UPDATE_QUANTITY',
        signature: sampleLine1.configurationSignature,
        quantity: 0,
      });

      expect(state.lines).toHaveLength(0);
    });

    it('removes line via REMOVE_LINE action', () => {
      let state = cartReducer(initialCartState, { type: 'ADD_LINE', line: sampleLine1 });
      state = cartReducer(state, { type: 'ADD_LINE', line: sampleLine2 });
      expect(state.lines).toHaveLength(2);

      state = cartReducer(state, {
        type: 'REMOVE_LINE',
        signature: sampleLine1.configurationSignature,
      });

      expect(state.lines).toHaveLength(1);
      expect(state.lines[0]?.configurationSignature).toBe(sampleLine2.configurationSignature);
    });

    it('clears all lines on CLEAR_CART', () => {
      let state = cartReducer(initialCartState, { type: 'ADD_LINE', line: sampleLine1 });
      state = cartReducer(state, { type: 'CLEAR_CART' });
      expect(state.lines).toHaveLength(0);
    });

    it('clears lines when session ID changes in SET_SESSION', () => {
      let state: CartState = {
        sessionId: 'session-old',
        lines: [sampleLine1],
        currency: 'TRY',
        acceptedPolicyFingerprint: null,
      };

      state = cartReducer(state, { type: 'SET_SESSION', sessionId: 'session-new' });
      expect(state.sessionId).toBe('session-new');
      expect(state.lines).toHaveLength(0);
    });

    it('computes cart totals correctly', () => {
      const totals = computeCartTotals([sampleLine1, sampleLine2]);
      // line 1: qty 2, total 30000
      // line 2: qty 1, total 5000
      expect(totals.totalItemCount).toBe(3);
      expect(totals.subtotalMinorUnits).toBe(35000);
    });
  });

  describe('cartStorage', () => {
    beforeEach(() => {
      window.sessionStorage.clear();
    });

    it('saves and restores cart state for matching session', () => {
      const state: CartState = {
        sessionId: 'session-123',
        lines: [sampleLine1],
        currency: 'TRY',
        acceptedPolicyFingerprint: null,
      };

      saveCartToStorage(state);
      const restored = loadCartFromStorage('session-123');

      expect(restored).not.toBeNull();
      expect(restored?.sessionId).toBe('session-123');
      expect(restored?.lines).toHaveLength(1);
      expect(restored?.lines[0]?.configurationSignature).toBe(sampleLine1.configurationSignature);
    });

    it('clears storage and returns null when session ID does not match', () => {
      const state: CartState = {
        sessionId: 'session-original',
        lines: [sampleLine1],
        currency: 'TRY',
        acceptedPolicyFingerprint: null,
      };

      saveCartToStorage(state);
      const restored = loadCartFromStorage('session-different');

      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when schema version does not match', () => {
      const legacyPayload = {
        version: 999, // incompatible version
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [sampleLine1],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(legacyPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('safely handles corrupted JSON in sessionStorage', () => {
      window.sessionStorage.setItem(CART_STORAGE_KEY, 'not-valid-json{{{');
      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('safely handles invalid line item structure', () => {
      const corruptedLinesPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [{ invalidField: true }],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedLinesPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when selectedModifierOptionIds contains non-string items', () => {
      const corruptedPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [
          {
            ...sampleLine1,
            selectedModifierOptionIds: ['opt-1', 12345], // Contains number
          },
        ],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when quantity is not a safe integer', () => {
      const corruptedPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [
          {
            ...sampleLine1,
            quantity: 1.5, // float
          },
        ],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when unit price is negative or not safe integer', () => {
      const corruptedPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [
          {
            ...sampleLine1,
            unitPriceMinorUnits: -100,
          },
        ],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when lineTotalMinorUnits !== unitPriceMinorUnits * quantity', () => {
      const corruptedPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [
          {
            ...sampleLine1,
            quantity: 2,
            unitPriceMinorUnits: 15000,
            lineTotalMinorUnits: 99999, // Inconsistent mathematical total
          },
        ],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when customerNote exceeds CUSTOMER_NOTE_MAX_LENGTH', () => {
      const corruptedPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [
          {
            ...sampleLine1,
            customerNote: 'x'.repeat(201), // exceeds 200 chars
          },
        ],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when customerNote is not string or null', () => {
      const corruptedPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [
          {
            ...sampleLine1,
            customerNote: 12345, // invalid type
          },
        ],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears storage and returns null when configurationSignature is forged or mismatched', () => {
      const corruptedPayload = {
        version: CART_STORAGE_SCHEMA_VERSION,
        sessionId: 'session-123',
        currency: 'TRY',
        lines: [
          {
            ...sampleLine1,
            configurationSignature: 'FORGED_SIGNATURE_ATTEMPT',
          },
        ],
      };
      window.sessionStorage.setItem(CART_STORAGE_KEY, JSON.stringify(corruptedPayload));

      const restored = loadCartFromStorage('session-123');
      expect(restored).toBeNull();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });

    it('clears cart draft via clearCartFromStorage', () => {
      const state: CartState = {
        sessionId: 'session-123',
        lines: [sampleLine1],
        currency: 'TRY',
        acceptedPolicyFingerprint: null,
      };
      saveCartToStorage(state);
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).not.toBeNull();

      clearCartFromStorage();
      expect(window.sessionStorage.getItem(CART_STORAGE_KEY)).toBeNull();
    });
  });
});
