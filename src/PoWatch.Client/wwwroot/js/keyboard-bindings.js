// PoWatch keyboard bindings for the Terminal shell.
//
//   1..6 / Ctrl+1..6   jump to LIVE, STATS, HISTORY, REGULARS, TROPHIES, SYSTEM
//   / or Ctrl+K        focus the PWCH> command line
//   Escape             leave the command line
//
// Deliberately NOT bound: F-keys and Ctrl+R. Swallowing a browser key (F5, Ctrl+R) takes away the
// one recovery gesture an unattended kiosk operator has, so the key caps show numbers instead.
// Every binding calls preventDefault(), so only keys that actually do something are listed.
(function () {
    'use strict';

    const SECTIONS = ['', 'stats', 'history', 'regulars', 'trophies', 'system'];

    function setup() {
        document.removeEventListener('keydown', onKeyDown);
        document.addEventListener('keydown', onKeyDown);
        document.removeEventListener('click', closeMenus);
        document.addEventListener('click', closeMenus);
    }

    // The header's settings menu is a native <details>; close it on any click outside it.
    function closeMenus(e) {
        document.querySelectorAll('details.term-menu[open]').forEach(function (menu) {
            if (!menu.contains(e.target)) menu.removeAttribute('open');
        });
    }

    function isTyping(target) {
        return target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.tagName === 'SELECT' || target.isContentEditable);
    }

    function commandLine() {
        return document.querySelector('[data-command-line]');
    }

    function onKeyDown(e) {
        const typing = isTyping(e.target);

        if (e.key === 'Escape' && e.target === commandLine()) {
            e.target.blur();
            return;
        }
        if (typing || e.altKey || e.metaKey) return;

        const digit = /^[1-6]$/.test(e.key) ? Number(e.key) : 0;
        if (digit && !e.shiftKey) {
            e.preventDefault();
            navigate(SECTIONS[digit - 1]);
            return;
        }

        if ((e.key === '/' && !e.ctrlKey) || (e.ctrlKey && e.key.toLowerCase() === 'k')) {
            const input = commandLine();
            if (!input) return;
            e.preventDefault();
            input.focus({ preventScroll: true });
            input.select();
        }
    }

    // Blazor SPA navigation; a full reload would re-boot the WASM runtime for an in-app hop.
    function navigate(path) {
        if (window.powatchFx) {
            window.powatchFx.navigate(path);   // panels morph between sections
        } else if (window.Blazor && typeof window.Blazor.navigateTo === 'function') {
            window.powatchFx ? window.powatchFx.navigate(path) : window.Blazor.navigateTo(path);
        } else {
            window.location.href = path || '/';
        }
    }

    window.powatchKeybindings = { setup };
})();
