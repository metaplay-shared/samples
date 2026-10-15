/**
 * The runner's animation: the runner sheet's frames and anchors, the live tuning knobs (`hsrRunnerTuning`), and
 * the per-runner state that picks the frame a runner is drawn in: the high-landing roll, the mantle's climb
 * cycle and its pull-over.
 */

import { finiteOr } from '../util.js';

/**
 * The runner sheet, `ninja-sprite-set`: 1× pixel art at
 * `RUNNER_SRC_PX_PER_METRE`, one 128 × 160 px cell per frame, one row per animation, frames left to right.
 * The standing runner is about 102 source pixels tall, `RUNNER_SRC_HEIGHT_METRES` at that scale, so the
 * `runnerHeightMetres` option draws the sheet at `pxPerMetre × runnerHeightMetres / RUNNER_SRC_HEIGHT_METRES`
 * source pixels to the metre, exactly 64 at the default 1.6 m. It is drawn from the image as it is,
 * nearest-neighbour, like the crest and the torii. The opponents draw the same cells from
 * `ninja-sprite-set-ghost`, baked from it.
 */
export const RUNNER_CELL_W = 128;
export const RUNNER_CELL_H = 160;
export const RUNNER_SRC_PX_PER_METRE = 64;
export const RUNNER_SRC_HEIGHT_METRES = 1.6;

/**
 * One frame: its cell's row and column, and the source point pinned to the course. A `feet` frame pins
 * `anchorX` / `anchorY` on the runner's course position, its feet; a `grip` frame pins its hand grip on the
 * catchable face at the height the runner's body reaches; a `rope` frame pins its hand on the vine's grip
 * point (see `runnerPin`).
 */
function runnerFrame(name, row, col, anchorX, anchorY, pin = 'feet') {
    return { name: name, row: row, col: col, anchorX: anchorX, anchorY: anchorY, pin: pin };
}

/** The pit-climb grip, the same source pixel on its first four frames. */
const CLIMB_GRIP = { x: 100, y: 32 };
/** The raised fist of the jump row's rising frame, which the swing holds the rope by. */
const VINE_FIST = { x: 98, y: 26 };
/** The body-centre column of each high-landing roll cell, by sheet column. */
const ROLL_BODY_X = [45, 52, 59, 68, 76, 64];
/** The roll's sheet columns in playback order: the sheet has its third and fourth frames the wrong way round. */
const ROLL_COLUMNS = [0, 1, 3, 2, 4, 5];

export const RUNNER_FRAMES = {
    /** The run cycle, six frames. Its rate is tied to `vx` through `RUNNER_TUNING.metresPerRunCycle`. */
    run: [0, 1, 2, 3, 4, 5].map(col => runnerFrame(`run-${col}`, 0, col, 64, 150)),
    charge: runnerFrame('charge', 1, 0, 64, 150),
    rising: runnerFrame('jump-rising', 2, 0, 64, 140),
    apex: runnerFrame('jump-apex', 2, 1, 64, 140),
    falling: runnerFrame('jump-falling', 2, 2, 64, 140),
    stumble: runnerFrame('stumble', 3, 0, 64, 150),
    /** catch, kneeUp, pull, kneeOver (hung from the grip) then push and topCrouch, on the feet. */
    climb: [
        runnerFrame('climb-0', 4, 0, CLIMB_GRIP.x, CLIMB_GRIP.y, 'grip'),
        runnerFrame('climb-1', 4, 1, CLIMB_GRIP.x, CLIMB_GRIP.y, 'grip'),
        runnerFrame('climb-2', 4, 2, CLIMB_GRIP.x, CLIMB_GRIP.y, 'grip'),
        runnerFrame('climb-3', 4, 3, CLIMB_GRIP.x, CLIMB_GRIP.y, 'grip'),
        runnerFrame('climb-4', 4, 4, 10, 150),
        runnerFrame('climb-5', 4, 5, 35, 150),
    ],
    /** touchdown, tuck, inverted, shoulder, push, runHandoff: the body centre travels forward through the cells. */
    roll: ROLL_COLUMNS.map(col => runnerFrame(`roll-${col}`, 5, col, ROLL_BODY_X[col], 150)),
    /** The jump row's rising frame, hung from the rope by its raised fist. */
    vine: runnerFrame('vine-hang', 2, 0, VINE_FIST.x, VINE_FIST.y, 'rope'),
};

