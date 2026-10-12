import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { TableInspector, LocalTableLayout } from '../TableInspector';
import type { RestaurantTableDto } from '@restaurant-order/contracts';

const mockTable: RestaurantTableDto = {
  id: 'tbl-1',
  tenantId: 'tenant-1',
  branchId: 'branch-1',
  diningAreaId: 'area-1',
  tableNumber: 'T-10',
  name: 'Salon Masası',
  capacity: 4,
  shape: 'Square',
  positionX: 50,
  positionY: 60,
  width: 80,
  height: 80,
  rotationDegrees: 0,
  isActive: true,
  qrVersion: 1,
  publicCode: 'code-123',
  createdAtUtc: '2026-10-10T00:00:00Z',
  concurrencyToken: 'ct-1',
};

const mockLayout: LocalTableLayout = {
  positionX: 50,
  positionY: 60,
  width: 80,
  height: 80,
  rotationDegrees: 0,
  shape: 'Square',
};

describe('TableInspector Component', () => {
  it('renders table information and layout inputs', () => {
    const handleUpdate = vi.fn();
    const handleEdit = vi.fn();
    const handleClose = vi.fn();

    render(
      <TableInspector
        table={mockTable}
        layout={mockLayout}
        onUpdateLayout={handleUpdate}
        onEditDetails={handleEdit}
        onClose={handleClose}
      />
    );

    expect(screen.getByText(/Masa T-10: Salon Masası/i)).toBeDefined();
    expect(screen.getByText(/Kapasite: 4 kişi/i)).toBeDefined();

    // Trigger positionX change
    const xInput = screen.getByLabelText('Masa X Koordinatı');
    fireEvent.change(xInput, { target: { value: '120' } });
    expect(handleUpdate).toHaveBeenCalledWith({ positionX: 120 });

    // Trigger positionY change
    const yInput = screen.getByLabelText('Masa Y Koordinatı');
    fireEvent.change(yInput, { target: { value: '150' } });
    expect(handleUpdate).toHaveBeenCalledWith({ positionY: 150 });

    // Trigger width change
    const wInput = screen.getByLabelText('Masa Genişliği');
    fireEvent.change(wInput, { target: { value: '100' } });
    expect(handleUpdate).toHaveBeenCalledWith({ width: 100 });

    // Trigger height change
    const hInput = screen.getByLabelText('Masa Yüksekliği');
    fireEvent.change(hInput, { target: { value: '90' } });
    expect(handleUpdate).toHaveBeenCalledWith({ height: 90 });

    // Trigger rotation change
    const rotInput = screen.getByLabelText('Masa Döndürme Derecesi');
    fireEvent.change(rotInput, { target: { value: '90' } });
    expect(handleUpdate).toHaveBeenCalledWith({ rotationDegrees: 90 });

    // Trigger shape change
    const shapeInput = screen.getByLabelText('Masa Şekli');
    fireEvent.change(shapeInput, { target: { value: 'Round' } });
    expect(handleUpdate).toHaveBeenCalledWith({ shape: 'Round' });

    // Trigger edit details button
    const editBtn = screen.getByTestId('edit-table-details-btn');
    fireEvent.click(editBtn);
    expect(handleEdit).toHaveBeenCalledWith(mockTable);

    // Trigger close button
    const closeBtn = screen.getByLabelText('Seçimi kaldır');
    fireEvent.click(closeBtn);
    expect(handleClose).toHaveBeenCalled();
  });
});
