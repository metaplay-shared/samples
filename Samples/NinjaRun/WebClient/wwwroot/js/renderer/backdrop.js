/**
 * The backdrop: the altitude band's sky and its parallax planes, and the crossing from one band to the next as
 * the run climbs.
 */

import { ALTITUDE_BANDS } from './artSpecs.js';
import { loadImage } from './imageCache.js';
import { bandIndexAt } from './courseModel.js';

/** How long an altitude band takes to cross over the one below it, in seconds of wall clock. */
const ALTITUDE_SWAP_SECONDS = 0.7;

/**
 * The altitude bands with every plane's image resolved: each band's sky and its planes, back to front.
 * @returns {Array<{ name: string, sky: string, planes: Array<{ art: object, depth: number, mirrorTile: boolean, base: undefined | null | { row: number, color: string } }> }>}
 */
export function loadAltitudeBands(view) {
    return ALTITUDE_BANDS.map(band => ({
        name: band.name,
        sky: band.sky,
        planes: band.planes.map(plane => ({
            art: loadImage(view.baseUrl + plane.file),
            depth: plane.depth,
            mirrorTile: plane.mirrorTile === true,
            /** Measured from the pixels on the first draw after the art loads; see `measurePlaneBase`. */
            base: undefined,
        })),
    }));
}

/**
 * Halfway up a slope the view crosses into the band above it. Course position picks the moment, so the same
 * run always changes band in the same place, and the band only ever rises: a stumble that pushes the runner
 * back below a midpoint leaves the view at the height it reached. A course with more climbs than there are
 * bands holds the last one for the rest of the run. The crossing takes `ALTITUDE_SWAP_SECONDS` of the frame
 * clock, and is a cut with reduced motion.
 * @param {number} x the runner's course position
 * @param {number} dt the frame's clock step, in seconds
 */
export function advanceAltitude(view, x, dt) {
    const reachedBandIndex = bandIndexAt(view, x);
    if (reachedBandIndex > view.altitudeBandIndex) {
        view.altitudeFromBandIndex = view.altitudeBandIndex;
        view.altitudeBandIndex = reachedBandIndex;
        view.altitudeBlend = view.reducedMotion ? 1 : 0;
    }
    if (view.altitudeBlend < 1)
        view.altitudeBlend = Math.min(1, view.altitudeBlend + dt / ALTITUDE_SWAP_SECONDS);
    if (view.altitudeBlend >= 1)
        view.altitudeFromBandIndex = view.altitudeBandIndex;
}

/** Shows the band a run is in at course position `x`, with no crossing under way. */
export function showAltitudeAt(view, x) {
    view.altitudeBandIndex = bandIndexAt(view, x);
    view.altitudeFromBandIndex = view.altitudeBandIndex;
    view.altitudeBlend = 1;
}

/** Measures every loaded plane's base not yet measured, so no frame has to. */
export function measureBackdropBases(view) {
    for (const band of view.altitudeBands) {
        for (const plane of band.planes) {
            if (plane.base === undefined && plane.art.ready)
                plane.base = measurePlaneBase(plane.art.image);
        }
    }
}

/**
 * The lowest source row a backdrop plane covers edge to edge, and the colour of the band just above it: what
 * the renderer carries on below the plane so its base always reaches under the terrain. Null when the pixels
 * cannot be read, in which case the plane is drawn as it is.
 * @param {HTMLImageElement} image
 * @returns {{ row: number, color: string } | null}
 */
function measurePlaneBase(image) {
    try {
        const w = image.naturalWidth || image.width;
        const h = image.naturalHeight || image.height;
        const probe = document.createElement('canvas');
        probe.width = w;
        probe.height = h;
        const pctx = probe.getContext('2d', { willReadFrequently: true });
        pctx.drawImage(image, 0, 0);
        const data = pctx.getImageData(0, 0, w, h).data;
        const coveredRow = y => {
            let opaque = 0;
            for (let x = 0; x < w; x++) {
                if (data[(y * w + x) * 4 + 3] > 200)
                    opaque++;
            }
            return opaque >= w * 0.98;
        };
        let row = h - 1;
        while (row > 0 && !coveredRow(row))
            row--;
        if (row <= 0)
            return null;
        // Averaged over a few rows above the lowest covered one, clear of its anti-aliased edge.
        let r = 0, g = 0, b = 0, n = 0;
        for (let y = Math.max(0, row - 8); y < row; y++) {
            for (let x = 0; x < w; x++) {
                const i = (y * w + x) * 4;
                r += data[i]; g += data[i + 1]; b += data[i + 2]; n++;
            }
        }
        if (n === 0)
            return null;
        return { row: row, color: `rgb(${Math.round(r / n)}, ${Math.round(g / n)}, ${Math.round(b / n)})` };
    } catch {
        return null;
    }
}

