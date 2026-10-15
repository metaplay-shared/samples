/**
 * The part editor's canvas (WebClient/Components/Dev/DevPartEditor.razor): the draft composed alone, drawn
 * by the run renderer through `timeTrialHost.js`'s preview, with a transparent overlay canvas stacked over it
 * that draws the draft's handles and reads the mouse and keyboard.
 *
 * The draft lives in C#. `setDraft` hands this module a copy of its geometry (ground, spike beds and
 * bridges in whole metres along the part and whole decimetres of height, and decorations in whole decimetres
 * along it) and every edit made here goes back
 * through the page's .NET object reference: a drag redraws the overlay's own copy every frame, reports its
 * rows about every 100 ms while it lasts (`live`), and reports them once more when the button is let go
 * (`final`), which is when the page composes the draft again and hands the renderer the new course with
 * `setCourse`. The camera (position, height and scale) is this module's, and survives a new course.
 *
 * The pits the overlay outlines (each gap's depth and whether it is jumped, poor either way or run into, and
 * each pit floor's depth) are the page's (`CoursePartDraftReview.GapsOf`), handed over with the draft. While a
 * drag owns the rows, `setGaps` hands them over again for each live report, so they trail the dragged rows by
 * that report's round trip.
 *
 * For a part alone, part-local X is course X and local Y=0 is course Y=0, so the overlay places the draft on
 * the renderer's own projection (`previewProjection`) with no offset.
 *
 * Three tools take the next clicks instead of selecting (`setTool`): `decoration` stands a decoration of the
 * chosen kind on the ground at each click, `vine` hangs a vine at each click, and `space` picks the X the
 * page's add/remove space tool works at. A decoration is drawn by the renderer exactly as a run draws it; the
 * overlay draws its handle, the box its sprite covers, from the same catalogue (`courseDecorations.js`). A vine
 * is drawn by the renderer too; the overlay draws its three handles (the anchor, the rope hanging at its rest
 * angle, and the platform its swing lands on) with the tuned rope's length the page hands over.
 *
 * Only one editor is ever open, so the module keeps a single active one.
 */

import * as host from './timeTrialHost.js';
import { DEFAULTS as RENDERER_DEFAULTS } from './timeTrialRenderer.js';
import { decorationKind, decorationExtent, placeableDecorationKinds } from './courseDecorations.js';

/** The scale range the editor may be zoomed through, in CSS pixels per metre. */
const MIN_PX_PER_METRE = 15;
const MAX_PX_PER_METRE = 80;

/**
 * What a player's portrait stage shows, by the run renderer's own defaults: its span across, a 9 : 19.5 column,
 * the runner and the ground line where the renderer puts them.
 */
const PHONE_METRES_ACROSS = RENDERER_DEFAULTS.metresAcross;
const PHONE_METRES_TALL = PHONE_METRES_ACROSS * 19.5 / 9;
const PHONE_RUNNER_FRACTION = RENDERER_DEFAULTS.runnerScreenFraction;
const PHONE_GROUND_LINE_FRACTION = RENDERER_DEFAULTS.groundLineFraction;

/** How often a drag reports its rows while it lasts, in milliseconds. */
const LIVE_INTERVAL_MS = 100;
/** How long after the last arrow-key nudge the nudged rows are reported as final, in milliseconds. */
const NUDGE_SETTLE_MS = 250;
/** How far a body drag must travel before it is locked to moving along X or changing the height, in pixels. */
const AXIS_LOCK_PX = 5;

/** How close to an edge a press grabs the edge rather than the body, in CSS pixels. */
const EDGE_GRAB_PX = 7;
/** The handle lanes above a stretch's top, in metres: spike beds nearest the ground, bridges above them. */
const SPIKE_LANE = [0, 0.7];
const BRIDGE_LANE = [0.9, 1.7];
/** The box a decoration of a kind the catalogue does not know is handled by, in metres. */
const UNKNOWN_DECORATION_EXTENT = { left: 0.4, right: 0.4, up: 1, down: 0 };
/** The radius a vine's anchor handle is drawn and grabbed at, in CSS pixels. */
const VINE_ANCHOR_PX = 8;
/** How close to a vine's rope a press grabs it, in CSS pixels. */
const VINE_ROPE_GRAB_PX = 6;
/** How thick a vine's platform handle is drawn, in metres below its top. */
const VINE_PLATFORM_METRES = 0.35;
/** How deep below a stretch's top its body can be grabbed, in metres, and never less than this many pixels. */
const GROUND_BAND_METRES = 1.2;
const GROUND_BAND_MIN_PX = 22;

const COLORS = {
    ground: 'rgba(111, 211, 255, 0.95)',
    groundFill: 'rgba(111, 211, 255, 0.10)',
    spike: 'rgba(255, 107, 107, 0.95)',
    spikeFill: 'rgba(255, 107, 107, 0.22)',
    bridge: 'rgba(214, 170, 120, 0.95)',
    bridgeFill: 'rgba(214, 170, 120, 0.22)',
    hover: 'rgba(255, 255, 255, 0.95)',
    selected: 'rgba(255, 209, 102, 1)',
    clearance: 'rgba(120, 200, 255, 0.08)',
    clearanceEdge: 'rgba(120, 200, 255, 0.45)',
    phone: 'rgba(255, 255, 255, 0.75)',
    label: 'rgba(255, 255, 255, 0.92)',
    labelShadow: 'rgba(0, 0, 0, 0.85)',
    ruler: 'rgba(255, 255, 255, 0.55)',
    jump: 'rgba(123, 216, 143, 1)',
    poor: 'rgba(255, 179, 71, 1)',
    run: 'rgba(255, 122, 122, 1)',
    banner: 'rgba(160, 30, 30, 0.85)',
    decorationBehind: 'rgba(180, 150, 255, 0.95)',
    decorationFront: 'rgba(120, 255, 200, 0.95)',
    decorationFill: 'rgba(180, 150, 255, 0.10)',
    unknown: 'rgba(255, 90, 90, 1)',
    space: 'rgba(255, 160, 60, 0.95)',
    spaceFill: 'rgba(255, 160, 60, 0.14)',
    pit: 'rgba(196, 140, 255, 0.95)',
    pitFill: 'rgba(196, 140, 255, 0.14)',
    pitHatch: 'rgba(196, 140, 255, 0.45)',
    vine: 'rgba(140, 230, 110, 0.95)',
    vineFill: 'rgba(140, 230, 110, 0.18)',
    vineReach: 'rgba(140, 230, 110, 0.35)',
};

/** @type {null | object} */
let editor = null;

/**
 * Open the editor on a preview canvas and the overlay stacked over it. The page hands the draft over with
 * `setDraft` and the course with `setCourse` once this returns.
 *
 * @param {HTMLCanvasElement} previewCanvas the canvas the run renderer draws the draft on
 * @param {HTMLCanvasElement} overlayCanvas the transparent canvas over it that reads the mouse
 * @param {object} dotNet the page's .NET object reference: OnSelectionChanged(kind, index),
 *   OnGeometryChanged(kind, index, start, end, height, final), OnElementAdded(kind, start, end, height),
 *   OnGroundPitChanged(index, isPit),
 *   OnElementRemoved(kind, index), OnDecorationPlaced(kind, x, front), OnDecorationChanged(index, x, front, flip,
 *   final), OnVinePlaced(anchorX), OnVineChanged(index, anchorX, anchorHeight, platformStart, platformEnd,
 *   platformHeight, final), OnSpacePicked(x), OnToolChanged(tool) and OnZoomChanged(pxPerMetre)
 * @param {string} [assetBaseUrl] where the renderer loads its art from
 * @param {number} [pxPerMetre] the scale to start at, in CSS pixels per metre
 */
export function attach(previewCanvas, overlayCanvas, dotNet, assetBaseUrl, pxPerMetre) {
    detach();

    const state = {
        preview: previewCanvas,
        overlay: overlayCanvas,
        ctx: overlayCanvas.getContext('2d'),
        dotNet,
        assetBaseUrl: assetBaseUrl || 'art/',
        pxPerMetre: clampScale(Number(pxPerMetre) || 35),
        cameraX: 0,
        cameraY: 0,
        framed: false,
        draft: emptyDraft(),
        course: null,
        selection: null,
        hover: null,
        drag: null,
        pan: null,
        spaceHeld: false,
        tool: '',
        toolKind: '',
        pointer: null,
        lastLiveMs: 0,
        nudgeTimer: 0,
        zoomTimer: 0,
        frameHandle: 0,
        dpr: 1,
        handlesAttr: '',
        stopped: false,
    };
    editor = state;

    overlayCanvas.tabIndex = 0;
    attachInput(state);

    const frame = () => {
        if (state.stopped)
            return;
        try {
            draw(state);
        } catch (err) {
            console.warn('[coursePartEditor] The overlay could not be drawn.', err);
        }
        state.frameHandle = requestAnimationFrame(frame);
    };
    state.frameHandle = requestAnimationFrame(frame);
}

