/**
 * The field: the opponents drawn from the baked ghost sheet behind the player, and the poofs and warp-ins that
 * cover them leaving the course and coming back.
 */

import { finiteOr } from '../util.js';
import { bridgeSurfaceOffsetAt } from './bridges.js';
import { groundTopAt } from './courseModel.js';
import { RUNNER_TUNING, createRunnerAnimation, pullOverPoint, runnerFrameFor, updateRunnerAnimation } from './runnerAnimation.js';
import { blitRunnerFrame, drawSpeedLines, runnerFrameRect } from './runnerDraw.js';
import { GATE_OPENING, GATE_PX_PER_METRE, GATE_TORII_RECT } from './torii.js';

/**
 * The cyan effects that cover a field member leaving and the field coming back, both 1× pixel-art horizontal
 * sheets drawn at `GHOST_EFFECT_PX_PER_METRE` and played once on the renderer's own clock. The poof
 * (`SHEET_FILES.ghostPoof`) stands where an opponent was last drawn as it leaves at a climb's bottom torii; the
 * warp-in (`SHEET_FILES.ghostWarpIn`) stands under the gate for the whole field at the regroup. `anchorX` / `baseY` is the source point planted on the ground, within each frame.
 *
 * The warp-in's frames are 192 × 260, its mist spanning columns 42..150 of every frame. It is drawn at its
 * height's scale and squeezed sideways, nearest-neighbour, so that span fills `GATE_OPENING` exactly: 108
 * source columns across 1.104 m, about 97.8 px per metre against the height's 32, a third of its width.
 */
const GHOST_EFFECT_PX_PER_METRE = 32;
const GHOST_POOF = { frameW: 64, frameH: 64, frames: 4, anchorX: 32, baseY: 56, frameMs: 90 };
const GHOST_WARP_IN = {
    frameW: 192, frameH: 260, frames: 4, anchorX: 96, baseY: 258, frameMs: 100,
    mistLeft: 42, mistRight: 150,
};

/** The `hideReason`s that mean a torii took the opponent off the course, which it leaves in a poof. */
const TORII_HIDE_REASONS = new Set(['climb', 'finish']);

/**
 * Where in the run cycle, 0 to 1, the opponent at lineup position `index` starts: spaced round the cycle by the
 * golden ratio from the player's own start, so any number of runners land on well-separated, fractional points
 * of it, with a little jitter so no two runs line up the same.
 */
function ghostRunPhaseStart(index) {
    const spread = (index + 1) * 0.6180339887;
    const jitter = (Math.random() - 0.5) * 0.08;
    return spread + jitter - Math.floor(spread + jitter);
}

/**
 * Starts the poofs and the warp-ins from two consecutive snapshots. The field is on the course only between
 * the torii at its two ends, and every torii it passes through plays one of the two effects. An opponent
 * that was drawn last frame and is hidden now for a torii that takes it, its snapshot's `hideReason` the
 * bottom torii of a climb (`climb`) or the finish line (`finish`), leaves in a poof where it was last drawn. The field comes back under one
 * warp-in when it is drawn: under the start torii as the run-in ends and the field arrives on step 0, and
 * under a gate at the regroup, the player's snapshot passing it. A new run starts over with nothing playing.
 * With reduced motion nothing plays: opponents leave and return as a cut.
 */
export function updateGhostEffects(view, state, restarted) {
    const ghosts = Array.isArray(state.ghosts) ? state.ghosts : [];
    const nextGateIndex = finiteOr(state.nextGateIndex, 0);
    const runIn = state.runIn === true;
    if (restarted) {
        view.ghostEffects = [];
        view.ghostSightings = [];
        view.lastNextGateIndex = nextGateIndex;
        view.lastRunIn = runIn;
    }

    for (let index = 0; index < ghosts.length; index++) {
        const ghost = ghosts[index];
        if (ghost === null || typeof ghost !== 'object')
            continue;
        const hidden = ghost.hidden === true;
        const seen = view.ghostSightings[index];
        if (hidden && seen !== undefined && !seen.hidden && TORII_HIDE_REASONS.has(ghost.hideReason))
            startGhostEffect(view, GHOST_POOF, seen.x, seen.y);

        const gx = finiteOr(ghost.x, view.startX);
        view.ghostSightings[index] = hidden
            ? { hidden: true, x: seen !== undefined ? seen.x : gx, y: seen !== undefined ? seen.y : 0 }
            : { hidden: false, x: gx, y: finiteOr(ghost.y, groundTopAt(view, gx, view.cameraY)) };
    }

    const fieldDrawn = ghosts.some(ghost => ghost !== null && typeof ghost === 'object' && ghost.hidden !== true);
    if (view.lastRunIn && !runIn && fieldDrawn)
        startGhostEffect(view, GHOST_WARP_IN, view.startX, view.torii[0].groundY);
    view.lastRunIn = runIn;

    if (nextGateIndex > view.lastNextGateIndex) {
        const climb = view.climbs[nextGateIndex - 1];
        if (climb !== undefined && fieldDrawn)
            startGhostEffect(view, GHOST_WARP_IN, climb.gateX, climb.topY);
    }
    view.lastNextGateIndex = nextGateIndex;

    expireGhostEffects(view);
}

