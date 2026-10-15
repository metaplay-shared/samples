/**
 * Canvas renderer for the TimeTrial view of the ninja autorunner.
 *
 * The renderer owns nothing but pixels: the Blazor page runs the deterministic simulation and hands
 * this module an immutable course once and a render snapshot every animation frame. Drawing is plain
 * canvas 2D against the art sheets in `wwwroot/art/`.
 *
 * Layout is portrait-first: a fixed span of course is visible across the canvas width, the ground line
 * sits in the lower third, and the runner rides a fixed fraction of the width. The camera X equals the
 * runner's X, so it never pulls ahead of them; the camera Y eases toward the ground level under the
 * runner so stepped ledges stay on screen (over a pit, the lip they left rather than the pit's floor), and is pinned to the runner between a climb's two torii so the
 * ascent reads as the mountain moving past a runner who holds one screen height.
 *
 * Every course is dressed the same way: the mountain's three altitude bands (foothills, mid-ridge, summit)
 * each a sky colour and two parallax planes, with the terrain's edge in that band's own crest stage
 * (`ALTITUDE_BANDS`), and stone spikes throughout. The run starts in the first band and crosses into the next
 * at the midpoint of each climb's slope, so on a composed map the first part is the foothills, the second the
 * mid-ridge and the last the summit. A course may name the band it starts in (`altitudeBand`), which is how
 * the part editor previews a part alone at any height. The terrain itself is the same near-black silhouette
 * in every band.
 *
 * Course decorations, cosmetic props a part places, such as the stone lantern, are drawn from the catalogue
 * in `courseDecorations.js`, in one of two layers: `behind` with the torii's far layer, before the terrain and
 * every runner, and `front` last of all, over the torii's near post, the field and the player's runner.
 *
 * This module is the entry: it builds the one `view` context every drawing module under `renderer/` reads
 * and writes: the canvas and its 2D context, the options, the course as the renderer reads it, the art, the
 * viewport and camera with their projections (`screenX`, `screenY`, `alignPx`, `viewLeftX`, `viewRightX`),
 * the renderer's clock (`animMs`) and every piece of animation state, and runs the frame over them in
 * drawing order.
 */

import { diagnosticsEnabled, prefersReducedMotion, finiteOr } from './util.js';
import { SHEET_FILES, ALTITUDE_BANDS } from './renderer/artSpecs.js';
import { loadImage } from './renderer/imageCache.js';
import { readCourse, groundTopAt, cameraGroundAt, previewCameraY } from './renderer/courseModel.js';
import { createRunnerAnimation, updateRunnerAnimation, RUNNER_TUNING, timeScale } from './renderer/runnerAnimation.js';
import { drawRunner, publishRunnerCounts } from './renderer/runnerDraw.js';
import { loadAltitudeBands, advanceAltitude, showAltitudeAt, drawBackdrop, measureBackdropBases } from './renderer/backdrop.js';
import { buildTerrainPieces, drawTerrain, drawPits, drawTerrainCrest, drawHazards, prepareHazardSheet, releaseHazardSheet } from './renderer/terrain.js';
import { updateDroopingBridges, bridgeSurfaceOffsetAt, drawBridgeChasms, drawBridgeWaterfalls, drawDroopingBridges } from './renderer/bridges.js';
import { drawVines, drawGroundedVineTree } from './renderer/vines.js';
import { drawTorii, drawToriiFront } from './renderer/torii.js';
import {
    readDecorations, updateDecorations, drawDecorations, updateClimbDressing, drawClimbShadow, playDecorationPassBy,
} from './renderer/decorations.js';
import { updateGhostEffects, expireGhostEffects, playGhostEffects, drawGhostEffects, drawGhostRunners } from './renderer/ghostField.js';

export { timeScale };

// ---------------------------------------------------------------------------
// Look and feel
// ---------------------------------------------------------------------------

const DEPTH_SHADE_COLOR = 'rgba(2, 8, 24, 0.55)';

/** Device pixel ratio cap */
const MAX_DPR = 1;

/**
 * Every option's value when the caller gives none. The part editor frames its phone outline from the same
 * values, so a player's stage and the outline always agree.
 */
