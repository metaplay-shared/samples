# Art Direction

How Sticky Paws looks and moves: the illustration style, the card frame, the clan and rarity languages, and the rules
that keep art and game state apart. What sits where on the board, and why, is [`client.md`](client.md); the shipped
rasters and how they were made are [`../Client/wwwroot/art/README.md`](../Client/wwwroot/art/README.md).

## Style

Manga-cute storybook portraits: chibi animals, bold readable silhouettes, thick dark outlines, rounded shapes and
restrained painted texture — mischievous and collectible rather than babyish. The cute surface is deliberate: it
softens a for-keeps economy into schoolyard "playing for keeps" rather than a casino.

An illustration carries no text, numbers, icons, rarity marks or frame. Every value a player reads is live text, so a
rank change, a Weather discount or a buff never waits on a repaint.

## The card

- **One summary geometry for both types.** Critters and Tricks share a 96 × 140 card and one frame; a Critter adds a
  stat-socket strip for attack and health. Card type changes the content, never the hit area. Hover enlarges the whole
  card in place, keeping its fan rotation, and a separate parchment tooltip carries rules, type, rank, keyword
  explanations and flavour — there is no second, large card design.
- **Two illustration layers.** A clan-tinted backdrop is clipped to the art window; a Critter is a transparent cutout
  drawn above it, free to break the frame with one clear gesture (a paw, an ear, a tail). Tricks are full-bleed
  scenes. Cost and clan badges sit above all artwork, and every portrait keeps the name strip and both top corners
  clear.
- **Rarity lives inside the frame.** It colours the nameplate and adds a marker at its left end: Common a copper
  circle, Rare a cyan diamond, Epic a magenta crown. The shape carries the meaning without colour, and rarity never
  glows outside the card, because exterior light belongs to gameplay state.
- **Gameplay state recolours the card's backing.** Every card sits on a translucent physical backing, and state tints
  it with a matching halo: lime for ready (with a travelling highlight), violet for selected, orange for an attack
  target, emerald for a heal target. Guard, Bubble, Sneaky, sleep, damage and the padlock are independent overlays.

## Clans

Each clan has a shape language and palette as well as a colour, and its symbol is named after the mark rather than the
clan, so the art survives a renaming.

| Clan | Symbol | Shapes | Palette | Personality |
|---|---|---|---|---|
| Kitsune Flames | `split-flame` | Rising teardrops, forward diagonals, swept tails | Coral red, ember orange, cream | Impulsive, theatrical, delighted by speed |
| Tidepool Otters | `tidal-eye` | Circles, water arcs, loops, stacked pebbles | Turquoise, lagoon blue, seafoam | Curious, methodical, smug about what comes next |
| Mossback Bears | `rising-rings` | Low wide triangles, growth rings, boulders | Moss green, honey brown, stone grey | Patient, wholesome, inevitably enormous |
| Sunny Pups | `hearth-knot` | Linked figures, sunbursts, shields, scarves | Warm yellow, biscuit gold, red accents | Earnest teamwork and snack-based morale |
| Moonlight Raccoons | `returning-crescent` | Crescents, hooks, masks, reaching paws | Indigo, violet, moonlit silver | Clever, shameless, sure everything is reusable |
| Wanderers | `waystar` | Simple species-led silhouettes | Warm neutrals, meadow green, dusty blue | Ordinary animals with strong opinions |

Rarity scales the drama: a Common is front-readable with one prop, a Rare takes a stronger diagonal and a bolder
breakout, and an Epic gets a dramatic crop, distinctive lighting and the largest silhouette.

## Motion

- **Cause before result.** Every changed number has a visible cause: impact lands before health changes, a body
  reaches the graveyard before its count rises, a heal reaches its target before the hearts refill.
- **Motion teaches the rules.** Combat damage lands on both critters in the same frame, because the exchange is
  simultaneous; effect chains play one beat per step in the rules' own order.
- **Local feedback is not game state.** Selection, a lifted card and target rings appear at once; cards, health, mana
  and counts move only after the server confirms.
- **Physical cards, restrained effects.** Cards rise, travel on shallow arcs and settle; flashes and floating values
  mark individual impacts, with no screen shake or long particle trails over a six-critter board.
- **Motion must end.** Every sequence has a stable final frame; reduced motion collapses travel to it without changing
  the order, and a reconnect draws the current state rather than replaying beats.

## Accessibility and layering

Rasters provide illustration and material only. Interaction, readiness, targeting, damage, sleep and disabled states
belong to CSS and component state, and decorative images are marked decorative. Cards, Dens, zones, the Weather and
End turn carry accessible names and focus states, and hover always has a keyboard-focus and tap equivalent. Opponent
hand cards are backs: no art, label or tooltip may name a hidden card.
