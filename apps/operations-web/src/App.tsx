import React from 'react';
import { Card, Button, Badge, ErrorBoundary, EmptyState } from '@restaurant-order/ui';

export interface OperationsAppProps {
  hasActiveTables?: boolean;
  initialError?: boolean;
}

export const OperationsContent: React.FC<OperationsAppProps> = ({
  hasActiveTables = true,
  initialError = false,
}) => {
  if (initialError) {
    throw new Error('Operasyon verileri yüklenemedi.');
  }

  return (
    <div className="container">
      <header className="header" role="banner">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h1 className="title">Garson & Operasyon</h1>
            <p className="subtitle">Şube: Kadıköy • Aktif Vardiya</p>
          </div>
          <Badge variant="primary">Garson Modu</Badge>
        </div>
      </header>

      <main className="content" role="main">
        {!hasActiveTables ? (
          <Card padding="md">
            <EmptyState
              title="Aktif Masa Bulunmuyor"
              description="Şu anda atanmış veya işlem bekleyen aktif bir masa bulunmamaktadır."
              actionLabel="Masaları Yenile"
              onAction={() => {}}
            />
          </Card>
        ) : (
          <Card padding="md">
            <h2 style={{ fontSize: '1.125rem', marginBottom: '8px' }}>Masa Yönetimi</h2>
            <p style={{ color: 'var(--ro-color-text-muted)', fontSize: '0.875rem', lineHeight: '1.5' }}>
              Masa durumlarını görüntüleyebilir, yeni sipariş alabilir ve servis çağrılarına yanıt verebilirsiniz.
            </p>
            <div style={{ marginTop: '16px', display: 'flex', gap: '8px' }}>
              <Button variant="secondary" size="md" style={{ flex: 1 }}>
                Masa Planı
              </Button>
              <Button variant="outline" size="md">
                Çağrılar (0)
              </Button>
            </div>
          </Card>
        )}
      </main>
    </div>
  );
};

export const App: React.FC<OperationsAppProps> = (props) => {
  return (
    <ErrorBoundary>
      <OperationsContent {...props} />
    </ErrorBoundary>
  );
};

export default App;