/**
 * Parallax backdrop: the altitude band's own sky under its own planes, with the band above crossing over
 * both once the run is half way up a slope. The arriving band is drawn over the one below it rather than
 * replacing it outright, so the view is uncovered into the new height instead of cutting to it.
 */
export function drawBackdrop(view) {
    const fromBand = view.altitudeBands[view.altitudeFromBandIndex];
    const toBand = view.altitudeBands[view.altitudeBandIndex];
    const arriving = toBand === fromBand ? 0 : view.altitudeBlend;

    view.ctx.fillStyle = fromBand.sky;
    view.ctx.fillRect(0, 0, view.viewW, view.viewH);

    if (arriving > 0) {
        view.ctx.save();
        view.ctx.globalAlpha = arriving;
        view.ctx.fillStyle = toBand.sky;
        view.ctx.fillRect(0, 0, view.viewW, view.viewH);
        view.ctx.restore();
    }

    for (const plane of fromBand.planes)
        drawBackdropPlane(view, plane, 1);
    if (arriving > 0) {
        for (const plane of toBand.planes)
            drawBackdropPlane(view, plane, arriving);
    }
}

/**
 * One tiled backdrop plane. It drifts at its own fraction of the camera on both axes: sideways so the
 * skyline sits behind the course, and vertically so a climb carries the mountain down past the runner
 * instead of leaving it pinned to the frame. Both are measured from where the course starts, so a band's
 * far plate and its nearer silhouette pull apart by the difference between their depths. Every plane
 * keeps its source aspect and settles a little below the ground line, and the colour of its lowest solid
 * band is filled on from there to the foot of the frame, so the plane carries under the terrain rather
 * than ending on it, whatever transparent margin the art leaves under its silhouette, and however far
 * below the course's starting height the camera goes.
 *
 * A plane marked `mirrorTile` alternates plain and flipped repeats. The repeat is then two images wide
 * and every join meets its own mirror image, which is what keeps a silhouette that is high at one edge
 * and low at the other from stepping as it wraps. Each repeat ends on the device pixel the next begins
 * on, so a join is never a gap, an overlap or a filtered edge; a repeat is a device pixel wider or
 * narrower than its neighbour for it, which nothing can see.
 * @param {{ art: { image: HTMLImageElement, ready: boolean }, depth: number, mirrorTile: boolean }} plane
 * @param {number} alpha
 */
function drawBackdropPlane(view, plane, alpha) {
    if (!plane.art.ready || alpha <= 0)
        return;

    const image = plane.art.image;
    if (plane.base === undefined)
        plane.base = measurePlaneBase(image);
    const height = view.viewH * 0.72;
    const width = height * (image.width / image.height);
    const period = plane.mirrorTile ? width * 2 : width;
    const offset = (view.cameraX * view.pxPerMetre * plane.depth) % period;
    const rise = (view.cameraY - view.backdropAnchorY) * view.pxPerMetre * plane.depth;
    const top = view.groundLineY + view.viewH * 0.06 - height + rise;
    const drawH = Math.ceil(height);

    view.ctx.save();
    view.ctx.globalAlpha = alpha;
    // The plane's base colour carries on below its last solid row to the foot of the frame, so neither
    // art with a transparent margin under its silhouette nor a camera below the course's starting height
    // ever opens sky between the plane and the terrain.
    if (plane.base !== null) {
        const baseTop = Math.floor(top + (plane.base.row / image.height) * drawH);
        if (baseTop < view.viewH) {
            view.ctx.fillStyle = plane.base.color;
            view.ctx.fillRect(0, baseTop, view.viewW, view.viewH - baseTop);
        }
    }
    // Neighbouring repeats share one edge, on a whole device pixel, so every join is flush and unfiltered:
    // an overlap would composite the near plane's translucent silhouette twice into a darker line, and an
    // edge between two device pixels would be antialiased into a lighter one.
    let dx = -offset;
    if (dx > 0)
        dx -= period;
    let edge = Math.round(dx * view.dpr);
    for (let repeat = 0; edge < view.viewW * view.dpr; repeat += 1) {
        const nextEdge = Math.round((dx + (repeat + 1) * width) * view.dpr);
        const left = edge / view.dpr;
        const tileW = (nextEdge - edge) / view.dpr;
        if (plane.mirrorTile && repeat % 2 === 1) {
            view.ctx.save();
            view.ctx.translate(left + tileW, 0);
            view.ctx.scale(-1, 1);
            view.ctx.drawImage(image, 0, view.alignPx(top), tileW, drawH);
            view.ctx.restore();
        } else {
            view.ctx.drawImage(image, left, view.alignPx(top), tileW, drawH);
        }
        edge = nextEdge;
    }
    view.ctx.restore();
}
