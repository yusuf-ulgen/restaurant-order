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
        statuses={mockStatuses}
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
        statuses={mockStatuses}
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

  it('renders batch print section and toggles print mode', async () => {
    const handleRotated = vi.fn();
    vi.spyOn(floorApi, 'getTableStaticQrSvg').mockResolvedValue('<svg><rect /></svg>');

    render(
      <QrGeneratorView
        branchId="br-1"
        branchName="Kadıköy Şubesi"
        tables={mockTables}
        statuses={mockStatuses}
        onTableRotated={handleRotated}
      />
    );

    // Look for print preview button and click it
    const batchBtn = screen.getByTestId('batch-print-btn');
    fireEvent.click(batchBtn);

    await waitFor(() => {
      expect(screen.getByText('Tüm Masalar İçin Baskı Önizlemesi')).toBeDefined();
    });

    // Close preview
    const closeBtn = screen.getByText('Önizlemeyi Kapat');
    fireEvent.click(closeBtn);

    expect(screen.queryByText('Tüm Masalar İçin Baskı Önizlemesi')).toBeNull();
  });

  it('confirms rotation and invokes onTableRotated callback', async () => {
    const handleRotated = vi.fn();
    vi.spyOn(floorApi, 'rotateTableQr').mockResolvedValue({
      ...mockTables[0]!,
      qrVersion: 2,
    });

    render(
      <QrGeneratorView
        branchId="br-1"
        branchName="Kadıköy Şubesi"
        tables={mockTables}
        statuses={mockStatuses}
        onTableRotated={handleRotated}
      />
    );

    await waitFor(() => {
      expect(screen.getByTestId('rotate-qr-btn')).toBeDefined();
    });

    fireEvent.click(screen.getByTestId('rotate-qr-btn'));

    const confirmBtn = screen.getByRole('button', { name: /Evet, İptal Et & Yenile/i });
    fireEvent.click(confirmBtn);

    await waitFor(() => {
      expect(floorApi.rotateTableQr).toHaveBeenCalledWith('br-1', 'tbl-1', 'ct-1');
      expect(handleRotated).toHaveBeenCalled();
    });
  });

  it('triggers SVG download and print handlers without errors', async () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => {});
    const origCreateObjectURL = window.URL.createObjectURL;
    const origRevokeObjectURL = window.URL.revokeObjectURL;
    window.URL.createObjectURL = vi.fn().mockReturnValue('blob:http://localhost/dummy');
    window.URL.revokeObjectURL = vi.fn();

    render(
      <QrGeneratorView
        branchId="br-1"
        branchName="Kadıköy Şubesi"
        tables={mockTables}
        statuses={mockStatuses}
        onTableRotated={vi.fn()}
      />
    );

    await waitFor(() => {
      expect(screen.getByTestId('download-svg-btn')).toBeDefined();
    });

    fireEvent.click(screen.getByTestId('download-svg-btn'));
    fireEvent.click(screen.getByTestId('print-qr-btn'));

    expect(printSpy).toHaveBeenCalled();
    printSpy.mockRestore();
    window.URL.createObjectURL = origCreateObjectURL;
    window.URL.revokeObjectURL = origRevokeObjectURL;
  });
});
