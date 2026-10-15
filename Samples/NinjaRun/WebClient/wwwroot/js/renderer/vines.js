/**
 * The vines: every vine's rope, the tree it hangs from, the branch its swing lands on, and the echo of an
 * opponent holding it. Each is drawn once its sheet has loaded, and not before.
 */

import { finiteOr } from '../util.js';
import { groundTopAt } from './courseModel.js';
import { blit, hashInt } from './drawHelpers.js';
import { CREST_PX_PER_METRE, CREST_TUFT_EDGE_MARGIN, tuftPositions } from './terrain.js';

/**
 * The branch a vine's swing lands on, `vine-branch`: 1× pixel art
 * at `CREST_PX_PER_METRE`, in the crest's language. A limb tiled a metre to the cell along the authored
 * span (a thickening left end, two alternating middles, a right end tapering to its tip) with the top row
 * of every cell on the landing surface, and moss tufts pinned by their base to that surface. A span shorter
 * than two metres is all middle.
 */
const VINE_BRANCH_CELLS = {
    leftEnd: { x: 8, y: 8, w: 32, h: 16 },
    middle: [{ x: 48, y: 8, w: 32, h: 16 }, { x: 88, y: 8, w: 32, h: 16 }],
    rightEnd: { x: 128, y: 8, w: 32, h: 16 },
    tufts: [
        { x: 8, y: 32, w: 16, h: 12, anchorX: 8, anchorY: 10 },
        { x: 32, y: 32, w: 16, h: 12, anchorX: 8, anchorY: 10 },
        { x: 56, y: 32, w: 16, h: 12, anchorX: 8, anchorY: 10 },
    ],
};

/** Hanging vines use course-space geometry, so they stay readable independently of the tiled ground. */
export function drawVines(view, state, runnerX, runnerY) {
    for (let i = 0; i < view.encounters.length; i++) {
        const vine = view.encounters[i];
        if (vine.kind !== 'vine')
            continue;

        const anchorX = finiteOr(vine.x, 0);
        const anchorY = finiteOr(vine.y, 0);
        const fullLength = Math.max(finiteOr(vine.length, 0), 0);
        const isActive = finiteOr(state.activeEncounterIndex, -1) === finiteOr(vine.index, -2)
            && state.mode === 'Traversing'
            && state.encounterPhase === 'tap-to-release';
        const restAngle = finiteOr(vine.restAngle, 0);
        const visualState = typeof state.vineVisualState === 'string' ? state.vineVisualState : 'idle';
        const detachedAngle = visualState === 'settling' || visualState === 'hanging'
            ? finiteOr(state.vineVisualAngle, 0)
            : restAngle;
        const endX = isActive ? runnerX : anchorX + Math.sin(detachedAngle) * fullLength;
        const endY = isActive ? runnerY + finiteOr(vine.gripHeight, 0) : anchorY - Math.cos(detachedAngle) * fullLength;
        const dx = endX - anchorX;
        const dy = endY - anchorY;
        const distance = Math.max(Math.hypot(dx, dy), 0.001);
        const tailLength = Math.max(fullLength - distance, 0);
        const tailX = endX + dx / distance * tailLength;
        const tailY = endY + dy / distance * tailLength;

        // The rope is drawn alone, with no dark stroke or shadow under it.
        if (view.vineTextureArt?.ready === true)
            drawTexturedVine(view, anchorX, anchorY, endX, endY, tailX, tailY, fullLength, distance);

        // The dashed rope echo belongs to the opponent holding it, so it goes wherever that opponent
        // goes, including away, while a climb has it.
        for (const ghost of (Array.isArray(state.ghosts) ? state.ghosts : [])) {
            if (ghost !== null && typeof ghost === 'object'
                && ghost.hidden !== true
                && ghost.vineAttached === true
                && finiteOr(ghost.activeEncounterIndex, -1) === finiteOr(vine.index, -2))
                drawGhostVineOverlay(view, vine, ghost);
        }

        drawVineBranch(view, vine);
    }
}

