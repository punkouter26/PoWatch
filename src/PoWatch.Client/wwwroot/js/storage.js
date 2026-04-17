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