export const DEFAULTS = Object.freeze({
    assetBaseUrl: 'art/',
    /** Course span visible across the canvas width, in metres. */
    metresAcross: 12,
    /**
     * A fixed scale in CSS pixels per metre, or 0 for the scale `metresAcross` gives. With a fixed scale the
     * canvas' size decides how much course is shown rather than how large it is drawn.
     */
    pxPerMetre: 0,
    /** Ground line position as a fraction of the canvas height. */
    groundLineFraction: 0.67,
    /** Runner position as a fraction of the canvas width. */
    runnerScreenFraction: 0.3,
    /**
     * Visible height of the standing runner in metres. The sheet's standing runner is 1.6 m at its own 64 px per
     * metre, so the default draws it at exactly that scale; another height scales every frame with it.
     */
    runnerHeightMetres: 1.6,
    /** Time constant of the camera's vertical easing, in seconds. */
    cameraEaseSeconds: 0.22,
    /** Horizontal speed above which speed lines and the motion ghost appear, in m/s. */
    speedLineThreshold: 9,
});

// ---------------------------------------------------------------------------
// Public API
// ---------------------------------------------------------------------------

/**
 * @typedef {Object} GroundSegment
 * @property {number} startX Left edge in metres.
 * @property {number} endX Right edge in metres.
 * @property {number} topY Walking surface height in metres.
 */

/**
 * @typedef {Object} HazardSpan
 * @property {number} startX Left edge in metres.
 * @property {number} endX Right edge in metres.
 */

/**
 * @typedef {Object} Course
 * @property {string} [id] Course id, for reference only.
 * @property {string} [altitudeBand] The band the course starts in: `foothills`, `mid-ridge` or `summit`. The
 *   foothills when absent or unknown.
 * @property {number} startX
 * @property {number} finishX
 * @property {GroundSegment[]} ground Sorted, non-overlapping. Every pit's floor is one of these.
 * @property {Pit[]} [pits] The shafts, in course order, each with its floor already in `ground`.
 * @property {HazardSpan[]} hazards Sorted, each inside one ground segment.
 * @property {Array<{index:number,id:string,kind:string,x:number,bottomY:number,topY:number,minimumGrabY:number,landingX:number}>} encounters
 *     Contextual course features, including vine placement.
 * @property {{leftExtent:number,rightExtent:number,height:number}} playerBody
 * @property {DroopingBridge[]} [droopingBridges] Presentation-only bridge spans over ordinary ground.
 * @property {Climb[]} [climbs] The climbs, whose slopes fill their gap in the ground and whose gates close a segment.
 * @property {{ledgeGrabClearanceHeight:number,ledgeGrabRiseSpeed:number,ledgeGrabTopTransitionSeconds:number}} [runnerRules] The player's
 *     effective rules, as far as drawing the runner needs them. Without them nothing plays the high-landing roll.
 * @property {Array} [ghostRunnerRules] The same for each opponent, by lineup position.
 */

/**
 * @typedef {Object} Pit
 * @property {number} startX The near lip, where the ground before the pit ends.
 * @property {number} endX The far lip, where the ground after it begins and its climbable wall stands.
 * @property {number} floorY Height of the floor the runner lands on and runs along.
 * @property {number} depth How far that floor lies below the lower of the two lips, in metres.
 */

/**
 * @typedef {Object} Climb
 * @property {string} id
 * @property {number} bottomX The bottom torii, on the flat ground before the slope.
 * @property {number} bottomY Height of that flat, which the slope starts from.
 * @property {number} slopeStartX The foot of the slope, where the ground below ends. A course view without one
 *   has its slope start at the bottom torii.
 * @property {number} topX Top of the slope, where ordinary ground resumes.
 * @property {number} topY Height the slope arrives at.
 * @property {number} gateX The top torii, at the far end of the flat run-out: the segment gate.
 */

/**
 * @typedef {Object} DroopingBridge
 * @property {string} id
 * @property {number} startX
 * @property {number} endX
 * @property {number} collisionY
 * @property {number} restSag
 * @property {number} maxLocalDroop
 * @property {number} responseRadius
 * @property {number} logCount
 */

