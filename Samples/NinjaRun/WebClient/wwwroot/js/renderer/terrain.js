/**
 * The terrain: the near-black mass the course stands on, its dressed crest, the haze in every pit and the stone
 * spikes along every spike span.
 */

import { climbFillingGap, crestStageAt, groundTopAt, groundTopEndingAt, groundTopStartingAt, isCatchableFace } from './courseModel.js';
import { blit, hashInt } from './drawHelpers.js';

/**
 * The terrain is one near-black mass filled from the course geometry: every ground segment down to the foot of
 * the view and every climb's slope under its walked line. Only its boundary is dressed, from
 * `course-terrain-crest`, a 1× pixel-art sheet drawn at `CREST_PX_PER_METRE` and enlarged nearest-neighbour.
 */
const TERRAIN_FILL = '#05070d';
export const CREST_PX_PER_METRE = 32;

/**
 * The crest sheet has one row of cells per stage, all laid out alike.
 * `anchorX` / `anchorY` is the source pixel pinned to the terrain point the cell dresses:
 *
 * - `top`: the walkable surface, a 1 m horizontal repeat whose row 0 is the surface.
 * - `outerLeft` / `outerRight`: where a surface drops away at its left or right end, pinned on the face at the
 *   surface; the crest rounds over the edge and turns down into the rim.
 * - `rimLeft` / `rimRight`: a 1 m vertical repeat down a face whose open side is to its left or right. Their lit
 *   pixels sit just inside the mass, at the face.
 * - `rimClimbable`: the left-facing rim of a face the runner can catch and mantle.
 * - `innerLeft` / `innerRight`: where a surface meets the foot of a higher face on its left or right, pinned on
 *   that face at the lower surface.
 * - `tufts`: upright clumps, pinned at their base.
 *
 * Right-facing parts are drawn dimmer than left-facing ones: the moon is high to the left.
 */
const CREST_CELLS = {
    top: [{ x: 8, w: 32, h: 8 }, { x: 48, w: 32, h: 8 }],
    outerLeft: { x: 88, w: 16, h: 16, anchorX: 0, anchorY: 0 },
    outerRight: { x: 112, w: 16, h: 16, anchorX: 16, anchorY: 0 },
    rimLeft: { x: 136, w: 8, h: 32, anchorX: 0 },
    rimRight: { x: 152, w: 8, h: 32, anchorX: 8 },
    rimClimbable: { x: 168, w: 8, h: 32, anchorX: 0 },
    innerLeft: { x: 184, w: 8, h: 8, anchorX: 2, anchorY: 5 },
    innerRight: { x: 200, w: 8, h: 8, anchorX: 6, anchorY: 5 },
    tufts: [
        { x: 216, w: 16, h: 12, anchorX: 8, anchorY: 10 },
        { x: 240, w: 16, h: 12, anchorX: 8, anchorY: 10 },
        { x: 264, w: 16, h: 12, anchorX: 8, anchorY: 10 },
        { x: 288, w: 16, h: 12, anchorX: 8, anchorY: 10 },
    ],
};

/**
 * Source row of each altitude band's cells, keyed by the band's name. The sheet's first row, at y = 8, is a
 * neutral garden moss set that nothing draws.
 */
const CREST_STAGE_Y = { foothills: 56, 'mid-ridge': 104, summit: 152 };

/** How far the rounded outer corner pulls the surface strip back from the face, in metres. */
const CREST_CORNER_INSET = 3 / CREST_PX_PER_METRE;

/** A metre of crest carries a tuft when its hash falls in this share, and never within this margin of an edge. */
const CREST_TUFT_ONE_IN = 3;
export const CREST_TUFT_EDGE_MARGIN = 0.6;

/**
 * Source pixels to the metre in `course-hazards-and-pits`: its cells are one-metre tiles. Spikes stand on
 * `HAZARD_BASE_Y`, measured from the pixels. Every coordinate here is in the source's pixels, which is
 * `HAZARD_SRC_WIDTH` wide; the served sheet may be downscaled, and is addressed through its loaded width.
 */
