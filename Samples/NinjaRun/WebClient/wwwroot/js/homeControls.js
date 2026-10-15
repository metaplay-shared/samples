/**
 * The browser-level controls of the main page (Home.razor) that no single element owns: the keyboard
 * shortcuts of the menu and the score screen, the browser back button while a view on a history entry of its
 * own, the customize view or a run, is open, and fullscreen. Home.razor owns every decision, through
 * Services/HomeControlsInterop.cs; this module only listens on the window and the document and calls back into
 * it through a .NET object reference:
 *
 * - `OnMenuStartKey()`: Space on the main menu, which opens a run like the Start button.
 * - `OnResultDismissKey()`: Space or Enter on the score screen, the same as a press on it: on to the
 *   standing, or back to the menu from the last panel.
 * - `OnHistoryLeftView(view)`: the history entry the open `view` sits on was left: browser back, the Android
 *   back gesture, or one of the view's own ways out, which go through `history.back()`: the customize view's
 *   Back button, and the run's exit button and a press on its score screen. A run's entry covers the run and
 *   its score screen alike.
 * - `OnHistoryEnteredView(view)` → bool: browser forward landed on an entry of a `view` that reopens from the
 *   history, with the view closed; false when Home cannot open the view now, in which case the entry is
 *   stepped back off again.
 * - `OnFullscreenChanged(isFullscreen)`: the document entered or left fullscreen, however it happened.
 *
 * The views on entries of their own never share the history: a run cannot open from the customize view and
 * the customize view cannot open during a run, so at most one entry is live at a time. Browser forward onto a
 * run entry that is no longer live never reopens a run; the entry is stepped back off.
 *
 * Only one page instance is ever attached; attaching again replaces the previous owner.
 */

/** The owning page, or null while nothing is attached. */
let owner = null;

/**
 * What the page is showing, as far as the keys are concerned: 'menu' (Space starts a run), 'result' (Space
 * or Enter dismisses the score screen) or 'none' (the keys are left alone).
 */
let keyMode = 'none';

/** The keys physically down right now, by `KeyboardEvent.code`. */
const heldKeys = new Set();

/**
 * The keys that were already down when the current key mode began. Each is ignored until it has been
 * released, so a Space held through the finish line cannot dismiss the score screen it lands on, and a key
 * held through a view change never acts on the view it lands on.
 */
let staleKeys = new Set();

/** The keys whose keydown acted here: their keyup is cancelled too, so a focused button never clicks on it. */
const swallowedKeyUps = new Set();

/**
 * Whether focus was last moved with the keyboard (Tab) rather than a pointer. A control reached with Tab
 * keeps its native Space and Enter; one that only holds focus because it was clicked does not.
 */
let focusByKeyboard = false;

/**
 * Start listening for `dotNetOwner`. Returns whether this browser can go fullscreen at all, which is what
 * decides whether the fullscreen button is shown.
 */
export function attach(dotNetOwner) {
    detach();
    owner = dotNetOwner;

    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', onWindowBlur);
    window.addEventListener('pointerdown', onPointerDown, true);
    window.addEventListener('popstate', onPopState);
    document.addEventListener('fullscreenchange', onFullscreenChange);

    return isFullscreenEnabled();
}

/** Stop listening and forget the owner. Safe to call when nothing is attached. */
export function detach() {
    if (!owner)
        return;

    owner = null;
    keyMode = 'none';
    delete document.documentElement.dataset.homeKeyMode;
    heldKeys.clear();
    staleKeys = new Set();
    swallowedKeyUps.clear();

    window.removeEventListener('keydown', onKeyDown);
    window.removeEventListener('keyup', onKeyUp);
    window.removeEventListener('blur', onWindowBlur);
    window.removeEventListener('pointerdown', onPointerDown, true);
    window.removeEventListener('popstate', onPopState);
    document.removeEventListener('fullscreenchange', onFullscreenChange);
}

// ---------------- Keys ----------------

/**
 * Say what the page is showing. A change of mode marks every key still held as stale, so only a press that
 * begins in the new mode acts in it. The mode is also written to `<html data-home-key-mode>` for diagnostics
 * and browser tests.
 */
export function setKeyMode(mode) {
    if (mode === keyMode)
        return;
    keyMode = mode;
    staleKeys = new Set(heldKeys);
    document.documentElement.dataset.homeKeyMode = mode;
}

function onKeyDown(event) {
    const code = event.code;
    heldKeys.add(code);

    if (code === 'Tab')
        focusByKeyboard = true;

    // An auto-repeat, or a key that was already down when this mode began, is not a new press.
    if (event.repeat || staleKeys.has(code))
        return;
    if (!owner || event.defaultPrevented || event.ctrlKey || event.altKey || event.metaKey)
        return;

    const acts = keyMode === 'menu'
        ? code === 'Space'
        : keyMode === 'result' && (code === 'Space' || code === 'Enter' || code === 'NumpadEnter');
    if (!acts)
        return;

    const target = event.target instanceof Element ? event.target : null;
    if (target && isTextEntry(target))
        return;
    if (target && keyMode === 'result' && target.closest('[data-testid="run-result"]'))
        return; // The focused score screen answers its own keys (Components/Result/ScoreScreen.razor's OnKeyDown).
    if (target && focusByKeyboard && isControl(target))
        return; // A control reached with Tab keeps its native activation.

    event.preventDefault();
    swallowedKeyUps.add(code);
    owner.invokeMethodAsync(keyMode === 'menu' ? 'OnMenuStartKey' : 'OnResultDismissKey').catch(() => {
        // The page has gone away; there is nothing left to act on.
    });
}

