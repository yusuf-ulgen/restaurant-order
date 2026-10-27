// Bildirimler genel metin ve opak olay kimliği taşır; geçerli uygulama rotası köktür.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', event => event.waitUntil(self.clients.claim()));

self.addEventListener('push', event => {
  event.waitUntil((async () => {
    let payload;
    try { payload = event.data.json(); } catch { return; }
    const data = payload.data || payload;
    const eventId = data.event_id;
    if (typeof eventId !== 'string' || !eventId) return;
    if (data.expires_at && Number(data.expires_at) * 1000 <= Date.now()) return;
    const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of clients) client.postMessage({ type: 'PUSH_RECEIVED', event_id: eventId });
    if (clients.some(client => client.visibilityState === 'visible')) return;
    await self.registration.showNotification('Restoran bildirimi', {
      body: 'Yeni bir güncelleme var. Ayrıntılar için uygulamayı açın.',
      tag: eventId, renotify: false, data: { event_id: eventId, target_route: '/' },
    });
  })());
});

self.addEventListener('notificationclick', event => {
  event.notification.close();
  event.waitUntil((async () => {
    const clients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
    for (const client of clients) {
      if (new URL(client.url).origin === self.location.origin && 'focus' in client) return client.focus();
    }
    return self.clients.openWindow('/');
  })());
});
