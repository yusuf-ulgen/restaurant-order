import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';
import { resolve } from 'node:path';
import { describe, expect, it, vi } from 'vitest';

describe.each(['customer-web', 'operations-web'])('%s push worker', app => {
  const load = (visibility: string) => {
    const listeners = new Map<string, (event: unknown) => void>();
    const client = { visibilityState: visibility, postMessage: vi.fn(), url: 'https://app.example/', focus: vi.fn() };
    const self = { addEventListener: (name: string, fn: (event: unknown) => void) => listeners.set(name, fn),
      skipWaiting: vi.fn(), location: { origin: 'https://app.example' },
      clients: { claim: vi.fn(), matchAll: async () => [client], openWindow: vi.fn() },
      registration: { showNotification: vi.fn() } };
    runInNewContext(readFileSync(resolve(process.cwd(), `../../apps/${app}/public/sw.js`), 'utf8'), { self, URL, Date });
    return { self, listeners, client };
  };

  it('claims the scope on activation and suppresses a second alert for a visible app', async () => {
    const { self, listeners, client } = load('visible');
    const waitUntil = vi.fn();
    listeners.get('activate')!({ waitUntil });
    expect(self.clients.claim).toHaveBeenCalled();
    listeners.get('push')!({ data: { json: () => ({ data: { event_id: 'opaque-event' } }) }, waitUntil });
    await waitUntil.mock.calls.at(-1)?.[0];
    expect(client.postMessage).toHaveBeenCalledWith({ type: 'PUSH_RECEIVED', event_id: 'opaque-event' });
    expect(self.registration.showNotification).not.toHaveBeenCalled();
  });

  it('minimizes hidden notifications and ignores untrusted navigation targets', async () => {
    const { self, listeners } = load('hidden');
    const waitUntil = vi.fn();
    listeners.get('push')!({ data: { json: () => ({ data: { event_id: 'opaque-event', title: 'private content' } }) }, waitUntil });
    await waitUntil.mock.calls.at(-1)?.[0];
    expect(self.registration.showNotification).toHaveBeenCalledWith('Restoran bildirimi', expect.objectContaining({
      tag: 'opaque-event', renotify: false, data: { event_id: 'opaque-event', target_route: '/' },
    }));
    expect(JSON.stringify(self.registration.showNotification.mock.calls)).not.toContain('private content');
  });
});
