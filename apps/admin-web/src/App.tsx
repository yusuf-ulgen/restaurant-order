import React from 'react';
import {
  Card,
  Badge,
  ErrorBoundary,
  EmptyState,
  AppShell,
  PageHeader,
  ContentSection,
  NavSectionConfig,
} from '@restaurant-order/ui';

export interface AdminAppProps {
  hasMetrics?: boolean;
  initialError?: boolean;
}

export const adminSidebarSections: NavSectionConfig[] = [
  {
    id: 'main',
    title: 'Ana Menü',
    order: 1,
    items: [
      { id: 'dashboard', label: 'Kontrol Paneli', isActive: true, order: 1 },
      { id: 'menu', label: 'Menü Yönetimi', order: 2 },
      { id: 'tables', label: 'Şube & Masalar', order: 3 },
    ],
  },
  {
    id: 'operations',
    title: 'Operasyon',
    order: 2,
    items: [
      { id: 'orders', label: 'Siparişler', order: 1 },
      { id: 'staff', label: 'Personel & Vardiya', order: 2 },
    ],
  },
  {
    id: 'system',
    title: 'Sistem',
    order: 3,
    items: [
      { id: 'reports', label: 'Raporlar', order: 1 },
      { id: 'settings', label: 'Ayarlar', order: 2 },
    ],
  },
];

export const AdminContent: React.FC<AdminAppProps> = ({
  hasMetrics = true,
  initialError = false,
}) => {
  if (initialError) {
    throw new Error('Yönetim verileri yüklenemedi.');
  }

  return (
    <AppShell
      variant="admin"
      sidebar={{
        title: 'Restoran Yönetim',
        ariaLabel: 'Yönetim Menüsü',
        navAriaLabel: 'Ana Gezinti',
        sections: adminSidebarSections,
      }}
      header={{
        title: <h1 className="title">Yönetim Paneli</h1>,
        subtitle: <p className="subtitle">Organizasyon Genel Bakışı</p>,
        actions: <Badge variant="primary">Restoran Admini</Badge>,
      }}
      footer={{
        copyright: '© 2026 Restaurant Order Platform',
        businessText: 'Admin Kontrol Paneli',
      }}
    >
      <PageHeader
        title="Genel Bakış"
        subtitle="Güncel şube durumları ve operasyonel göstergeler"
        headingLevel={3}
      />

      {!hasMetrics ? (
        <Card padding="md">
          <EmptyState
            title="Henüz Raporlanmış Veri Yok"
            description="Seçili şube ve dönem için henüz sipariş veya ciro verisi kaydedilmemiştir."
            actionLabel="Raporları Yenile"
            onAction={() => {}}
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
    </AppShell>
  );
};

export const App: React.FC<AdminAppProps> = (props) => {
  return (
    <ErrorBoundary>
      <AdminContent {...props} />
    </ErrorBoundary>
  );
};

export default App;
