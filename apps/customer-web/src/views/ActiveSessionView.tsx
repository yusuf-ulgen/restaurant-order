import React, { useState, useEffect, useRef, useCallback } from 'react';
import type { CustomerSessionDto, RuntimeMenuReadModel, CustomerOrderResponseDto } from '@restaurant-order/contracts';
import { Card, Button, RealtimeStatusBadge, useRealtimeEvents } from '@restaurant-order/ui';
import { DigitalMenuView } from '../components/menu/DigitalMenuView';
import { CartProvider, useCart } from '../cart/CartContext';
import { CartSheet } from '../components/cart/CartSheet';
import { ServiceRequestSheet } from '../components/service/ServiceRequestSheet';
import { OrderConfirmationSheet } from '../components/cart/OrderConfirmationSheet';
import { OrderSuccessSheet } from '../components/cart/OrderSuccessSheet';
import { OrderHistorySheet } from '../components/cart/OrderHistorySheet';
import { ActiveOrderStatusCard } from '../components/cart/ActiveOrderStatusCard';
import { runtimeMenuApi } from '../api/runtimeMenuApi';
import { customerOrderApi } from '../api/customerOrderApi';

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
  const [isConfirmationOpen, setIsConfirmationOpen] = useState(false);
  const [isSuccessOpen, setIsSuccessOpen] = useState(false);
  const [isHistoryOpen, setIsHistoryOpen] = useState(false);
  const [lastPlacedOrder, setLastPlacedOrder] = useState<CustomerOrderResponseDto | null>(null);
  const [activeOrders, setActiveOrders] = useState<CustomerOrderResponseDto[]>([]);
  const [orderRefreshKey, setOrderRefreshKey] = useState(0);
  const [runtimeMenuData, setRuntimeMenuData] = useState<RuntimeMenuReadModel | null>(initialMenuData);
  const [isMenuLoading, setIsMenuLoading] = useState(false);
  const [menuError, setMenuError] = useState<string | null>(null);
  const menuRequestIdRef = useRef(0);

  const cart = useCart();

  const refreshOrders = useCallback(async () => {
    try {
      const data = await customerOrderApi.getOrders();
      setActiveOrders([...data].sort((a, b) => b.roundNumber - a.roundNumber));
      setOrderRefreshKey((prev) => prev + 1);
    } catch {
      // Ignore or log error
    }
  }, []);

  useEffect(() => {
    if (session?.sessionId) {
      refreshOrders();
    }
  }, [session?.sessionId, refreshOrders]);

  const { status: realtimeStatus } = useRealtimeEvents({
    enabled: Boolean(session?.sessionId),
    tenantId: session.tenantId,
    branchId: session.branchId,
    sessionId: session.sessionId,
    onEvent: (event) => {
      if (
        event.event_type === 'menu.item_availability_changed' ||
        event.event_type === 'catalog.policy_invalidated'
      ) {
        refreshRuntimeMenu();
      }
      if (
        event.event_type === 'order.item_status_changed' ||
        event.event_type === 'order.created'
      ) {
        refreshOrders();
      }
    },
    onSessionClosed: () => {
      onLeaveSession();
    },
    onResyncRequired: () => {
      refreshRuntimeMenu();
      refreshOrders();
    },
  });

  const refreshRuntimeMenu = useCallback(async () => {
    if (!session.branchId) return;
    const reqId = ++menuRequestIdRef.current;
    setIsMenuLoading(true);
    setMenuError(null);
    try {
      const freshMenu = await runtimeMenuApi.getRuntimeMenu(session.branchId);
      if (reqId === menuRequestIdRef.current) {
        setRuntimeMenuData(freshMenu);
        setIsMenuLoading(false);
      }
    } catch {
      if (reqId === menuRequestIdRef.current) {
        setIsMenuLoading(false);
        setMenuError('Güncel menü bilgisine ulaşılamadı. Lütfen tekrar deneyiniz.');
      }
    }
  }, [session.branchId]);

  useEffect(() => {
    if (cart.isCartOpen && session.branchId) {
      refreshRuntimeMenu();
    }
  }, [cart.isCartOpen, session.branchId, refreshRuntimeMenu]);

  const handleResolvePriceConflict = useCallback(() => {
    cart.requireReconfirmation();
    setIsConfirmationOpen(false);
    cart.openCart();
    refreshRuntimeMenu();
  }, [cart, refreshRuntimeMenu]);

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
          onAddToCart={(line, policyFingerprint) => cart.addLine(line, policyFingerprint)}
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
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 'var(--ro-space-2)' }}>
                <h2 className="customer-card-title" style={{ margin: 0 }}>Hoş Geldiniz</h2>
                <RealtimeStatusBadge status={realtimeStatus} data-testid="customer-realtime-badge" />
              </div>
              <p className="customer-card-text">
                <strong>Masa {session.tableNumber} ({session.tableName})</strong> için oturumunuz aktif.
                Menüyü inceleyebilir, ürünleri sepetinize ekleyip sipariş verebilir ve servis talebinde bulunabilirsiniz.
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
                onClick={() => setIsHistoryOpen(true)}
                data-testid="view-orders-tab-btn"
                style={{ minHeight: '44px' }}
              >
                Masa Siparişlerim
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

          <ActiveOrderStatusCard orders={activeOrders} />
        </div>
      )}

      {/* Cart Bottom Sheet (CUST-04) */}
      <CartSheet
        isOpen={cart.isCartOpen}
        onClose={cart.closeCart}
        runtimeMenu={runtimeMenuData || initialMenuData}
        isLoadingMenu={isMenuLoading}
        menuError={menuError}
        onRetryMenu={refreshRuntimeMenu}
        tableInfo={{
          tableNumber: session.tableNumber,
          tableName: session.tableName,
        }}
        onNavigateToMenu={() => setActiveTab('menu')}
        onProceedToConfirmation={() => {
          cart.closeCart();
          setIsConfirmationOpen(true);
        }}
      />

      {/* Order Confirmation Bottom Sheet (Phase 8.4) */}
      <OrderConfirmationSheet
        isOpen={isConfirmationOpen}
        onClose={() => setIsConfirmationOpen(false)}
        tableInfo={{
          tableNumber: session.tableNumber,
          tableName: session.tableName,
        }}
        runtimeMenu={runtimeMenuData || initialMenuData}
        onOrderSuccess={(order) => {
          setLastPlacedOrder(order);
          setIsSuccessOpen(true);
          refreshOrders();
        }}
        onSessionExpired={onLeaveSession}
        onResolvePriceConflict={handleResolvePriceConflict}
      />

      {/* Order Success Bottom Sheet (Phase 8.4) */}
      <OrderSuccessSheet
        isOpen={isSuccessOpen}
        onClose={() => setIsSuccessOpen(false)}
        order={lastPlacedOrder}
        onViewOrders={() => setIsHistoryOpen(true)}
        onBrowseMenu={() => setActiveTab('menu')}
      />

      {/* Order History Bottom Sheet (Phase 8.4) */}
      <OrderHistorySheet
        isOpen={isHistoryOpen}
        onClose={() => setIsHistoryOpen(false)}
        tableInfo={{
          tableNumber: session.tableNumber,
          tableName: session.tableName,
        }}
        onSessionExpired={onLeaveSession}
        refreshKey={orderRefreshKey}
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
