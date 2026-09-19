import React from 'react';
import { Card, Button, Badge } from '@restaurant-order/ui';

export const App: React.FC = () => {
  return (
    <div className="container">
      <header className="header">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
          <div>
            <h1 className="title">Restoran Sipariş</h1>
            <p className="subtitle">Masa 04 • Giriş Salonu</p>
          </div>
          <Badge variant="success">Açık Oturum</Badge>
        </div>
      </header>

      <main className="content">
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
      </main>
    </div>
  );
};

export default App;