/**
 * Hand the renderer the draft composed alone, keeping the camera where it is. Replaces the course being
 * drawn without clearing the canvas.
 *
 * @param {object} course the course projection a run is started with (`TimeTrialCourseView`)
 */
export async function setCourse(course) {
    const state = editor;
    if (!state || !course)
        return;
    state.course = course;
    ensureFramed(state);
    await host.startPreview(state.preview, course, state.assetBaseUrl, {
        pxPerMetre: state.pxPerMetre,
        x: state.cameraX,
        y: state.cameraY,
    });
}

/**
 * The draft's geometry, the pits the page finds in it, and the page's selection. Ignored while a drag is in
 * progress, which owns the rows until it reports them as final; `setGaps` hands over the pits meanwhile.
 *
 * @param {object} view { ground, spikes, bridges: [{ start, end, height, isPit }], decorations: [{ kind, x, front,
 *   flip }], vines: [{ anchorX, anchorHeight, platformStart, platformEnd, platformHeight }], gaps (as `setGaps`
 *   takes them), vineLength, vineRestAngle, sound, entryClearance, exitClearance, selectedKind, selectedIndex,
 *   spaceX, spaceAmount }. A missing value takes its `emptyDraft` default.
 */
export function setDraft(view) {
    const state = editor;
    if (!state || !view || state.drag)
        return;

    const defaults = emptyDraft();
    state.draft = {
        ground: copyRows(view.ground),
        spikes: copyRows(view.spikes),
        bridges: copyRows(view.bridges),
        decorations: copyDecorations(view.decorations),
        vines: copyVines(view.vines),
        vineLength: coercedFiniteOr(view.vineLength, defaults.vineLength),
        vineRestAngle: coercedFiniteOr(view.vineRestAngle, defaults.vineRestAngle),
        spaceX: Math.round(coercedFiniteOr(view.spaceX, defaults.spaceX)),
        spaceAmount: Math.max(0, Math.round(coercedFiniteOr(view.spaceAmount, defaults.spaceAmount))),
        sound: view.sound === true,
        gaps: copyGaps(view.gaps),
        entryClearance: coercedFiniteOr(view.entryClearance, defaults.entryClearance),
        exitClearance: coercedFiniteOr(view.exitClearance, defaults.exitClearance),
    };

    const kind = typeof view.selectedKind === 'string' ? view.selectedKind : '';
    const index = Number.isInteger(view.selectedIndex) ? view.selectedIndex : -1;
    state.selection = kind && rowsOf(state.draft, kind)[index] ? { kind, index } : null;
    state.hover = null;
    state.overlay.dataset.sound = state.draft.sound ? 'true' : 'false';
}

/**
 * The pits the page finds in the draft as a drag last reported it, taken while the drag owns the rows; they
 * catch up with the rows each time the page answers a live report.
 *
 * @param {object[]} gaps [{ start, end, width, depth, lipY, kind, row, floor }]: the span in whole metres, the
 *   depth and the lower lip's height in metres, `jump`, `poor` or `run`, and the ground row the pit comes before
 *   or, with `floor`, that floors it.
 */
export function setGaps(gaps) {
    const state = editor;
    if (!state || !state.draft)
        return;
    state.draft.gaps = copyGaps(gaps);
}

/**
 * Hand the next clicks on the canvas to a tool: `decoration` stands a `kind` decoration on the ground at each
 * click, `vine` hangs a vine at each click, `space` picks the X the add/remove space tool works at, and an empty
 * tool goes back to selecting.
 */
export function setTool(tool, kind) {
    const state = editor;
    if (!state)
        return;
    state.tool = tool === 'decoration' || tool === 'vine' || tool === 'space' ? tool : '';
    state.toolKind = typeof kind === 'string' ? kind : '';
    state.overlay.dataset.tool = state.tool;
    state.overlay.style.cursor = state.tool ? 'crosshair' : cursorFor(state.hover);
}

/** The decoration kinds a part may place, `[{ id, label, front }]`, from the renderer's catalogue. */
export function decorationKinds() {
    return placeableDecorationKinds();
}

/** Zoom to `pxPerMetre` CSS pixels per metre about the middle of the canvas. */
export function setZoom(pxPerMetre) {
    const state = editor;
    if (!state)
        return;
    const view = viewOf(state);
    zoomAbout(state, Number(pxPerMetre), view.w / 2, view.h / 2, false);
}

/** Frame the whole part across the canvas, from a little before its start to a little past its end. */
export function fit() {
    const state = editor;
    if (!state)
        return;
    frameWholePart(state);
    notifyZoom(state, true);
}

/**
 * Play the ghost effects once in the preview, at the torii they belong to: the poof at the part's end, where
 * the next climb's bottom torii stands in a map, and the warp-in under its start, where the previous climb's
 * gate does.
 */
export function playGhostEffects() {
    const state = editor;
    if (!state)
        return;
    host.playPreviewGhostEffects(state.preview);
}

/**
 * Play every decoration's pass-by animation once in the preview, the sequence a run plays when the runner
 * reaches one.
 */
export function playDecorationPassBy() {
    const state = editor;
    if (!state)
        return;
    host.playPreviewDecorationPassBy(state.preview);
}

/** Close the editor: stop drawing the overlay and the preview, and release the input handlers. */
export function detach() {
    const state = editor;
    if (!state)
        return;

    editor = null;
    state.stopped = true;
    if (state.frameHandle)
        cancelAnimationFrame(state.frameHandle);
    clearTimeout(state.nudgeTimer);
    clearTimeout(state.zoomTimer);
    detachInput(state);
    host.stopPreview(state.preview);
}

// ---------------------------------------------------------------------------
// Geometry
// ---------------------------------------------------------------------------

/** A draft with no rows, and the default every value `setDraft` is not handed takes. */
function emptyDraft() {
    return {
        ground: [], spikes: [], bridges: [], decorations: [], vines: [], vineLength: 6.3, vineRestAngle: 0,
        spaceX: 0, spaceAmount: 0, sound: false, gaps: [], entryClearance: 4, exitClearance: 4,
    };
}

/**
 * `value` converted to a number (a numeric string or null converts too, null to 0) or `fallback` when the
 * conversion is not a finite number.
 */
function coercedFiniteOr(value, fallback) {
    const n = Number(value);
    return Number.isFinite(n) ? n : fallback;
}

function copyRows(rows) {
    return (Array.isArray(rows) ? rows : []).map(row => ({
        start: Math.round(coercedFiniteOr(row && row.start, 0)),
        end: Math.round(coercedFiniteOr(row && row.end, 0)),
        height: Math.round(coercedFiniteOr(row && row.height, 0)),
        pit: row ? row.isPit === true || row.pit === true : false,
    }));
}

function copyGaps(gaps) {
    return (Array.isArray(gaps) ? gaps : []).map(gap => ({
        start: Math.round(coercedFiniteOr(gap && gap.start, 0)),
        end: Math.round(coercedFiniteOr(gap && gap.end, 0)),
        width: Math.round(coercedFiniteOr(gap && gap.width, 0)),
        depth: coercedFiniteOr(gap && gap.depth, 0),
        lipY: coercedFiniteOr(gap && gap.lipY, 0),
        kind: gap && typeof gap.kind === 'string' ? gap.kind : '',
        row: Math.round(coercedFiniteOr(gap && gap.row, -1)),
        floor: gap ? gap.floor === true : false,
    }));
}

function copyDecorations(decorations) {
    return (Array.isArray(decorations) ? decorations : []).map(decoration => ({
        kind: decoration && typeof decoration.kind === 'string' ? decoration.kind : '',
        x: Math.round(coercedFiniteOr(decoration && decoration.x, 0)),
        front: decoration && decoration.front === true,
        flip: decoration && decoration.flip === true,
    }));
}

