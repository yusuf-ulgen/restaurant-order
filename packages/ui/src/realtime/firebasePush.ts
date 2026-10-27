import { initializeApp, getApps, type FirebaseOptions } from 'firebase/app';
import { getMessaging, isSupported, onRegistered, register, unregister, type Messaging } from 'firebase/messaging';

export interface BrowserPushConfig extends FirebaseOptions { vapidKey: string }
let configuration: BrowserPushConfig | undefined;
let messaging: Messaging | undefined;
let stopRegistrationListener: (() => void) | undefined;

export function configureBrowserPush(config: BrowserPushConfig): void { configuration = config; }

// FID ancak Messaging.register başarılı olduğunda gönderim hedefidir. Yerel rastgele
// kurulum kimliği veya getToken sonucuyla değiştirilmez (Firebase JS 13.1).
export async function registerFirebasePush(onTargetChanged: (fid: string) => Promise<boolean>): Promise<string> {
  if (!configuration?.apiKey || !configuration.appId || !configuration.projectId || !configuration.messagingSenderId || !configuration.vapidKey)
    throw new Error('Bildirim sağlayıcı yapılandırması eksik.');
  if (!await isSupported()) throw new Error('Bu tarayıcı bildirim sağlayıcısını desteklemiyor.');
  const app = getApps().find(a => a.name === 'restaurant-order-push') ?? initializeApp(configuration, 'restaurant-order-push');
  messaging = getMessaging(app);
  const serviceWorkerRegistration = await navigator.serviceWorker.register('/sw.js', { scope: '/' });
  await navigator.serviceWorker.ready;
  stopRegistrationListener?.();
  let resolveTarget!: (target: string) => void;
  let rejectTarget!: (error: Error) => void;
  const targetReady = new Promise<string>((resolve, reject) => { resolveTarget = resolve; rejectTarget = reject; });
  const timeout = setTimeout(() => rejectTarget(new Error('Bildirim sağlayıcı kaydı zaman aşımına uğradı.')), 20000);
  stopRegistrationListener = onRegistered(messaging, fid => {
    void onTargetChanged(fid).then(saved => {
      if (saved) resolveTarget(fid);
      else rejectTarget(new Error('Bildirim hedefi sunucuya kaydedilemedi.'));
    }).catch(() => rejectTarget(new Error('Bildirim kaydı tamamlanamadı.')));
  });
  try {
    // register() yalnızca başlatmayı bekler; hedef onRegistered ile asenkron gelir.
    await Promise.all([register(messaging, { vapidKey: configuration.vapidKey, serviceWorkerRegistration }), targetReady]);
    return await targetReady;
  } finally { clearTimeout(timeout); }
}

export async function unregisterFirebasePush(): Promise<void> {
  stopRegistrationListener?.();
  stopRegistrationListener = undefined;
  if (messaging) await unregister(messaging);
}