/**
 * How far above the feet the grip sits on the catch frame, in metres at the default runner height: rows 150
 * to 32. A grip frame's hands are pinned this far above the simulation's feet, so the catch frame stands
 * exactly where the simulation holds the runner and the climb frames move the body against the hands.
 */
export const CLIMB_GRIP_REACH_METRES = (150 - CLIMB_GRIP.y) / RUNNER_SRC_PX_PER_METRE;

/**
 * Metres of rise one pass of the three-frame climb cycle covers. The pass count is the rise's height over
 * this, rounded and at least one, so every rise ends on the knee-over frame the pull-over follows.
 */
const CLIMB_METRES_PER_CYCLE = 1.2;

/**
 * The runner's animation timing, one object so it can be tuned live from the browser console while a run is on
 * the stage (`hsrRunnerTuning.metresPerRunCycle = 4`); the renderer reads it every frame. The values are set by
 * eye in play.
 *
 * - `timeScale`: slow motion for looking at animation: the run's clock and the renderer's both advance at this
 *   fraction of real time (`hsrRunnerTuning.timeScale = 0.2`). The run itself is unchanged, only slower.
 * - `metresPerRunCycle`: ground one full six-frame run cycle covers, which ties the animation rate to `vx`.
 * - `rollMetres`: ground the high-landing roll covers at the speed it lands at.
 *   `rollMinSpeed` floors that speed so a slow landing does not stretch the roll out, and `rollMaxMs` caps it.
 * - `highLandingDrop`: the drop, from the top of an airborne stretch to the ground it lands on, past which the
 *   landing plays the roll, in metres; above what a full-charge jump on the flat reaches.
 * - `mantlePushLead`: how far below the top of a mantle's rise the pull-over takes over from the climb cycle,
 *   in metres, so the ninja pushes over as the body clears the lip. From there to the
 *   end of the top transition the runner is drawn on one straight line, up and to the right, from where the
 *   pull-over starts to where it crouches on top, instead of the simulation's rise straight up and then across.
 * - `mantleCrouchAt`: how far along that line, 0 to 1, the crouch on top replaces the pull-over frame.
 * - `ghostFilter`: a canvas filter every opponent is drawn through, for trying other looks on the baked sheet
 *   (`hsrRunnerTuning.ghostFilter = 'brightness(0.2)'` for dark shadows); `'none'` draws the sheet as baked.
 * - `frames`: every runner frame (`RUNNER_FRAMES`): its sheet `row` and `col`, the source pixel `anchorX` /
 *   `anchorY` pinned to the runner, and `pin`, `'feet'` (on the simulation's feet) or `'grip'` (hands on the
 *   catchable face). Editing one moves it on the next frame (`hsrRunnerTuning.frames.climb[4].anchorY = 140`).
 */
export const RUNNER_TUNING = {
    timeScale: 1,
    metresPerRunCycle: 3.4,
    rollMetres: 4,
    rollMinSpeed: 4,
    rollMaxMs: 800,
    highLandingDrop: 3.5,
    mantlePushLead: 1.2,
    mantleCrouchAt: 0.4,
    ghostFilter: 'none',
    frames: RUNNER_FRAMES,
};
globalThis.hsrRunnerTuning = RUNNER_TUNING;
/** Vertical speed inside which an airborne runner reads as hanging at the apex, in m/s. */
const APEX_VY = 2.5;

/**
 * `RUNNER_TUNING.timeScale`, kept to a usable range. The run's host slows the run's own clock by the same
 * value, so the run and its drawing slow down together.
 * @returns {number}
 */
export function timeScale() {
    const scale = finiteOr(RUNNER_TUNING.timeScale, 1);
    return scale > 0 ? Math.min(scale, 4) : 1;
}

/**
 * A runner's own animation state, one for the player and one per lineup position: the mode and height it had on
 * the last frame, the top of the airborne stretch it is in, the rise start of the mantle it is in, and the
 * one-shot animation playing on the renderer's clock, if any.
 */
export function createRunnerAnimation() {
    return {
        lastMode: '',
        lastY: 0,
        airPeakY: -Infinity,
        riseStartY: NaN,
        oneShot: '',
        oneShotStartMs: 0,
        oneShotMs: 0,
    };
}

