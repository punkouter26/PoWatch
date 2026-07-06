/**
 * PoWatch Storage — lightweight localStorage helpers exposed to Blazor via IJSRuntime.
 * All data is stored locally in the browser; nothing leaves the device.
 *
 * Usage:
 *   PoWatchStorage.get(key)          → string | null
 *   PoWatchStorage.set(key, value)   → void
 *   PoWatchStorage.remove(key)       → void
 */
window.PoWatchStorage = {
    get: function (key) {
        try { return localStorage.getItem(key); }
        catch { return null; }
    },
    set: function (key, value) {
        try { localStorage.setItem(key, value); }
        catch { /* storage quota exceeded — ignore */ }
    },
    remove: function (key) {
        try { localStorage.removeItem(key); }
        catch { /* ignore */ }
    }
};

/**
 * PoWatch image validation (NET_RUN_10 #2) — the blob API hands back a SAS URL
 * even when the underlying blob is missing, so evidence cards would render a wall
 * of broken "No image" tiles. Preload each URL and report which actually decode,
 * so Blazor can render only real evidence and collapse the rest into a count.
 *
 *   PoWatchImages.filterLoadable([url, ...]) → Promise<string[]>  (subset that loaded)
 */
window.PoWatchImages = {
    loads: function (url) {
        return new Promise(function (resolve) {
            if (!url) { resolve(false); return; }
            var img = new Image();
            var done = false;
            var finish = function (ok) { if (!done) { done = true; resolve(ok); } };
            img.onload = function () { finish(img.naturalWidth > 0); };
            img.onerror = function () { finish(false); };
            // Safety timeout so a hung request can't block the grid indefinitely.
            setTimeout(function () { finish(false); }, 6000);
            img.src = url;
        });
    },
    filterLoadable: async function (urls) {
        if (!Array.isArray(urls)) return [];
        var results = await Promise.all(urls.map(function (u) {
            return window.PoWatchImages.loads(u).then(function (ok) { return ok ? u : null; });
        }));
        return results.filter(function (u) { return u !== null; });
    }
};
