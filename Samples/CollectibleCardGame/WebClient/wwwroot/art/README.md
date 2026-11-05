# The board's art

Every raster the client draws, as WebP: the rooftop background, the Den, the card frame and stat sockets, badges, the
six clan symbols and clan backdrops, keyword, zone and Weather icons, and an illustration for each of the 67 configured
cards (65 collectibles plus The Acorn and the Lamb token). WebP throughout, because every asset either has an alpha
channel to keep or is a photograph that JPEG would not beat. Nothing here carries text.

## How it was made

The art was produced with an image-generation tool, one call per asset, and each output was inspected for subject,
composition and transparency before it was kept; no card values, names or frames are painted into an illustration.
Critters are transparent cutouts and Tricks full-bleed scenes. The native-resolution masters were transcoded into
this set — illustrations capped at 768 pixels per side without upscaling, the frame reduced to one 384 × 560 base — and
are not part of the sample: these files are the art. The style they follow is
[`Docs/art.md`](../../../Docs/art.md).

## Size

The whole directory is **9.9 MB**, of which `cards/` is 9.5 MB: 67 illustrations at 512 or 768 square, the
largest illustrations around 207 KiB. That is a deliberate trade for a card game whose art *is* the product, and
it is bounded by what any one screen actually fetches rather than by the total:

- **A match** loads the background, the shared frame, the stat sockets, the Den, the badges and symbols, and
  the illustrations for the cards actually in play — of the order of **1 MB**, not the whole set.
- **The collection** renders the shared `CardFace` for all 65 collectibles. Its illustrations currently load eagerly;
  the full 67-illustration set is 9,295,116 bytes (8.86 MiB), with a 142 MiB decoded RGBA upper bound. Portrait-only
  lists still use lazy `CardPortrait` images. Card faces also remain eager on the board to avoid late card reveals.

## Resolution and hover

The board is drawn at **1280 x 720 and scaled to fit** its window, so its effective pixel density is not the device's
alone: a 2560-wide window draws the 1280-wide design at 2x with no device pixel ratio involved. 2x of the CSS box is
therefore the floor, not a retina extra. The shared frame is 384 × 560 (4x the reference card), balancing
enlarged-card detail against download size; both card types load the same base, and the stat strip adds no duplicate
frame pixels, so the two frame layers total about 27 KiB. Illustrations are 768 × 768, which bounds decoded texture
memory, except the seven original ones at 512 × 512. Browser compositing can still soften rotated layers; more source
pixels cannot eliminate that.

## What each file renders at

| File | Pixels | CSS size | Drawn by |
|---|---:|---:|---|
| `match-background-rooftop.webp` | 2560 x 1440 | 1280 x 720 | `BoardFrame` |
| `den.webp` | 346 x 280 | 173 x 140 | `DenDoor`, both seats, same size. Painted at the box's own aspect (`--den-w` : `--den-height`, 1.2343), so `object-fit: contain` fills the box and distorts nothing; `BoardGeometryCssTests` fails if the two drift |
| `card-frame-shared.webp` | 384 x 560 | 96 x 140 | `CardFace`, both card types |
| `card-stat-sockets.webp` | 384 x 140 | bottom 96 x 35 | `CardFace`, critters only |
| `card-badge-cost.webp` | 64 x 64 | 24 x 24 | `CardFace`, live cost number |
| `cards/background-*.webp` | 512 x 512 | clipped card art window | `CardFace`, clan fallback/backdrop |
| `cards/*.webp` | 512 or 768 square | card illustration layer | `CardFace` / `CardPortrait`, mapped by `CardId` |
| `card-back.webp` | 64 x 90 | 32 x 45 | `OpponentHand` |
| `mana-acorn.webp` | 52 x 48 | 26 x 24 | `ManaAcorns`, unspent |
| `mana-acorn-empty.webp` | 52 x 48 | 26 x 24 | `ManaAcorns`, spent |
| `clan-symbol-*.webp` | 106 x 106 | 24 x 24 | `CardFace` |
| `weather-*.webp` | 132 x 132 | 66 x 66 | `WeatherBanner`, all six Weathers |
| `icon-guard/zoomies/bubble/sneaky/snacktime.webp` | 40 x 56 | keyword badge layer | `CardFace` |
| `icon-lock.webp` | 68 x 96 | lock layer | `CardFace` |
| `icon-graveyard/unseen-pool.webp` | 64 x 64 | zone dock icon | `ZoneInspector` |

## Mappings the filenames do not give you

**Card illustrations are client-baked and keyed by `CardId`.** `CardArtCatalog` owns the explicit mapping. A card
introduced over the air before its art ships uses its clan backdrop and config emoji, so it remains readable.

**Clan symbols are named after the mark, not the clan** — deliberately, so a naming pass over the clans does
not rename the art. `CardArtCatalog` holds the current mapping from `ClanId` to symbol.

**Weather icons are named after the Weather** in kebab-case (`AcornRain` → `weather-acorn-rain`), but
`WeatherBanner` maps them explicitly rather than transforming the id, so a Weather added without art falls
back to its config emoji instead of requesting a file that is not there.

## Everything else on the board is not art

Backings, selection and targeting colour, damage, healing, sleep marks, mulligan marks, motion, refusal feedback
and every number and name are CSS or component state, per the layering rule in
[`Docs/art.md`](../../../Docs/art.md). Nothing in this directory carries text.
