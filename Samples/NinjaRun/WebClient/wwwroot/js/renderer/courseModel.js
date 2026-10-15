/**
 * The course as the renderer reads it: the course view normalised onto the `view` once, and the queries the
 * drawing asks of it: the ground under a point, the lips of a pit, a climb's slope, the altitude band a point
 * lies in.
 */

import { finiteOr } from '../util.js';
import { ALTITUDE_BANDS } from './artSpecs.js';

/**
 * Reads the course view onto `view`: its ground, pits, spike spans, bridges, climbs, encounters and torii, the
 * player's body, and the rules each runner is drawn by. Every list is copied with its numbers made finite, so
 * nothing drawn later has to check a field.
 * @param {*} course
 */
export function readCourse(view, course) {
    // The band the course starts in: the foothills, unless the course names another: a part previewed or
    // played alone from the part editor, which has no climb to carry it up, so its theme says where it stands.
    view.startBandIndex = Math.max(0, ALTITUDE_BANDS.findIndex(band => band.name === (course && course.altitudeBand)));

    view.encounters = Array.isArray(course && course.encounters) ? course.encounters : [];
    view.hasVine = view.encounters.some(encounter => encounter && encounter.kind === 'vine');
    /** How far above the feet the simulation holds the rope; the same on every vine. */
    view.vineGripHeight = finiteOr(view.encounters.find(encounter => encounter && encounter.kind === 'vine')?.gripHeight, 0);

    view.ground = (Array.isArray(course && course.ground) ? course.ground : [])
        .slice()
        .sort((a, b) => a.startX - b.startX);
    // A pit is a shaft with a floor rather than a gap, and the course says where every one of them is. Its
    // floor arrives as an ordinary ground segment, so nothing here has to work one out from the ground.
    view.pits = (Array.isArray(course && course.pits) ? course.pits : [])
        .filter(pit => pit && finiteOr(pit.endX, 0) > finiteOr(pit.startX, 0))
        .map(pit => ({
            startX: finiteOr(pit.startX, 0),
            endX: finiteOr(pit.endX, 0),
            floorY: finiteOr(pit.floorY, 0),
            depth: Math.max(0, finiteOr(pit.depth, 0)),
        }));
    view.hazards = Array.isArray(course && course.hazards) ? course.hazards : [];
    view.droopingBridges = (Array.isArray(course && course.droopingBridges) ? course.droopingBridges : [])
        .filter(bridge => finiteOr(bridge.endX, 0) > finiteOr(bridge.startX, 0))
        .map(bridge => {
            const logCount = Math.max(3, Math.round(finiteOr(bridge.logCount, 9)));
            return {
                id: typeof bridge.id === 'string' ? bridge.id : '',
                startX: finiteOr(bridge.startX, 0),
                endX: finiteOr(bridge.endX, 0),
                collisionY: finiteOr(bridge.collisionY, 0),
                restSag: Math.max(0, finiteOr(bridge.restSag, 0)),
                maxLocalDroop: Math.max(0, finiteOr(bridge.maxLocalDroop, 0)),
                responseRadius: Math.max(0.1, finiteOr(bridge.responseRadius, 1)),
                logCount: logCount,
                hasWaterfall: bridge.hasWaterfall === true,
                offsets: new Array(logCount).fill(0),
                velocities: new Array(logCount).fill(0),
            };
        });
    // A climb's gap in the ground is its slope, not a pit, and its two torii stand at the ends of the stretch
    // the course owns the runner over: the bottom one on the flat before the slope's foot.
    view.climbs = (Array.isArray(course && course.climbs) ? course.climbs : [])
        .filter(climb => climb && finiteOr(climb.topX, 0) > finiteOr(climb.slopeStartX, finiteOr(climb.bottomX, 0)))
        .map(climb => ({
            id: typeof climb.id === 'string' ? climb.id : '',
            bottomX: finiteOr(climb.bottomX, 0),
            bottomY: finiteOr(climb.bottomY, 0),
            slopeStartX: finiteOr(climb.slopeStartX, finiteOr(climb.bottomX, 0)),
            topX: finiteOr(climb.topX, 0),
            topY: finiteOr(climb.topY, 0),
            gateX: finiteOr(climb.gateX, finiteOr(climb.topX, 0)),
        }));
    const bodySource = course && course.playerBody ? course.playerBody : {};
    view.playerBody = {
        leftExtent: finiteOr(bodySource.leftExtent, 0.8),
        rightExtent: finiteOr(bodySource.rightExtent, 0.85),
        height: finiteOr(bodySource.height, 1.7),
    };
    // The rules each runner moves by, as far as its drawing needs them: the player's effective rules, and each
    // opponent's own by lineup position. An opponent with no entry is drawn by the player's.
    const playerRunnerRules = readRunnerRules(course && course.runnerRules);
    view.playerRunnerRules = playerRunnerRules;
    view.ghostRunnerRules = (Array.isArray(course && course.ghostRunnerRules) ? course.ghostRunnerRules : [])
        .map(rules => rules === null || rules === undefined ? playerRunnerRules : readRunnerRules(rules));
    view.startX = finiteOr(course && course.startX, 0);
    view.finishX = finiteOr(course && course.finishX, view.startX + 100);
    // Every torii, in course order, each planted on the ground at its course position: the start line, both
    // ends of every climb, and the finish line.
    view.torii = [{ x: view.startX, groundY: groundTopAt(view, view.startX, 0) }];
    for (const climb of view.climbs) {
        view.torii.push({ x: climb.bottomX, groundY: climb.bottomY });
        view.torii.push({ x: climb.gateX, groundY: climb.topY });
    }
    view.torii.push({ x: view.finishX, groundY: groundTopAt(view, view.finishX, 0) });
}