const HAZARD_SRC_WIDTH = 1774;
const HAZARD_SRC_PX_PER_METRE = 365;
const HAZARD_BASE_Y = 401;

/**
 * The stone spike tile every spike span is tiled from, one metre to the tile. The sheet also holds a capped
 * bamboo set, in its columns 48..1287, that nothing draws.
 */
const SPIKE_TILE = { x: 1347, y: 118, w: 379, h: 285 };

/**
 * How dark the air in a shaft gets just above its floor. It thickens with depth and stops there: a pit is
 * deep, never bottomless, and the floor has to stay plainly visible through it.
 */
const PIT_DEPTH_HAZE = 'rgba(5, 12, 30, 0.45)';

/**
 * The course's solid ground as the renderer draws it: every ground segment with the spans a cosmetic bridge
 * replaces cut out, and what each end of what is left meets.
 *
 * An end is `flush` where the surface carries on at the same height or into a climb's slope; `inner` where
 * it runs into the foot of a higher face; and `outer` where the surface drops away, to lower ground whose
 * height is `lowY`, or with `lowY` null, to the foot of the view. An outer end at a left edge is a face the
 * runner meets head on, so it is `climbable` when the course lets them catch and mantle it.
 *
 * Each piece also knows its crest stage, which is the stage of the altitude band its middle stands in.
 */
export function buildTerrainPieces(view) {
    const pieces = [];
    for (let i = 0; i < view.ground.length; i++) {
        const seg = view.ground[i];
        const before = i > 0 ? view.ground[i - 1] : null;
        const after = i + 1 < view.ground.length ? view.ground[i + 1] : null;

        // What is left of the segment once the bridges have taken their spans.
        let spans = [{ startX: seg.startX, endX: seg.endX, cutLeft: false, cutRight: false }];
        for (const bridge of view.droopingBridges) {
            const next = [];
            for (const span of spans) {
                if (bridge.endX <= span.startX || bridge.startX >= span.endX) {
                    next.push(span);
                    continue;
                }
                if (bridge.startX > span.startX)
                    next.push({ startX: span.startX, endX: bridge.startX, cutLeft: span.cutLeft, cutRight: true });
                if (bridge.endX < span.endX)
                    next.push({ startX: bridge.endX, endX: span.endX, cutLeft: true, cutRight: span.cutRight });
            }
            spans = next;
        }

        for (const span of spans) {
            if (span.endX - span.startX < 1e-6)
                continue;
            const left = span.cutLeft ? { kind: 'outer', lowY: null }
                : terrainEnd(seg, before, before !== null ? before.endX : NaN, seg.startX,
                    before !== null && climbFillingGap(view, before.endX, seg.startX) !== null);
            const right = span.cutRight ? { kind: 'outer', lowY: null }
                : terrainEnd(seg, after, after !== null ? after.startX : NaN, seg.endX,
                    after !== null && climbFillingGap(view, seg.endX, after.startX) !== null);
            left.climbable = left.kind === 'outer' && isCatchableFace(view, span.startX);
            pieces.push({
                startX: span.startX,
                endX: span.endX,
                topY: seg.topY,
                left: left,
                right: right,
                stage: crestStageAt(view, (span.startX + span.endX) / 2),
            });
        }
    }
    return pieces;
}

/**
 * What one end of a ground segment meets: its neighbour on that side, whose near edge is at `neighbourX`,
 * with `edgeX` the segment's own edge there and `sloped` whether a climb fills the gap between them.
 */
function terrainEnd(seg, neighbour, neighbourX, edgeX, sloped) {
    if (neighbour === null)
        return { kind: 'outer', lowY: null };
    if (sloped)
        return { kind: 'flush', lowY: null };
    if (Math.abs(neighbourX - edgeX) > 1e-6)
        return { kind: 'outer', lowY: null };
    if (neighbour.topY > seg.topY + 1e-6)
        return { kind: 'inner', lowY: null };
    if (neighbour.topY < seg.topY - 1e-6)
        return { kind: 'outer', lowY: neighbour.topY };
    return { kind: 'flush', lowY: null };
}

