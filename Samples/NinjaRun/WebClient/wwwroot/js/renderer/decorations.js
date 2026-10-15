/**
 * The course's decorations (the props its parts place, and the warding fence along every climb's slope) with
 * their pass-by animations, and the bamboo shadow that sweeps the view as a climb takes the runner.
 */

import { finiteOr } from '../util.js';
import { decorationKind, climbFenceKind } from '../courseDecorations.js';
import { loadImage } from './imageCache.js';
import { slopeSurfaceAt } from './courseModel.js';
import { drawPinned } from './drawHelpers.js';

/**
 * What a climb dresses its hands-off stretch with, none of it touching the run.
 *
 * `shadow` is a bamboo shadow cast over the scene once per climb: `climb-bamboo-shadow-sweep`
 * (`SHEET_FILES.climbShadow`), drawn in
 * screen space exactly as authored: its opacity is the art's, not this module's. It enters from the right as the runner
 * is taken at the bottom torii, crosses the view in `sweepSeconds` and is gone before the gate, over everything
 * lit (backdrop, crest, decorations, torii, runner) under the HUD, which is not on this canvas. Over the
 * near-black terrain it is invisible by construction. The runner is being carried up the mountain, so the
 * shadow also sinks: it is drawn `dropFraction` of the view taller than the view and slides down by that much
 * over the sweep, its top edge starting above the frame so the stalk is never seen cut off. With reduced motion
 * nothing sweeps.
 *
 * The fence is the catalogue's climb-fence kind (`courseDecorations.js`), planted every `spacingMetres` along
 * the slope from its foot to its top, with the shimenawa strung between consecutive stakes as a slight catenary.
 */
const CLIMB_SHADOW = { srcW: 512, srcH: 1024, sweepSeconds: 3.0, dropFraction: 0.3 };
const CLIMB_FENCE = {
    /** The first and last stake stand this far in from the ends of the slope, clear of the flat and the plateau. */
    marginMetres: 1.2,
    /** How far the rope hangs below the straight line between two ties, at its middle. */
    sagMetres: 0.18,
    rope: '#c9b37a',
    ropeWidthMetres: 0.045,
};

/**
 * Reads the course's decorations onto `view`, and plants the fence along every climb's slope. Each kind's
 * sheet is loaded once, for the kinds the course uses (`view.decorationArt`). A kind the catalogue does not
 * know is kept only to be counted: there is nothing to draw it with.
 * @param {*} course
 */
export function readDecorations(view, course) {
    // The props the course's parts place, each standing on the ground point the course view gives it.
    view.decorations = (Array.isArray(course && course.decorations) ? course.decorations : [])
        .filter(decoration => decoration && typeof decoration.kind === 'string')
        .map(decoration => ({
            id: typeof decoration.id === 'string' ? decoration.id : '',
            kindId: decoration.kind,
            kind: decorationKind(decoration.kind),
            x: finiteOr(decoration.x, 0),
            y: finiteOr(decoration.y, 0),
            front: decoration.layer === 'front',
            flip: decoration.flip === true,
            /** Whether the player's runner has reached this run's trigger point for its pass-by animation. */
            passed: false,
            /** When its pass-by animation started on `animMs`, or -1 while it shows its idle frame. */
            playStartMs: -1,
            /** Whether it holds its sequence's last frame: a hold-last kind the runner has passed this run. */
            held: false,
        }));
    /** Each decoration kind's sheet, loaded once for the kinds this course uses. */
    view.decorationArt = new Map();
    for (const decoration of view.decorations) {
        if (decoration.kind !== null && !view.decorationArt.has(decoration.kindId))
            view.decorationArt.set(decoration.kindId, loadImage(view.baseUrl + decoration.kind.file));
    }
    const unknownDecorationKinds = [...new Set(view.decorations.filter(d => d.kind === null).map(d => d.kindId))];
    if (unknownDecorationKinds.length > 0)
        console.warn(`[timeTrialRenderer] No decoration kind ${unknownDecorationKinds.join(', ')} in the catalogue; those decorations are not drawn.`);

    // The warding fence along every climb's slope: the catalogue's climb-fence kind planted on the slope's own
    // line every so many metres, from a margin in from the foot of the slope to the same margin short of its top, each stake a decoration of its own with the same pass-by as any other, and each climb's stakes
    // in course order so the rope can be strung between neighbours. No part places these.
    const fence = climbFenceKind();
    view.climbStakes = [];
    for (let climbIndex = 0; climbIndex < view.climbs.length && fence !== null; climbIndex++) {
        const climb = view.climbs[climbIndex];
        const from = climb.slopeStartX + CLIMB_FENCE.marginMetres;
        const to = climb.topX - CLIMB_FENCE.marginMetres;
        const spacing = Math.max(0.5, finiteOr(fence.kind.spacingMetres, 2));
        const count = Math.max(0, Math.floor((to - from) / spacing) + 1);
        // Centre the row on the slope, so both ends keep the same distance from the slope's ends.
        const first = from + ((to - from) - (count - 1) * spacing) / 2;
        for (let ndx = 0; ndx < count; ndx++) {
            const x = first + ndx * spacing;
            view.climbStakes.push({
                id: `${climb.id}-stake-${ndx + 1}`,
                kindId: fence.id,
                kind: fence.kind,
                x: x,
                y: slopeSurfaceAt(climb, x),
                front: false,
                flip: false,
                passed: false,
                playStartMs: -1,
                held: false,
                climbIndex: climbIndex,
            });
        }
        if (!view.decorationArt.has(fence.id))
            view.decorationArt.set(fence.id, loadImage(view.baseUrl + fence.kind.file));
    }
}

