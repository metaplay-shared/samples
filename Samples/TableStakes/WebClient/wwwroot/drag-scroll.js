// Drag scrolling with the mouse. The app hides its scrollbars (app.css), so a press-and-drag with the mouse scrolls
// a surface marked with data-drag-scroll, and the content follows the pointer 1:1.
//
// Every handler returns early unless `pointerType === 'mouse'`, so touch and pen input keep the browser's native
// scrolling with its momentum and `overscroll-behavior`. The listeners are on the document and find the surface
// with `closest()`, because Blazor re-renders the elements often and per-element listeners would need re-attaching.

(function () {
    'use strict';

    // Selector for the elements that drag scrolling applies to.
    var SURFACE = '[data-drag-scroll]';

    // Selector for an open modal dialog. While a modal is open, a drag must not scroll anything outside it
    // (see resolveSurface).
    var MODAL = '[aria-modal="true"]';

    // Class on the surface under the pointer when a drag would scroll it. The grab cursor CSS keys off this
    // class rather than the data-drag-scroll attribute, so a surface that cannot scroll shows no grab cursor.
    var DRAGGABLE_CLASS = 'is-drag-scrollable';

    // A press inside a text field starts no drag, so the browser can select text.
    var TEXT_ENTRY = 'input, textarea, select, [contenteditable=""], [contenteditable="true"]';

    // How far the pointer must move before the press counts as a drag instead of a click. Every control sits
    // inside a scroll surface, so a value that is too small turns clicks into drags.
    var DRAG_THRESHOLD_PX = 5;

    // How much overflow a surface must have before it counts as scrollable. Borders, shadows and sub-pixel
    // layout can leave a pixel of overflow on a surface whose content fits.
    var OVERFLOW_EPSILON_PX = 2;

    // The scrollable surface under the current press. Null when no press is tracked.
    var surface = null;
    var pointerId = -1;
    var originX = 0;
    var originY = 0;
    var originScrollLeft = 0;
    var originScrollTop = 0;

    // True once the pointer has moved past DRAG_THRESHOLD_PX in this press.
    var dragging = false;

    // True once the surface's scroll position has changed in this press. A drag can move the pointer without
    // scrolling, when the surface is already at its end or scrolls only on the other axis. Such a press is
    // treated as a click. This flag, not the pointer distance, decides the cursor, the pointer capture and
    // whether the click is swallowed.
    var scrolled = false;

    // Set when a drag that scrolled ends, so the click the browser fires for the same press does not activate
    // the control under the pointer. A drag released outside the window fires no click, so the next press and
    // the next key press also clear this flag. Otherwise it would swallow a later, intended click.
    var swallowNextClick = false;

    // The surface that has DRAGGABLE_CLASS, so the class can be removed when the pointer leaves it.
    var markedSurface = null;

    function canScroll(el) {
        return el.scrollHeight - el.clientHeight > OVERFLOW_EPSILON_PX
            || el.scrollWidth - el.clientWidth > OVERFLOW_EPSILON_PX;
    }

    // Returns the drag-scroll surface for a press on target, or null when there is none. While a modal is
    // open, returns null unless both target and the surface are inside the modal. The modal's scrim is a DOM
    // child of the page's scroll surface (it is only visually detached by `position: fixed`), so a `closest()`
    // walk from a press on the scrim would otherwise find the page behind the dialog and scroll it.
    function resolveSurface(target) {
        var modals = document.querySelectorAll(MODAL);
        for (var i = 0; i < modals.length; i++) {
            if (!modals[i].contains(target))
                return null;
        }

        var candidate = target.closest(SURFACE);

        for (var j = 0; j < modals.length; j++) {
            if (candidate !== null && !modals[j].contains(candidate))
                return null;
        }

        return candidate;
    }

    function markDraggable(el) {
        if (markedSurface === el)
            return;

        if (markedSurface !== null)
            markedSurface.classList.remove(DRAGGABLE_CLASS);

        markedSurface = el;

        if (el !== null)
            el.classList.add(DRAGGABLE_CLASS);
    }

    function endDrag() {
        if (scrolled)
            document.body.classList.remove('is-drag-scrolling');

        if (surface !== null && pointerId !== -1 && surface.hasPointerCapture(pointerId)) {
            try {
                surface.releasePointerCapture(pointerId);
            } catch (err) {
                // The pointer no longer exists, so there is no capture to release.
            }
        }

        surface = null;
        pointerId = -1;
        dragging = false;
        scrolled = false;
    }

    // Moves DRAGGABLE_CLASS to the surface under the pointer. A surface whose content fits, or one behind an
    // open modal, gets no class and no grab cursor.
    document.addEventListener('pointerover', function (e) {
        if (e.pointerType !== 'mouse' || !(e.target instanceof Element))
            return;

        var el = resolveSurface(e.target);
        markDraggable(el !== null && canScroll(el) ? el : null);
    });

    document.addEventListener('pointerdown', function (e) {
        // Only a primary button press starts or ends a drag. A secondary press during a drag must not reset
        // the drag or swallowNextClick, or the click from the following primary release would activate the
        // control under the pointer.
        if (e.pointerType !== 'mouse' || e.button !== 0)
            return;

        // Clear state left by a previous drag that ended without a pointerup or pointercancel reaching the page.
        swallowNextClick = false;
        endDrag();

        if (!(e.target instanceof Element))
            return;

        var el = resolveSurface(e.target);
        if (el === null || !canScroll(el)) {
            markDraggable(null);
            return;
        }

        if (e.target.closest(TEXT_ENTRY) !== null)
            return;

        surface = el;
        pointerId = e.pointerId;
        originX = e.clientX;
        originY = e.clientY;
        originScrollLeft = el.scrollLeft;
        originScrollTop = el.scrollTop;
    });

    document.addEventListener('pointermove', function (e) {
        if (surface === null || e.pointerId !== pointerId)
            return;

        // No button is pressed. The pointer is captured only after the surface starts scrolling, so a release
        // outside the window, or after focus moved to another application, never reaches the page. Without
        // this reset, the next mouse move would scroll the surface with no button pressed.
        if (e.buttons === 0) {
            endDrag();
            return;
        }

        var dx = e.clientX - originX;
        var dy = e.clientY - originY;

        if (!dragging) {
            if (Math.abs(dx) < DRAG_THRESHOLD_PX && Math.abs(dy) < DRAG_THRESHOLD_PX)
                return;

            dragging = true;
        }

        // The content follows the pointer 1:1, so dragging up scrolls down.
        surface.scrollLeft = originScrollLeft - dx;
        surface.scrollTop = originScrollTop - dy;

        if (scrolled || (surface.scrollLeft === originScrollLeft && surface.scrollTop === originScrollTop))
            return;

        // The surface has scrolled for the first time in this press.
        scrolled = true;
        document.body.classList.add('is-drag-scrolling');

        // Capture the pointer here, not on pointerdown. Pointer capture retargets the events the browser builds
        // a click from, so capturing on pointerdown would send every click to the surface instead of the control
        // under the pointer. At this point the click is swallowed anyway. The capture keeps the drag going
        // when the pointer leaves the surface or the phone frame.
        try {
            surface.setPointerCapture(pointerId);
        } catch (err) {
            // Capture is optional. The document listeners still receive the moves while the pointer is over the page.
        }

        // Clear the text selection only if it starts inside this surface, where the drag could have made it.
        // A selection elsewhere on the page is kept.
        var selection = window.getSelection();
        if (selection !== null && selection.anchorNode !== null && surface.contains(selection.anchorNode))
            selection.removeAllRanges();
    });

    document.addEventListener('pointerup', function (e) {
        if (surface === null || e.pointerId !== pointerId || e.button !== 0)
            return;

        // Swallow the click only if the surface scrolled. Otherwise the press was a click on the control.
        swallowNextClick = scrolled;
        endDrag();
    });

    // A cancelled pointer or a lost pointer capture resets the drag, the same as a release.
    function onPointerLost(e) {
        if (surface === null || e.pointerId !== pointerId)
            return;

        endDrag();
    }

    document.addEventListener('pointercancel', onPointerLost);
    document.addEventListener('lostpointercapture', onPointerLost);

    // Registered in the capture phase and before Blazor starts, so it runs before Blazor's delegated click
    // handler and a drag that ends over a button does not activate the button. `stopImmediatePropagation` also
    // stops other capture-phase listeners on the document.
    document.addEventListener('click', function (e) {
        if (!swallowNextClick)
            return;

        swallowNextClick = false;
        e.stopImmediatePropagation();
        e.preventDefault();
    }, true);

    // Enter or Space on a focused button fires a click with no pointer event before it. Any key press clears
    // swallowNextClick, so a flag left by a drag that fired no click does not swallow that keyboard click.
    document.addEventListener('keydown', function () {
        swallowNextClick = false;
    }, true);

    // A press on a link would otherwise start the browser's native drag-and-drop, which stops the pointer
    // events in the middle of a scroll. The navigation bar and the Home shortcuts are links.
    document.addEventListener('dragstart', function (e) {
        if (surface !== null)
            e.preventDefault();
    }, true);
})();
