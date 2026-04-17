// ═══════════════════════════════════════════════════════════════════════════
// PoWatch Keyboard Bindings & Command Palette
// Modern UI/UX Enhancement (2025/2026)
// ═══════════════════════════════════════════════════════════════════════════

(function () {
    'use strict';

    const SHORTCUTS = {
        // Navigation shortcuts
        'ctrl+1': { action: 'nav', path: '/', label: 'Observer Hub' },
        'ctrl+2': { action: 'nav', path: '/live-dashboard', label: 'Live Dashboard' },
        'ctrl+3': { action: 'nav', path: '/archives', label: 'Archives' },
        'ctrl+4': { action: 'nav', path: '/identity', label: 'Identity Nexus' },
        'ctrl+5': { action: 'nav', path: '/diagnostics', label: 'Diagnostics' },
        
        // Action shortcuts
        'ctrl+b': { action: 'sidebar-toggle', label: 'Toggle Sidebar' },
        'ctrl+k': { action: 'command-palette', label: 'Command Palette' },
        'ctrl+r': { action: 'refresh', label: 'Refresh' },
        'r': { action: 'refresh', label: 'Refresh (when not in input)' },
        '?': { action: 'help', label: 'Show Help' },
        'Escape': { action: 'close', label: 'Close/Dismiss' },
        
        // Archives navigation
        '←': { action: 'prev-day', label: 'Previous Day' },
        '→': { action: 'next-day', label: 'Next Day' },
    };

    let _dotNetRef = null;
    let _commandPaletteOpen = false;

    // Setup keyboard bindings
    function setup(dotNetRef) {
        _dotNetRef = dotNetRef;
        
        document.addEventListener('keydown', handleKeyDown);
        console.log('[PoWatch] Keyboard bindings initialized');
    }

    function cleanup() {
        document.removeEventListener('keydown', handleKeyDown);
        _dotNetRef = null;
        console.log('[PoWatch] Keyboard bindings cleaned up');
    }

    function handleKeyDown(e) {
        // Ignore if in input/textarea/select
        const target = e.target;
        if (target.tagName === 'INPUT' || 
            target.tagName === 'TEXTAREA' || 
            target.tagName === 'SELECT' ||
            target.isContentEditable) {
            
            // Allow Escape to close command palette even in input
            if (e.key !== 'Escape') return;
        }

        const key = buildKeyString(e);
        const shortcut = SHORTCUTS[key];

        if (!shortcut) return;

        e.preventDefault();
        
        switch (shortcut.action) {
            case 'nav':
                window.location.href = shortcut.path;
                break;
                
            case 'sidebar-toggle':
                triggerEvent('sidebar-toggle');
                break;
                
            case 'command-palette':
                toggleCommandPalette();
                break;
                
            case 'refresh':
                triggerEvent('refresh');
                break;
                
            case 'help':
                triggerEvent('help');
                break;
                
            case 'close':
                if (_commandPaletteOpen) {
                    closeCommandPalette();
                }
                break;
                
            case 'prev-day':
                triggerEvent('archives-prev');
                break;
                
            case 'next-day':
                triggerEvent('archives-next');
                break;
        }

        // Notify .NET
        if (_dotNetRef && key !== 'Escape') {
            _dotNetRef.invokeMethodAsync('OnShortcut', key);
        }
    }

    function buildKeyString(e) {
        const parts = [];
        
        if (e.ctrlKey) parts.push('ctrl');
        if (e.shiftKey && e.key !== 'Shift') parts.push('shift');
        if (e.altKey) parts.push('alt');
        
        // Normalize key names
        let key = e.key;
        if (key === ' ') key = 'space';
        if (key === 'ArrowLeft') key = '←';
        if (key === 'ArrowRight') key = '→';
        if (key === 'ArrowUp') key = '↑';
        if (key === 'ArrowDown') key = '↓';
        
        parts.push(key.toLowerCase());
        return parts.join('+');
    }

    function triggerEvent(action) {
        window.dispatchEvent(new CustomEvent('powatch:shortcut', { detail: action }));
    }

    // Command Palette
    function toggleCommandPalette() {
        if (_commandPaletteOpen) {
            closeCommandPalette();
        } else {
            openCommandPalette();
        }
    }

    function openCommandPalette() {
        _commandPaletteOpen = true;
        triggerEvent('command-palette-open');
        
        // Create overlay if not exists
        let overlay = document.getElementById('command-palette-overlay');
        if (!overlay) {
            overlay = createCommandPaletteOverlay();
            document.body.appendChild(overlay);
        }
        
        overlay.style.display = 'flex';
        setTimeout(() => {
            const input = overlay.querySelector('.command-palette-input');
            if (input) input.focus();
        }, 50);
    }

    function closeCommandPalette() {
        _commandPaletteOpen = false;
        triggerEvent('command-palette-close');
        
        const overlay = document.getElementById('command-palette-overlay');
        if (overlay) {
            overlay.style.display = 'none';
        }
    }

    function createCommandPaletteOverlay() {
        const overlay = document.createElement('div');
        overlay.id = 'command-palette-overlay';
        overlay.className = 'command-palette-overlay';
        overlay.style.display = 'none';
        
        overlay.innerHTML = `
            <div class="command-palette">
                <input type="text" 
                       class="command-palette-input" 
                       placeholder="Type a command or search..."
                       autocomplete="off"
                       spellcheck="false" />
                <div class="command-palette-results"></div>
            </div>
        `;

        // Close on backdrop click
        overlay.addEventListener('click', (e) => {
            if (e.target === overlay) closeCommandPalette();
        });

        // Handle input
        const input = overlay.querySelector('.command-palette-input');
        input.addEventListener('input', (e) => {
            updateResults(e.target.value);
        });

        // Handle keyboard navigation in results
        input.addEventListener('keydown', (e) => {
            const results = overlay.querySelector('.command-palette-results');
            const items = results.querySelectorAll('.command-palette-item');
            
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                navigateResults(items, 1);
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                navigateResults(items, -1);
            } else if (e.key === 'Enter') {
                e.preventDefault();
                const selected = results.querySelector('.command-palette-item--selected');
                if (selected) selected.click();
            } else if (e.key === 'Escape') {
                closeCommandPalette();
            }
        });

        return overlay;
    }

    function navigateResults(items, direction) {
        const selected = document.querySelector('.command-palette-item--selected');
        let index = selected ? Array.from(items).indexOf(selected) : -1;
        
        index = Math.max(-1, Math.min(items.length - 1, index + direction));
        
        items.forEach((item, i) => {
            item.classList.toggle('command-palette-item--selected', i === index);
            if (i === index) item.scrollIntoView({ block: 'nearest' });
        });
    }

    function updateResults(query) {
        const results = document.querySelector('.command-palette-results');
        if (!results) return;

        // Default commands
        const commands = [
            { label: 'Observer Hub', action: '/', shortcut: 'Ctrl+1', icon: 'home' },
            { label: 'Live Dashboard', action: '/live-dashboard', shortcut: 'Ctrl+2', icon: 'chart' },
            { label: 'Archives', action: '/archives', shortcut: 'Ctrl+3', icon: 'archive' },
            { label: 'Identity Nexus', action: '/identity', shortcut: 'Ctrl+4', icon: 'users' },
            { label: 'Diagnostics', action: '/diagnostics', shortcut: 'Ctrl+5', icon: 'cpu' },
            { label: 'Toggle Sidebar', action: 'sidebar-toggle', shortcut: 'Ctrl+B', icon: 'sidebar' },
            { label: 'Refresh Page', action: 'refresh', shortcut: 'R', icon: 'refresh' },
            { label: 'Show Help', action: 'help', shortcut: '?', icon: 'help' },
        ];

        const filtered = query 
            ? commands.filter(c => c.label.toLowerCase().includes(query.toLowerCase()))
            : commands;

        results.innerHTML = filtered.map((cmd, i) => `
            <div class="command-palette-item ${i === 0 ? 'command-palette-item--selected' : ''}"
                 data-action="${cmd.action}">
                <span class="command-palette-item-icon">${getIconSvg(cmd.icon)}</span>
                <span class="command-palette-item-label">${cmd.label}</span>
                <span class="command-palette-item-shortcut">${cmd.shortcut}</span>
            </div>
        `).join('');

        // Add click handlers
        results.querySelectorAll('.command-palette-item').forEach(item => {
            item.addEventListener('click', () => {
                const action = item.dataset.action;
                executeAction(action);
            });
        });
    }

    function executeAction(action) {
        closeCommandPalette();
        
        if (action.startsWith('/')) {
            window.location.href = action;
        } else {
            triggerEvent(action);
        }
    }

    function getIconSvg(name) {
        const icons = {
            home: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/></svg>',
            chart: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="20" x2="12" y2="10"/><line x1="18" y1="20" x2="18" y2="4"/><line x1="6" y1="20" x2="6" y2="16"/></svg>',
            archive: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="21 8 21 21 3 21 3 8"/><rect x="1" y="3" width="22" height="5"/><line x1="10" y1="12" x2="14" y2="12"/></svg>',
            users: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>',
            cpu: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="4" y="4" width="16" height="16" rx="2"/><rect x="9" y="9" width="6" height="6"/><line x1="9" y1="1" x2="9" y2="4"/><line x1="15" y1="1" x2="15" y2="4"/><line x1="9" y1="20" x2="9" y2="23"/><line x1="15" y1="20" x2="15" y2="23"/><line x1="20" y1="9" x2="23" y2="9"/><line x1="20" y1="14" x2="23" y2="14"/><line x1="1" y1="9" x2="4" y2="9"/><line x1="1" y1="14" x2="4" y2="14"/></svg>',
            sidebar: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><line x1="9" y1="3" x2="9" y2="21"/></svg>',
            refresh: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="23 4 23 10 17 10"/><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10"/></svg>',
            help: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="10"/><path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>',
        };
        return icons[name] || '';
    }

    // Register global handlers
    window.powatchKeybindings = { setup, cleanup, toggleCommandPalette, closeCommandPalette };

})();