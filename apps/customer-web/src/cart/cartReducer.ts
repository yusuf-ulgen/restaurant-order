import type { DraftCartLine } from '@restaurant-order/contracts';

export interface CartState {
  sessionId: string | null;
  lines: DraftCartLine[];
  currency: string;
  acceptedPolicyFingerprint: string | null;
  requiresReconfirmation?: boolean;
}

export const initialCartState: CartState = {
  sessionId: null,
  lines: [],
  currency: 'TRY',
  acceptedPolicyFingerprint: null,
  requiresReconfirmation: false,
};

export type CartAction =
  | { type: 'SET_SESSION'; sessionId: string; currency?: string }
  | { type: 'ADD_LINE'; line: DraftCartLine; policyFingerprint?: string }
  | { type: 'UPDATE_QUANTITY'; signature: string; quantity: number }
  | { type: 'REMOVE_LINE'; signature: string }
  | { type: 'UPDATE_LINE'; oldSignature: string; newLine: DraftCartLine }
  | { type: 'CLEAR_CART' }
  | { type: 'RESTORE_CART'; state: CartState }
  | {
      type: 'ACCEPT_PRICE_CHANGES';
      updates: Array<{ signature: string; newUnitPriceMinorUnits: number }>;
      policyFingerprint?: string;
    }
  | { type: 'SET_ACCEPTED_POLICY'; policyFingerprint: string }
  | { type: 'REQUIRE_RECONFIRMATION'; reason?: string };

