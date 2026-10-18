import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { RuntimeMenuReadModel, DraftCartLine } from '@restaurant-order/contracts';
import { CartProvider } from '../cart/CartContext';
import { CartSheet } from '../components/cart/CartSheet';
import { OrderConfirmationSheet } from '../components/cart/OrderConfirmationSheet';
import { customerOrderApi, CustomerOrderApiError } from '../api/customerOrderApi';

describe('Phase 8 Policy Change, Re-approval & Checkout Gating Regression Tests', () => {
  const policy1Fingerprint = '0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9';
  const policy2Fingerprint = '40eaf86c5bce0a262451923576b8f26dbcf44d0d0aa4c05919d740d99a49508f';

  const sampleRuntimeMenu: RuntimeMenuReadModel = {
    branchId: 'branch-1',
    currency: 'TRY',
    pricesIncludeTax: true,
    defaultTaxRateBps: 1000,
    isServiceChargeEnabled: false,
    serviceChargeRateBps: 0,
    pricingPolicyFingerprint: policy1Fingerprint,
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
                basePriceMinorUnits: 18000,
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

  const sampleLine: DraftCartLine = {
    menuItemId: 'item-burger',
    variantId: null,
    selectedModifierOptionIds: [],
    customerNote: 'Tuzsuz olsun',
    quantity: 2,
    unitPriceMinorUnits: 18000,
    lineTotalMinorUnits: 36000,
    currency: 'TRY',
    configurationSignature: 'item-burger:none::Tuzsuz olsun',
  };

  beforeEach(() => {
    vi.restoreAllMocks();
    window.sessionStorage.clear();
  });

  it('blocks checkout CTA when menu is null, loading, or has error, and unblocks on retry', () => {
    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [sampleLine],
      savedAt: Date.now(),
      acceptedPolicyFingerprint: policy1Fingerprint,
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    const onRetry = vi.fn();
    const { rerender } = render(
      <CartProvider sessionId="test-session">
        <CartSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={null}
          isLoadingMenu={false}
          menuError={null}
        />
      </CartProvider>
    );

    // Blocked when runtime menu is null
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);

    // Blocked when loading
    rerender(
      <CartProvider sessionId="test-session">
        <CartSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          isLoadingMenu={true}
          menuError={null}
        />
      </CartProvider>
    );
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);

    // Blocked when menuError
    rerender(
      <CartProvider sessionId="test-session">
        <CartSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          isLoadingMenu={false}
          menuError="Güncel menü bilgisine ulaşılamadı."
          onRetryMenu={onRetry}
        />
      </CartProvider>
    );
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);
    expect(screen.getByTestId('cart-menu-error-alert')).toBeDefined();

    // Click retry
    fireEvent.click(screen.getByTestId('cart-menu-retry-btn'));
    expect(onRetry).toHaveBeenCalledTimes(1);

    // Unblocked after retry succeeds
    rerender(
      <CartProvider sessionId="test-session">
        <CartSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={sampleRuntimeMenu}
          isLoadingMenu={false}
          menuError={null}
        />
      </CartProvider>
    );
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', false);
  });

  it('policy change with identical item prices blocks checkout CTA and requires explicit re-approval', async () => {
    // Draft accepted under policy1
    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [sampleLine],
      savedAt: Date.now(),
      acceptedPolicyFingerprint: policy1Fingerprint,
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    // Menu now has policy2 (e.g. tax/service charge policy changed), but unit prices are identical
    const updatedPolicyMenu: RuntimeMenuReadModel = {
      ...sampleRuntimeMenu,
      isServiceChargeEnabled: true,
      serviceChargeRateBps: 500,
      pricingPolicyFingerprint: policy2Fingerprint,
    };

    render(
      <CartProvider sessionId="test-session">
        <CartSheet isOpen={true} onClose={vi.fn()} runtimeMenu={updatedPolicyMenu} />
      </CartProvider>
    );

    // CTA must be blocked despite unit prices matching
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);
    expect(screen.getByTestId('cart-staleness-warning').textContent).toContain('Politika Güncellemesi');
    const acceptBtn = screen.getByTestId('accept-price-changes-btn');
    expect(acceptBtn.textContent).toContain('Yeni Koşulları Kabul Et');

    // User explicitly confirms new policy
    fireEvent.click(acceptBtn);

    await waitFor(() => {
      expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', false);
      expect(screen.queryByTestId('cart-staleness-warning')).toBeNull();
    });

    // Check sessionStorage updated with new policy fingerprint
    const saved = JSON.parse(window.sessionStorage.getItem('ro_customer_cart_draft') || '{}');
    expect(saved.acceptedPolicyFingerprint).toBe(policy2Fingerprint);
  });

  it('recovers from 409 ORDER_PRICING_POLICY_CHANGED and ORDER_TOTAL_CHANGED via resolve button calling onResolvePriceConflict', async () => {
    const onClose = vi.fn();
    const onResolve = vi.fn();

    const policyError = new CustomerOrderApiError(409, 'ORDER_PRICING_POLICY_CHANGED', 'Vergi politikası güncellendi.');
    vi.spyOn(customerOrderApi, 'submitOrder').mockRejectedValueOnce(policyError);

    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [sampleLine],
      savedAt: Date.now(),
      acceptedPolicyFingerprint: policy1Fingerprint,
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    const { rerender } = render(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={onClose}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
          onResolvePriceConflict={onResolve}
        />
      </CartProvider>
    );

    // Submit order -> gets 409 ORDER_PRICING_POLICY_CHANGED
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('order-submit-error-alert')).toBeDefined();
      expect(screen.getByTestId('resolve-price-change-btn')).toBeDefined();
    });

    // Clicking resolve button calls both onClose and onResolvePriceConflict
    fireEvent.click(screen.getByTestId('resolve-price-change-btn'));
    expect(onClose).toHaveBeenCalled();
    expect(onResolve).toHaveBeenCalled();

    // Now test ORDER_TOTAL_CHANGED
    const totalError = new CustomerOrderApiError(409, 'ORDER_TOTAL_CHANGED', 'Genel toplam güncellendi.');
    vi.spyOn(customerOrderApi, 'submitOrder').mockRejectedValueOnce(totalError);

    rerender(
      <CartProvider sessionId="test-session">
        <OrderConfirmationSheet
          isOpen={true}
          onClose={onClose}
          runtimeMenu={sampleRuntimeMenu}
          onOrderSuccess={vi.fn()}
          onResolvePriceConflict={onResolve}
        />
      </CartProvider>
    );

    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('order-submit-error-alert')).toBeDefined();
      expect(screen.getByTestId('resolve-price-change-btn')).toBeDefined();
    });

    fireEvent.click(screen.getByTestId('resolve-price-change-btn'));
    expect(onResolve).toHaveBeenCalledTimes(2);
  });

  it('submits order with mandatory clientPricingPolicyFingerprint and clientExpectedTotalMinorUnits', async () => {
    const preseededDraft = {
      version: 1,
      sessionId: 'test-session',
      currency: 'TRY',
      lines: [sampleLine],
      savedAt: Date.now(),
      acceptedPolicyFingerprint: policy1Fingerprint,
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseededDraft));

    let capturedRequest: unknown = null;
    vi.spyOn(customerOrderApi, 'submitOrder').mockImplementation(async (req) => {
      capturedRequest = req;
      return {
        id: 'ord-test-1',
        tenantId: 't-1',
        branchId: 'b-1',
        diningSessionId: 's-1',
        tableId: 'tbl-1',
        roundNumber: 1,
        orderSource: 'Customer',
        status: 'Submitted',
        currency: 'TRY',
        subtotalMinorUnits: 36000,
        taxMinorUnits: 3600,
        serviceChargeMinorUnits: 0,
        totalMinorUnits: 36000,
        idempotencyKey: 'key-1',
        concurrencyToken: 'tok-1',
        createdAtUtc: new Date().toISOString(),
        submittedAtUtc: new Date().toISOString(),
        items: [],
      };
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
      expect(capturedRequest).not.toBeNull();
    });

    const req = capturedRequest as {
      clientPricingPolicyFingerprint: string;
      clientExpectedTotalMinorUnits: number;
      items: unknown[];
    };
    expect(req.clientPricingPolicyFingerprint).toBe(policy1Fingerprint);
    expect(req.clientExpectedTotalMinorUnits).toBe(36000); // 2 * 18000 = 36000
    expect(req.items).toHaveLength(1);
  });
});
