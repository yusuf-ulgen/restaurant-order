import React, { useState, useEffect, useCallback } from 'react';
import {
  Card,
  Button,
  Spinner,
  EmptyState,
  BottomSheet,
} from '@restaurant-order/ui';
import {
  fetchWithCsrf,
  EffectiveBranchSettingsContract,
  UpdateBranchSettingsRequest,
  BranchOperatingHoursContract,
  UpdateBranchOperatingHoursRequest,
} from '@restaurant-order/contracts';
import { useAdminConfig } from '../config/AdminConfigContext';
import { BranchFinancialSettingsForm } from './BranchFinancialSettingsForm';
import { BranchOperatingHoursForm } from './BranchOperatingHoursForm';

export interface BranchSettingsViewProps {
  initialTab?: 'financial' | 'hours';
  onSaved?: () => void;
  onCancel?: () => void;
}

export const BranchSettingsView: React.FC<BranchSettingsViewProps> = ({
  initialTab = 'financial',
  onSaved,
}) => {
  const { selectedBranchId, selectedBranch } = useAdminConfig();

  const [activeTab, setActiveTab] = useState<'financial' | 'hours'>(initialTab);
  const [settings, setSettings] = useState<EffectiveBranchSettingsContract | null>(null);
  const [operatingHours, setOperatingHours] = useState<BranchOperatingHoursContract | null>(null);

  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isConflict, setIsConflict] = useState<boolean>(false);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const [hasUnsavedChanges, setHasUnsavedChanges] = useState<boolean>(false);
  const [isMobileSummaryOpen, setIsMobileSummaryOpen] = useState<boolean>(false);

  const loadData = useCallback(async () => {
    if (!selectedBranchId) return;

    setIsLoading(true);
    setErrorMessage(null);
    setIsConflict(false);
    setSuccessMessage(null);

    try {
      const [settingsRes, hoursRes] = await Promise.all([
        fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/settings`),
        fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/operating-hours`),
      ]);

      if (!settingsRes.ok) {
        const err = await settingsRes.json().catch(() => null);
        throw new Error(err?.detail || 'Şube finansal ayarları alınamadı.');
      }
      if (!hoursRes.ok) {
        const err = await hoursRes.json().catch(() => null);
        throw new Error(err?.detail || 'Şube çalışma saatleri alınamadı.');
      }

      const settingsData: EffectiveBranchSettingsContract = await settingsRes.json();
      const hoursData: BranchOperatingHoursContract = await hoursRes.json();

      setSettings(settingsData);
      setOperatingHours(hoursData);
      setHasUnsavedChanges(false);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Veriler yüklenirken bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsLoading(false);
    }
  }, [selectedBranchId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleTabSwitch = (newTab: 'financial' | 'hours') => {
    if (newTab === activeTab) return;
    if (hasUnsavedChanges) {
      const proceed = window.confirm(
        'Kaydedilmemiş değişiklikleriniz var. Sekmeyi değiştirmek istediğinizden emin misiniz?'
      );
      if (!proceed) return;
    }
    setActiveTab(newTab);
    setErrorMessage(null);
    setSuccessMessage(null);
    setIsConflict(false);
  };

  const handleSaveFinancial = async (payload: UpdateBranchSettingsRequest) => {
    if (!selectedBranchId) return;

    setIsSubmitting(true);
    setErrorMessage(null);
    setIsConflict(false);
    setSuccessMessage(null);

    try {
      const res = await fetchWithCsrf(
        `/api/v1/restaurant-config/branches/${selectedBranchId}/settings`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        }
      );

      if (res.ok) {
        setSuccessMessage('Şube finansal ve operasyonel ayarları başarıyla güncellendi.');
        await loadData();
        onSaved?.();
      } else if (res.status === 409) {
        setIsConflict(true);
        setErrorMessage(
          'Bu ayarlar başka bir oturum tarafından güncellenmiş (Çakışma / 409). Lütfen son verileri yeniden yükleyin.'
        );
      } else {
        const errData = await res.json().catch(() => null);
        setErrorMessage(errData?.detail || 'Ayarlar kaydedilemedi.');
      }
    } catch {
      setErrorMessage('Ağ bağlantı hatası oluştu. Lütfen tekrar deneyin.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSaveHours = async (payload: UpdateBranchOperatingHoursRequest) => {
    if (!selectedBranchId) return;

    setIsSubmitting(true);
    setErrorMessage(null);
    setIsConflict(false);
    setSuccessMessage(null);

    try {
      const res = await fetchWithCsrf(
        `/api/v1/restaurant-config/branches/${selectedBranchId}/operating-hours`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload),
        }
      );

      if (res.ok) {
        setSuccessMessage('Şube haftalık çalışma saatleri başarıyla kaydedildi.');
        await loadData();
        onSaved?.();
      } else if (res.status === 409) {
        setIsConflict(true);
        setErrorMessage(
          'Çalışma saatleri başka bir oturum tarafından güncellenmiş (Çakışma / 409). Lütfen son verileri yeniden yükleyin.'
        );
      } else {
        const errData = await res.json().catch(() => null);
        setErrorMessage(errData?.detail || 'Çalışma saatleri kaydedilemedi.');
      }
    } catch {
      setErrorMessage('Ağ bağlantı hatası oluştu. Lütfen tekrar deneyin.');
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!selectedBranchId) {
    return (
      <Card padding="md">
        <EmptyState
          title="Şube Seçilmedi"
          description="Lütfen yukarıdaki şube seçiciden işlem yapmak istediğiniz şubeyi belirleyin."
        />
      </Card>
    );
  }

  return (
    <div data-testid="branch-settings-view" style={{ maxWidth: '900px', margin: '0 auto' }}>
      {/* Header and Summary Button */}
      <div
        style={{
          marginBottom: 'var(--ro-space-4)',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: 'var(--ro-space-3)',
        }}
      >
        <div>
          <h2 style={{ fontSize: 'var(--ro-font-size-xl)', fontWeight: 'bold', margin: 0 }}>
            {selectedBranch?.name || 'Şube'} Yönetim Ayarları
          </h2>
          <p style={{ color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-sm)', marginTop: '4px' }}>
            Operasyonel finans parametrelerini ve 7 günlük çalışma takvimini yapılandırın.
          </p>
        </div>

        <Button
          variant="outline"
          size="sm"
          onClick={() => setIsMobileSummaryOpen(true)}
          data-testid="btn-open-summary"
        >
          Şube Özeti (Mobil)
        </Button>
      </div>

      {/* Tabs */}
      <div
        style={{
          display: 'flex',
          gap: 'var(--ro-space-2)',
          borderBottom: '1px solid var(--ro-color-border)',
          marginBottom: 'var(--ro-space-4)',
        }}
      >
        <button
          type="button"
          data-testid="tab-financial"
          onClick={() => handleTabSwitch('financial')}
          style={{
            padding: '8px 16px',
            fontSize: 'var(--ro-font-size-sm)',
            fontWeight: activeTab === 'financial' ? 600 : 400,
            border: 'none',
            borderBottom:
              activeTab === 'financial'
                ? '2px solid var(--ro-color-primary)'
                : '2px solid transparent',
            backgroundColor: 'transparent',
            color:
              activeTab === 'financial'
                ? 'var(--ro-color-primary)'
                : 'var(--ro-color-text-muted)',
            cursor: 'pointer',
          }}
        >
          Finansal & Operasyonel Ayarlar
        </button>
        <button
          type="button"
          data-testid="tab-hours"
          onClick={() => handleTabSwitch('hours')}
          style={{
            padding: '8px 16px',
            fontSize: 'var(--ro-font-size-sm)',
            fontWeight: activeTab === 'hours' ? 600 : 400,
            border: 'none',
            borderBottom:
              activeTab === 'hours'
                ? '2px solid var(--ro-color-primary)'
                : '2px solid transparent',
            backgroundColor: 'transparent',
            color:
              activeTab === 'hours'
                ? 'var(--ro-color-primary)'
                : 'var(--ro-color-text-muted)',
            cursor: 'pointer',
          }}
        >
          Haftalık Çalışma Saatleri
        </button>
      </div>

      {/* Conflict / Error / Success Messages */}
      {isConflict && (
        <div style={{ marginBottom: 'var(--ro-space-4)' }}>
          <Card padding="md">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 'var(--ro-space-3)' }}>
              <p style={{ color: 'var(--ro-color-danger)', margin: 0, fontSize: 'var(--ro-font-size-sm)' }} data-testid="conflict-message">
                Bu ayarlar başka bir kullanıcı veya sekmede güncellendi.
              </p>
              <Button
                variant="primary"
                size="sm"
                data-testid="btn-reload-conflict"
                onClick={loadData}
              >
                Yeniden Yükle
              </Button>
            </div>
          </Card>
        </div>
      )}

      {errorMessage && !isConflict && (
        <div style={{ marginBottom: 'var(--ro-space-4)' }}>
          <Card padding="md">
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <p style={{ color: 'var(--ro-color-danger)', margin: 0, fontSize: 'var(--ro-font-size-sm)' }} data-testid="error-message">
                {errorMessage}
              </p>
              <Button variant="outline" size="sm" onClick={loadData}>
                Tekrar Dene
              </Button>
            </div>
          </Card>
        </div>
      )}

      {successMessage && (
        <div style={{ marginBottom: 'var(--ro-space-4)' }}>
          <Card padding="md">
            <p style={{ color: 'var(--ro-color-success)', margin: 0, fontSize: 'var(--ro-font-size-sm)' }} data-testid="success-message">
              {successMessage}
            </p>
          </Card>
        </div>
      )}

      {/* Loading or Forms */}
      {isLoading ? (
        <div
          data-testid="branch-settings-loading"
          style={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '260px' }}
        >
          <Spinner size="lg" label="Şube ayarları yükleniyor..." />
        </div>
      ) : activeTab === 'financial' && settings ? (
        <BranchFinancialSettingsForm
          settings={settings}
          isSubmitting={isSubmitting}
          onSave={handleSaveFinancial}
          onReset={loadData}
          onHasUnsavedChanges={setHasUnsavedChanges}
        />
      ) : activeTab === 'hours' && operatingHours ? (
        <BranchOperatingHoursForm
          operatingHours={operatingHours}
          isSubmitting={isSubmitting}
          onSave={handleSaveHours}
          onReset={loadData}
          onHasUnsavedChanges={setHasUnsavedChanges}
        />
      ) : (
        <Card padding="md">
          <EmptyState
            title="Ayar Bulunamadı"
            description="Bu şube için kayıtlı ayar verisi yüklenemedi."
          />
        </Card>
      )}

      {/* Mobile BottomSheet for Branch Summary */}
      <BottomSheet
        isOpen={isMobileSummaryOpen}
        onClose={() => setIsMobileSummaryOpen(false)}
        title="Şube Operasyonel Özeti"
      >
        <div style={{ padding: 'var(--ro-space-4)' }} data-testid="mobile-summary-sheet">
          {settings && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
              <div>
                <strong>Şube:</strong> {settings.displayName || settings.branchName}
              </div>
              <div>
                <strong>Saat Dilimi:</strong> {settings.timezone}
              </div>
              <div>
                <strong>Para Birimi:</strong> {settings.currency}
              </div>
              <div>
                <strong>Vergi Oranı:</strong> %{(settings.defaultTaxRateBps / 100).toFixed(2)}{' '}
                {settings.pricesIncludeTax ? '(Dahil)' : '(Hariç)'}
              </div>
              <div>
                <strong>Servis Ücreti:</strong>{' '}
                {settings.isServiceChargeEnabled
                  ? `%${(settings.serviceChargeRateBps / 100).toFixed(2)}`
                  : 'Pasif'}
              </div>
              <div>
                <strong>Sipariş Kabulü:</strong>{' '}
                {settings.isOrderTakingEnabled ? 'Açık' : 'Kapalı'}
              </div>
            </div>
          )}
          <div style={{ marginTop: 'var(--ro-space-4)', display: 'flex', justifyContent: 'flex-end' }}>
            <Button variant="primary" size="sm" onClick={() => setIsMobileSummaryOpen(false)}>
              Kapat
            </Button>
          </div>
        </div>
      </BottomSheet>
    </div>
  );
};
