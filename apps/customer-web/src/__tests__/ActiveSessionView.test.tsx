import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { ActiveSessionView } from '../views/ActiveSessionView';
import { UnauthenticatedQrView } from '../views/UnauthenticatedQrView';
import { runtimeMenuApi } from '../api/runtimeMenuApi';
import { customerOrderApi } from '../api/customerOrderApi';
import { generateConfigurationSignature } from '../utils/customizationUtils';
import type { CustomerSessionDto, RuntimeMenuReadModel, CustomerOrderResponseDto } from '@restaurant-order/contracts';

const sampleSession: CustomerSessionDto = {
  sessionId: 'sess-123',
  tenantId: 'ten-123',
  branchId: 'br-123',
  tableId: 'tbl-123',
  tableNumber: '14',
  tableName: 'Teras',
  sessionStatus: 'Active',
  concurrencyToken: 'token-1',
  guestCount: 2,
  openedAtUtc: '2026-10-18T12:00:00Z',
};

const sampleMenu: RuntimeMenuReadModel = {
  branchId: 'br-123',
  currency: 'TRY',
  pricesIncludeTax: true,
  defaultTaxRateBps: 1000,
  isServiceChargeEnabled: false,
  serviceChargeRateBps: 0,
  pricingPolicyFingerprint: '0268781158e5455cd5066342bc2e2a04ede6e69974fef181a1dbd5d26c4728b9',
  menus: [
    {
      id: 'menu-1',
      name: 'Pizzalar Menüsü',
      slug: 'pizzalar-menusu',
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
              id: 'mi-1',
              name: 'Margarita',
              slug: 'margarita',
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
              variants: [],
              modifierGroups: [],
            },
          ],
        },
      ],
    },
  ],
};

