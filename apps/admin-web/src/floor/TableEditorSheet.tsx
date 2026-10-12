import React, { useState, useEffect } from 'react';
import {
  BottomSheet,
  Modal,
  Button,
  Input,
  Select,
  FormError,
} from '@restaurant-order/ui';
import type {
  RestaurantTableDto,
  CreateTableRequest,
  UpdateTableRequest,
  TableShape,
  DiningAreaContract,
} from '@restaurant-order/contracts';

export interface TableEditorSheetProps {
  isOpen: boolean;
  onClose: () => void;
  diningAreas: DiningAreaContract[];
  initialDiningAreaId: string;
  tableToEdit?: RestaurantTableDto | null;
  onSaveCreate: (req: CreateTableRequest) => Promise<void>;
  onSaveUpdate: (tableId: string, req: UpdateTableRequest, etag: string) => Promise<void>;
  onToggleActive?: (table: RestaurantTableDto) => Promise<void>;
  isSaving: boolean;
}

export const TableEditorSheet: React.FC<TableEditorSheetProps> = ({
  isOpen,
  onClose,
  diningAreas,
  initialDiningAreaId,
  tableToEdit,
  onSaveCreate,
  onSaveUpdate,
  onToggleActive,
  isSaving,
}) => {
  const isEditing = Boolean(tableToEdit);

  const [tableNumber, setTableNumber] = useState('');
  const [name, setName] = useState('');
  const [capacity, setCapacity] = useState(4);
  const [diningAreaId, setDiningAreaId] = useState(initialDiningAreaId);
  const [shape, setShape] = useState<TableShape>('Square');
  const [formError, setFormError] = useState<string | null>(null);

  // Responsive mode: check window width (< 768px -> BottomSheet, >= 768px -> Modal)
  const [isMobile, setIsMobile] = useState(() =>
    typeof window !== 'undefined' ? window.innerWidth < 768 : false
  );

  useEffect(() => {
    const handleResize = () => setIsMobile(window.innerWidth < 768);
    window.addEventListener('resize', handleResize);
    return () => window.removeEventListener('resize', handleResize);
  }, []);

  useEffect(() => {
    if (tableToEdit) {
      setTableNumber(tableToEdit.tableNumber);
      setName(tableToEdit.name);
      setCapacity(tableToEdit.capacity);
      setDiningAreaId(tableToEdit.diningAreaId);
      setShape(tableToEdit.shape);
    } else {
      setTableNumber('');
      setName('');
      setCapacity(4);
      setDiningAreaId(initialDiningAreaId);
      setShape('Square');
    }
    setFormError(null);
  }, [tableToEdit, initialDiningAreaId, isOpen]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    const trimmedNumber = tableNumber.trim();
    const trimmedName = name.trim();

    if (!trimmedNumber) {
      setFormError('Masa numarası / kodu zorunludur.');
      return;
    }
    if (!trimmedName) {
      setFormError('Masa adı zorunludur.');
      return;
    }
    if (capacity < 1 || capacity > 100) {
      setFormError('Masa kapasitesi 1 ile 100 kişi arasında olmalıdır.');
      return;
    }
    if (!diningAreaId) {
      setFormError('Lütfen geçerli bir masa alanı seçin.');
      return;
    }

    try {
      if (isEditing && tableToEdit) {
        await onSaveUpdate(
          tableToEdit.id,
          {
            diningAreaId,
            tableNumber: trimmedNumber,
            name: trimmedName,
            capacity,
          },
          tableToEdit.concurrencyToken
        );
      } else {
        await onSaveCreate({
          diningAreaId,
          tableNumber: trimmedNumber,
          name: trimmedName,
          capacity,
          shape,
          positionX: 50,
          positionY: 50,
          width: 100,
          height: 100,
          rotationDegrees: 0,
        });
      }
      onClose();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Kayıt sırasında bir hata oluştu.';
      setFormError(msg);
    }
  };

  const title = isEditing ? `Masa Düzenle: ${tableToEdit?.tableNumber}` : 'Yeni Masa Ekle';
  const description = isEditing
    ? 'Masa bilgilerini ve kapasitesini güncelleyin.'
    : 'Seçili alana yeni bir masa tanımlayın.';

  const formContent = (
    <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }}>
      {formError && <FormError>{formError}</FormError>}

      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
        <div>
          <label htmlFor="table-area-select" style={{ display: 'block', marginBottom: '4px', fontSize: 'var(--ro-font-size-sm)', fontWeight: 600 }}>
            Masa Alanı
          </label>
          <Select
            id="table-area-select"
            value={diningAreaId}
            onChange={(e) => setDiningAreaId(e.target.value)}
            disabled={isSaving}
          >
            {diningAreas.map((area) => (
              <option key={area.id} value={area.id}>
                {area.name} ({area.areaType})
              </option>
            ))}
          </Select>
        </div>

        <div>
          <label htmlFor="table-number-input" style={{ display: 'block', marginBottom: '4px', fontSize: 'var(--ro-font-size-sm)', fontWeight: 600 }}>
            Masa Kodu / Numarası (Örn: T-01, B-04)
          </label>
          <Input
            id="table-number-input"
            value={tableNumber}
            onChange={(e) => setTableNumber(e.target.value)}
            placeholder="T-01"
            disabled={isSaving}
          />
        </div>

        <div>
          <label htmlFor="table-name-input" style={{ display: 'block', marginBottom: '4px', fontSize: 'var(--ro-font-size-sm)', fontWeight: 600 }}>
            Masa Görünen Adı
          </label>
          <Input
            id="table-name-input"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="Bahçe Köşe Masa"
            disabled={isSaving}
          />
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--ro-space-3)' }}>
          <div>
            <label htmlFor="table-capacity-input" style={{ display: 'block', marginBottom: '4px', fontSize: 'var(--ro-font-size-sm)', fontWeight: 600 }}>
              Kapasite (Kişi)
            </label>
            <Input
              id="table-capacity-input"
              type="number"
              min={1}
              max={100}
              value={capacity}
              onChange={(e) => setCapacity(parseInt(e.target.value, 10) || 1)}
              disabled={isSaving}
            />
          </div>

          {!isEditing && (
            <div>
              <label htmlFor="table-shape-select" style={{ display: 'block', marginBottom: '4px', fontSize: 'var(--ro-font-size-sm)', fontWeight: 600 }}>
                Masa Şekli
              </label>
              <Select
                id="table-shape-select"
                value={shape}
                onChange={(e) => setShape(e.target.value as TableShape)}
                disabled={isSaving}
              >
                <option value="Square">Kare</option>
                <option value="Rectangle">Dikdörtgen</option>
                <option value="Round">Yuvarlak</option>
              </Select>
            </div>
          )}
        </div>

        {isEditing && tableToEdit && onToggleActive && (
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              padding: 'var(--ro-space-3)',
              backgroundColor: '#f5f5f5',
              borderRadius: 'var(--ro-radius-md)',
              border: '1px solid var(--ro-color-border)',
            }}
          >
            <div>
              <span style={{ fontWeight: 600, fontSize: 'var(--ro-font-size-sm)', display: 'block' }}>
                Masa Durumu: {tableToEdit.isActive ? 'Aktif' : 'Pasif (Devre Dışı)'}
              </span>
              <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-muted)' }}>
                {tableToEdit.isActive
                  ? 'Pasife alındığında yeni müşteri oturumu açılamaz.'
                  : 'Aktife alındığında tekrar oturum açılabilir.'}
              </span>
            </div>
            <Button
              type="button"
              variant={tableToEdit.isActive ? 'danger' : 'outline'}
              size="sm"
              disabled={isSaving}
              onClick={() => onToggleActive(tableToEdit)}
            >
              {tableToEdit.isActive ? 'Pasife Al' : 'Aktifleştir'}
            </Button>
          </div>
        )}
      </div>

      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 'var(--ro-space-2)', marginTop: 'var(--ro-space-2)' }}>
        <Button type="button" variant="outline" size="md" onClick={onClose} disabled={isSaving}>
          İptal
        </Button>
        <Button type="submit" variant="primary" size="md" disabled={isSaving} data-testid="table-form-submit-btn">
          {isSaving ? 'Kaydediliyor...' : isEditing ? 'Değişiklikleri Kaydet' : 'Masa Oluştur'}
        </Button>
      </div>
    </form>
  );

  if (!isOpen) return null;

  return isMobile ? (
    <BottomSheet isOpen={isOpen} onClose={onClose} title={title} description={description}>
      <div style={{ padding: 'var(--ro-space-2) 0' }}>{formContent}</div>
    </BottomSheet>
  ) : (
    <Modal isOpen={isOpen} onClose={onClose} title={title} description={description} size="md">
      <div style={{ padding: 'var(--ro-space-2) 0' }}>{formContent}</div>
    </Modal>
  );
};
