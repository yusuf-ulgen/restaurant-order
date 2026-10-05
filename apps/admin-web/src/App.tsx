import React, { useState } from 'react';
import {
  Card,
  Badge,
  Button,
  Spinner,
  ErrorBoundary,
  EmptyState,
  AppShell,
  PageHeader,
  ContentSection,
} from '@restaurant-order/ui';
import { AuthProvider, useAuth } from './auth/AuthContext';
import { ProtectedRoute } from './auth/ProtectedRoute';
import { AdminConfigProvider, useAdminConfig } from './config/AdminConfigContext';
import {
  resolveAdminNavigation,
  NavigationRegistryId,
  NAVIGATION_REGISTRY,
} from './navigation/navigationRegistry';
import { BrandingSettingsView } from './settings/BrandingSettingsView';
import { BranchSettingsView } from './settings/BranchSettingsView';
import { DiningAreasView } from './settings/DiningAreasView';
import { PreparationStationsView } from './settings/PreparationStationsView';
import { FeatureFlagsView } from './settings/FeatureFlagsView';
import { MenuCatalogView } from './catalog/MenuCatalogView';

export interface AdminAppProps {
  hasMetrics?: boolean;
  initialError?: boolean;
  initialView?: NavigationRegistryId;
}

function formatRole(role?: string): string {
  switch (role) {
    case 'SuperAdmin':
      return 'Super Admin';
    case 'RestaurantAdmin':
      return 'Restoran Admini';
    case 'BranchManager':
      return 'Şube Müdürü';
    case 'Cashier':
      return 'Kasa / Operasyon';
    case 'Waiter':
      return 'Garson';
    default:
      return role || 'Kullanıcı';
  }
}

