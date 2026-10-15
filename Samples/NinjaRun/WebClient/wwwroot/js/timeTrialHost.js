/**
 * Drives one time trial from the browser side: an animation frame loop that asks C# to advance the run,
 * hands what comes back to the canvas renderer and the sound, and turns taps and the space bar into button
 * presses.
 *
 * The run itself lives in C# (WebClient/Gameplay/TimeTrial.cs). This file owns no game state. `host` is a
 * .NET object reference whose methods are called synchronously, so a frame is simulated and drawn inside the
 * one animation frame callback rather than a promise later.
 *
 * The renderer and the sound are both loaded as separate modules and both are optional: a missing or broken
 * one leaves the run playable, unpainted or silent, rather than taking the page down with it.
 *
 * Only one run is ever in play, so the module keeps a single active loop.
 */

import { diagnosticsEnabled } from './util.js';

/** Whether accessibility settings ask the app to use the shorter, mostly-static screen cover. */
export { prefersReducedMotion } from './util.js';

let active = null;
let latestSnapshot = null;

/** The sound module, imported once and shared with the menu's tap and mute button. */
let audioModulePromise = null;

/** The renderer module once it has loaded, or null while it has not or would not. */
let rendererModule = null;

/**
 * How long `start` waits for the run's art before going ahead without what has not arrived, in milliseconds.
 * Long enough for a whole course's sheets over a slow mobile link; short enough that a sheet the network
 * never delivers does not hold the run for good.
 */
const ART_WAIT_CAP_MS = 20000;

/**
 * Gate gameplay controls while the whole-screen curtain is moving. The frame loop may keep preparing and
 * drawing behind the opaque cover, but no key or pointer gesture reaches the simulation until reveal ends.
 */
export function setInputEnabled(enabled) {
    if (active)
        active.inputEnabled = enabled;
}

/** Expose the current handoff state for diagnostics and browser-level continuity tests. */
export function isInputEnabled() {
    return active?.inputEnabled ?? false;
}

/** Last frame received from the deterministic host, exposed for browser-level diagnostics and tests. */
export function getLastSnapshot() {
    return latestSnapshot;
}

/** Drive the same host input boundary from deterministic browser diagnostics. */
export function diagnosticPress() {
    if (active && !active.stopped && active.inputEnabled)
        active.host.invokeMethod('Press');
}

/** Drive the matching release from deterministic browser diagnostics. */
export function diagnosticRelease() {
    if (active && !active.stopped && active.inputEnabled)
        active.host.invokeMethod('Release');
}

/**
 * Start playing. Waits for the course's art, then draws the first frame and keeps going until `Frame` hands
 * back nothing, once the run-out past the finish line is over, or `stop` is called.
 *
 * The page calls this behind the covered curtain and starts the reveal when it returns, so the wait for the
 * art is what puts a complete stage under the reveal rather than one whose backdrop and runner arrive during
 * the run-in. The wait is capped (`ART_WAIT_CAP_MS`): past it the run goes ahead and draws what has arrived,
 * as a frame always does.
 *
 * @param {HTMLCanvasElement} canvas the canvas to draw on; also the surface taps are read from
 * @param {object} host .NET object reference exposing Frame(timestampMs, diagnostics), Press() and Release()
 * @param {object} course course geometry: { startX, finishX, ground: [...], pits: [...], hazards: [...] }
 * @param {string} assetBaseUrl where the renderer loads its art from
 * @param {string} [audioBaseUrl] where the sound loads its clips from
 * @param {object} [page] .NET object reference told, through OnRunArtProgress(settled, total), how many of
 *   the run's sheets are in while the art is waited for
 */
