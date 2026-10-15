/**
 * Drawing primitives every part of the renderer shares.
 */

/**
 * Stable per-column pseudo-random value, so tile variants never shimmer as the camera moves.
 * @param {number} i
 * @returns {number} an unsigned 32-bit integer
 */
export function hashInt(i) {
    let h = Math.imul(i | 0, 2654435761) >>> 0;
    h ^= h >>> 15;
    h = Math.imul(h, 2246822519) >>> 0;
    h ^= h >>> 13;
    return h >>> 0;
}

/**
 * Draws a source rectangle of a sheet, in the sheet's own pixels, into a canvas rectangle.
 * @param {CanvasImageSource} source
 */
export function blit(view, source, sx, sy, sw, sh, dx, dy, dw, dh) {
    view.ctx.drawImage(source, sx, sy, sw, sh, dx, dy, dw, dh);
}

/**
 * Draws a source rectangle at a fixed metres-per-source-pixel scale, with one source point pinned to
 * one screen point. Everything whose size is authored in metres goes through here.
 * @param {CanvasImageSource} source
 * @param {{ x: number, y: number, w: number, h: number }} rect
 */
export function drawPinned(view, source, rect, pinSrcX, pinSrcY, pinX, pinY, srcPxPerMetre) {
    const k = view.pxPerMetre / srcPxPerMetre;
    const dx = view.alignPx(pinX - (pinSrcX - rect.x) * k);
    const dy = view.alignPx(pinY - (pinSrcY - rect.y) * k);
    blit(view, source, rect.x, rect.y, rect.w, rect.h, dx, dy,
        Math.max(1, Math.round(rect.w * k)), Math.max(1, Math.round(rect.h * k)));
}
