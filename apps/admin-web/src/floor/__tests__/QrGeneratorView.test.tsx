import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QrGeneratorView } from '../QrGeneratorView';
import { floorApi } from '../floorApi';
import type { RestaurantTableDto, TableFloorStatusDto } from '@restaurant-order/contracts';

const mockTables: RestaurantTableDto[] = [
  {
    id: 'tbl-1',
    tenantId: 'tenant-1',
    branchId: 'br-1',
    diningAreaId: 'area-1',
    tableNumber: 'T-01',
    name: 'Salon 1',
    capacity: 4,
    shape: 'Square',
    positionX: 100,
    positionY: 100,
    width: 80,
    height: 80,
    rotationDegrees: 0,
    isActive: true,
    qrVersion: 1,
    publicCode: 'T-01',
    createdAtUtc: '2026-10-01T00:00:00Z',
    concurrencyToken: 'ct-1',
  },
  {
    id: 'tbl-2',
    tenantId: 'tenant-1',
    branchId: 'br-1',
    diningAreaId: 'area-1',
    tableNumber: 'T-02',
    name: 'Salon 2',
    capacity: 2,
    shape: 'Round',
    positionX: 200,
    positionY: 100,
    width: 80,
    height: 80,
    rotationDegrees: 0,
    isActive: true,
    qrVersion: 1,
    publicCode: 'T-02',
    createdAtUtc: '2026-10-01T00:00:00Z',
    concurrencyToken: 'ct-2',
  },
];

const mockStatuses: TableFloorStatusDto[] = [
  {
    table: mockTables[0]!,
    activeSession: {
      id: 'sess-100',
      tenantId: 'tenant-1',
      branchId: 'br-1',
      tableId: 'tbl-1',
      status: 'Active',
      guestCount: 2,
      openedAtUtc: '2026-10-10T12:00:00Z',
      concurrencyToken: 'ct-s',
      createdAtUtc: '2026-10-10T12:00:00Z',
    },
  },
  {
    table: mockTables[1]!,
    activeSession: null,
  },
];

describe('QrGeneratorView Component', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.spyOn(floorApi, 'getTableStaticQr').mockResolvedValue({
      tableId: 'tbl-1',
      publicCode: 'T-01',
      qrVersion: 1,
      mode: 'static',
      token: 'token-xyz',
      svg: '<svg data-testid="mock-svg"><rect width="100" height="100" /></svg>',
    });
  });

  it('renders table selector and loads static QR code preview', async () => {
    const handleRotated = vi.fn();

    render(
      <QrGeneratorView
        branchId="br-1"
        branchName="Kadıköy Şubesi"
        tables={mockTables}
        statuses={mockStatuses as any}
        onTableRotated={handleRotated}
      />
    );

    expect(screen.getByTestId('qr-generator-view')).toBeDefined();
    await waitFor(() => {
      expect(screen.getByTestId('qr-svg-preview')).toBeDefined();
    });
    expect(screen.getByText(/Kadıköy Şubesi/i)).toBeDefined();
    expect(screen.getByText(/Masa T-01/i)).toBeDefined();
  });

  it('allows switching to dynamic QR when table has active session', async () => {
    vi.spyOn(floorApi, 'getSessionDynamicQr').mockResolvedValue({
      sessionId: 'sess-100',
      tableId: 'tbl-1',
      publicCode: 'T-01',
      mode: 'dynamic',
      token: 'dynamic-token',
      svg: '<svg data-testid="dynamic-mock-svg"></svg>',
      expiresAt: '2026-10-10T14:00:00Z',
    });

    const handleRotated = vi.fn();

    render(
      <QrGeneratorView
        branchId="br-1"
        branchName="Kadıköy Şubesi"
        tables={mockTables}
        statuses={[
          {
            table: mockTables[0]!,
            activeSession: {
              id: 'sess-100',
              tenantId: 't-1',
              branchId: 'br-1',
              tableId: 'tbl-1',
              status: 'Active',
              guestCount: 2,
              openedAtUtc: '2026-10-10T12:00:00Z',
              concurrencyToken: 'ct-s',
              createdAtUtc: '2026-10-10T12:00:00Z',
            },
          },
        ]}
        onTableRotated={handleRotated}
      />
    );

    await waitFor(() => {
      expect(screen.getByTestId('qr-svg-preview')).toBeDefined();
    });

    const dynamicTabBtn = screen.getByText(/Dinamik Oturum QR/i);
    fireEvent.click(dynamicTabBtn);

    await waitFor(() => {
      expect(screen.getByTestId('qr-svg-preview')).toBeDefined();
    });
  });

  it('opens confirmation modal before rotating/revoking QR', async () => {
    const handleRotated = vi.fn();

    render(
      <QrGeneratorView
        branchId="br-1"
        branchName="Kadıköy Şubesi"
        tables={mockTables}
        statuses={mockStatuses as any}
        onTableRotated={handleRotated}
      />
    );

    await waitFor(() => {
      expect(screen.getByTestId('rotate-qr-btn')).toBeDefined();
    });

    const rotateBtn = screen.getByTestId('rotate-qr-btn');
    fireEvent.click(rotateBtn);

    expect(screen.getByText(/mevcut QR kodunu geçersiz kılmak üzeresiniz/i)).toBeDefined();
  });

  it('renders batch print section with explicit print instruction (no fake download PDF)', () => {
    const handleRotated = vi.fn();

    render(
      <QrGeneratorView
        branchId="br-1"
        branchName="Kadıköy Şubesi"
        tables={mockTables}
        statuses={mockStatuses as any}
        onTableRotated={handleRotated}
      />
    );

    // Look for print preview button
    expect(screen.getByTestId('batch-print-btn')).toBeDefined();
  });
});
