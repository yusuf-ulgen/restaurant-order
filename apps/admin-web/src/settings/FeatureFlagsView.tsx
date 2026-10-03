import React, { useState, useEffect, useCallback } from 'react';
import {
  Card,
  Button,
  Spinner,
  Badge,
  BottomSheet,
} from '@restaurant-order/ui';
import {
  fetchWithCsrf,
  EffectiveFeatureFlagsContract,
  TenantFeatureFlagsContract,
  BranchFeatureFlagsContract,
  UpdateFeatureFlagsRequest,
} from '@restaurant-order/contracts';
import { useAdminConfig } from '../config/AdminConfigContext';
import { useAuth } from '../auth/AuthContext';

export interface FeatureFlagsViewProps {
  onCancel?: () => void;
}

const FEATURE_CATALOG: { key: string; label: string; description: string; phase: string }[] = [
  { key: 'CustomerQrOrdering', label: 'QR Menü & Sipariş', description: 'Müşterilerin masadaki QR kodu okutarak sipariş vermesine izin verir.', phase: 'Aktif' },
  { key: 'CustomerServiceRequests', label: 'Garson Çağırma & Hizmet Talebi', description: 'Müşterilerin garson veya hesap çağırma talepleri gönderebilmesi.', phase: 'Aktif' },
  { key: 'Tips', label: 'Bahşiş Modülü', description: 'Müşterilerin bahşiş ekleme yeteneği (Faz 10 finansal motor).', phase: 'Gelecek Faz (Varsayılan Kapalı)' },
  { key: 'SplitBilling', label: 'Hesap Bölme', description: 'Masada veya kasada hesabı parçalı/bölüşmeli ödeme (Faz 10).', phase: 'Gelecek Faz (Varsayılan Kapalı)' },
  { key: 'OnlinePayments', label: 'Online / Sanal POS Ödemeleri', description: 'QR veya web üzerinden doğrudan kredi kartıyla ödeme alma (Faz 10).', phase: 'Gelecek Faz (Varsayılan Kapalı)' },
  { key: 'KitchenDisplay', label: 'Mutfak KDS Ekranı', description: 'Mutfak istasyonları için gerçek zamanlı sipariş hazırlık ekranı (Faz 11).', phase: 'Gelecek Faz (Varsayılan Kapalı)' },
  { key: 'BarDisplay', label: 'Bar KDS Ekranı', description: 'Bar istasyonları için gerçek zamanlı içecek hazırlık ekranı (Faz 11).', phase: 'Gelecek Faz (Varsayılan Kapalı)' },
  { key: 'Reservations', label: 'Masa Rezervasyonu', description: 'Müşteri veya personel tarafından masa ayırtma özellikleri (Faz 14).', phase: 'Gelecek Faz (Varsayılan Kapalı)' },
  { key: 'KioskMode', label: 'Kiosk Modu', description: 'Restoran içi self-servis sipariş terminalleri (Faz 14).', phase: 'Gelecek Faz (Varsayılan Kapalı)' },
];