describe('ActiveSessionView and UnauthenticatedQrView Tests', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('UnauthenticatedQrView', () => {
    it('submits manual token on form submission', () => {
      const onTokenSubmit = vi.fn();
      render(<UnauthenticatedQrView onTokenSubmit={onTokenSubmit} />);

      const input = screen.getByPlaceholderText('Örn: test-qr-token');
      fireEvent.change(input, { target: { value: 'tok-xyz-123' } });

      const submitBtn = screen.getByText('Giriş Yap');
      fireEvent.click(submitBtn);

      expect(onTokenSubmit).toHaveBeenCalledWith('tok-xyz-123');
    });

    it('renders without form when onTokenSubmit is not provided', () => {
      render(<UnauthenticatedQrView />);
      expect(screen.getByText('Masadaki QR Kodu Okutun')).toBeDefined();
      expect(screen.queryByPlaceholderText('Örn: test-qr-token')).toBeNull();
    });
  });

  describe('ActiveSessionView', () => {
    beforeEach(() => {
      vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue(sampleMenu);
    });

    it('switches between table and menu tabs via buttons and bottom navigation', async () => {
      const onLeaveSession = vi.fn();

      render(
        <ActiveSessionView
          session={sampleSession}
          onLeaveSession={onLeaveSession}
          initialMenuData={sampleMenu}
          initialTab="table"
        />
      );

      expect(screen.getByText('Hoş Geldiniz')).toBeDefined();
      expect(screen.getByText(/Masa 14 \(Teras\)/)).toBeDefined();

      // Click "Menüyü İncele" button
      const gotoMenuBtn = screen.getByTestId('goto-menu-btn');
      fireEvent.click(gotoMenuBtn);

      // Now menu tab is active
      expect(screen.getByTestId('digital-menu-view')).toBeDefined();

      // Click bottom nav table button to return
      const tableNavBtn = screen.getByTestId('bottom-nav-table-btn');
      fireEvent.click(tableNavBtn);
      expect(screen.getByText('Hoş Geldiniz')).toBeDefined();

      // Click bottom nav menu button
      const menuNavBtn = screen.getByTestId('bottom-nav-menu-btn');
      fireEvent.click(menuNavBtn);
      expect(screen.getByTestId('digital-menu-view')).toBeDefined();
    });

    it('opens history, waiter service and cart sheets from buttons', async () => {
      const onLeaveSession = vi.fn();

      render(
        <ActiveSessionView
          session={sampleSession}
          onLeaveSession={onLeaveSession}
          initialMenuData={sampleMenu}
          initialTab="table"
        />
      );

      // Open history sheet
      const historyBtn = screen.getByTestId('view-orders-tab-btn');
      fireEvent.click(historyBtn);
      await waitFor(() => {
        expect(screen.getByTestId('order-history-sheet')).toBeDefined();
      });

      // Open service sheet
      const serviceBtn = screen.getByTestId('request-service-btn');
      fireEvent.click(serviceBtn);
      await waitFor(() => {
        expect(screen.getByTestId('service-request-sheet')).toBeDefined();
      });

      // Open cart sheet via bottom nav
      const cartNavBtn = screen.getByTestId('bottom-nav-cart-btn');
      fireEvent.click(cartNavBtn);
      await waitFor(() => {
        expect(screen.getByTestId('customer-cart-sheet')).toBeDefined();
      });
    });

    it('refreshes runtime menu on cart open handling both success and error branches', async () => {
      const getMenuSpy = vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue(sampleMenu);

      render(
        <ActiveSessionView
          session={sampleSession}
          onLeaveSession={vi.fn()}
          initialMenuData={sampleMenu}
          initialTab="table"
        />
      );

      // Open cart
      fireEvent.click(screen.getByTestId('bottom-nav-cart-btn'));
      await waitFor(() => {
        expect(getMenuSpy).toHaveBeenCalledWith('br-123');
      });

      // Close cart
      fireEvent.click(screen.getByLabelText('Kapat'));

      // API failure branch test
      getMenuSpy.mockRejectedValue(new Error('Network error'));
      fireEvent.click(screen.getByTestId('bottom-nav-cart-btn'));
      await waitFor(() => {
        expect(getMenuSpy).toHaveBeenCalledTimes(2);
      });
    });

    it('handles order submission success and navigates to history', async () => {
      const sampleOrder: CustomerOrderResponseDto = {
        id: 'ord-placed-1',
        tenantId: 'ten-123',
        branchId: 'br-123',
        diningSessionId: 'sess-123',
        tableId: 'tbl-123',
        roundNumber: 1,
        orderSource: 'Customer',
        status: 'Submitted',
        currency: 'TRY',
        subtotalMinorUnits: 2500,
        taxMinorUnits: 250,
        serviceChargeMinorUnits: 0,
        totalMinorUnits: 2750,
        idempotencyKey: 'idem-placed-1',
        concurrencyToken: 'tok-1',
        createdAtUtc: '2026-10-18T12:00:00Z',
        submittedAtUtc: '2026-10-18T12:00:00Z',
        items: [
          {
            id: 'item-1',
            menuItemId: 'mi-1',
            itemVariantId: null,
            itemName: 'Margarita',
            variantName: null,
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

      vi.spyOn(customerOrderApi, 'submitOrder').mockResolvedValue(sampleOrder);
      vi.spyOn(customerOrderApi, 'getOrders').mockResolvedValue([sampleOrder]);

      const mi1Sig = generateConfigurationSignature({
        menuItemId: 'mi-1',
        variantId: null,
        selectedModifierOptionIds: [],
        customerNote: null,
      });

      // Seed cart draft
      window.sessionStorage.setItem(
        'ro_customer_cart_draft',
        JSON.stringify({
          version: 1,
          sessionId: 'sess-123',
          currency: 'TRY',
          lines: [
            {
              menuItemId: 'mi-1',
              variantId: null,
              selectedModifierOptionIds: [],
              customerNote: null,
              quantity: 1,
              unitPriceMinorUnits: 2500,
              lineTotalMinorUnits: 2500,
              currency: 'TRY',
              configurationSignature: mi1Sig,
            },
          ],
          savedAt: Date.now(),
        })
      );

      render(
        <ActiveSessionView
          session={sampleSession}
          onLeaveSession={vi.fn()}
          initialMenuData={sampleMenu}
          initialTab="table"
        />
      );

      // Open cart
      fireEvent.click(screen.getByTestId('bottom-nav-cart-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('submit-order-cta-btn')).toBeDefined();
      });

      // Proceed to confirmation
      fireEvent.click(screen.getByTestId('submit-order-cta-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('confirm-and-submit-order-btn')).toBeDefined();
      });

      // Submit order
      fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));

      // Order success sheet opens
      await waitFor(() => {
        expect(screen.getByTestId('order-success-sheet')).toBeDefined();
        expect(screen.getByTestId('order-round-number').textContent).toContain('#1');
      });

      // Click "Masa Siparişlerimi Gör"
      fireEvent.click(screen.getByTestId('view-orders-history-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('order-history-sheet')).toBeDefined();
      });
    });

    it('order success sheet allows returning to menu and switches active tab', async () => {
      const sampleOrder: CustomerOrderResponseDto = {
        id: 'ord-placed-2',
        tenantId: 'ten-123',
        branchId: 'br-123',
        diningSessionId: 'sess-123',
        tableId: 'tbl-123',
        roundNumber: 2,
        orderSource: 'Customer',
        status: 'Submitted',
        currency: 'TRY',
        subtotalMinorUnits: 2500,
        taxMinorUnits: 250,
        serviceChargeMinorUnits: 0,
        totalMinorUnits: 2750,
        idempotencyKey: 'idem-placed-2',
        concurrencyToken: 'tok-2',
        createdAtUtc: '2026-10-18T12:00:00Z',
        submittedAtUtc: '2026-10-18T12:00:00Z',
        items: [
          {
            id: 'item-1',
            menuItemId: 'mi-1',
            itemVariantId: null,
            itemName: 'Margarita',
            variantName: null,
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

      vi.spyOn(customerOrderApi, 'submitOrder').mockResolvedValue(sampleOrder);

      const mi1Sig = generateConfigurationSignature({
        menuItemId: 'mi-1',
        variantId: null,
        selectedModifierOptionIds: [],
        customerNote: null,
      });

      window.sessionStorage.setItem(
        'ro_customer_cart_draft',
        JSON.stringify({
          version: 1,
          sessionId: 'sess-123',
          currency: 'TRY',
          lines: [
            {
              menuItemId: 'mi-1',
              variantId: null,
              selectedModifierOptionIds: [],
              customerNote: null,
              quantity: 1,
              unitPriceMinorUnits: 2500,
              lineTotalMinorUnits: 2500,
              currency: 'TRY',
              configurationSignature: mi1Sig,
            },
          ],
          savedAt: Date.now(),
        })
      );

      render(
        <ActiveSessionView
          session={sampleSession}
          onLeaveSession={vi.fn()}
          initialMenuData={sampleMenu}
          initialTab="table"
        />
      );

      fireEvent.click(screen.getByTestId('bottom-nav-cart-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('submit-order-cta-btn')).toBeDefined();
      });
      fireEvent.click(screen.getByTestId('submit-order-cta-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('confirm-and-submit-order-btn')).toBeDefined();
      });
      fireEvent.click(screen.getByTestId('confirm-and-submit-order-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('order-success-sheet')).toBeDefined();
      });

      // Click "Menüyü İncelemeye Devam Et"
      fireEvent.click(screen.getByTestId('return-to-menu-btn'));
      await waitFor(() => {
        expect(screen.getByTestId('digital-menu-view')).toBeDefined();
      });
    });
  });
});
