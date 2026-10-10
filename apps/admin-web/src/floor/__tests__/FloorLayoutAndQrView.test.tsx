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
    vi.mocked(AdminConfigContext.useAdminConfig).mockReturnValue({
      selectedBranchId: 'branch-1',
      selectedBranch: mockBranch,
    } as unknown as ReturnType<typeof AdminConfigContext.useAdminConfig>);
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
    vi.mocked(AdminConfigContext.useAdminConfig).mockReturnValue({
      selectedBranchId: null,
      selectedBranch: null,
    } as unknown as ReturnType<typeof AdminConfigContext.useAdminConfig>);

    render(<FloorLayoutAndQrView />);

    expect(screen.getByText('Şube Seçilmedi')).toBeDefined();
  });

  it('switches dining area tabs and triggers table creation save', async () => {
    vi.spyOn(floorApi, 'createTable').mockResolvedValue({
      id: 'tbl-new',
      tenantId: 'tenant-1',
      branchId: 'branch-1',
      diningAreaId: 'area-2',
      tableNumber: 'T-99',
      name: 'Yeni Masa',
      capacity: 4,
      shape: 'Square',
      positionX: 50,
      positionY: 50,
      width: 80,
      height: 80,
      rotationDegrees: 0,
      isActive: true,
      qrVersion: 1,
      publicCode: 'code-99',
      createdAtUtc: '2026-10-10T00:00:00Z',
      concurrencyToken: 'ct-new',
    });

    render(<FloorLayoutAndQrView />);

    await waitFor(() => {
      expect(screen.getByTestId('area-tab-area-2')).toBeDefined();
    });

    // Switch area tab
    fireEvent.click(screen.getByTestId('area-tab-area-2'));

    // Open create sheet
    fireEvent.click(screen.getByTestId('create-table-btn'));

    const numInput = screen.getByLabelText(/Masa Kodu \/ Numarası/i);
    const nameInput = screen.getByLabelText(/Masa Görünen Adı/i);
    fireEvent.change(numInput, { target: { value: 'T-99' } });
    fireEvent.change(nameInput, { target: { value: 'Yeni Masa' } });

    fireEvent.click(screen.getByText('Masa Oluştur'));

    await waitFor(() => {
      expect(floorApi.createTable).toHaveBeenCalledWith(
        'branch-1',
        expect.objectContaining({ tableNumber: 'T-99', name: 'Yeni Masa' })
      );
    });
  });

  it('selects table on canvas, opens editor, saves update and toggles active status', async () => {
    vi.spyOn(floorApi, 'updateTable').mockResolvedValue({
      ...mockTables[0]!,
      name: 'Güncel Masa 1',
    });
    vi.spyOn(floorApi, 'deactivateTable').mockResolvedValue({
      ...mockTables[0]!,
      isActive: false,
    });

    render(<FloorLayoutAndQrView />);

    await waitFor(() => {
      expect(screen.getByTestId('floor-table-tbl-1')).toBeDefined();
    });

    // Click table on canvas to select
    fireEvent.click(screen.getByTestId('floor-table-tbl-1'));

    // Open table inspector edit details button
    await waitFor(() => {
      expect(screen.getByTestId('edit-table-details-btn')).toBeDefined();
    });

    fireEvent.click(screen.getByTestId('edit-table-details-btn'));

    // Modify name and save
    const nameInput = screen.getByLabelText(/Masa Görünen Adı/i);
    fireEvent.change(nameInput, { target: { value: 'Güncel Masa 1' } });
    fireEvent.click(screen.getByText('Değişiklikleri Kaydet'));

    await waitFor(() => {
      expect(floorApi.updateTable).toHaveBeenCalledWith(
        'branch-1',
        'tbl-1',
        expect.objectContaining({ name: 'Güncel Masa 1' }),
        'ct-t1'
      );
    });

    // Re-open editor to toggle active status
    fireEvent.click(screen.getByTestId('edit-table-details-btn'));
    const toggleBtn = screen.getByRole('button', { name: /Pasife Al/i });
    fireEvent.click(toggleBtn);

    await waitFor(() => {
      expect(floorApi.deactivateTable).toHaveBeenCalledWith('branch-1', 'tbl-1', 'ct-t1');
    });
  });
});
