import React, { useState, useEffect } from 'react';
import {
  Button,
  FormError,
  BottomSheet,
  NavigationItemOverrideContract,
} from '@restaurant-order/ui';
import { fetchWithCsrf } from '@restaurant-order/contracts';
import { useAdminConfig } from '../config/AdminConfigContext';
import { NavigationRegistryId } from '../navigation/navigationRegistry';
import { LiveThemePreview } from './LiveThemePreview';
import { NavigationConfigTable } from './NavigationConfigTable';
import { ThemeColorFields } from './ThemeColorFields';
import { ThemeBrandIdentityFields } from './ThemeBrandIdentityFields';
import { ThemeTextHeaderFooterFields } from './ThemeTextHeaderFooterFields';

interface ThemeFormData {
  displayName: string;
  logoUrl: string;
  faviconUrl: string;
  primaryColor: string;
  primaryHoverColor: string;
  secondaryColor: string;
  accentColor: string;
  surfaceColor: string;
  backgroundColor: string;
  shellTitle: string;
  shellSubtitle: string;
  footerText: string;
  footerBranchInfo: string;
  navigationOverrides: NavigationItemOverrideContract[];
}

export interface BrandingSettingsViewProps {
  onSaved?: () => void;
  onCancel?: () => void;
}

export const BrandingSettingsView: React.FC<BrandingSettingsViewProps> = ({ onSaved, onCancel }) => {
  const { theme, reload, updateThemeState } = useAdminConfig();

  const [formData, setFormData] = useState<ThemeFormData>({
    displayName: theme?.brandDisplayName || '',
    logoUrl: theme?.logoUrl || '',
    faviconUrl: theme?.faviconUrl || '',
    primaryColor: theme?.primaryColor || '#111827',
    primaryHoverColor: theme?.primaryHoverColor || '#1f2937',
    secondaryColor: theme?.secondaryColor || '#4b5563',
    accentColor: theme?.accentColor || '#2563eb',
    surfaceColor: theme?.surfaceColor || '#ffffff',
    backgroundColor: theme?.backgroundColor || '#f9fafb',
    shellTitle: theme?.shellTitle || '',
    shellSubtitle: theme?.shellSubtitle || '',
    footerText: theme?.footerText || '',
    footerBranchInfo: theme?.footerBranchInfo || '',
    navigationOverrides: theme?.navigationOverrides ? [...theme.navigationOverrides] : [],
  });

  const [concurrencyToken, setConcurrencyToken] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState<boolean>(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);
  const [isPreviewOpen, setIsPreviewOpen] = useState<boolean>(false);

  useEffect(() => {
    if (theme) {
      setFormData({
        displayName: theme.brandDisplayName || '',
        logoUrl: theme.logoUrl || '',
        faviconUrl: theme.faviconUrl || '',
        primaryColor: theme.primaryColor || '#111827',
        primaryHoverColor: theme.primaryHoverColor || '#1f2937',
        secondaryColor: theme.secondaryColor || '#4b5563',
        accentColor: theme.accentColor || '#2563eb',
        surfaceColor: theme.surfaceColor || '#ffffff',
        backgroundColor: theme.backgroundColor || '#f9fafb',
        shellTitle: theme.shellTitle || '',
        shellSubtitle: theme.shellSubtitle || '',
        footerText: theme.footerText || '',
        footerBranchInfo: theme.footerBranchInfo || '',
        navigationOverrides: theme.navigationOverrides ? [...theme.navigationOverrides] : [],
      });
    }
  }, [theme]);

  // Load latest brand theme to get the latest concurrency token
  useEffect(() => {
    let isMounted = true;
    async function fetchLatestBrandTheme() {
      if (!theme?.brandId) return;
      try {
        const res = await fetchWithCsrf(`/api/v1/restaurant-config/brands/${theme.brandId}/theme`);
        if (res.ok && isMounted) {
          const data = await res.json();
          if (data.concurrencyToken) {
            setConcurrencyToken(data.concurrencyToken);
          }
        }
      } catch {
        // Silently keep default token state
      }
    }
    fetchLatestBrandTheme();
    return () => {
      isMounted = false;
    };
  }, [theme?.brandId]);

  const handleFieldChange = (field: keyof ThemeFormData, value: string) => {
    setFormData((prev) => ({ ...prev, [field]: value }));
  };

  const handleNavOverrideChange = (
    id: NavigationRegistryId,
    updates: Partial<NavigationItemOverrideContract>
  ) => {
    setFormData((prev) => {
      const existing = prev.navigationOverrides.find((o) => o.id === id);
      let updated: NavigationItemOverrideContract[];
      if (existing) {
        updated = prev.navigationOverrides.map((o) =>
          o.id === id ? { ...o, ...updates } : o
        );
      } else {
        updated = [...prev.navigationOverrides, { id, ...updates }];
      }
      return { ...prev, navigationOverrides: updated };
    });
  };

  const validateForm = (): boolean => {
    setErrorMessage(null);

    const hexRegex = /^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$/;
    const colors = [
      { name: 'Primary Renk', val: formData.primaryColor },
      { name: 'Primary Hover', val: formData.primaryHoverColor },
      { name: 'Secondary Renk', val: formData.secondaryColor },
      { name: 'Accent Renk', val: formData.accentColor },
      { name: 'Surface Renk', val: formData.surfaceColor },
      { name: 'Background Renk', val: formData.backgroundColor },
    ];

    for (const c of colors) {
      if (!hexRegex.test(c.val.trim())) {
        setErrorMessage(`${c.name} geçerli bir hex renk kodu (#RGB veya #RRGGBB) olmalıdır.`);
        return false;
      }
    }

    if (formData.logoUrl && !formData.logoUrl.startsWith('/') && !formData.logoUrl.startsWith('https://')) {
      setErrorMessage('Logo URL yalnızca HTTPS veya kök dizin yolu (/) ile başlayabilir.');
      return false;
    }

    if (formData.faviconUrl && !formData.faviconUrl.startsWith('/') && !formData.faviconUrl.startsWith('https://')) {
      setErrorMessage('Favicon URL yalnızca HTTPS veya kök dizin yolu (/) ile başlayabilir.');
      return false;
    }

    return true;
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!validateForm()) return;
    if (!theme?.brandId) {
      setErrorMessage('Marka bağlamı bulunamadı.');
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);
    setSuccessMessage(null);

    const payload = {
      displayName: formData.displayName.trim(),
      logoUrl: formData.logoUrl.trim() || null,
      faviconUrl: formData.faviconUrl.trim() || null,
      primaryColor: formData.primaryColor.trim(),
      primaryHoverColor: formData.primaryHoverColor.trim(),
      secondaryColor: formData.secondaryColor.trim(),
      accentColor: formData.accentColor.trim(),
      surfaceColor: formData.surfaceColor.trim(),
      backgroundColor: formData.backgroundColor.trim(),
      shellTitle: formData.shellTitle.trim() || null,
      shellSubtitle: formData.shellSubtitle.trim() || null,
      footerText: formData.footerText.trim() || null,
      footerBranchInfo: formData.footerBranchInfo.trim() || null,
      concurrencyToken,
      navigationOverrides: formData.navigationOverrides,
    };

    try {
      const url = `/api/v1/restaurant-config/brands/${theme.brandId}/theme`;
      const res = await fetchWithCsrf(url, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });

      if (res.ok) {
        const savedData = await res.json();
        setSuccessMessage('Kurumsal görünüm ayarları başarıyla kaydedildi.');
        if (savedData.concurrencyToken) {
          setConcurrencyToken(savedData.concurrencyToken);
        }
        updateThemeState({
          brandDisplayName: formData.displayName,
          logoUrl: formData.logoUrl || null,
          faviconUrl: formData.faviconUrl || null,
          primaryColor: formData.primaryColor,
          primaryHoverColor: formData.primaryHoverColor,
          secondaryColor: formData.secondaryColor,
          accentColor: formData.accentColor,
          surfaceColor: formData.surfaceColor,
          backgroundColor: formData.backgroundColor,
          shellTitle: formData.shellTitle || null,
          shellSubtitle: formData.shellSubtitle || null,
          footerText: formData.footerText || null,
          footerBranchInfo: formData.footerBranchInfo || null,
          navigationOverrides: formData.navigationOverrides,
        });
        await reload();
        onSaved?.();
      } else if (res.status === 409) {
        setErrorMessage(
          'Bu ayarlar başka bir kullanıcı tarafından güncellenmiştir. Lütfen sayfayı yenileyip tekrar deneyin.'
        );
      } else {
        const errData = await res.json().catch(() => null);
        setErrorMessage(errData?.detail || 'Ayarlar kaydedilirken bir hata oluştu.');
      }
    } catch {
      setErrorMessage('Ağ bağlantı hatası oluştu. Lütfen tekrar deneyin.');
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleReset = () => {
    if (!theme) return;
    setFormData({
      displayName: theme.brandDisplayName || '',
      logoUrl: theme.logoUrl || '',
      faviconUrl: theme.faviconUrl || '',
      primaryColor: theme.primaryColor || '#111827',
      primaryHoverColor: theme.primaryHoverColor || '#1f2937',
      secondaryColor: theme.secondaryColor || '#4b5563',
      accentColor: theme.accentColor || '#2563eb',
      surfaceColor: theme.surfaceColor || '#ffffff',
      backgroundColor: theme.backgroundColor || '#f9fafb',
      shellTitle: theme.shellTitle || '',
      shellSubtitle: theme.shellSubtitle || '',
      footerText: theme.footerText || '',
      footerBranchInfo: theme.footerBranchInfo || '',
      navigationOverrides: theme.navigationOverrides ? [...theme.navigationOverrides] : [],
    });
    setErrorMessage(null);
    setSuccessMessage(null);
  };

  const handleRevertBranchOverride = async () => {
    if (!theme?.branchId || !theme.hasBranchOverride) return;
    setIsSubmitting(true);
    setErrorMessage(null);
    try {
      const res = await fetchWithCsrf(`/api/v1/restaurant-config/branches/${theme.branchId}/theme`, {
        method: 'DELETE',
      });
      if (res.ok) {
        setSuccessMessage('Şube özelleştirmeleri temizlendi ve marka görünümüne dönüldü.');
        await reload();
      } else {
        setErrorMessage('Şube özelleştirmesi temizlenemedi.');
      }
    } catch {
      setErrorMessage('Bağlantı hatası oluştu.');
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div data-testid="branding-settings-view" style={{ maxWidth: '900px', margin: '0 auto' }}>
      <div style={{ marginBottom: 'var(--ro-space-4)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div>
          <h2 style={{ fontSize: 'var(--ro-font-size-xl)', fontWeight: 'bold', margin: 0 }}>
            Kurumsal Görünüm ve Tema Ayarları
          </h2>
          <p style={{ color: 'var(--ro-color-text-muted)', fontSize: 'var(--ro-font-size-sm)', marginTop: '4px' }}>
            Marka ve şube bazlı kontrollü tasarım tokenlarını ve navigasyon görünürlüğünü yapılandırın.
          </p>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setIsPreviewOpen(true)}
          style={{ display: 'inline-flex' }}
        >
          Canlı Önizleme
        </Button>
      </div>

      {errorMessage && (
        <div style={{ marginBottom: 'var(--ro-space-4)' }}>
          <FormError id="branding-error">{errorMessage}</FormError>
        </div>
      )}

      {successMessage && (
        <div
          data-testid="branding-success-message"
          role="status"
          style={{
            padding: 'var(--ro-space-3) var(--ro-space-4)',
            backgroundColor: '#f0fdf4',
            border: '1px solid #bbf7d0',
            borderRadius: 'var(--ro-radius-md)',
            color: '#166534',
            fontSize: 'var(--ro-font-size-sm)',
            marginBottom: 'var(--ro-space-4)',
          }}
        >
          {successMessage}
        </div>
      )}

      <form onSubmit={handleSave}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-6)' }}>
          {/* Section 1: Brand & Logo */}
          <ThemeBrandIdentityFields
            values={{
              displayName: formData.displayName,
              logoUrl: formData.logoUrl,
              faviconUrl: formData.faviconUrl,
            }}
            onChange={(field, val) => handleFieldChange(field, val)}
          />

          {/* Section 2: Controlled Design Tokens */}
          <ThemeColorFields
            colors={{
              primaryColor: formData.primaryColor,
              primaryHoverColor: formData.primaryHoverColor,
              secondaryColor: formData.secondaryColor,
              accentColor: formData.accentColor,
              surfaceColor: formData.surfaceColor,
              backgroundColor: formData.backgroundColor,
            }}
            onChange={(field, val) => handleFieldChange(field, val)}
          />

          {/* Section 3: Header & Footer Text */}
          <ThemeTextHeaderFooterFields
            values={{
              shellTitle: formData.shellTitle,
              shellSubtitle: formData.shellSubtitle,
              footerText: formData.footerText,
            }}
            onChange={(field, val) => handleFieldChange(field, val)}
          />

          {/* Section 4: Navigation Visibility & Sequence Customization */}
          <NavigationConfigTable
            navigationOverrides={formData.navigationOverrides}
            onChange={handleNavOverrideChange}
          />

          {/* Action Buttons */}
          <div style={{ display: 'flex', gap: 'var(--ro-space-3)', justifyContent: 'flex-end', flexWrap: 'wrap' }}>
            {theme?.hasBranchOverride && (
              <Button
                type="button"
                variant="danger"
                size="md"
                onClick={handleRevertBranchOverride}
                disabled={isSubmitting}
              >
                Marka Ayarlarına Dön
              </Button>
            )}
            <Button
              type="button"
              variant="outline"
              size="md"
              onClick={handleReset}
              disabled={isSubmitting}
            >
              Sıfırla
            </Button>
            {onCancel && (
              <Button
                type="button"
                variant="ghost"
                size="md"
                onClick={onCancel}
                disabled={isSubmitting}
              >
                İptal
              </Button>
            )}
            <Button
              type="submit"
              variant="primary"
              size="md"
              disabled={isSubmitting}
              loading={isSubmitting}
            >
              Ayarları Kaydet
            </Button>
          </div>
        </div>
      </form>

      {/* Mobile / Dialog Live Preview */}
      <BottomSheet
        isOpen={isPreviewOpen}
        onClose={() => setIsPreviewOpen(false)}
        title="Canlı Tema Önizlemesi"
      >
        <div style={{ padding: 'var(--ro-space-4)' }}>
          <LiveThemePreview
            backgroundColor={formData.backgroundColor}
            surfaceColor={formData.surfaceColor}
            primaryColor={formData.primaryColor}
            accentColor={formData.accentColor}
            logoUrl={formData.logoUrl}
            shellTitle={formData.shellTitle}
            displayName={formData.displayName}
            shellSubtitle={formData.shellSubtitle}
            footerBranchInfo={formData.footerBranchInfo}
            footerText={formData.footerText}
          />
          <div style={{ marginTop: 'var(--ro-space-4)', display: 'flex', justifyContent: 'flex-end' }}>
            <Button variant="primary" size="sm" onClick={() => setIsPreviewOpen(false)}>
              Kapat
            </Button>
          </div>
        </div>
      </BottomSheet>
    </div>
  );
};
