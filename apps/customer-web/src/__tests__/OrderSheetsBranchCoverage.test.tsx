import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { OrderSuccessSheet } from '../components/cart/OrderSuccessSheet';
import { OrderHistorySheet } from '../components/cart/OrderHistorySheet';
import { customerOrderApi, CustomerOrderApiError } from '../api/customerOrderApi';
import type { CustomerOrderResponseDto } from '@restaurant-order/contracts';

const sampleOrder: CustomerOrderResponseDto = {
  id: 'ord-123',
  tenantId: 'ten-123',
  branchId: 'br-123',
  diningSessionId: 'sess-123',
  tableId: 'tbl-123',
  roundNumber: 2,
  orderSource: 'CustomerQr',
  status: 'InPreparation',
  currency: 'TRY',
  subtotalMinorUnits: 15000,
  taxMinorUnits: 1500,
  serviceChargeMinorUnits: 0,
  totalMinorUnits: 16500,
  idempotencyKey: 'idem-123',
  concurrencyToken: 'token-123',
  createdAtUtc: '2026-10-18T12:00:00Z',
  submittedAtUtc: '2026-10-18T12:00:00Z',
  items: [
    {
      id: 'item-1',
      menuItemId: 'mi-1',
      itemVariantId: 'var-1',
      itemName: 'Pizza Margherita',
      variantName: 'Büyük Boy',
      quantity: 1,
      customerNote: null,
      unitPriceMinorUnits: 15000,
      modifierTotalMinorUnits: 0,
      lineTotalMinorUnits: 15000,
      taxRateBasisPoints: 1000,
      status: 'InPreparation',
      modifiers: [],
    },
  ],
};

describe('Order Sheets & UI Coverage Tests', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe('OrderSuccessSheet', () => {
    it('handles action button clicks (onViewOrders and onBrowseMenu)', () => {
      const onClose = vi.fn();
      const onViewOrders = vi.fn();
      const onBrowseMenu = vi.fn();

      render(
        <OrderSuccessSheet
          isOpen={true}
          onClose={onClose}
          order={sampleOrder}
          onViewOrders={onViewOrders}
          onBrowseMenu={onBrowseMenu}
        />
      );

      const viewBtn = screen.getByTestId('view-orders-history-btn');
      fireEvent.click(viewBtn);
      expect(onClose).toHaveBeenCalled();
      expect(onViewOrders).toHaveBeenCalled();

      const browseBtn = screen.getByTestId('return-to-menu-btn');
      fireEvent.click(browseBtn);
      expect(onBrowseMenu).toHaveBeenCalled();
    });

    it('renders cleanly without optional action callbacks or with null order', () => {
      const onClose = vi.fn();

      const { rerender } = render(
        <OrderSuccessSheet
          isOpen={true}
          onClose={onClose}
          order={null}
        />
      );

      // When order is null, nothing rendered inside
      expect(screen.queryByTestId('order-success-total')).toBeNull();

      rerender(
        <OrderSuccessSheet
          isOpen={true}
          onClose={onClose}
          order={sampleOrder}
        />
      );

      // onViewOrders is not passed, so history button is not rendered
      expect(screen.queryByTestId('view-orders-history-btn')).toBeNull();

      // Click browse menu without onBrowseMenu callback
      const browseBtn = screen.getByTestId('return-to-menu-btn');
      fireEvent.click(browseBtn);
      expect(onClose).toHaveBeenCalled();
    });
  });

  describe('OrderHistorySheet', () => {
    it('handles all order status badges, table info and refresh button', async () => {
      const statusOrders: CustomerOrderResponseDto[] = [
        { ...sampleOrder, id: 'ord-sub', roundNumber: 1, status: 'Submitted' },
        { ...sampleOrder, id: 'ord-prep', roundNumber: 2, status: 'InPreparation' },
        { ...sampleOrder, id: 'ord-ready', roundNumber: 3, status: 'Ready' },
        { ...sampleOrder, id: 'ord-served', roundNumber: 4, status: 'Served' },
        { ...sampleOrder, id: 'ord-canc', roundNumber: 5, status: 'Cancelled' },
        {
          ...sampleOrder,
          id: 'ord-custom',
          roundNumber: 6,
          status: 'CustomStatus',
          items: [
            {
              ...sampleOrder.items[0]!,
              variantName: null,
            },
          ],
        },
      ];

      const getOrdersSpy = vi.spyOn(customerOrderApi, 'getOrders').mockResolvedValue(statusOrders);

      render(
        <OrderHistorySheet
          isOpen={true}
          onClose={vi.fn()}
          tableInfo={{ tableNumber: '10', tableName: 'Teras' }}
        />
      );

      await waitFor(() => {
        expect(screen.getByText('Masa 10 (Teras) • Sipariş Geçmişi')).toBeDefined();
        expect(screen.getByText('Mutfak Onayı Bekliyor')).toBeDefined();
        expect(screen.getByText('Hazırlanıyor')).toBeDefined();
        expect(screen.getByText('Hazır')).toBeDefined();
        expect(screen.getByText('Servis Edildi')).toBeDefined();
        expect(screen.getByText('İptal Edildi')).toBeDefined();
        expect(screen.getByText('CustomStatus')).toBeDefined();
        expect(screen.getAllByText(/Büyük Boy/).length).toBeGreaterThan(0);
      });

      // Test manual refresh button
      const refreshBtn = screen.getByTestId('refresh-orders-btn');
      fireEvent.click(refreshBtn);
      expect(getOrdersSpy).toHaveBeenCalledTimes(2);
    });

    it('renders fallback description when tableInfo is not provided', async () => {
      vi.spyOn(customerOrderApi, 'getOrders').mockResolvedValue([]);

      render(
        <OrderHistorySheet
          isOpen={true}
          onClose={vi.fn()}
        />
      );

      await waitFor(() => {
        expect(screen.getByText('Bu masada verilen tüm siparişler')).toBeDefined();
        expect(screen.getByText('Bu masada henüz verilmiş bir sipariş bulunmamaktadır.')).toBeDefined();
      });
    });

    it('handles API error and renders error alert banner', async () => {
      vi.spyOn(customerOrderApi, 'getOrders').mockRejectedValue(new Error('Network error'));

      render(
        <OrderHistorySheet
          isOpen={true}
          onClose={vi.fn()}
        />
      );

      await waitFor(() => {
        expect(screen.getByTestId('order-history-error-alert')).toBeDefined();
        expect(screen.getByText('Sipariş geçmişi yüklenemedi. Lütfen tekrar deneyiniz.')).toBeDefined();
      });
    });

    it('triggers onSessionExpired callback when 401 error is received', async () => {
      const onSessionExpired = vi.fn();
      const error401 = new CustomerOrderApiError(401, 'UNKNOWN', 'Unauthorized');
      vi.spyOn(customerOrderApi, 'getOrders').mockRejectedValue(error401);

      render(
        <OrderHistorySheet
          isOpen={true}
          onClose={vi.fn()}
          onSessionExpired={onSessionExpired}
        />
      );

      await waitFor(() => {
        expect(onSessionExpired).toHaveBeenCalled();
        expect(screen.getByTestId('order-history-error-alert')).toBeDefined();
      });
    });
  });
});
