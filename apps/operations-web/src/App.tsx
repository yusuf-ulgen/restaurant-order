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
import { TerminalContextDto, UserPrincipalDto } from '@restaurant-order/contracts';
import { TerminalProvider, useTerminal } from './auth/TerminalContext';
import { TerminalActivationView } from './auth/TerminalActivationView';
import { PinPadView } from './auth/PinPadView';

export interface OperationsAppProps {
  hasActiveTables?: boolean;
  initialError?: boolean;
  initialTerminal?: TerminalContextDto | null;
  initialStaff?: UserPrincipalDto | null;
}

export const OperationsContent: React.FC<{ hasActiveTables?: boolean }> = ({
  hasActiveTables = true,
}) => {
  const [isCallsModalOpen, setIsCallsModalOpen] = useState(false);
  const { terminal, staffUser, logoutStaff } = useTerminal();

  const handleOpenCallsModal = () => setIsCallsModalOpen(true);
  const handleCloseCallsModal = () => setIsCallsModalOpen(false);

  return (
    <AppShell
      variant="operations"
      header={{
        title: <h1 className="title">Garson & Operasyon</h1>,
        subtitle: (
          <p className="subtitle">
            Şube: {terminal?.terminalName ? `${terminal.terminalName} • Aktif Vardiya` : 'Kadıköy • Aktif Vardiya'}
          </p>
        ),
        actions: (
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <Badge variant="primary">{staffUser?.role || 'Garson'}</Badge>
            {terminal && <Badge variant="neutral">{terminal.terminalName}</Badge>}
            <Button
              variant="outline"
              size="sm"
              onClick={logoutStaff}
              title="Vardiya oturumunu kilitle ve PIN ekranına dön"
            >
              Kilitle
            </Button>
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
    </AppShell>
  );
};

export const AppContent: React.FC<OperationsAppProps> = ({
  hasActiveTables = true,
  initialError = false,
}) => {
  const { terminal, isEnrolled, staffUser } = useTerminal();

  if (initialError) {
    throw new Error('Operasyon verileri yüklenemedi.');
  }

  // State 1: UNENROLLED -> Only TerminalActivationView in DOM
  if (!terminal || !isEnrolled) {
    return <TerminalActivationView />;
  }

  // State 2: LOCKED -> Only PinPadView in DOM
  if (!staffUser) {
    return <PinPadView terminal={terminal} />;
  }

  // State 3: AUTHENTICATED -> OperationsContent with AppShell
  return <OperationsContent hasActiveTables={hasActiveTables} />;
};

export const App: React.FC<OperationsAppProps> = ({
  initialTerminal,
  initialStaff,
  ...props
}) => {
  return (
    <ErrorBoundary>
      <TerminalProvider initialTerminal={initialTerminal} initialStaff={initialStaff}>
        <AppContent {...props} />
      </TerminalProvider>
    </ErrorBoundary>
  );
};

export default App;
