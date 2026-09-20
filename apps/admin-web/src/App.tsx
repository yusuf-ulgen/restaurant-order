import React from 'react';
import { Card, Button, Badge, ErrorBoundary, EmptyState } from '@restaurant-order/ui';

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
    <div className="layout">
      <aside className="sidebar" aria-label="Yönetim Menüsü">
        <h2 style={{ fontSize: '1.25rem', fontWeight: 700, marginBottom: '24px' }}>Restoran Yönetim</h2>
        <nav aria-label="Ana Gezinti" style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
          <Button variant="outline" size="sm" style={{ color: '#ffffff', borderColor: 'rgba(255,255,255,0.2)', justifyContent: 'flex-start' }}>
            Kontrol Paneli
          </Button>
          <Button variant="outline" size="sm" style={{ color: '#ffffff', borderColor: 'rgba(255,255,255,0.2)', justifyContent: 'flex-start' }}>
            Menü Yönetimi
          </Button>
          <Button variant="outline" size="sm" style={{ color: '#ffffff', borderColor: 'rgba(255,255,255,0.2)', justifyContent: 'flex-start' }}>
            Şube & Masalar
          </Button>
        </nav>
      </aside>

      <main className="main-content" role="main">
        <header className="header" role="banner">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              <h1 className="title">Yönetim Paneli</h1>
              <p className="subtitle">Organizasyon Genel Bakışı</p>
            </div>
            <Badge variant="primary">Restoran Admini</Badge>
          </div>
        </header>

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
            style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '20px' }}
          >
            <Card padding="md">
              <h3 style={{ fontSize: '1rem', color: 'var(--ro-color-text-muted)' }}>Günlük Sipariş</h3>
              <p style={{ fontSize: '1.75rem', fontWeight: 700, marginTop: '8px' }}>0</p>
            </Card>
            <Card padding="md">
              <h3 style={{ fontSize: '1rem', color: 'var(--ro-color-text-muted)' }}>Aktif Masalar</h3>
              <p style={{ fontSize: '1.75rem', fontWeight: 700, marginTop: '8px' }}>0</p>
            </Card>
          </section>
        )}
      </main>
    </div>
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