/** Drops the effects that have played their last frame, and publishes how many of each have started. */
export function expireGhostEffects(view) {
    view.ghostEffects = view.ghostEffects.filter(effect => view.animMs - effect.startMs < effect.spec.frames * effect.spec.frameMs);
    view.testData.ghostPoofCount = String(view.ghostPoofCount);
    view.testData.ghostWarpInCount = String(view.ghostWarpInCount);
}

/**
 * Plays the ghost effects once at every torii, from the next preview frame on, so the developer course
 * tools can judge them in place: a warp-in under the start torii and each climb's gate, where the field
 * arrives, and a poof at each climb's bottom torii and the finish line, where it leaves, the same torii a
 * run plays them at. A part composed alone has only its start and finish torii, which stand where the
 * previous climb's gate and the next climb's bottom torii would on a map. They play whatever the
 * reduced-motion setting, because a developer asked for them. Only the preview calls this; a run starts its
 * own effects from its snapshots.
 * @returns {void}
 */
export function playGhostEffects(view) {
    const poofs = view.climbs.map(climb => ({ x: climb.bottomX, y: climb.bottomY }));
    poofs.push({ x: view.finishX, y: groundTopAt(view, view.finishX, 0) });
    const warpIns = [{ x: view.startX, y: groundTopAt(view, view.startX, 0) }];
    for (const climb of view.climbs)
        warpIns.push({ x: climb.gateX, y: climb.topY });
    for (const poof of poofs) {
        view.ghostEffects.push({ spec: GHOST_POOF, x: poof.x, y: poof.y, startMs: view.animMs });
        view.ghostPoofCount += 1;
    }
    for (const warpIn of warpIns) {
        view.ghostEffects.push({ spec: GHOST_WARP_IN, x: warpIn.x, y: warpIn.y, startMs: view.animMs });
        view.ghostWarpInCount += 1;
    }
    view.testData.ghostPoofCount = String(view.ghostPoofCount);
    view.testData.ghostWarpInCount = String(view.ghostWarpInCount);
}

/** Plays one effect from this frame on, planted on the ground at course position `x`, height `y`. */
function startGhostEffect(view, spec, x, y) {
    if (view.reducedMotion)
        return;
    view.ghostEffects.push({ spec: spec, x: x, y: y, startMs: view.animMs });
    if (spec === GHOST_POOF)
        view.ghostPoofCount += 1;
    else
        view.ghostWarpInCount += 1;
}

/**
 * The poofs and warp-ins still playing, over the field and behind the player's runner and every torii's
 * near post: each belongs to the opponents, so it stands where they do.
 */
export function drawGhostEffects(view) {
    for (const effect of view.ghostEffects) {
        const art = effect.spec === GHOST_POOF ? view.ghostPoofArt : view.ghostWarpInArt;
        if (!art.ready)
            continue;
        const spec = effect.spec;
        const frame = Math.min(spec.frames - 1, Math.floor((view.animMs - effect.startMs) / spec.frameMs));
        const k = view.pxPerMetre / GHOST_EFFECT_PX_PER_METRE;
        // The warp-in's mist is fitted to the gate's opening: its span pinned across the opening, squeezed
        // sideways, at its own height.
        let centreX = view.screenX(effect.x);
        let anchorX = spec.anchorX;
        let kx = k;
        if (spec === GHOST_WARP_IN) {
            const openingLeft = (GATE_OPENING.left - GATE_TORII_RECT.anchorX) / GATE_PX_PER_METRE;
            const openingRight = (GATE_OPENING.right - GATE_TORII_RECT.anchorX) / GATE_PX_PER_METRE;
            kx = (openingRight - openingLeft) * view.pxPerMetre / (spec.mistRight - spec.mistLeft);
            centreX = view.screenX(effect.x + openingLeft);
            anchorX = spec.mistLeft;
        }
        if (centreX + (spec.frameW - anchorX) * kx < 0 || centreX - anchorX * kx > view.viewW)
            continue;
        const dx = view.alignPx(centreX - anchorX * kx);
        const dy = view.alignPx(view.screenY(effect.y) - spec.baseY * k);
        view.ctx.drawImage(art.image, frame * spec.frameW, 0, spec.frameW, spec.frameH, dx, dy,
            Math.max(1, Math.round(spec.frameW * kx)), Math.max(1, Math.round(spec.frameH * k)));
    }
}