export function cartReducer(state: CartState, action: CartAction): CartState {
  switch (action.type) {
    case 'SET_SESSION': {
      // If session ID changes, safely clear old cart lines
      if (state.sessionId && state.sessionId !== action.sessionId) {
        return {
          sessionId: action.sessionId,
          lines: [],
          currency: action.currency || state.currency,
          acceptedPolicyFingerprint: null,
          requiresReconfirmation: false,
        };
      }
      return {
        ...state,
        sessionId: action.sessionId,
        currency: action.currency || state.currency,
      };
    }

    case 'ADD_LINE': {
      const newLine = action.line;
      const incomingPolicy = action.policyFingerprint;

      let nextAcceptedPolicy = state.acceptedPolicyFingerprint;
      let nextRequiresReconfirmation = Boolean(state.requiresReconfirmation);

      if (state.lines.length === 0) {
        nextAcceptedPolicy = incomingPolicy ?? null;
        nextRequiresReconfirmation = !incomingPolicy;
      } else {
        if (incomingPolicy && state.acceptedPolicyFingerprint) {
          if (incomingPolicy !== state.acceptedPolicyFingerprint) {
            nextRequiresReconfirmation = true;
          }
        } else if (!state.acceptedPolicyFingerprint) {
          nextRequiresReconfirmation = true;
        }
      }

      const existingIndex = state.lines.findIndex(
        (l) => l.configurationSignature === newLine.configurationSignature
      );

      if (existingIndex >= 0) {
        const existingLine = state.lines[existingIndex]!;
        const updatedQuantity = Math.min(99, existingLine.quantity + newLine.quantity);
        const updatedLineTotal = existingLine.unitPriceMinorUnits * updatedQuantity;

        const updatedLines = [...state.lines];
        updatedLines[existingIndex] = {
          ...existingLine,
          quantity: updatedQuantity,
          lineTotalMinorUnits: updatedLineTotal,
        };

        return {
          ...state,
          lines: updatedLines,
          acceptedPolicyFingerprint: nextAcceptedPolicy,
          requiresReconfirmation: nextRequiresReconfirmation,
        };
      }

      // Append new distinct line
      return {
        ...state,
        lines: [...state.lines, newLine],
        currency: newLine.currency || state.currency,
        acceptedPolicyFingerprint: nextAcceptedPolicy,
        requiresReconfirmation: nextRequiresReconfirmation,
      };
    }

    case 'UPDATE_QUANTITY': {
      const { signature, quantity } = action;

      if (quantity <= 0) {
        const remainingLines = state.lines.filter((l) => l.configurationSignature !== signature);
        return {
          ...state,
          lines: remainingLines,
          acceptedPolicyFingerprint: remainingLines.length === 0 ? null : state.acceptedPolicyFingerprint,
          requiresReconfirmation: remainingLines.length === 0 ? false : state.requiresReconfirmation,
        };
      }

      const clampedQty = Math.min(99, Math.max(1, Math.floor(quantity)));

      return {
        ...state,
        lines: state.lines.map((line) => {
          if (line.configurationSignature !== signature) return line;
          return {
            ...line,
            quantity: clampedQty,
            lineTotalMinorUnits: line.unitPriceMinorUnits * clampedQty,
          };
        }),
      };
    }

    case 'REMOVE_LINE': {
      const remainingLines = state.lines.filter((l) => l.configurationSignature !== action.signature);
      return {
        ...state,
        lines: remainingLines,
        acceptedPolicyFingerprint: remainingLines.length === 0 ? null : state.acceptedPolicyFingerprint,
        requiresReconfirmation: remainingLines.length === 0 ? false : state.requiresReconfirmation,
      };
    }

    case 'UPDATE_LINE': {
      const { oldSignature, newLine } = action;
      const targetIndex = state.lines.findIndex((l) => l.configurationSignature === oldSignature);

      if (targetIndex < 0) {
        return cartReducer(state, { type: 'ADD_LINE', line: newLine });
      }

      // Check if another existing line has the same signature as the new updated line
      const duplicateIndex = state.lines.findIndex(
        (l, idx) => idx !== targetIndex && l.configurationSignature === newLine.configurationSignature
      );

      if (duplicateIndex >= 0) {
        // Merge into the duplicate line and remove old line
        const existingDuplicate = state.lines[duplicateIndex]!;
        const mergedQty = Math.min(99, existingDuplicate.quantity + newLine.quantity);
        const mergedTotal = existingDuplicate.unitPriceMinorUnits * mergedQty;

        const filtered = state.lines.filter((_, idx) => idx !== targetIndex);
        return {
          ...state,
          lines: filtered.map((l) =>
            l.configurationSignature === existingDuplicate.configurationSignature
              ? { ...l, quantity: mergedQty, lineTotalMinorUnits: mergedTotal }
              : l
          ),
        };
      }

      const updatedLines = [...state.lines];
      updatedLines[targetIndex] = newLine;
      return { ...state, lines: updatedLines };
    }

    case 'CLEAR_CART': {
      return {
        ...state,
        lines: [],
        acceptedPolicyFingerprint: null,
        requiresReconfirmation: false,
      };
    }

    case 'RESTORE_CART': {
      return {
        ...action.state,
      };
    }

    case 'ACCEPT_PRICE_CHANGES': {
      const updateMap = new Map(action.updates.map((u) => [u.signature, u.newUnitPriceMinorUnits]));
      const updatedLines = state.lines.map((line) => {
        const newUnitPrice = updateMap.get(line.configurationSignature);
        if (newUnitPrice === undefined) return line;
        return {
          ...line,
          unitPriceMinorUnits: newUnitPrice,
          lineTotalMinorUnits: newUnitPrice * line.quantity,
        };
      });
      return {
        ...state,
        lines: updatedLines,
        acceptedPolicyFingerprint: action.policyFingerprint !== undefined
          ? action.policyFingerprint
          : state.acceptedPolicyFingerprint,
        requiresReconfirmation: false,
      };
    }

    case 'SET_ACCEPTED_POLICY': {
      return {
        ...state,
        acceptedPolicyFingerprint: action.policyFingerprint,
        requiresReconfirmation: false,
      };
    }

    case 'REQUIRE_RECONFIRMATION': {
      return {
        ...state,
        requiresReconfirmation: true,
      };
    }

    default:
      return state;
  }
}

export function computeCartTotals(lines: DraftCartLine[]): {
  totalItemCount: number;
  subtotalMinorUnits: number;
} {
  let totalItemCount = 0;
  let subtotalMinorUnits = 0;

  for (const line of lines) {
    totalItemCount += line.quantity;
    subtotalMinorUnits += line.lineTotalMinorUnits;
  }

  if (!Number.isSafeInteger(subtotalMinorUnits)) {
    throw new Error('Sepet toplam tutarı güvenli sayısal sınırları aştı.');
  }

  return {
    totalItemCount,
    subtotalMinorUnits,
  };
}
