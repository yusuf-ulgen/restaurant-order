import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { RuntimeMenuReadModel, DraftCartLine } from '@restaurant-order/contracts';
import { CartProvider } from '../cart/CartContext';
import { CartSheet } from '../components/cart/CartSheet';
import { OrderConfirmationSheet } from '../components/cart/OrderConfirmationSheet';
import { customerOrderApi, CustomerOrderApiError } from '../api/customerOrderApi';

describe('Phase 8 Price Change UX & Idempotency Tests', () => {
  const sampleRuntimeMenu: RuntimeMenuReadModel = {
    branchId: 'branch-1',
    currency: 'TRY',
    pricesIncludeTax: true,
    defaultTaxRateBps: 1000,
    isServiceChargeEnabled: false,
    serviceChargeRateBps: 0,
    pricingPolicyFingerprint: '0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9',
    menus: [
      {
        id: 'menu-1',
        name: 'Ana Menü',
        slug: 'ana-menu',
        description: null,
        sortOrder: 1,
        categories: [
          {
            id: 'cat-1',
            name: 'Burgerler',
            slug: 'burgerler',
            description: null,
            sortOrder: 1,
            items: [
              {
                id: 'item-burger',
                name: 'Klasik Burger',
                slug: 'klasik-burger',
                shortDescription: null,
                fullDescription: null,
                imageUrl: null,
                basePriceMinorUnits: 18000, // Price increased from 15000 to 18000
                sortOrder: 1,
                spicyLevel: 0,
                dietaryTags: [],
                allergenTags: [],
                isAvailable: true,
                expectedAvailableAtUtc: null,
                variants: [
                  {
                    id: 'var-single',
                    name: 'Tek Köfte',
                    code: 'SNG',
                    absolutePriceMinorUnits: 18000,
                    sortOrder: 1,
                    isDefault: true,
                    isAvailable: true,
                    expectedAvailableAtUtc: null,
                  },
                ],
                modifierGroups: [
                  {
                    id: 'group-sauce',
                    name: 'Sos',
                    minSelections: 0,
                    maxSelections: 2,
                    sortOrder: 1,
                    options: [
                      {
                        id: 'opt-mayo',
                        name: 'Mayonez',
                        priceDeltaMinorUnits: 1000, // Modifier delta 1000
                        sortOrder: 1,
                        isDefault: true,
                      },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      },
    ],
  };

  // Cart line at OLD price: base 15000 + mod 1000 = 16000. Qty 2 = 32000 total.
  // Runtime menu price: base 18000 + mod 1000 = 19000.
  const oldPriceLine: DraftCartLine = {
    menuItemId: 'item-burger',
    variantId: 'var-single',
    selectedModifierOptionIds: ['opt-mayo'],
    customerNote: 'Tuzsuz olsun',
    quantity: 2,
    unitPriceMinorUnits: 16000,
    lineTotalMinorUnits: 32000,
    currency: 'TRY',
    configurationSignature: 'item-burger:var-single:opt-mayo:Tuzsuz olsun',
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    window.sessionStorage.clear();
  });

  it('price change blocks checkout CTA and displays old and new prices', () => {
    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [oldPriceLine],
      savedAt: Date.now(),
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    render(
      <CartProvider sessionId="test-session">
        <CartSheet isOpen={true} onClose={vi.fn()} runtimeMenu={sampleRuntimeMenu} />
      </CartProvider>
    );

    const ctaBtn = screen.getByTestId('submit-order-cta-btn');
    expect(ctaBtn).toHaveProperty('disabled', true);
    expect(screen.getByTestId('cart-staleness-warning')).toBeDefined();
    expect(screen.getAllByText(/160,00.*190,00/).length).toBeGreaterThan(0);
    expect(screen.getByTestId('accept-price-changes-btn')).toBeDefined();
  });

  it('does not apply new price silently without user confirmation', () => {
    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [oldPriceLine],
      savedAt: Date.now(),
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    render(
      <CartProvider sessionId="test-session">
        <CartSheet isOpen={true} onClose={vi.fn()} runtimeMenu={sampleRuntimeMenu} />
      </CartProvider>
    );

    // Old subtotal is still 320.00 TL
    expect(screen.getByTestId('cart-subtotal-display').textContent).toContain('320,00');
    // Button is disabled
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);
  });

  it('user confirmation updates line and total, preserves notes/modifiers/qty, and updates sessionStorage', async () => {
    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [oldPriceLine],
      savedAt: Date.now(),
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    render(
      <CartProvider sessionId="test-session">
        <CartSheet isOpen={true} onClose={vi.fn()} runtimeMenu={sampleRuntimeMenu} />
      </CartProvider>
    );

    const acceptBtn = screen.getByTestId('accept-price-changes-btn');
    fireEvent.click(acceptBtn);

    // After accepting: line price becomes 19000, line total 38000 (380.00 TL)
    await waitFor(() => {
      expect(screen.getByTestId('cart-subtotal-display').textContent).toContain('380,00');
      expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', false);
      expect(screen.queryByTestId('cart-staleness-warning')).toBeNull();
    });

    // Check sessionStorage persistence
    const saved = JSON.parse(window.sessionStorage.getItem('ro_customer_cart_draft') || '{}');
    expect(saved.lines[0].unitPriceMinorUnits).toBe(19000);
    expect(saved.lines[0].lineTotalMinorUnits).toBe(38000);
    expect(saved.lines[0].quantity).toBe(2);
    expect(saved.lines[0].customerNote).toBe('Tuzsuz olsun');
    expect(saved.lines[0].selectedModifierOptionIds).toEqual(['opt-mayo']);
  });

  it('idempotency key remains stable on network retry, changes when payload changes', async () => {
    const sampleLine: DraftCartLine = {
      menuItemId: 'item-burger',
      variantId: 'var-single',
      selectedModifierOptionIds: ['opt-mayo'],
      customerNote: 'Tuzsuz olsun',
      quantity: 1,
      unitPriceMinorUnits: 19000,
      lineTotalMinorUnits: 19000,
      currency: 'TRY',
      configurationSignature: 'item-burger:var-single:opt-mayo:Tuzsuz olsun',
    };

    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [sampleLine],
      savedAt: Date.now(),
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    const capturedKeys: string[] = [];
    vi.spyOn(customerOrderApi, 'submitOrder').mockImplementation(async (_req, key) => {
      capturedKeys.push(key);
      throw new CustomerOrderApiError(0, 'NETWORK_ERROR', 'Ağ hatası');
    });

    const { rerender } = render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    // First submit attempt
    const submitBtn = screen.getByTestId('confirm-and-submit-order-btn');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByTestId('order-submit-error-alert')).toBeDefined();
    });
    expect(capturedKeys).toHaveLength(1);
    const firstKey = capturedKeys[0]!;

    // Retry with SAME payload
    fireEvent.click(submitBtn);
    await waitFor(() => {
      expect(capturedKeys).toHaveLength(2);
    });
    // Key MUST be identical on retry
    expect(capturedKeys[1]).toBe(firstKey);

    // Now change payload: update note with a new session
    const updatedLine: DraftCartLine = {
      ...sampleLine,
      customerNote: 'Bol soslu',
      configurationSignature: 'item-burger:var-single:opt-mayo:Bol soslu',
    };
    window.sessionStorage.setItem(
      'ro_customer_cart_draft',
      JSON.stringify({ ...preseededDraft, sessionId: 'test-session-2', lines: [updatedLine] })
    );

    rerender(
      <CartProvider key="session-2" sessionId="test-session-2">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));
    await waitFor(() => {
      expect(capturedKeys).toHaveLength(3);
    });
    // Changed payload MUST produce a different idempotency key
    expect(capturedKeys[2]).not.toBe(firstKey);
  });

  it('recovers from 409 ORDER_PRICE_CHANGED with resolve button redirecting to cart', async () => {
    const onClose = vi.fn();
    const priceChangedError = new CustomerOrderApiError(
      409,
      'ORDER_PRICE_CHANGED',
      'Ürün fiyatları güncellendi.',
      {
        status: 409,
        detail: 'Price changed',
        extensions: { errorCode: 'ORDER_PRICE_CHANGED', previousPriceMinorUnits: 16000, currentPriceMinorUnits: 19000 },
      }
    );
    vi.spyOn(customerOrderApi, 'submitOrder').mockRejectedValue(priceChangedError);

    window.sessionStorage.setItem(
      'ro_customer_cart_draft',
      JSON.stringify({
        version: 1,
        sessionId: 'test-session',
        currency: 'TRY',
        lines: [oldPriceLine],
        savedAt: Date.now(),
      })
    );

    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={onClose}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    // Trigger submit
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('order-submit-error-alert')).toBeDefined();
      expect(screen.getByTestId('resolve-price-change-btn')).toBeDefined();
    });

    // Clicking resolve closes confirmation sheet to return to cart
    fireEvent.click(screen.getByTestId('resolve-price-change-btn'));
    expect(onClose).toHaveBeenCalled();
  });

  it('renders realistic financial breakdown in confirmation sheet for tax included, excluded, and service charge', () => {
    const taxExcludedMenu: RuntimeMenuReadModel = {
      ...sampleRuntimeMenu,
      pricesIncludeTax: false,
      defaultTaxRateBps: 2000, // 20% tax
      isServiceChargeEnabled: true,
      serviceChargeRateBps: 1000, // 10% service charge
      pricingPolicyFingerprint: 'f10591c527585aee34a1ec419eb7137fee27b199dc6b82b083373bd14402d9af',
    };

    const line: DraftCartLine = {
      menuItemId: 'item-burger',
      variantId: 'var-single',
      selectedModifierOptionIds: [],
      customerNote: null,
      quantity: 1,
      unitPriceMinorUnits: 10000, // 100.00 TL
      lineTotalMinorUnits: 10000,
      currency: 'TRY',
      configurationSignature: 'item-burger:var-single::',
    };
    window.sessionStorage.setItem(
      'ro_customer_cart_draft',
      JSON.stringify({ version: 1, sessionId: 'test-session', currency: 'TRY', lines: [line], savedAt: Date.now() })
    );

    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={taxExcludedMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    // 100.00 TL net, 20% tax = 20.00 TL, subtotal with tax = 120.00 TL, 10% service charge on gross = 12.00 TL, grand total = 132.00 TL
    expect(screen.getByTestId('confirmation-net-subtotal').textContent).toContain('100,00');
    expect(screen.getByTestId('confirmation-tax-label').textContent).toContain('Fiyatlara Hariçtir');
    expect(screen.getByTestId('confirmation-tax-amount').textContent).toContain('20,00');
    expect(screen.getByTestId('confirmation-service-label').textContent).toContain('Servis Ücreti');
    expect(screen.getByTestId('confirmation-service-charge').textContent).toContain('12,00');
    expect(screen.getByTestId('confirmation-total-price').textContent).toContain('132,00');
  });
});