/**
 * Prepares the hazards sheet for the current viewport. Its source pixels are far finer than the screen's, so it
 * is scaled once, smoothed, to about a device pixel per source pixel, and every spike is then drawn from that
 * copy nearest-neighbour, which keeps it crisp instead of shimmering. At or above its natural size it is drawn
 * from the image itself. `view.hazardSheet` holds what is drawn from (nothing until the sheet has loaded) and
 * stays `dirty` until a loaded sheet has been prepared; a resize makes it dirty again.
 */
export function prepareHazardSheet(view) {
    const sheet = view.hazardSheet;
    const art = view.hazardArt;
    if (!art.ready) {
        sheet.source = null;
        return;
    }
    sheet.dirty = false;

    const imageScale = art.image.width / HAZARD_SRC_WIDTH;
    const scale = view.pxPerMetre * view.dpr / (HAZARD_SRC_PX_PER_METRE * imageScale);
    if (!(scale > 0) || scale >= 0.95) {
        sheet.source = art.image;
        sheet.scale = imageScale;
        return;
    }

    const w = Math.max(1, Math.round(art.image.width * scale));
    const h = Math.max(1, Math.round(art.image.height * scale));
    if (sheet.canvas === null)
        sheet.canvas = document.createElement('canvas');
    const target = sheet.canvas;
    target.width = w;
    target.height = h;
    const tctx = target.getContext('2d');
    tctx.clearRect(0, 0, w, h);
    tctx.imageSmoothingEnabled = true;
    tctx.imageSmoothingQuality = 'high';
    tctx.drawImage(art.image, 0, 0, w, h);
    sheet.source = target;
    sheet.scale = w / HAZARD_SRC_WIDTH;
}

/** Drops the scaled copy of the hazards sheet; nothing is drawn from it afterwards. */
export function releaseHazardSheet(view) {
    const sheet = view.hazardSheet;
    if (sheet.canvas !== null) {
        sheet.canvas.width = 0;
        sheet.canvas.height = 0;
        sheet.canvas = null;
    }
    sheet.source = null;
}

/**
 * The places along a stretch of surface a tuft may stand: at most one per course metre from `fromX` up to
 * `toX`, chosen and placed by a hash of that metre so they never shimmer. Each comes with `variant`, the hash
 * bits that pick which of a sheet's tuft cells it is drawn in. The caller keeps the ones its surface carries.
 * @param {number} fromX
 * @param {number} toX
 * @returns {Generator<{ x: number, variant: number }>}
 */
export function* tuftPositions(fromX, toX) {
    for (let metre = Math.floor(fromX); metre < toX; metre++) {
        const h = hashInt(metre * 31 + 7);
        if (h % CREST_TUFT_ONE_IN !== 0)
            continue;
        yield { x: metre + 0.15 + ((h >>> 8) % 70) / 100, variant: h >>> 4 };
    }
}

/**
 * The terrain mass: every piece of ground filled from its surface to the foot of the view, and every climb's
 * slope filled under its walked line, all in the one near-black. Pieces that meet merge into a single
 * silhouette, so a step, a pit's walls and its floor need nothing of their own.
 */
export function drawTerrain(view) {
    const leftX = view.viewLeftX();
    const rightX = view.viewRightX();

    view.ctx.fillStyle = TERRAIN_FILL;
    for (const piece of view.terrainPieces) {
        if (piece.endX < leftX || piece.startX > rightX)
            continue;
        const x0 = view.alignPx(view.screenX(piece.startX));
        const x1 = view.alignPx(view.screenX(piece.endX));
        const top = view.alignPx(view.screenY(piece.topY));
        if (top < view.viewH)
            view.ctx.fillRect(x0, top, Math.max(1, x1 - x0), view.viewH - top);
    }

    for (const climb of view.climbs) {
        if (climb.topX < leftX || climb.slopeStartX > rightX)
            continue;
        const x0 = view.screenX(climb.slopeStartX);
        const x1 = view.screenX(climb.topX);
        view.ctx.beginPath();
        view.ctx.moveTo(x0, view.screenY(climb.bottomY));
        view.ctx.lineTo(x1, view.screenY(climb.topY));
        view.ctx.lineTo(x1, view.viewH);
        view.ctx.lineTo(x0, view.viewH);
        view.ctx.closePath();
        view.ctx.fill();
    }
}