/**
 * The properties in brackets are diagnostics: only a frame on a page the end-to-end tests opened carries
 * them (see `diagnosticsEnabled` in `util.js`), and nothing here reads them.
 *
 * @typedef {Object} TimeTrialSnapshot
 * @property {number} step Simulation step index.
 * @property {number} elapsedMs Simulated time so far.
 * @property {number} x Runner's feet position along the course, in metres.
 * @property {number} y Runner's feet height, in metres.
 * @property {number} vx Horizontal velocity, m/s.
 * @property {number} vy Vertical velocity, m/s.
 * @property {"Running"|"Charging"|"Airborne"|"Stumbling"|"Finished"|"Traversing"|"Climbing"} mode
 * @property {number} activeEncounterIndex
 * @property {string} encounterPhase
 * @property {number} encounterProgress
 * @property {number} charge Charge fraction, 0..1.
 * @property {boolean} finished True from the finish crossing on, through the run-out past the line, where
 *     `mode` is the running (or, coming down, airborne) mode the runner is drawn in.
 * @property {number} [finishTimeMs]
 * @property {boolean} inputLocked True while the button does nothing: during the run-in, and while a climb owns
 *     movement.
 * @property {boolean} [runIn] True while the field runs in to the start line before the clock starts, on step
 *     0: every runner's `x` is then behind the line, and they reach it together as the run-in ends.
 * @property {number} nextGateIndex The gate, a climb's top torii, being raced towards, or one past the last.
 * @property {number} nextClimbIndex The bottom torii being raced towards, which is where a segment is decided.
 */

/**
 * @typedef {Object} TimeTrialRendererOptions
 * @property {string} [assetBaseUrl] Where the art sheets live; defaults to `art/`.
 * @property {number} [metresAcross] Course span visible across the canvas width.
 * @property {number} [pxPerMetre] Fixed scale in CSS pixels per metre, overriding `metresAcross` when positive.
 * @property {number} [groundLineFraction] Ground line height as a fraction of the canvas.
 * @property {number} [runnerScreenFraction] Runner position as a fraction of the canvas width.
 * @property {number} [runnerHeightMetres] Visible runner height in metres.
 * @property {number} [cameraEaseSeconds] Time constant of the camera's vertical easing.
 * @property {number} [speedLineThreshold] Speed at which speed lines appear, m/s.
 */

/**
 * @typedef {Object} TimeTrialRenderer
 * @property {(snapshot: TimeTrialSnapshot) => void} render Draws one frame. Never awaits.
 * @property {(courseX: number, courseY?: number) => void} renderPreview Draws the course with no run in progress.
 * @property {() => void} resize Re-reads the canvas' CSS size and device pixel ratio.
 * @property {(value: number) => void} setPixelsPerMetre Fixes the scale, or returns to `metresAcross` for 0.
 * @property {() => object} previewProjection The camera and scale the last frame was drawn with.
 * @property {() => void} playGhostEffects Plays the poof and the warp-in once at their torii, in the preview.
 * @property {() => void} playDecorationPassBy Plays every decoration's pass-by animation once, in the preview.
 * @property {(onProgress?: (settled: number, total: number) => void) => Promise<void>} whenReady Resolves once
 *     everything the course draws from is in and prepared, so the first frame is drawn complete.
 * @property {() => void} dispose Stops observing the canvas and releases the scaled art.
 */

/**
 * Creates a renderer bound to one canvas and one course.
 *
 * The canvas is sized from its CSS box, so give it a sized container (see `.time-trial-stage` /
 * `.time-trial-canvas` in `css/base.css`). Resizes are picked up automatically through a `ResizeObserver`;
 * `resize()` is only needed when the canvas becomes visible without its box changing.
 *
 * @param {HTMLCanvasElement} canvas
 * @param {Course} course
 * @param {TimeTrialRendererOptions} [options]
 * @returns {TimeTrialRenderer}
 */