/**
 * Advances one runner's animation state from this frame's snapshot, before it is drawn. It tracks the top of
 * the airborne stretch the runner is in (from the height it left the ground at) and the height a mantle's
 * rise starts from, and starts, ends and cuts short the high-landing roll:
 *
 * - A roll starts when `Airborne` becomes `Running` and the drop from the top of the airborne stretch to the
 *   landing exceeds `RUNNER_TUNING.highLandingDrop`, for a runner whose rules allow it, unless motion is
 *   reduced: then the runner goes straight back into the run.
 * - It plays for `RUNNER_TUNING.rollMetres` of ground at the landing speed on the renderer's clock.
 * - Any mode but `Running` cuts it short at once, and the frame follows the new mode from then on.
 *
 * @returns {'' | 'started' | 'interrupted'} whether the roll started or was cut short this frame
 */
export function updateRunnerAnimation(view, anim, mode, phase, y, vx, rules) {
    let result = '';
    if (mode === 'Airborne')
        anim.airPeakY = Math.max(anim.lastMode === 'Airborne' || anim.lastMode === '' ? anim.airPeakY : anim.lastY, y);

    if (phase === 'ledge-hang' || (phase === 'ledge-rise' && !Number.isFinite(anim.riseStartY)))
        anim.riseStartY = y;
    else if (!phase.startsWith('ledge-'))
        anim.riseStartY = NaN;

    if (anim.oneShot === 'roll') {
        if (mode !== 'Running') {
            anim.oneShot = '';
            result = 'interrupted';
        } else if (view.animMs - anim.oneShotStartMs >= anim.oneShotMs) {
            anim.oneShot = '';
        }
    }

    if (anim.lastMode === 'Airborne' && mode === 'Running' && !view.reducedMotion
        && rules.canRoll && anim.airPeakY - y > RUNNER_TUNING.highLandingDrop) {
        anim.oneShot = 'roll';
        anim.oneShotStartMs = view.animMs;
        anim.oneShotMs = Math.min(RUNNER_TUNING.rollMaxMs,
            (1000 * RUNNER_TUNING.rollMetres) / Math.max(Math.abs(vx), RUNNER_TUNING.rollMinSpeed));
        result = 'started';
    }

    if (mode !== 'Airborne')
        anim.airPeakY = -Infinity;
    anim.lastMode = mode;
    anim.lastY = y;
    return result;
}

/**
 * Picks the frame a runner is drawn in. Every mode has a frame of its own, so the frame alone carries what
 * the runner is doing; the only timed animation is the high-landing roll, which `updateRunnerAnimation`
 * starts and stops. An ordinary landing goes straight back into the run cycle. Crossing the finish line
 * changes nothing here: a finished runner is running on past the line, and is drawn from its mode like any
 * other.
 *
 * A mantle, out of a pit or up a step, is one climb for all of them: the catch
 * while hanging, the three-frame climb cycle through the rise, then the pull-over and the crouch on top
 * through the top transition. The cycle is driven by the rise's own progress, never the clock, so the frame
 * always matches how far up the face the simulation has the runner. Every other traversal is a vine swing.
 *
 * @param anim The runner's animation state, or null for a runner that has none.
 */
export function runnerFrameFor(view, mode, vy, waiting, cyclePhase, phase, progress, anim, y, encounterIndex, rules) {
    if (waiting)
        return RUNNER_FRAMES.run[0];
    if (mode === 'Charging')
        return RUNNER_FRAMES.charge;
    if (mode === 'Airborne') {
        // Scraping up a face below its catch height: reaching for it.
        if (phase === 'ledge-wall-rise')
            return RUNNER_FRAMES.climb[0];
        if (phase === 'ledge-impact')
            return RUNNER_FRAMES.falling;
        return vy > APEX_VY ? RUNNER_FRAMES.rising : vy < -APEX_VY ? RUNNER_FRAMES.falling : RUNNER_FRAMES.apex;
    }
    if (mode === 'Stumbling')
        return RUNNER_FRAMES.stumble;
    if (mode === 'Traversing') {
        const pullOver = pullOverPoint(view, phase, progress, anim, 0, y, encounterIndex, rules);
        if (pullOver !== null)
            return pullOver.u < RUNNER_TUNING.mantleCrouchAt ? RUNNER_FRAMES.climb[4] : RUNNER_FRAMES.climb[5];
        if (phase === 'ledge-rise')
            return RUNNER_FRAMES.climb[climbRiseFrameIndex(view, progress, anim, y, encounterIndex, rules)];
        if (phase === 'ledge-top-transition')
            return RUNNER_FRAMES.climb[5];
        if (phase.startsWith('ledge-'))
            return RUNNER_FRAMES.climb[0];
        return RUNNER_FRAMES.vine;
    }

    if (mode === 'Running' && anim !== null && anim.oneShot === 'roll') {
        const index = Math.floor(((view.animMs - anim.oneShotStartMs) / anim.oneShotMs) * RUNNER_FRAMES.roll.length);
        return RUNNER_FRAMES.roll[Math.min(Math.max(index, 0), RUNNER_FRAMES.roll.length - 1)];
    }

    // The phase may be negative, and a plain remainder would index off the front of the cycle.
    const cycle = RUNNER_FRAMES.run;
    const frame = Math.floor(cyclePhase * cycle.length) % cycle.length;
    return cycle[(frame + cycle.length) % cycle.length];
}

