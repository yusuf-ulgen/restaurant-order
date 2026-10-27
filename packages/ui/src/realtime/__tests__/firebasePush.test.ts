import { beforeEach, describe, expect, it, vi } from 'vitest';
import { configureBrowserPush, registerFirebasePush, unregisterFirebasePush } from '../firebasePush.js';

const sdk = vi.hoisted(() => ({
  registered: undefined as undefined | ((fid: string) => void),
  register: vi.fn(), unregister: vi.fn(), supported: vi.fn().mockResolvedValue(true),
  serviceWorker: vi.fn().mockResolvedValue({ scope: '/' }), stop: vi.fn(),
}));
vi.mock('firebase/app', () => ({ initializeApp: vi.fn().mockReturnValue({}), getApps: () => [] }));
vi.mock('firebase/messaging', () => ({
  getMessaging: () => ({}), isSupported: sdk.supported,
  register: sdk.register, unregister: sdk.unregister,
  onRegistered: (_: unknown, cb: (fid: string) => void) => { sdk.registered = cb; return sdk.stop; },
}));

describe('FCM browser subscription', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sdk.supported.mockResolvedValue(true);
    sdk.register.mockImplementation(async () => { queueMicrotask(() => sdk.registered?.('real_sdk_test_fid')); });
    Object.defineProperty(navigator, 'serviceWorker', { configurable: true, value: { register: sdk.serviceWorker, ready: Promise.resolve({}) } });
    configureBrowserPush({ apiKey: 'synthetic-public-config', appId: 'test-app', projectId: 'test-project', messagingSenderId: 'test-sender', vapidKey: 'synthetic-public-vapid' });
  });

  it('registers the actual service worker and waits for the SDK target and API persistence', async () => {
    const save = vi.fn().mockResolvedValue(true);
    await expect(registerFirebasePush(save)).resolves.toBe('real_sdk_test_fid');
    expect(sdk.serviceWorker).toHaveBeenCalledWith('/sw.js', { scope: '/' });
    expect(sdk.register).toHaveBeenCalledWith(expect.anything(), expect.objectContaining({ serviceWorkerRegistration: { scope: '/' } }));
    expect(save).toHaveBeenCalledWith('real_sdk_test_fid');
    sdk.registered?.('refreshed_sdk_fid');
    await Promise.resolve();
    expect(save).toHaveBeenCalledWith('refreshed_sdk_fid');
  });

  it('does not report registration success when the backend rejects the target', async () => {
    await expect(registerFirebasePush(async () => false)).rejects.toThrow();
  });

  it('fails closed for unsupported browsers and missing configuration', async () => {
    sdk.supported.mockResolvedValue(false);
    await expect(registerFirebasePush(async () => true)).rejects.toThrow();
    expect(sdk.register).not.toHaveBeenCalled();
    configureBrowserPush({ vapidKey: '' });
    await expect(registerFirebasePush(async () => true)).rejects.toThrow();
  });

  it('reports provider errors and unregisters the provider subscription on logout', async () => {
    sdk.register.mockRejectedValue(new Error('provider unavailable'));
    await expect(registerFirebasePush(async () => true)).rejects.toThrow('provider unavailable');
    await unregisterFirebasePush();
    expect(sdk.stop).toHaveBeenCalled();
    expect(sdk.unregister).toHaveBeenCalled();
  });
});
