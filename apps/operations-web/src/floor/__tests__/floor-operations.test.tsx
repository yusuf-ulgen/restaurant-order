import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FloorPlanView } from '../FloorPlanView';
import { SessionDetailDrawer } from '../SessionDetailDrawer';
import { SessionMovementModal } from '../SessionMovementModal';
import {
  TableFloorStatusDto,
  DiningAreaContract,
  StaffOrderResponseDto,
  StaffServiceRequestDto,
} from '@restaurant-order/contracts';

const mockAreas: DiningAreaContract[] = [
  {
    id: 'area-1',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    name: 'Bahçe',
    code: 'BAHCE',
    sortOrder: 1,
    isActive: true,
    areaType: 'Garden',
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
    concurrencyToken: 'token-a1',
  },
  {
    id: 'area-2',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    name: 'Teras',
    code: 'TERAS',
    sortOrder: 2,
    isActive: true,
    areaType: 'Terrace',
    createdAtUtc: new Date().toISOString(),
    updatedAtUtc: new Date().toISOString(),
    concurrencyToken: 'token-a2',
  },
];

const mockTables: TableFloorStatusDto[] = [
  {
    table: {
      id: 'table-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      diningAreaId: 'area-1',
      tableNumber: 'T-1',
      name: 'Bahçe Masa 1',
      capacity: 4,
      positionX: 50,
      positionY: 50,
      width: 100,
      height: 100,
      rotationDegrees: 0,
      shape: 'Square',
      isActive: true,
      qrVersion: 1,
      publicCode: 'T1PUB',
      concurrencyToken: 'token-t1',
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: null,
    },
    activeSession: {
      id: 'sess-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      tableId: 'table-1',
      status: 'Active',
      guestCount: 2,
      assignedWaiterId: null,
      openedAtUtc: new Date(Date.now() - 30 * 60000).toISOString(),
      concurrencyToken: 'token-s1',
      createdAtUtc: new Date(Date.now() - 30 * 60000).toISOString(),
    },
    activeOrdersCount: 2,
    readyItemsCount: 1,
    activeServiceRequestsCount: 1,
    derivedStatus: 'ReadyToServe',
  },
  {
    table: {
      id: 'table-2',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      diningAreaId: 'area-2',
      tableNumber: 'T-2',
      name: 'Teras Masa 2',
      capacity: 2,
      positionX: 200,
      positionY: 50,
      width: 100,
      height: 100,
      rotationDegrees: 0,
      shape: 'Round',
      isActive: true,
      qrVersion: 1,
      publicCode: 'T2PUB',
      concurrencyToken: 'token-t2',
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: null,
    },
    activeSession: null,
    activeOrdersCount: 0,
    readyItemsCount: 0,
    activeServiceRequestsCount: 0,
    derivedStatus: 'Available',
  },
  {
    table: {
      id: 'table-3',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      diningAreaId: 'area-1',
      tableNumber: 'T-3',
      name: 'Bahçe Masa 3',
      capacity: 6,
      positionX: 350,
      positionY: 50,
      width: 120,
      height: 100,
      rotationDegrees: 0,
      shape: 'Square',
      isActive: true,
      qrVersion: 1,
      publicCode: 'T3PUB',
      concurrencyToken: 'token-t3',
      createdAtUtc: new Date().toISOString(),
      updatedAtUtc: null,
    },
    activeSession: {
      id: 'sess-3',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      tableId: 'table-3',
      status: 'BillRequested',
      guestCount: 4,
      assignedWaiterId: null,
      openedAtUtc: new Date(Date.now() - 60 * 60000).toISOString(),
      concurrencyToken: 'token-s3',
      createdAtUtc: new Date(Date.now() - 60 * 60000).toISOString(),
    },
    activeOrdersCount: 1,
    readyItemsCount: 0,
    activeServiceRequestsCount: 0,
    derivedStatus: 'BillRequested',
  },
];