export const AdminContent: React.FC<AdminAppProps> = ({
  hasMetrics = true,
  initialError = false,
  initialView = 'dashboard',
}) => {
  if (initialError) {
    throw new Error('Yönetim verileri yüklenemedi.');
  }

  let user = null;
  try {
    const auth = useAuth();
    user = auth.user;
  } catch {
    user = {
      userId: 'user-admin-1',
      email: 'admin@restoran.com',
      role: 'RestaurantAdmin',
      tenantId: 'tenant-1',
      securityVersion: 1,
    };
  }
  const {
    theme,
    branches,
    selectedBranchId,
    selectedBranch,
    selectBranch,
    isLoading,
    error,
    retry,
  } = useAdminConfig();

  const [currentView, setCurrentView] = useState<NavigationRegistryId>(initialView);

  // Resolve dynamic sidebar navigation from registry, db overrides, and user RBAC
  const sidebarSections = resolveAdminNavigation(
    theme?.navigationOverrides,
    user?.role,
    [],
    currentView,
    (viewId) => setCurrentView(viewId)
  );

  const activeItem = NAVIGATION_REGISTRY[currentView];

  return (
    <AppShell
      variant="admin"
      sidebar={{
        title: theme?.brandDisplayName || 'Restoran Yönetim',
        ariaLabel: 'Yönetim Menüsü',
        navAriaLabel: 'Ana Gezinti',
        sections: sidebarSections,
      }}
      header={{
        logoUrl: theme?.logoUrl || undefined,
        title: (
          <h1
            className="title"
            style={{
              fontSize: 'var(--ro-font-size-md)',
              fontWeight: 'var(--ro-font-weight-bold)',
              margin: 0,
            }}
          >
            {theme?.shellTitle || 'Yönetim Paneli'}
          </h1>
        ),
        subtitle: (
          <div
            className="subtitle"
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '6px',
              fontSize: 'var(--ro-font-size-xs)',
              color: 'var(--ro-color-text-muted)',
            }}
          >
            <span data-testid="header-branch-name">
              {selectedBranch?.name || theme?.branchDisplayName || 'Merkez Şube'}
            </span>
            {theme?.shellSubtitle && (
              <>
                <span aria-hidden="true">•</span>
                <span data-testid="header-shell-subtitle">{theme.shellSubtitle}</span>
              </>
            )}
          </div>
        ),
        actions: (
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--ro-space-3)' }}>
            {branches.length > 1 && (
              <select
                data-testid="branch-switcher"
                aria-label="Şube Seçici"
                value={selectedBranchId || ''}
                onChange={(e) => selectBranch(e.target.value)}
                style={{
                  padding: '4px 8px',
                  fontSize: 'var(--ro-font-size-xs)',
                  borderRadius: 'var(--ro-radius-sm)',
                  border: '1px solid var(--ro-color-border)',
                  backgroundColor: 'var(--ro-color-surface)',
                  color: 'var(--ro-color-text-primary)',
                  cursor: 'pointer',
                }}
              >
                {branches.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </select>
            )}
            <Badge variant="primary">{formatRole(user?.role)}</Badge>
          </div>
        ),
      }}
      footer={{
        copyright: '© 2026 Restaurant Order Platform',
        businessText:
          theme?.footerBranchInfo || theme?.footerText || 'Admin Kontrol Paneli',
        links: [
          { id: 'privacy', label: 'Gizlilik', href: '#/privacy' },
          { id: 'support', label: 'Destek', href: '#/support' },
        ],
        visible: true,
      }}
    >
      {/* API Error Notification with Retry */}
      {error && (
        <div style={{ marginBottom: 'var(--ro-space-4)' }}>
          <Card padding="md">
            <div
              style={{
                display: 'flex',
                justifyContent: 'space-between',
                alignItems: 'center',
                gap: 'var(--ro-space-3)',
                flexWrap: 'wrap',
              }}
            >
              <p
                data-testid="config-error-message"
                style={{ color: 'var(--ro-color-danger)', margin: 0, fontSize: 'var(--ro-font-size-sm)' }}
              >
                {error}
              </p>
              <Button variant="outline" size="sm" onClick={retry}>
                Tekrar Dene
              </Button>
            </div>
          </Card>
        </div>
      )}

      {/* Loading state indicator */}
      {isLoading ? (
        <div
          data-testid="admin-loading"
          style={{
            display: 'flex',
            justifyContent: 'center',
            alignItems: 'center',
            minHeight: '260px',
          }}
        >
          <Spinner size="lg" label="Yönetim ayarları yükleniyor..." />
        </div>
      ) : currentView === 'brand-settings' ? (
        <BrandingSettingsView
          onSaved={() => setCurrentView('dashboard')}
          onCancel={() => setCurrentView('dashboard')}
        />
      ) : currentView === 'branch-settings' ? (
        <BranchSettingsView
          initialTab="financial"
          onSaved={() => {}}
          onCancel={() => setCurrentView('dashboard')}
        />
      ) : currentView === 'operating-hours' ? (
        <BranchSettingsView
          initialTab="hours"
          onSaved={() => {}}
          onCancel={() => setCurrentView('dashboard')}
        />
      ) : currentView === 'dining-areas' ? (
        <DiningAreasView
          onCancel={() => setCurrentView('dashboard')}
        />
      ) : currentView === 'preparation-stations' ? (
        <PreparationStationsView
          onCancel={() => setCurrentView('dashboard')}
        />
      ) : currentView === 'feature-settings' ? (
        <FeatureFlagsView
          onCancel={() => setCurrentView('dashboard')}
        />
      ) : currentView === 'menu' ? (
        <MenuCatalogView />
      ) : currentView === 'dashboard' ? (
        <>
          <PageHeader
            title="Genel Bakış"
            subtitle="Güncel şube durumları ve operasyonel göstergeler"
            headingLevel={3}
          />

          {!hasMetrics ? (
            <Card padding="md">
              <EmptyState
                title="Henüz Raporlanmış Veri Yok"
                description="Seçili şube ve dönem için henüz sipariş veya ciro verisi kaydedilmemiştir. Raporlama özellikleri Phase 13'te etkinleşecektir."
              />
            </Card>
          ) : (
            <ContentSection
              title="Özet Metrikler"
              description="Bugüne ait canlı operasyon metrikleri"
              variant="default"
            >
              <div className="admin-kpi-grid">
                <Card padding="md">
                  <h3 className="admin-kpi-label">Günlük Sipariş</h3>
                  <p className="admin-kpi-value">0</p>
                  <p className="admin-kpi-subtext">Bugün tamamlanan sipariş sayısı</p>
                </Card>
                <Card padding="md">
                  <h3 className="admin-kpi-label">Aktif Masalar</h3>
                  <p className="admin-kpi-value">0</p>
                  <p className="admin-kpi-subtext">Şu anda oturan veya sipariş bekleyen masalar</p>
                </Card>
              </div>
            </ContentSection>
          )}
        </>
      ) : (
        <Card padding="md">
          <EmptyState
            title={activeItem?.defaultLabel || 'Modül'}
            description="Bu modül yol haritasında planlanmış olup henüz aktif değildir."
          />
        </Card>
      )}
    </AppShell>
  );
};

export const App: React.FC<AdminAppProps> = (props) => {
  return (
    <ErrorBoundary>
      <AuthProvider>
        <AdminConfigProvider>
          <ProtectedRoute allowedRoles={['SuperAdmin', 'RestaurantAdmin', 'BranchManager']}>
            <AdminContent {...props} />
          </ProtectedRoute>
        </AdminConfigProvider>
      </AuthProvider>
    </ErrorBoundary>
  );
};

export default App;