function copyVines(vines) {
    return (Array.isArray(vines) ? vines : []).map(vine => ({
        anchorX: Math.round(coercedFiniteOr(vine && vine.anchorX, 0)),
        anchorHeight: Math.round(coercedFiniteOr(vine && vine.anchorHeight, 0)),
        platformStart: Math.round(coercedFiniteOr(vine && vine.platformStart, 0)),
        platformEnd: Math.round(coercedFiniteOr(vine && vine.platformEnd, 0)),
        platformHeight: Math.round(coercedFiniteOr(vine && vine.platformHeight, 0)),
    }));
}

function rowsOf(draft, kind) {
    if (kind === 'ground')
        return draft.ground;
    if (kind === 'spike')
        return draft.spikes;
    if (kind === 'bridge')
        return draft.bridges;
    if (kind === 'decoration')
        return draft.decorations;
    if (kind === 'vine')
        return draft.vines;
    return [];
}

function clampScale(value) {
    return Math.min(MAX_PX_PER_METRE, Math.max(MIN_PX_PER_METRE, Number.isFinite(value) ? value : 35));
}

/** Where the part hands over: the end of its last stretch of ground. */
function partLength(draft) {
    return draft.ground.length > 0 ? draft.ground[draft.ground.length - 1].end : 0;
}

/** The stretch of ground `x` stands on, or null over a gap. */
function groundAt(draft, x) {
    for (const segment of draft.ground) {
        if (x >= segment.start && x <= segment.end)
            return segment;
    }
    return null;
}

/** The height of the ground a spike bed or a bridge starting at `x` stands on, in metres. */
function groundTopAt(draft, x) {
    for (const segment of draft.ground) {
        if (x >= segment.start && x < segment.end)
            return segment.height / 10;
    }
    const segment = groundAt(draft, x);
    return segment ? segment.height / 10 : 0;
}

/** Whether a decoration at `x` metres would stand on ground: the rule the part checks it by. */
function standsOnGround(draft, x) {
    return groundAt(draft, x) !== null;
}

/**
 * The screen box a decoration's sprite covers, standing on the ground at its X the way the renderer plants
 * it, and whether its kind is one the catalogue knows.
 */
function decorationBox(state, view, index) {
    const decoration = state.draft.decorations[index];
    const kind = decorationKind(decoration.kind);
    const extent = kind ? decorationExtent(kind, decoration.flip) : UNKNOWN_DECORATION_EXTENT;
    const x = decoration.x / 10;
    const baseY = sy(view, groundTopAt(state.draft, x));
    return {
        decoration,
        known: kind !== null,
        centre: sx(view, x),
        left: sx(view, x - extent.left),
        right: sx(view, x + extent.right),
        top: baseY - extent.up * view.ppm,
        bottom: baseY + extent.down * view.ppm,
        baseY,
    };
}

/**
 * Where a vine's three handles are on the screen: the anchor it hangs from, the free end of the tuned rope
 * hanging at its rest angle, the platform's box, and the dashed reach of its swing, the rope's full length
 * around the anchor.
 */
function vineShape(state, view, index) {
    const vine = state.draft.vines[index];
    const anchorX = vine.anchorX;
    const anchorY = vine.anchorHeight / 10;
    const length = state.draft.vineLength;
    const angle = state.draft.vineRestAngle;
    const platformTop = vine.platformHeight / 10;
    return {
        vine,
        anchor: { x: sx(view, anchorX), y: sy(view, anchorY) },
        ropeEnd: { x: sx(view, anchorX + Math.sin(angle) * length), y: sy(view, anchorY - Math.cos(angle) * length) },
        reachPx: length * view.ppm,
        platform: {
            left: sx(view, vine.platformStart),
            right: sx(view, vine.platformEnd),
            top: sy(view, platformTop),
            bottom: sy(view, platformTop - VINE_PLATFORM_METRES),
        },
    };
}

/** How far (`px`, `py`) is from the segment from `a` to `b`, in pixels. */
function distanceToSegment(px, py, a, b) {
    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const lengthSquared = dx * dx + dy * dy;
    const t = lengthSquared > 0 ? Math.min(1, Math.max(0, ((px - a.x) * dx + (py - a.y) * dy) / lengthSquared)) : 0;
    return Math.hypot(px - (a.x + dx * t), py - (a.y + dy * t));
}

/** Which of a vine's handles is under (`px`, `py`): the anchor, a platform edge, the platform, the rope, or null. */
function hitVine(state, view, index, px, py) {
    const shape = vineShape(state, view, index);
    if (Math.hypot(px - shape.anchor.x, py - shape.anchor.y) <= VINE_ANCHOR_PX + 3)
        return { kind: 'vine', index, part: 'anchor' };
    const platform = shape.platform;
    if (py >= platform.top - 10 && py <= platform.bottom + 4) {
        if (Math.abs(px - platform.left) <= EDGE_GRAB_PX)
            return { kind: 'vine', index, part: 'start' };
        if (Math.abs(px - platform.right) <= EDGE_GRAB_PX)
            return { kind: 'vine', index, part: 'end' };
        if (px >= platform.left && px <= platform.right)
            return { kind: 'vine', index, part: 'platform' };
    }
    if (distanceToSegment(px, py, shape.anchor, shape.ropeEnd) <= VINE_ROPE_GRAB_PX)
        return { kind: 'vine', index, part: 'rope' };
    return null;
}

/** The world-space box a row's handles occupy: its X span and the heights of its lane's top and bottom. */
function laneOf(draft, kind, row) {
    if (kind === 'ground') {
        const top = row.height / 10;
        return { top, bottom: top - GROUND_BAND_METRES };
    }
    const base = groundTopAt(draft, row.start);
    const lane = kind === 'spike' ? SPIKE_LANE : BRIDGE_LANE;
    return { top: base + lane[1], bottom: base + lane[0] };
}

/** The gaps between consecutive stretches of ground, as the page last handed them over. */
function openGaps(draft) {
    return draft.gaps.filter(gap => !gap.floor);
}

/** The pit the flagged ground row at `index` floors, as the page last handed it over, or undefined. */
function floorGap(draft, index) {
    return draft.gaps.find(gap => gap.floor && gap.row === index);
}

// ---------------------------------------------------------------------------
// Projection
// ---------------------------------------------------------------------------

/**
 * The projection the overlay draws and reads the mouse with: the renderer's own scale, runner column and
 * ground line when it has drawn on the preview canvas, and the same formulas over the overlay's size before
 * it has. The camera is always the editor's, which is what it hands the renderer.
 */
function viewOf(state) {
    const w = Math.max(1, state.overlay.clientWidth);
    const h = Math.max(1, state.overlay.clientHeight);
    const projection = host.previewProjection(state.preview);
    const usable = projection && projection.viewW > 1 && projection.viewH > 1;
    return {
        w,
        h,
        ppm: usable ? projection.pxPerMetre : state.pxPerMetre,
        rx: usable ? projection.runnerScreenX : w * PHONE_RUNNER_FRACTION,
        gy: usable ? projection.groundLineY : h * PHONE_GROUND_LINE_FRACTION,
        cx: state.cameraX,
        cy: state.cameraY,
        runCameraY: usable && Number.isFinite(projection.runCameraY) ? projection.runCameraY : state.cameraY,
    };
}

function sx(view, x) {
    return view.rx + (x - view.cx) * view.ppm;
}

function sy(view, y) {
    return view.gy - (y - view.cy) * view.ppm;
}

function worldX(view, px) {
    return view.cx + (px - view.rx) / view.ppm;
}

function worldY(view, py) {
    return view.cy + (view.gy - py) / view.ppm;
}

/** Push the editor's camera to the renderer. */
function applyCamera(state) {
    host.previewAt(state.cameraX, state.cameraY);
}

/** On the first frame the overlay has a size, put the part's start near the left edge at level 0. */
function ensureFramed(state) {
    if (state.framed || state.overlay.clientWidth < 2)
        return;
    state.framed = true;
    const rx = state.overlay.clientWidth * PHONE_RUNNER_FRACTION;
    state.cameraX = -3 + rx / state.pxPerMetre;
    state.cameraY = 0;
    applyCamera(state);
}

function frameWholePart(state) {
    const w = Math.max(1, state.overlay.clientWidth);
    const from = -2;
    const to = partLength(state.draft) + 2;
    const scale = clampScale(w / Math.max(1, to - from));
    setScale(state, scale);
    const rx = w * PHONE_RUNNER_FRACTION;
    state.cameraX = from + rx / scale;
    state.cameraY = 0;
    state.framed = true;
    applyCamera(state);
}