/**
 * Sets off and runs out the decorations' pass-by animations. Only the player's runner sets one off (the
 * field is many, and a prop that rustled for every opponent would never be still), the
 * first frame its feet reach `triggerMetres` short of the decoration's X, once per run. A new run starts
 * with every decoration idle, and one the runner starts already past stays idle. An animation plays on
 * this renderer's own clock and returns to the idle frame when its last frame has been held. With reduced
 * motion the trigger is still counted but nothing plays. `playerX` null (the preview, where nobody is
 * running) only lets animations already playing run out.
 * @param {number|null} playerX
 * @param {boolean} restarted
 */
export function updateDecorations(view, playerX, restarted) {
    let playing = 0;
    let held = 0;
    for (const decoration of view.decorations) {
        const outcome = advancePassBy(view, decoration, playerX, restarted);
        if (outcome.triggered)
            view.decorationPassByCount += 1;
        if (outcome.playing)
            playing += 1;
        if (decoration.held)
            held += 1;
    }
    for (const stake of view.climbStakes) {
        const outcome = advancePassBy(view, stake, playerX, restarted);
        if (outcome.triggered)
            view.climbStakePassByCount += 1;
        if (outcome.playing)
            playing += 1;
    }
    view.testData.decorationPassByCount = String(view.decorationPassByCount);
    view.testData.decorationPassByPlaying = String(playing);
    view.testData.decorationHeldCount = String(held);
    view.testData.climbStakePassByCount = String(view.climbStakePassByCount);
}

/**
 * One decoration's pass-by state for this frame: whether the runner set it off now, and whether it is
 * playing. A kind with no pass-by is neither.
 * @returns {{ triggered: boolean, playing: boolean }}
 */
function advancePassBy(view, decoration, playerX, restarted) {
    const passBy = decoration.kind !== null ? decoration.kind.passBy : undefined;
    if (passBy === undefined)
        return { triggered: false, playing: false };
    const triggerX = decoration.x - finiteOr(passBy.triggerMetres, 0);
    let triggered = false;

    if (restarted) {
        decoration.passed = playerX !== null && playerX >= triggerX;
        decoration.playStartMs = -1;
        decoration.held = false;
    } else if (playerX !== null && !decoration.passed && playerX >= triggerX) {
        decoration.passed = true;
        triggered = true;
        // A hold-last kind is changed for good by the pass, and with reduced motion it cuts straight to
        // that: the pass still happened, only the motion is left out.
        decoration.held = passBy.holdLast === true;
        if (!view.reducedMotion)
            decoration.playStartMs = view.animMs;
    }

    if (decoration.playStartMs >= 0 && view.animMs - decoration.playStartMs >= passBy.sequence.length * passBy.frameMs)
        decoration.playStartMs = -1;
    return { triggered: triggered, playing: decoration.playStartMs >= 0 };
}

