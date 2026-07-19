// preferences.js — bridge for UserPreferencesService (audit #8).
//
// Only touches keys prefixed with "powatch:" so localStorage stays tidy and
// the namespace is enumerable in DevTools. Failures are swallowed because
// private-mode browsers throw on localStorage.

(function () {
    'use strict';
    var NS = 'powatch:';
    var cache = null;

    function ls() {
        try { return window.localStorage; } catch (_) { return null; }
    }

    function readAll() {
        if (cache) return cache;
        cache = {};
        var s = ls(); if (!s) return cache;
        for (var i = 0; i < s.length; i++) {
            var k = s.key(i);
            if (k && k.indexOf(NS) === 0) cache[k] = s.getItem(k) || '';
        }
        return cache;
    }

    window.powatchPref = {
        keys: function () {
            return Object.keys(readAll());
        },
        get: function (fullKey) {
            return readAll()[fullKey] || null;
        },
        set: function (fullKey, value) {
            var s = ls(); if (!s) return;
            try {
                s.setItem(fullKey, value);
                cache && (cache[fullKey] = value);
            } catch (_) { /* private mode */ }
        },
        remove: function (fullKey) {
            var s = ls(); if (!s) return;
            try { s.removeItem(fullKey); if (cache) delete cache[fullKey]; } catch (_) { }
        },
        // Namespace constant so callers don't hard-code the prefix.
        ns: NS
    };
})();