/**
 * What drawing a runner needs from the rules it moves by, from the course view's `runnerRules`:
 *
 * - `canRoll`: whether a high landing plays the roll: only a runner on a run has rules, so a course drawn with
 *   no run on it never rolls.
 * - `ledgeClearance`: how far above the lip a mantle's rise lifts the feet, which is where the rise ends.
 * - `ledgeRiseSpeed`, `ledgeTopTransitionSeconds`: how fast the rise climbs and how long the top transition
 *   lasts, which share the pull-over's straight line out between the two.
 *
 * @param {*} source `{ ledgeGrabClearanceHeight, ledgeGrabRiseSpeed, ledgeGrabTopTransitionSeconds }`
 * @returns {{ canRoll: boolean, ledgeClearance: number, ledgeRiseSpeed: number, ledgeTopTransitionSeconds: number }}
 */
function readRunnerRules(source) {
    const present = source !== null && typeof source === 'object';
    const rules = present ? source : {};
    return {
        canRoll: present,
        ledgeClearance: Math.max(0, finiteOr(rules.ledgeGrabClearanceHeight, 0)),
        ledgeRiseSpeed: Math.max(0.1, finiteOr(rules.ledgeGrabRiseSpeed, 4)),
        ledgeTopTransitionSeconds: Math.max(0, finiteOr(rules.ledgeGrabTopTransitionSeconds, 1 / 6)),
    };
}

/**
 * Ground height under a course position, or `fallback` where nothing carries the runner: a climb's
 * slope, or past either end of the course. A pit answers with its floor.
 * @param {number} x
 * @param {number} fallback
 * @returns {number}
 */
export function groundTopAt(view, x, fallback) {
    for (let i = 0; i < view.ground.length; i++) {
        const seg = view.ground[i];
        if (x >= seg.startX && x <= seg.endX)
            return seg.topY;
    }
    return fallback;
}

/**
 * The ground height the camera follows at a course position: the ground under it, except over a pit,
 * where it is the near lip the runner crossed from, so the view never dips toward a pit's floor.
 * @param {number} x
 * @param {number} groundY the ground under `x`, as `groundTopAt` answers it
 * @returns {number}
 */
export function cameraGroundAt(view, x, groundY) {
    for (const pit of view.pits) {
        if (x > pit.startX && x < pit.endX)
            return groundTopEndingAt(view, pit.startX, groundY);
    }
    return groundY;
}

/**
 * The surface a climb carries the runner over at `x`: its slope's line between the foot and the top of the
 * slope, the flat it starts from before that, and the ground it arrives on after.
 */
export function slopeSurfaceAt(climb, x) {
    if (x <= climb.slopeStartX)
        return climb.bottomY;
    if (x >= climb.topX)
        return climb.topY;
    return climb.bottomY + (climb.topY - climb.bottomY) * (x - climb.slopeStartX) / (climb.topX - climb.slopeStartX);
}

/** Height of the ground that ends exactly at `x`, which for a pit's near lip is the ground it falls from. */
export function groundTopEndingAt(view, x, fallback) {
    for (const seg of view.ground) {
        if (Math.abs(seg.endX - x) < 1e-6)
            return seg.topY;
    }
    return fallback;
}

/** Height of the ground that begins exactly at `x`: for a pit's far lip, what the mantle arrives on. */
export function groundTopStartingAt(view, x, fallback) {
    for (const seg of view.ground) {
        if (Math.abs(seg.startX - x) < 1e-6)
            return seg.topY;
    }
    return fallback;
}

/**
 * The climb whose slope fills the gap between two ground segments, or null where nothing does. Every pit
 * has a floor of its own, so a slope is the only thing left that fills a gap in the ground.
 * @param {number} fromX right edge of the segment before the gap
 * @param {number} toX left edge of the segment after it
 */
export function climbFillingGap(view, fromX, toX) {
    for (const climb of view.climbs) {
        if (Math.abs(climb.slopeStartX - fromX) < 1e-6 && Math.abs(climb.topX - toX) < 1e-6)
            return climb;
    }
    return null;
}

/** Whether the course lets the runner catch the face standing at this course position. */
export function isCatchableFace(view, x) {
    return view.encounters.some(encounter => encounter
        && encounter.kind === 'ledge-grab'
        && Math.abs(finiteOr(encounter.x, NaN) - x) < 1e-3);
}

/** The altitude band a course position lies in: the starting band, one higher per slope midpoint passed. */
export function bandIndexAt(view, x) {
    return Math.min(ALTITUDE_BANDS.length - 1, view.startBandIndex + slopeMidpointsPassed(view, x));
}

/** The crest stage terrain at a course position is dressed in: the name of the altitude band it lies in. */
export function crestStageAt(view, x) {
    return ALTITUDE_BANDS[bandIndexAt(view, x)].name;
}

/**
 * The camera height a run settles on at `x`: the ground there, the near lip over a pit, and the slope's
 * own height between a climb's torii, where a run's camera is pinned to the climbing runner.
 * @param {number} x
 * @returns {number}
 */
export function previewCameraY(view, x) {
    for (const climb of view.climbs) {
        if (x > climb.slopeStartX && x < climb.topX)
            return slopeSurfaceAt(climb, x);
    }
    return cameraGroundAt(view, x, groundTopAt(view, x, view.cameraY));
}

/**
 * How many climb slopes the runner is past the middle of. Each one is an altitude band change, so this
 * counts the bands the run has climbed above the first.
 */
function slopeMidpointsPassed(view, x) {
    let passed = 0;
    for (const climb of view.climbs) {
        if (x >= (climb.slopeStartX + climb.topX) / 2)
            passed += 1;
    }
    return passed;
}
