// Publishes the screen positions that the reward coin burst animation flies between, as CSS custom properties
// on each phone frame element. C# plans the sprites (WalletBurstPlan, WalletBurstLayer) and CSS animates them
// (meta-shell.css). The positions must be measured in the browser, because a chip moves when its balance gains a digit,
// when the player's name changes width, and when the frame is resized. The observers are document-level because
// Blazor re-renders the elements often.
//
// It also publishes the reduced-motion preference as window.tsReducedMotion. CSS disables the animations under
// that preference, and the HUD must then also skip holding back the new balance (Integration/MotionPreference.cs),
// or the player sees the old balance with no animation.

(function () {
    'use strict';

    // A phone frame. It is the containing block for the fixed-position burst layer inside it (app.css, "The
    // phone frame"), so all coordinates are relative to the frame's padding box.
    //
    // The component gallery draws several frames on one page, each with its own burst layer. The coordinates
    // are therefore set on each frame element, not on the document.
    var FRAME = '[data-app-frame]';

    // A balance chip. The attribute value is the currency: the C# enum member name in lower case.
    var TARGET = '[data-wallet-target]';

    // The row of reward items in the reveal, where the sprites start. The row is removed shortly after the
    // burst starts, and the last published position is kept after that.
    var SOURCE = '[data-wallet-source]';

    // One item in the reward row. The attribute value is its currency. Each currency's sprites start from the
    // centre of that currency's item. The row centre is the fallback for a currency whose item was not measured.
    var ITEM = '[data-wallet-item]';

    // Start position when no reveal has been measured: horizontally centred, at this fraction of the frame
    // height, which is about where the reveal's items are.
    var FALLBACK_ORIGIN_Y = 0.46;

    var scheduled = false;
    var watchedElements = null;

    // Schedules publish() to run at most once per animation frame. Every event handler here calls this instead
    // of measuring directly, because the MutationObserver fires on every re-render.
    function schedulePublish() {
        if (scheduled)
            return;

        scheduled = true;
        requestAnimationFrame(function () {
            scheduled = false;
            publish();
        });
    }

    function publish() {
        var frames = document.querySelectorAll(FRAME);
        for (var f = 0; f < frames.length; f++)
            publishFrame(frames[f]);
    }

    function publishFrame(frame) {
        // Measure against the padding box, not the border box. The frame draws its bezel as a border, and the
        // containing block of a fixed element excludes the border. The border widths are read from the element,
        // because the app's frame and the gallery's frames have different bezels.
        var box = frame.getBoundingClientRect();
        var style = window.getComputedStyle(frame);
        var left = box.left + (parseFloat(style.borderLeftWidth) || 0);
        var top = box.top + (parseFloat(style.borderTopWidth) || 0);
        var width = frame.clientWidth;
        var height = frame.clientHeight;

        if (width === 0 || height === 0)
            return;

        var source = frame.querySelector(SOURCE);
        if (source) {
            var from = source.getBoundingClientRect();
            setPxProperty(frame, '--m-fx-origin-x', from.left - left + from.width / 2);
            setPxProperty(frame, '--m-fx-origin-y', from.top - top + from.height / 2);

            var items = source.querySelectorAll(ITEM);
            for (var n = 0; n < items.length; n++) {
                var item = items[n];
                var itemCurrency = item.getAttribute('data-wallet-item');
                if (!itemCurrency)
                    continue;

                var itemBox = item.getBoundingClientRect();
                setPxProperty(frame, '--m-fx-origin-' + itemCurrency + '-x', itemBox.left - left + itemBox.width / 2);
                setPxProperty(frame, '--m-fx-origin-' + itemCurrency + '-y', itemBox.top - top + itemBox.height / 2);
            }
        } else if (!frame.style.getPropertyValue('--m-fx-origin-x')) {
            setPxProperty(frame, '--m-fx-origin-x', width / 2);
            setPxProperty(frame, '--m-fx-origin-y', height * FALLBACK_ORIGIN_Y);
        }

        var targets = frame.querySelectorAll(TARGET);
        for (var i = 0; i < targets.length; i++) {
            var target = targets[i];
            var currency = target.getAttribute('data-wallet-target');
            if (!currency)
                continue;

            var rect = target.getBoundingClientRect();
            setPxProperty(frame, '--m-fx-' + currency + '-x', rect.left - left + rect.width / 2);
            setPxProperty(frame, '--m-fx-' + currency + '-y', rect.top - top + rect.height / 2);

            watchSize(target);
        }
    }

    function setPxProperty(root, name, px) {
        root.style.setProperty(name, px.toFixed(1) + 'px');
    }

    // Re-measures when a chip's size changes, for example when a balance gains a digit. No resize or
    // mutation event reports that change.
    var sizeWatcher = window.ResizeObserver ? new ResizeObserver(schedulePublish) : null;

    function watchSize(element) {
        if (!sizeWatcher)
            return;

        if (!watchedElements)
            watchedElements = new WeakSet();

        if (!watchedElements.has(element)) {
            watchedElements.add(element);
            sizeWatcher.observe(element);
        }
    }

    // Re-measures when elements are added or removed, because the chips and the reveal appear and disappear:
    // the table has no HUD, the reveal is shown briefly, and a reconnect rebuilds the whole tree.
    if (window.MutationObserver) {
        new MutationObserver(function (records) {
            for (var i = 0; i < records.length; i++) {
                if (records[i].addedNodes.length > 0 || records[i].removedNodes.length > 0) {
                    schedulePublish();
                    return;
                }
            }
        }).observe(document.documentElement, { childList: true, subtree: true });
    }

    window.addEventListener('resize', schedulePublish);
    window.addEventListener('orientationchange', schedulePublish);
    document.addEventListener('DOMContentLoaded', schedulePublish);
    window.addEventListener('load', schedulePublish);

    // Publishes the reduced-motion preference. The app reads it once per reward claim, so a change during the
    // session applies from the next claim.
    var reducedMotionQuery = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;

    function publishReducedMotion() {
        window.tsReducedMotion = !!(reducedMotionQuery && reducedMotionQuery.matches);
    }

    if (reducedMotionQuery && reducedMotionQuery.addEventListener)
        reducedMotionQuery.addEventListener('change', publishReducedMotion);

    publishReducedMotion();
    schedulePublish();
})();