export function createTimeTrialRenderer(canvas, course, options) {
    const opts = Object.assign({}, DEFAULTS, options || {});
    const ctx = canvas.getContext('2d', { alpha: false });

    const view = {
        canvas: canvas,
        ctx: ctx,
        opts: opts,
        // Where the renderer's state is published for the end-to-end tests: the canvas' data attributes on a
        // page the tests opened, and otherwise a plain object nothing reads, so a player's frame writes no
        // attributes.
        testData: diagnosticsEnabled() ? canvas.dataset : {},
        baseUrl: opts.assetBaseUrl.endsWith('/') ? opts.assetBaseUrl : opts.assetBaseUrl + '/',
        reducedMotion: prefersReducedMotion(),

        // Viewport, refreshed by resize().
        dpr: 1,
        viewW: 0,
        viewH: 0,
        pxPerMetre: 1,
        groundLineY: 0,
        runnerScreenX: 0,
        depthShade: null,
        depthShadeTop: 0,

        // Camera and the renderer's clock.
        cameraX: 0,
        cameraY: 0,
        lastFrameMs: 0,
        lastStep: -1,
        animMs: 0,
        firstFrame: true,

        /** The altitude band the run has climbed into, as an index into `altitudeBands`. */
        altitudeBandIndex: 0,
        /** The band that one is crossing over: where the view was before the change started. */
        altitudeFromBandIndex: 0,
        /** How far the arriving band has crossed over the one below it, 0 to 1. */
        altitudeBlend: 1,

        /** The player's runner's own animation state; see `createRunnerAnimation`. */
        playerAnimation: createRunnerAnimation(),
        runPhase: 0,
        /** How many high-landing rolls the player's runner has started, and how many were cut short. */
        playerRollsStarted: 0,
        playerRollsInterrupted: 0,
        /** How many frames have drawn the player's runner in a mantle's climb frames. */
        playerClimbFramesDrawn: 0,
        /** Offscreen silhouette used for the stumble flash. */
        tintCanvas: null,
        tintCtx: null,

        /**
         * One running animation phase per opponent, kept across frames by lineup position. Each starts at its
         * own point in the cycle (see `ghostRunPhaseStart`), so the field never steps in unison or holds one pose.
         */
        ghostRunPhases: [],
        /** Each opponent's animation state, by lineup position. */
        ghostAnimations: [],
        /**
         * What the last frame saw of each opponent, by lineup position: whether it was hidden, and where it was
         * last drawn. A change between two frames is what starts a poof.
         */
        ghostSightings: [],
        /** The gate the player was racing towards on the last frame; passing it is the regroup. */
        lastNextGateIndex: 0,
        /** Whether the last frame was still the run-in; its end is when the field arrives under the start torii. */
        lastRunIn: false,
        /** The poofs and warp-ins still playing, each `{ spec, x, y, startMs }` in course metres and `animMs`. */
        ghostEffects: [],
        ghostPoofCount: 0,
        ghostWarpInCount: 0,

        bridgeWasLoaded: false,
        bridgeReboundUntilMs: 0,

        /** How many pass-by animations the player's runner has set off this renderer's lifetime. */
        decorationPassByCount: 0,
        /** How many stake pass-bys the player's runner has set off this renderer's lifetime. */
        climbStakePassByCount: 0,
        /** The shadow crossing the view, `{ startMs }` on `animMs`, or null between climbs. */
        climbShadow: null,
        climbShadowSweepCount: 0,
        /** The bottom torii the player was racing towards on the last frame; reaching it starts the sweep. */
        lastNextClimbIndex: 0,

        /** The hazards sheet as it is drawn at this viewport; see `prepareHazardSheet`. */
        hazardSheet: { source: null, scale: 1, canvas: null, dirty: true },
        /** Set by `dispose`, after which nothing is prepared or drawn. */
        disposed: false,

        /** Snaps a CSS-pixel coordinate to a whole device pixel, so tiles meet without seams. */
        alignPx: v => Math.round(v * view.dpr) / view.dpr,
        /** World X to canvas X, in CSS pixels. */
        screenX: x => view.runnerScreenX + (x - view.cameraX) * view.pxPerMetre,
        /** World Y to canvas Y, in CSS pixels. */
        screenY: y => view.groundLineY - (y - view.cameraY) * view.pxPerMetre,
        /** Left edge of the visible course span, in metres, with a tile of margin. */
        viewLeftX: () => view.cameraX - view.runnerScreenX / view.pxPerMetre - 1,
        /** Right edge of the visible course span, in metres, with a tile of margin. */
        viewRightX: () => view.viewLeftX() + view.viewW / view.pxPerMetre + 2,
    };

    readCourse(view, course);

    view.runnerArt = loadImage(view.baseUrl + SHEET_FILES.runner);
    view.ghostRunnerArt = loadImage(view.baseUrl + SHEET_FILES.ghostRunner);
    view.crestArt = loadImage(view.baseUrl + SHEET_FILES.crest);
    view.hazardArt = loadImage(view.baseUrl + SHEET_FILES.hazards);
    // The altitude bands, with every plane's image resolved once: a band change is a cross-fade, so the band
    // above has to be loaded before the run reaches it.
    view.altitudeBands = loadAltitudeBands(view);
    view.waterfallBodyArt = loadImage(view.baseUrl + SHEET_FILES.waterfallBody);
    view.waterfallCrestArt = loadImage(view.baseUrl + SHEET_FILES.waterfallCrest);
    view.vineTextureArt = view.hasVine ? loadImage(view.baseUrl + SHEET_FILES.vineTexture) : null;
    view.vineTreeArt = view.hasVine ? loadImage(view.baseUrl + SHEET_FILES.vineTree) : null;
    view.vineBranchArt = view.hasVine ? loadImage(view.baseUrl + SHEET_FILES.vineBranch) : null;
    view.toriiBackArt = loadImage(view.baseUrl + SHEET_FILES.toriiBack);
    view.toriiFrontArt = loadImage(view.baseUrl + SHEET_FILES.toriiFront);
    view.ghostPoofArt = loadImage(view.baseUrl + SHEET_FILES.ghostPoof);
    view.ghostWarpInArt = loadImage(view.baseUrl + SHEET_FILES.ghostWarpIn);
    readDecorations(view, course);
    view.climbShadowArt = view.climbs.length > 0 ? loadImage(view.baseUrl + SHEET_FILES.climbShadow) : null;

    /** Every sheet this course draws from, each once, which is what `whenReady` waits for. */
    view.courseArt = [...new Set([
        view.runnerArt, view.ghostRunnerArt, view.crestArt, view.hazardArt,
        ...view.altitudeBands.flatMap(band => band.planes.map(plane => plane.art)),
        view.waterfallBodyArt, view.waterfallCrestArt, view.vineTextureArt, view.vineTreeArt, view.vineBranchArt,
        view.toriiBackArt, view.toriiFrontArt, view.ghostPoofArt, view.ghostWarpInArt,
        ...view.decorationArt.values(), view.climbShadowArt,
    ].filter(entry => entry !== null))];

    view.cameraX = view.startX;
    view.cameraY = groundTopAt(view, view.startX, 0);
    /** The camera height the backdrop's own parallax is measured from: where the course starts. */
    view.backdropAnchorY = view.cameraY;

    const testData = view.testData;
    testData.artReady = 'false';
    testData.droopingBridgeCount = String(view.droopingBridges.length);
    testData.droopingBridgeSpan = view.droopingBridges.length === 1
        ? (view.droopingBridges[0].endX - view.droopingBridges[0].startX).toFixed(3) : '';
    testData.bridgeWaterfallPresent = view.droopingBridges.some(bridge => bridge.hasWaterfall) ? 'true' : 'false';
    testData.bridgeWaterfallReducedMotion = view.reducedMotion ? 'true' : 'false';
    testData.bridgeReducedMotion = view.reducedMotion ? 'true' : 'false';
    testData.bridgeState = view.droopingBridges.length > 0 ? 'resting' : 'absent';
    testData.vineCount = String(view.encounters.filter(encounter => encounter && encounter.kind === 'vine').length);
    testData.ghostPoofCount = '0';
    testData.ghostWarpInCount = '0';
    testData.pitCount = String(view.pits.length);
    testData.terrainCrestStages = ALTITUDE_BANDS.slice(view.startBandIndex).map(band => band.name).join(' ');
    testData.decorationCount = String(view.decorations.filter(d => d.kind !== null).length);
    testData.decorationBehindCount = String(view.decorations.filter(d => d.kind !== null && !d.front).length);
    testData.decorationFrontCount = String(view.decorations.filter(d => d.kind !== null && d.front).length);
    testData.decorationPassByCount = '0';
    testData.decorationPassByPlaying = '0';
    testData.decorationHeldCount = '0';
    testData.climbStakePassByCount = '0';
    testData.climbShadowSweepCount = '0';
    testData.climbShadowPlaying = 'false';
    testData.runnerSheetReady = 'false';
    testData.runnerFrame = '';
    publishRunnerCounts(view);

    view.terrainPieces = buildTerrainPieces(view);

    view.observer = typeof ResizeObserver === 'function'
        ? new ResizeObserver(() => { resize(view); })
        : null;
    if (view.observer !== null)
        view.observer.observe(canvas);

    resize(view);

    return {
        render: snapshot => render(view, snapshot),
        renderPreview: (courseX, courseY) => renderPreview(view, courseX, courseY),
        resize: () => resize(view),
        setPixelsPerMetre: value => setPixelsPerMetre(view, value),
        previewProjection: () => previewProjection(view),
        playGhostEffects: () => playGhostEffects(view),
        playDecorationPassBy: () => playDecorationPassBy(view),
        whenReady: onProgress => whenReady(view, onProgress),
        dispose: () => dispose(view),
    };
}

