import React from 'react';
import {
  Card,
  Button,
  Badge,
  ErrorBoundary,
  EmptyState,
  AppShell,
} from '@restaurant-order/ui';

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
    <AppShell
      variant="customer"
      header={{
        title: (
          <h1 className="title" style={{ margin: 0, fontSize: 'var(--ro-font-size-lg)' }}>
            Restoran Sipariş
          </h1>
        ),
        subtitle: (
          <p className="subtitle" style={{ margin: 0 }}>
            Masa 04 • Giriş Salonu
          </p>
        ),
        actions: <Badge variant="success">Açık Oturum</Badge>,
      }}
      footer={{
        copyright: '© 2026 Restaurant Order',
        businessText: 'Giriş Salonu',
      }}
    >
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
          <p
            style={{
              color: 'var(--ro-color-text-muted)',
              fontSize: '0.875rem',
              lineHeight: '1.5',
            }}
          >
            Masadaki QR kodu tarayarak menüyü inceleyebilir, doğrudan sipariş verebilir ve
            hesap durumunuzu takip edebilirsiniz.
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
    </AppShell>
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