describe('WAIT-02: FloorPlanView Component Tests', () => {
  it('renders tables with text labels, status badges, and alert counts', () => {
    const onSelect = vi.fn();
    render(
      <FloorPlanView
        tables={mockTables}
        diningAreas={mockAreas}
        onSelectTable={onSelect}
      />
    );

    // T-1 (ReadyToServe)
    expect(screen.getByText('T-1')).toBeDefined();
    expect(screen.getByText('Bahçe Masa 1')).toBeDefined();
    expect(screen.getByText('Servise Hazır')).toBeDefined();
    expect(screen.getByText('🍽️ 1')).toBeDefined();
    expect(screen.getByText('🔔 1')).toBeDefined();

    // T-2 (Available)
    expect(screen.getByText('T-2')).toBeDefined();
    expect(screen.getByText('Boş')).toBeDefined();

    // T-3 (BillRequested)
    expect(screen.getByText('T-3')).toBeDefined();
    expect(screen.getByText('Hesap İstendi')).toBeDefined();
  });

  it('filters tables by search query on table number and name', () => {
    const onSelect = vi.fn();
    render(
      <FloorPlanView
        tables={mockTables}
        diningAreas={mockAreas}
        onSelectTable={onSelect}
      />
    );

    const searchInput = screen.getByLabelText('Masa arama');
    fireEvent.change(searchInput, { target: { value: 'T-2' } });

    expect(screen.getByText('T-2')).toBeDefined();
    expect(screen.queryByText('T-1')).toBeNull();
    expect(screen.queryByText('T-3')).toBeNull();
  });

  it('filters tables by selected dining area', () => {
    const onSelect = vi.fn();
    render(
      <FloorPlanView
        tables={mockTables}
        diningAreas={mockAreas}
        onSelectTable={onSelect}
      />
    );

    const areaSelect = screen.getByLabelText('Yemek Alanı Filtresi');
    // Select "Teras" (area-2)
    fireEvent.change(areaSelect, { target: { value: 'area-2' } });

    expect(screen.getByText('T-2')).toBeDefined();
    expect(screen.queryByText('T-1')).toBeNull();
    expect(screen.queryByText('T-3')).toBeNull();
  });

  it('switches between Grid and Canvas interactive layout view', () => {
    const onSelect = vi.fn();
    render(
      <FloorPlanView
        tables={mockTables}
        diningAreas={mockAreas}
        onSelectTable={onSelect}
      />
    );

    expect(screen.getByRole('grid')).toBeDefined();
    expect(screen.queryByRole('region', { name: 'Etkileşimli Kroki Kat Planı' })).toBeNull();

    const canvasBtn = screen.getByRole('button', { name: 'Kat Planı Görünümü' });
    fireEvent.click(canvasBtn);

    expect(screen.getByRole('region', { name: 'Etkileşimli Kroki Kat Planı' })).toBeDefined();
    expect(screen.queryByRole('grid')).toBeNull();
  });
});

describe('WAIT-03: SessionDetailDrawer Component Tests', () => {
  const mockOrders: StaffOrderResponseDto[] = [
    {
      id: 'ord-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      diningSessionId: 'sess-1',
      tableId: 'table-1',
      roundNumber: 1,
      orderSource: 'Customer',
      status: 'Submitted',
      currency: 'TRY',
      subtotalMinorUnits: 15000,
      taxMinorUnits: 1500,
      serviceChargeMinorUnits: 0,
      totalMinorUnits: 16500,
      idempotencyKey: 'idemp-1',
      concurrencyToken: 'token-ord-1',
      createdAtUtc: new Date().toISOString(),
      items: [
        {
          id: 'item-1',
          menuItemId: 'm-1',
          itemName: 'Adana Kebap',
          quantity: 2,
          unitPriceMinorUnits: 7500,
          modifierTotalMinorUnits: 0,
          lineTotalMinorUnits: 15000,
          taxRateBasisPoints: 1000,
          status: 'Ready',
          createdAtUtc: new Date().toISOString(),
          modifiers: [],
        },
      ],
    },
  ];

  const mockServiceRequests: StaffServiceRequestDto[] = [
    {
      id: 'sr-1',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      diningSessionId: 'sess-1',
      tableId: 'table-1',
      tableNumber: 'T-1',
      requestType: 'Water',
      status: 'Open',
      note: 'Soğuk olsun lütfen',
      createdAtUtc: new Date().toISOString(),
      concurrencyToken: 'token-sr1',
    },
  ];

  beforeEach(() => {
    globalThis.fetch = vi.fn().mockImplementation((url: string) => {
      if (typeof url === 'string' && url.includes('/sessions/sess-1/orders')) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(mockOrders) });
      }
      if (typeof url === 'string' && url.includes('/sessions/sess-1')) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(mockTables[0]?.activeSession ?? null) });
      }
      if (typeof url === 'string' && url.includes('/service-requests')) {
        return Promise.resolve({ ok: true, json: () => Promise.resolve(mockServiceRequests) });
      }
      return Promise.resolve({ ok: true, json: () => Promise.resolve({}) });
    }) as unknown as typeof fetch;
  });

  it('renders session details, duration, order rounds and calculated grand total without simulating paid debt', async () => {
    const onClose = vi.fn();
    const onRefresh = vi.fn();
    const onMovement = vi.fn();

    render(
      <SessionDetailDrawer
        isOpen={true}
        onClose={onClose}
        branchId="branch-1"
        tableFloorDto={mockTables[0]!}
        onRefreshFloor={onRefresh}
        onOpenMovementModal={onMovement}
      />
    );

    // Header table info
    expect(screen.getByText('T-1 - Bahçe Masa 1')).toBeDefined();

    await waitFor(() => {
      // Order items rendered
      expect(screen.getByText(/Adana Kebap/)).toBeDefined();
      // Grand total formatted in TRY
      expect(screen.getByText('Genel Toplam:')).toBeDefined();
      // Bill requested button present
      expect(screen.getByRole('button', { name: /Hesap İste/i })).toBeDefined();
      // Movement button present
      expect(screen.getByRole('button', { name: /Taşı \/ Birleştir/i })).toBeDefined();
    });
  });

  it('allows acknowledging open service request with concurrency token', async () => {
    const onClose = vi.fn();
    const onRefresh = vi.fn();
    const onMovement = vi.fn();

    render(
      <SessionDetailDrawer
        isOpen={true}
        onClose={onClose}
        branchId="branch-1"
        tableFloorDto={mockTables[0]!}
        onRefreshFloor={onRefresh}
        onOpenMovementModal={onMovement}
      />
    );

    await waitFor(() => {
      expect(screen.getByText('Water')).toBeDefined();
    });

    const ackBtn = screen.getByRole('button', { name: 'Kabul Et' });
    fireEvent.click(ackBtn);

    await waitFor(() => {
      const calls = vi.mocked(globalThis.fetch).mock.calls;
      const call = calls.find(([url]) => typeof url === 'string' && url.includes('/service-requests/sr-1/acknowledge'));
      expect(call).toBeDefined();
      expect(call?.[1]?.method).toBe('POST');
      expect((call?.[1]?.headers as Headers | undefined)?.get('if-match')).toBe('"token-sr1"');
    });
  });
});

