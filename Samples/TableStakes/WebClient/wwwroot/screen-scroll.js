// Scrolls a newly opened screen to its top.
//
// The document does not scroll. The shell scrolls one element that stays in place across navigations (meta-shell.css),
// so without this script a new screen opens at the previous screen's scroll offset. The browser's own scroll
// restoration applies only to the document.
//
// A navigation is detected by the [data-screen-in] element being replaced. MetaLayout.razor
// (Components/Shell/MetaLayout.razor) replaces it on every navigation and at no other time. The URL is not used
// because it can change before the new screen renders, and the entrance animation is not used because it does
// not run with reduced motion.

(function () {
    'use strict';

    var SCREEN = '[data-screen-in]';

    // The screen element already scrolled to the top. While it is still connected, no navigation has happened,
    // and a mutation costs one isConnected check.
    var scrolledScreen = null;

    function scrollNewScreenToTop() {
        if (scrolledScreen && scrolledScreen.isConnected)
            return;

        scrolledScreen = document.querySelector(SCREEN);

        if (!scrolledScreen)
            return;

        // Scroll the screen's drag-scroll surface, not the window.
        var surface = scrolledScreen.closest('[data-drag-scroll]');

        if (surface)
            surface.scrollTop = 0;
    }

    // Runs directly in the observer callback, not deferred to an animation frame. MutationObserver callbacks run
    // before the next paint, so the new screen's first painted frame is already at its top.
    new MutationObserver(scrollNewScreenToTop).observe(document.documentElement, { childList: true, subtree: true });

    scrollNewScreenToTop();
})();
