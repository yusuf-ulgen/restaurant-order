import React from 'react';
import { Card, Button, Badge, ErrorBoundary, EmptyState } from '@restaurant-order/ui';

export interface CustomerAppProps {
  hasActiveSession?: boolean;
  initialError?: boolean;
}

export const CustomerContent: React.FC<CustomerAppProps> = ({
  hasActiveSession = true,
  initialError = false,
}) => {
  if (initialError) {
    throw new Error('Aktif oturum yüklenemedi.');
  }

  return (
    <div className="container">
      <header className="header" role="banner">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h1 className="title">Restoran Sipariş</h1>
            <p className="subtitle">Masa 04 • Giriş Salonu</p>
          </div>
          <Badge variant="success">Açık Oturum</Badge>
        </div>
      </header>

      <main className="content" role="main">
        {!hasActiveSession ? (
          <Card padding="md">
            <EmptyState
              title="Aktif Sipariş Bulunmuyor"
              description="Masada henüz aktif bir siparişiniz bulunmamaktadır. Menüyü inceleyerek sipariş verebilirsiniz."
              actionLabel="Menüyü Aç"
              onAction={() => {}}
            />
          </Card>
        ) : (
          <Card padding="md">
            <h2 style={{ fontSize: '1.125rem', marginBottom: '8px' }}>Hoş Geldiniz</h2>
            <p style={{ color: 'var(--ro-color-text-muted)', fontSize: '0.875rem', lineHeight: '1.5' }}>
              Masadaki QR kodu tarayarak menüyü inceleyebilir, doğrudan sipariş verebilir ve hesap durumunuzu takip edebilirsiniz.
            </p>
            <div style={{ marginTop: '16px', display: 'flex', gap: '8px' }}>
              <Button variant="primary" size="md" style={{ flex: 1 }}>
                Menüyü İncele
              </Button>
              <Button variant="outline" size="md">
                Garson Çağır
              </Button>
            </div>
          </Card>
        )}
      </main>
    </div>
  );
};

export const App: React.FC<CustomerAppProps> = (props) => {
  return (
    <ErrorBoundary>
      <CustomerContent {...props} />
    </ErrorBoundary>
  );
};

export default App;
