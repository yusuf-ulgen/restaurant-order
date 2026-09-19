import React from 'react';
import { Card, Button, Badge } from '@restaurant-order/ui';

export const App: React.FC = () => {
  return (
    <div className="layout">
      <aside className="sidebar">
        <h2 style={{ fontSize: '1.25rem', fontWeight: 700, marginBottom: '24px' }}>Restoran Yönetim</h2>
        <nav style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
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

      <main className="main-content">
        <header className="header">
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <div>
              <h1 className="title">Yönetim Paneli</h1>
              <p className="subtitle">Organizasyon Genel Bakışı</p>
            </div>
            <Badge variant="primary">Restoran Admini</Badge>
          </div>
        </header>

        <section style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '20px' }}>
          <Card padding="md">
            <h3 style={{ fontSize: '1rem', color: 'var(--ro-color-text-muted)' }}>Günlük Sipariş</h3>
            <p style={{ fontSize: '1.75rem', fontWeight: 700, marginTop: '8px' }}>0</p>
          </Card>
          <Card padding="md">
            <h3 style={{ fontSize: '1rem', color: 'var(--ro-color-text-muted)' }}>Aktif Masalar</h3>
            <p style={{ fontSize: '1.75rem', fontWeight: 700, marginTop: '8px' }}>0</p>
          </Card>
        </section>
      </main>
    </div>
  );
};

export default App;
