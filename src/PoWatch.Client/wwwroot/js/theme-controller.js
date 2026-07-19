// theme-controller.js — runs once at app boot, before any Blazor paint.
// Reads the persisted theme from localStorage (or matches the OS preference)
// and applies it to <html data-theme> BEFORE the layout renders, so the user
// never sees a dark -> light flash. Subsequent toggles are handled by the
// theme-toggle button in NavMenu.

(function () {
    'use strict';
    try {
        var stored = null;
        try { stored = localStorage.getItem('powatch:theme'); } catch (_) { /* private mode */ }
        if (stored !== 'light' && stored !== 'dark') {
            var prefersLight = window.matchMedia && window.matchMedia('(prefers-color-scheme: light)').matches;
            stored = prefersLight ? 'light' : 'dark';
        }
        document.documentElement.setAttribute('data-theme', stored);
        // Mark the page so server-side RenderTreeBuilder can avoid SSR-side CSS dump conflicts.
        document.documentElement.setAttribute('data-theme-ready', 'true');
    } catch (_) {
        // Old browser, very strict CSP, or no localStorage — leave default (dark).
    }

    // Expose a tiny API for the theme toggle button to call.
    window.powatchTheme = {
        get: function () {
            return document.documentElement.getAttribute('data-theme') || 'dark';
        },
        set: function (value) {
            if (value !== 'light' && value !== 'dark') return;
            document.documentElement.setAttribute('data-theme', value);
            try { localStorage.setItem('powatch:theme', value); } catch (_) { /* private mode */ }
            // Notify any Blazor components listening so they can update their UI.
            try {
                window.dispatchEvent(new CustomEvent('powatch:themechanged', { detail: { theme: value } }));
            } catch (_) { /* IE */ }
        },
        toggle: function () {
            var current = window.powatchTheme.get();
            window.powatchTheme.set(current === 'dark' ? 'light' : 'dark');
            return window.powatchTheme.get();
        }
    };
})();