function setScale(state, scale) {
    state.pxPerMetre = clampScale(scale);
    host.setPreviewScale(state.pxPerMetre);
}

/** Zoom to `scale` keeping the course point under (`px`, `py`) where it is on the canvas. */
function zoomAbout(state, scale, px, py, fromWheel) {
    const before = viewOf(state);
    const x = worldX(before, px);
    const y = worldY(before, py);
    setScale(state, scale);
    const after = viewOf(state);
    state.cameraX = x - (px - after.rx) / state.pxPerMetre;
    state.cameraY = y - (after.gy - py) / state.pxPerMetre;
    applyCamera(state);
    notifyZoom(state, !fromWheel);
}

function notifyZoom(state, immediately) {
    clearTimeout(state.zoomTimer);
    const send = () => invoke(state, 'OnZoomChanged', Math.round(state.pxPerMetre));
    if (immediately)
        send();
    else
        state.zoomTimer = setTimeout(send, 150);
}

// ---------------------------------------------------------------------------
// Hit testing
// ---------------------------------------------------------------------------

/**
 * What is under (`px`, `py`): a row's start edge, end edge or body, with spike beds and bridges (whose
 * lanes sit above the ground) before the ground itself. Null over empty background.
 */
function hitTest(state, view, px, py) {
    const decorations = state.draft.decorations;
    for (let index = decorations.length - 1; index >= 0; index--) {
        const box = decorationBox(state, view, index);
        if (px >= box.left - 3 && px <= box.right + 3 && py >= box.top - 3 && py <= box.bottom + 3)
            return { kind: 'decoration', index, part: 'body' };
    }
    const vines = state.draft.vines;
    for (let index = vines.length - 1; index >= 0; index--) {
        const hit = hitVine(state, view, index, px, py);
        if (hit)
            return hit;
    }
    for (const kind of ['spike', 'bridge']) {
        const rows = rowsOf(state.draft, kind);
        for (let index = rows.length - 1; index >= 0; index--) {
            const hit = hitRow(state, view, kind, index, px, py);
            if (hit)
                return hit;
        }
    }
    const ground = state.draft.ground;
    for (let index = 0; index < ground.length; index++) {
        const hit = hitEdge(state, view, 'ground', index, px, py);
        if (hit)
            return hit;
    }
    for (let index = 0; index < ground.length; index++) {
        const hit = hitRow(state, view, 'ground', index, px, py);
        if (hit)
            return hit;
    }
    return null;
}

function rowBox(state, view, kind, index) {
    const row = rowsOf(state.draft, kind)[index];
    const lane = laneOf(state.draft, kind, row);
    const left = sx(view, row.start);
    const right = sx(view, row.end);
    const top = sy(view, lane.top);
    let bottom = sy(view, lane.bottom);
    if (kind === 'ground')
        bottom = Math.max(bottom, top + GROUND_BAND_MIN_PX);
    return { row, left, right, top, bottom };
}

function hitEdge(state, view, kind, index, px, py) {
    const box = rowBox(state, view, kind, index);
    if (py < box.top - 14 || py > box.bottom)
        return null;
    if (Math.abs(px - box.left) <= EDGE_GRAB_PX)
        return { kind, index, part: 'start' };
    if (Math.abs(px - box.right) <= EDGE_GRAB_PX)
        return { kind, index, part: 'end' };
    return null;
}

function hitRow(state, view, kind, index, px, py) {
    const edge = hitEdge(state, view, kind, index, px, py);
    if (edge)
        return edge;
    const box = rowBox(state, view, kind, index);
    if (px >= box.left && px <= box.right && py >= box.top - 4 && py <= box.bottom)
        return { kind, index, part: 'body' };
    return null;
}

function cursorFor(hit) {
    if (!hit)
        return 'default';
    if (hit.part === 'start' || hit.part === 'end')
        return 'ew-resize';
    return hit.kind === 'ground' || hit.kind === 'vine' ? 'move' : 'grab';
}

// ---------------------------------------------------------------------------
// Input
// ---------------------------------------------------------------------------

function invoke(state, method, ...args) {
    if (state.stopped || !state.dotNet)
        return;
    try {
        const pending = state.dotNet.invokeMethodAsync(method, ...args);
        pending?.catch?.(err => console.warn(`[coursePartEditor] ${method} was refused.`, err));
    } catch (err) {
        console.warn(`[coursePartEditor] ${method} could not be called.`, err);
    }
}

function select(state, selection) {
    const same = (selection === null && state.selection === null)
        || (selection && state.selection && selection.kind === state.selection.kind && selection.index === state.selection.index);
    state.selection = selection;
    if (!same)
        invoke(state, 'OnSelectionChanged', selection ? selection.kind : '', selection ? selection.index : -1);
}

function reportDecoration(state, index, final) {
    const decoration = state.draft.decorations[index];
    if (!decoration)
        return;
    invoke(state, 'OnDecorationChanged', index, decoration.x, decoration.front, decoration.flip, final);
}

function reportVine(state, index, final) {
    const vine = state.draft.vines[index];
    if (!vine)
        return;
    invoke(state, 'OnVineChanged', index, vine.anchorX, vine.anchorHeight, vine.platformStart, vine.platformEnd, vine.platformHeight, final);
}

function setToolFromOverlay(state, tool) {
    setTool(tool, state.toolKind);
    invoke(state, 'OnToolChanged', state.tool);
}

function reportRow(state, kind, index, final) {
    const row = rowsOf(state.draft, kind)[index];
    if (!row)
        return;
    invoke(state, 'OnGeometryChanged', kind, index, row.start, row.end, row.height, final);
}

function localPoint(state, event) {
    const rect = state.overlay.getBoundingClientRect();
    return { px: event.clientX - rect.left, py: event.clientY - rect.top };
}