function onKeyUp(event) {
    const code = event.code;
    heldKeys.delete(code);
    staleKeys.delete(code);

    // The keydown already acted: a focused button must not also click on the release.
    if (swallowedKeyUps.delete(code))
        event.preventDefault();
}

/** A window that loses focus never hears the releases, so nothing is left counted as held. */
function onWindowBlur() {
    heldKeys.clear();
    staleKeys = new Set();
    swallowedKeyUps.clear();
}

function onPointerDown() {
    focusByKeyboard = false;
}

/** Whether typing into `element` is what the key is for: a text field, a text area or editable content. */
function isTextEntry(element) {
    if (element.isContentEditable)
        return true;
    const tag = element.tagName;
    if (tag === 'TEXTAREA' || tag === 'SELECT')
        return true;
    if (tag !== 'INPUT')
        return false;
    const type = (element.getAttribute('type') || 'text').toLowerCase();
    return !['button', 'checkbox', 'radio', 'range', 'reset', 'submit', 'image', 'color', 'file'].includes(type);
}

/** Whether `element` is, or sits in, something Space or Enter would activate natively. */
function isControl(element) {
    return element.closest('button, a[href], input, summary, [role="button"], [role="link"], [tabindex]') !== null;
}

// ---------------- Browser back for the views on history entries of their own ----------------

/**
 * The views that sit on a history entry of their own, by the name the page passes, each with the key in
 * `history.state` its entries are marked with (the value is that entry's own token) and whether browser
 * forward onto one of its entries opens the view again. A run is never reopened from the history; the
 * customize view is.
 */
const VIEW_ENTRIES = {
    run: { marker: 'hsrRun', reopens: false },
    customize: { marker: 'hsrCustomize', reopens: true },
};

/**
 * The entry the open view sits on, or null while no view is on one: `{ view, token, leavePending }`, where
 * `leavePending` says a step back off it has been asked for and its `popstate` has not arrived yet, so a
 * second way out pressed in that gap does not step back a second time, which would leave the page.
 */
let live = null;

/** A fresh token for a history entry, so an entry left behind is never mistaken for the live one. */
function newToken() {
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

/**
 * Put an entry for `view` on the history as it starts opening: the same URL, the current state kept (so
 * Blazor's own navigation index is unchanged) and a token of its own. Browser back from it leaves the view.
 */
export function pushViewEntry(view) {
    const spec = VIEW_ENTRIES[view];
    if (!spec)
        return;
    const token = newToken();
    const state = Object.assign({}, history.state ?? {}, { [spec.marker]: token });
    history.pushState(state, '', location.href);
    live = { view, token, leavePending: false };
}

/**
 * One of `view`'s own ways out. When the view sits on its entry this steps back off it, and the resulting
 * `popstate` is what leaves the view, exactly as browser back does; returns true, and keeps returning true
 * until that `popstate` has arrived. Returns false when there is no entry to step off, and the page leaves the
 * view itself.
 */
export function leaveViewEntry(view) {
    if (!live || live.view !== view)
        return false;
    if (live.leavePending)
        return true;
    if (history.state?.[VIEW_ENTRIES[view].marker] === live.token) {
        live.leavePending = true;
        history.back();
        return true;
    }
    live = null;
    return false;
}

function onPopState(event) {
    if (!owner)
        return;

    if (live) {
        if (event.state?.[VIEW_ENTRIES[live.view].marker] === live.token)
            return;
        const left = live.view;
        live = null;
        owner.invokeMethodAsync('OnHistoryLeftView', left).catch(() => { });
        return;
    }

    for (const [view, spec] of Object.entries(VIEW_ENTRIES)) {
        const token = event.state?.[spec.marker] ?? null;
        if (token === null)
            continue;

        // Forward onto the entry of a view that is no longer open. A run is never reopened from the history,
        // so its entry is stepped back off; another view is opened on it again, or, when the page cannot open
        // it now, its entry is stepped back off. Either way the history keeps matching the screen.
        if (!spec.reopens) {
            history.back();
            return;
        }

        live = { view, token, leavePending: false };
        owner.invokeMethodAsync('OnHistoryEnteredView', view).then((opened) => {
            if (!opened && live?.token === token) {
                live = null;
                history.back();
            }
        }).catch(() => { });
        return;
    }
}

// ---------------- Fullscreen ----------------

/** Whether the browser lets the page go fullscreen. iPhone Safari does not. */
function isFullscreenEnabled() {
    return document.fullscreenEnabled === true && typeof document.documentElement.requestFullscreen === 'function';
}

/** Whether the document is fullscreen now. */
export function isFullscreen() {
    return !!document.fullscreenElement;
}

/**
 * Enter fullscreen with the whole document, or leave it. Called from the button's click, which is the user
 * gesture the browser asks for. Returns whether the document is fullscreen afterwards.
 */
export async function toggleFullscreen() {
    try {
        if (document.fullscreenElement)
            await document.exitFullscreen();
        else
            await document.documentElement.requestFullscreen({ navigationUI: 'hide' });
    } catch (err) {
        console.warn('[homeControls] Fullscreen could not be switched.', err);
    }
    return isFullscreen();
}

function onFullscreenChange() {
    owner?.invokeMethodAsync('OnFullscreenChanged', isFullscreen()).catch(() => { });
}
