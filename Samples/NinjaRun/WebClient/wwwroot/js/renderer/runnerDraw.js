/**
 * The player's runner on screen: its frame from the runner sheet pinned where the simulation has it, with the
 * charge meter, the speed lines and the stumble flash. The opponents are drawn through the same frame
 * placement (`ghostField.js`).
 */

import { CLIMB_GRIP_REACH_METRES, RUNNER_CELL_H, RUNNER_CELL_W, RUNNER_FRAMES, RUNNER_SRC_HEIGHT_METRES, RUNNER_SRC_PX_PER_METRE, pullOverPoint, runnerFrameFor } from './runnerAnimation.js';

// ---------------------------------------------------------------------------
// Look and feel
// ---------------------------------------------------------------------------

const CHARGE_BACK = 'rgba(8, 14, 30, 0.72)';
const CHARGE_LOW = '#ffd54a';
const CHARGE_HIGH = '#ff5d2e';
const STUMBLE_TINT = '#ff4d3d';
const SPEED_LINE_COLOR = 'rgba(200, 226, 255, 0.85)';

/** Publishes the player's roll and climb counts on the canvas, for the tests. */
export function publishRunnerCounts(view) {
    view.testData.runnerRollsStarted = String(view.playerRollsStarted);
    view.testData.runnerRollsInterrupted = String(view.playerRollsInterrupted);
    view.testData.runnerClimbFramesDrawn = String(view.playerClimbFramesDrawn);
}

/** CSS pixels drawn per source pixel of the runner sheet: its scale at the runner's height, at this zoom. */
function runnerScale(view) {
    return (view.pxPerMetre * (view.opts.runnerHeightMetres / RUNNER_SRC_HEIGHT_METRES)) / RUNNER_SRC_PX_PER_METRE;
}

/**
 * The screen point a frame's anchor is pinned to for a runner whose feet the simulation has at `x`, `y`. A
 * feet frame stands on them. A grip frame hangs its hands on the catchable face: the simulation holds the
 * body's leading edge against it, so the face is at `x + playerBody.rightExtent`, at the height the body
 * reaches: `CLIMB_GRIP_REACH_METRES` above the feet, scaled with the runner. The catch frame therefore stands
 * exactly on the simulation's feet, and at the catch from a pit floor its hands are within a metre of the lip.
 * A rope frame hangs its hand on the vine's grip point, `vineGripHeight` above the feet, where the rope ends.
 */
function runnerPin(view, frame, x, y) {
    if (frame.pin === 'grip') {
        const reach = CLIMB_GRIP_REACH_METRES * (view.opts.runnerHeightMetres / RUNNER_SRC_HEIGHT_METRES);
        return { x: view.screenX(x + view.playerBody.rightExtent), y: view.screenY(y + reach) };
    }
    if (frame.pin === 'rope')
        return { x: view.screenX(x), y: view.screenY(y + view.vineGripHeight) };
    return { x: view.screenX(x), y: view.screenY(y) };
}

/**
 * Where a frame lands on screen: its cell, scaled by `runnerScale` and `stretchX` / `stretchY` about its
 * anchor, with the anchor on `runnerPin` and shifted `offsetX` CSS pixels.
 */
export function runnerFrameRect(view, frame, x, y, stretchX, stretchY, offsetX) {
    const k = runnerScale(view);
    const pin = runnerPin(view, frame, x, y);
    return {
        sx: frame.col * RUNNER_CELL_W,
        sy: frame.row * RUNNER_CELL_H,
        dx: view.alignPx(pin.x - frame.anchorX * k * stretchX + offsetX),
        dy: view.alignPx(pin.y - frame.anchorY * k * stretchY),
        dw: Math.max(1, Math.round(RUNNER_CELL_W * k * stretchX)),
        dh: Math.max(1, Math.round(RUNNER_CELL_H * k * stretchY)),
    };
}

/** Draws one runner cell from `image` at `dx`, nearest-neighbour. */
export function blitRunnerFrame(view, image, rect, dx) {
    view.ctx.imageSmoothingEnabled = false;
    view.ctx.drawImage(image, rect.sx, rect.sy, RUNNER_CELL_W, RUNNER_CELL_H, dx, rect.dy, rect.dw, rect.dh);
}

