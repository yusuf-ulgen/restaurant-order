import React, { useState, useEffect } from 'react';
import type { CustomerSessionDto, RuntimeMenuReadModel } from '@restaurant-order/contracts';
import { Card, Button } from '@restaurant-order/ui';
import { DigitalMenuView } from '../components/menu/DigitalMenuView';
import { CartProvider, useCart } from '../cart/CartContext';
import { CartSheet } from '../components/cart/CartSheet';
import { ServiceRequestSheet } from '../components/service/ServiceRequestSheet';
import { runtimeMenuApi } from '../api/runtimeMenuApi';

export interface ActiveSessionViewProps {
  session: CustomerSessionDto;
  onLeaveSession: () => void;
  initialMenuData?: RuntimeMenuReadModel | null;
  initialTab?: 'menu' | 'table';
}

const ActiveSessionContent: React.FC<ActiveSessionViewProps> = ({
  session,
  onLeaveSession,
  initialMenuData = null,
  initialTab = 'table',
}) => {
  const [currentSession, setCurrentSession] = useState<CustomerSessionDto>(session);
  const [activeTab, setActiveTab] = useState<'menu' | 'table'>(initialTab);
  const [isServiceSheetOpen, setIsServiceSheetOpen] = useState(false);
  const [runtimeMenuData, setRuntimeMenuData] = useState<RuntimeMenuReadModel | null>(initialMenuData);

  const cart = useCart();

  useEffect(() => {
    if (cart.isCartOpen && session.branchId) {
      runtimeMenuApi
        .getRuntimeMenu(session.branchId)
        .then((freshMenu) => setRuntimeMenuData(freshMenu))
        .catch(() => {});
    }
  }, [cart.isCartOpen, session.branchId]);

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        minHeight: '100%',
        paddingBottom: '80px',
        boxSizing: 'border-box',
      }}
      data-testid="customer-active-session"
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
          onAddToCart={(line) => cart.addLine(line)}
          onMenuLoaded={setRuntimeMenuData}
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
          <div data-testid="session-welcome-view">
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
                variant="secondary"
                size="md"
                className="customer-action-btn-secondary"
                onClick={() => setIsServiceSheetOpen(true)}
                data-testid="request-service-btn"
                style={{ minHeight: '44px' }}
              >
                Garson Çağır
              </Button>
            </div>
          </Card>
          </div>
        </div>
      )}

      {/* Cart Bottom Sheet (CUST-04) */}
      <CartSheet
        isOpen={cart.isCartOpen}
        onClose={cart.closeCart}
        runtimeMenu={runtimeMenuData || initialMenuData}
        tableInfo={{
          tableNumber: session.tableNumber,
          tableName: session.tableName,
        }}
        onNavigateToMenu={() => setActiveTab('menu')}
      />

      {/* Service Request Bottom Sheet (CUST-06) */}
      <ServiceRequestSheet
        isOpen={isServiceSheetOpen}
        onClose={() => setIsServiceSheetOpen(false)}
        session={currentSession}
        onSessionUpdated={setCurrentSession}
      />

      {/* Mobile Bottom Navigation Bar with 44px min touch targets */}
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
            minWidth: '60px',
            gap: '2px',
          }}
        >
          <span style={{ fontSize: '18px' }} aria-hidden="true">🍽️</span>
          <span>Menü</span>
        </button>

        <button
          type="button"
          onClick={cart.openCart}
          aria-label={`Sepetim, ${cart.totalItemsCount} ürün`}
          data-testid="bottom-nav-cart-btn"
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            justifyContent: 'center',
            background: 'transparent',
            border: 'none',
            color: 'var(--ro-color-text-primary)',
            fontWeight: 'var(--ro-font-weight-regular)',
            fontSize: 'var(--ro-font-size-xs)',
            cursor: 'pointer',
            minHeight: '44px',
            minWidth: '60px',
            gap: '2px',
            position: 'relative',
          }}
        >
          <span style={{ fontSize: '18px' }} aria-hidden="true">🛒</span>
          <span>Sepet</span>
          {cart.totalItemsCount > 0 && (
            <span
              style={{
                position: 'absolute',
                top: '2px',
                right: '12px',
                backgroundColor: 'var(--ro-color-text-primary)',
                color: 'var(--ro-color-surface)',
                borderRadius: 'var(--ro-radius-full)',
                fontSize: '10px',
                fontWeight: 'var(--ro-font-weight-bold)',
                padding: '1px 5px',
                minWidth: '16px',
                textAlign: 'center',
              }}
              data-testid="cart-badge-count"
            >
              {cart.totalItemsCount}
            </span>
          )}
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
            minWidth: '60px',
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
            minWidth: '60px',
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

export const ActiveSessionView: React.FC<ActiveSessionViewProps> = (props) => {
  return (
    <CartProvider sessionId={props.session.sessionId} currency="TRY">
      <ActiveSessionContent {...props} />
    </CartProvider>
  );
};
