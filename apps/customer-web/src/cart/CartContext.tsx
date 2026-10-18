import React, { createContext, useContext, useReducer, useEffect, useState, useMemo } from 'react';
import type { DraftCartLine } from '@restaurant-order/contracts';
import { cartReducer, computeCartTotals, initialCartState } from './cartReducer';
import { loadCartFromStorage, saveCartToStorage, clearCartFromStorage } from './cartStorage';

export interface CartContextValue {
  lines: DraftCartLine[];
  totalItemsCount: number;
  subtotalMinorUnits: number;
  currency: string;
  addLine: (line: DraftCartLine) => void;
  updateQuantity: (signature: string, quantity: number) => void;
  removeLine: (signature: string) => void;
  updateLine: (oldSignature: string, newLine: DraftCartLine) => void;
  clearCart: () => void;
  acceptPriceChanges: (updates: Array<{ signature: string; newUnitPriceMinorUnits: number }>) => void;
  isCartOpen: boolean;
  openCart: () => void;
  closeCart: () => void;
}

const CartContext = createContext<CartContextValue | null>(null);

export interface CartProviderProps {
  sessionId: string | null;
  currency?: string;
  children: React.ReactNode;
}

export const CartProvider: React.FC<CartProviderProps> = ({
  sessionId,
  currency = 'TRY',
  children,
}) => {
  const [state, dispatch] = useReducer(cartReducer, initialCartState, () => {
    if (sessionId) {
      const restored = loadCartFromStorage(sessionId);
      if (restored) return restored;
    }
    return { ...initialCartState, sessionId, currency };
  });

  const [isCartOpen, setIsCartOpen] = useState(false);

  // Synchronize when sessionId prop changes (e.g. session restored, switched, or left)
  useEffect(() => {
    if (sessionId) {
      const stored = loadCartFromStorage(sessionId);
      if (stored) {
        dispatch({ type: 'RESTORE_CART', state: stored });
      } else {
        dispatch({ type: 'SET_SESSION', sessionId, currency });
      }
    } else {
      clearCartFromStorage();
      dispatch({ type: 'CLEAR_CART' });
    }
  }, [sessionId, currency]);

  // Persist non-sensitive cart lines to sessionStorage whenever state changes
  useEffect(() => {
    if (state.sessionId && state.lines.length > 0) {
      saveCartToStorage(state);
    } else if (state.sessionId && state.lines.length === 0) {
      clearCartFromStorage();
    }
  }, [state]);

  const totals = useMemo(() => {
    return computeCartTotals(state.lines);
  }, [state.lines]);

  const value = useMemo<CartContextValue>(() => ({
    lines: state.lines,
    totalItemsCount: totals.totalItemCount,
    subtotalMinorUnits: totals.subtotalMinorUnits,
    currency: state.currency,
    addLine: (line) => dispatch({ type: 'ADD_LINE', line }),
    updateQuantity: (signature, quantity) => dispatch({ type: 'UPDATE_QUANTITY', signature, quantity }),
    removeLine: (signature) => dispatch({ type: 'REMOVE_LINE', signature }),
    updateLine: (oldSignature, newLine) => dispatch({ type: 'UPDATE_LINE', oldSignature, newLine }),
    clearCart: () => {
      dispatch({ type: 'CLEAR_CART' });
      clearCartFromStorage();
    },
    acceptPriceChanges: (updates) => dispatch({ type: 'ACCEPT_PRICE_CHANGES', updates }),
    isCartOpen,
    openCart: () => setIsCartOpen(true),
    closeCart: () => setIsCartOpen(false),
  }), [state.lines, state.currency, totals, isCartOpen]);

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>;
};

export function useCart(): CartContextValue {
  const ctx = useContext(CartContext);
  if (!ctx) {
    throw new Error('useCart must be used within a CartProvider');
  }
  return ctx;
}
