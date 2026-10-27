import { useEffect, useRef } from 'react';
import type { CustomerSessionDto, CustomerOrderResponseDto, RuntimeMenuReadModel } from '@restaurant-order/contracts';
import { markEventReceivedInForeground, unregisterPushDevice, useRealtimeEvents } from '@restaurant-order/ui';
import { customerOrderApi } from '../api/customerOrderApi';
import { runtimeMenuApi } from '../api/runtimeMenuApi';

interface CustomerRealtimeOptions {
  session: CustomerSessionDto;
  onLeaveSession: () => void;
  refreshMenu: () => Promise<void>;
  refreshOrders: () => Promise<void>;
  onSnapshot: (menu: RuntimeMenuReadModel, orders: CustomerOrderResponseDto[]) => void;
}

export function useCustomerSessionRealtime({ session, onLeaveSession, refreshMenu, refreshOrders, onSnapshot }: CustomerRealtimeOptions) {
  const scopeKey = `${session.tenantId}:${session.branchId}:${session.sessionId}`;
  const scope = useRef(scopeKey);
  scope.current = scopeKey;
  const menuRefreshTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    scope.current = scopeKey;
    return () => {
      scope.current = '';
      if (menuRefreshTimer.current) clearTimeout(menuRefreshTimer.current);
    };
  }, [scopeKey]);

  return useRealtimeEvents({
    enabled: Boolean(session.sessionId), tenantId: session.tenantId, branchId: session.branchId, sessionId: session.sessionId,
    onEvent: event => {
      markEventReceivedInForeground(event.event_id);
      if (event.event_type === 'menu.item_availability_changed' || event.event_type === 'catalog.policy_invalidated') {
        // Catch-up sayfasındaki çok sayıda katalog invalidation'ı tek okumada birleştirilir.
        if (menuRefreshTimer.current) clearTimeout(menuRefreshTimer.current);
        menuRefreshTimer.current = setTimeout(() => { void refreshMenu(); }, 50);
      }
      if (event.event_type === 'order.item_status_changed' || event.event_type === 'order.created') void refreshOrders();
    },
    onSessionClosed: () => { void unregisterPushDevice(''); onLeaveSession(); },
    onResyncRequired: async () => {
      const [menu, orders] = await Promise.all([runtimeMenuApi.getRuntimeMenu(session.branchId), customerOrderApi.getOrders()]);
      if (scope.current === scopeKey) onSnapshot(menu, orders);
    },
  });
}
