// The keyboard: `g` then a letter jumps to a section, Space starts or stops observing, ← → step History's day,
// and Ctrl+K opens a command palette listing all of it.
(function () {
    'use strict';

    const SECTIONS = { l: 'live', s: 'stats', h: 'history', r: 'regulars' };
    const find = (test) => document.querySelector(`[data-test="${test}"]`);
    const click = (test) => { const el = find(test); el?.click(); return !!el; };

    // Stop if running; otherwise start from wherever the start control is (Live's panel or the header).
    const toggleSession = () => click('header-stop') || click('start-camera') || click('header-start');

    const commands = () => [
        ...Object.entries(SECTIONS).map(([key, name]) => ({ label: `Go to ${name}`, hint: `g ${key}`, run: () => click(`nav-${name}`) })),
        { label: 'Start or stop observing', hint: 'Space', run: toggleSession },
        { label: 'Start the demo scene', hint: '', run: () => click('start-demo') || Blazor.navigateTo('?start=demo') },
        { label: 'Notifications', hint: '', run: () => click('tray-toggle') },
        { label: 'Settings: theme, sound, captions, watch rules, your data', hint: '', run: () => click('settings-menu') },
        { label: 'System status', hint: '', run: () => Blazor.navigateTo('system') },
        { label: 'Full screen', hint: '', run: () => document.documentElement.requestFullscreen?.() },
    ];

    // ── Command palette ──────────────────────────────────────────────────────

    let palette = null;

    function openPalette() {
        if (!palette) {
            palette = document.createElement('dialog');
            palette.className = 'term-palette';
            palette.setAttribute('aria-label', 'Command palette');
            palette.innerHTML = '<input type="text" placeholder="Type a command…" aria-label="Command" autocomplete="off" /><ul role="listbox"></ul>';
            document.body.appendChild(palette);
            const input = palette.querySelector('input');
            input.addEventListener('input', () => renderPalette(0));
            palette.addEventListener('keydown', onPaletteKey);
            palette.addEventListener('click', (e) => {
                const item = e.target.closest('li');
                if (item) runPalette(Number(item.dataset.index));
                else if (e.target === palette) palette.close();
            });
        }
        palette.querySelector('input').value = '';
        renderPalette(0);
        palette.showModal();
    }

    function matches() {
        const query = palette.querySelector('input').value.trim().toLowerCase();
        return commands().filter((c) => c.label.toLowerCase().includes(query));
    }

    function renderPalette(selected) {
        const list = palette.querySelector('ul');
        list.replaceChildren(...matches().map((command, i) => {
            const item = document.createElement('li');
            item.dataset.index = i;
            item.setAttribute('role', 'option');
            item.setAttribute('aria-selected', i === selected);
            item.append(command.label);
            const hint = document.createElement('kbd');
            hint.textContent = command.hint;
            item.append(hint);
            return item;
        }));
        palette.dataset.selected = selected;
    }

    function runPalette(index) {
        const command = matches()[index];
        palette.close();
        command?.run();
    }

    function onPaletteKey(e) {
        const count = matches().length;
        const selected = Number(palette.dataset.selected) || 0;
        if (e.key === 'ArrowDown') { e.preventDefault(); renderPalette((selected + 1) % Math.max(count, 1)); }
        else if (e.key === 'ArrowUp') { e.preventDefault(); renderPalette((selected - 1 + count) % Math.max(count, 1)); }
        else if (e.key === 'Enter') { e.preventDefault(); runPalette(selected); }
    }

    // ── Shortcuts ────────────────────────────────────────────────────────────

    let leader = 0; // when `g` was pressed

    function onKey(e) {
        if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
            e.preventDefault();
            openPalette();
            return;
        }

        // Never steal keys from typing, from an open dialog, or from a modified chord.
        const target = e.target;
        if (e.ctrlKey || e.metaKey || e.altKey || palette?.open) return;
        const tag = target instanceof HTMLElement ? target.tagName : '';
        if (target instanceof HTMLElement && (target.isContentEditable || /^(INPUT|TEXTAREA|SELECT)$/.test(tag))) return;

        // The day arrows work straight after clicking ◀ ▶ (focus is on the button), but a tab strip
        // keeps its own arrow keys.
        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
            if (!target.closest?.('[role="tab"], [role="tablist"]')) click(e.key === 'ArrowLeft' ? 'history-prev' : 'history-next');
            return;
        }

        // Space and letters belong to whatever control has focus.
        if (/^(BUTTON|A|SUMMARY)$/.test(tag)) return;

        const key = e.key.toLowerCase();
        if (Date.now() - leader < 1200 && SECTIONS[key]) {
            leader = 0;
            click(`nav-${SECTIONS[key]}`);
        } else if (key === 'g') {
            leader = Date.now();
        } else if (e.key === ' ') {
            e.preventDefault();
            toggleSession();
        }
    }

    function setup() {
        document.removeEventListener('keydown', onKey);
        document.addEventListener('keydown', onKey);
    }

    /** Whether there is room for secondary table columns (the same breakpoint as terminal.css). */
    const isWide = () => window.innerWidth > 720;

    window.powatchMenus = { setup, openPalette, isWide };
})();
