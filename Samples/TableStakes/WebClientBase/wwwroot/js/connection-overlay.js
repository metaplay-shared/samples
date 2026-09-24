// Reports page visibility to ConnectionOverlay.razor.
//
// A browser throttles the timers of a hidden tab so much that the client's update loop stops keeping the
// connection alive. A connection lost around a tab switch is then a resume, not an outage. The SDK's browser
// build reports no application lifecycle events, so this script reports visibility instead.
//
// The reported value is always document.visibilityState. The focus, blur, pageshow and pagehide events are
// extra points at which to read it, because visibilitychange alone is unreliable on iOS and does not fire for
// a page restored from the back/forward cache.

const VISIBILITY_EVENTS = ['visibilitychange', 'pageshow', 'pagehide', 'focus', 'blur'];

let subscribedListener = null;

export function subscribe(dotNetRef) {
    unsubscribe();

    const report = () => {
        try {
            dotNetRef.invokeMethodAsync('OnPageVisibilityChanged', document.visibilityState === 'visible');
        } catch {
            // The component has been disposed. Its DisposeAsync calls unsubscribe, which removes the listeners.
        }
    };

    for (const name of VISIBILITY_EVENTS) {
        // visibilitychange fires on the document and the other events on the window. Each is registered on
        // both, so a single list is enough.
        document.addEventListener(name, report);
        window.addEventListener(name, report);
    }

    subscribedListener = report;
    report();
}

export function unsubscribe() {
    if (!subscribedListener) {
        return;
    }

    for (const name of VISIBILITY_EVENTS) {
        document.removeEventListener(name, subscribedListener);
        window.removeEventListener(name, subscribedListener);
    }

    subscribedListener = null;
}