/**
 * The decorations of one layer: `front` false for the ones standing behind the field, drawn with the
 * torii's far layer before the terrain so the terrain covers each one's base row and it reads planted, and
 * true for the ones in front of everything. Each is its kind's sheet enlarged nearest-neighbour from the
 * kind's own pixels per metre, its anchor pinned on its ground point, mirrored about that anchor when
 * flipped, and skipped when it is off screen.
 * @param {boolean} front
 */
export function drawDecorations(view, front) {
    if (view.decorations.length === 0 && view.climbStakes.length === 0)
        return;
    view.ctx.imageSmoothingEnabled = false;
    const leftX = view.viewLeftX();
    const rightX = view.viewRightX();
    if (!front) {
        // The rope goes under the stakes, so each stake's tie covers the rope's end.
        drawClimbFenceRope(view, leftX, rightX);
        for (const stake of view.climbStakes)
            drawDecorationSprite(view, stake, leftX, rightX);
    }
    for (const decoration of view.decorations) {
        if (decoration.front === front)
            drawDecorationSprite(view, decoration, leftX, rightX);
    }
}

/**
 * One decoration's current frame on its ground point (the frame its pass-by is on, the last frame a
 * hold-last kind keeps after the pass, or otherwise the rest frame) or nothing while its sheet loads or it
 * is off screen.
 */
function drawDecorationSprite(view, decoration, leftX, rightX) {
    const kind = decoration.kind;
    if (kind === null)
        return;
    const art = view.decorationArt.get(decoration.kindId);
    if (art === undefined || !art.ready)
        return;
    const reach = Math.max(kind.anchorX, kind.frameW - kind.anchorX) / kind.pxPerMetre;
    if (decoration.x + reach < leftX || decoration.x - reach > rightX)
        return;

    let frame = 0;
    if (decoration.playStartMs >= 0 && kind.passBy !== undefined) {
        const sequence = kind.passBy.sequence;
        const step = Math.floor((view.animMs - decoration.playStartMs) / kind.passBy.frameMs);
        frame = sequence[Math.min(sequence.length - 1, Math.max(0, step))];
    } else if (decoration.held && kind.passBy !== undefined) {
        frame = kind.passBy.sequence[kind.passBy.sequence.length - 1];
    }
    const rect = { x: frame * kind.frameW, y: 0, w: kind.frameW, h: kind.frameH };
    const pinX = view.screenX(decoration.x);
    const pinY = view.screenY(decoration.y);
    if (!decoration.flip) {
        drawPinned(view, art.image, rect, rect.x + kind.anchorX, kind.baseY, pinX, pinY, kind.pxPerMetre);
        return;
    }
    view.ctx.save();
    view.ctx.translate(view.alignPx(pinX), 0);
    view.ctx.scale(-1, 1);
    drawPinned(view, art.image, rect, rect.x + kind.anchorX, kind.baseY, 0, pinY, kind.pxPerMetre);
    view.ctx.restore();
}

/**
 * The shimenawa between each climb's consecutive stakes: one stroke from tie to tie, sagging a little at
 * its middle, drawn in code because every part's slope has its own rise and a rope baked into the sheet
 * would meet the next stake at the wrong height.
 */
function drawClimbFenceRope(view, leftX, rightX) {
    if (view.climbStakes.length < 2)
        return;
    view.ctx.save();
    view.ctx.strokeStyle = CLIMB_FENCE.rope;
    view.ctx.lineWidth = Math.max(1, CLIMB_FENCE.ropeWidthMetres * view.pxPerMetre);
    view.ctx.lineCap = 'round';
    view.ctx.beginPath();
    for (let ndx = 1; ndx < view.climbStakes.length; ndx++) {
        const from = view.climbStakes[ndx - 1];
        const to = view.climbStakes[ndx];
        if (from.climbIndex !== to.climbIndex || to.x < leftX || from.x > rightX)
            continue;
        const tieHeight = (from.kind.baseY - finiteOr(from.kind.ropeTieY, 0)) / from.kind.pxPerMetre;
        const x0 = view.screenX(from.x);
        const y0 = view.screenY(from.y + tieHeight);
        const x1 = view.screenX(to.x);
        const y1 = view.screenY(to.y + tieHeight);
        // A quadratic through the midpoint sagging `sagMetres` below the chord: the control point sits
        // twice as far down.
        view.ctx.moveTo(x0, y0);
        view.ctx.quadraticCurveTo((x0 + x1) / 2, (y0 + y1) / 2 + 2 * CLIMB_FENCE.sagMetres * view.pxPerMetre, x1, y1);
    }
    view.ctx.stroke();
    view.ctx.restore();
}