export async function start(canvas, host, course, assetBaseUrl, audioBaseUrl, page) {
    stop();
    latestSnapshot = null;

    const state = {
        canvas,
        host,
        renderer: null,
        audio: null,
        frameHandle: 0,
        stopped: false,
        inputEnabled: false,
        diagnostics: diagnosticsEnabled(),
    };
    active = state;

    [state.renderer, state.audio] = await Promise.all([
        createRenderer(canvas, course, assetBaseUrl),
        createAudio(audioBaseUrl, course),
    ]);

    // A run that was stopped while the modules were loading must not start drawing after all.
    if (state.stopped) {
        state.audio?.dispose();
        return;
    }

    await waitForArt(state, page);

    // A run stopped while its art was loading has had its renderer and sound disposed by `stop` already.
    if (state.stopped)
        return;

    // This is reached from the "Start run" click, which is a user gesture, so the audio context may open
    // here. The first press of the run is the fallback for anything that got here without one.
    unlockAudio(state);

    attachInput(state);

    if (state.renderer)
        state.renderer.resize?.();

    state.frameHandle = requestAnimationFrame(frame);
}

/** The course being looked at in the developer course tools, drawn with no run in progress. */
let preview = null;

/**
 * Draw `course` with the run renderer and no runner, from the start line, and keep redrawing it where
 * `previewAt` last put the camera, so the art appears as it loads. Replaces any preview already showing:
 * the one it replaces stops drawing at once but is disposed only once the new renderer has drawn its first
 * frame, so swapping the course on the same canvas never shows an empty frame. Nothing is simulated and no
 * input is read.
 *
 * @param {HTMLCanvasElement} canvas the canvas to draw on
 * @param {object} course course geometry, the same projection a run is started with
 * @param {string} assetBaseUrl where the renderer loads its art from
 * @param {{ pxPerMetre?: number, x?: number, y?: number }} [options] a fixed scale in CSS pixels per metre
 *   (the run's own 12 m across the canvas when absent), and where the camera starts: `x` along the course,
 *   and `y` to hold it at a height instead of following the ground
 */
export async function startPreview(canvas, course, assetBaseUrl, options) {
    const previous = preview;
    if (previous) {
        previous.stopped = true;
        if (previous.frameHandle)
            cancelAnimationFrame(previous.frameHandle);
    }

    const settings = options || {};
    const state = {
        canvas,
        renderer: null,
        x: Number.isFinite(settings.x) ? settings.x : (Number(course && course.startX) || 0),
        y: Number.isFinite(settings.y) ? settings.y : null,
        pxPerMetre: Number.isFinite(settings.pxPerMetre) && settings.pxPerMetre > 0 ? settings.pxPerMetre : 0,
        frameHandle: 0,
        stopped: false,
    };
    preview = state;

    const renderer = await createRenderer(canvas, course, assetBaseUrl, { pxPerMetre: state.pxPerMetre });
    if (state.stopped) {
        renderer?.dispose?.();
        previous?.renderer?.dispose?.();
        return;
    }

    state.renderer = renderer;
    renderer?.resize?.();

    const draw = () => {
        if (state.stopped)
            return;
        try {
            state.renderer?.renderPreview?.(state.x, state.y ?? undefined);
        } catch (err) {
            console.warn('[timeTrialHost] The course preview could not be drawn.', err);
        }
        state.frameHandle = requestAnimationFrame(draw);
    };

    // The first frame is drawn here, in the same task that retires the previous renderer, so the canvas goes
    // straight from the old course to the new one.
    draw();
    previous?.renderer?.dispose?.();
}

/**
 * Move the preview camera to course position `x`, in metres. A finite `y` holds the camera at that height;
 * without one the camera follows the ground under `x`, the way a run's camera settles.
 */
export function previewAt(x, y) {
    if (!preview || !Number.isFinite(x))
        return;
    preview.x = x;
    preview.y = Number.isFinite(y) ? y : null;
}

/**
 * Draw the preview at a fixed `pxPerMetre` CSS pixels per metre from now on, or at the run's own scale for
 * 0. A course handed over later keeps the scale only if `startPreview` is given it again.
 */
export function setPreviewScale(pxPerMetre) {
    if (!preview)
        return;
    preview.pxPerMetre = Number.isFinite(pxPerMetre) && pxPerMetre > 0 ? pxPerMetre : 0;
    preview.renderer?.setPixelsPerMetre?.(preview.pxPerMetre);
}

/**
 * The camera and scale the preview's last frame was drawn with (see the renderer's `previewProjection`), or
 * null while nothing is drawn. With `canvas`, null unless the preview is drawing on that canvas.
 */
