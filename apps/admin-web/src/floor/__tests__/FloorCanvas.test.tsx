import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { FloorCanvas } from '../FloorCanvas';
import type { RestaurantTableDto, TableFloorStatusDto } from '@restaurant-order/contracts';

const mockTables: RestaurantTableDto[] = [
  {
    id: 'tbl-1',
    tenantId: 'tenant-1',
    branchId: 'br-1',
    diningAreaId: 'area-1',
    tableNumber: 'T-01',
    name: 'Salon Masa 1',
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
    name: 'Salon Masa 2',
    capacity: 6,
    shape: 'Round',
    positionX: 300,
    positionY: 200,
    width: 90,
    height: 90,
    rotationDegrees: 45,
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
      id: 'sess-1',
      tenantId: 'tenant-1',
      branchId: 'br-1',
      tableId: 'tbl-1',
      status: 'Active',
      guestCount: 3,
      openedAtUtc: '2026-10-10T12:00:00Z',
      concurrencyToken: 'ct-s1',
      createdAtUtc: '2026-10-10T12:00:00Z',
    },
  },
  {
    table: mockTables[1]!,
    activeSession: null,
  },
];

describe('FloorCanvas Component', () => {
  it('renders SVG floor canvas and all tables with accurate labels', () => {
    const handleSelect = vi.fn();
    const handleSaveBatch = vi.fn();
    const handleEdit = vi.fn();

    render(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId={null}
        onSelectTable={handleSelect}
        onSaveBatchLayout={handleSaveBatch}
        isSaving={false}
        onEditTableDetails={handleEdit}
      />
    );

    expect(screen.getByTestId('floor-canvas-svg')).toBeDefined();
    expect(screen.getByTestId('floor-table-tbl-1')).toBeDefined();
    expect(screen.getByTestId('floor-table-tbl-2')).toBeDefined();

    // Verify session badge derivation: tbl-1 has active session ('Aktif'), tbl-2 is 'Boş'
    expect(screen.getByText('Aktif')).toBeDefined();
    expect(screen.getByText('Boş')).toBeDefined();
  });

  it('selects table on click and shows inspector', () => {
    const handleSelect = vi.fn();
    const handleSaveBatch = vi.fn();
    const handleEdit = vi.fn();

    const { rerender } = render(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId={null}
        onSelectTable={handleSelect}
        onSaveBatchLayout={handleSaveBatch}
        isSaving={false}
        onEditTableDetails={handleEdit}
      />
    );

    const table1 = screen.getByTestId('floor-table-tbl-1');
    fireEvent.click(table1);
    expect(handleSelect).toHaveBeenCalledWith(mockTables[0]);

    // Rerender with selected table to test inspector
    rerender(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId="tbl-1"
        onSelectTable={handleSelect}
        onSaveBatchLayout={handleSaveBatch}
        isSaving={false}
        onEditTableDetails={handleEdit}
      />
    );

    expect(screen.getByText(/Masa T-01/i)).toBeDefined();
    expect(screen.getByLabelText(/X Koordinatı/i)).toBeDefined();
    expect(screen.getByLabelText(/Y Koordinatı/i)).toBeDefined();
  });

  it('supports keyboard navigation (Arrow keys) to move selected table', () => {
    const handleSelect = vi.fn();
    const handleSaveBatch = vi.fn();
    const handleEdit = vi.fn();

    render(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId="tbl-1"
        onSelectTable={handleSelect}
        onSaveBatchLayout={handleSaveBatch}
        isSaving={false}
        onEditTableDetails={handleEdit}
      />
    );

    const table1 = screen.getByTestId('floor-table-tbl-1');
    // Press ArrowRight to move 10px
    fireEvent.keyDown(table1, { key: 'ArrowRight' });

    // Unsaved changes banner should now be visible
    expect(screen.getByText(/kaydedilmemiş değişiklikler var/i)).toBeDefined();
  });

  it('allows reverting changes via the unsaved banner', () => {
    const handleSelect = vi.fn();
    const handleSaveBatch = vi.fn();
    const handleEdit = vi.fn();

    render(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId="tbl-1"
        onSelectTable={handleSelect}
        onSaveBatchLayout={handleSaveBatch}
        isSaving={false}
        onEditTableDetails={handleEdit}
      />
    );

    const table1 = screen.getByTestId('floor-table-tbl-1');
    fireEvent.keyDown(table1, { key: 'ArrowRight' });

    const revertButton = screen.getByText('Geri Al');
    fireEvent.click(revertButton);

    expect(screen.queryByText(/kaydedilmemiş değişiklikler var/i)).toBeNull();
  });

  it('triggers atomic batch save with updated coordinates', async () => {
    const handleSelect = vi.fn();
    const handleSaveBatch = vi.fn().mockResolvedValue(undefined);
    const handleEdit = vi.fn();

    render(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId="tbl-1"
        onSelectTable={handleSelect}
        onSaveBatchLayout={handleSaveBatch}
        isSaving={false}
        onEditTableDetails={handleEdit}
      />
    );

    const table1 = screen.getByTestId('floor-table-tbl-1');
    fireEvent.keyDown(table1, { key: 'ArrowRight' }); // moves X from 100 to 110

    const saveButton = screen.getByTestId('save-batch-layout-btn');
    fireEvent.click(saveButton);

    expect(handleSaveBatch).toHaveBeenCalledWith([
      expect.objectContaining({
        tableId: 'tbl-1',
        positionX: 110,
        positionY: 100,
        concurrencyToken: 'ct-1',
      }),
    ]);
  });

  it('supports pointer drag and drop to reposition table', () => {
    const handleSelect = vi.fn();
    render(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId="tbl-1"
        onSelectTable={handleSelect}
        onSaveBatchLayout={vi.fn()}
        isSaving={false}
        onEditTableDetails={vi.fn()}
      />
    );

    const table1 = screen.getByTestId('floor-table-tbl-1');
    const svg = screen.getByTestId('floor-canvas-svg');

    fireEvent.pointerDown(table1, { clientX: 100, clientY: 100, pointerId: 1 });
    fireEvent.pointerMove(svg, { clientX: 150, clientY: 140, pointerId: 1 });
    fireEvent.pointerUp(svg, { pointerId: 1 });

    expect(screen.getByText(/kaydedilmemiş değişiklikler var/i)).toBeDefined();
  });

  it('supports ArrowLeft, ArrowUp, and ArrowDown navigation', () => {
    render(
      <FloorCanvas
        tables={mockTables}
        statuses={mockStatuses}
        selectedTableId="tbl-1"
        onSelectTable={vi.fn()}
        onSaveBatchLayout={vi.fn()}
        isSaving={false}
        onEditTableDetails={vi.fn()}
      />
    );

    const table1 = screen.getByTestId('floor-table-tbl-1');
    fireEvent.keyDown(table1, { key: 'ArrowLeft' });
    fireEvent.keyDown(table1, { key: 'ArrowUp' });
    fireEvent.keyDown(table1, { key: 'ArrowDown' });

    expect(screen.getByText(/kaydedilmemiş değişiklikler var/i)).toBeDefined();
  });
});