/**
 * Starts and runs the bamboo shadow's sweep from this frame's snapshot: reaching a bottom torii starts it,
 * and it drops out once it has crossed. A new run starts with nothing sweeping. With reduced motion nothing
 * sweeps.
 */
export function updateClimbDressing(view, state, restarted) {
    const nextClimbIndex = finiteOr(state.nextClimbIndex, 0);
    if (restarted) {
        view.climbShadow = null;
        view.lastNextClimbIndex = nextClimbIndex;
    } else if (nextClimbIndex > view.lastNextClimbIndex && view.climbShadowArt !== null && !view.reducedMotion) {
        view.climbShadow = { startMs: view.animMs };
        view.climbShadowSweepCount += 1;
    }
    view.lastNextClimbIndex = nextClimbIndex;

    if (view.climbShadow !== null && view.animMs - view.climbShadow.startMs >= CLIMB_SHADOW.sweepSeconds * 1000)
        view.climbShadow = null;

    view.testData.climbShadowSweepCount = String(view.climbShadowSweepCount);
    view.testData.climbShadowPlaying = view.climbShadow !== null ? 'true' : 'false';
}

/**
 * Where the sweeping shadow is on the canvas this frame, `{ left, top, width, height }` in CSS pixels, or
 * null. It crosses from off the right edge to off the left over the sweep, and sinks with the mountain: taller
 * than the view by `dropFraction`, its top starts that far above the frame and reaches the frame's top edge
 * as the sweep ends, by which time it has left to the left.
 */
function climbShadowRect(view) {
    if (view.climbShadow === null)
        return null;
    const height = view.viewH * (1 + CLIMB_SHADOW.dropFraction);
    const width = height * CLIMB_SHADOW.srcW / CLIMB_SHADOW.srcH;
    const progress = Math.min(1, (view.animMs - view.climbShadow.startMs) / (CLIMB_SHADOW.sweepSeconds * 1000));
    return {
        left: view.viewW - progress * (view.viewW + width),
        top: -(1 - progress) * CLIMB_SHADOW.dropFraction * view.viewH,
        width: width,
        height: height,
    };
}

/**
 * The bamboo shadow crossing the view, over everything the frame has drawn. It is a cast shadow, so it is
 * drawn smoothed, not as pixel art.
 */
export function drawClimbShadow(view) {
    const sweep = climbShadowRect(view);
    if (sweep === null || view.climbShadowArt === null || !view.climbShadowArt.ready)
        return;
    view.ctx.imageSmoothingEnabled = true;
    view.ctx.drawImage(view.climbShadowArt.image, sweep.left, sweep.top, sweep.width, sweep.height);
    view.ctx.imageSmoothingEnabled = false;
}

/**
 * Plays every decoration's pass-by animation once from the next preview frame on, the sequence a run plays
 * when the runner reaches it, so the part editor can judge it in place. It plays whatever the reduced-motion
 * setting, because a developer asked for it, and counts as a trigger. A hold-last kind plays through and
 * returns to its rest frame here, the frame the editor places it by. Only the preview calls this.
 * @returns {void}
 */
export function playDecorationPassBy(view) {
    for (const decoration of view.decorations) {
        if (decoration.kind === null || decoration.kind.passBy === undefined)
            continue;
        decoration.playStartMs = view.animMs;
        view.decorationPassByCount += 1;
    }
    view.testData.decorationPassByCount = String(view.decorationPassByCount);
}
