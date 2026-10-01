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
import { TerminalProvider, useTerminal } from './auth/TerminalContext';
import { TerminalActivationModal } from './auth/TerminalActivationModal';
import { PinAuthModal } from './auth/PinAuthModal';

export interface OperationsAppProps {
  hasActiveTables?: boolean;
  initialError?: boolean;
}

export const OperationsContent: React.FC<OperationsAppProps> = ({
  hasActiveTables = true,
  initialError = false,
}) => {
  const [isCallsModalOpen, setIsCallsModalOpen] = useState(false);
  const [isActivationModalOpen, setIsActivationModalOpen] = useState(false);
  const [isPinModalOpen, setIsPinModalOpen] = useState(false);

  const handleOpenCallsModal = () => setIsCallsModalOpen(true);
  const handleCloseCallsModal = () => setIsCallsModalOpen(false);

  let isEnrolled = false;
  let staffUser = null;
  try {
    const terminalCtx = useTerminal();
    isEnrolled = terminalCtx.isEnrolled;
    staffUser = terminalCtx.staffUser;
  } catch {
    // Graceful fallback outside TerminalProvider
  }

  if (initialError) {
    throw new Error('Operasyon verileri yüklenemedi.');
  }

  return (
    <AppShell
      variant="operations"
      header={{
        title: <h1 className="title">Garson & Operasyon</h1>,
        subtitle: <p className="subtitle">Şube: Kadıköy • Aktif Vardiya</p>,
        actions: (
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <Badge variant={isEnrolled ? 'primary' : 'warning'}>
              {staffUser ? staffUser.role : isEnrolled ? 'Terminal Aktif' : 'Garson Modu'}
            </Badge>
            {!isEnrolled ? (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setIsActivationModalOpen(true)}
              >
                Terminal Kaydet
              </Button>
            ) : (
              <Button
                variant="outline"
                size="sm"
                onClick={() => setIsPinModalOpen(true)}
              >
                {staffUser ? 'PIN Değiştir' : 'PIN Girişi'}
              </Button>
            )}
          </div>
        ),
      }}
      mobileNav={{
        items: [
          { id: 'tables', label: 'Masalar', isActive: true },
          { id: 'calls', label: 'Çağrılar', onClick: handleOpenCallsModal },
          { id: 'orders', label: 'Siparişler (Yakında)', disabled: true },
        ],
      }}
    >
      {!hasActiveTables ? (
        <Card padding="md">
          <EmptyState
            title="Aktif Masa Bulunmuyor"
            description="Şu anda atanmış veya işlem bekleyen aktif bir masa bulunmamaktadır. Masa atama özellikleri Phase 6'da etkinleşecektir."
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
                disabled
                title="Masa planı görünümü Phase 6 kapsamında etkinleşecektir"
              >
                Masa Planı (Yakında)
              </Button>
              <Button
                variant="outline"
                size="md"
                onClick={handleOpenCallsModal}
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
        onClose={handleCloseCallsModal}
        title="Aktif Çağrılar"
        description="Masalardan gelen servis ve hesap çağrıları"
        size="sm"
      >
        <EmptyState
          title="Bekleyen Çağrı Yok"
          description="Şu anda masalardan iletilen açık bir garson veya hesap çağrısı bulunmamaktadır."
        />
      </Modal>

      {/* Terminal Activation Modal */}
      <TerminalActivationModal
        isOpen={isActivationModalOpen}
        onClose={() => setIsActivationModalOpen(false)}
      />

      {/* PIN Auth Modal */}
      <PinAuthModal
        isOpen={isPinModalOpen}
        onClose={() => setIsPinModalOpen(false)}
      />
    </AppShell>
  );
};

export const App: React.FC<OperationsAppProps> = (props) => {
  return (
    <ErrorBoundary>
      <TerminalProvider>
        <OperationsContent {...props} />
      </TerminalProvider>
    </ErrorBoundary>
  );
};

export default App;