// ---------------------------------------------------------------------------
// Viewport
// ---------------------------------------------------------------------------

/**
 * Re-reads the canvas' CSS box and device pixel ratio, resizing the backing store to match.
 * @returns {void}
 */
function resize(view) {
    const nextDpr = Math.min(window.devicePixelRatio || 1, MAX_DPR);
    const cssW = Math.max(1, view.canvas.clientWidth || view.canvas.width || 1);
    const cssH = Math.max(1, view.canvas.clientHeight || view.canvas.height || 1);
    const bufferW = Math.round(cssW * nextDpr);
    const bufferH = Math.round(cssH * nextDpr);

    if (view.canvas.width !== bufferW || view.canvas.height !== bufferH) {
        view.canvas.width = bufferW;
        view.canvas.height = bufferH;
    }

    view.dpr = nextDpr;
    view.viewW = cssW;
    view.viewH = cssH;
    view.pxPerMetre = view.opts.pxPerMetre > 0 ? view.opts.pxPerMetre : cssW / view.opts.metresAcross;
    view.groundLineY = cssH * view.opts.groundLineFraction;
    view.runnerScreenX = cssW * view.opts.runnerScreenFraction;

    view.ctx.setTransform(view.dpr, 0, 0, view.dpr, 0, 0);
    view.ctx.imageSmoothingEnabled = false;

    // One screen-space gradient settles everything under the ground line into shadow, so ground and
    // pit walls alike get darker with depth.
    view.depthShadeTop = view.groundLineY - view.viewH * 0.1;
    view.depthShade = view.ctx.createLinearGradient(0, view.depthShadeTop, 0, view.viewH);
    view.depthShade.addColorStop(0, 'rgba(2, 8, 24, 0)');
    view.depthShade.addColorStop(0.45, 'rgba(2, 8, 24, 0.3)');
    view.depthShade.addColorStop(1, DEPTH_SHADE_COLOR);

    view.hazardSheet.dirty = true;
}

