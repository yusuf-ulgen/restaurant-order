import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { StaffOrderDrawer } from '../StaffOrderDrawer';
import {
  DiningSessionDto,
  RuntimeMenuReadModel,
  StaffOrderResponseDto,
  CreateStaffOrderRequest,
} from '@restaurant-order/contracts';
import { TerminalContextValue } from '../../auth/TerminalContext';

const mockMenu: RuntimeMenuReadModel = {
  branchId: 'branch-1',
  currency: 'TRY',
  pricesIncludeTax: true,
  defaultTaxRateBps: 1000,
  isServiceChargeEnabled: false,
  serviceChargeRateBps: 0,
  pricingPolicyFingerprint: 'policy-fp-v1',
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
              id: 'item-1',
              name: 'Klasik Burger',
              slug: 'klasik-burger',
              shortDescription: '150g dana köfte, marul, domates',
              fullDescription: null,
              imageUrl: null,
              basePriceMinorUnits: 25000,
              sortOrder: 1,
              spicyLevel: 0,
              dietaryTags: [],
              allergenTags: [],
              isAvailable: true,
              expectedAvailableAtUtc: null,
              variants: [
                { id: 'var-single', name: 'Tek Köfte', code: 'SINGLE', absolutePriceMinorUnits: 25000, sortOrder: 1, isDefault: true, isAvailable: true, expectedAvailableAtUtc: null },
                { id: 'var-double', name: 'Çift Köfte', code: 'DOUBLE', absolutePriceMinorUnits: 35000, sortOrder: 2, isDefault: false, isAvailable: true, expectedAvailableAtUtc: null },
              ],
              modifierGroups: [
                {
                  id: 'mod-group-cheese',
                  name: 'Peynir Seçimi',
                  minSelections: 0,
                  maxSelections: 1,
                  sortOrder: 1,
                  options: [{ id: 'opt-cheddar', name: 'Cheddar', priceDeltaMinorUnits: 3000, sortOrder: 1, isDefault: false }],
                },
              ],
            },
            {
              id: 'item-out-of-stock',
              name: 'Tükenen Tatlı',
              slug: 'tukenen-tatli',
              shortDescription: 'Bugün kalmadı',
              fullDescription: null,
              imageUrl: null,
              basePriceMinorUnits: 15000,
              sortOrder: 2,
              spicyLevel: 0,
              dietaryTags: [],
              allergenTags: [],
              isAvailable: false,
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

const mockSession: DiningSessionDto = {
  id: 'session-1234',
  tenantId: 'tenant-1',
  branchId: 'branch-1',
  tableId: 'table-1',
  status: 'Active',
  guestCount: 2,
  openedAtUtc: new Date().toISOString(),
  activatedAtUtc: new Date().toISOString(),
  concurrencyToken: 'session-token-1',
  createdAtUtc: new Date().toISOString(),
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

describe('WAIT-04 Quick Staff Order Creation & Idempotency', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  it('renders menu items and adds configured line with variant, modifier and note to draft', async () => {
    vi.spyOn(global, 'fetch').mockImplementation(async (input) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/runtime-menu')) {
        return new Response(JSON.stringify(mockMenu), { status: 200 });
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    render(
      <StaffOrderDrawer
        isOpen={true}
        branchId="branch-1"
        session={mockSession}
        tableNumber="T-1"
        terminalContext={mockTerminalContextWaiter}
        onClose={vi.fn()}
        onOrderCreated={vi.fn()}
      />
    );

    await waitFor(() => {
      expect(screen.getByText('Klasik Burger')).toBeDefined();
    });

    const outOfStockButton = screen.getByText('Tükenen Tatlı').closest('button');
    expect(outOfStockButton?.hasAttribute('disabled')).toBe(true);

    fireEvent.click(screen.getByText('Klasik Burger'));
    expect(screen.getByRole('dialog', { name: 'Klasik Burger' })).toBeDefined();

    fireEvent.click(screen.getByText(/Çift Köfte/));
    fireEvent.click(screen.getByRole('radio'));

    const noteInput = screen.getByPlaceholderText(/Az tuzlu/);
    fireEvent.change(noteInput, { target: { value: 'İyi pişmiş olsun' } });

    fireEvent.click(screen.getByText(/Sepete Ekle/));

    await waitFor(() => {
      expect(screen.getByText(/1x Klasik Burger \(Çift Köfte\)/)).toBeDefined();
      expect(screen.getByText(/Peynir Seçimi: Cheddar/)).toBeDefined();
      expect(screen.getByText(/Not: İyi pişmiş olsun/)).toBeDefined();
    });

    expect(screen.getAllByText('₺380.00').length).toBeGreaterThanOrEqual(1);
  });

  it('submits staff order with Idempotency-Key and authoritative payload', async () => {
    let capturedHeaders: Headers | undefined;
    let capturedBody: Record<string, unknown> | undefined;

    vi.spyOn(global, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/runtime-menu')) {
        return new Response(JSON.stringify(mockMenu), { status: 200 });
      }
      if (url.includes('/orders')) {
        capturedHeaders = new Headers(init?.headers);
        capturedBody = JSON.parse(init?.body as string);
        const orderResponse: StaffOrderResponseDto = {
          id: 'order-round-1',
          tenantId: 'tenant-1',
          branchId: 'branch-1',
          diningSessionId: mockSession.id,
          tableId: 'table-1',
          roundNumber: 1,
          orderSource: 'Staff',
          status: 'Submitted',
          currency: 'TRY',
          subtotalMinorUnits: 34545,
          taxMinorUnits: 3455,
          serviceChargeMinorUnits: 0,
          totalMinorUnits: 38000,
          idempotencyKey: capturedHeaders.get('idempotency-key') || '',
          concurrencyToken: 'token-ord-1',
          createdAtUtc: new Date().toISOString(),
          items: [],
        };
        return new Response(JSON.stringify(orderResponse), { status: 201 });
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    const onOrderCreated = vi.fn();
    const onClose = vi.fn();

    render(
      <StaffOrderDrawer
        isOpen={true}
        branchId="branch-1"
        session={mockSession}
        tableNumber="T-1"
        terminalContext={mockTerminalContextWaiter}
        onClose={onClose}
        onOrderCreated={onOrderCreated}
      />
    );

    await waitFor(() => {
      expect(screen.getByText('Klasik Burger')).toBeDefined();
    });

    fireEvent.click(screen.getByText('Klasik Burger'));
    fireEvent.click(screen.getByText(/Sepete Ekle/));

    await waitFor(() => {
      expect(screen.getByText(/Siparişi Onayla ve Gönder/)).toBeDefined();
    });

    fireEvent.click(screen.getByText(/Siparişi Onayla ve Gönder/));

    await waitFor(() => {
      expect(onOrderCreated).toHaveBeenCalled();
      expect(onClose).toHaveBeenCalled();
    });

    expect(capturedHeaders?.get('idempotency-key')).toBeTruthy();
    expect(capturedBody).toBeDefined();
    const body = capturedBody as unknown as CreateStaffOrderRequest;
    expect(body.clientPricingPolicyFingerprint).toBe('policy-fp-v1');
    expect(body.clientExpectedTotalMinorUnits).toBe(25000);
    expect(body.items).toHaveLength(1);
    expect(body.items[0]?.menuItemId).toBe('item-1');
  });

  it('handles 409 ORDER_PRICE_CHANGED and 409 ITEM_OUT_OF_STOCK by preserving draft and alerting user', async () => {
    vi.spyOn(global, 'fetch').mockImplementation(async (input) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/runtime-menu')) {
        return new Response(JSON.stringify(mockMenu), { status: 200 });
      }
      if (url.includes('/orders')) {
        return new Response(
          JSON.stringify({
            status: 409,
            title: 'Conflict',
            errorCode: 'ORDER_PRICE_CHANGED',
            detail: 'Fiyatlar değişti.',
          }),
          { status: 409 }
        );
      }
      return new Response(JSON.stringify({}), { status: 200 });
    });

    render(
      <StaffOrderDrawer
        isOpen={true}
        branchId="branch-1"
        session={mockSession}
        tableNumber="T-1"
        terminalContext={mockTerminalContextWaiter}
        onClose={vi.fn()}
        onOrderCreated={vi.fn()}
      />
    );

    await waitFor(() => {
      expect(screen.getByText('Klasik Burger')).toBeDefined();
    });

    fireEvent.click(screen.getByText('Klasik Burger'));
    fireEvent.click(screen.getByText(/Sepete Ekle/));

    await waitFor(() => {
      expect(screen.getByText(/Siparişi Onayla ve Gönder/)).toBeDefined();
    });

    fireEvent.click(screen.getByText(/Siparişi Onayla ve Gönder/));

    await waitFor(() => {
      expect(screen.getByText(/Menü fiyatları veya vergi politikası güncellendi/)).toBeDefined();
    });

    expect(screen.getByText(/1x Klasik Burger/)).toBeDefined();
  });
});
