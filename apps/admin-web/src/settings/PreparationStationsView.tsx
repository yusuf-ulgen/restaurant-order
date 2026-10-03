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
  PreparationStationContract,
  PreparationStationType,
  CreatePreparationStationRequest,
  UpdatePreparationStationRequest,
} from '@restaurant-order/contracts';
import { useAdminConfig } from '../config/AdminConfigContext';

export interface PreparationStationsViewProps {
  onSaved?: () => void;
  onCancel?: () => void;
}

const STATION_TYPES: { value: PreparationStationType; label: string }[] = [
  { value: 'Kitchen', label: 'Mutfak (Kitchen)' },
  { value: 'Bar', label: 'Bar / İçecek (Bar)' },
  { value: 'Other', label: 'Diğer (Other)' },
];

export const PreparationStationsView: React.FC<PreparationStationsViewProps> = ({ onCancel }) => {
  const { selectedBranchId, selectedBranch } = useAdminConfig();

  const [stations, setStations] = useState<PreparationStationContract[]>([]);
  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  // Form modal/drawer state
  const [isFormOpen, setIsFormOpen] = useState<boolean>(false);
  const [editingStation, setEditingStation] = useState<PreparationStationContract | null>(null);
  const [formDisplayName, setFormDisplayName] = useState<string>('');
  const [formCode, setFormCode] = useState<string>('');
  const [formType, setFormType] = useState<PreparationStationType>('Kitchen');

  const loadStations = useCallback(async () => {
    if (!selectedBranchId) return;

    setIsLoading(true);
    setErrorMessage(null);
    try {
      const res = await fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/stations`);
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || 'Hazırlık istasyonları listelenemedi.');
      }
      const data: PreparationStationContract[] = await res.json();
      setStations(data);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'İstasyonlar yüklenirken bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsLoading(false);
    }
  }, [selectedBranchId]);

  useEffect(() => {
    loadStations();
  }, [loadStations]);

  const handleOpenCreate = () => {
    setEditingStation(null);
    setFormDisplayName('');
    setFormCode('');
    setFormType('Kitchen');
    setErrorMessage(null);
    setSuccessMessage(null);
    setIsFormOpen(true);
  };

  const handleOpenEdit = (station: PreparationStationContract) => {
    setEditingStation(station);
    setFormDisplayName(station.displayName);
    setFormCode(station.code);
    setFormType(station.stationType);
    setErrorMessage(null);
    setSuccessMessage(null);
    setIsFormOpen(true);
  };

  const handleCloseForm = () => {
    setIsFormOpen(false);
    setEditingStation(null);
  };

  const handleSubmitForm = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedBranchId) return;

    if (!formDisplayName.trim()) {
      setErrorMessage('İstasyon görünen adı boş bırakılamaz.');
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      if (editingStation) {
        const payload: UpdatePreparationStationRequest = {
          displayName: formDisplayName.trim(),
          stationType: formType,
          concurrencyToken: editingStation.concurrencyToken,
        };
        const res = await fetchWithCsrf(
          `/api/v1/restaurant-config/branches/${selectedBranchId}/stations/${editingStation.id}`,
          {
            method: 'PUT',
            body: JSON.stringify(payload),
          }
        );
        if (!res.ok) {
          const err = await res.json().catch(() => null);
          throw new Error(err?.detail || 'İstasyon güncellenemedi.');
        }
        setSuccessMessage('İstasyon başarıyla güncellendi.');
      } else {
        if (!formCode.trim()) {
          setErrorMessage('İstasyon kodu boş bırakılamaz.');
          setIsSubmitting(false);
          return;
        }
        const payload: CreatePreparationStationRequest = {
          code: formCode.trim().toUpperCase(),
          displayName: formDisplayName.trim(),
          stationType: formType,
          sortOrder: stations.length,
        };
        const res = await fetchWithCsrf(
          `/api/v1/restaurant-config/branches/${selectedBranchId}/stations`,
          {
            method: 'POST',
            body: JSON.stringify(payload),
          }
        );
        if (!res.ok) {
          const err = await res.json().catch(() => null);
          throw new Error(err?.detail || 'İstasyon oluşturulamadı.');
        }
        setSuccessMessage('Yeni istasyon başarıyla eklendi.');
      }

      handleCloseForm();
      await loadStations();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Kaydetme sırasında bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleToggleActive = async (station: PreparationStationContract) => {
    if (!selectedBranchId) return;

    const action = station.isActive ? 'deactivate' : 'activate';
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const res = await fetchWithCsrf(
        `/api/v1/restaurant-config/branches/${selectedBranchId}/stations/${station.id}/${action}`,
        {
          method: 'POST',
          body: JSON.stringify({ concurrencyToken: station.concurrencyToken }),
        }
      );
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || `İstasyon ${station.isActive ? 'pasife' : 'aktife'} alınamadı.`);
      }
      setSuccessMessage(`İstasyon başarıyla ${station.isActive ? 'pasife' : 'aktife'} alındı.`);
      await loadStations();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Durum güncellenirken bir hata oluştu.';
      setErrorMessage(msg);
    }
  };

  const handleMove = async (index: number, direction: 'up' | 'down') => {
    if (!selectedBranchId) return;
    const targetIndex = direction === 'up' ? index - 1 : index + 1;
    if (targetIndex < 0 || targetIndex >= stations.length) return;

    const reordered = [...stations];
    const [moved] = reordered.splice(index, 1);
    if (!moved) return;
    reordered.splice(targetIndex, 0, moved);

    setStations(reordered);
    try {
      const res = await fetchWithCsrf(
        `/api/v1/restaurant-config/branches/${selectedBranchId}/stations/reorder`,
        {
          method: 'POST',
          body: JSON.stringify({ orderedStationIds: reordered.map((s) => s.id) }),
        }
      );
      if (!res.ok) {
        await loadStations();
      }
    } catch {
      await loadStations();
    }
  };

  return (
    <div className="preparation-stations-view" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 'var(--ro-space-3)' }}>
        <div>
          <h2 style={{ margin: 0, fontSize: 'var(--ro-font-size-lg)', fontWeight: 'var(--ro-font-weight-bold)' }}>
            Hazırlık İstasyonları (Preparation Stations)
          </h2>
          <p style={{ margin: 'var(--ro-space-1) 0 0', color: 'var(--ro-color-text-secondary)', fontSize: 'var(--ro-font-size-sm)' }}>
            {selectedBranch?.name || 'Seçili Şube'} altındaki mutfak ve bar hazırlık istasyonları
          </p>
        </div>
        <div style={{ display: 'flex', gap: 'var(--ro-space-2)' }}>
          {onCancel && (
            <Button variant="outline" size="sm" onClick={onCancel}>
              Kapat
            </Button>
          )}
          <Button variant="primary" size="sm" onClick={handleOpenCreate} data-testid="add-station-button">
            + Yeni İstasyon Ekle
          </Button>
        </div>
      </div>

      {errorMessage && (
        <Card padding="sm">
          <div data-testid="station-error" style={{ color: 'var(--ro-color-danger)', fontSize: 'var(--ro-font-size-sm)' }}>
            {errorMessage}
          </div>
        </Card>
      )}

      {successMessage && (
        <Card padding="sm">
          <div data-testid="station-success" style={{ color: 'var(--ro-color-success, #10b981)', fontSize: 'var(--ro-font-size-sm)' }}>
            {successMessage}
          </div>
        </Card>
      )}

      {isLoading ? (
        <div data-testid="stations-loading" style={{ display: 'flex', justifyContent: 'center', padding: 'var(--ro-space-6)' }}>
          <Spinner size="lg" label="İstasyonlar yükleniyor..." />
        </div>
      ) : stations.length === 0 ? (
        <Card padding="lg">
          <EmptyState
            title="Henüz Tanımlı İstasyon Yok"
            description="Sipariş hazırlık süreçlerini ayrıştırmak için Mutfak, Sıcak Bar vb. istasyonlar oluşturabilirsiniz."
            actionLabel="İlk İstasyonu Oluştur"
            onAction={handleOpenCreate}
          />
        </Card>
      ) : (
        <div className="stations-list" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
          {stations.map((station, idx) => (
            <Card key={station.id} padding="md">
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
                      disabled={idx === stations.length - 1}
                      onClick={() => handleMove(idx, 'down')}
                      style={{ border: 'none', background: 'none', cursor: idx === stations.length - 1 ? 'default' : 'pointer', opacity: idx === stations.length - 1 ? 0.3 : 1 }}
                    >
                      ▼
                    </button>
                  </div>
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                      <strong style={{ fontSize: 'var(--ro-font-size-md)' }}>{station.displayName}</strong>
                      <Badge variant="neutral">{station.code}</Badge>
                      <Badge variant={station.isActive ? 'success' : 'warning'}>
                        {station.isActive ? 'Aktif' : 'Pasif'}
                      </Badge>
                    </div>
                    <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-secondary)' }}>
                      Tür: {STATION_TYPES.find((t) => t.value === station.stationType)?.label || station.stationType}
                    </span>
                  </div>
                </div>

                <div style={{ display: 'flex', gap: 'var(--ro-space-2)', alignItems: 'center' }}>
                  <Button variant="outline" size="sm" onClick={() => handleOpenEdit(station)} data-testid={`edit-station-${station.id}`}>
                    Düzenle
                  </Button>
                  <Button
                    variant={station.isActive ? 'ghost' : 'outline'}
                    size="sm"
                    onClick={() => handleToggleActive(station)}
                    data-testid={`toggle-station-${station.id}`}
                  >
                    {station.isActive ? 'Pasife Al' : 'Aktifleştir'}
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
        title={editingStation ? 'İstasyonu Düzenle' : 'Yeni Hazırlık İstasyonu Oluştur'}
      >
        <form onSubmit={handleSubmitForm} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)', padding: 'var(--ro-space-2)' }}>
          <div>
            <label htmlFor="station-display-name-input" style={{ display: 'block', marginBottom: 'var(--ro-space-1)', fontWeight: 'var(--ro-font-weight-medium)', fontSize: 'var(--ro-font-size-sm)' }}>
              Görünen Ad *
            </label>
            <input
              id="station-display-name-input"
              data-testid="station-display-name-input"
              type="text"
              required
              value={formDisplayName}
              onChange={(e) => setFormDisplayName(e.target.value)}
              placeholder="Örn: Ana Mutfak, Sıcak İstasyon, Bar"
              style={{ width: '100%', padding: 'var(--ro-space-2)', borderRadius: 'var(--ro-radius-md)', border: '1px solid var(--ro-color-border)' }}
            />
          </div>

          <div>
            <label htmlFor="station-code-input" style={{ display: 'block', marginBottom: 'var(--ro-space-1)', fontWeight: 'var(--ro-font-weight-medium)', fontSize: 'var(--ro-font-size-sm)' }}>
              İstasyon Kodu * {editingStation && <span style={{ color: 'var(--ro-color-text-secondary)' }}>(Değiştirilemez)</span>}
            </label>
            <input
              id="station-code-input"
              data-testid="station-code-input"
              type="text"
              required
              disabled={!!editingStation}
              value={formCode}
              onChange={(e) => setFormCode(e.target.value)}
              placeholder="Örn: KITCHEN-MAIN, BAR-01"
              style={{ width: '100%', padding: 'var(--ro-space-2)', borderRadius: 'var(--ro-radius-md)', border: '1px solid var(--ro-color-border)', opacity: editingStation ? 0.7 : 1 }}
            />
          </div>

          <div>
            <label htmlFor="station-type-select" style={{ display: 'block', marginBottom: 'var(--ro-space-1)', fontWeight: 'var(--ro-font-weight-medium)', fontSize: 'var(--ro-font-size-sm)' }}>
              İstasyon Türü
            </label>
            <select
              id="station-type-select"
              data-testid="station-type-select"
              value={formType}
              onChange={(e) => setFormType(e.target.value as PreparationStationType)}
              style={{ width: '100%', padding: 'var(--ro-space-2)', borderRadius: 'var(--ro-radius-md)', border: '1px solid var(--ro-color-border)' }}
            >
              {STATION_TYPES.map((t) => (
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
            <Button variant="primary" size="md" type="submit" disabled={isSubmitting} data-testid="save-station-button">
              {isSubmitting ? 'Kaydediliyor...' : editingStation ? 'Güncelle' : 'Oluştur'}
            </Button>
          </div>
        </form>
      </BottomSheet>
    </div>
  );
};
