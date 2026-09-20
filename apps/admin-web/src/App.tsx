import React from 'react';
import {
  Card,
  Badge,
  ErrorBoundary,
  EmptyState,
  AppShell,
} from '@restaurant-order/ui';

export interface AdminAppProps {
  hasMetrics?: boolean;
  initialError?: boolean;
}

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
        sections: [
          {
            id: 'sec-nav',
            items: [
              { id: 'dashboard', label: 'Kontrol Paneli', isActive: true },
              { id: 'menu', label: 'Menü Yönetimi' },
              { id: 'tables', label: 'Şube & Masalar' },
            ],
          },
        ],
      }}
      header={{
        title: (
          <h1 className="title" style={{ margin: 0, fontSize: 'var(--ro-font-size-lg)' }}>
            Yönetim Paneli
          </h1>
        ),
        subtitle: (
          <p className="subtitle" style={{ margin: 0 }}>
            Organizasyon Genel Bakışı
          </p>
        ),
        actions: <Badge variant="primary">Restoran Admini</Badge>,
      }}
    >
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
        <section
          aria-label="Özet Metrikler"
          style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))',
            gap: '20px',
          }}
        >
          <Card padding="md">
            <h3 style={{ fontSize: '1rem', color: 'var(--ro-color-text-muted)' }}>
              Günlük Sipariş
            </h3>
            <p style={{ fontSize: '1.75rem', fontWeight: 700, marginTop: '8px' }}>0</p>
          </Card>
          <Card padding="md">
            <h3 style={{ fontSize: '1rem', color: 'var(--ro-color-text-muted)' }}>
              Aktif Masalar
            </h3>
            <p style={{ fontSize: '1.75rem', fontWeight: 700, marginTop: '8px' }}>0</p>
          </Card>
        </section>
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
