import React, { useState } from 'react';
import {
  Card,
  Button,
  Badge,
  ErrorBoundary,
  EmptyState,
  AppShell,
  BottomSheet,
} from '@restaurant-order/ui';

export interface CustomerAppProps {
  hasActiveSession?: boolean;
  initialError?: boolean;
}

export const CustomerContent: React.FC<CustomerAppProps> = ({
  hasActiveSession = true,
  initialError = false,
}) => {
  const [isServiceSheetOpen, setIsServiceSheetOpen] = useState(false);
  const handleCloseServiceSheet = () => setIsServiceSheetOpen(false);

  if (initialError) {
    throw new Error('Aktif oturum yüklenemedi.');
  }

  return (
    <AppShell
      variant="customer"
      header={{
        title: <h1 className="title">Restoran Sipariş</h1>,
        subtitle: <p className="subtitle">Masa 04 • Giriş Salonu</p>,
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
            description="Masada henüz aktif bir siparişiniz bulunmamaktadır. Menü ve sipariş özellikleri Phase 5'te etkinleşecektir."
          />
        </Card>
      ) : (
        <Card padding="md">
          <h2 className="customer-card-title">Hoş Geldiniz</h2>
          <p className="customer-card-text">
            Masadaki QR kodu tarayarak menüyü inceleyebilir, doğrudan sipariş verebilir ve
            hesap durumunuzu takip edebilirsiniz.
          </p>
          <div className="customer-actions">
            <Button
              variant="primary"
              size="md"
              className="customer-action-btn-primary"
              disabled
              title="Menü inceleme ve sipariş verme Phase 5 kapsamında etkinleşecektir"
            >
              Menüyü İncele (Yakında)
            </Button>
            <Button
              variant="outline"
              size="md"
              onClick={() => setIsServiceSheetOpen(true)}
            >
              Garson Çağır
            </Button>
          </div>
        </Card>
      )}

      {/* Service Request Bottom Sheet */}
      <BottomSheet
        isOpen={isServiceSheetOpen}
        onClose={handleCloseServiceSheet}
        title="Garson Çağır"
        description="Masanız için servis veya destek talebi iletin."
      >
        <p
          style={{
            fontSize: 'var(--ro-font-size-sm)',
            color: 'var(--ro-color-text-muted)',
            marginTop: 0,
            marginBottom: 'var(--ro-space-3)',
          }}
        >
          Servis çağrı iletimi Phase 7 (Realtime & Garson) kapsamında etkinleşecektir.
        </p>
        <div className="service-options-list">
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Su ve peçete talebi Phase 7'de etkinleşecektir"
          >
            <span>Masaya Su / Peçete Talebi (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Hesap talebi Phase 7'de etkinleşecektir"
          >
            <span>Hesap İstiyorum (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Garson görüşme talebi Phase 7'de etkinleşecektir"
          >
            <span>Garson ile Görüşme Talebi (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
        </div>
      </BottomSheet>
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
