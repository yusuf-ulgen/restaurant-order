import { act, fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { RealtimeEventsOptions } from '@restaurant-order/ui';
import type { EventEnvelope } from '@restaurant-order/contracts';
import { App } from './App';

const realtime = vi.hoisted(() => ({
  options: undefined as RealtimeEventsOptions | undefined,
  register: undefined as undefined | ((id: string) => Promise<boolean>),
  play: vi.fn(), registerDevice: vi.fn(), mark: vi.fn(),
}));
vi.mock('@restaurant-order/ui', async () => ({
  ...await vi.importActual('@restaurant-order/ui'),
  useRealtimeEvents: (options: RealtimeEventsOptions) => { realtime.options = options; return { status: 'connected' }; },
  useAudioChime: () => ({ isMuted: false, toggleMute: vi.fn(), playChime: realtime.play }),
  markEventReceivedInForeground: realtime.mark,
  registerPushDevice: realtime.registerDevice,
  PushNotificationBanner: (props: { onPermissionGranted: (id: string) => Promise<boolean> }) => {
    realtime.register = props.onPermissionGranted; return null;
  },
}));

const terminal = { terminalId: 'synthetic-terminal', tenantId: 'tenant', branchId: 'branch', terminalName: 'Synthetic', deviceIdentifier: 'synthetic', isActive: true };
const staff = { userId: 'synthetic-user', email: 'synthetic@example.invalid', role: 'Waiter', tenantId: 'tenant', branchId: 'branch', securityVersion: 1 };
const event = (id: number, type: string) => ({ event_id: `event-${id}`, event_type: type, data: {} }) as EventEnvelope<unknown>;

describe('operational realtime delivery', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => [],
      })
    );
  });

  it('renders authorized live events, silences catch-up, and clears stale feed on full resync', () => {
    render(<App initialTerminal={terminal} initialStaff={staff} />);
    act(() => {
      ['order.created', 'table.service_requested', 'order.item_status_changed', 'menu.item_availability_changed'].forEach((type, id) => {
        realtime.options?.onEvent?.(event(id, type), { isCatchUp: id === 3, isLive: id !== 3 });
      });
    });
    expect(realtime.play).toHaveBeenCalledTimes(3);
    expect(realtime.mark).toHaveBeenCalledTimes(4);
    fireEvent.click(screen.getByRole('button', { name: 'Çağrılar' }));
    expect(screen.getByText('Yeni Sipariş Alındı')).toBeDefined();
    expect(screen.getByText('Masa Servis / Garson Çağrısı')).toBeDefined();
    expect(screen.getByText('Ürün Durumu Güncellendi')).toBeDefined();
    expect(screen.getByText('Menü Stok Durumu Değişti')).toBeDefined();
    act(() => { realtime.options?.onResyncRequired?.(); });
    expect(screen.getByText('Bekleyen Çağrı Yok')).toBeDefined();
  });

  it('propagates push persistence failure to the banner instead of reporting success', async () => {
    realtime.registerDevice.mockResolvedValue(false);
    render(<App initialTerminal={terminal} initialStaff={staff} />);
    await expect(realtime.register?.('sdk-fid')).resolves.toBe(false);
    expect(realtime.registerDevice).toHaveBeenCalledWith('', undefined, 'sdk-fid');
  });
});
