import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import type { CustomerSessionDto, RuntimeMenuReadModel, CustomerOrderResponseDto, DraftCartLine } from '@restaurant-order/contracts';
import { ActiveSessionView } from '../views/ActiveSessionView';
import { CartProvider, useCart } from '../cart/CartContext';
import { CartSheet } from '../components/cart/CartSheet';
import { runtimeMenuApi } from '../api/runtimeMenuApi';
import { customerOrderApi, CustomerOrderApiError } from '../api/customerOrderApi';
import { generateConfigurationSignature } from '../utils/customizationUtils';

const policy1 = '0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9';
const policy2 = '40eaf86c5bce0a262451923576b8f26dbcf44d0d0aa4c05919d740d99a49508f';

const sampleSession: CustomerSessionDto = {
  sessionId: 'sess-regression-1',
  tenantId: 'ten-1',
  branchId: 'br-1',
  tableId: 'tbl-1',
  tableNumber: '7',
  tableName: 'Bahçe',
  sessionStatus: 'Active',
  concurrencyToken: 'token-1',
  guestCount: 2,
  openedAtUtc: '2026-10-18T12:00:00Z',
};

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

const sampleLine2: DraftCartLine = {
  menuItemId: 'mi-margarita-2',
  variantId: null,
  selectedModifierOptionIds: [],
  customerNote: null,
  quantity: 1,
  unitPriceMinorUnits: 30000,
  lineTotalMinorUnits: 30000,
  currency: 'TRY',
  configurationSignature: 'mi-margarita-2:none::',
};