export function previewProjection(canvas) {
    if (!preview || !preview.renderer || (canvas && preview.canvas !== canvas))
        return null;
    try {
        return preview.renderer.previewProjection?.() ?? null;
    } catch {
        return null;
    }
}

/**
 * Play the ghost poof and warp-in once at the torii they belong to in the preview (see the renderer's
 * `playGhostEffects`), on the renderer's own clock. With `canvas`, only a preview drawing on that canvas plays
 * them. Nothing a run draws is touched.
 */
export function playPreviewGhostEffects(canvas) {
    if (!preview || !preview.renderer || (canvas && preview.canvas !== canvas))
        return;
    try {
        preview.renderer.playGhostEffects?.();
    } catch (err) {
        console.warn('[timeTrialHost] The ghost effects could not be played.', err);
    }
}

/**
 * Play every decoration's pass-by animation once in the preview (see the renderer's `playDecorationPassBy`),
 * the sequence a run plays when the runner reaches one. With `canvas`, only a preview drawing on that canvas
 * plays them.
 */
export function playPreviewDecorationPassBy(canvas) {
    if (!preview || !preview.renderer || (canvas && preview.canvas !== canvas))
        return;
    try {
        preview.renderer.playDecorationPassBy?.();
    } catch (err) {
        console.warn('[timeTrialHost] The pass-by animations could not be played.', err);
    }
}

/**
 * Stop drawing the preview and dispose its renderer. Safe to call when nothing is being previewed. With
 * `canvas`, only a preview drawing on that canvas is stopped.
 */
export function stopPreview(canvas) {
    const state = preview;
    if (!state || (canvas && state.canvas !== canvas))
        return;

    preview = null;
    state.stopped = true;
    if (state.frameHandle)
        cancelAnimationFrame(state.frameHandle);
    state.renderer?.dispose?.();
}

/**
 * Stop the loop, release the input handlers and dispose the renderer and the sound. Safe to call when
 * nothing is running.
 */
export function stop() {
    const state = active;
    if (!state)
        return;

    active = null;
    state.stopped = true;

    if (state.frameHandle)
        cancelAnimationFrame(state.frameHandle);

    detachInput(state);

    if (state.renderer)
        state.renderer.dispose?.();

    if (state.audio) {
        try {
            state.audio.dispose();
        } catch (err) {
            console.warn('[timeTrialHost] The run audio could not be shut down.', err);
        }
    }
}

/**
 * Play the menu tick, from a click on a menu button. The click is also the gesture the audio context is
 * opened on, so the first run starts with its clips already decoded.
 *
 * @returns {Promise<void>}
 */
export async function playMenuTap() {
    const module = await loadAudioModule();
    try {
        module?.playMenuTap();
    } catch (err) {
        console.warn('[timeTrialHost] The menu tap could not be played.', err);
    }
}

/**
 * Whether sound is switched off, so the mute button can be labelled before anything has played.
 *
 * @returns {Promise<boolean>}
 */
export async function isMuted() {
    const module = await loadAudioModule();
    try {
        return module ? module.isMuted() : false;
    } catch {
        return false;
    }
}

/**
 * Flip the mute switch, which is remembered across visits.
 *
 * @returns {Promise<boolean>} the flag as it now stands
 */
export async function toggleMuted() {
    const module = await loadAudioModule();
    try {
        return module ? module.toggleMuted() : false;
    } catch (err) {
        console.warn('[timeTrialHost] The mute switch could not be flipped.', err);
        return false;
    }
}

