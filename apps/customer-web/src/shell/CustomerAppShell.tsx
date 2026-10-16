import React, { useState, useCallback } from 'react';
import {
  Card,
  Badge,
  EmptyState,
  AppShell,
} from '@restaurant-order/ui';
import type { CustomerSessionDto, QrExchangeResponse } from '@restaurant-order/contracts';
import { useCustomerSession } from '../session/CustomerSessionContext';
import { ActiveSessionView } from '../views/ActiveSessionView';
import { ExpiredSessionView } from '../views/ExpiredSessionView';
import { SessionLoadingView } from '../views/SessionLoadingView';
import { SessionErrorView } from '../views/SessionErrorView';
import { TableWelcomeView } from '../TableWelcomeView';
import { UnauthenticatedQrView } from '../views/UnauthenticatedQrView';

export interface CustomerAppShellProps {
  hasActiveSession?: boolean;
  initialError?: boolean;
  token?: string;
  isQrLanding?: boolean;
  children?: React.ReactNode;
}

export const CustomerAppShell: React.FC<CustomerAppShellProps> = ({
  hasActiveSession = true,
  initialError = false,
  token,
  isQrLanding,
  children,
}) => {
  const {
    status,
    session,
    error,
    setSession,
    clearSession,
    resetToUnauthenticated,
    retry,
  } = useCustomerSession();

  const [welcomeSession, setWelcomeSession] = useState<QrExchangeResponse | null>(null);

  if (initialError) {
    throw new Error('Aktif oturum yüklenemedi.');
  }

  // Detect QR landing route from URL or props
  const hasQrInUrl = useCallback(() => {
    if (typeof window === 'undefined') return false;
    return Boolean(
      window.location.pathname.includes('/q/') ||
      window.location.hash.includes('/q/') ||
      new URLSearchParams(window.location.search).has('token')
    );
  }, []);

  const isQrRoute = Boolean(
    isQrLanding ||
    token ||
    (hasQrInUrl() && !session && !welcomeSession)
  );

  const handleSessionReady = (qrSession: QrExchangeResponse) => {
    setWelcomeSession(qrSession);
    const sessionDto: CustomerSessionDto = {
      sessionId: qrSession.sessionId,
      tenantId: qrSession.tenantId,
      branchId: qrSession.branchId,
      tableId: qrSession.sessionId,
      tableNumber: qrSession.tableNumber,
      tableName: qrSession.tableName,
      sessionStatus: qrSession.sessionStatus,
      concurrencyToken: qrSession.sessionId,
      guestCount: 1,
      openedAtUtc: new Date().toISOString(),
    };
    setSession(sessionDto);
  };

  const currentDisplaySession = session || (welcomeSession ? {
    tableNumber: welcomeSession.tableNumber,
    tableName: welcomeSession.tableName,
  } : null);

  const headerSubtitle = currentDisplaySession
    ? `Masa ${currentDisplaySession.tableNumber} • ${currentDisplaySession.tableName}`
    : 'Masa 04 • Giriş Salonu';

  const isSessionActive = status === 'active' || Boolean(welcomeSession);

  return (
    <AppShell
      variant="customer"
      header={{
        title: <h1 className="title">Restoran Sipariş</h1>,
        subtitle: <p className="subtitle">{headerSubtitle}</p>,
        actions: (
          <Badge variant={isSessionActive ? 'success' : 'neutral'}>
            {isSessionActive ? 'Oturum Açık' : 'Açık Oturum'}
          </Badge>
        ),
      }}
      footer={{
        copyright: '© 2026 Restaurant Order',
        businessText: currentDisplaySession?.tableName || 'Giriş Salonu',
      }}
    >
      {children}
      {isQrRoute && !session ? (
        <TableWelcomeView
          initialToken={token}
          onSessionReady={handleSessionReady}
        />
      ) : status === 'bootstrapping' ? (
        <SessionLoadingView message="Mevcut oturum doğrulanıyor..." />
      ) : status === 'loading' ? (
        <SessionLoadingView message="Yükleniyor..." />
      ) : status === 'error' ? (
        <SessionErrorView
          message={error?.message}
          retryAfterSeconds={error?.retryAfterSeconds}
          onRetry={retry}
        />
      ) : status === 'expired' ? (
        <ExpiredSessionView onScanNewQr={resetToUnauthenticated} />
      ) : status === 'unauthenticated' ? (
        !hasActiveSession ? (
          <Card padding="md">
            <EmptyState
              title="Aktif Sipariş Bulunmuyor"
              description="Masada henüz aktif bir siparişiniz bulunmamaktadır. Menü ve sipariş özellikleri Faz 7 ve Faz 8 kapsamında etkinleşecektir."
            />
          </Card>
        ) : (
          <UnauthenticatedQrView
            onTokenSubmit={() => {
              // Trigger QR scan / token resolution
            }}
          />
        )
      ) : status === 'active' && session ? (
        <ActiveSessionView session={session} onLeaveSession={clearSession} />
      ) : (
        <UnauthenticatedQrView />
      )}
    </AppShell>
  );
};