describe('Phase 8 Pricing Policy Reapproval & Real Flow Regressions', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    window.sessionStorage.clear();
  });

  it('runs complete real customer flow from empty storage to 409 conflict, gated checkout and re-approval', async () => {
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue(baseMenu);

    let submittedPayload: unknown = null;
    let submitAttempt = 0;

    vi.spyOn(customerOrderApi, 'submitOrder').mockImplementation(async (req) => {
      submitAttempt++;
      submittedPayload = req;

      if (submitAttempt === 1) {
        throw new CustomerOrderApiError(
          409,
          'ORDER_PRICING_POLICY_CHANGED',
          'Restoran servis ücreti politikası güncellendi.'
        );
      }

      const mockResponse: CustomerOrderResponseDto = {
        id: 'ord-success-reg-1',
        tenantId: 'ten-1',
        branchId: 'br-1',
        diningSessionId: 'sess-regression-1',
        tableId: 'tbl-1',
        roundNumber: 1,
        orderSource: 'Customer',
        status: 'Submitted',
        currency: 'TRY',
        subtotalMinorUnits: 25000,
        taxMinorUnits: 2273,
        serviceChargeMinorUnits: 2500,
        totalMinorUnits: 27500,
        idempotencyKey: 'idem-reg-1',
        concurrencyToken: 'token-reg-1',
        createdAtUtc: new Date().toISOString(),
        submittedAtUtc: new Date().toISOString(),
        items: [],
      };
      return mockResponse;
    });

    render(
      <ActiveSessionView
        session={sampleSession}
        onLeaveSession={vi.fn()}
        initialMenuData={baseMenu}
        initialTab="menu"
      />
    );

    // 1. Ürün kartını aç
    const itemCard = screen.getByTestId('menu-item-card-mi-margarita');
    fireEvent.click(itemCard);

    // 2. Ürün detay modalı açılır ve gerçek Sepete Ekle düğmesine basılır
    await waitFor(() => {
      expect(screen.getByTestId('menu-item-detail-sheet')).toBeDefined();
    });
    const addToCartBtn = screen.getByTestId('add-to-cart-cta-btn');
    fireEvent.click(addToCartBtn);

    // 3. Sepet alt çubuktan açılır
    const cartNavBtn = screen.getByTestId('bottom-nav-cart-btn');
    fireEvent.click(cartNavBtn);

    await waitFor(() => {
      expect(screen.getByTestId('customer-cart-sheet')).toBeDefined();
      expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', false);
    });

    // 4. Sepeti onayla düğmesine basarak Confirmation modalına geç
    fireEvent.click(screen.getByTestId('submit-order-cta-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('order-confirmation-sheet')).toBeDefined();
      expect(screen.getByTestId('confirm-and-submit-order-btn')).toHaveProperty('disabled', false);
    });

    // 5. Siparişi Gönder -> İlk deneme 409 ORDER_PRICING_POLICY_CHANGED alır
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('order-submit-error-alert')).toBeDefined();
      expect(screen.getByTestId('resolve-price-change-btn')).toBeDefined();
    });

    // 6. Yeni menü snapshot'ı hazırla: ürün birim fiyatı aynı, servis ücreti eklendi, yeni fingerprint
    const updatedPolicyMenu: RuntimeMenuReadModel = {
      ...baseMenu,
      isServiceChargeEnabled: true,
      serviceChargeRateBps: 1000, // 10% servis ücreti
      pricingPolicyFingerprint: policy2,
    };
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue(updatedPolicyMenu);

    // 7. "Sepete Dön ve Fiyatları Güncelle" düğmesine bas
    fireEvent.click(screen.getByTestId('resolve-price-change-btn'));

    // 8. Sepet açılır ve yeni menü yüklendiğinde checkout CTA'sının HÂLÂ KAPALI olduğunu doğrula
    await waitFor(() => {
      expect(screen.getByTestId('customer-cart-sheet')).toBeDefined();
    });

    await waitFor(() => {
      expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);
    });

    // 9. "Yeni Koşulları Kabul Et" açık onay düğmesinin görünmesini doğrula
    const acceptBtn = screen.getByTestId('accept-price-changes-btn');
    expect(acceptBtn.textContent).toContain('Yeni Koşulları Kabul Et');
    expect(screen.queryByText(policy2)).toBeNull(); // Teknik fingerprint kullanıcıya yazılmaz

    // 10. Kabul düğmesine bas
    fireEvent.click(acceptBtn);

    // Kabul sonrasında checkout düğmesi açılmalıdır
    await waitFor(() => {
      expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', false);
    });

    // 11. Yeni onaylanan sepet ile Confirmation'a ilerle
    fireEvent.click(screen.getByTestId('submit-order-cta-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('order-confirmation-sheet')).toBeDefined();
      expect(screen.getByTestId('confirm-and-submit-order-btn')).toHaveProperty('disabled', false);
    });

    // 12. İkinci kez Siparişi Gönder -> 200/201 başarıyla döner
    fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

    await waitFor(() => {
      expect(screen.getByTestId('order-success-sheet')).toBeDefined();
    });

    // Submit payload'ının güncellenen fingerprint ve beklenen genel toplamı taşıdığını doğrula
    const req = submittedPayload as {
      clientPricingPolicyFingerprint: string;
      clientExpectedTotalMinorUnits: number;
    };
    expect(req.clientPricingPolicyFingerprint).toBe(policy2);
    expect(req.clientExpectedTotalMinorUnits).toBe(27500); // 25000 + 2500 servis ücreti
  }, 15000);

  it('requires explicit re-approval when cart is restored from storage without fingerprint', async () => {
    const sig = generateConfigurationSignature({
      menuItemId: 'mi-margarita',
      variantId: null,
      selectedModifierOptionIds: [],
      customerNote: null,
    });

    const legacyDraft = {
      version: 1,
      sessionId: 'sess-regression-1',
      currency: 'TRY',
      lines: [
        {
          menuItemId: 'mi-margarita',
          variantId: null,
          selectedModifierOptionIds: [],
          customerNote: null,
          quantity: 1,
          unitPriceMinorUnits: 25000,
          lineTotalMinorUnits: 25000,
          currency: 'TRY',
          configurationSignature: sig,
        },
      ],
      savedAt: Date.now(),
      acceptedPolicyFingerprint: null,
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(legacyDraft));

    render(
      <CartProvider sessionId="sess-regression-1">
        <CartSheet isOpen={true} onClose={vi.fn()} runtimeMenu={baseMenu} />
      </CartProvider>
    );

    // Fingerprint bulunmayan eski sepet doğrulanmış sayılmaz, checkout CTA kapalıdır
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);
    const acceptBtn = screen.getByTestId('accept-price-changes-btn');
    expect(acceptBtn.textContent).toContain('Yeni Koşulları Kabul Et');

    // Açık kabul verilince checkout açılır
    fireEvent.click(acceptBtn);
    await waitFor(() => {
      expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', false);
    });
  });

  it('does not silently overwrite existing accepted policy when adding item under another policy', () => {
    const CartInspector = () => {
      const cart = useCart();
      return (
        <div>
          <span data-testid="policy-value">{cart.acceptedPolicyFingerprint || 'none'}</span>
          <span data-testid="reconf-value">{String(cart.requiresReconfirmation)}</span>
          <button data-testid="add-item-1" onClick={() => cart.addLine(sampleLine1, policy1)}>Add 1</button>
          <button data-testid="add-item-2" onClick={() => cart.addLine(sampleLine2, policy2)}>Add 2</button>
        </div>
      );
    };

    render(
      <CartProvider sessionId="sess-regression-1">
        <CartInspector />
      </CartProvider>
    );

    // 1. İlk ürün policy1 ile eklenir
    fireEvent.click(screen.getByTestId('add-item-1'));
    expect(screen.getByTestId('policy-value').textContent).toBe(policy1);
    expect(screen.getByTestId('reconf-value').textContent).toBe('false');

    // 2. İkinci ürün farklı policy2 ile eklenir
    fireEvent.click(screen.getByTestId('add-item-2'));

    // Mevcut acceptedPolicyFingerprint ezilmez, requiresReconfirmation true olur
    expect(screen.getByTestId('policy-value').textContent).toBe(policy1);
    expect(screen.getByTestId('reconf-value').textContent).toBe('true');
  });

  it('keeps re-approval requirement across menu refresh failure and successful retry', async () => {
    const sig = generateConfigurationSignature({
      menuItemId: 'mi-margarita',
      variantId: null,
      selectedModifierOptionIds: [],
      customerNote: null,
    });

    const preseeded = {
      version: 1,
      sessionId: 'sess-regression-1',
      currency: 'TRY',
      lines: [
        {
          menuItemId: 'mi-margarita',
          variantId: null,
          selectedModifierOptionIds: [],
          customerNote: null,
          quantity: 1,
          unitPriceMinorUnits: 25000,
          lineTotalMinorUnits: 25000,
          currency: 'TRY',
          configurationSignature: sig,
        },
      ],
      savedAt: Date.now(),
      acceptedPolicyFingerprint: policy1,
      requiresReconfirmation: true,
    };
    window.sessionStorage.setItem('ro_customer_cart_draft', JSON.stringify(preseeded));

    const onRetry = vi.fn();
    const { rerender } = render(
      <CartProvider sessionId="sess-regression-1">
        <CartSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={baseMenu}
          isLoadingMenu={false}
          menuError="Bağlantı hatası oluştu."
          onRetryMenu={onRetry}
        />
      </CartProvider>
    );

    // Menü hatası varken kabul ve checkout engellidir, retry butonu çalışır
    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);
    expect(screen.queryByTestId('accept-price-changes-btn')).toBeNull();
    const retryBtn = screen.getByTestId('cart-menu-retry-btn');
    fireEvent.click(retryBtn);
    expect(onRetry).toHaveBeenCalledTimes(1);

    // Başarılı retry sonrasında da yeniden onay gereksinimi korunur
    rerender(
      <CartProvider sessionId="sess-regression-1">
        <CartSheet
          isOpen={true}
          onClose={vi.fn()}
          runtimeMenu={baseMenu}
          isLoadingMenu={false}
          menuError={null}
        />
      </CartProvider>
    );

    expect(screen.getByTestId('submit-order-cta-btn')).toHaveProperty('disabled', true);
    expect(screen.getByTestId('accept-price-changes-btn')).toBeDefined();
  });
});