/**
 * Prepares the viewport for a frame: advances the renderer's clock by the wall-clock time since the last frame
 * (none on a frame that `restartsClock`, and at most 50 ms) scaled by `clockScale`, picks up a changed device
 * pixel ratio, and rescales the hazards sheet if the viewport has changed since it was last scaled.
 * @param {boolean} restartsClock
 * @param {number} clockScale
 * @returns {number} the frame's clock step, in seconds
 */
function beginFrame(view, restartsClock, clockScale) {
    const nowMs = (typeof performance === 'object' && performance !== null) ? performance.now() : Date.now();
    const dt = restartsClock ? 0 : Math.min(Math.max((nowMs - view.lastFrameMs) / 1000, 0), 0.05) * clockScale;
    view.lastFrameMs = nowMs;
    view.animMs += dt * 1000;

    if (Math.min(window.devicePixelRatio || 1, MAX_DPR) !== view.dpr)
        resize(view);
    if (view.testData.runnerSheetReady !== 'true')
        view.testData.runnerSheetReady = view.runnerArt.ready ? 'true' : 'false';
    if (view.hazardSheet.dirty)
        prepareHazardSheet(view);
    return dt;
}

// ---------------------------------------------------------------------------
// Frame
// ---------------------------------------------------------------------------

/**
 * Draws one frame from a render snapshot.
 * @param {TimeTrialSnapshot} snapshot
 * @returns {void}
 */
