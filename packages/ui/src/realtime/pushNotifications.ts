import { fetchWithCsrf } from '@restaurant-order/contracts';
import { registerFirebasePush, unregisterFirebasePush } from './firebasePush.js';
export { configureBrowserPush } from './firebasePush.js';
/**
 * Client Push Notification management and service-worker integration.
 * Adheres to ADR-0011, lockscreen data minimization, and safe allowlisted navigation.
 */

const INSTALLATION_STORAGE_KEY = 'restaurant_order_installation_id';
const DISMISSED_STORAGE_KEY = 'restaurant_order_push_dismissed';
const REGISTERED_AT_KEY = 'restaurant_order_push_registered_at';

const foregroundEventCache = new Map<string, number>();
let registrationEpoch = 0;
let registrationPending: Promise<boolean> | null = null;
const pendingSaves = new Set<Promise<boolean>>();
const knownTargets = new Set<string>();
const FOREGROUND_CACHE_TTL_MS = 60000;

export const ALLOWLISTED_ROUTES = [
  '/',
  '/table',
  '/orders',
  '/menu',
  '/operations',
  '/kds',
  '/dashboard',
] as const;

export function isPushSupported(): boolean {
  if (typeof window === 'undefined') return false;
  return (
    'serviceWorker' in navigator &&
    'PushManager' in window &&
    'Notification' in window
  );
}

export function getPushPermission(): NotificationPermission | 'unsupported' {
  if (!isPushSupported()) return 'unsupported';
  return Notification.permission;
}

