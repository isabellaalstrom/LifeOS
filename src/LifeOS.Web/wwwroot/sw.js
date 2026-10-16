// LifeOS service worker. Its only jobs are showing push notifications and opening the app when one is tapped.
// It does not cache anything and never sees app data: the push payload is a generic title/text and a relative page.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

self.addEventListener('push', (event) => {
    let data = {};
    try {
        data = event.data ? event.data.json() : {};
    } catch (e) {
        data = {};
    }

    event.waitUntil(self.registration.showNotification(data.title || 'LifeOS', {
        body: data.body || '',
        icon: 'icons/icon-192.png',
        badge: 'icons/icon-192.png',
        data: { url: data.url || '' },
    }));
});

self.addEventListener('notificationclick', (event) => {
    event.notification.close();
    const target = new URL((event.notification.data && event.notification.data.url) || '', self.registration.scope).href;
    event.waitUntil((async () => {
        const windows = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        for (const client of windows) {
            if (client.url.startsWith(self.registration.scope) && 'focus' in client) {
                await client.focus();
                if ('navigate' in client) {
                    await client.navigate(target);
                }
                return;
            }
        }
        await self.clients.openWindow(target);
    })());
});