/**
 * The air in every pit: a haze that thickens with depth and stops at the floor. The shaft itself is the gap
 * the terrain mass leaves (its walls are the faces either side and its floor is ordinary ground), and the
 * backdrop shows through it, so the black runner stays readable down there.
 */
export function drawPits(view) {
    if (view.pits.length === 0)
        return;

    const leftX = view.viewLeftX();
    const rightX = view.viewRightX();

    for (const pit of view.pits) {
        if (pit.endX < leftX || pit.startX > rightX)
            continue;

        const lowerLipY = pit.floorY + pit.depth;
        const nearLipY = groundTopEndingAt(view, pit.startX, lowerLipY);
        const farLipY = groundTopStartingAt(view, pit.endX, lowerLipY);
        const top = view.alignPx(view.screenY(Math.max(nearLipY, farLipY)));
        const floor = view.alignPx(view.screenY(pit.floorY));
        if (top >= view.viewH || floor <= 0 || floor <= top)
            continue;

        const x0 = view.alignPx(view.screenX(pit.startX));
        const x1 = view.alignPx(view.screenX(pit.endX));

        // The floor stays lit enough to land on: the haze is thickest a little above it.
        const haze = view.ctx.createLinearGradient(0, top, 0, floor);
        haze.addColorStop(0, 'rgba(5, 12, 30, 0)');
        haze.addColorStop(0.75, PIT_DEPTH_HAZE);
        haze.addColorStop(1, PIT_DEPTH_HAZE);
        view.ctx.fillStyle = haze;
        view.ctx.fillRect(x0, top, Math.max(1, x1 - x0), floor - top);
    }
}

/**
 * The terrain's dressed boundary, in its piece's crest stage: the rim down every exposed face, the surface
 * strip along every top and slope, the rounded corner wherever a surface drops away, the fillet where one
 * meets the foot of a higher face, and a sparse scatter of tufts.
 */
export function drawTerrainCrest(view) {
    if (!view.crestArt.ready)
        return;

    const leftX = view.viewLeftX();
    const rightX = view.viewRightX();

    for (const piece of view.terrainPieces) {
        if (piece.endX < leftX - 1 || piece.startX > rightX + 1)
            continue;
        const rowY = CREST_STAGE_Y[piece.stage];

        if (piece.left.kind === 'outer')
            drawCrestRim(view, piece.left.climbable ? CREST_CELLS.rimClimbable : CREST_CELLS.rimLeft, rowY,
                piece.startX, piece.topY, piece.left.lowY);
        if (piece.right.kind === 'outer')
            drawCrestRim(view, CREST_CELLS.rimRight, rowY, piece.endX, piece.topY, piece.right.lowY);

        // The fillets go under the surface strip: only their rise up the higher face shows above it, and
        // their own lower rows would cut across the strip.
        if (piece.left.kind === 'inner')
            drawCrestCell(view, CREST_CELLS.innerLeft, rowY, piece.startX, piece.topY, null);
        if (piece.right.kind === 'inner')
            drawCrestCell(view, CREST_CELLS.innerRight, rowY, piece.endX, piece.topY, null);

        const stripFrom = piece.left.kind === 'outer' ? piece.startX + CREST_CORNER_INSET : piece.startX;
        const stripTo = piece.right.kind === 'outer' ? piece.endX - CREST_CORNER_INSET : piece.endX;
        drawCrestStrip(view, rowY, stripFrom, stripTo, piece.topY);

        if (piece.left.kind === 'outer')
            drawCrestCell(view, CREST_CELLS.outerLeft, rowY, piece.startX, piece.topY, piece.left.lowY);
        if (piece.right.kind === 'outer')
            drawCrestCell(view, CREST_CELLS.outerRight, rowY, piece.endX, piece.topY, piece.right.lowY);

        drawCrestTufts(view, rowY, piece.startX + CREST_TUFT_EDGE_MARGIN, piece.endX - CREST_TUFT_EDGE_MARGIN,
            () => piece.topY);
    }

    for (const climb of view.climbs) {
        if (climb.topX < leftX || climb.slopeStartX > rightX)
            continue;
        drawSlopeCrest(view, climb, leftX, rightX);
    }
}