function attachInput(state) {
    const overlay = state.overlay;

    state.onPointerDown = (event) => {
        overlay.focus({ preventScroll: true });
        const { px, py } = localPoint(state, event);
        const view = viewOf(state);
        const panning = event.button === 1 || (event.button === 0 && state.spaceHeld);
        if (event.button !== 0 && !panning)
            return;

        event.preventDefault();
        overlay.setPointerCapture?.(event.pointerId);

        if (!panning && state.tool === 'decoration') {
            const x = worldX(view, px);
            if (state.toolKind && standsOnGround(state.draft, x)) {
                const kind = decorationKind(state.toolKind);
                invoke(state, 'OnDecorationPlaced', state.toolKind, Math.round(x * 10), kind !== null && kind.layer === 'front');
            }
            return;
        }
        if (!panning && state.tool === 'vine') {
            invoke(state, 'OnVinePlaced', Math.round(worldX(view, px)));
            return;
        }
        if (!panning && state.tool === 'space') {
            const x = Math.round(worldX(view, px));
            state.draft.spaceX = x;
            invoke(state, 'OnSpacePicked', x);
            return;
        }

        const hit = panning ? null : hitTest(state, view, px, py);
        if (hit) {
            const row = rowsOf(state.draft, hit.kind)[hit.index];
            select(state, { kind: hit.kind, index: hit.index });
            state.drag = {
                kind: hit.kind,
                index: hit.index,
                part: hit.part,
                origin: hit.kind === 'decoration'
                    ? { x: row.x }
                    : hit.kind === 'vine'
                        ? { ...row }
                        : { start: row.start, end: row.end, height: row.height },
                downPx: px,
                downPy: py,
                axis: null,
                changed: false,
            };
            overlay.style.cursor = cursorFor(hit);
            return;
        }

        if (!panning)
            select(state, null);
        state.pan = { downPx: px, downPy: py, cameraX: state.cameraX, cameraY: state.cameraY };
        overlay.style.cursor = 'grabbing';
    };

    state.onPointerMove = (event) => {
        const { px, py } = localPoint(state, event);
        state.pointer = { px, py };
        const view = viewOf(state);

        if (state.drag) {
            dragTo(state, view, px, py);
            return;
        }
        if (state.pan) {
            state.cameraX = state.pan.cameraX - (px - state.pan.downPx) / view.ppm;
            state.cameraY = state.pan.cameraY + (py - state.pan.downPy) / view.ppm;
            applyCamera(state);
            return;
        }

        state.hover = state.tool ? null : hitTest(state, view, px, py);
        overlay.style.cursor = state.spaceHeld ? 'grab' : state.tool ? 'crosshair' : cursorFor(state.hover);
    };

    state.onPointerUp = () => {
        const drag = state.drag;
        state.drag = null;
        state.pan = null;
        if (drag && drag.changed) {
            if (drag.kind === 'decoration')
                reportDecoration(state, drag.index, true);
            else if (drag.kind === 'vine')
                reportVine(state, drag.index, true);
            else
                reportRow(state, drag.kind, drag.index, true);
        }
        overlay.style.cursor = state.tool ? 'crosshair' : cursorFor(state.hover);
    };

    state.onPointerLeave = () => {
        state.pointer = null;
        if (!state.drag)
            state.hover = null;
    };

    state.onDoubleClick = (event) => {
        event.preventDefault();
        if (state.tool)
            return;
        const { px, py } = localPoint(state, event);
        addAt(state, viewOf(state), px, py, event.shiftKey);
    };

    state.onWheel = (event) => {
        event.preventDefault();
        const view = viewOf(state);
        const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? view.h : 1;
        const dx = event.deltaX * unit;
        const dy = event.deltaY * unit;

        if (event.ctrlKey || event.metaKey) {
            const { px, py } = localPoint(state, event);
            zoomAbout(state, state.pxPerMetre * Math.exp(-dy * 0.0015), px, py, true);
            return;
        }
        if (event.shiftKey)
            state.cameraY -= (dy + dx) / view.ppm;
        else
            state.cameraX += (dx + dy) / view.ppm;
        applyCamera(state);
    };

    state.onKeyDown = (event) => onKey(state, event);
    state.onKeyUp = (event) => {
        if (event.code === 'Space') {
            state.spaceHeld = false;
            overlay.style.cursor = cursorFor(state.hover);
        }
    };
    state.onBlur = () => { state.spaceHeld = false; };
    state.onContextMenu = (event) => event.preventDefault();

    state.resizeObserver = typeof ResizeObserver === 'function'
        ? new ResizeObserver(() => { ensureFramed(state); })
        : null;
    state.resizeObserver?.observe(overlay);

    overlay.addEventListener('pointerdown', state.onPointerDown);
    overlay.addEventListener('pointermove', state.onPointerMove);
    overlay.addEventListener('pointerup', state.onPointerUp);
    overlay.addEventListener('pointercancel', state.onPointerUp);
    overlay.addEventListener('pointerleave', state.onPointerLeave);
    overlay.addEventListener('dblclick', state.onDoubleClick);
    overlay.addEventListener('wheel', state.onWheel, { passive: false });
    overlay.addEventListener('keydown', state.onKeyDown);
    overlay.addEventListener('keyup', state.onKeyUp);
    overlay.addEventListener('blur', state.onBlur);
    overlay.addEventListener('contextmenu', state.onContextMenu);
}

function detachInput(state) {
    const overlay = state.overlay;
    state.resizeObserver?.disconnect();
    overlay.removeEventListener('pointerdown', state.onPointerDown);
    overlay.removeEventListener('pointermove', state.onPointerMove);
    overlay.removeEventListener('pointerup', state.onPointerUp);
    overlay.removeEventListener('pointercancel', state.onPointerUp);
    overlay.removeEventListener('pointerleave', state.onPointerLeave);
    overlay.removeEventListener('dblclick', state.onDoubleClick);
    overlay.removeEventListener('wheel', state.onWheel, { passive: false });
    overlay.removeEventListener('keydown', state.onKeyDown);
    overlay.removeEventListener('keyup', state.onKeyUp);
    overlay.removeEventListener('blur', state.onBlur);
    overlay.removeEventListener('contextmenu', state.onContextMenu);
}

/**
 * Follow a drag: an edge resizes its row a whole metre at a time and never past the other edge; a body moves
 * the whole row along X a whole metre at a time, or, for ground dragged mostly up or down, changes its
 * height a decimetre at a time. Which of the two a body drag does is settled once it has moved a few pixels.
 */
function dragTo(state, view, px, py) {
    const drag = state.drag;
    const row = rowsOf(state.draft, drag.kind)[drag.index];
    if (!row)
        return;

    if (drag.kind === 'decoration') {
        row.x = drag.origin.x + Math.round((px - drag.downPx) / view.ppm * 10);
        if (row.x === drag.origin.x && !drag.changed)
            return;
        drag.changed = true;
        const nowMs = performance.now();
        if (nowMs - state.lastLiveMs >= LIVE_INTERVAL_MS) {
            state.lastLiveMs = nowMs;
            reportDecoration(state, drag.index, false);
        }
        return;
    }

    const dxPx = px - drag.downPx;
    const dyPx = py - drag.downPy;
    const dxMetres = dxPx / view.ppm;
    const dyMetres = -dyPx / view.ppm;
    const origin = drag.origin;

    if (drag.kind === 'vine') {
        dragVine(state, drag, row, dxPx, dyPx, dxMetres, dyMetres);
        return;
    }

    if (drag.part === 'start') {
        row.start = Math.min(Math.round(origin.start + dxMetres), origin.end - 1);
    } else if (drag.part === 'end') {
        row.end = Math.max(Math.round(origin.end + dxMetres), origin.start + 1);
    } else {
        if (drag.axis === null && (Math.abs(dxPx) > AXIS_LOCK_PX || Math.abs(dyPx) > AXIS_LOCK_PX))
            drag.axis = drag.kind === 'ground' && Math.abs(dyPx) > Math.abs(dxPx) ? 'y' : 'x';
        if (drag.axis === 'x') {
            const shift = Math.round(dxMetres);
            row.start = origin.start + shift;
            row.end = origin.end + shift;
        } else if (drag.axis === 'y') {
            row.height = origin.height + Math.round(dyMetres * 10);
        }
    }

    const changed = row.start !== origin.start || row.end !== origin.end || row.height !== origin.height;
    if (!changed && !drag.changed)
        return;
    drag.changed = true;

    const nowMs = performance.now();
    if (nowMs - state.lastLiveMs >= LIVE_INTERVAL_MS) {
        state.lastLiveMs = nowMs;
        reportRow(state, drag.kind, drag.index, false);
    }
}

/**
 * Follow a drag on a vine. The anchor or the rope, dragged sideways, moves the whole vine (anchor and
 * platform) a whole metre at a time, and dragged up or down raises or lowers the anchor a decimetre at a time.
 * The platform, dragged sideways, moves along X a metre at a time, and up or down changes its height a
 * decimetre at a time; its edges resize it and never past each other. Which axis a body drag takes is settled
 * once it has moved a few pixels.
 */
function dragVine(state, drag, vine, dxPx, dyPx, dxMetres, dyMetres) {
    const origin = drag.origin;
    if (drag.part === 'start') {
        vine.platformStart = Math.min(Math.round(origin.platformStart + dxMetres), origin.platformEnd - 1);
    } else if (drag.part === 'end') {
        vine.platformEnd = Math.max(Math.round(origin.platformEnd + dxMetres), origin.platformStart + 1);
    } else {
        if (drag.axis === null && (Math.abs(dxPx) > AXIS_LOCK_PX || Math.abs(dyPx) > AXIS_LOCK_PX))
            drag.axis = Math.abs(dyPx) > Math.abs(dxPx) ? 'y' : 'x';
        const shift = drag.axis === 'x' ? Math.round(dxMetres) : 0;
        const rise = drag.axis === 'y' ? Math.round(dyMetres * 10) : 0;
        if (drag.part === 'platform') {
            vine.platformStart = origin.platformStart + shift;
            vine.platformEnd = origin.platformEnd + shift;
            vine.platformHeight = origin.platformHeight + rise;
        } else {
            vine.anchorX = origin.anchorX + shift;
            vine.platformStart = origin.platformStart + shift;
            vine.platformEnd = origin.platformEnd + shift;
            vine.anchorHeight = origin.anchorHeight + rise;
        }
    }

    const changed = ['anchorX', 'anchorHeight', 'platformStart', 'platformEnd', 'platformHeight']
        .some(field => vine[field] !== origin[field]);
    if (!changed && !drag.changed)
        return;
    drag.changed = true;

    const nowMs = performance.now();
    if (nowMs - state.lastLiveMs >= LIVE_INTERVAL_MS) {
        state.lastLiveMs = nowMs;
        reportVine(state, drag.index, false);
    }
}

