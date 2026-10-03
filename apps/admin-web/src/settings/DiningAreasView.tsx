import React, { useState, useEffect, useCallback } from 'react';
import {
  Card,
  Button,
  Spinner,
  EmptyState,
  Badge,
  BottomSheet,
} from '@restaurant-order/ui';
import {
  fetchWithCsrf,
  DiningAreaContract,
  DiningAreaType,
  CreateDiningAreaRequest,
  UpdateDiningAreaRequest,
} from '@restaurant-order/contracts';
import { useAdminConfig } from '../config/AdminConfigContext';

export interface DiningAreasViewProps {
  onSaved?: () => void;
  onCancel?: () => void;
}

const AREA_TYPES: { value: DiningAreaType; label: string }[] = [
  { value: 'Indoor', label: 'İç Mekan (Indoor)' },
  { value: 'Terrace', label: 'Teras (Terrace)' },
  { value: 'Garden', label: 'Bahçe (Garden)' },
  { value: 'BarArea', label: 'Bar Bölümü (BarArea)' },
  { value: 'Other', label: 'Diğer (Other)' },
];

export const DiningAreasView: React.FC<DiningAreasViewProps> = ({ onCancel }) => {
  const { selectedBranchId, selectedBranch } = useAdminConfig();

  const [areas, setAreas] = useState<DiningAreaContract[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  // Form modal/drawer state
  const [isFormOpen, setIsFormOpen] = useState<boolean>(false);
  const [editingArea, setEditingArea] = useState<DiningAreaContract | null>(null);
  const [formName, setFormName] = useState<string>('');
  const [formCode, setFormCode] = useState<string>('');
  const [formType, setFormType] = useState<DiningAreaType>('Indoor');

  const loadAreas = useCallback(async () => {
    if (!selectedBranchId) return;

    setIsLoading(true);
    setErrorMessage(null);
    try {
      const res = await fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/dining-areas`);
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || 'Masa alanları listelenemedi.');
      }
      const data: DiningAreaContract[] = await res.json();
      setAreas(data);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Alanlar yüklenirken bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsLoading(false);
    }
  }, [selectedBranchId]);

  useEffect(() => {
    loadAreas();
  }, [loadAreas]);

  const handleOpenCreate = () => {
    setEditingArea(null);
    setFormName('');
    setFormCode('');
    setFormType('Indoor');
    setErrorMessage(null);
    setSuccessMessage(null);
    setIsFormOpen(true);
  };

  const handleOpenEdit = (area: DiningAreaContract) => {
    setEditingArea(area);
    setFormName(area.name);
    setFormCode(area.code);
    setFormType(area.areaType);
    setErrorMessage(null);
    setSuccessMessage(null);
    setIsFormOpen(true);
  };

  const handleCloseForm = () => {
    setIsFormOpen(false);
    setEditingArea(null);
  };

  const handleSubmitForm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedBranchId) return;

    if (!formName.trim()) {
      setErrorMessage('Alan adı boş bırakılamaz.');
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      if (editingArea) {
        const payload: UpdateDiningAreaRequest = {
          name: formName.trim(),
          areaType: formType,
          concurrencyToken: editingArea.concurrencyToken,
        };
        const res = await fetchWithCsrf(
          `/api/v1/restaurant-config/branches/${selectedBranchId}/dining-areas/${editingArea.id}`,
          {
            method: 'PUT',
            body: JSON.stringify(payload),
          }
        );
        if (!res.ok) {
          const err = await res.json().catch(() => null);
          throw new Error(err?.detail || 'Alan güncellenemedi.');
        }
        setSuccessMessage('Alan başarıyla güncellendi.');
      } else {
        if (!formCode.trim()) {
          setErrorMessage('Alan kodu/slug boş bırakılamaz.');
          setIsSubmitting(false);
          return;
        }
        const payload: CreateDiningAreaRequest = {
          name: formName.trim(),
          code: formCode.trim().toLowerCase(),
          areaType: formType,
          sortOrder: areas.length,
        };
        const res = await fetchWithCsrf(
          `/api/v1/restaurant-config/branches/${selectedBranchId}/dining-areas`,
          {
            method: 'POST',
            body: JSON.stringify(payload),
          }
        );
        if (!res.ok) {
          const err = await res.json().catch(() => null);
          throw new Error(err?.detail || 'Alan oluşturulamadı.');
        }
        setSuccessMessage('Yeni alan başarıyla eklendi.');
      }

      handleCloseForm();
      await loadAreas();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Kaydetme sırasında bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleToggleActive = async (area: DiningAreaContract) => {
    if (!selectedBranchId) return;

    const action = area.isActive ? 'deactivate' : 'activate';
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const res = await fetchWithCsrf(
        `/api/v1/restaurant-config/branches/${selectedBranchId}/dining-areas/${area.id}/${action}`,
        {
          method: 'POST',
          body: JSON.stringify({ concurrencyToken: area.concurrencyToken }),
        }
      );
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || `Alan ${area.isActive ? 'pasife' : 'aktife'} alınamadı.`);
      }
      setSuccessMessage(`Alan başarıyla ${area.isActive ? 'pasife' : 'aktife'} alındı.`);
      await loadAreas();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Durum güncellenirken bir hata oluştu.';
      setErrorMessage(msg);
    }
  };

  const handleMove = async (index: number, direction: 'up' | 'down') => {
    if (!selectedBranchId) return;
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= areas.length) return;

    const reordered = [...areas];
    const [moved] = reordered.splice(index, 1);
    if (!moved) return;
    reordered.splice(targetIndex, 0, moved);

    setAreas(reordered);
    try {
      const res = await fetchWithCsrf(
        `/api/v1/restaurant-config/branches/${selectedBranchId}/dining-areas/reorder`,
        {
          method: 'POST',
          body: JSON.stringify({ orderedAreaIds: reordered.map((a) => a.id) }),
        }
      );
      if (!res.ok) {
        await loadAreas();
      }
    } catch {
      await loadAreas();
    }
  };

  return (
    <div className="dining-areas-view" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 'var(--ro-space-3)' }}>
        <div>
          <h2 style={{ margin: 0, fontSize: 'var(--ro-font-size-lg)', fontWeight: 'var(--ro-font-weight-bold)' }}>
            Masa Alanları (Dining Areas)
          </h2>
          <p style={{ margin: 'var(--ro-space-1) 0 0', color: 'var(--ro-color-text-secondary)', fontSize: 'var(--ro-font-size-sm)' }}>
            {selectedBranch?.name || 'Seçili Şube'} altındaki servis ve oturma alanları
          </p>
        </div>
        <div style={{ display: 'flex', gap: 'var(--ro-space-2)' }}>
          {onCancel && (
            <Button variant="outline" size="sm" onClick={onCancel}>
              Kapat
            </Button>
          )}
          <Button variant="primary" size="sm" onClick={handleOpenCreate} data-testid="add-dining-area-button">
            + Yeni Alan Ekle
          </Button>
        </div>
      </div>

      {errorMessage && (
        <Card padding="sm">
          <div data-testid="dining-area-error" style={{ color: 'var(--ro-color-danger)', fontSize: 'var(--ro-font-size-sm)' }}>
            {errorMessage}
          </div>
        </Card>
      )}

      {successMessage && (
        <Card padding="sm">
          <div data-testid="dining-area-success" style={{ color: 'var(--ro-color-success, #10b981)', fontSize: 'var(--ro-font-size-sm)' }}>
            {successMessage}
          </div>
        </Card>
      )}

      {isLoading ? (
        <div data-testid="dining-areas-loading" style={{ display: 'flex', justifyContent: 'center', padding: 'var(--ro-space-6)' }}>
          <Spinner size="lg" label="Alanlar yükleniyor..." />
        </div>
      ) : areas.length === 0 ? (
        <Card padding="lg">
          <EmptyState
            title="Henüz Tanımlı Alan Yok"
            description="Şubenizde masa yerleşimlerini gruplamak için İç Mekan, Teras vb. alanlar oluşturabilirsiniz."
            actionLabel="İlk Alanı Oluştur"
            onAction={handleOpenCreate}
          />
        </Card>
      ) : (
        <div className="dining-areas-list" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
          {areas.map((area, idx) => (
            <Card key={area.id} padding="md">
              <div
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  flexWrap: 'wrap',
                  gap: 'var(--ro-space-3)',
                }}
              >
                <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-3)' }}>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
                    <button
                      type="button"
                      aria-label="Yukarı taşı"
                      disabled={idx === 0}
                      onClick={() => handleMove(idx, 'up')}
                      style={{ border: 'none', background: 'none', cursor: idx === 0 ? 'default' : 'pointer', opacity: idx === 0 ? 0.3 : 1 }}
                    >
                      ▲
                    </button>
                    <button
                      type="button"
                      aria-label="Aşağı taşı"
                      disabled={idx === areas.length - 1}
                      onClick={() => handleMove(idx, 'down')}
                      style={{ border: 'none', background: 'none', cursor: idx === areas.length - 1 ? 'default' : 'pointer', opacity: idx === areas.length - 1 ? 0.3 : 1 }}
                    >
                      ▼
                    </button>
                  </div>
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                      <strong style={{ fontSize: 'var(--ro-font-size-md)' }}>{area.name}</strong>
                      <Badge variant="neutral">{area.code}</Badge>
                      <Badge variant={area.isActive ? 'success' : 'warning'}>
                        {area.isActive ? 'Aktif' : 'Pasif'}
                      </Badge>
                    </div>
                    <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-secondary)' }}>
                      Tür: {AREA_TYPES.find((t) => t.value === area.areaType)?.label || area.areaType}
                    </span>
                  </div>
                </div>

                <div style={{ display: 'flex', gap: 'var(--ro-space-2)', alignItems: 'center' }}>
                  <Button variant="outline" size="sm" onClick={() => handleOpenEdit(area)} data-testid={`edit-area-${area.id}`}>
                    Düzenle
                  </Button>
                  <Button
                    variant={area.isActive ? 'ghost' : 'outline'}
                    size="sm"
                    onClick={() => handleToggleActive(area)}
                    data-testid={`toggle-area-${area.id}`}
                  >
                    {area.isActive ? 'Pasife Al' : 'Aktifleştir'}
                  </Button>
                </div>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* Form BottomSheet / Dialog */}
      <BottomSheet
        isOpen={isFormOpen}
        onClose={handleCloseForm}
        title={editingArea ? 'Alanı Düzenle' : 'Yeni Masa Alanı Oluştur'}
      >
        <form onSubmit={handleSubmitForm} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)', padding: 'var(--ro-space-2)' }}>
          <div>
            <label htmlFor="area-name-input" style={{ display: 'block', marginBottom: 'var(--ro-space-1)', fontWeight: 'var(--ro-font-weight-medium)', fontSize: 'var(--ro-font-size-sm)' }}>
              Alan Adı *
            </label>
            <input
              id="area-name-input"
              data-testid="area-name-input"
              type="text"
              required
              value={formName}
              onChange={(e) => setFormName(e.target.value)}
              placeholder="Örn: Ön Bahçe, Teras Bölümü"
              style={{ width: '100%', padding: 'var(--ro-space-2)', borderRadius: 'var(--ro-radius-md)', border: '1px solid var(--ro-color-border)' }}
            />
          </div>

          <div>
            <label htmlFor="area-code-input" style={{ display: 'block', marginBottom: 'var(--ro-space-1)', fontWeight: 'var(--ro-font-weight-medium)', fontSize: 'var(--ro-font-size-sm)' }}>
              Alan Kodu / Slug * {editingArea && <span style={{ color: 'var(--ro-color-text-secondary)' }}>(Değiştirilemez)</span>}
            </label>
            <input
              id="area-code-input"
              data-testid="area-code-input"
              type="text"
              required
              disabled={!!editingArea}
              value={formCode}
              onChange={(e) => setFormCode(e.target.value)}
              placeholder="Örn: on-bahce, teras-1"
              style={{ width: '100%', padding: 'var(--ro-space-2)', borderRadius: 'var(--ro-radius-md)', border: '1px solid var(--ro-color-border)', opacity: editingArea ? 0.7 : 1 }}
            />
          </div>

          <div>
            <label htmlFor="area-type-select" style={{ display: 'block', marginBottom: 'var(--ro-space-1)', fontWeight: 'var(--ro-font-weight-medium)', fontSize: 'var(--ro-font-size-sm)' }}>
              Alan Türü
            </label>
            <select
              id="area-type-select"
              data-testid="area-type-select"
              value={formType}
              onChange={(e) => setFormType(e.target.value as DiningAreaType)}
              style={{ width: '100%', padding: 'var(--ro-space-2)', borderRadius: 'var(--ro-radius-md)', border: '1px solid var(--ro-color-border)' }}
            >
              {AREA_TYPES.map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
            </select>
          </div>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 'var(--ro-space-2)', marginTop: 'var(--ro-space-4)' }}>
            <Button variant="ghost" size="md" onClick={handleCloseForm} type="button">
              İptal
            </Button>
            <Button variant="primary" size="md" type="submit" disabled={isSubmitting} data-testid="save-area-button">
              {isSubmitting ? 'Kaydediliyor...' : editingArea ? 'Güncelle' : 'Oluştur'}
            </Button>
          </div>
        </form>
      </BottomSheet>
    </div>
  );
};