/**
 * One crest cell pinned by its anchor to a course point. `lowY`, where given, is the lower surface the face
 * under the cell drops to: rows below it would run on into solid terrain, so they are left out.
 */
function drawCrestCell(view, cell, rowY, x, y, lowY) {
    const k = view.pxPerMetre / CREST_PX_PER_METRE;
    const anchorY = cell.anchorY || 0;
    let rows = cell.h;
    if (lowY !== null)
        rows = Math.min(rows, Math.round((y - lowY) * CREST_PX_PER_METRE) + anchorY);
    if (rows <= 0)
        return;
    const dx = view.alignPx(view.screenX(x) - cell.anchorX * k);
    const dy = view.alignPx(view.screenY(y) - anchorY * k);
    blit(view, view.crestArt.image, cell.x, rowY, cell.w, rows, dx, dy,
        Math.max(1, Math.round(cell.w * k)), Math.max(1, Math.round(rows * k)));
}

/** A rim tiled down a face from the surface at `topY` to `lowY`, or to the foot of the view. */
function drawCrestRim(view, cell, rowY, x, topY, lowY) {
    const k = view.pxPerMetre / CREST_PX_PER_METRE;
    const top = view.screenY(topY);
    const bottom = Math.min(lowY === null ? view.viewH : view.screenY(lowY), view.viewH);
    if (bottom <= 0 || top >= view.viewH || bottom <= top)
        return;
    const dx = view.alignPx(view.screenX(x) - cell.anchorX * k);
    const dw = Math.max(1, Math.round(cell.w * k));
    const tileH = cell.h * k;
    // Tiles above the view are skipped whole, so the repeat stays pinned to the surface.
    for (let tile = Math.max(0, Math.floor(-top / tileH)); top + tile * tileH < bottom; tile++) {
        const tileTop = top + tile * tileH;
        const rows = Math.min(cell.h, Math.ceil((bottom - tileTop) / k));
        const dy = view.alignPx(tileTop);
        blit(view, view.crestArt.image, cell.x, rowY, cell.w, rows, dx, dy, dw, Math.max(1, view.alignPx(tileTop + rows * k) - dy));
    }
}

/** The surface strip along a flat top, a metre to the repeat, pinned to whole course metres. */
function drawCrestStrip(view, rowY, fromX, toX, topY) {
    const leftX = Math.max(fromX, view.viewLeftX());
    const rightX = Math.min(toX, view.viewRightX());
    if (rightX <= leftX)
        return;
    const k = view.pxPerMetre / CREST_PX_PER_METRE;
    const dy = view.alignPx(view.screenY(topY));
    const dh = Math.max(1, Math.round(CREST_CELLS.top[0].h * k));
    for (let metre = Math.floor(leftX); metre < rightX; metre++) {
        const x0 = Math.max(metre, leftX);
        const x1 = Math.min(metre + 1, rightX);
        if (x1 <= x0)
            continue;
        const cell = CREST_CELLS.top[hashInt(metre) & 1];
        const dx0 = view.alignPx(view.screenX(x0));
        const dx1 = view.alignPx(view.screenX(x1));
        blit(view, view.crestArt.image, cell.x + (x0 - metre) * CREST_PX_PER_METRE, rowY,
            (x1 - x0) * CREST_PX_PER_METRE, cell.h, dx0, dy, Math.max(1, dx1 - dx0), dh);
    }
}

/**
 * The surface strip up a climb's slope. The strip is laid one source column at a time, each column dropped
 * onto the walked line, which bends the art along the slope and keeps its pixels upright and square rather
 * than rotating them. Each column takes the stage of the band it stands in, so the crest changes at the
 * slope's midpoint along with the backdrop.
 */