export function isPushDismissed(): boolean {
  try {
    return localStorage.getItem(DISMISSED_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

export function dismissPushPrompt(): void {
  try {
    localStorage.setItem(DISMISSED_STORAGE_KEY, 'true');
  } catch {
    // Ignore localStorage error
  }
}

export async function requestPushPermission(): Promise<NotificationPermission | 'unsupported'> {
  if (!isPushSupported()) return 'unsupported';
  try {
    const permission = await Notification.requestPermission();
    return permission;
  } catch {
    return 'denied';
  }
}

export function getOrCreateInstallationId(): string {
  try {
    const existing = localStorage.getItem(INSTALLATION_STORAGE_KEY);
    if (existing && /^[a-zA-Z0-9_-]{8,256}$/.test(existing)) {
      return existing;
    }

    const generated = `inst_${crypto.randomUUID().replace(/-/g, '')}`;
    localStorage.setItem(INSTALLATION_STORAGE_KEY, generated);
    return generated;
  } catch {
    return `inst_${Date.now()}_${Math.random().toString(36).substring(2, 10)}`;
  }
}

export function clearInstallationId(): void {
  try {
    localStorage.removeItem(INSTALLATION_STORAGE_KEY);
    localStorage.removeItem(REGISTERED_AT_KEY);
  } catch {
    // Ignore error
  }
}

export function registerPushDevice(
  apiBaseUrl: string,
  authToken?: string,
  installationId?: string
): Promise<boolean> {
  if (registrationPending) return registrationPending;
  registrationPending = registerPushDeviceCore(apiBaseUrl, authToken, installationId)
    .finally(() => { registrationPending = null; });
  return registrationPending;
}

async function registerPushDeviceCore(apiBaseUrl: string, authToken?: string, installationId?: string): Promise<boolean> {
  // Üçüncü parametre yalnızca eski çağıranların uyumluluğu içindir; rastgele kimlik gönderilmez.
  void installationId;
  if (getPushPermission() !== 'granted') return false;
  const epoch = registrationEpoch;
  try {
    let previous = localStorage.getItem('restaurant_order_fcm_fid');
    await registerFirebasePush(async fid => {
      if (epoch !== registrationEpoch) return false;
      knownTargets.add(fid);
      if (previous && previous !== fid) await unregisterTarget(apiBaseUrl, authToken, previous);
      if (epoch !== registrationEpoch) return false;
      const saving = savePushTarget(apiBaseUrl, authToken, fid);
      pendingSaves.add(saving);
      let saved: boolean;
      try { saved = await saving; } finally { pendingSaves.delete(saving); }
      if (saved && epoch === registrationEpoch) { previous = fid; localStorage.setItem('restaurant_order_fcm_fid', fid); }
      return saved;
    });
    return epoch === registrationEpoch;
  } catch { return false; }
}

async function savePushTarget(apiBaseUrl: string, authToken: string | undefined, effectiveId: string): Promise<boolean> {

  try {
    const base = apiBaseUrl.replace(/\/+$/, '');
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
    };
    if (authToken) {
      headers['Authorization'] = `Bearer ${authToken}`;
    }

    const response = await fetchWithCsrf(`${base}/api/v1/notifications/devices`, {
      method: 'POST',
      credentials: 'include',
      headers,
      body: JSON.stringify({
        installation_id: effectiveId,
        platform: 'web',
      }),
    });

    if (response.ok) {
      try {
        localStorage.setItem(REGISTERED_AT_KEY, new Date().toISOString());
      } catch {
        // Ignore localStorage error
      }
      return true;
    }

    return false;
  } catch {
    return false;
  }
}

export async function unregisterPushDevice(
  apiBaseUrl: string,
  authToken?: string,
  installationId?: string
): Promise<boolean> {
  let effectiveId = installationId;
  try { effectiveId ||= localStorage.getItem('restaurant_order_fcm_fid') || undefined; } catch { /* Bellekteki hedefler yine iptal edilir. */ }
  registrationEpoch++;
  await Promise.allSettled([...pendingSaves]);
  try { await unregisterFirebasePush(); } catch { /* Sunucudaki kaydı yine de iptal et. */ }
  if (effectiveId) knownTargets.add(effectiveId);
  let removed = true;
  for (const target of [...knownTargets]) {
    if (await unregisterTarget(apiBaseUrl, authToken, target)) knownTargets.delete(target);
    else removed = false;
  }
  if (removed) { try { localStorage.removeItem('restaurant_order_fcm_fid'); } catch { /* Sağlayıcı ve API kaydı kaldırıldı. */ } }
  return removed;
}

async function unregisterTarget(apiBaseUrl: string, authToken: string | undefined, effectiveId: string): Promise<boolean> {

  try {
    const base = apiBaseUrl.replace(/\/+$/, '');
    const headers: Record<string, string> = {};
    if (authToken) {
      headers['Authorization'] = `Bearer ${authToken}`;
    }

    const response = await fetchWithCsrf(`${base}/api/v1/notifications/devices/${encodeURIComponent(effectiveId)}`, {
      method: 'DELETE',
      credentials: 'include',
      headers,
    });

    clearInstallationId();
    return response.status === 204 || response.ok;
  } catch {
    clearInstallationId();
    return false;
  }
}

export function markEventReceivedInForeground(eventId: string): void {
  if (!eventId) return;
  const now = Date.now();
  foregroundEventCache.set(eventId, now);

  // Evict stale items
  if (foregroundEventCache.size > 100) {
    for (const [key, ts] of foregroundEventCache.entries()) {
      if (now - ts > FOREGROUND_CACHE_TTL_MS) {
        foregroundEventCache.delete(key);
      }
    }
  }
}

export function shouldSuppressForegroundAlert(eventId: string): boolean {
  if (!eventId) return false;
  const ts = foregroundEventCache.get(eventId);
  if (!ts) return false;
  return Date.now() - ts < FOREGROUND_CACHE_TTL_MS;
}

export function isAllowlistedRoute(path: string): boolean {
  if (!path || typeof path !== 'string') return false;
  const normalized = path.split('?')[0]?.split('#')[0] || '';
  return ALLOWLISTED_ROUTES.some((allowed) => normalized === allowed || normalized.startsWith(`${allowed}/`));
}