function render(view, snapshot) {
    const state = snapshot || {};
    const known = typeof state.mode === 'string' || typeof state.x === 'number';
    const x = finiteOr(state.x, view.startX);
    const groundY = groundTopAt(view, x, view.cameraY);
    const y = finiteOr(state.y, groundY);
    const vx = finiteOr(state.vx, 0);
    const vy = finiteOr(state.vy, 0);
    const mode = typeof state.mode === 'string' ? state.mode : 'Running';
    const step = finiteOr(state.step, view.lastStep + 1);
    const finished = state.finished === true || mode === 'Finished';
    // Every run starts in stride, running in to the start line, so only a frame with nothing to draw
    // from is drawn waiting, on the run cycle's first frame.
    const waiting = !known;

    // Wall-clock delta drives the animation, slowed by the slow-motion knob along with the run's own clock;
    // the simulation clock only says when a run restarted.
    const dt = beginFrame(view, view.firstFrame, timeScale());

    // Camera: X sits on the runner exactly. Y eases toward the ground under them, so a step in the
    // ground slides into view instead of jumping. Over a pit that ground is the lip the runner left, not
    // the pit's floor: the view holds its height across the gap, and a runner who drops in is watched
    // from above rather than chased down the shaft. The exception is between a climb's two
    // torii, where it is pinned to the runner: the runner then holds one screen height for the whole
    // ascent and the terrain and the backdrop are what move. Both torii stand on level ground the
    // runner is already at, so the pin is taken and given back without a step in either direction.
    // Past the finish line nothing changes: the camera follows the runner through the run-out the same
    // way, over the ground the course view carries on beyond the line, and it follows the run-in to the
    // start line over the ground carried back behind it. The run-in's locked button is not a climb, so it
    // does not pin the camera.
    const restarted = view.firstFrame || step < view.lastStep;
    const climbing = mode === 'Climbing' || (state.inputLocked === true && state.runIn !== true);
    const cameraGroundY = cameraGroundAt(view, x, groundY);
    view.cameraX = x;
    if (restarted) {
        view.cameraY = cameraGroundY;
        view.playerAnimation = createRunnerAnimation();
        view.altitudeBandIndex = 0;
        view.altitudeFromBandIndex = 0;
        view.altitudeBlend = 1;
        // A new run brings a new lineup, so the opponents' animation phases and states start over with it.
        view.ghostRunPhases = [];
        view.ghostAnimations = [];
    } else if (climbing) {
        view.cameraY = y;
    } else {
        view.cameraY += (cameraGroundY - view.cameraY) * (1 - Math.exp(-dt / view.opts.cameraEaseSeconds));
    }
    view.lastStep = step;
    view.firstFrame = false;

    advanceAltitude(view, x, dt);

    updateDroopingBridges(view, state, dt, restarted);
    updateGhostEffects(view, state, restarted);
    updateDecorations(view, x, restarted);
    updateClimbDressing(view, state, restarted);

    const encounterPhase = typeof state.encounterPhase === 'string' ? state.encounterPhase : '';
    const rollChange = waiting ? '' : updateRunnerAnimation(view, view.playerAnimation, mode, encounterPhase, y, vx, view.playerRunnerRules);
    if (rollChange === 'started')
        view.playerRollsStarted += 1;
    else if (rollChange === 'interrupted')
        view.playerRollsInterrupted += 1;

    view.runPhase += (Math.abs(vx) * dt) / RUNNER_TUNING.metresPerRunCycle;
    if (view.runPhase > 1e6)
        view.runPhase = 0;

    drawScene(view, state, x, y, dt);
    drawRunner(view,
        x, y + bridgeSurfaceOffsetAt(view, x), vx, vy, mode, finiteOr(state.charge, 0), finished, waiting,
        encounterPhase,
        finiteOr(state.encounterProgress, 0),
        finiteOr(state.activeEncounterIndex, -1));
    publishRunnerCounts(view);
    drawToriiFront(view);
    drawDecorations(view, true);
    drawClimbShadow(view);
}

/**
 * Draws a course with no run in progress: the camera stands at course position `courseX`, at the height
 * the run camera would settle on there, in the altitude band a run is in at that point, and everything a
 * run draws is drawn except the runner and the field. This is how the developer course tools look along a
 * course with the renderer a player sees it through. The next `render` starts its run over.
 *
 * A finite `courseY` holds the camera at that height instead, which is how the part editor keeps the view
 * from dropping as its camera passes over a pit.
 * @param {number} courseX
 * @param {number} [courseY]
 * @returns {void}
 */
function renderPreview(view, courseX, courseY) {
    const x = finiteOr(courseX, view.startX);

    // The preview's clock is the wall clock: the slow-motion knob slows a run, not the preview.
    const dt = beginFrame(view, view.lastFrameMs === 0, 1);

    view.cameraX = x;
    view.cameraY = Number.isFinite(courseY) ? courseY : previewCameraY(view, x);
    showAltitudeAt(view, x);
    view.firstFrame = true;
    view.lastStep = -1;

    // Nobody is on the course, so nothing loads a bridge deck. The only effects that play are the ones
    // `playGhostEffects` starts; a run's own effects never outlive it, because the next `render` starts
    // its run over.
    const state = { x: x, mode: 'Finished', finished: true, elapsedMs: 0 };
    updateDroopingBridges(view, state, dt, false);
    expireGhostEffects(view);
    updateDecorations(view, null, false);
    // Nothing climbs in a preview, so no shadow sweeps.
    updateClimbDressing(view, state, true);
    view.testData.previewX = x.toFixed(2);
    drawScene(view, state, x, view.cameraY, dt);
    drawToriiFront(view);
    drawDecorations(view, true);
}

