/**
 * The course decoration catalogue: what each decoration kind a part places looks like. A part names a kind by
 * id (`CoursePartDecoration.Kind` in shared code, `decorations[].kind` in CourseParts.json); everything about
 * its art lives here, beside the renderer that draws it (`timeTrialRenderer.js`) and the part editor that
 * places it (`coursePartEditor.js`). Decorations are art only, so the catalogue is the client's alone.
 *
 * A kind is one horizontal sprite sheet of equal frames, drawn at `pxPerMetre` source pixels to the metre and
 * nearest-neighbour:
 *
 * - `file`: the sheet, under the renderer's `assetBaseUrl`.
 * - `frameW` / `frameH`: one frame's size in source pixels; frame `i` starts at `i * frameW`.
 * - `frames`: how many frames the sheet holds. Frame 0 is the idle frame, drawn whenever nothing plays.
 * - `anchorX` / `baseY`: the source point planted on the ground point the decoration stands on, within a
 *   frame: `anchorX` its root's centre line, `baseY` the bottom edge of its last solid row, so that row sits
 *   on the surface and anything fainter under it is below the ground line, where the terrain covers it in the
 *   Behind layer.
 * - `layer`: the layer the part editor places a new one in, `behind` or `front`; a part may put any kind in
 *   either.
 * - `passBy` (optional): an animation played once when the player's runner reaches the decoration:
 *   `sequence` lists the sheet frames in play order, each held `frameMs`, and `triggerMetres` is how far
 *   short of the decoration's X the runner's feet start it. The renderer plays it on its own wall clock and
 *   then holds frame 0, or, with `holdLast` true, the sequence's last frame for the rest of the run, for a
 *   thing the pass changes for good, such as a bird that has flown. With reduced motion a run leaves a kind
 *   on frame 0, or cuts a `holdLast` kind straight to its last frame. The part editor's preview plays the
 *   same sequence on demand and returns every kind to frame 0, the frame it is placed by.
 * - `label`: what the part editor lists it as.
 * - `climbFence` (optional): true for a kind the renderer plants itself along every climb's slope, one every
 *   `spacingMetres`, and strings its rope between; a part never places it and the editor does not offer it.
 *   `ropeTieY` is the source row the rope meets the stake at.
 */
export const DECORATION_KINDS = Object.freeze({
    // The path lantern: 1× pixel art at 32 px per metre, 0.9 m wide and 1.5 m tall.
    'stone-lantern': Object.freeze({
        label: 'Stone lantern',
        file: 'path-lantern.sha256-4877a146fc93a067.webp',
        frameW: 29,
        frameH: 48,
        frames: 1,
        pxPerMetre: 32,
        anchorX: 14.5,
        baseY: 47,
        layer: 'behind',
    }),
    // The reed flowers: three 192 × 256 frames (rest, slightly bent, bent)
    // whose solid parts span rows 36..224 at rest, 189 rows. At 160 px per metre that is 1.18 m, a clump of
    // reeds reaching the runner's chest, 0.89 m across at its widest. The root is columns 59..145 of row 224.
    'reed-flowers': Object.freeze({
        label: 'Reed flowers',
        file: 'course-reed-flowers-brush.sha256-5dcb6afc85200a48.webp',
        frameW: 192,
        frameH: 256,
        frames: 3,
        pxPerMetre: 160,
        anchorX: 102,
        baseY: 225,
        layer: 'front',
        passBy: Object.freeze({ sequence: Object.freeze([0, 1, 2, 1, 0]), frameMs: 80, triggerMetres: 0.6 }),
    }),
    // The kekkai stakes: three 16 × 52 frames (rest, shide swung, shide swung farther) of a
    // bamboo stake carrying the zigzag paper, 1× pixel art at 32 px per metre. The stake stands in columns 2..4
    // from row 2 down to row 50, 1.5 m tall; row 51 is its dark foot, under the ground line. The shimenawa is
    // tied at row 8, where the paper hangs from. The warding fence of a climb's slope: planted by the renderer,
    // not by a part.
    'kekkai-stake': Object.freeze({
        label: 'Kekkai stake',
        file: 'kekkai-stakes.sha256-059fb0f10e6f9c54.webp',
        frameW: 16,
        frameH: 52,
        frames: 3,
        pxPerMetre: 32,
        anchorX: 3.5,
        baseY: 51,
        layer: 'behind',
        passBy: Object.freeze({ sequence: Object.freeze([0, 1, 2, 1, 0]), frameMs: 80, triggerMetres: 0.6 }),
        climbFence: true,
        spacingMetres: 2.4,
        ropeTieY: 8.5,
    }),
});

/** The kind with this id, or null when the catalogue has none. */
export function decorationKind(id) {
    return typeof id === 'string' && Object.prototype.hasOwnProperty.call(DECORATION_KINDS, id)
        ? DECORATION_KINDS[id]
        : null;
}

/**
 * A kind's extent around the ground point it stands on, in metres: how far its frame reaches left and right
 * of its X, and how far above and below the surface.
 */
export function decorationExtent(kind, flip) {
    const left = kind.anchorX / kind.pxPerMetre;
    const right = (kind.frameW - kind.anchorX) / kind.pxPerMetre;
    return {
        left: flip ? right : left,
        right: flip ? left : right,
        up: kind.baseY / kind.pxPerMetre,
        down: (kind.frameH - kind.baseY) / kind.pxPerMetre,
    };
}

/**
 * Every kind a part may place, in catalogue order, as `{ id, label, front }`: `front` is the layer it is placed
 * in. A climb-fence kind is the renderer's to plant, so it is left out.
 */
export function placeableDecorationKinds() {
    return Object.keys(DECORATION_KINDS)
        .filter(id => DECORATION_KINDS[id].climbFence !== true)
        .map(id => ({ id, label: DECORATION_KINDS[id].label, front: DECORATION_KINDS[id].layer === 'front' }));
}

/** The one climb-fence kind, or null when the catalogue has none. */
export function climbFenceKind() {
    const id = Object.keys(DECORATION_KINDS).find(id => DECORATION_KINDS[id].climbFence === true);
    return id === undefined ? null : { id, kind: DECORATION_KINDS[id] };
}