function frame(timestampMs) {
    const state = active;
    if (!state || state.stopped)
        return;

    // The run's clock: the frame timestamps, slowed by the renderer's slow-motion knob
    // (`hsrRunnerTuning.timeScale`, read through the renderer's `timeScale`) when one is set.
    const scale = rendererModule !== null ? rendererModule.timeScale() : 1;
    state.runClockMs = typeof state.lastTimestampMs === 'number'
        ? state.runClockMs + (timestampMs - state.lastTimestampMs) * scale
        : timestampMs;
    state.lastTimestampMs = timestampMs;

    let snapshot;
    try {
        // The frame arrives as JSON text, serialized on the .NET side.
        const frameJson = state.host.invokeMethod('Frame', state.runClockMs, state.diagnostics);
        snapshot = frameJson ? JSON.parse(frameJson) : null;
    } catch (err) {
        console.error('[timeTrialHost] The run could not be advanced.', err);
        stop();
        return;
    }

    // No snapshot means the run has nothing further to show.
    if (!snapshot) {
        stop();
        return;
    }

    latestSnapshot = snapshot;
    if (state.diagnostics)
        publishDiagnostics(state.canvas, snapshot);

    if (state.renderer)
        state.renderer.render(snapshot);

    if (state.audio) {
        try {
            state.audio.onSnapshot(snapshot);
        } catch (err) {
            // A run that cannot make a noise still has to reach the finish line.
            console.warn('[timeTrialHost] The run audio failed and is switched off for this run.', err);
            state.audio = null;
        }
    }

    // A finished run keeps drawing: the runner runs on past the line until C# stops handing back snapshots,
    // which is what ends the loop above.
    state.frameHandle = requestAnimationFrame(frame);
}

/**
 * Publish the frame's snapshot on the canvas as data attributes, which is how the end-to-end tests read the
 * run. Called only on a page the tests opened (see `diagnosticsEnabled` in `util.js`).
 */
function publishDiagnostics(canvas, snapshot) {
    canvas.dataset.runnerX = String(snapshot?.x ?? 0);
    canvas.dataset.runnerVx = String(snapshot?.vx ?? 0);
    canvas.dataset.runnerMode = typeof snapshot?.mode === 'string' ? snapshot.mode : '';
    // Whether the finish line is behind the runner: true on the crossing and through the run-out after it.
    canvas.dataset.finished = snapshot?.finished === true ? 'true' : 'false';
    canvas.dataset.inputLocked = snapshot?.inputLocked === true ? 'true' : 'false';
    // Whether the field is still running in to the start line, before the clock starts.
    canvas.dataset.runIn = snapshot?.runIn === true ? 'true' : 'false';
}

/**
 * Wait for the run's art (see the renderer's `whenReady`), for at most `ART_WAIT_CAP_MS`, telling `page` how
 * it is coming in. Never rejects: a renderer that cannot say, or an art load that fails, leaves the run to
 * draw what it has.
 */
function waitForArt(state, page) {
    const renderer = state.renderer;
    if (!renderer || typeof renderer.whenReady !== 'function')
        return Promise.resolve();

    const report = (settled, total) => {
        if (!page || state.stopped)
            return;
        try {
            const result = page.invokeMethodAsync('OnRunArtProgress', settled, total);
            if (result && typeof result.catch === 'function')
                result.catch(() => { /* The page has gone; there is nobody left to tell. */ });
        } catch {
            // Same: a page that cannot be reached is not told.
        }
    };

    let capHandle = 0;
    const cap = new Promise((resolve) => {
        capHandle = window.setTimeout(() => {
            console.warn(`[timeTrialHost] The run's art was still loading after ${ART_WAIT_CAP_MS} ms; the run goes ahead with what has arrived.`);
            resolve();
        }, ART_WAIT_CAP_MS);
    });
    const ready = renderer.whenReady(report).catch((err) => {
        console.warn('[timeTrialHost] The run\'s art could not be waited for; the run goes ahead with what has arrived.', err);
    });

    return Promise.race([ready, cap]).finally(() => window.clearTimeout(capHandle));
}

/**
 * The renderer is drawn from its own module so the game view and the art can move independently. A missing
 * or broken renderer leaves the run playable and unpainted rather than taking the page down with it.
 */
async function createRenderer(canvas, course, assetBaseUrl, extraOptions) {
    try {
        const module = await import('./timeTrialRenderer.js');
        rendererModule = module;
        return module.createTimeTrialRenderer(canvas, course, Object.assign({ assetBaseUrl }, extraOptions || {}));
    } catch (err) {
        console.warn('[timeTrialHost] No canvas renderer available; the run plays unpainted.', err);
        return null;
    }
}

/**
 * The sound for this run, or null if the module will not load. Either way the run is played. The course's
 * pits go with it: a touchdown on the floor of one is a heavier landing than a touchdown on the ground, and
 * the snapshot alone does not say which it was.
 */