/**
 * The pull-over, or null outside it: from `RUNNER_TUNING.mantlePushLead` metres below the top of a mantle's
 * rise through the whole top transition. `u` runs 0 to 1 along it (the rest of the rise and the transition
 * each take their share by the time they last) and `x`, `y` is where the runner is drawn: on the straight
 * line from the face at the height the pull-over starts to the ledge's landing point on top. A catch already
 * inside the lead starts the pull-over at the catch.
 */
export function pullOverPoint(view, phase, progress, anim, x, y, encounterIndex, rules) {
    if (phase !== 'ledge-rise' && phase !== 'ledge-top-transition')
        return null;
    const ledge = view.encounters.find(encounter => encounter && finiteOr(encounter.index, -1) === encounterIndex);
    if (ledge === undefined)
        return null;
    const topY = finiteOr(ledge.topY, y);
    const endY = topY + rules.ledgeClearance;
    const startY = anim !== null && Number.isFinite(anim.riseStartY) ? anim.riseStartY : y;
    const fromY = Math.max(endY - Math.max(0, finiteOr(RUNNER_TUNING.mantlePushLead, 0)), startY);
    const faceX = finiteOr(ledge.x, x) - view.playerBody.rightExtent;
    const landingX = finiteOr(ledge.landingX, faceX);

    const riseSeconds = Math.max(0, endY - fromY) / rules.ledgeRiseSpeed;
    const total = riseSeconds + rules.ledgeTopTransitionSeconds;
    const riseShare = total > 0 ? riseSeconds / total : 0;
    let u;
    if (phase === 'ledge-rise') {
        if (y < fromY)
            return null;
        u = endY > fromY ? riseShare * Math.min((y - fromY) / (endY - fromY), 1) : 0;
    } else {
        u = riseShare + (1 - riseShare) * Math.min(Math.max(progress, 0), 1);
    }
    return { u: u, x: faceX + (landingX - faceX) * u, y: fromY + (topY - fromY) * u };
}

/**
 * Which of the three climb-cycle frames (1–3) a mantle's rise is on below the pull-over: they repeat as many
 * times as the climbing height holds `CLIMB_METRES_PER_CYCLE`, at least once, spread evenly over it by the
 * feet's height. With no height to climb, the rise's progress spreads the cycle instead.
 */
function climbRiseFrameIndex(view, progress, anim, y, encounterIndex, rules) {
    const ledge = view.encounters.find(encounter => encounter && finiteOr(encounter.index, -1) === encounterIndex);
    const startY = anim !== null && Number.isFinite(anim.riseStartY) ? anim.riseStartY : y;
    const endY = ledge !== undefined ? finiteOr(ledge.topY, y) + rules.ledgeClearance : y;
    const pushFromY = endY - Math.max(0, finiteOr(RUNNER_TUNING.mantlePushLead, 0));
    const climbHeight = Math.min(pushFromY, endY) - startY;
    const passes = Math.max(1, Math.round(climbHeight / CLIMB_METRES_PER_CYCLE));
    const frames = passes * 3;
    const u = climbHeight > 0 ? (y - startY) / climbHeight : progress;
    const at = Math.min(frames - 1, Math.floor(Math.min(Math.max(u, 0), 1) * frames));
    return 1 + (at % 3);
}
