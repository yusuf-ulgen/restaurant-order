import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { FloorLayoutAndQrView } from '../FloorLayoutAndQrView';
import { floorApi } from '../floorApi';
import * as AdminConfigContext from '../../config/AdminConfigContext';

vi.mock('../../config/AdminConfigContext', () => ({
  useAdminConfig: vi.fn(),
}));

const mockBranch = {
  id: 'branch-1',
  name: 'Kadıköy Şubesi',
  code: 'KDK-01',
};

const mockAreas = [
  {
    id: 'area-1',
    branchId: 'branch-1',
    name: 'İç Salon',
    code: 'ic-salon',
    areaType: 'Indoor' as const,
    sortOrder: 0,
    isActive: true,
    tenantId: 't-1',
    createdAtUtc: '2026-10-01T00:00:00Z',
    updatedAtUtc: '2026-10-01T00:00:00Z',
    concurrencyToken: 'ct-a1',
  },
  {
    id: 'area-2',
    branchId: 'branch-1',
    name: 'Bahçe',
    code: 'bahce',
    areaType: 'Garden' as const,
    sortOrder: 1,
    isActive: true,
    tenantId: 't-1',
    createdAtUtc: '2026-10-01T00:00:00Z',
    updatedAtUtc: '2026-10-01T00:00:00Z',
    concurrencyToken: 'ct-a2',
  },
];

const mockTables = [
  {
    id: 'tbl-1',
    tenantId: 't-1',
    branchId: 'branch-1',
    diningAreaId: 'area-1',
    tableNumber: 'T-01',
    name: 'Salon 1',
    capacity: 4,
    shape: 'Square' as const,
    positionX: 100,
    positionY: 100,
    width: 80,
    height: 80,
    rotationDegrees: 0,
    isActive: true,
    qrVersion: 1,
    publicCode: 'T-01',
    createdAtUtc: '2026-10-01T00:00:00Z',
    concurrencyToken: 'ct-t1',
  },
];

describe('FloorLayoutAndQrView Component', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    (AdminConfigContext.useAdminConfig as any).mockReturnValue({
      selectedBranchId: 'branch-1',
      selectedBranch: mockBranch,
    });
    vi.spyOn(floorApi, 'getDiningAreas').mockResolvedValue(mockAreas);
    vi.spyOn(floorApi, 'getTables').mockResolvedValue(mockTables);
    vi.spyOn(floorApi, 'getFloorStatus').mockResolvedValue({ branchId: 'branch-1', tables: [] });
  });

  it('renders dining area tabs and canvas for the selected branch', async () => {
    render(<FloorLayoutAndQrView />);

    await waitFor(() => {
      expect(screen.getByTestId('area-tab-area-1')).toBeDefined();
      expect(screen.getByTestId('area-tab-area-2')).toBeDefined();
    });

    expect(screen.getByText('Masa Düzeni & QR Yönetimi')).toBeDefined();
    expect(screen.getByTestId('create-table-btn')).toBeDefined();
  });

  it('switches between Layout and QR Generator tabs', async () => {
    render(<FloorLayoutAndQrView />);

    await waitFor(() => {
      expect(screen.getByTestId('create-table-btn')).toBeDefined();
    });

    const qrTabBtn = screen.getByTestId('tab-qr-btn');
    fireEvent.click(qrTabBtn);

    await waitFor(() => {
      expect(screen.getByTestId('qr-generator-view')).toBeDefined();
    });
  });

  it('opens TableEditorSheet when "Yeni Masa Ekle" is clicked', async () => {
    render(<FloorLayoutAndQrView />);

    await waitFor(() => {
      expect(screen.getByTestId('create-table-btn')).toBeDefined();
    });

    const addBtn = screen.getByTestId('create-table-btn');
    fireEvent.click(addBtn);

    expect(screen.getByText('Yeni Masa Ekle')).toBeDefined();
  });

  it('handles empty branch selection state cleanly', () => {
    (AdminConfigContext.useAdminConfig as any).mockReturnValue({
      selectedBranchId: null,
      selectedBranch: null,
    });

    render(<FloorLayoutAndQrView />);

    expect(screen.getByText('Şube Seçilmedi')).toBeDefined();
  });
});