async function createAudio(audioBaseUrl, course) {
    const module = await loadAudioModule();
    if (!module)
        return null;

    try {
        return module.createTimeTrialAudio({
            baseUrl: audioBaseUrl || 'audio/',
            pits: Array.isArray(course && course.pits) ? course.pits : [],
        });
    } catch (err) {
        console.warn('[timeTrialHost] No audio available; the run plays silent.', err);
        return null;
    }
}

/** The sound module itself, imported once. A failed import is remembered as "no sound", not retried. */
function loadAudioModule() {
    if (!audioModulePromise) {
        audioModulePromise = import('./timeTrialAudio.js').catch((err) => {
            console.warn('[timeTrialHost] No audio module available; the client stays silent.', err);
            return null;
        });
    }

    return audioModulePromise;
}

/** Open the audio context from whatever gesture we are inside, never letting a refusal reach the run. */
function unlockAudio(state) {
    try {
        state.audio?.unlock();
    } catch (err) {
        console.warn('[timeTrialHost] The audio context could not be opened.', err);
    }
}

/**
 * The whole game area is the jump button: a touch or click anywhere on it presses, letting go releases. The
 * space bar does the same on a desktop keyboard, ignoring the auto-repeat a held key produces.
 *
 * A held touch is a jump charge, never a long-press. The canvas' non-passive `touchstart` listener cancels
 * the touch sequence's default handling, which is what keeps the platform's long-press gesture (and the
 * haptic buzz Android gives when it recognises one), text selection and the touch callout from firing on
 * it; `touch-action` and a cancelled `contextmenu` only act after that gesture has already been
 * recognised. Cancelling `touchstart` suppresses only the compatibility mouse events, so the pointer events
 * that press and release still arrive. A touch `contextmenu` is deliberately left uncancelled: Chrome on
 * Android buzzes whenever the page consumes a long-press, and cancelling the menu counts as consuming it.
 * The mouse's right-click menu is cancelled page-wide in `index.html`.
 *
 * The first press is also the gesture the browser wants before it will let anything be heard.
 */
function attachInput(state) {
    const press   = () => { if (!state.stopped && state.inputEnabled) state.host.invokeMethod('Press'); };
    const release = () => { if (!state.stopped && state.inputEnabled) state.host.invokeMethod('Release'); };

    state.onPointerDown = (event) => {
        event.preventDefault();
        state.canvas.setPointerCapture?.(event.pointerId);
        unlockAudio(state);
        press();
    };
    state.onPointerUp = () => release();

    // Cancelling the touch start is enough for the whole hold: the browser never starts its gesture
    // recogniser for that touch, and `touch-action: none` already keeps the moves from scrolling.
    state.onTouchStart = (event) => event.preventDefault();

    state.onKeyDown = (event) => {
        if (event.code !== 'Space' || event.repeat)
            return;
        event.preventDefault();
        unlockAudio(state);
        press();
    };
    state.onKeyUp = (event) => {
        if (event.code !== 'Space')
            return;
        event.preventDefault();
        release();
    };

    state.onResize = () => { if (state.renderer) state.renderer.resize?.(); };

    state.canvas.addEventListener('pointerdown', state.onPointerDown);
    state.canvas.addEventListener('touchstart', state.onTouchStart, { passive: false });
    // Released on the window, so a finger that slides off the canvas before lifting still ends the charge.
    window.addEventListener('pointerup', state.onPointerUp);
    window.addEventListener('pointercancel', state.onPointerUp);
    window.addEventListener('keydown', state.onKeyDown);
    window.addEventListener('keyup', state.onKeyUp);
    window.addEventListener('resize', state.onResize);
}

function detachInput(state) {
    if (!state.onPointerDown)
        return;

    state.canvas.removeEventListener('pointerdown', state.onPointerDown);
    state.canvas.removeEventListener('touchstart', state.onTouchStart, { passive: false });
    window.removeEventListener('pointerup', state.onPointerUp);
    window.removeEventListener('pointercancel', state.onPointerUp);
    window.removeEventListener('keydown', state.onKeyDown);
    window.removeEventListener('keyup', state.onKeyUp);
    window.removeEventListener('resize', state.onResize);
}
