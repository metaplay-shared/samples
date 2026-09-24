// Reloads the page when a newer build of the client has been deployed (docs/web-client.md, "Updating clients after
// a deploy", which also says when the reload waits and why).
//
// The page's build ID is the ts-build-id meta tag, which tools/ServerImageBuild.cs stamps when it stages the
// published client. The deployed build ID is the buildId field of build-info.json. A page whose build ID is "dev"
// (the dev server, or a publish that was not staged) never checks. An element with aria-modal="true" counts as an
// open dialog, and the reload waits for it to close because a purchase or a claim may be in progress.
//
// Test knobs: updateBuildId sets the page's build ID, updateCheckMs the check interval, updateIdleMs the idle time.
(function () {
    'use strict';

    var CHECK_INTERVAL_MS = 60000;
    var IDLE_MS = 10000;
    var SAFE_MOMENT_POLL_MS = 250;

    var query = new URLSearchParams(location.search);
    var meta = document.querySelector('meta[name="ts-build-id"]');
    var pageBuildId = query.get('updateBuildId') || (meta ? meta.getAttribute('content') : null);
    if (!pageBuildId || pageBuildId === 'dev')
        return;

    function readQueryMs(name, fallback) {
        var value = parseInt(query.get(name), 10);
        return !isNaN(value) && value >= 0 ? value : fallback;
    }

    var checkIntervalMs = readQueryMs('updateCheckMs', CHECK_INTERVAL_MS);
    var idleMs = readQueryMs('updateIdleMs', IDLE_MS);
    var buildInfoUrl = new URL('build-info.json', document.baseURI).toString();

    var lastInputAt = Date.now();
    var newBuildDeployed = false;
    var checking = false;

    ['pointerdown', 'touchstart', 'keydown', 'wheel'].forEach(function (type) {
        window.addEventListener(type, function () { lastInputAt = Date.now(); }, { capture: true, passive: true });
    });

    function isAtTable() {
        return location.pathname === '/table' || location.pathname.indexOf('/table/') === 0;
    }

    function isSafeMoment() {
        if (isAtTable() || document.querySelector('[aria-modal="true"]'))
            return false;
        return document.hidden || Date.now() - lastInputAt >= idleMs;
    }

    function reloadIfSafe() {
        if (newBuildDeployed && isSafeMoment())
            location.reload();
    }

    function checkForNewBuild() {
        if (checking || newBuildDeployed)
            return;

        checking = true;
        fetch(buildInfoUrl, { cache: 'no-store' })
            .then(function (response) { return response.ok ? response.json() : null; })
            .then(function (info) {
                if (info && typeof info.buildId === 'string' && info.buildId !== pageBuildId)
                    newBuildDeployed = true;
            })
            .catch(function () {
                // A failed check is repeated at the next interval.
            })
            .then(function () {
                checking = false;
                reloadIfSafe();
            });
    }

    setInterval(checkForNewBuild, checkIntervalMs);
    setInterval(reloadIfSafe, SAFE_MOMENT_POLL_MS);

    // Also check when the player returns to the tab, because a deploy may have happened while the tab was hidden.
    document.addEventListener('visibilitychange', function () {
        if (!document.hidden)
            checkForNewBuild();
    });
    window.addEventListener('focus', checkForNewBuild);
})();