/** Draw the generated vine in short source strips which follow the geometry-driven rope path. */
function drawTexturedVine(view, anchorX, anchorY, gripX, gripY, tailX, tailY, fullLength, gripLength) {
    const image = view.vineTextureArt.image;
    const segmentCount = Math.max(12, Math.ceil(fullLength / 0.32));
    const gripFraction = Math.min(Math.max(gripLength / Math.max(fullLength, 0.001), 0.001), 1);
    const controlX = (anchorX + gripX) * 0.5 + 0.12;
    const controlY = (anchorY + gripY) * 0.5;
    const points = [];
    for (let i = 0; i <= segmentCount; i++) {
        const t = i / segmentCount;
        if (t <= gripFraction || gripFraction >= 0.999) {
            const u = Math.min(t / gripFraction, 1);
            const inverse = 1 - u;
            points.push({
                x: inverse * inverse * anchorX + 2 * inverse * u * controlX + u * u * gripX,
                y: inverse * inverse * anchorY + 2 * inverse * u * controlY + u * u * gripY,
            });
        } else {
            const u = (t - gripFraction) / (1 - gripFraction);
            points.push({ x: gripX + (tailX - gripX) * u, y: gripY + (tailY - gripY) * u });
        }
    }

    view.ctx.save();
    view.ctx.imageSmoothingEnabled = true;
    for (let i = 0; i < segmentCount; i++) {
        const here = points[i];
        const next = points[i + 1];
        const sourceX = image.width * 0.35;
        const sourceWidth = image.width * 0.30;
        const sourceY = image.height * i / segmentCount;
        const sourceHeight = image.height / segmentCount + 1;
        const deltaX = view.screenX(next.x) - view.screenX(here.x);
        const deltaY = view.screenY(next.y) - view.screenY(here.y);
        const lengthPx = Math.hypot(deltaX, deltaY) + 2;
        const widthPx = view.pxPerMetre * 0.58;
        view.ctx.save();
        view.ctx.translate(view.screenX(here.x), view.screenY(here.y));
        view.ctx.rotate(Math.atan2(deltaY, deltaX) - Math.PI / 2);
        view.ctx.drawImage(image, sourceX, sourceY, sourceWidth, sourceHeight, -widthPx / 2, -1, widthPx, lengthPx);
        view.ctx.restore();
    }
    view.ctx.restore();
}

/**
 * A replay can occupy the vine independently of the player simulation. A translucent echo shows that
 * second deterministic rope pose without replacing the solid rope which always represents live play.
 */
function drawGhostVineOverlay(view, vine, ghost) {
    const anchorX = finiteOr(vine.x, 0);
    const anchorY = finiteOr(vine.y, 0);
    const gripX = finiteOr(ghost.vineGripX, finiteOr(ghost.x, anchorX));
    const gripY = finiteOr(ghost.vineGripY, finiteOr(ghost.y, anchorY));
    const gripLength = Math.max(finiteOr(ghost.vineGripLength, 0), 0.001);
    const fullLength = Math.max(finiteOr(vine.length, gripLength), gripLength);
    const tailScale = Math.max(fullLength - gripLength, 0) / gripLength;
    const tailX = gripX + (gripX - anchorX) * tailScale;
    const tailY = gripY + (gripY - anchorY) * tailScale;

    view.ctx.save();
    view.ctx.globalAlpha = 0.48;
    view.ctx.strokeStyle = '#80e8ec';
    view.ctx.lineWidth = Math.max(2, view.pxPerMetre * 0.1);
    view.ctx.lineCap = 'round';
    view.ctx.setLineDash([Math.max(3, view.pxPerMetre * 0.2), Math.max(3, view.pxPerMetre * 0.14)]);
    view.ctx.beginPath();
    view.ctx.moveTo(view.screenX(anchorX), view.screenY(anchorY));
    view.ctx.lineTo(view.screenX(gripX), view.screenY(gripY));
    if (tailScale > 0.001)
        view.ctx.lineTo(view.screenX(tailX), view.screenY(tailY));
    view.ctx.stroke();
    view.ctx.restore();
}

/**
 * The branch a good swing lands on: the bough sheet tiled a metre to the cell along the authored span,
 * its ends at the span's ends and its top row on the landing surface, with moss tufts hashed onto the
 * top by course metre so they never shimmer. A span under two metres is all middle. Nothing is drawn while
 * the sheet loads.
 */
