import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { CartProvider } from '../cart/CartContext';
import { OrderConfirmationSheet } from '../components/cart/OrderConfirmationSheet';
import { OrderSuccessSheet } from '../components/cart/OrderSuccessSheet';
import { OrderHistorySheet } from '../components/cart/OrderHistorySheet';
import type { CustomerOrderResponseDto, RuntimeMenuReadModel, DraftCartLine } from '@restaurant-order/contracts';

describe('Phase 8.4 Customer Order Negative Flows & History Tests', () => {
  const sampleRuntimeMenu: RuntimeMenuReadModel = {
    branchId: 'branch-1',
    currency: 'TRY',
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
                modifierGroups: [],
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
    selectedModifierOptionIds: [],
    customerNote: null,
    quantity: 1,
    unitPriceMinorUnits: 2500,
    lineTotalMinorUnits: 2500,
    currency: 'TRY',
    configurationSignature: 'item-burger-1:var-1::',
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
    subtotalMinorUnits: 2500,
    taxMinorUnits: 250,
    serviceChargeMinorUnits: 0,
    totalMinorUnits: 2500,
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
        quantity: 1,
        customerNote: null,
        unitPriceMinorUnits: 2500,
        modifierTotalMinorUnits: 0,
        lineTotalMinorUnits: 2500,
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

  it('handles ORDER_PRICE_CHANGED 409 conflict and asks user to re-confirm', async () => {
    setupCart();

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        status: 409,
        detail: 'Price changed',
        extensions: {
          errorCode: 'ORDER_PRICE_CHANGED',
          previousPriceMinorUnits: 2500,
          currentPriceMinorUnits: 2900,
        },
      }),
      headers: new Headers(),
    });

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

    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByText('Fiyat Değişikliği')).toBeDefined();
      expect(screen.getByRole('alert').textContent).toContain('fiyatları güncellendi');
    });
  });

  it('handles ITEM_OUT_OF_STOCK 409 conflict and highlights affected item', async () => {
    setupCart();

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        status: 409,
        detail: 'Out of stock',
        extensions: {
          errorCode: 'ITEM_OUT_OF_STOCK',
          menuItemId: 'item-burger-1',
        },
      }),
      headers: new Headers(),
    });

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

    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByText('Ürün Tükendi')).toBeDefined();
      const affectedItem = screen.getByTestId('confirmation-item-item-burger-1');
      expect(affectedItem).toBeDefined();
    });
  });

  it('handles SESSION_NOT_ACTIVE 409 conflict and triggers onSessionExpired', async () => {
    setupCart();

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: async () => ({
        status: 409,
        detail: 'Session closed',
        extensions: { errorCode: 'SESSION_NOT_ACTIVE' },
      }),
      headers: new Headers(),
    });

    const onSessionExpired = vi.fn();

    render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
          onSessionExpired={onSessionExpired}
        />
      </CartProvider>
    );

    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByRole('alert').textContent).toContain('oturumunuz aktif değil');
    });

    await waitFor(
      () => {
        expect(onSessionExpired).toHaveBeenCalled();
      },
      { timeout: 3500 }
    );
  });

  it('renders OrderSuccessSheet with human-friendly Round #1 and status Submitted without raw technical ID', () => {
    render(
      <OrderSuccessSheet
        isOpen={true}
        onClose={vi.fn()}
        order={sampleSuccessOrder}
        onViewOrders={vi.fn()}
        onBrowseMenu={vi.fn()}
      />
    );

    expect(screen.getByText('Sipariş Turu #1')).toBeDefined();
    expect(screen.getByText('Durum: Mutfak Onayı Bekliyor (Submitted)')).toBeDefined();
    expect(screen.getByText('1x Klasik Burger')).toBeDefined();

    // Technical GUID must NOT be displayed to customer
    expect(screen.queryByText('ord-succ-1')).toBeNull();
    expect(screen.queryByText('tenant-1')).toBeNull();
    expect(screen.queryByText('token-1')).toBeNull();
  });

  it('renders OrderHistorySheet, displays past orders, and triggers manual refresh', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [sampleSuccessOrder],
      headers: new Headers(),
    });

    render(
      <OrderHistorySheet
        isOpen={true}
        onClose={vi.fn()}
        tableInfo={{ tableNumber: '5', tableName: 'Salon' }}
      />
    );

    await waitFor(() => {
      expect(screen.getByText('Sipariş Turu #1')).toBeDefined();
      expect(screen.getByText('Mutfak Onayı Bekliyor')).toBeDefined();
      expect(screen.getByText('1x Klasik Burger (150g Tek Köfte)')).toBeDefined();
    });

    const refreshBtn = screen.getByTestId('refresh-orders-btn');
    fireEvent.click(refreshBtn);

    await waitFor(() => {
      // getOrders fetch called again
      expect(globalThis.fetch).toHaveBeenCalledTimes(2);
    });
  });
});
