// viewport-watch.js — exposes a tiny API so Blazor components can subscribe to viewport
// breakpoint changes without polling. MatchMediaChange fires when the page crosses a
// threshold; we forward the new "is this below X" boolean to the subscribed .NET
// instance via JSInvokable.

(function () {
    'use strict';

    /**
     * @param {number} breakpointPx
     * @returns {boolean}
     */
    function isCompact(breakpointPx) {
        if (!window.matchMedia) return false;
        return window.matchMedia(`(max-width: ${breakpointPx - 1}px)`).matches;
    }

    /**
     * Subscribe the given .NET object to breakpoint changes for `breakpointPx`.
     * Calls dotnet.invokeMethodAsync('OnViewportChangedAsync', isCompact(...)).
     *
     * @param {number} breakpointPx
     * @param {object} dotnetRef
     */
    function subscribe(breakpointPx, dotnetRef) {
        if (!window.matchMedia || !dotnetRef) return;
        var mq = window.matchMedia(`(max-width: ${breakpointPx - 1}px)`);
        // Modern browsers fire `change`; older Safari only fires addListener. Both are wired.
        var handler = function () {
            try { dotnetRef.invokeMethodAsync('OnViewportChangedAsync', mq.matches); }
            catch (_) { /* page navigated */ }
        };
        if (mq.addEventListener) mq.addEventListener('change', handler);
        else if (mq.addListener) mq.addListener(handler);
    }

    window.powatchViewport = {
        isCompact: isCompact,
        subscribe: subscribe
    };
})();