/** The runner, plus the charge meter, speed lines and the stumble flash. */
export function drawRunner(view, x, y, vx, vy, mode, charge, finished, waiting, encounterPhase = '', encounterProgress = 0, encounterIndex = -1) {
    const frame = runnerFrameFor(view, mode, vy, waiting, view.runPhase, encounterPhase, encounterProgress,
        view.playerAnimation, y, encounterIndex, view.playerRunnerRules);
    view.testData.runnerFrame = frame.name;
    const feetY = view.screenY(y);
    const centreX = view.screenX(x);
    // The standing runner's box with a little headroom: what the charge meter and the speed lines are
    // spaced off.
    const boxH = view.opts.runnerHeightMetres * 1.05 * view.pxPerMetre;

    let stretchY = 1;
    let stretchX = 1;
    let shakeX = 0;

    if (mode === 'Charging' && !finished && !waiting) {
        // The crouch compresses a touch more as the charge builds, so the meter is not the only tell.
        stretchY = 1 - 0.06 * Math.min(Math.max(charge, 0), 1);
        stretchX = 1 + 0.04 * Math.min(Math.max(charge, 0), 1);
    } else if (mode === 'Stumbling' && !finished && !waiting) {
        shakeX = (Math.sin(view.animMs * 0.062) + Math.sin(view.animMs * 0.037)) * 0.025 * view.pxPerMetre;
    }

    const fast = vx - view.opts.speedLineThreshold;
    if (fast > 0)
        drawSpeedLines(view, centreX, feetY, boxH, Math.min(fast / 6, 1));

    if (!view.runnerArt.ready)
        return;
    if (frame.row === RUNNER_FRAMES.climb[0].row)
        view.playerClimbFramesDrawn += 1;

    const pullOver = mode === 'Traversing'
        ? pullOverPoint(view, encounterPhase, encounterProgress, view.playerAnimation, x, y, encounterIndex, view.playerRunnerRules) : null;
    const rect = pullOver !== null
        ? runnerFrameRect(view, frame, pullOver.x, pullOver.y, stretchX, stretchY, shakeX)
        : runnerFrameRect(view, frame, x, y, stretchX, stretchY, shakeX);

    // A trailing ghost sells the speed without a real motion blur.
    if (fast > 0) {
        view.ctx.globalAlpha = 0.18 * Math.min(fast / 6, 1);
        blitRunnerFrame(view, view.runnerArt.image, rect, rect.dx - view.pxPerMetre * 0.22);
        view.ctx.globalAlpha = 1;
    }

    blitRunnerFrame(view, view.runnerArt.image, rect, rect.dx);

    if (mode === 'Stumbling' && !finished && !waiting)
        drawTintFlash(view, view.runnerArt.image, rect, STUMBLE_TINT);

    if (mode === 'Charging' && !finished && !waiting)
        drawChargeMeter(view, centreX, feetY, boxH, Math.min(Math.max(charge, 0), 1));

}

/**
 * Pulses colour into the current frame for the stumble beat. The silhouette is added on top of the
 * sprite rather than replacing it, so the runner stays readable through the flash.
 */
function drawTintFlash(view, image, rect, color) {
    const dw = rect.dw;
    const dh = rect.dh;
    if (view.tintCanvas === null) {
        view.tintCanvas = document.createElement('canvas');
        view.tintCtx = view.tintCanvas.getContext('2d');
    }
    if (view.tintCanvas.width !== dw || view.tintCanvas.height !== dh) {
        view.tintCanvas.width = dw;
        view.tintCanvas.height = dh;
    }
    view.tintCtx.clearRect(0, 0, dw, dh);
    view.tintCtx.globalCompositeOperation = 'source-over';
    view.tintCtx.imageSmoothingEnabled = false;
    view.tintCtx.drawImage(image, rect.sx, rect.sy, RUNNER_CELL_W, RUNNER_CELL_H, 0, 0, dw, dh);
    view.tintCtx.globalCompositeOperation = 'source-in';
    view.tintCtx.fillStyle = color;
    view.tintCtx.fillRect(0, 0, dw, dh);
    view.tintCtx.globalCompositeOperation = 'source-over';

    view.ctx.globalCompositeOperation = 'lighter';
    view.ctx.globalAlpha = 0.1 + 0.1 * (0.5 + 0.5 * Math.sin(view.animMs * 0.02));
    view.ctx.drawImage(view.tintCanvas, rect.dx, rect.dy, dw, dh);
    view.ctx.globalAlpha = 1;
    view.ctx.globalCompositeOperation = 'source-over';
}

/** Charge meter floating above the runner while the jump charges. */
function drawChargeMeter(view, centreX, feetY, boxH, charge) {
    const w = view.pxPerMetre * 0.95;
    const h = view.pxPerMetre * 0.16;
    const x = view.alignPx(centreX - w / 2);
    // Clear of the crouch, which only reaches about three quarters of the standing box.
    const y = view.alignPx(feetY - boxH * 0.95 - h);

    view.ctx.fillStyle = CHARGE_BACK;
    view.ctx.fillRect(x - 2, y - 2, w + 4, h + 4);
    view.ctx.fillStyle = charge > 0.8 ? CHARGE_HIGH : CHARGE_LOW;
    view.ctx.fillRect(x, y, Math.max(1, w * charge), h);
    view.ctx.strokeStyle = 'rgba(255, 255, 255, 0.55)';
    view.ctx.lineWidth = 1;
    view.ctx.strokeRect(x - 1.5, y - 1.5, w + 3, h + 3);
}

/** Short streaks trailing the runner once they are moving fast. */
export function drawSpeedLines(view, centreX, feetY, boxH, strength) {
    view.ctx.strokeStyle = SPEED_LINE_COLOR;
    view.ctx.globalAlpha = 0.5 * strength;
    view.ctx.lineWidth = Math.max(1, view.pxPerMetre * 0.05);
    view.ctx.beginPath();
    for (let i = 0; i < 6; i++) {
        const phase = (view.animMs * 0.0016 + i * 0.37) % 1;
        const ly = feetY - boxH * (0.08 + 0.15 * i);
        const lx = centreX - view.pxPerMetre * (0.3 + phase * 2.4);
        view.ctx.moveTo(lx, ly);
        view.ctx.lineTo(lx - view.pxPerMetre * (0.7 + 0.8 * strength), ly);
    }
    view.ctx.stroke();
    view.ctx.globalAlpha = 1;
}
