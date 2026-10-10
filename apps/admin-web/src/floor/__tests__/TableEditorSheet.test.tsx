import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { TableEditorSheet } from '../TableEditorSheet';
import type { DiningAreaContract, RestaurantTableDto } from '@restaurant-order/contracts';

const mockDiningAreas: DiningAreaContract[] = [
  {
    id: 'area-1',
    tenantId: 'tenant-1',
    branchId: 'branch-1',
    name: 'İç Salon',
    code: 'ic-salon',
    areaType: 'Indoor',
    sortOrder: 0,
    isActive: true,
    createdAtUtc: '2026-10-02T10:00:00Z',
    updatedAtUtc: '2026-10-02T10:00:00Z',
    concurrencyToken: 'token-area-1',
  },
];

const mockExistingTable: RestaurantTableDto = {
  id: 'tbl-1',
  tenantId: 'tenant-1',
  branchId: 'branch-1',
  diningAreaId: 'area-1',
  tableNumber: 'T-01',
  name: 'Masa 1',
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
  concurrencyToken: 'ct-existing',
};

describe('TableEditorSheet Component', () => {
  it('renders creation form when no tableToEdit is provided', () => {
    const handleClose = vi.fn();
    const handleSaveCreate = vi.fn();
    const handleSaveUpdate = vi.fn();

    render(
      <TableEditorSheet
        isOpen={true}
        onClose={handleClose}
        diningAreas={mockDiningAreas}
        initialDiningAreaId="area-1"
        tableToEdit={null}
        onSaveCreate={handleSaveCreate}
        onSaveUpdate={handleSaveUpdate}
        isSaving={false}
      />
    );

    expect(screen.getByText('Yeni Masa Ekle')).toBeDefined();
    expect(screen.getByLabelText(/Masa Kodu \/ Numarası/i)).toBeDefined();
    expect(screen.getByLabelText(/Masa Görünen Adı/i)).toBeDefined();
    expect(screen.getByLabelText(/Kapasite/i)).toBeDefined();
  });

  it('validates required table number before submission', async () => {
    const handleClose = vi.fn();
    const handleSaveCreate = vi.fn();
    const handleSaveUpdate = vi.fn();

    render(
      <TableEditorSheet
        isOpen={true}
        onClose={handleClose}
        diningAreas={mockDiningAreas}
        initialDiningAreaId="area-1"
        tableToEdit={null}
        onSaveCreate={handleSaveCreate}
        onSaveUpdate={handleSaveUpdate}
        isSaving={false}
      />
    );

    const submitBtn = screen.getByText('Masa Oluştur');
    fireEvent.click(submitBtn);

    expect(await screen.findByText('Masa numarası / kodu zorunludur.')).toBeDefined();
    expect(handleSaveCreate).not.toHaveBeenCalled();
  });

  it('submits create request with all required fields', async () => {
    const handleClose = vi.fn();
    const handleSaveCreate = vi.fn().mockResolvedValue(undefined);
    const handleSaveUpdate = vi.fn();

    render(
      <TableEditorSheet
        isOpen={true}
        onClose={handleClose}
        diningAreas={mockDiningAreas}
        initialDiningAreaId="area-1"
        tableToEdit={null}
        onSaveCreate={handleSaveCreate}
        onSaveUpdate={handleSaveUpdate}
        isSaving={false}
      />
    );

    const numberInput = screen.getByLabelText(/Masa Kodu \/ Numarası/i);
    const nameInput = screen.getByLabelText(/Masa Görünen Adı/i);

    fireEvent.change(numberInput, { target: { value: 'B-01' } });
    fireEvent.change(nameInput, { target: { value: 'Bahçe Köşe' } });

    const submitBtn = screen.getByText('Masa Oluştur');
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(handleSaveCreate).toHaveBeenCalledWith(
        expect.objectContaining({
          tableNumber: 'B-01',
          name: 'Bahçe Köşe',
          capacity: 4,
          shape: 'Square',
          diningAreaId: 'area-1',
        })
      );
    });
  });

  it('renders edit form and passes concurrency token on update', async () => {
    const handleClose = vi.fn();
    const handleSaveCreate = vi.fn();
    const handleSaveUpdate = vi.fn().mockResolvedValue(undefined);

    render(
      <TableEditorSheet
        isOpen={true}
        onClose={handleClose}
        diningAreas={mockDiningAreas}
        initialDiningAreaId="area-1"
        tableToEdit={mockExistingTable}
        onSaveCreate={handleSaveCreate}
        onSaveUpdate={handleSaveUpdate}
        isSaving={false}
      />
    );

    expect(screen.getByText('Masa Düzenle: T-01')).toBeDefined();

    const nameInput = screen.getByLabelText(/Masa Görünen Adı/i);
    fireEvent.change(nameInput, { target: { value: 'Masa 1 Yeni' } });

    const updateBtn = screen.getByText('Değişiklikleri Kaydet');
    fireEvent.click(updateBtn);

    await waitFor(() => {
      expect(handleSaveUpdate).toHaveBeenCalledWith(
        'tbl-1',
        expect.objectContaining({
          tableNumber: 'T-01',
          name: 'Masa 1 Yeni',
        }),
        'ct-existing'
      );
    });
  });

  it('updates capacity, shape, and handles cancellation', () => {
    const handleClose = vi.fn();
    const handleSaveCreate = vi.fn();
    const handleSaveUpdate = vi.fn();

    render(
      <TableEditorSheet
        isOpen={true}
        onClose={handleClose}
        diningAreas={mockDiningAreas}
        initialDiningAreaId="area-1"
        tableToEdit={null}
        onSaveCreate={handleSaveCreate}
        onSaveUpdate={handleSaveUpdate}
        isSaving={false}
      />
    );

    const capacityInput = screen.getByLabelText(/Kapasite/i);
    fireEvent.change(capacityInput, { target: { value: '8' } });

    const shapeSelect = screen.getByLabelText(/Masa Şekli/i);
    fireEvent.change(shapeSelect, { target: { value: 'Round' } });

    const cancelBtn = screen.getByText('İptal');
    fireEvent.click(cancelBtn);
    expect(handleClose).toHaveBeenCalled();
  });

  it('displays error message when save fails', async () => {
    const handleClose = vi.fn();
    const handleSaveCreate = vi.fn().mockRejectedValue(new Error('Kayıt başarısız oldu.'));
    const handleSaveUpdate = vi.fn();

    render(
      <TableEditorSheet
        isOpen={true}
        onClose={handleClose}
        diningAreas={mockDiningAreas}
        initialDiningAreaId="area-1"
        tableToEdit={null}
        onSaveCreate={handleSaveCreate}
        onSaveUpdate={handleSaveUpdate}
        isSaving={false}
      />
    );

    const numberInput = screen.getByLabelText(/Masa Kodu \/ Numarası/i);
    const nameInput = screen.getByLabelText(/Masa Görünen Adı/i);
    fireEvent.change(numberInput, { target: { value: 'T-99' } });
    fireEvent.change(nameInput, { target: { value: 'Hata Masası' } });

    const submitBtn = screen.getByText('Masa Oluştur');
    fireEvent.click(submitBtn);

    expect(await screen.findByText('Kayıt başarısız oldu.')).toBeDefined();
  });
});