function drawSlopeCrest(view, climb, leftX, rightX) {
    const k = view.pxPerMetre / CREST_PX_PER_METRE;
    const dh = Math.max(1, Math.round(CREST_CELLS.top[0].h * k));
    const rise = (climb.topY - climb.bottomY) / (climb.topX - climb.slopeStartX);
    const firstColumn = Math.ceil(Math.max(climb.slopeStartX, leftX) * CREST_PX_PER_METRE);
    const lastColumn = Math.floor(Math.min(climb.topX, rightX) * CREST_PX_PER_METRE);
    for (let column = firstColumn; column < lastColumn; column++) {
        const x = column / CREST_PX_PER_METRE;
        const metre = Math.floor(x);
        const cell = CREST_CELLS.top[hashInt(metre) & 1];
        const rowY = CREST_STAGE_Y[crestStageAt(view, x)];
        const dx0 = view.alignPx(view.screenX(x));
        const dx1 = view.alignPx(view.screenX(x + 1 / CREST_PX_PER_METRE));
        const dy = view.alignPx(view.screenY(climb.bottomY + (x - climb.slopeStartX) * rise));
        blit(view, view.crestArt.image, cell.x + (column - metre * CREST_PX_PER_METRE), rowY, 1, cell.h,
            dx0, dy, Math.max(1, dx1 - dx0), dh);
    }
    drawCrestTufts(view, null, climb.slopeStartX + CREST_TUFT_EDGE_MARGIN, climb.topX - CREST_TUFT_EDGE_MARGIN,
        x => climb.bottomY + (x - climb.slopeStartX) * rise);
}

/**
 * Tufts scattered along a surface: at most one per course metre, chosen and placed by a hash of that metre
 * so they never shimmer, and kept off spike spans. `rowY` null takes each tuft's stage from its position.
 */
function drawCrestTufts(view, rowY, fromX, toX, surfaceAt) {
    const leftX = Math.max(fromX, view.viewLeftX());
    const rightX = Math.min(toX, view.viewRightX());
    for (const { x, variant } of tuftPositions(leftX, rightX)) {
        if (x < leftX || x > rightX || isOnHazard(view, x))
            continue;
        const cell = CREST_CELLS.tufts[variant % CREST_CELLS.tufts.length];
        const tuftRowY = rowY !== null ? rowY : CREST_STAGE_Y[crestStageAt(view, x)];
        drawCrestCell(view, cell, tuftRowY, x, surfaceAt(x), null);
    }
}

/** Whether a spike span covers this course position, with a little clearance either side. */
function isOnHazard(view, x) {
    for (const hazard of view.hazards) {
        if (x >= hazard.startX - 0.3 && x <= hazard.endX + 0.3)
            return true;
    }
    return false;
}

/** Spike spans: the stone tile repeated along the span, one metre to the tile. */
export function drawHazards(view) {
    if (view.hazardSheet.source === null || view.hazards.length === 0)
        return;

    const leftX = view.viewLeftX();
    const rightX = view.viewRightX();

    for (let i = 0; i < view.hazards.length; i++) {
        const hazard = view.hazards[i];
        if (hazard.endX < leftX || hazard.startX > rightX)
            continue;

        const baseY = view.screenY(groundTopAt(view, (hazard.startX + hazard.endX) / 2, view.cameraY));
        for (let x0 = hazard.startX; x0 < hazard.endX - 1e-6; x0 += 1)
            drawSpikeTile(view, SPIKE_TILE, x0, Math.min(x0 + 1, hazard.endX), baseY);
    }
}

/**
 * One metre of spike span, standing on the ground line. A trailing partial metre takes a
 * proportional slice of the tile.
 */
function drawSpikeTile(view, rect, x0, x1, baseY) {
    const k = view.pxPerMetre / HAZARD_SRC_PX_PER_METRE;
    const frac = Math.min(1, x1 - x0);
    const dx0 = view.alignPx(view.screenX(x0));
    const dw = Math.max(1, view.alignPx(view.screenX(x1)) - dx0);
    const dy = view.alignPx(baseY - (HAZARD_BASE_Y - rect.y) * k);
    const s = view.hazardSheet.scale;
    blit(view, view.hazardSheet.source, rect.x * s, rect.y * s, rect.w * frac * s, rect.h * s,
        dx0, dy, dw, Math.max(1, Math.round(rect.h * k)));
}