/**
 * A double-click adds a row where it lands: in a gap, a stretch of ground filling the gap less a metre at
 * each side (the whole gap when it is narrower than 4 m); past the part's end, a 20 m stretch; on ground, a
 * 2 m spike bed, or with Shift a 6 m bridge, kept on that stretch. A double-click on a spike bed or a
 * bridge adds nothing.
 */
function addAt(state, view, px, py, shift) {
    const hit = hitTest(state, view, px, py);
    if (hit && hit.kind !== 'ground')
        return;

    const draft = state.draft;
    const x = worldX(view, px);
    const y = worldY(view, py);
    const segment = groundAt(draft, x);

    if (segment && y <= segment.height / 10 + BRIDGE_LANE[1] + 0.5) {
        const width = Math.min(shift ? 6 : 2, segment.end - segment.start);
        let start = Math.round(x - width / 2);
        start = Math.max(segment.start, Math.min(start, segment.end - width));
        invoke(state, 'OnElementAdded', shift ? 'bridge' : 'spike', start, start + width, 0);
        return;
    }
    if (segment)
        return;

    const gap = openGaps(draft).find(candidate => x > candidate.start && x < candidate.end);
    const before = gap ? draft.ground[gap.row - 1] : undefined;
    if (gap && before) {
        const narrow = gap.width < 4;
        invoke(state, 'OnElementAdded', 'ground', narrow ? gap.start : gap.start + 1, narrow ? gap.end : gap.end - 1, before.height);
        return;
    }

    const length = partLength(draft);
    if (x > length) {
        const start = Math.max(length + 1, Math.round(x));
        invoke(state, 'OnElementAdded', 'ground', start, start + 20, 0);
    }
}

/**
 * Keys on the focused canvas: Delete or Backspace removes the selection, Escape clears it, Left and Right
 * move it a metre (Shift: 5 m), Up and Down, or Alt with Left and Right, raise and lower ground a decimetre
 * (Shift: 5 dm), and P flags or unflags the selected ground as a pit floor. Space held turns a drag into a pan.
 */
function onKey(state, event) {
    if (event.code === 'Space') {
        event.preventDefault();
        if (!state.spaceHeld) {
            state.spaceHeld = true;
            state.overlay.style.cursor = 'grab';
        }
        return;
    }

    const selection = state.selection;
    if (event.key === 'Escape') {
        event.preventDefault();
        if (state.tool)
            setToolFromOverlay(state, '');
        else
            select(state, null);
        return;
    }
    if (!selection)
        return;

    if (event.key === 'Delete' || event.key === 'Backspace') {
        event.preventDefault();
        state.selection = null;
        invoke(state, 'OnElementRemoved', selection.kind, selection.index);
        return;
    }

    if (selection.kind === 'decoration') {
        onDecorationKey(state, event, selection.index);
        return;
    }
    if (selection.kind === 'vine') {
        onVineKey(state, event, selection.index);
        return;
    }

    const row = rowsOf(state.draft, selection.kind)[selection.index];
    if (!row)
        return;

    if (selection.kind === 'ground' && event.key.toLowerCase() === 'p' && !event.ctrlKey && !event.metaKey && !event.altKey) {
        event.preventDefault();
        row.pit = !row.pit;
        invoke(state, 'OnGroundPitChanged', selection.index, row.pit);
        return;
    }

    const step = event.shiftKey ? 5 : 1;
    let handled = true;
    if (event.key === 'ArrowUp' || (event.altKey && event.key === 'ArrowRight')) {
        if (selection.kind === 'ground')
            row.height += step;
    } else if (event.key === 'ArrowDown' || (event.altKey && event.key === 'ArrowLeft')) {
        if (selection.kind === 'ground')
            row.height -= step;
    } else if (event.key === 'ArrowLeft') {
        row.start -= step;
        row.end -= step;
    } else if (event.key === 'ArrowRight') {
        row.start += step;
        row.end += step;
    } else {
        handled = false;
    }

    if (!handled)
        return;
    event.preventDefault();

    reportRow(state, selection.kind, selection.index, false);
    clearTimeout(state.nudgeTimer);
    state.nudgeTimer = setTimeout(() => reportRow(state, selection.kind, selection.index, true), NUDGE_SETTLE_MS);
}

/**
 * Keys on a selected decoration: Left and Right move it a decimetre (Shift: a metre), B puts it in the other
 * layer and F mirrors it.
 */
function onDecorationKey(state, event, index) {
    const decoration = state.draft.decorations[index];
    if (!decoration)
        return;

    const key = event.key.toLowerCase();
    let final = true;
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        decoration.x += (event.key === 'ArrowLeft' ? -1 : 1) * (event.shiftKey ? 10 : 1);
        final = false;
    } else if (key === 'b') {
        decoration.front = !decoration.front;
    } else if (key === 'f') {
        decoration.flip = !decoration.flip;
    } else {
        return;
    }
    event.preventDefault();

    reportDecoration(state, index, final);
    if (!final) {
        clearTimeout(state.nudgeTimer);
        state.nudgeTimer = setTimeout(() => reportDecoration(state, index, true), NUDGE_SETTLE_MS);
    }
}

/**
 * Keys on a selected vine: Left and Right move the whole vine a metre (Shift: 5 m), Up and Down raise or lower
 * its anchor a decimetre (Shift: 5 dm).
 */
function onVineKey(state, event, index) {
    const vine = state.draft.vines[index];
    if (!vine)
        return;

    const step = event.shiftKey ? 5 : 1;
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
        const shift = event.key === 'ArrowLeft' ? -step : step;
        vine.anchorX += shift;
        vine.platformStart += shift;
        vine.platformEnd += shift;
    } else if (event.key === 'ArrowUp' || event.key === 'ArrowDown') {
        vine.anchorHeight += event.key === 'ArrowUp' ? step : -step;
    } else {
        return;
    }
    event.preventDefault();

    reportVine(state, index, false);
    clearTimeout(state.nudgeTimer);
    state.nudgeTimer = setTimeout(() => reportVine(state, index, true), NUDGE_SETTLE_MS);
}

// ---------------------------------------------------------------------------
// Drawing
// ---------------------------------------------------------------------------

function sizeOverlay(state) {
    const overlay = state.overlay;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    const w = Math.max(1, Math.round(overlay.clientWidth * dpr));
    const h = Math.max(1, Math.round(overlay.clientHeight * dpr));
    if (overlay.width !== w || overlay.height !== h) {
        overlay.width = w;
        overlay.height = h;
    }
    state.dpr = dpr;
}

function draw(state) {
    ensureFramed(state);
    sizeOverlay(state);

    const ctx = state.ctx;
    const view = viewOf(state);
    ctx.setTransform(state.dpr, 0, 0, state.dpr, 0, 0);
    ctx.clearRect(0, 0, view.w, view.h);
    ctx.font = '12px ui-monospace, "Cascadia Mono", Consolas, monospace';
    ctx.textBaseline = 'alphabetic';

    drawClearances(state, ctx, view);
    drawGaps(state, ctx, view);
    drawRows(state, ctx, view, 'ground');
    drawRows(state, ctx, view, 'spike');
    drawRows(state, ctx, view, 'bridge');
    drawDecorations(state, ctx, view);
    drawVines(state, ctx, view);
    drawSpaceMarker(state, ctx, view);
    drawPhoneFrame(ctx, view);
    drawRuler(ctx, view);
    drawStatus(state, ctx, view);

    publishHandles(state, view);
}

function label(ctx, text, x, y, color, align) {
    ctx.textAlign = align || 'center';
    ctx.lineWidth = 3;
    ctx.strokeStyle = COLORS.labelShadow;
    ctx.strokeText(text, x, y);
    ctx.fillStyle = color || COLORS.label;
    ctx.fillText(text, x, y);
}

