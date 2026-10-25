// Operations Web Service Worker for Push Notifications
// Adheres to ADR-0011: Data minimization and allowlisted route navigation.

const ALLOWED_ROUTES = ['/', '/operations', '/kds', '/orders'];

self.addEventListener('install', (event) => {
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  event.waitUntil(self.clients.claim());
});

self.addEventListener('push', (event) => {
  let eventId = '';
  try {
    if (event.data) {
      const payload = event.data.json();
      eventId = payload.event_id || (payload.data && payload.data.event_id) || '';
    }
  } catch {
    // If payload is not valid JSON, proceed with empty eventId
  }

  // Data minimization: Lockscreen must never contain customer notes, item names, or prices.
  const title = 'Operasyon bildirimi';
  const options = {
    body: 'Yeni bir masa veya sipariş bildirimi var. Ayrıntılar için dokunun.',
    tag: eventId || 'restaurant-order-ops',
    data: {
      event_id: eventId,
      target_route: '/operations',
    },
    renotify: true,
  };

  event.waitUntil(
    Promise.all([
      self.registration.showNotification(title, options),
      self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
        for (const client of clientList) {
          client.postMessage({
            type: 'PUSH_RECEIVED',
            event_id: eventId,
          });
        }
      }),
    ])
  );
});

self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  let targetRoute = '/operations';
  const notificationData = event.notification.data;
  if (notificationData && notificationData.target_route) {
    const rawRoute = notificationData.target_route.split('?')[0].split('#')[0];
    if (ALLOWED_ROUTES.includes(rawRoute)) {
      targetRoute = rawRoute;
    }
  }

  event.waitUntil(
    self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then((clientList) => {
      for (const client of clientList) {
        if ('focus' in client) {
          return client.focus();
        }
      }
      if (self.clients.openWindow) {
        return self.clients.openWindow(targetRoute);
      }
    })
  );
});
