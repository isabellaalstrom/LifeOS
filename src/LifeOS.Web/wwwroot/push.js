// Browser side of Web Push for the settings page. Only talks to this app's own server.
window.lifeosPush = (() => {
    const toKey = (base64Url) => {
        const padding = '='.repeat((4 - (base64Url.length % 4)) % 4);
        const raw = atob((base64Url + padding).replace(/-/g, '+').replace(/_/g, '/'));
        return Uint8Array.from(raw, (c) => c.charCodeAt(0));
    };
    const toBase64 = (buffer) => btoa(String.fromCharCode(...new Uint8Array(buffer)));

    const registration = async () => {
        const existing = await navigator.serviceWorker.getRegistration('./');
        return existing || navigator.serviceWorker.register('sw.js', { scope: './' });
    };

    return {
        // What this browser can do. 'secure' is false on plain http (except localhost), where push is not available.
        support: () => ({
            supported: 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window,
            secure: window.isSecureContext === true,
            permission: 'Notification' in window ? Notification.permission : 'denied',
        }),

        // The endpoint this browser is currently subscribed with, or null.
        current: async () => {
            if (!('serviceWorker' in navigator)) return null;
            const reg = await navigator.serviceWorker.getRegistration('./');
            const sub = reg ? await reg.pushManager.getSubscription() : null;
            return sub ? sub.endpoint : null;
        },

        // Asks for permission, subscribes and returns what the server needs to store.
        subscribe: async (publicKey) => {
            try {
                const permission = await Notification.requestPermission();
                if (permission !== 'granted') return { error: 'denied' };
                const reg = await registration();
                await navigator.serviceWorker.ready;
                let sub = await reg.pushManager.getSubscription();
                if (!sub) {
                    sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: toKey(publicKey) });
                }
                return {
                    endpoint: sub.endpoint,
                    p256dh: toBase64(sub.getKey('p256dh')),
                    auth: toBase64(sub.getKey('auth')),
                    userAgent: navigator.userAgent,
                };
            } catch (e) {
                return { error: 'failed' };
            }
        },

        // Unsubscribes this browser and returns the endpoint that was removed, so the server can forget it.
        unsubscribe: async () => {
            const reg = await navigator.serviceWorker.getRegistration('./');
            const sub = reg ? await reg.pushManager.getSubscription() : null;
            if (!sub) return null;
            const endpoint = sub.endpoint;
            await sub.unsubscribe();
            return endpoint;
        },
    };
})();
