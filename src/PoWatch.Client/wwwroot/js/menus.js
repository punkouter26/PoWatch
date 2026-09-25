// The header's settings menu is a native <details>; close it on any click outside it.
(function () {
    'use strict';

    function closeMenus(e) {
        document.querySelectorAll('details.term-menu[open]').forEach(function (menu) {
            if (!menu.contains(e.target)) menu.removeAttribute('open');
        });
    }

    function setup() {
        document.removeEventListener('click', closeMenus);
        document.addEventListener('click', closeMenus);
    }

    window.powatchMenus = { setup };
})();
