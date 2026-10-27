import React, { useState, useRef, useCallback } from 'react';
import {
  Card,
  Button,
  Badge,
  ErrorBoundary,
  EmptyState,
  AppShell,
  RealtimeStatusBadge,
  useAudioChime,
  PushNotificationBanner,
  registerPushDevice,
} from '@restaurant-order/ui';
import { TerminalContextDto, UserPrincipalDto } from '@restaurant-order/contracts';
import { TerminalProvider, useTerminal } from './auth/TerminalContext';
import { TerminalActivationView } from './auth/TerminalActivationView';
import { PinPadView } from './auth/PinPadView';
import { OperationsFloorContainer } from './floor/OperationsFloorContainer';
import { useOperationsRealtimeCoordinator } from './floor/useOperationsRealtimeCoordinator';
import { OperationsCallsDrawer } from './floor/OperationsCallsDrawer';

export interface OperationsAppProps {
  hasActiveTables?: boolean;
  initialError?: boolean;
  initialTerminal?: TerminalContextDto | null;
  initialStaff?: UserPrincipalDto | null;
}

export const OperationsContent: React.FC<{ hasActiveTables?: boolean }> = ({
  hasActiveTables = true,
}) => {
  const [isCallsDrawerOpen, setIsCallsDrawerOpen] = useState(false);
  const [focusedTableId, setFocusedTableId] = useState<string | null>(null);

  const { terminal, staffUser, logoutStaff } = useTerminal();
  const { isMuted, toggleMute } = useAudioChime();

  const floorRefreshFnRef = useRef<(() => Promise<void>) | null>(null);

  const handleRegisterRefreshFloor = useCallback((refreshFn: () => Promise<void>) => {
    floorRefreshFnRef.current = refreshFn;
  }, []);

  const handleRefreshFloorFromRealtime = useCallback(async () => {
    if (floorRefreshFnRef.current) {
      await floorRefreshFnRef.current();
    }
  }, []);

  const {
    realtimeStatus,
    activeRequests,
    readyItems,
    notifications,
    isOperating,
    actionError,
    setActionError,
    handleAcknowledge,
    handleComplete,
    handleDismissNotification,
    handleClearAllNotifications,
  } = useOperationsRealtimeCoordinator({
    terminal,
    staffUser,
    onRefreshFloor: handleRefreshFloorFromRealtime,
  });

  const handleOpenCallsModal = () => setIsCallsDrawerOpen(true);
  const handleCloseCallsModal = () => setIsCallsDrawerOpen(false);

  const handleGoToTable = (tableId: string) => {
    setFocusedTableId(tableId);
  };

  const handleClearFocusedTable = () => {
    setFocusedTableId(null);
  };

  const pendingCallsCount = activeRequests.length;
  const readyItemsCount = readyItems.length;

  return (
    <AppShell
      variant="operations"
      header={{
        title: <h1 className="title">Garson & Operasyon</h1>,
        subtitle: (
          <p className="subtitle">
            {terminal?.terminalName ? `Şube: ${terminal.terminalName} • Aktif Vardiya` : 'Aktif Vardiya'}
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
              onClick={handleOpenCallsModal}
              title="Aktif servis çağrıları ve hazır ürünleri görüntüle"
              data-testid="operations-calls-drawer-toggle"
            >
              Çağrılar ({pendingCallsCount})
              {readyItemsCount > 0 && ` • 🍽️ ${readyItemsCount}`}
            </Button>
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
          {
            id: 'calls',
            label: 'Çağrılar',
            onClick: handleOpenCallsModal,
          },
          { id: 'orders', label: 'Siparişler (Yakında)', disabled: true },
        ],
      }}
    >
      <PushNotificationBanner
        onPermissionGranted={async (installationId) => {
          return registerPushDevice('', undefined, installationId);
        }}
      />
      {!hasActiveTables ? (
        <Card padding="md">
          <EmptyState
            title="Aktif Masa Bulunmuyor"
            description="Şu anda atanmış veya işlem bekleyen aktif bir masa bulunmamaktadır."
          />
        </Card>
      ) : (
        <OperationsFloorContainer
          branchId={terminal?.branchId || ''}
          terminalName={terminal?.terminalName}
          onOpenCallsModal={handleOpenCallsModal}
          liveEventCount={pendingCallsCount}
          readyItemsCount={readyItemsCount}
          focusedTableId={focusedTableId}
          onClearFocusedTable={handleClearFocusedTable}
          onRegisterRefreshFloor={handleRegisterRefreshFloor}
        />
      )}

      {/* WAIT-07: Bildirim Çekmecesi ve Servis Çağrıları */}
      <OperationsCallsDrawer
        isOpen={isCallsDrawerOpen}
        onClose={handleCloseCallsModal}
        activeRequests={activeRequests}
        readyItems={readyItems}
        notifications={notifications}
        onAcknowledgeRequest={handleAcknowledge}
        onCompleteRequest={handleComplete}
        onDismissNotification={handleDismissNotification}
        onClearAllNotifications={handleClearAllNotifications}
        onGoToTable={handleGoToTable}
        isOperating={isOperating}
        actionError={actionError}
        onClearActionError={() => setActionError(null)}
        realtimeStatus={realtimeStatus}
      />
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
