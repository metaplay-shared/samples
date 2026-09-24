// Page visibility for the app shell's connection handling.
//
// A browser throttles a hidden tab's timers hard enough that the client's update pump stops keeping the
// link alive, so a lapsed connection around a tab switch is a resume rather than an outage. Only the page
// can tell the client which one it is: the SDK's browser build reports no application lifecycle events.
//
// visibilityState is the signal; focus, blur, pageshow and pagehide are extra sampling points, because
// visibilitychange alone is unreliable on iOS and does not fire for a page restored from the back/forward
// cache.

const EVENTS = ['visibilitychange', 'pageshow', 'pagehide', 'focus', 'blur'];

let subscription = null;

export function subscribe(dotNetRef) {
    unsubscribe();

    const report = () => {
        try {
            dotNetRef.invokeMethodAsync('OnPageVisibilityChanged', document.visibilityState === 'visible');
        } catch {
            // The component is gone; the next unsubscribe removes the listeners.
        }
    };

    for (const name of EVENTS) {
        // visibilitychange is a document event; the rest are window events. Registering each on both is
        // harmless and keeps the two lists from drifting apart.
        document.addEventListener(name, report);
        window.addEventListener(name, report);
    }

    subscription = report;
    report();
}

export function unsubscribe() {
    if (!subscription) {
        return;
    }

    for (const name of EVENTS) {
        document.removeEventListener(name, subscription);
        window.removeEventListener(name, subscription);
    }

    subscription = null;
}