function drawVineBranch(view, vine) {
    const start = finiteOr(vine.platformStartX, 0);
    const end = finiteOr(vine.platformEndX, start);
    const top = finiteOr(vine.platformTopY, 0);
    if (end <= view.viewLeftX() || start >= view.viewRightX() || end <= start)
        return;
    if (view.vineBranchArt?.ready !== true)
        return;

    const sheet = view.vineBranchArt.image;
    const k = view.pxPerMetre / CREST_PX_PER_METRE;
    const dy = view.alignPx(view.screenY(top));
    const dh = Math.max(1, Math.round(VINE_BRANCH_CELLS.middle[0].h * k));
    const metres = Math.ceil(end - start - 1e-6);
    const hasEnds = end - start >= 2;
    const leftX = Math.max(start, view.viewLeftX() - 1);
    const rightX = Math.min(end, view.viewRightX() + 1);

    view.ctx.save();
    view.ctx.imageSmoothingEnabled = false;
    for (let i = 0; i < metres; i++) {
        const x0 = start + i;
        const x1 = Math.min(x0 + 1, end);
        if (x1 <= leftX || x0 >= rightX)
            continue;

        let cell;
        let srcX;
        if (hasEnds && i === 0) {
            cell = VINE_BRANCH_CELLS.leftEnd;
            srcX = cell.x;
        } else if (hasEnds && i === metres - 1) {
            // The tip is drawn right-aligned to the span's end, so a partial last metre keeps the tip.
            cell = VINE_BRANCH_CELLS.rightEnd;
            srcX = cell.x + cell.w - Math.round((x1 - x0) * CREST_PX_PER_METRE);
        } else {
            cell = VINE_BRANCH_CELLS.middle[hashInt(Math.floor(x0)) & 1];
            srcX = cell.x;
        }

        const dx0 = view.alignPx(view.screenX(x0));
        const dx1 = view.alignPx(view.screenX(x1));
        blit(view, sheet, srcX, cell.y, (x1 - x0) * CREST_PX_PER_METRE, cell.h, dx0, dy, Math.max(1, dx1 - dx0), dh);
    }

    // Tufts stand on the top row, off the ends, at most one per metre and chosen by that metre's hash.
    for (const { x, variant } of tuftPositions(Math.max(start + 1, leftX), Math.min(end - 1, rightX))) {
        if (x < start + CREST_TUFT_EDGE_MARGIN || x > end - CREST_TUFT_EDGE_MARGIN)
            continue;
        const tuft = VINE_BRANCH_CELLS.tufts[variant % VINE_BRANCH_CELLS.tufts.length];
        const tx = view.alignPx(view.screenX(x) - tuft.anchorX * k);
        const ty = view.alignPx(view.screenY(top) - tuft.anchorY * k);
        blit(view, sheet, tuft.x, tuft.y, tuft.w, tuft.h, tx, ty,
            Math.max(1, Math.round(tuft.w * k)), Math.max(1, Math.round(tuft.h * k)));
    }
    view.ctx.restore();
}

/** Every vine on the course hangs from a tree of its own. */
export function drawGroundedVineTree(view) {
    if (view.vineTreeArt?.ready !== true)
        return;

    for (const encounter of view.encounters) {
        if (encounter && encounter.kind === 'vine')
            drawGroundedVineTreeFor(view, encounter);
    }
}

/** The complete transparent tree spans continuously from basin roots to the authored vine anchor. */
function drawGroundedVineTreeFor(view, vine) {
    const image = view.vineTreeArt.image;
    const anchorX = finiteOr(vine.x, 0);
    const anchorY = finiteOr(vine.y, 0);
    const terrainY = groundTopAt(view, anchorX, view.cameraY);
    const branchXFraction = 0.71;
    const branchYFraction = 0.235;
    const rootsYFraction = 0.965;
    const metresPerSourcePixel = (anchorY - terrainY)
        / ((rootsYFraction - branchYFraction) * image.height);
    const widthMetres = image.width * metresPerSourcePixel;
    const heightMetres = image.height * metresPerSourcePixel;
    const leftWorld = anchorX - branchXFraction * widthMetres;
    const topWorld = anchorY + branchYFraction * heightMetres;

    view.ctx.drawImage(
        image,
        view.screenX(leftWorld),
        view.screenY(topWorld),
        widthMetres * view.pxPerMetre,
        heightMetres * view.pxPerMetre);
}
