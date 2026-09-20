import React, { useState } from 'react';
import {
  Card,
  Button,
  Badge,
  ErrorBoundary,
  EmptyState,
  AppShell,
  Modal,
} from '@restaurant-order/ui';

export interface OperationsAppProps {
  hasActiveTables?: boolean;
  initialError?: boolean;
}

export const OperationsContent: React.FC<OperationsAppProps> = ({
  hasActiveTables = true,
  initialError = false,
}) => {
  const [isCallsModalOpen, setIsCallsModalOpen] = useState(false);

  if (initialError) {
    throw new Error('Operasyon verileri yüklenemedi.');
  }

  return (
    <AppShell
      variant="operations"
      header={{
        title: <h1 className="title">Garson & Operasyon</h1>,
        subtitle: <p className="subtitle">Şube: Kadıköy • Aktif Vardiya</p>,
        actions: <Badge variant="primary">Garson Modu</Badge>,
      }}
      mobileNav={{
        items: [
          { id: 'tables', label: 'Masalar', isActive: true },
          { id: 'calls', label: 'Çağrılar', onClick: () => setIsCallsModalOpen(true) },
          { id: 'orders', label: 'Siparişler' },
        ],
      }}
    >
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
        <>
          <Card padding="md">
            <h2 className="operations-card-title">Masa Yönetimi</h2>
            <p className="operations-card-text">
              Masa durumlarını görüntüleyebilir, yeni sipariş alabilir ve servis çağrılarına yanıt
              verebilirsiniz.
            </p>
            <div className="operations-actions">
              <Button
                variant="secondary"
                size="md"
                className="operations-action-btn-primary"
              >
                Masa Planı
              </Button>
              <Button
                variant="outline"
                size="md"
                onClick={() => setIsCallsModalOpen(true)}
              >
                Çağrılar (0)
              </Button>
            </div>
          </Card>

          <div className="operations-quick-stats">
            <div className="operations-stat-item">
              <span className="operations-stat-label">Bekleyen Çağrı</span>
              <span className="operations-stat-value">0</span>
            </div>
            <div className="operations-stat-item">
              <span className="operations-stat-label">Açık Hesaplar</span>
              <span className="operations-stat-value">0</span>
            </div>
            <div className="operations-stat-item">
              <span className="operations-stat-label">Hazır Siparişler</span>
              <span className="operations-stat-value">0</span>
            </div>
          </div>
        </>
      )}

      {/* Calls Modal */}
      <Modal
        isOpen={isCallsModalOpen}
        onClose={() => setIsCallsModalOpen(false)}
        title="Aktif Çağrılar"
        description="Masalardan gelen servis ve hesap çağrıları"
        size="sm"
      >
        <EmptyState
          title="Bekleyen Çağrı Yok"
          description="Şu anda masalardan iletilen açık bir garson veya hesap çağrısı bulunmamaktadır."
        />
      </Modal>
    </AppShell>
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
