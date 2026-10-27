import { act, renderHook } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import type { CustomerSessionDto, RuntimeMenuReadModel } from '@restaurant-order/contracts';
import type { RealtimeEventsOptions } from '@restaurant-order/ui';
import { useCustomerSessionRealtime } from '../realtime/useCustomerSessionRealtime';

const realtime = vi.hoisted(() => ({ options: undefined as RealtimeEventsOptions | undefined, menu: vi.fn(), orders: vi.fn() }));
vi.mock('@restaurant-order/ui', () => ({
  useRealtimeEvents: (options: RealtimeEventsOptions) => { realtime.options = options; return { status: 'connected' }; },
  markEventReceivedInForeground: vi.fn(), unregisterPushDevice: vi.fn(),
}));
vi.mock('../api/runtimeMenuApi', () => ({ runtimeMenuApi: { getRuntimeMenu: realtime.menu } }));
vi.mock('../api/customerOrderApi', () => ({ customerOrderApi: { getOrders: realtime.orders } }));

it('does not apply a late full snapshot from a previous tenant/session', async () => {
  let finish!: (menu: RuntimeMenuReadModel) => void;
  realtime.menu.mockReturnValue(new Promise(resolve => { finish = resolve; }));
  realtime.orders.mockResolvedValue([]);
  const onSnapshot = vi.fn();
  const session = (id: string) => ({ tenantId: id, branchId: id, sessionId: id }) as CustomerSessionDto;
  const { rerender } = renderHook(({ id }) => useCustomerSessionRealtime({ session: session(id), onSnapshot,
    onLeaveSession: vi.fn(), refreshMenu: vi.fn(), refreshOrders: vi.fn() }), { initialProps: { id: 'tenant-A' } });
  let resync: void | Promise<void> = undefined;
  act(() => { resync = realtime.options?.onResyncRequired?.(); });
  rerender({ id: 'tenant-B' });
  await act(async () => { finish({ branchId: 'tenant-A' } as RuntimeMenuReadModel); await resync; });
  expect(onSnapshot).not.toHaveBeenCalled();
});
