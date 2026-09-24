// Keyboard focus for every dialog and sheet, that is, every element with `aria-modal="true"`:
//   - While a dialog is open, focus stays inside it, including controls Blazor adds later. A reward reveal adds its
//     Continue button only after the server confirms the grant, and the MutationObserver catches that change.
//   - Tab and Shift+Tab cycle within the dialog. The key listener is on the document, because when the focused
//     control is removed or disabled, focus moves to <body> and a Tab never reaches a listener on the dialog.
//   - On close, focus returns to the control that opened the dialog, or to <main> when that control is gone or
//     cannot take focus, as a claim control often is once its grant commits. Focus is never left on <body>,
//     because the document does not scroll (app.css).
// The document-level observer only schedules the work, which runs at most once per animation frame, because the
// card table animates continuously and would otherwise trigger the work on every mutation.

(function () {
    'use strict';

    var MODAL = '[aria-modal="true"]';

    var FOCUSABLE =
        'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), ' +
        'textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

    // One entry per open modal, keyed by its element, in the order they opened. The last one opened holds the
    // keyboard, and each restores focus to its own opener when it closes.
    var openDialogs = new Map();

    // The element that last lost focus to nothing, or null once any element takes focus. The same render that
    // opens a dialog often disables the control that opened it, and the browser blurs a disabled element. By the
    // time the dialog is found, `document.activeElement` is <body>, and this variable holds the opener.
    var blurredToNothing = null;

    document.addEventListener('focusin', function () {
        blurredToNothing = null;
    }, true);

    document.addEventListener('focusout', function (event) {
        if (!event.relatedTarget && event.target && event.target.nodeType === 1)
            blurredToNothing = event.target;
    }, true);

    // True when the element has a layout box. An element with `display: none` on itself or an ancestor has no
    // client rects. `offsetParent` is not used, because it is also null for a `position: fixed` element.
    function hasLayoutBox(el) {
        return el.getClientRects().length > 0;
    }

    function focusablesIn(modal) {
        return Array.prototype.filter.call(modal.querySelectorAll(FOCUSABLE), hasLayoutBox);
    }

    // Moves focus into the modal, unless a control in it already has focus. Runs on every syncOpenDialogs call, so
    // focus also moves to a control that is added after the modal opened.
    function focusInto(modal) {
        var active = document.activeElement;
        if (active && active !== modal && modal.contains(active))
            return;

        var focusables = focusablesIn(modal);
        if (focusables.length > 0)
            focusables[0].focus({ preventScroll: true });
        else if (active !== modal)
            modal.focus({ preventScroll: true });
    }

    // Records a newly opened dialog and the element to return focus to. syncOpenDialogs moves the focus afterwards,
    // so two dialogs that open in the same frame both record the opener from before either of them.
    function recordOpenedDialog(modal) {
        var active = document.activeElement;
        var returnTo = active && active !== document.body ? active : blurredToNothing;

        openDialogs.set(modal, { returnTo: returnTo });
    }

    function restoreFocusAfterClose(entry) {
        var returnTo = entry.returnTo;
        if (returnTo && returnTo.isConnected && typeof returnTo.focus === 'function') {
            returnTo.focus({ preventScroll: true });

            // focus() on a disabled control does nothing and throws nothing, so check where focus ended up.
            if (document.activeElement === returnTo)
                return;
        }

        // The opener is gone or cannot take focus. Every screen has a <main>, and focusing it makes the next Tab
        // move through the visible page.
        var main = document.querySelector('main');
        if (!main)
            return;

        if (!main.hasAttribute('tabindex'))
            main.setAttribute('tabindex', '-1');

        main.focus({ preventScroll: true });
    }

    // The dialog the keyboard belongs to: the last one opened.
    function topmost() {
        var last = null;
        openDialogs.forEach(function (_, modal) { last = modal; });
        return last;
    }

    document.addEventListener('keydown', function (event) {
        if (event.key !== 'Tab' || openDialogs.size === 0)
            return;

        var modal = topmost();
        var focusables = focusablesIn(modal);
        if (focusables.length === 0) {
            // The dialog has no focusable control, for example while a reward reveal waits for the server.
            // Tab must not move focus outside it.
            event.preventDefault();
            return;
        }

        var first = focusables[0];
        var last = focusables[focusables.length - 1];
        var active = document.activeElement;

        if (!modal.contains(active)) {
            // Focus is outside the dialog because the focused control was removed or disabled and the browser
            // moved focus to <body>. Move it back to the dialog's first control.
            event.preventDefault();
            first.focus();
        } else if (event.shiftKey && active === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && active === last) {
            event.preventDefault();
            first.focus();
        }
    }, true);

    // Updates `openDialogs` to match the dialogs in the document. Runs at most once per frame, with one query for the
    // whole document.
    //
    // Closed dialogs are handled first. When one dialog replaces another in the same frame, the new dialog then
    // records the element that the closed dialog returned focus to.
    function syncOpenDialogs() {
        var present = new Set(document.querySelectorAll(MODAL));

        var closed = [];
        openDialogs.forEach(function (_, modal) {
            if (!present.has(modal))
                closed.push(modal);
        });

        for (var c = 0; c < closed.length; c++) {
            var entry = openDialogs.get(closed[c]);
            openDialogs.delete(closed[c]);
            restoreFocusAfterClose(entry);
        }

        present.forEach(function (modal) {
            if (!openDialogs.has(modal))
                recordOpenedDialog(modal);
        });

        // Only the topmost dialog takes focus, so two open dialogs do not move focus back and forth.
        if (openDialogs.size > 0)
            focusInto(topmost());
    }

    var scheduled = false;

    function scheduleSync() {
        if (scheduled)
            return;

        scheduled = true;
        requestAnimationFrame(function () {
            scheduled = false;
            syncOpenDialogs();
        });
    }

    new MutationObserver(scheduleSync).observe(document.documentElement, { childList: true, subtree: true });
    scheduleSync();
})();
