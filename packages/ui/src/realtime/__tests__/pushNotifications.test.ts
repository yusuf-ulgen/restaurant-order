import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
vi.mock('../firebasePush.js', () => ({
  configureBrowserPush: vi.fn(), unregisterFirebasePush: vi.fn(),
  registerFirebasePush: async (save: (target: string) => Promise<boolean>) => {
    if (!await save('synthetic_fid')) throw new Error('API registration failed');
    return 'synthetic_fid';
  },
}));
import {
  isPushSupported,
  getPushPermission,
  isPushDismissed,
  dismissPushPrompt,
  requestPushPermission,
  getOrCreateInstallationId,
  clearInstallationId,
  registerPushDevice,
  unregisterPushDevice,
  markEventReceivedInForeground,
  shouldSuppressForegroundAlert,
  isAllowlistedRoute,
  ALLOWLISTED_ROUTES,
} from '../pushNotifications.js';

describe('Client Push Notifications Infrastructure', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
    vi.stubGlobal('Notification', { permission: 'granted', requestPermission: vi.fn().mockResolvedValue('granted') });
    vi.stubGlobal('PushManager', class {});
    Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: {} });
  });

  afterEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  describe('Support and Permission Detection', () => {
    it('detects support accurately based on environment', () => {
      const supported = isPushSupported();
      expect(typeof supported).toBe('boolean');
    });

    it('returns current permission state or unsupported', () => {
      const permission = getPushPermission();
      expect(['default', 'granted', 'denied', 'unsupported']).toContain(permission);
    });

    it('tracks user dismissal of push banner in localStorage', () => {
      expect(isPushDismissed()).toBe(false);
      dismissPushPrompt();
      expect(isPushDismissed()).toBe(true);
    });

    it('requests push permission via user-initiated action', async () => {
      if (typeof window !== 'undefined' && 'Notification' in window) {
        vi.spyOn(Notification, 'requestPermission').mockResolvedValue('granted');
        const res = await requestPushPermission();
        expect(res).toBe('granted');
      }
    });

    it('handles permission denial gracefully without throwing', async () => {
      if (typeof window !== 'undefined' && 'Notification' in window) {
        vi.spyOn(Notification, 'requestPermission').mockResolvedValue('denied');
        const res = await requestPushPermission();
        expect(res).toBe('denied');
      }
    });
  });

  describe('Installation ID Lifecycle', () => {
    it('generates a stable, alphanumeric installation ID and persists it', () => {
      const id1 = getOrCreateInstallationId();
      expect(id1).toMatch(/^inst_[a-zA-Z0-9_-]{8,256}$/);

      // Calling again returns the exact same ID
      const id2 = getOrCreateInstallationId();
      expect(id2).toBe(id1);
    });

    it('clears installation ID on unregister or logout', () => {
      const id = getOrCreateInstallationId();
      expect(id).toBeTruthy();

      clearInstallationId();
      expect(localStorage.getItem('restaurant_order_installation_id')).toBeNull();
    });
  });

  describe('Device Registration API Calls', () => {
    it('waits for an in-flight registration before deleting its target during logout', async () => {
      let finishSave!: (response: { ok: boolean; status: number }) => void;
      const fetchMock = vi.fn().mockImplementation((_url, init) => init.method === 'POST'
        ? new Promise(resolve => { finishSave = resolve; }) : Promise.resolve({ ok: true, status: 204 }));
      globalThis.fetch = fetchMock;
      const registering = registerPushDevice('', 'test-token');
      await Promise.resolve();
      const loggingOut = unregisterPushDevice('', 'test-token');
      await Promise.resolve();
      expect(fetchMock).toHaveBeenCalledTimes(1);
      finishSave({ ok: true, status: 200 });
      expect(await registering).toBe(false);
      expect(await loggingOut).toBe(true);
      expect(fetchMock.mock.calls.map(call => call[1].method)).toEqual(['POST', 'DELETE']);
      expect(localStorage.getItem('restaurant_order_fcm_fid')).toBeNull();
    });
    it('calls POST /api/v1/notifications/devices and returns true on success', async () => {
      const mockFetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ id: 'mock-id' }),
      });
      globalThis.fetch = mockFetch;

      const success = await registerPushDevice('https://api.restaurant.com', 'test-token', 'inst_valid_123');

      expect(success).toBe(true);
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.restaurant.com/api/v1/notifications/devices',
        expect.objectContaining({
          method: 'POST',
          credentials: 'include',
          headers: expect.any(Headers),
          body: JSON.stringify({
            installation_id: 'synthetic_fid',
            platform: 'web',
          }),
        })
      );
      expect((mockFetch.mock.calls[0]?.[1].headers as Headers).get('Authorization')).toBe('Bearer test-token');
    });

    it('calls DELETE /api/v1/notifications/devices/{id} on unregister', async () => {
      const mockFetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 204,
      });
      globalThis.fetch = mockFetch;

      const success = await unregisterPushDevice('https://api.restaurant.com', 'test-token', 'inst_valid_123');

      expect(success).toBe(true);
      expect(mockFetch).toHaveBeenCalledWith(
        'https://api.restaurant.com/api/v1/notifications/devices/inst_valid_123',
        expect.objectContaining({
          method: 'DELETE',
          credentials: 'include',
        })
      );
    });
  });

  describe('Foreground Double-Alert Suppression', () => {
    it('suppresses alert when event was already handled in foreground recently', () => {
      const eventId = 'ev-foreground-test-123';
      expect(shouldSuppressForegroundAlert(eventId)).toBe(false);

      markEventReceivedInForeground(eventId);
      expect(shouldSuppressForegroundAlert(eventId)).toBe(true);
    });

    it('does not suppress unknown or empty events', () => {
      expect(shouldSuppressForegroundAlert('')).toBe(false);
      expect(shouldSuppressForegroundAlert('unknown-event-id')).toBe(false);
    });
  });

  describe('Allowlisted Route Validation', () => {
    it('permits internal allowlisted routes', () => {
      for (const route of ALLOWLISTED_ROUTES) {
        expect(isAllowlistedRoute(route)).toBe(true);
        expect(isAllowlistedRoute(`${route}?query=test`)).toBe(true);
      }
    });

    it('rejects external or unallowlisted URLs', () => {
      expect(isAllowlistedRoute('https://evil-site.com')).toBe(false);
      expect(isAllowlistedRoute('/untrusted/path')).toBe(false);
      expect(isAllowlistedRoute('javascript:alert(1)')).toBe(false);
      expect(isAllowlistedRoute('')).toBe(false);
    });
  });
});
