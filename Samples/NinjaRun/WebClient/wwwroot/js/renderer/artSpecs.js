/**
 * The renderer's art: every sheet it draws, by file name, and the altitude bands those sheets dress a course
 * in. This is the one place under `renderer/` that names a served art file. Each name is a file under
 * `wwwroot/art/`, named after its content. The decoration kinds' sheets are named in their catalogue,
 * `courseDecorations.js`.
 */

/** Art sheets by key, resolved against `assetBaseUrl`. */
export const SHEET_FILES = {
    runner: 'ninja-sprite-set.sha256-f31fb7ac4b0a55af.webp',
    ghostRunner: 'ninja-sprite-set-ghost.sha256-199652c883a55e3a.webp',
    crest: 'course-terrain-crest.sha256-30e5984e457f4105.webp',
    hazards: 'course-hazards-and-pits.sha256-026372fd063914b5.webp',
    vineTexture: 'vine-swing-texture.sha256-436c6bb61c4cde4d.webp',
    vineTree: 'vine-tree-shadow.sha256-62992485794187ae.webp',
    vineBranch: 'vine-branch.sha256-02616d19bdf95b1d.webp',
    waterfallBody: 'waterfall-body-tile.sha256-017cb1c581192556.webp',
    waterfallCrest: 'waterfall-crest-tile.sha256-cc912d6ea26d5f49.webp',
    toriiBack: 'gate-torii-back.sha256-6bf4254b5fa39a04.webp',
    toriiFront: 'gate-torii-front.sha256-cb31a463a732bb52.webp',
    ghostPoof: 'ghost-poof.sha256-0e59db37ca32f5db.webp',
    ghostWarpIn: 'gate-ghost-warp-in.sha256-29c8b888590abc92.webp',
    climbShadow: 'climb-bamboo-shadow-sweep.sha256-2636979b91a6798c.webp',
};

/**
 * How much of the camera's own movement a backdrop plane makes, on both axes. Horizontally it is what keeps
 * the skyline behind the course; vertically it is what makes a climb read as the mountain sinking past the
 * runner rather than as a still image the terrain slides over.
 *
 * Every band is a far plate under a nearer silhouette, each at its own share, so the two separate as the
 * camera moves and the gap between them is the depth.
 */
const BACKDROP_FAR_PARALLAX = 0.09;
const BACKDROP_NEAR_PARALLAX = 0.20;

/**
 * What every course is dressed in: the altitude bands the view rises through, the first from the start of the
 * course and each next one from the midpoint of a climb's slope. A composed map's two climbs are its part
 * boundaries, so its first part is drawn in the foothills, its second on the mid-ridge and its last at the
 * summit, whatever parts it was drawn from. A course with more climbs than there are bands stays on the last
 * one; one with fewer never reaches it, unless it names a higher band to start in (`Course.altitudeBand`).
 *
 * A band names the sky filling the frame behind it and the backdrop planes drawn over that sky, back to front:
 * a far plate under a nearer ridge silhouette. `depth` is the plane's share of the camera's own movement, and
 * `mirrorTile` flips every second repeat so a plane whose two edges do not match wraps without a step in its
 * silhouette. Terrain in a band is dressed in the crest stage of the band's name (`CREST_STAGE_Y`), so the
 * crest changes where the backdrop does.
 */
export const ALTITUDE_BANDS = [
    {
        name: 'foothills', sky: '#1a5374',
        planes: [
            { file: 'backdrop-foothills-far.sha256-7d49375e1462db4d.webp', depth: BACKDROP_FAR_PARALLAX, mirrorTile: true },
            { file: 'backdrop-foothills-near-sharp.sha256-313a8a66b5da7f47.webp', depth: BACKDROP_NEAR_PARALLAX, mirrorTile: true },
        ],
    },
    {
        name: 'mid-ridge', sky: '#195477',
        planes: [
            { file: 'backdrop-mid-ridge-far.sha256-a153df8bb26005a5.webp', depth: BACKDROP_FAR_PARALLAX, mirrorTile: true },
            { file: 'backdrop-mid-ridge-near.sha256-593b0ca55357d054.webp', depth: BACKDROP_NEAR_PARALLAX, mirrorTile: true },
        ],
    },
    {
        name: 'summit', sky: '#165579',
        planes: [
            { file: 'backdrop-summit-far.sha256-db9ca16054c0ae20.webp', depth: BACKDROP_FAR_PARALLAX, mirrorTile: true },
            { file: 'backdrop-summit-near.sha256-564ac15b7b301226.webp', depth: BACKDROP_NEAR_PARALLAX, mirrorTile: true },
        ],
    },
];