/**
 * Everything but the player's runner, from the backdrop to the field: shared by a run's frame and the
 * preview, so the preview is exactly what the run draws.
 */
function drawScene(view, state, x, y, dt) {
    view.ctx.imageSmoothingEnabled = false;
    drawBackdrop(view);
    drawGroundedVineTree(view);
    drawPits(view);
    // Only the air under the ground line takes the depth shade; the terrain over it is already black,
    // and its crest stays crisp at any depth.
    view.ctx.fillStyle = view.depthShade;
    view.ctx.fillRect(0, view.depthShadeTop, view.viewW, view.viewH - view.depthShadeTop);
    drawBridgeChasms(view);
    drawBridgeWaterfalls(view);
    drawTorii(view);
    drawDecorations(view, false);
    drawTerrain(view);
    drawTerrainCrest(view);
    drawHazards(view);
    drawVines(view, state, x, y);
    drawDroopingBridges(view);
    drawGhostRunners(view, state, dt);
    drawGhostEffects(view);
}

/**
 * Stops observing the canvas and drops the scaled art. The renderer must not be used afterwards.
 * @returns {void}
 */
function dispose(view) {
    view.disposed = true;
    if (view.observer !== null)
        view.observer.disconnect();
    releaseHazardSheet(view);
    if (view.tintCanvas !== null) {
        view.tintCanvas.width = 0;
        view.tintCanvas.height = 0;
        view.tintCanvas = null;
        view.tintCtx = null;
    }
}

/**
 * Draws at a fixed `value` CSS pixels per metre from now on, or at the `metresAcross` scale for 0.
 * @param {number} value
 * @returns {void}
 */
function setPixelsPerMetre(view, value) {
    const next = Number.isFinite(value) && value > 0 ? value : 0;
    if (next === view.opts.pxPerMetre)
        return;
    view.opts.pxPerMetre = next;
    resize(view);
}

/**
 * The camera the last frame was drawn with, so an overlay can place things on the course exactly where
 * the renderer did: canvas X is `runnerScreenX + (x - cameraX) * pxPerMetre` and canvas Y is
 * `groundLineY - (y - cameraY) * pxPerMetre`, in CSS pixels. `runCameraY` is the height a run's camera
 * settles on at `cameraX`, whatever height the preview holds the camera at.
 * @returns {{ pxPerMetre: number, cameraX: number, cameraY: number, runnerScreenX: number, groundLineY: number, viewW: number, viewH: number, runCameraY: number }}
 */
function previewProjection(view) {
    return {
        pxPerMetre: view.pxPerMetre,
        cameraX: view.cameraX,
        cameraY: view.cameraY,
        runnerScreenX: view.runnerScreenX,
        groundLineY: view.groundLineY,
        viewW: view.viewW,
        viewH: view.viewH,
        runCameraY: previewCameraY(view, view.cameraX),
    };
}

/**
 * Resolves once everything this course draws from is in: every sheet arrived and decoded (or given up
 * on, for one that never arrives, since the run draws without it) and the work the first frames would
 * otherwise do on them done here instead: the hazards sheet scaled to the viewport, and every backdrop
 * plane's base measured, so neither the first frame nor a band change has anything left to compute.
 * `onProgress` is told how many sheets have settled out of how many, at once and again as each one
 * settles. The canvas' `data-art-ready` says the same to a browser test. A renderer disposed meanwhile
 * still resolves, preparing nothing.
 * @param {(settled: number, total: number) => void} [onProgress]
 * @returns {Promise<void>}
 */
function whenReady(view, onProgress) {
    const total = view.courseArt.length;
    let settled = 0;
    const report = () => {
        if (typeof onProgress === 'function')
            onProgress(settled, total);
    };
    report();
    return Promise.all(view.courseArt.map(entry => entry.promise.then(() => {
        settled += 1;
        report();
    }))).then(() => {
        if (view.disposed)
            return;
        view.testData.runnerSheetReady = view.runnerArt.ready ? 'true' : 'false';
        view.hazardSheet.dirty = true;
        prepareHazardSheet(view);
        measureBackdropBases(view);
        view.testData.artReady = 'true';
    });
}
