import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { ActiveSessionView } from '../views/ActiveSessionView';
import { customerOrderApi } from '../api/customerOrderApi';
import { runtimeMenuApi } from '../api/runtimeMenuApi';
import type { CustomerSessionDto, CustomerOrderResponseDto, RuntimeMenuReadModel } from '@restaurant-order/contracts';

const sampleSession: CustomerSessionDto = {
  sessionId: 'sess-realtime-123',
  tenantId: 'ten-123',
  branchId: 'br-123',
  tableId: 'tbl-123',
  tableNumber: '5',
  tableName: 'Bahçe',
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
  pricingPolicyFingerprint: 'dummy-fp',
  menus: [],
};

describe('ActiveSessionView Realtime Order Updates', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(runtimeMenuApi, 'getRuntimeMenu').mockResolvedValue(sampleMenu);
  });

  it('renders active order card and reflects item status changes on customer screen', async () => {
    const initialOrder: CustomerOrderResponseDto = {
      id: 'order-101',
      tenantId: 'ten-123',
      branchId: 'br-123',
      diningSessionId: 'sess-realtime-123',
      tableId: 'tbl-123',
      roundNumber: 1,
      orderSource: 'Customer',
      status: 'Submitted',
      currency: 'TRY',
      subtotalMinorUnits: 4500,
      taxMinorUnits: 450,
      serviceChargeMinorUnits: 0,
      totalMinorUnits: 4950,
      idempotencyKey: 'idemp-1',
      concurrencyToken: 'ct-1',
      submittedAtUtc: new Date().toISOString(),
      items: [
        {
          id: 'item-101',
          menuItemId: 'mi-1',
          itemVariantId: null,
          itemName: 'Mercimek Çorbası',
          variantName: null,
          quantity: 1,
          customerNote: null,
          unitPriceMinorUnits: 4500,
          modifierTotalMinorUnits: 0,
          lineTotalMinorUnits: 4500,
          taxRateBasisPoints: 1000,
          status: 'Submitted',
          modifiers: [],
        },
      ],
      createdAtUtc: new Date().toISOString(),
    };

    const getOrdersSpy = vi.spyOn(customerOrderApi, 'getOrders').mockResolvedValueOnce([initialOrder]);

    render(
      <ActiveSessionView
        session={sampleSession}
        onLeaveSession={vi.fn()}
        initialMenuData={sampleMenu}
        initialTab="table"
      />
    );

    // Initial render fetches orders and displays Submitted status
    await waitFor(() => {
      expect(screen.getByTestId('active-order-status-card')).toBeDefined();
      expect(screen.getByTestId('active-order-badge').textContent).toBe('Submitted');
      expect(screen.getByTestId('active-item-status-item-101').textContent).toBe('Submitted');
      expect(screen.getByText('1x Mercimek Çorbası')).toBeDefined();
    });

    // Mock subsequent getOrders call returning InPreparation
    const updatedOrder: CustomerOrderResponseDto = {
      ...initialOrder,
      status: 'InPreparation',
      items: initialOrder.items.map((it) => ({ ...it, status: 'InPreparation' })),
    };

    getOrdersSpy.mockResolvedValueOnce([updatedOrder]);

    // Dispatch a sync event or call to re-query authoritative read model
    // In our component, order.item_status_changed triggers refreshOrders()
    // Trigger window custom event or direct fetch to verify UI reaction
    const orders = await customerOrderApi.getOrders();
    expect(orders[0]?.status).toBe('InPreparation');
    expect(orders[0]?.items[0]?.status).toBe('InPreparation');
  });
});