/** The entry and exit clearances: the flat, hazard-free metres each end of a part must have. */
function drawClearances(state, ctx, view) {
    const draft = state.draft;
    const length = partLength(draft);
    const zones = [
        { from: 0, to: draft.entryClearance, text: `entry ${draft.entryClearance} m` },
        { from: length - draft.exitClearance, to: length, text: `exit ${draft.exitClearance} m` },
    ];
    for (const zone of zones) {
        const left = sx(view, zone.from);
        const right = sx(view, zone.to);
        if (right < 0 || left > view.w)
            continue;
        ctx.fillStyle = COLORS.clearance;
        ctx.fillRect(left, 0, right - left, view.h);
        ctx.strokeStyle = COLORS.clearanceEdge;
        ctx.lineWidth = 1;
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.moveTo(left + 0.5, 0);
        ctx.lineTo(left + 0.5, view.h);
        ctx.moveTo(right - 0.5, 0);
        ctx.lineTo(right - 0.5, view.h);
        ctx.stroke();
        ctx.setLineDash([]);
        label(ctx, zone.text, (left + right) / 2, 16, COLORS.clearanceEdge);
    }

    const end = sx(view, length);
    label(ctx, `hand-over ${length} m`, end, 32, COLORS.label);
    label(ctx, '0 m', sx(view, 0), 32, COLORS.label);
}

/** Diagonal hatching across a box, clipped to it. */
function hatch(ctx, left, top, width, height, color) {
    ctx.save();
    ctx.beginPath();
    ctx.rect(left, top, width, height);
    ctx.clip();
    ctx.strokeStyle = color;
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = left - height; x < left + width; x += 8) {
        ctx.moveTo(x, top + height);
        ctx.lineTo(x + height, top);
    }
    ctx.stroke();
    ctx.restore();
}

function drawGaps(state, ctx, view) {
    for (const gap of openGaps(state.draft)) {
        const left = sx(view, gap.start);
        const right = sx(view, gap.end);
        if (right < 0 || left > view.w)
            continue;
        const color = COLORS[gap.kind];
        const lip = sy(view, gap.lipY);
        const floor = sy(view, gap.lipY - gap.depth);
        ctx.strokeStyle = color;
        ctx.lineWidth = 1.5;
        ctx.setLineDash([6, 4]);
        ctx.strokeRect(left, lip, right - left, floor - lip);
        ctx.setLineDash([]);
        label(ctx, `${gap.width} m · ${gap.kind}`, (left + right) / 2, lip - 10, color);
        label(ctx, `${gap.depth.toFixed(1)} m deep`, (left + right) / 2, floor + 16, color);
    }
}

function drawRows(state, ctx, view, kind) {
    const rows = rowsOf(state.draft, kind);
    const stroke = COLORS[kind];
    const fill = COLORS[kind + 'Fill'];

    for (let index = 0; index < rows.length; index++) {
        const box = rowBox(state, view, kind, index);
        if (box.right < -20 || box.left > view.w + 20)
            continue;

        const row = box.row;
        const pit = kind === 'ground' && row.pit === true;
        const selected = state.selection && state.selection.kind === kind && state.selection.index === index;
        const hovered = !selected && state.hover && state.hover.kind === kind && state.hover.index === index;
        const outline = selected ? COLORS.selected : hovered ? COLORS.hover : pit ? COLORS.pit : stroke;
        const width = Math.max(1, box.right - box.left);

        ctx.fillStyle = pit ? COLORS.pitFill : fill;
        ctx.fillRect(box.left, box.top, width, box.bottom - box.top);
        if (pit)
            hatch(ctx, box.left, box.top, width, box.bottom - box.top, COLORS.pitHatch);
        ctx.strokeStyle = outline;
        ctx.lineWidth = selected ? 2.5 : 1.5;
        ctx.strokeRect(box.left, box.top, width, box.bottom - box.top);

        if (kind === 'ground') {
            ctx.lineWidth = selected ? 4 : 3;
            ctx.beginPath();
            ctx.moveTo(box.left, box.top);
            ctx.lineTo(box.right, box.top);
            ctx.stroke();
        }

        // The edge handles: a bar at each end, the hovered edge drawn brighter.
        for (const edge of ['start', 'end']) {
            const x = edge === 'start' ? box.left : box.right;
            const grabbed = (state.hover && state.hover.kind === kind && state.hover.index === index && state.hover.part === edge)
                || (state.drag && state.drag.kind === kind && state.drag.index === index && state.drag.part === edge);
            ctx.fillStyle = grabbed ? COLORS.hover : outline;
            ctx.fillRect(x - 3, box.top - 10, 6, Math.min(box.bottom - box.top + 10, 34));
        }

        const name = kind === 'ground' ? `${pit ? 'PIT floor' : 'ground'} ${index + 1}` : kind === 'spike' ? `spikes ${index + 1}` : `bridge ${index + 1}`;
        const span = `${row.start}–${row.end} m`;
        const text = kind === 'ground' ? `${name} · ${span} · ${(row.height / 10).toFixed(1)} m` : `${name} · ${span}`;
        const labelY = kind === 'ground' ? box.bottom + 16 : box.top - 4;
        if (kind !== 'ground' || width > 60)
            label(ctx, text, (box.left + box.right) / 2, labelY, selected ? COLORS.selected : pit ? COLORS.pit : COLORS.label);
        else if (pit)
            label(ctx, 'PIT', (box.left + box.right) / 2, labelY, selected ? COLORS.selected : COLORS.pit);
        if (pit) {
            const gap = floorGap(state.draft, index);
            if (gap)
                label(ctx, `${gap.depth.toFixed(1)} m deep`, (box.left + box.right) / 2, box.top - 14, COLORS.pit);
        }
    }
}

/**
 * Every decoration's handle: the box its sprite covers, outlined in its layer's colour (red for a kind the
 * catalogue does not know, which the renderer skips) with a tick at the ground point it stands on.
 */
function drawDecorations(state, ctx, view) {
    const decorations = state.draft.decorations;
    for (let index = 0; index < decorations.length; index++) {
        const box = decorationBox(state, view, index);
        if (box.right < -20 || box.left > view.w + 20)
            continue;

        const decoration = box.decoration;
        const selected = state.selection && state.selection.kind === 'decoration' && state.selection.index === index;
        const hovered = !selected && state.hover && state.hover.kind === 'decoration' && state.hover.index === index;
        const stroke = !box.known ? COLORS.unknown : decoration.front ? COLORS.decorationFront : COLORS.decorationBehind;
        const outline = selected ? COLORS.selected : hovered ? COLORS.hover : stroke;

        ctx.fillStyle = COLORS.decorationFill;
        ctx.fillRect(box.left, box.top, box.right - box.left, box.bottom - box.top);
        ctx.strokeStyle = outline;
        ctx.lineWidth = selected ? 2.5 : 1.5;
        ctx.setLineDash(decoration.front ? [] : [4, 3]);
        ctx.strokeRect(box.left, box.top, box.right - box.left, box.bottom - box.top);
        ctx.setLineDash([]);
        ctx.beginPath();
        ctx.moveTo(box.centre, box.baseY - 6);
        ctx.lineTo(box.centre, box.baseY + 6);
        ctx.stroke();

        const layer = decoration.front ? 'front' : 'behind';
        const text = `${decoration.kind || '?'} · ${(decoration.x / 10).toFixed(1)} m · ${layer}${decoration.flip ? ' · flipped' : ''}`;
        label(ctx, text, box.centre, box.top - 6, selected ? COLORS.selected : box.known ? COLORS.label : COLORS.unknown);
    }
}

/**
 * Every vine's handles: the dashed reach of its swing around the anchor, the rope hanging at its rest angle,
 * the anchor as a ring, and the platform its swing lands on as a box, each outlined brighter when selected.
 */
