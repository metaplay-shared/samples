/**
 * The torii: every gate on the course, drawn in two layers so the runners pass under them.
 */

import { drawPinned } from './drawHelpers.js';

/**
 * Every torii on the course: the start line, the two bracketing each climb (the bottom one on the flat a few
 * metres short of the slope, where the course takes the runner's hands off the button, and the gate at the far end of the run-out
 * where it hands them back) and the finish line. All of them are the same straight-post pixel-art gate, drawn
 * at `GATE_PX_PER_METRE` nearest-neighbour, in two layers over one 250 × 338 canvas cut at x = 125: the back
 * layer (`SHEET_FILES.toriiBack`, the left half: the far post and the receding roof and beams) goes behind every
 * runner, and the front layer (`SHEET_FILES.toriiFront`, the right half: the near post, turned toward the camera)
 * over them, so a runner passes under the gate rather than in
 * front of it. 250 / 6 source pixels to the metre makes the canvas six metres wide and about eight tall.
 * `anchorX` / `baseY` is the source point planted on the torii's course position: the cut, on the bottom edge
 * of the row both feet end on.
 */
export const GATE_TORII_RECT = { x: 0, y: 0, w: 250, h: 338, anchorX: 125, baseY: 335 };
export const GATE_PX_PER_METRE = 250 / 6;
/**
 * The opening between the gate's posts, in its source columns: from the far post's inner edge to the near
 * post's, measured along the shafts. 46 px, 1.104 m, centred 2 px (0.048 m) past the anchor.
 */
export const GATE_OPENING = { left: 104, right: 150 };

/**
 * The back layer of every torii, drawn before the terrain and every runner. They are all the same gate art
 * for the whole run: a torii says the clock is starting, the course is taking the runner or handing them
 * back, or the run is over, and nothing about who won the segment. Placement is read on the HUD's gate
 * strip, which fills a slot at the bottom torii where the segment is decided, and on the score screen.
 */
export function drawTorii(view) {
    view.ctx.imageSmoothingEnabled = false;
    for (const gate of view.torii)
        drawToriiLayer(view, view.toriiBackArt, gate.x, gate.groundY);
}

/**
 * The near post of every torii, drawn over the player's runner, the field and their effects, so each of
 * them runs under the gate.
 */
export function drawToriiFront(view) {
    view.ctx.imageSmoothingEnabled = false;
    for (const gate of view.torii)
        drawToriiLayer(view, view.toriiFrontArt, gate.x, gate.groundY);
}

/** One layer of one torii, planted midway between its posts on the ground at `groundY`. */
function drawToriiLayer(view, art, x, groundY) {
    if (!art.ready)
        return;
    const centreX = view.screenX(x);
    const halfWidthPx = (GATE_TORII_RECT.w / 2 / GATE_PX_PER_METRE) * view.pxPerMetre;
    if (centreX + halfWidthPx < 0 || centreX - halfWidthPx > view.viewW)
        return;
    drawPinned(view, art.image, GATE_TORII_RECT, GATE_TORII_RECT.anchorX, GATE_TORII_RECT.baseY,
        centreX, view.screenY(groundY), GATE_PX_PER_METRE);
}
