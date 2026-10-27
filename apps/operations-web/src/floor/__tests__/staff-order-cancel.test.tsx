import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { CancelItemModal } from '../CancelItemModal';
import {
  StaffOrderResponseDto,
  StaffOrderItemDto,
  CancelOrderItemRequest,
} from '@restaurant-order/contracts';
import { TerminalContextValue } from '../../auth/TerminalContext';

const mockOrder: StaffOrderResponseDto = {
  id: 'order-1',
  tenantId: 'tenant-1',
  branchId: 'branch-1',
  diningSessionId: 'session-1',
  tableId: 'table-1',
  roundNumber: 1,
  orderSource: 'Staff',
  status: 'Submitted',
  currency: 'TRY',
  subtotalMinorUnits: 25000,
  taxMinorUnits: 2500,
  serviceChargeMinorUnits: 0,
  totalMinorUnits: 25000,
  idempotencyKey: 'idemp-1',
  concurrencyToken: 'token-ord-1',
  createdAtUtc: new Date().toISOString(),
  items: [],
};

const mockTerminalContextWaiter: TerminalContextValue = {
  terminal: { terminalId: 'term-1', tenantId: 'tenant-1', branchId: 'branch-1', terminalName: 'Garson El Terminali 1', deviceIdentifier: 'dev-1', isActive: true },
  state: 'AUTHENTICATED',
  isEnrolled: true,
  staffUser: { userId: 'staff-waiter-1', email: 'waiter@test.com', role: 'Waiter', tenantId: 'tenant-1', branchId: 'branch-1', securityVersion: 1 },
  isLoading: false,
  error: null,
  activateTerminal: vi.fn(),
  loginWithPin: vi.fn(),
  logoutStaff: vi.fn(),
  deactivateTerminal: vi.fn(),
};

describe('WAIT-04 Order Item Cancellation & Supervisor Void', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  it('allows pre-prep item cancellation with reason only', async () => {
    const mockItem: StaffOrderItemDto = {
      id: 'item-uuid-1',
      menuItemId: 'item-1',
      itemName: 'Klasik Burger',
      quantity: 1,
      unitPriceMinorUnits: 25000,
      modifierTotalMinorUnits: 0,
      lineTotalMinorUnits: 25000,
      taxRateBasisPoints: 1000,
      status: 'Submitted',
      createdAtUtc: new Date().toISOString(),
      modifiers: [],
    };

    let cancelPayload: Record<string, unknown> | undefined;
    vi.spyOn(global, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/cancel')) {
        cancelPayload = JSON.parse(init?.body as string);
        return new Response(JSON.stringify({ ...mockOrder, status: 'Cancelled' }), { status: 200 });
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    const onItemCancelled = vi.fn();
    const onClose = vi.fn();

    render(
      <CancelItemModal
        isOpen={true}
        branchId="branch-1"
        sessionId="session-1"
        order={mockOrder}
        item={mockItem}
        terminalContext={mockTerminalContextWaiter}
        onClose={onClose}
        onItemCancelled={onItemCancelled}
      />
    );

    expect(screen.getByText('Kalem İptali (Hazırlık Öncesi)')).toBeDefined();
    expect(screen.queryByText(/Yönetici Onayı gereklidir/)).toBeNull();

    fireEvent.click(screen.getByText('Kalemi İptal Et'));

    await waitFor(() => {
      expect(onItemCancelled).toHaveBeenCalled();
      expect(onClose).toHaveBeenCalled();
    });

    expect(cancelPayload).toBeDefined();
    const payload = cancelPayload as unknown as CancelOrderItemRequest;
    expect(payload.reason).toBeTruthy();
    expect(payload.concurrencyToken).toBe('token-ord-1');
  });

  it('requires supervisor ID and PIN for in-prep item cancellation and scrubs PIN from memory', async () => {
    const mockItemInPrep: StaffOrderItemDto = {
      id: 'item-uuid-2',
      menuItemId: 'item-1',
      itemName: 'Klasik Burger',
      quantity: 1,
      unitPriceMinorUnits: 25000,
      modifierTotalMinorUnits: 0,
      lineTotalMinorUnits: 25000,
      taxRateBasisPoints: 1000,
      status: 'InPreparation',
      createdAtUtc: new Date().toISOString(),
      modifiers: [],
    };

    let cancelPayload: Record<string, unknown> | undefined;
    vi.spyOn(global, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/cancel')) {
        cancelPayload = JSON.parse(init?.body as string);
        return new Response(JSON.stringify({ ...mockOrder, status: 'Cancelled' }), { status: 200 });
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    const onItemCancelled = vi.fn();
    const onClose = vi.fn();

    render(
      <CancelItemModal
        isOpen={true}
        branchId="branch-1"
        sessionId="session-1"
        order={mockOrder}
        item={mockItemInPrep}
        terminalContext={mockTerminalContextWaiter}
        onClose={onClose}
        onItemCancelled={onItemCancelled}
      />
    );

    expect(screen.getByText('Yönetici Kalem İptali (Supervisor Void)')).toBeDefined();
    expect(screen.getByText(/Yönetici Onayı gereklidir/)).toBeDefined();

    fireEvent.click(screen.getByText('Kalemi İptal Et'));
    expect(screen.getByText(/Lütfen yönetici \(Supervisor\) kullanıcı kimliğini girin/)).toBeDefined();

    const supUserInput = screen.getByPlaceholderText(/Yönetici kullanıcı kimliği/);
    fireEvent.change(supUserInput, { target: { value: 'supervisor-mgr-1' } });

    const pinInput = screen.getByPlaceholderText('••••');
    fireEvent.change(pinInput, { target: { value: '4321' } });

    fireEvent.click(screen.getByText('Kalemi İptal Et'));

    await waitFor(() => {
      expect(onItemCancelled).toHaveBeenCalled();
      expect(onClose).toHaveBeenCalled();
    });

    expect(cancelPayload).toBeDefined();
    const payload = cancelPayload as unknown as CancelOrderItemRequest;
    expect(payload.supervisorUserId).toBe('supervisor-mgr-1');
    expect(payload.supervisorPin).toBe('4321');
    expect(payload.concurrencyToken).toBe('token-ord-1');

    expect(localStorage.getItem('supervisorPin')).toBeNull();
    expect(sessionStorage.getItem('supervisorPin')).toBeNull();
  });
});
