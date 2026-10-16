import React, { useState } from 'react';
import type { CustomerSessionDto, RuntimeMenuReadModel } from '@restaurant-order/contracts';
import { Card, Button, BottomSheet } from '@restaurant-order/ui';
import { DigitalMenuView } from '../components/menu/DigitalMenuView';

export interface ActiveSessionViewProps {
  session: CustomerSessionDto;
  onLeaveSession?: () => void;
  initialTab?: 'menu' | 'table';
  initialMenuData?: RuntimeMenuReadModel | null;
}

export const ActiveSessionView: React.FC<ActiveSessionViewProps> = ({
  session,
  onLeaveSession,
  initialTab = 'table',
  initialMenuData = null,
}) => {
  const [activeTab, setActiveTab] = useState<'menu' | 'table'>(initialTab);
  const [isServiceSheetOpen, setIsServiceSheetOpen] = useState(false);
  const handleCloseServiceSheet = () => setIsServiceSheetOpen(false);

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        minHeight: '100%',
        width: '100%',
        paddingBottom: '72px', // space for bottom navigation
      }}
      data-testid="active-session-view"
    >
      {activeTab === 'menu' ? (
        <DigitalMenuView
          branchId={session.branchId}
          tableInfo={{
            tableNumber: session.tableNumber,
            tableName: session.tableName,
          }}
          onSessionExpired={onLeaveSession}
          onCallWaiter={() => setIsServiceSheetOpen(true)}
          initialMenuData={initialMenuData}
        />
      ) : (
        <div
          style={{
            maxWidth: '520px',
            margin: '0 auto',
            padding: 'var(--ro-space-4)',
            width: '100%',
            boxSizing: 'border-box',
          }}
          data-testid="table-overview-tab"
        >
          <Card padding="md">
            <h2 className="customer-card-title">Hoş Geldiniz</h2>
            <p className="customer-card-text">
              <strong>Masa {session.tableNumber} ({session.tableName})</strong> için oturumunuz aktif.
              Menüyü inceleyebilir, ürünleri filtreleyebilir ve servis talebinde bulunabilirsiniz.
            </p>
            <div
              className="customer-actions"
              style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-3)' }}
            >
              <Button
                variant="primary"
                size="md"
                className="customer-action-btn-primary"
                onClick={() => setActiveTab('menu')}
                data-testid="goto-menu-btn"
                style={{ minHeight: '44px' }}
              >
                Menüyü İncele
              </Button>
              <Button
                variant="outline"
                size="md"
                onClick={() => setIsServiceSheetOpen(true)}
                style={{ minHeight: '44px' }}
              >
                Garson Çağır
              </Button>
            </div>
          </Card>
        </div>
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
          Servis çağrı iletimi Faz 7.4 (Servis Talepleri) kapsamında etkinleşecektir.
        </p>
        <div className="service-options-list" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--ro-space-2)' }}>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Su ve peçete talebi Faz 7.4'te etkinleşecektir"
          >
            <span>Masaya Su / Peçete Talebi (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Hesap talebi Faz 7.4'te etkinleşecektir"
          >
            <span>Hesap İstiyorum (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
          <button
            type="button"
            className="service-option-item"
            disabled
            aria-disabled="true"
            title="Garson görüşme talebi Faz 7.4'te etkinleşecektir"
          >
            <span>Garson ile Görüşme Talebi (Yakında)</span>
            <span aria-hidden="true">→</span>
          </button>
        </div>
      </BottomSheet>

      {/* Mobile Bottom Navigation Bar */}
      <nav
        aria-label="Müşteri Alt Gezinme Çubuğu"
        style={{
          position: 'fixed',
          bottom: 0,
          left: 0,
          right: 0,
          height: '60px',
          backgroundColor: 'var(--ro-color-surface)',
          borderTop: '1px solid var(--ro-color-border)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-around',
          zIndex: 100,
          boxShadow: 'var(--ro-shadow-md)',
          padding: '0 var(--ro-space-2)',
        }}
        data-testid="customer-bottom-nav"
      >
        <button
          type="button"
          onClick={() => setActiveTab('menu')}
          aria-label="Dijital Menü"
          aria-current={activeTab === 'menu' ? 'page' : undefined}
          data-testid="bottom-nav-menu-btn"
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'transparent',
            border: 'none',
            color: activeTab === 'menu' ? 'var(--ro-color-text-primary)' : 'var(--ro-color-text-muted)',
            fontWeight: activeTab === 'menu' ? 'var(--ro-font-weight-bold)' : 'var(--ro-font-weight-regular)',
            fontSize: 'var(--ro-font-size-xs)',
            cursor: 'pointer',
            minHeight: '44px',
            minWidth: '64px',
            gap: '2px',
          }}
        >
          <span style={{ fontSize: '18px' }} aria-hidden="true">🍽️</span>
          <span>Menü</span>
        </button>

        <button
          type="button"
          onClick={() => setIsServiceSheetOpen(true)}
          aria-label="Garson servisi"
          data-testid="bottom-nav-waiter-btn"
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'transparent',
            border: 'none',
            color: 'var(--ro-color-text-muted)',
            fontWeight: 'var(--ro-font-weight-regular)',
            fontSize: 'var(--ro-font-size-xs)',
            cursor: 'pointer',
            minHeight: '44px',
            minWidth: '64px',
            gap: '2px',
          }}
        >
          <span style={{ fontSize: '18px' }} aria-hidden="true">🛎️</span>
          <span>Garson</span>
        </button>

        <button
          type="button"
          onClick={() => setActiveTab('table')}
          aria-label="Masa Bilgisi"
          aria-current={activeTab === 'table' ? 'page' : undefined}
          data-testid="bottom-nav-table-btn"
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'transparent',
            border: 'none',
            color: activeTab === 'table' ? 'var(--ro-color-text-primary)' : 'var(--ro-color-text-muted)',
            fontWeight: activeTab === 'table' ? 'var(--ro-font-weight-bold)' : 'var(--ro-font-weight-regular)',
            fontSize: 'var(--ro-font-size-xs)',
            cursor: 'pointer',
            minHeight: '44px',
            minWidth: '64px',
            gap: '2px',
          }}
        >
          <span style={{ fontSize: '18px' }} aria-hidden="true">ℹ️</span>
          <span>Masa</span>
        </button>
      </nav>
    </div>
  );
};
