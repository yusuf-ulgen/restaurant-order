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
  pricesIncludeTax: false,
  defaultTaxRateBps: 2000,
  isServiceChargeEnabled: true,
  serviceChargeRateBps: 1000,
  pricingPolicyFingerprint: 'policy-sc-v1',
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
              shortDescription: '150g dana köfte',
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
  lockTerminal: vi.fn(),
  deactivateTerminal: vi.fn(),
};

describe('WAIT-04 Staff Order Idempotency, Freezing & Reload Recovery', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
  });

  it('preserves exact idempotency key and frozen payload across network retry when service charge and tax excluded', async () => {
    const sentIdempotencyKeys: string[] = [];
    const sentExpectedTotals: number[] = [];
    let attemptCount = 0;

    vi.spyOn(global, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/runtime-menu')) {
        return new Response(JSON.stringify(mockMenu), { status: 200 });
      }
      if (url.includes('/orders')) {
        attemptCount++;
        const headers = new Headers(init?.headers);
        const body = JSON.parse(init?.body as string) as CreateStaffOrderRequest;
        sentIdempotencyKeys.push(headers.get('idempotency-key') || '');
        sentExpectedTotals.push(body.clientExpectedTotalMinorUnits);

        if (attemptCount === 1) {
          throw new TypeError('Failed to fetch / network timeout');
        }

        const orderResponse: StaffOrderResponseDto = {
          id: 'order-round-retry',
          tenantId: 'tenant-1',
          branchId: 'branch-1',
          diningSessionId: mockSession.id,
          tableId: 'table-1',
          roundNumber: 1,
          orderSource: 'Staff',
          status: 'Submitted',
          currency: 'TRY',
          subtotalMinorUnits: 25000,
          taxMinorUnits: 5000,
          serviceChargeMinorUnits: 3000,
          totalMinorUnits: 33000,
          idempotencyKey: headers.get('idempotency-key') || '',
          concurrencyToken: 'token-ord-retry',
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
      // 250.00 base + 50.00 tax (20%) + 30.00 service charge (10% of 300) = 330.00
      expect(screen.getByText(/Siparişi Onayla ve Gönder \(₺330.00\)/)).toBeDefined();
    });

    // 1. Gönderim denemesi (Ağ hatası alacak)
    fireEvent.click(screen.getByText(/Siparişi Onayla ve Gönder/));

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeDefined();
    });

    expect(sentIdempotencyKeys).toHaveLength(1);
    expect(sentExpectedTotals[0]).toBe(33000);

    // 2. Aynı ekrandan retry yapılıyor
    fireEvent.click(screen.getByText(/Siparişi Onayla ve Gönder/));

    await waitFor(() => {
      expect(onOrderCreated).toHaveBeenCalled();
      expect(onClose).toHaveBeenCalled();
    });

    expect(sentIdempotencyKeys).toHaveLength(2);
    expect(sentIdempotencyKeys[1]).toBe(sentIdempotencyKeys[0]);
    expect(sentExpectedTotals[1]).toBe(33000);
  });

  it('recovers frozen pending submission with exact idempotency key and payload on drawer reload', async () => {
    const storageKey = `ro_staff_order_draft_tenant-1_branch-1_session-1234_staff-waiter-1`;
    localStorage.setItem(
      storageKey,
      JSON.stringify({
        tenantId: 'tenant-1',
        branchId: 'branch-1',
        sessionId: mockSession.id,
        staffUserId: 'staff-waiter-1',
        items: [
          {
            id: 'line-preloaded',
            menuItemId: 'item-1',
            itemName: 'Klasik Burger',
            variantId: null,
            variantName: null,
            selectedModifierOptionIds: [],
            selectedModifiers: [],
            quantity: 1,
            customerNote: null,
            unitPriceMinorUnits: 25000,
            lineTotalMinorUnits: 25000,
          },
        ],
        idempotencyKey: 'frozen-idempotency-key-12345',
        lastPayloadSignature: 'frozen-signature-12345',
        isPendingSubmission: true,
        updatedAt: Date.now(),
      })
    );

    let submittedIdempotencyKey: string | null = null;

    vi.spyOn(global, 'fetch').mockImplementation(async (input, init) => {
      const url = typeof input === 'string' ? input : (input as Request).url;
      if (url.includes('/runtime-menu')) {
        return new Response(JSON.stringify(mockMenu), { status: 200 });
      }
      if (url.includes('/orders')) {
        const headers = new Headers(init?.headers);
        submittedIdempotencyKey = headers.get('idempotency-key');
        return new Response(JSON.stringify({ id: 'recovered-ord' }), { status: 201 });
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
      expect(screen.getByText(/Siparişi Onayla ve Gönder/)).toBeDefined();
    });

    fireEvent.click(screen.getByText(/Siparişi Onayla ve Gönder/));

    await waitFor(() => {
      expect(submittedIdempotencyKey).toBe('frozen-idempotency-key-12345');
    });
  });
});
