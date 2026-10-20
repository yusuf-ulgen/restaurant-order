import React, { useState } from 'react';
import {
  Card,
  Button,
  Badge,
  ErrorBoundary,
  EmptyState,
  AppShell,
  Modal,
  RealtimeStatusBadge,
  useRealtimeEvents,
  useAudioChime,
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
  const [liveNotifications, setLiveNotifications] = useState<
    Array<{ id: string; title: string; time: string; eventType: string }>
  >([]);
  const { terminal, staffUser, logoutStaff } = useTerminal();
  const { isMuted, toggleMute, playChime } = useAudioChime();

  const { status: realtimeStatus } = useRealtimeEvents({
    enabled: Boolean(staffUser),
    onEvent: (event) => {
      playChime();

      let title = 'Yeni Operasyon Olayı';
      if (event.event_type === 'order.created') {
        title = 'Yeni Sipariş Alındı';
      } else if (event.event_type === 'table.service_requested') {
        title = 'Masa Servis / Garson Çağrısı';
      } else if (event.event_type === 'order.item_status_changed') {
        title = 'Ürün Durumu Güncellendi';
      } else if (event.event_type === 'menu.item_availability_changed') {
        title = 'Menü Stok Durumu Değişti';
      }

      setLiveNotifications((prev) => [
        {
          id: event.event_id,
          title,
          time: new Date().toLocaleTimeString('tr-TR', { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
          eventType: event.event_type,
        },
        ...prev.slice(0, 19),
      ]);
    },
  });

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
            <RealtimeStatusBadge status={realtimeStatus} />
            <Badge variant="primary">{staffUser?.role || 'Garson'}</Badge>
            {terminal && <Badge variant="neutral">{terminal.terminalName}</Badge>}
            <Button
              variant="outline"
              size="sm"
              onClick={toggleMute}
              title={isMuted ? 'Sesi Aç' : 'Sesi Kapat'}
              data-testid="chime-mute-toggle"
            >
              {isMuted ? '🔇' : '🔔'}
            </Button>
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
              <span className="operations-stat-value">{liveNotifications.length}</span>
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
        {liveNotifications.length === 0 ? (
          <EmptyState
            title="Bekleyen Çağrı Yok"
            description="Şu anda masalardan iletilen açık bir garson veya hesap çağrısı bulunmamaktadır."
          />
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', maxHeight: '350px', overflowY: 'auto' }}>
            {liveNotifications.map((notif) => (
              <div
                key={notif.id}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  alignItems: 'center',
                  padding: '10px 12px',
                  borderRadius: '6px',
                  backgroundColor: 'var(--ro-color-surface-hover, #f3f4f6)',
                  border: '1px solid var(--ro-color-border, #e5e7eb)',
                }}
              >
                <div>
                  <div style={{ fontWeight: 600, fontSize: '14px' }}>{notif.title}</div>
                  <div style={{ fontSize: '12px', color: '#6b7280' }}>{notif.time}</div>
                </div>
                <Badge variant="primary">{notif.eventType}</Badge>
              </div>
            ))}
          </div>
        )}
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