export const FeatureFlagsView: React.FC<FeatureFlagsViewProps> = ({ onCancel }) => {
  const { selectedBranchId, selectedBranch } = useAdminConfig();
  let userRole = 'RestaurantAdmin';
  try {
    const auth = useAuth();
    userRole = auth.user?.role || 'RestaurantAdmin';
  } catch {
    // default
  }

  const isTenantAdmin = userRole === 'RestaurantAdmin';

  const [activeTab, setActiveTab] = useState<'effective' | 'tenant' | 'branch'>('effective');
  const [effectiveData, setEffectiveData] = useState<EffectiveFeatureFlagsContract | null>(null);
  const [tenantData, setTenantData] = useState<TenantFeatureFlagsContract | null>(null);
  const [branchData, setBranchData] = useState<BranchFeatureFlagsContract | null>(null);

  const [isLoading, setIsLoading] = useState<boolean>(true);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  // Edit draft states
  const [tenantDraft, setTenantDraft] = useState<Record<string, boolean>>({});
  const [branchDraft, setBranchDraft] = useState<Record<string, boolean>>({});
  const [isMobileDrawerOpen, setIsMobileDrawerOpen] = useState<boolean>(false);

  const loadData = useCallback(async () => {
    if (!selectedBranchId) return;

    setIsLoading(true);
    setErrorMessage(null);

    try {
      const effPromise = fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/features/effective`);
      const brPromise = fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/features/override`);
      const tnPromise = isTenantAdmin ? fetchWithCsrf('/api/v1/restaurant-config/tenant/features') : Promise.resolve<Response | null>(null);

      const [effRes, brRes, tnRes] = await Promise.all([effPromise, brPromise, tnPromise]);

      if (!effRes.ok) {
        throw new Error('Efektif özellik ayarları yüklenemedi.');
      }
      const eff: EffectiveFeatureFlagsContract = await effRes.json();
      setEffectiveData(eff);

      if (brRes.ok) {
        const br: BranchFeatureFlagsContract = await brRes.json();
        setBranchData(br);
        const brMap: Record<string, boolean> = {};
        br.overrides.forEach((item) => {
          brMap[item.key] = item.isEnabled;
        });
        setBranchDraft(brMap);
      }

      if (tnRes && tnRes.ok) {
        const tn: TenantFeatureFlagsContract = await tnRes.json();
        setTenantData(tn);
        const tnMap: Record<string, boolean> = {};
        tn.flags.forEach((item) => {
          tnMap[item.key] = item.isEnabled;
        });
        setTenantDraft(tnMap);
      }
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Özellik bayrakları yüklenemedi.';
      setErrorMessage(msg);
    } finally {
      setIsLoading(false);
    }
  }, [selectedBranchId, isTenantAdmin]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleSaveTenantDefaults = async () => {
    if (!tenantData) return;
    setIsSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const payload: UpdateFeatureFlagsRequest = {
        flags: tenantDraft,
        concurrencyToken: tenantData.concurrencyToken,
      };
      const res = await fetchWithCsrf('/api/v1/restaurant-config/tenant/features', {
        method: 'PUT',
        headers: {
          'If-Match': `"${tenantData.concurrencyToken}"`,
        },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || 'Tenant varsayılanları güncellenemedi.');
      }
      setSuccessMessage('Tenant varsayılan özellikleri başarıyla güncellendi.');
      await loadData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Kaydetme sırasında bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSaveBranchOverride = async () => {
    if (!selectedBranchId) return;
    setIsSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const payload: UpdateFeatureFlagsRequest = {
        flags: branchDraft,
        concurrencyToken: branchData?.concurrencyToken || null,
      };
      const res = await fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/features/override`, {
        method: 'PUT',
        headers: branchData?.concurrencyToken ? { 'If-Match': `"${branchData.concurrencyToken}"` } : undefined,
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || 'Şube override ayarları güncellenemedi.');
      }
      setSuccessMessage('Şube özellik ayarları başarıyla kaydedildi.');
      await loadData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Kaydetme sırasında bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleClearBranchOverride = async () => {
    if (!selectedBranchId) return;
    const confirmClear = window.confirm('Bu şubeye özel tüm özellik istisnalarını temizleyip tenant varsayılanlarına dönmek istiyor musunuz?');
    if (!confirmClear) return;

    setIsSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    try {
      const token = branchData?.concurrencyToken;
      const res = await fetchWithCsrf(`/api/v1/restaurant-config/branches/${selectedBranchId}/features/override`, {
        method: 'DELETE',
        headers: token ? { 'If-Match': `"${token}"` } : undefined,
        body: JSON.stringify({ concurrencyToken: token }),
      });
      if (!res.ok) {
        const err = await res.json().catch(() => null);
        throw new Error(err?.detail || 'Şube istisnaları temizlenemedi.');
      }
      setSuccessMessage('Şube istisnaları temizlendi. Tenant varsayılanları geçerli kılındı.');
      await loadData();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'İşlem sırasında bir hata oluştu.';
      setErrorMessage(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="feature-flags-view" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-4)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 'var(--ro-space-3)' }}>
        <div>
          <h2 style={{ margin: 0, fontSize: 'var(--ro-font-size-lg)', fontWeight: 'var(--ro-font-weight-bold)' }}>
            Özellik Yönetimi (Feature Flags)
          </h2>
          <p style={{ margin: 'var(--ro-space-1) 0 0', color: 'var(--ro-color-text-secondary)', fontSize: 'var(--ro-font-size-sm)' }}>
            {selectedBranch?.name || 'Seçili Şube'} ve organizasyonel modül görünürlük kontrolleri
          </p>
        </div>
        <div style={{ display: 'flex', gap: 'var(--ro-space-2)' }}>
          {onCancel && (
            <Button variant="outline" size="sm" onClick={onCancel}>
              Kapat
            </Button>
          )}
          <Button variant="outline" size="sm" onClick={() => setIsMobileDrawerOpen(true)}>
            Güvenlik Bilgisi
          </Button>
        </div>
      </div>

      <div style={{ display: 'flex', gap: 'var(--ro-space-2)', borderBottom: '1px solid var(--ro-color-border)', paddingBottom: 'var(--ro-space-2)' }}>
        <Button
          variant={activeTab === 'effective' ? 'primary' : 'ghost'}
          size="sm"
          onClick={() => setActiveTab('effective')}
          data-testid="tab-effective-flags"
        >
          Efektif Ayarlar ({selectedBranch?.name || 'Şube'})
        </Button>
        <Button
          variant={activeTab === 'branch' ? 'primary' : 'ghost'}
          size="sm"
          onClick={() => setActiveTab('branch')}
          data-testid="tab-branch-override"
        >
          Şube İstisnaları (Override)
        </Button>
        {isTenantAdmin && (
          <Button
            variant={activeTab === 'tenant' ? 'primary' : 'ghost'}
            size="sm"
            onClick={() => setActiveTab('tenant')}
            data-testid="tab-tenant-defaults"
          >
            Tenant Varsayılanları
          </Button>
        )}
      </div>

      {errorMessage && (
        <Card padding="sm">
          <div data-testid="feature-error" style={{ color: 'var(--ro-color-danger)', fontSize: 'var(--ro-font-size-sm)' }}>
            {errorMessage}
          </div>
        </Card>
      )}

      {successMessage && (
        <Card padding="sm">
          <div data-testid="feature-success" style={{ color: 'var(--ro-color-success, #10b981)', fontSize: 'var(--ro-font-size-sm)' }}>
            {successMessage}
          </div>
        </Card>
      )}

      {isLoading ? (
        <div data-testid="features-loading" style={{ display: 'flex', justifyContent: 'center', padding: 'var(--ro-space-6)' }}>
          <Spinner size="lg" label="Özellik bayrakları yükleniyor..." />
        </div>
      ) : activeTab === 'effective' ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
          <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)', color: 'var(--ro-color-text-secondary)' }}>
            Aşağıda bu şube için nihai olarak hesaplanan (Tenant varsayılanı + Şube istisnası) aktif özellikler listelenmektedir.
          </p>
          {FEATURE_CATALOG.map((item) => {
            const isEnabled = effectiveData?.evaluatedFlags[item.key] ?? false;
            const detailItem = effectiveData?.flags.find((f) => f.key === item.key);
            const source = detailItem?.source || 'CatalogDefault';
            return (
              <Card key={item.key} padding="md">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 'var(--ro-space-2)' }}>
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)' }}>
                      <strong>{item.label}</strong>
                      <Badge variant={isEnabled ? 'success' : 'neutral'}>
                        {isEnabled ? 'Açık' : 'Kapalı'}
                      </Badge>
                      <Badge variant="neutral">Kaynak: {source}</Badge>
                    </div>
                    <p style={{ margin: 'var(--ro-space-1) 0 0', fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-secondary)' }}>
                      {item.description}
                    </p>
                  </div>
                  <span style={{ fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-secondary)' }}>
                    {item.phase}
                  </span>
                </div>
              </Card>
            );
          })}
        </div>
      ) : activeTab === 'branch' ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)', color: 'var(--ro-color-text-secondary)' }}>
              Bu şubeye özel istisna bayrakları belirleyin. İstisnalar temizlendiğinde tenant varsayılanı geçerli olur.
            </p>
            {branchData && branchData.overrides.length > 0 && (
              <Button variant="outline" size="sm" onClick={handleClearBranchOverride} disabled={isSubmitting} data-testid="clear-branch-overrides">
                İstisnaları Temizle
              </Button>
            )}
          </div>
          {FEATURE_CATALOG.map((item) => {
            const isChecked = branchDraft[item.key] ?? false;
            return (
              <Card key={item.key} padding="md">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <strong>{item.label}</strong>
                    <p style={{ margin: 'var(--ro-space-1) 0 0', fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-secondary)' }}>
                      {item.description}
                    </p>
                  </div>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)', cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      data-testid={`branch-flag-${item.key}`}
                      checked={isChecked}
                      onChange={(e) => setBranchDraft({ ...branchDraft, [item.key]: e.target.checked })}
                    />
                    <span style={{ fontSize: 'var(--ro-font-size-sm)' }}>{isChecked ? 'Açık' : 'Kapalı'}</span>
                  </label>
                </div>
              </Card>
            );
          })}
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 'var(--ro-space-3)' }}>
            <Button variant="primary" size="md" onClick={handleSaveBranchOverride} disabled={isSubmitting} data-testid="save-branch-flags">
              {isSubmitting ? 'Kaydediliyor...' : 'Şube İstisnalarını Kaydet'}
            </Button>
          </div>
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}>
          <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)', color: 'var(--ro-color-text-secondary)' }}>
            Tüm şubeler için geçerli olacak standart organizasyonel varsayılan özellikler.
          </p>
          {FEATURE_CATALOG.map((item) => {
            const isChecked = tenantDraft[item.key] ?? false;
            return (
              <Card key={item.key} padding="md">
                <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                  <div>
                    <strong>{item.label}</strong>
                    <p style={{ margin: 'var(--ro-space-1) 0 0', fontSize: 'var(--ro-font-size-xs)', color: 'var(--ro-color-text-secondary)' }}>
                      {item.description}
                    </p>
                  </div>
                  <label style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-2)', cursor: 'pointer' }}>
                    <input
                      type="checkbox"
                      data-testid={`tenant-flag-${item.key}`}
                      checked={isChecked}
                      onChange={(e) => setTenantDraft({ ...tenantDraft, [item.key]: e.target.checked })}
                    />
                    <span style={{ fontSize: 'var(--ro-font-size-sm)' }}>{isChecked ? 'Açık' : 'Kapalı'}</span>
                  </label>
                </div>
              </Card>
            );
          })}
          <div style={{ display: 'flex', justifyContent: 'flex-end', marginTop: 'var(--ro-space-3)' }}>
            <Button variant="primary" size="md" onClick={handleSaveTenantDefaults} disabled={isSubmitting} data-testid="save-tenant-flags">
              {isSubmitting ? 'Kaydediliyor...' : 'Tenant Varsayılanlarını Kaydet'}
            </Button>
          </div>
        </div>
      )}

      {/* Security note BottomSheet */}
      <BottomSheet
        isOpen={isMobileDrawerOpen}
        onClose={() => setIsMobileDrawerOpen(false)}
        title="Özellik Bayrağı Güvenlik İlkeleri"
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)', padding: 'var(--ro-space-2)' }}>
          <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)' }}>
            <strong>1. RBAC Yerine Geçemez:</strong> Özellik bayrakları yalnızca kullanıcı arayüzü ve davranış uygunluğunu kontrol eder; backend yetki sınırlarını gevşetemez.
          </p>
          <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)' }}>
            <strong>2. İzolasyon Korunur:</strong> Bir şubenin veya tenant&apos;ın bayrağı diğer tenant veya şubelerin verilerine erişim sağlayamaz.
          </p>
          <p style={{ margin: 0, fontSize: 'var(--ro-font-size-sm)' }}>
            <strong>3. Güvenli Varsayılanlar:</strong> Geliştirilmekte olan veya tamamlanmamış modüller varsayılan olarak kesinlikle kapalıdır.
          </p>
          <Button variant="primary" size="sm" onClick={() => setIsMobileDrawerOpen(false)}>
            Anladım
          </Button>
        </div>
      </BottomSheet>
    </div>
  );
};
