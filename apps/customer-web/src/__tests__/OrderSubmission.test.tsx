import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { CartProvider } from '../cart/CartContext';
import { OrderConfirmationSheet } from '../components/cart/OrderConfirmationSheet';
import { customerOrderApi, CustomerOrderApiError } from '../api/customerOrderApi';
import type { CustomerOrderResponseDto, RuntimeMenuReadModel, DraftCartLine } from '@restaurant-order/contracts';

describe('Phase 8.4 Customer Order Submission & Idempotency Tests', () => {
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
                id: 'item-burger-1',
                name: 'Klasik Burger',
                slug: 'klasik-burger',
                shortDescription: null,
                fullDescription: null,
                imageUrl: null,
                basePriceMinorUnits: 2500,
                sortOrder: 1,
                spicyLevel: 0,
                dietaryTags: [],
                allergenTags: [],
                isAvailable: true,
                expectedAvailableAtUtc: null,
                variants: [
                  {
                    id: 'var-1',
                    name: '150g Tek Köfte',
                    code: '150G',
                    absolutePriceMinorUnits: 2500,
                    sortOrder: 1,
                    isDefault: true,
                    isAvailable: true,
                    expectedAvailableAtUtc: null,
                  },
                ],
                modifierGroups: [
                  {
                    id: 'grp-cheddar',
                    name: 'Peynir',
                    minSelections: 0,
                    maxSelections: 2,
                    sortOrder: 1,
                    options: [
                      {
                        id: 'opt-cheddar',
                        name: 'Cheddar Peyniri',
                        priceDeltaMinorUnits: 0,
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

  const sampleLine: DraftCartLine = {
    menuItemId: 'item-burger-1',
    variantId: 'var-1',
    selectedModifierOptionIds: ['opt-cheddar'],
    customerNote: 'Soğansız olsun lütfen',
    quantity: 2,
    unitPriceMinorUnits: 2500,
    lineTotalMinorUnits: 5000,
    currency: 'TRY',
    configurationSignature: 'item-burger-1:var-1:opt-cheddar:Soğansız olsun lütfen',
  };

  const sampleSuccessOrder: CustomerOrderResponseDto = {
    id: 'ord-succ-1',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    diningSessionId: 'test-session',
    tableId: 'table-1',
    roundNumber: 1,
    orderSource: 'Customer',
    status: 'Submitted',
    currency: 'TRY',
    subtotalMinorUnits: 5000,
    taxMinorUnits: 500,
    serviceChargeMinorUnits: 0,
    totalMinorUnits: 5000,
    idempotencyKey: 'idem-1',
    concurrencyToken: 'token-1',
    createdAtUtc: '2026-10-17T12:00:00Z',
    submittedAtUtc: '2026-10-17T12:00:00Z',
    items: [
      {
        id: 'ord-item-1',
        menuItemId: 'item-burger-1',
        itemVariantId: 'var-1',
        itemName: 'Klasik Burger',
        variantName: '150g Tek Köfte',
        quantity: 2,
        customerNote: 'Soğansız olsun lütfen',
        unitPriceMinorUnits: 2500,
        modifierTotalMinorUnits: 0,
        lineTotalMinorUnits: 5000,
        taxRateBasisPoints: 1000,
        status: 'Submitted',
        modifiers: [],
      },
    ],
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    window.sessionStorage.clear();
  });

  afterEach(() => {
    window.sessionStorage.clear();
  });

  const setupCart = () => {
    const draft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [sampleLine],
      savedAt: Date.now(),
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(draft));
  };

  it('reuses the pending order key after page reload when the response was lost', async () => {
    setupCart();
    const submit = vi.spyOn(customerOrderApi, 'submitOrder')
      .mockRejectedValueOnce(new CustomerOrderApiError(0, 'NETWORK_ERROR', 'Yanıt alınamadı'))
      .mockResolvedValueOnce(sampleSuccessOrder);
    const checkout = () => (
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet isOpen={true} onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu} onOrderSuccess={vi.fn()} />
      </CartProvider>
    );
    const firstPage = render(checkout());
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));
    await waitFor(() => expect(screen.getByTestId('order-submit-error-alert')).toBeDefined());
    const firstKey = submit.mock.calls[0]![1];
    firstPage.unmount();

    render(checkout());
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => expect(submit).toHaveBeenCalledTimes(2));
    expect(submit.mock.calls[1]![1]).toBe(firstKey);
  });

  it('preserves the cart and does not send when the pending attempt cannot be saved', async () => {
    setupCart();
    const submit = vi.spyOn(customerOrderApi, 'submitOrder');
    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet isOpen={true} onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu} onOrderSuccess={vi.fn()} />
      </CartProvider>
    );
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new Error('quota'); });

    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => expect(screen.getByTestId('order-submit-error-alert')).toBeDefined());
    expect(submit).not.toHaveBeenCalled();
    expect(window.sessionStorage.getItem('ro_customer_cart_draft')).not.toBeNull();
  });

  it('successful order submission clears cart and invokes onOrderSuccess callback', async () => {
    setupCart();

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 201,
      json: async () => sampleSuccessOrder,
      headers: new Headers(),
    });

    const onOrderSuccess = vi.fn();
    const onClose = vi.fn();

    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={onClose}
          tableInfo={{ tableNumber: '5', tableName: 'Salon' }}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={onOrderSuccess}
        />
      </CartProvider>
    );

    expect(screen.getByText(/Soğansız olsun lütfen/)).toBeDefined();
    expect(screen.getByTestId('confirmation-total-price').textContent).toContain('50,00');

    const submitBtn = screen.getByTestId('confirm-and-submit-order-btn');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(onOrderSuccess).toHaveBeenCalledTimes(1);
    });

    expect(onOrderSuccess).toHaveBeenCalledWith(sampleSuccessOrder);
    expect(onClose).toHaveBeenCalled();

    // Verify cart was cleared in sessionStorage after success
    const raw = window.sessionStorage.getItem('ro_customer_cart_draft');
    if (raw) {
      const parsed = JSON.parse(raw);
      expect(parsed.lines).toHaveLength(0);
    }
  });

  it('prevents double-click and disables button while submission is in-flight', async () => {
    setupCart();

    let resolveFetch: (value: unknown) => void = () => {};
    const pendingPromise = new Promise((resolve) => {
      resolveFetch = resolve;
    });

    globalThis.fetch = vi.fn().mockImplementation(() => pendingPromise);

    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    const submitBtn = screen.getByTestId('confirm-and-submit-order-btn');
    fireEvent.click(submitBtn);
    fireEvent.click(submitBtn);
    fireEvent.click(submitBtn);

    // Only 1 fetch call should be dispatched
    expect(globalThis.fetch).toHaveBeenCalledTimes(1);
    expect(submitBtn.hasAttribute('disabled')).toBe(true);
    expect(submitBtn.textContent).toBe('İletiliyor...');

    // Resolve the fetch
    resolveFetch({
      ok: true,
      status: 201,
      json: async () => sampleSuccessOrder,
      headers: new Headers(),
    });

    await waitFor(() => {
      expect(submitBtn.textContent).not.toBe('İletiliyor...');
    });
  });

  it('preserves idempotency key across failed network retries with identical cart', async () => {
    setupCart();

    globalThis.fetch = vi.fn().mockRejectedValue(new Error('Network error'));

    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    const submitBtn = screen.getByTestId('confirm-and-submit-order-btn');

    // First attempt
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeDefined();
    });

    const [, firstCallInit] = vi.mocked(globalThis.fetch).mock.calls[0] as [string, RequestInit];
    const firstKey = (firstCallInit.headers as Headers).get('Idempotency-Key');
    expect(firstKey).toBeDefined();

    // Second attempt on the SAME cart payload
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(globalThis.fetch).toHaveBeenCalledTimes(2);
    });

    const [, secondCallInit] = vi.mocked(globalThis.fetch).mock.calls[1] as [string, RequestInit];
    const secondKey = (secondCallInit.headers as Headers).get('Idempotency-Key');

    // Key must be strictly identical across retries
    expect(secondKey).toBe(firstKey);
  });

  it('keeps cart untouched when network error occurs and displays alert with role="alert"', async () => {
    setupCart();

    globalThis.fetch = vi.fn().mockRejectedValue(new Error('Failed to fetch'));

    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
        />
      </CartProvider>
    );

    const submitBtn = screen.getByTestId('confirm-and-submit-order-btn');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      const alert = screen.getByRole('alert');
      expect(alert).toBeDefined();
      expect(alert.textContent).toContain('Sepetiniz korundu');
    });

    // Cart is preserved in sessionStorage
    const raw = window.sessionStorage.getItem('ro_customer_cart_draft');
    expect(raw).not.toBeNull();
    const parsed = JSON.parse(raw!);
    expect(parsed.lines).toHaveLength(1);
  });
});