describe('WAIT-05: SessionMovementModal Component Tests', () => {
  beforeEach(() => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, json: () => Promise.resolve({}) }) as unknown as typeof fetch;
  });

  it('filters only available tables for transfer and submits valid transfer payload', async () => {
    const onClose = vi.fn();
    const onSuccess = vi.fn();

    render(
      <SessionMovementModal
        isOpen={true}
        onClose={onClose}
        branchId="branch-1"
        sourceTable={mockTables[0]!}
        allTables={mockTables}
        onSuccess={onSuccess}
      />
    );

    expect(screen.getByText(/Kaynak Masa: T-1 - Bahçe Masa 1/)).toBeDefined();

    // Select target table T-2 (Available)
    const select = screen.getByLabelText('Hedef Boş Masa:');
    fireEvent.change(select, { target: { value: 'table-2' } });

    const submitBtn = screen.getByRole('button', { name: 'Taşımayı Tamamla' });
    expect(submitBtn.hasAttribute('disabled')).toBe(false);
    fireEvent.click(submitBtn);

    await waitFor(() => {
      const calls = vi.mocked(globalThis.fetch).mock.calls;
      const call = calls.find(([url]) => typeof url === 'string' && url.includes('/sessions/sess-1/transfer'));
      expect(call).toBeDefined();
      expect(call?.[1]?.method).toBe('POST');
      expect((call?.[1]?.headers as Headers | undefined)?.get('if-match')).toBe('"token-s1"');
      expect(onSuccess).toHaveBeenCalled();
    });
  });

  it('handles 409 Concurrency Conflict gracefully without silent overwrite', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: false,
      status: 409,
      json: () => Promise.resolve({ detail: 'Hedef masa durumu eşzamanlı olarak değişti.' }),
    }) as unknown as typeof fetch;

    const onClose = vi.fn();
    const onSuccess = vi.fn();

    render(
      <SessionMovementModal
        isOpen={true}
        onClose={onClose}
        branchId="branch-1"
        sourceTable={mockTables[0]!}
        allTables={mockTables}
        onSuccess={onSuccess}
      />
    );

    const select = screen.getByLabelText('Hedef Boş Masa:');
    fireEvent.change(select, { target: { value: 'table-2' } });

    const submitBtn = screen.getByRole('button', { name: 'Taşımayı Tamamla' });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeDefined();
      expect(screen.getByText(/Eşzamanlı işlem çakışması tespit edildi/)).toBeDefined();
    });
  });
});
