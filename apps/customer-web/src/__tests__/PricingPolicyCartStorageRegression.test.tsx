import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { RuntimeMenuReadModel, DraftCartLine } from '@restaurant-order/contracts';
import { CartProvider, useCart } from '../cart/CartContext';
import { OrderConfirmationSheet } from '../components/cart/OrderConfirmationSheet';
import { customerOrderApi } from '../api/customerOrderApi';
import { loadCartFromStorage, saveCartToStorage } from '../cart/cartStorage';

const policy1 = '0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9';

const baseMenu: RuntimeMenuReadModel = {
  branchId: 'br-1',
  currency: 'TRY',
  pricesIncludeTax: true,
  defaultTaxRateBps: 1000,
  isServiceChargeEnabled: false,
  serviceChargeRateBps: 0,
  pricingPolicyFingerprint: policy1,
  menus: [
    {
      id: 'menu-1',
      name: 'Lezzetler',
      slug: 'lezzetler',
      description: null,
      sortOrder: 1,
      categories: [
        {
          id: 'cat-1',
          name: 'Pizzalar',
          slug: 'pizzalar',
          description: null,
          sortOrder: 1,
          items: [
            {
              id: 'mi-margarita',
              name: 'Margarita Pizza',
              slug: 'margarita-pizza',
              shortDescription: 'Domates ve mozzarella',
              fullDescription: null,
              imageUrl: null,
              basePriceMinorUnits: 25000,
              sortOrder: 1,
              spicyLevel: 0,
              dietaryTags: [],
              allergenTags: [],
              isAvailable: true,
              expectedAvailableAtUtc: null,
              variants: [],
              modifierGroups: [],
            },
          ],
        },
      ],
    },
  ],
};

const sampleLine1: DraftCartLine = {
  menuItemId: 'mi-margarita',
  variantId: null,
  selectedModifierOptionIds: [],
  customerNote: null,
  quantity: 1,
  unitPriceMinorUnits: 25000,
  lineTotalMinorUnits: 25000,
  currency: 'TRY',
  configurationSignature: 'mi-margarita:none::',
};

describe('Phase 8 Pricing Policy Cart & Storage Regressions', () => {
  beforeEach(() => {
    window.sessionStorage.clear();
    vi.restoreAllMocks();
  });

  it('preserves re-approval requirement during quantity, note and line edits', () => {
    const CartEditorInspector = () => {
      const cart = useCart();
      return (
        <div>
          <span data-testid="editor-reconf">{String(cart.requiresReconfirmation)}</span>
          <span data-testid="editor-qty">{cart.lines[0]?.quantity ?? 0}</span>
          <span data-testid="editor-note">{cart.lines[0]?.customerNote ?? 'none'}</span>
          <button
            data-testid="btn-init"
            onClick={() => {
              cart.addLine(sampleLine1, policy1);
              cart.requireReconfirmation('conflict');
            }}
          >
            Init
          </button>
          <button
            data-testid="btn-qty"
            onClick={() => cart.updateQuantity(sampleLine1.configurationSignature, 3)}
          >
            Qty
          </button>
          <button
            data-testid="btn-update-line"
            onClick={() =>
              cart.updateLine(sampleLine1.configurationSignature, {
                ...sampleLine1,
                quantity: 3,
                customerNote: 'Çok pişmiş',
                configurationSignature: 'mi-margarita:none::Çok pişmiş',
              })
            }
          >
            Update Line
          </button>
        </div>
      );
    };

    render(
      <CartProvider sessionId="sess-regression-1">
        <CartEditorInspector />
      </CartProvider>
    );

    fireEvent.click(screen.getByTestId('btn-init'));
    expect(screen.getByTestId('editor-reconf').textContent).toBe('true');

    // Miktar değiştir -> reconf hala true
    fireEvent.click(screen.getByTestId('btn-qty'));
    expect(screen.getByTestId('editor-reconf').textContent).toBe('true');
    expect(screen.getByTestId('editor-qty').textContent).toBe('3');

    // Not/satır güncelle -> reconf hala true
    fireEvent.click(screen.getByTestId('btn-update-line'));
    expect(screen.getByTestId('editor-reconf').textContent).toBe('true');
    expect(screen.getByTestId('editor-note').textContent).toBe('Çok pişmiş');
  });

  it('persists and restores policy re-approval requirement via sessionStorage', () => {
    saveCartToStorage({
      sessionId: 'sess-regression-1',
      lines: [sampleLine1],
      currency: 'TRY',
      acceptedPolicyFingerprint: policy1,
      requiresReconfirmation: true,
    });

    const restored = loadCartFromStorage('sess-regression-1');
    expect(restored).not.toBeNull();
    expect(restored?.acceptedPolicyFingerprint).toBe(policy1);
    expect(restored?.requiresReconfirmation).toBe(true);
  });

  it('maintains stable idempotency key on retry and creates new key when payload changes', async () => {
    const capturedKeys: string[] = [];
    vi.spyOn(customerOrderApi, 'submitOrder').mockImplementation(async (_req, key) => {
      capturedKeys.push(key);
      throw new Error('Network timeout');
    });

    window.sessionStorage.setItem(
      'ro_customer_cart_draft',
      JSON.stringify({
        version: 1,
        sessionId: 'sess-regression-1',
        currency: 'TRY',
        lines: [sampleLine1],
        savedAt: Date.now(),
        acceptedPolicyFingerprint: policy1,
        requiresReconfirmation: false,
      })
    );

    const { rerender } = render(
      <CartProvider sessionId="sess-regression-1">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={baseMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    // 1. Submit denemesi (Ağ hatası alır)
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));
    await waitFor(() => {
      expect(capturedKeys).toHaveLength(1);
    });

    // 2. Ağ hatası sonrası tekrar dene (Aynı payload)
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));
    await waitFor(() => {
      expect(capturedKeys).toHaveLength(2);
    });

    // Aynı payload için aynı Idempotency-Key korunmalıdır
    expect(capturedKeys[0]).toBe(capturedKeys[1]);

    // 3. Sepet güncellendiğinde (örneğin miktar/not veya onaylanan yeni politika) yeni key üretilmelidir
    const updatedLine = {
      ...sampleLine1,
      quantity: 2,
      lineTotalMinorUnits: 50000,
    };

    window.sessionStorage.setItem(
      'ro_customer_cart_draft',
      JSON.stringify({
        version: 1,
        sessionId: 'sess-regression-2',
        currency: 'TRY',
        lines: [updatedLine],
        savedAt: Date.now(),
        acceptedPolicyFingerprint: policy1,
        requiresReconfirmation: false,
      })
    );

    rerender(
      <CartProvider sessionId="sess-regression-2">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={baseMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));
    await waitFor(() => {
      expect(capturedKeys).toHaveLength(3);
    });

    // Değişen payload için yeni idempotency key kullanılmalıdır
    expect(capturedKeys[2]).not.toBe(capturedKeys[0]);
  });
});