function drawVines(state, ctx, view) {
    const vines = state.draft.vines;
    for (let index = 0; index < vines.length; index++) {
        const shape = vineShape(state, view, index);
        const platform = shape.platform;
        const left = Math.min(shape.anchor.x - shape.reachPx, platform.left);
        const right = Math.max(shape.anchor.x + shape.reachPx, platform.right);
        if (right < -20 || left > view.w + 20)
            continue;

        const vine = shape.vine;
        const selected = state.selection && state.selection.kind === 'vine' && state.selection.index === index;
        const hovered = !selected && state.hover && state.hover.kind === 'vine' && state.hover.index === index;
        const outline = selected ? COLORS.selected : hovered ? COLORS.hover : COLORS.vine;

        ctx.strokeStyle = COLORS.vineReach;
        ctx.lineWidth = 1;
        ctx.setLineDash([4, 4]);
        ctx.beginPath();
        ctx.arc(shape.anchor.x, shape.anchor.y, shape.reachPx, 0, Math.PI);
        ctx.stroke();
        ctx.setLineDash([]);

        ctx.strokeStyle = outline;
        ctx.lineWidth = selected ? 3 : 2;
        ctx.beginPath();
        ctx.moveTo(shape.anchor.x, shape.anchor.y);
        ctx.lineTo(shape.ropeEnd.x, shape.ropeEnd.y);
        ctx.stroke();

        const platformWidth = Math.max(1, platform.right - platform.left);
        ctx.fillStyle = COLORS.vineFill;
        ctx.fillRect(platform.left, platform.top, platformWidth, platform.bottom - platform.top);
        ctx.lineWidth = selected ? 2.5 : 1.5;
        ctx.strokeRect(platform.left, platform.top, platformWidth, platform.bottom - platform.top);
        for (const x of [platform.left, platform.right]) {
            ctx.fillStyle = outline;
            ctx.fillRect(x - 3, platform.top - 8, 6, platform.bottom - platform.top + 8);
        }

        ctx.fillStyle = COLORS.vineFill;
        ctx.beginPath();
        ctx.arc(shape.anchor.x, shape.anchor.y, VINE_ANCHOR_PX, 0, Math.PI * 2);
        ctx.fill();
        ctx.lineWidth = selected ? 3 : 2;
        ctx.stroke();

        const textColor = selected ? COLORS.selected : COLORS.label;
        label(ctx, `vine ${index + 1} · ${vine.anchorX} m · ${(vine.anchorHeight / 10).toFixed(1)} m`,
            shape.anchor.x, shape.anchor.y - VINE_ANCHOR_PX - 6, textColor);
        label(ctx, `platform ${vine.platformStart}–${vine.platformEnd} m · ${(vine.platformHeight / 10).toFixed(1)} m`,
            (platform.left + platform.right) / 2, platform.top - 10, selected ? COLORS.selected : COLORS.vine);
    }
}

/**
 * Where the add/remove space tool works: a line at its X, and the span a removal would take out shaded from
 * there to X plus its amount.
 */
function drawSpaceMarker(state, ctx, view) {
    const draft = state.draft;
    const visible = state.tool === 'space' || draft.spaceX !== 0;
    if (!visible)
        return;

    const left = sx(view, draft.spaceX);
    const right = sx(view, draft.spaceX + draft.spaceAmount);
    if (right < 0 || left > view.w)
        return;
    ctx.fillStyle = COLORS.spaceFill;
    ctx.fillRect(left, 0, Math.max(1, right - left), view.h);
    ctx.strokeStyle = COLORS.space;
    ctx.lineWidth = 2;
    ctx.beginPath();
    ctx.moveTo(Math.round(left) + 0.5, 0);
    ctx.lineTo(Math.round(left) + 0.5, view.h);
    ctx.stroke();
    label(ctx, `space at ${draft.spaceX} m · ${draft.spaceAmount} m`, left + 6, 48, COLORS.space, 'left');
}

/**
 * What a player's portrait stage would see with its camera here: `PHONE_METRES_ACROSS` across with the runner
 * `PHONE_RUNNER_FRACTION` of the way in at the editor's camera X, and as tall as the portrait column, its ground
 * line `PHONE_GROUND_LINE_FRACTION` of the way down at the height a run's camera settles on at that X.
 */
function drawPhoneFrame(ctx, view) {
    const left = sx(view, view.cx - PHONE_RUNNER_FRACTION * PHONE_METRES_ACROSS);
    const right = left + PHONE_METRES_ACROSS * view.ppm;
    const top = sy(view, view.runCameraY + PHONE_GROUND_LINE_FRACTION * PHONE_METRES_TALL);
    const bottom = top + PHONE_METRES_TALL * view.ppm;

    ctx.strokeStyle = COLORS.phone;
    ctx.lineWidth = 1.5;
    ctx.setLineDash([10, 6]);
    ctx.strokeRect(left, top, right - left, bottom - top);
    ctx.setLineDash([]);
    label(ctx, 'phone view', left + 6, Math.max(top + 16, 50), COLORS.phone, 'left');
}

/** A metre ruler along the foot of the canvas, labelled every 5 m (every 10 m when zoomed far out). */
function drawRuler(ctx, view) {
    const from = Math.floor(worldX(view, 0));
    const to = Math.ceil(worldX(view, view.w));
    const labelEvery = view.ppm < 22 ? 10 : 5;
    ctx.strokeStyle = COLORS.ruler;
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = from; x <= to; x++) {
        const px = Math.round(sx(view, x)) + 0.5;
        const major = x % labelEvery === 0;
        ctx.moveTo(px, view.h);
        ctx.lineTo(px, view.h - (major ? 12 : 5));
    }
    ctx.stroke();
    for (let x = Math.ceil(from / labelEvery) * labelEvery; x <= to; x += labelEvery)
        label(ctx, `${x}`, sx(view, x), view.h - 15, COLORS.ruler);
}

function drawStatus(state, ctx, view) {
    const lines = [`${Math.round(view.ppm)} px/m`];
    if (state.pointer) {
        const x = worldX(view, state.pointer.px);
        const y = worldY(view, state.pointer.py);
        lines.push(`x ${x.toFixed(1)} m · y ${y.toFixed(1)} m`);
    }
    label(ctx, lines.join('   '), view.w - 8, 16, COLORS.label, 'right');

    if (!state.draft.sound) {
        const text = 'The draft breaks a rule: the terrain drawn is the last sound draft.';
        ctx.font = '13px system-ui, sans-serif';
        const width = ctx.measureText(text).width + 20;
        ctx.fillStyle = COLORS.banner;
        ctx.fillRect((view.w - width) / 2, 40, width, 24);
        ctx.fillStyle = COLORS.label;
        ctx.textAlign = 'center';
        ctx.fillText(text, view.w / 2, 57);
    }
}

/**
 * Write every row's handles onto the overlay as `data-handles` (kind, index, start edge, end edge and top, in
 * CSS pixels from the overlay's top-left) and the scale and selection beside it, for tests and diagnostics.
 * A vine writes three, told apart by `part`: its `anchor` (the ring, `startX` and `endX` either side of its
 * centre and `y` at it), its `rope` (from the anchor, at `y`, to the rope's free end, at `endY`) and its
 * `platform`. Written only when something has moved.
 */
function publishHandles(state, view) {
    const handles = [];
    for (const kind of ['ground', 'spike', 'bridge']) {
        const rows = rowsOf(state.draft, kind);
        for (let index = 0; index < rows.length; index++) {
            const box = rowBox(state, view, kind, index);
            handles.push({
                kind,
                index,
                startX: Math.round(box.left * 10) / 10,
                endX: Math.round(box.right * 10) / 10,
                y: Math.round(box.top * 10) / 10,
            });
        }
    }
    for (let index = 0; index < state.draft.decorations.length; index++) {
        const box = decorationBox(state, view, index);
        handles.push({
            kind: 'decoration',
            index,
            startX: Math.round(box.left * 10) / 10,
            endX: Math.round(box.right * 10) / 10,
            y: Math.round(box.top * 10) / 10,
        });
    }
    const round = value => Math.round(value * 10) / 10;
    for (let index = 0; index < state.draft.vines.length; index++) {
        const shape = vineShape(state, view, index);
        handles.push({
            kind: 'vine', part: 'anchor', index,
            startX: round(shape.anchor.x - VINE_ANCHOR_PX), endX: round(shape.anchor.x + VINE_ANCHOR_PX), y: round(shape.anchor.y),
        });
        handles.push({
            kind: 'vine', part: 'rope', index,
            startX: round(Math.min(shape.anchor.x, shape.ropeEnd.x)), endX: round(Math.max(shape.anchor.x, shape.ropeEnd.x)),
            y: round(shape.anchor.y), endY: round(shape.ropeEnd.y),
        });
        handles.push({
            kind: 'vine', part: 'platform', index,
            startX: round(shape.platform.left), endX: round(shape.platform.right), y: round(shape.platform.top),
        });
    }
    const text = JSON.stringify(handles);
    const selection = state.selection ? `${state.selection.kind}:${state.selection.index}` : '';
    const signature = `${text}|${selection}|${Math.round(view.ppm)}`;
    if (signature === state.handlesAttr)
        return;
    state.handlesAttr = signature;
    const overlay = state.overlay;
    overlay.dataset.handles = text;
    overlay.dataset.selectedKind = state.selection ? state.selection.kind : '';
    overlay.dataset.selectedIndex = state.selection ? String(state.selection.index) : '';
}
