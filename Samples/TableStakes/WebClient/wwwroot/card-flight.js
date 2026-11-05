// Starts the viewer's played card's flight into the trick from where the card was in the hand. Without this, the
// flight starts a fixed distance below the trick (app.css, "Arrival"), so a card from either end of the hand
// jumps sideways to the middle.
//
// Blazor removes the hand card and adds the trick card in the same render, so the hand card cannot be measured
// once the trick card exists. Instead, the card the player is pressing, dragging or has played is measured on
// every animation frame until it leaves the hand, and the last measurement is used.

(function () {
    'use strict';

    var HAND_CARD = '.ts-handcard';
    var OWN_SLOT = '.ts-slot--south';
    var DEAL = '.ts-slot__deal';

    // A measurement older than this is from an earlier play, for example before a reload of the table, and is
    // not used.
    var MAX_AGE_MS = 1500;

    var tracked = null;
    var lastRect = null;
    var lastMeasuredAt = 0;

    function measure() {
        if (!tracked)
            return;
        if (!tracked.isConnected) {
            tracked = null;
            return;
        }
        lastRect = tracked.getBoundingClientRect();
        lastMeasuredAt = performance.now();
        requestAnimationFrame(measure);
    }

    // A press starts a click or a drag, and a click also comes from the keyboard without a press.
    function track(event) {
        var card = event.target instanceof Element ? event.target.closest(HAND_CARD) : null;
        if (!card)
            return;
        var wasTracking = tracked !== null;
        tracked = card;
        if (!wasTracking)
            measure();
    }

    function startFlight(slot) {
        var deal = slot.querySelector(DEAL);
        if (!deal || !lastRect || performance.now() - lastMeasuredAt > MAX_AGE_MS)
            return;
        // The slot is not animated, so its box is where the card lands.
        var target = slot.getBoundingClientRect();
        var dx = lastRect.left + lastRect.width / 2 - (target.left + target.width / 2);
        var dy = lastRect.top + lastRect.height / 2 - (target.top + target.height / 2);
        deal.style.setProperty('--ts-deal-x', dx + 'px');
        deal.style.setProperty('--ts-deal-y', dy + 'px');
        deal.style.setProperty('--ts-deal-scale', String(lastRect.width / target.width));
        lastRect = null;
    }

    document.addEventListener('pointerdown', track, true);
    document.addEventListener('click', track, true);

    new MutationObserver(function (records) {
        for (var i = 0; i < records.length; i++) {
            var added = records[i].addedNodes;
            for (var j = 0; j < added.length; j++) {
                var node = added[j];
                if (!(node instanceof Element))
                    continue;
                var slot = node.matches(OWN_SLOT) ? node : node.querySelector(OWN_SLOT);
                if (slot)
                    startFlight(slot);
            }
        }
    }).observe(document.documentElement, { subtree: true, childList: true });
})();