/**
 * The opponents, drawn behind the player. They are drawn back to front (the one expected to finish
 * last first) so the runner the player is chasing sits on top of the pack rather than under it.
 *
 * An opponent between its own bottom torii and its gate is not drawn at all: each is on its own stretch
 * of the climb, so the field is only together again at the regroup the gate makes. It leaves in a poof and
 * the field comes back under one warp-in (see `updateGhostEffects`). Its run cycle keeps advancing while it
 * is away, so it comes back mid-stride rather than resetting to a standing frame, and it comes back with no
 * roll playing and no airborne stretch behind it.
 *
 * Each opponent has its own animation state and is drawn from its own snapshot's mode, encounter phase and
 * progress, so it climbs out of a pit and rolls off a high landing exactly as the player does, by its own
 * rules.
 */
export function drawGhostRunners(view, state, dt) {
    const ghosts = Array.isArray(state.ghosts) ? state.ghosts : [];
    if (ghosts.length === 0)
        return;

    while (view.ghostRunPhases.length < ghosts.length)
        view.ghostRunPhases.push(ghostRunPhaseStart(view.ghostRunPhases.length));
    while (view.ghostAnimations.length < ghosts.length)
        view.ghostAnimations.push(createRunnerAnimation());

    for (let index = ghosts.length - 1; index >= 0; index--) {
        const ghost = ghosts[index];
        if (ghost === null || typeof ghost !== 'object')
            continue;

        view.ghostRunPhases[index] += (Math.abs(finiteOr(ghost.vx, 0)) * dt) / RUNNER_TUNING.metresPerRunCycle;
        if (view.ghostRunPhases[index] > 1e6)
            view.ghostRunPhases[index] -= Math.floor(view.ghostRunPhases[index]);

        if (ghost.hidden === true) {
            view.ghostAnimations[index] = createRunnerAnimation();
            continue;
        }

        const anim = view.ghostAnimations[index];
        const gx = finiteOr(ghost.x, view.startX);
        const gy = finiteOr(ghost.y, groundTopAt(view, gx, view.cameraY));
        const gvx = finiteOr(ghost.vx, 0);
        const phase = typeof ghost.encounterPhase === 'string' ? ghost.encounterPhase : '';
        const mode = typeof ghost.mode === 'string' ? ghost.mode : 'Running';
        const rules = view.ghostRunnerRules[index] ?? view.playerRunnerRules;
        updateRunnerAnimation(view, anim, mode, phase, gy, gvx, rules);

        drawGhostRunner(view, 
            gx,
            gy + bridgeSurfaceOffsetAt(view, gx),
            gvx,
            finiteOr(ghost.vy, 0),
            mode,
            view.ghostRunPhases[index],
            phase,
            finiteOr(ghost.encounterProgress, 0),
            finiteOr(ghost.activeEncounterIndex, -1),
            anim,
            rules);
    }
}

/**
 * One opponent, drawn from the baked ghost sheet (every frame already cyan, softened and glowing) as baked,
 * in the frame the player would be in.
 */
function drawGhostRunner(view, x, y, vx, vy, mode, runPhaseValue, phase = '', progress = 0, encounterIndex = -1, anim = null, rules = view.playerRunnerRules) {
    const frame = runnerFrameFor(view, mode, vy, false, runPhaseValue, phase, progress, anim, y, encounterIndex, rules);
    const fast = vx - view.opts.speedLineThreshold;
    if (fast > 0)
        drawSpeedLines(view, view.screenX(x), view.screenY(y), view.opts.runnerHeightMetres * view.pxPerMetre, Math.min(fast / 6, 1));

    if (!view.ghostRunnerArt.ready)
        return;

    const pullOver = mode === 'Traversing' ? pullOverPoint(view, phase, progress, anim, x, y, encounterIndex, rules) : null;
    const rect = pullOver !== null ? runnerFrameRect(view, frame, pullOver.x, pullOver.y, 1, 1, 0) : runnerFrameRect(view, frame, x, y, 1, 1, 0);
    view.ctx.save();
    if (typeof RUNNER_TUNING.ghostFilter === 'string' && RUNNER_TUNING.ghostFilter !== 'none')
        view.ctx.filter = RUNNER_TUNING.ghostFilter;
    blitRunnerFrame(view, view.ghostRunnerArt.image, rect, rect.dx);
    view.ctx.restore();
}
