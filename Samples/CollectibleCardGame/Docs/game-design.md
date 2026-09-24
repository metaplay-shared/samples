# Sticky Paws — Game Design

What the game is: the match, the cards, the Heist and the economy around it. A compact but complete PvP card game loop
with a small hand-authored pool, built as a Metaplay sample. The numbers are content in game config, tuned by feel and
by self-play rather than by a balance pass. How each system is built is indexed in [`README.md`](README.md); possible
extensions are the README's [Follow-up work](../README.md#follow-up-work).

**The game is intentionally minimal and not designed to be fun.** Its rules and cards are the smallest set that
exercises every system the sample demonstrates. The pillars below describe the kind of game this skeleton is shaped
for; delivering on them — interesting mechanics, cards and synergies — is the work a real game would add.

---

## Pitch

A player-vs-player collectible card game about adorable animal clans robbing each other blind. Matches play like a
streamlined Hearthstone/Skyweaver: summon critters, cast tricks, smash the enemy Den. The twist: **after a ranked match,
the winner steals a rank from one card the loser dared to play.** Your best cards win you games — and putting them on
the table is how you risk losing them. Progression, stakes, and collection are one circulating, play-driven economy.

## Design pillars

1. **Every match matters.** Cards themselves are the stakes. No packs, no grind treadmill — your collection's power is
   won and lost in play.
2. **Courage is rewarded, hoarding is punished.** Only cards you actually played are stealable, so fielding your bombs
   is both how you win and what you wager.
3. **Plan, don't pray.** Randomness is input-shaped — singleton draws and a public per-match Weather — never dice on
   outcomes. Open graveyards and unseen pools. Wins feel earned; losses feel explainable.
4. **Cute on the surface, sharp underneath.** Manga-cute animal presentation softens the for-keeps economy — schoolyard
   "playing for keeps," not a casino.

## Reference positioning

| Axis | Hearthstone | Skyweaver | **Sticky Paws** |
|---|---|---|---|
| Hero | Class hero + power | Fighting hero | **None — attack the Den (life pool)** |
| Deck | 30 cards, 2 copies | Singleton 25/30 | **Singleton 25** |
| Identity | Class pools | 5 prisms | **5 clans (colors), max 2 per deck** |
| Mana | Ramp, cap 10 | Ramp, high cap | **Ramp, no cap** |
| Combat | Attacker picks, Taunt | Attacker picks, Guard | **Attacker picks, Guard protects Den only** |
| Info | Hidden | Open zones | **Hands hidden; graveyards + unseen pools open** |
| RNG | Embraced | Minimal | **Input-only: singleton draws + public Weather** |
| Stakes | None | Conquest (opt-in stake) | **Winner steals a rank from a played card** |

---

## The match

### Overview

| Parameter | Value |
|---|---|
| Players | 2, alternating turns in a realtime session, server-authoritative |
| Win condition | Reduce the enemy **Den** from **125 HP** to 0 |
| Deck | Exactly **25 cards**, singleton, 1–2 clans |
| Board | Max **6 critters** per side — a full board refuses critter plays, and effect summons onto it fizzle |
| Hand | Max **9**; an overflowing draw returns to the bottom of the deck (never burned) but still ticks Tuckered Out |
| Opening hand | First player 3 cards, second player 4 + **The Acorn** after the mulligan |
| Mulligan | Once: replace any subset of the opening hand |
| Weather | One public, symmetric modifier per match from a pool of six, revealed before the mulligan |
| Draw | 1 card at the start of your turn |
| Mana | Max mana starts at 0 and grows **+1 at each of your turn starts** (so 1 on your first turn), **no cap**; refills each turn |
| Turn timer | 60 s per turn + a small reserve bank |
| Target match length | 5–10 minutes, ~8–12 turns per player |

**The Acorn** is the second-player compensation: a 0-cost trick that grants +1 mana for the current turn (Hearthstone's
Coin). It is granted when the mulligan ends rather than dealt into the opening hand, so the one step that can put a
card back is only ever asked about cards that can go back.

### Turn structure

1. **Start of turn:** gain +1 max mana, refill mana, draw 1 (both seats draw on their own first turn — the opening-hand
   asymmetry plus The Acorn is the whole first-player compensation), your critters wake up.
2. **Main phase:** in any order — play critters, cast tricks, attack with ready critters.
3. **End turn:** pass; end-of-turn effects resolve.

### Weather

Every match is played under one **Weather** — a public, symmetric rules modifier drawn from a config-driven pool of six
and revealed *before* the mulligan, so both players can plan and mulligan around it. Weather is the game's
deliberate randomness: input randomness both players see, never dice on outcomes. It keeps a small card pool fresh, and
it compresses deck-power gaps — a pumped deck tuned for one plan sometimes fights on the wrong terrain, which is part of
the answer to rich-get-richer (see [Keeping the economy honest](#keeping-the-economy-honest)).

Four of the six:

- **Acorn Rain** — the first trick each player casts on their turn costs 1 less.
- **Bubble Bath** — all critters have Bubble.
- **Harvest Moon** — both players gain +1 extra max mana per turn; the whole game plays big.
- **Picnic Day** — whenever a critter dies, its owner's Den heals 5.

Weather applies in every mode.

### Ending the game (no stall-outs)

Uncapped mana needs an external clock. When a draw cannot be taken — the deck is empty, or the hand is full and the
card went to the bottom instead — you get **Tuckered Out**: your Den takes cumulative damage (5, then 10, then 15, …;
the schedule is a config knob). At one draw per turn the clock starts once a deck runs dry (around a seat's low-twenties
turn) and the escalation finishes the game within a few more; real matches end on Den damage well before that. A draw
(both Dens die simultaneously) is a tie: no steal, and each side scores a half in the rating.

### Information rules

- **Hidden:** both hands, deck *order*.
- **Open:** both graveyards, and each player's **unseen pool** — the unordered combined contents of their deck *and*
  hand, with deck and hand counts public separately. (A public listing of the remaining deck alone would leak the whole
  hand by subtraction, one card per draw. The pool keeps singleton's planning payoff — you know what you haven't seen
  yet — without narrating the draws.)
- **Public before the match:** the Weather, both deck Power Scores, both lock sets, and the stakes tier they imply —
  nobody discovers after winning that the Heist pays out less than expected.
- **Design consequence:** visibility and stealability are different things. Your whole deck is knowable; only what you
  *play* is stealable.

---

## Cards

### Anatomy

Every card has: **mana cost**, **clan** (or Wanderer), **type**, **rarity** (Common/Rare/Epic), name + art + flavor, and
— per player collection — a **rank (1–5)** with a per-card **rank track** (see [Ranks & The Heist](#ranks--the-heist)).

### Types

- **Critters** (units): Attack / Health. Enter play *sleepy* (can't attack until your next turn) unless **Zoomies**.
  Damage on critters persists (healing exists but is scarce and clan-gated).
- **Tricks** (spells): one-shot effects. Some are **Snacks** (a trick subtype that targets your own critters — buffs and
  treats) to give tribal-ish hooks without new card types.

Two types only, by design: it keeps the rules engine and the UI small.

### The five clans

Max 2 clans per deck. Each clan is a color, a loose family of animals, and a mechanical identity. Clan names are
faction identities rather than a declaration that every member is one particular species:

| Clan | Color | Animals | Identity | Signature moves |
|---|---|---|---|---|
| **Kitsune Flames** | Red | Foxes | Aggression, burn | Zoomies critters, direct Den damage, sacrifice-for-speed |
| **Tidepool Otters** | Blue | Otters, penguins | Tempo, knowledge | Card draw, bounce, cost manipulation, deck peeking |
| **Mossback Bears** | Green | Bears, boars, deer | Growth, size | Mana ramp, oversized critters, +1/+1 growth |
| **Sunny Pups** | Yellow | Dogs, sheep | Protection, teamwork | Guard, healing, wide boards, whole-team buffs |
| **Moonlight Raccoons** | Purple | Raccoons, cats, crows | Theft, recursion | Sneaky, graveyard recursion, stealing enemy tricks/stats in-match |

**Wanderers** (neutral, no clan) are the glue: playable in any deck, deliberately fair-but-bland.

The Moonlight Raccoons deliberately echo the meta-game: the clan that steals *inside* the match mirrors the game that
steals *between* matches. They're the mascot clan.

### Keywords

| Keyword | Effect |
|---|---|
| **Guard** | Enemy critters can't attack your Den while you have a Guard they could legally attack instead — a Guard the enemy can't attack (e.g. Sneaky) doesn't gate the Den. Guard never makes a critter unattackable or redirects attacks onto it: it protects the Den, not the team (Skyweaver-style). |
| **Zoomies** | Can attack the turn it's played. |
| **Bubble** | Ignores the first damage it would take, then pops. |
| **Sneaky** | Can't be attacked or targeted by the enemy until it deals damage. |
| **Snacktime** | Damage this critter deals also heals your Den for the same amount. |
| **Hello:** | Effect when played from hand. |
| **Goodbye:** | Effect when destroyed. |

Seven keywords — small enough to teach in one match.

### Card pool

**65 collectible cards:** ten per named clan and 15 Wanderers, plus Acorn and Lamb as non-collectible rules pieces.
The starter grant is 45 of them, and the six premade decks are built from those. Rarity signals complexity and
steal-desirability, not raw power.

### Example cards

Rank tracks are shown to make the progression concrete.

- **Ember Kit** — 1 mana, Kitsune Flames, Common. Critter 10/5, **Zoomies**. *Rank 2: +1 health · Rank 4: +1 attack
  (additional).*
- **Nine-Tail Matriarch** — 6 mana, Kitsune Flames, Epic. Critter 25/20, **Hello:** deal 5 damage to the enemy Den for
  each other Kitsune card you've played this match.
- **Pebble Collector** — 2 mana, Tidepool Otters, Common. Critter 10/10, **Hello:** look at the top 3 cards of your
  deck, put one in your hand and the rest on the bottom.
- **Undertow** — 4 mana, Tidepool Otters, Rare. Trick: return a critter to its owner's hand, then draw a card. *Rank 3:
  −1 cost.*
- **Acorn Hoard** — 2 mana, Mossback Bears, Common. Trick: gain +1 max mana permanently.
- **Old Mossback** — 7 mana, Mossback Bears, Epic. Critter 35/40, **Guard**. *Ranks 2–5 together: +6 attack and +6
  health.*
- **Sunbeam Retriever** — 3 mana, Sunny Pups, Common. Critter 10/15, **Guard**. *Ranks 2 and 4: +1 health each;
  ranks 3 and 5: +1 attack each.*
- **Warm Biscuit** — 2 mana, Sunny Pups, Common. Trick (Snack): restore 20 HP to your Den or a critter. *Ranks 2, 3 and
  5: +1 healing each.*
- **Dumpster Bandit** — 3 mana, Moonlight Raccoons, Rare. Critter 10/15, **Hello:** add a copy of the cheapest trick in
  the enemy graveyard to your hand.
- **Moonlit Alleycat** — 2 mana, Moonlight Raccoons, Common. Critter 15/5, **Sneaky**.

---

## Combat

- On your turn, each of your awake critters may attack once: any enemy critter, or the enemy Den if the enemy has no
  **Guard** in play.
- Critter-vs-critter combat is simultaneous: both deal damage equal to their Attack.
- The Den never counterattacks.
- Damage on critters persists between turns; healing is deliberately scarce (mostly Sunny Pups).
- No positioning — board order is cosmetic.

---

## Ranks & The Heist

The progression system and the signature mechanic are one system.

### The stat domain

Attack, health, Den hit points and every damage, heal and buff amount are counted in a domain five times finer than
the numbers a card looks like it has: a "1/2 for 1" critter is authored as 5/10, and the Den has 125 hit points rather
than 25. Nothing in the rules divides by that factor and no player-facing number is ever shown divided — the domain
*is* the game's numbers. The factor exists so that a rank track can grant a fifth of a visible point, which is what
makes the growth budget below meetable; `Global.StatQuantum` (5) lets content rules tell a coarse number from a
deliberately fine one.

Mana costs, card counts, board and hand caps, mulligan counts, deck size and turn counts are **not** in this domain:
they are a different kind of number.

### Ranks

- Every card in your collection has a rank, **1–5**. New cards arrive at rank 1.
- Ranks give config-driven stat growth, authored as **deltas**. Critters target approximately **16% total
  attack + health growth by rank 5**, rounded to the nearest whole point. This is a content budget, not a claim
  that combat win probability grows by 16%. Bonuses are distributed across ranks 2–5.
  Bodies with at least four bonus points improve at every rank; two-point tracks grant one point at ranks 2 and 4,
  and three-point tracks at ranks 2, 3 and 5. Zero-attack critters gain health only.
  Bonuses scale with the printed body instead of giving every critter the same +1/+1. For example, Wise Tortoise
  grows from 10/20 to 12/23, and Old Mossback from 35/40 to 41/46. Ember Kit's smallest-body rounding remains
  10/5 to 11/6 (13.3%). Card details name each stat and state that later bonuses are additional.
- Tricks retain effect-specific tracks: Foxfire gains 2 damage over its base 15, Warm Biscuit gains 3 healing over
  its base 20, and Undertow costs 1 less mana. Mana is not multiplied by the stat quantum; the 4-to-3 mana discount
  remains an explicit 25% exception.
- A deck's **Power Score** = sum of its cards' ranks (25 baseline, 125 max). Matchmaking uses it. Home displays the
  selected deck’s score beside its picker, and switching decks updates it.
- Alongside the starter collection, an account is offered a set of **config-authored starter decks** it can play
  immediately, in either mode, from the same picker as its own saved decks. They are content: every card in them is in
  the starter grant, so nothing about them is exempt from the ownership rule — and a starter deck is a card list and
  nothing more, so it is seated at whatever ranks the account holds and scores that account's Power Score. A veteran
  who plays one plays a real deck for real stakes. The deckbuilder is therefore optional on day one rather than a gate.

### Locks (the trophy case)

A player may **lock** cards in their collection. A locked card can neither lose ranks **nor gain them** — locking is
retirement, not insurance with upside. To grow a card you must field it exposed; when you are happy with what it earned,
you lock the gains in, and it still plays.

- **2 lock slots**. The slot count is account state, so progression could widen it; the principle governing any
  progression reward is that **it widens the toolbox, never deepens it** — access, never ranks.
- Locks are **open information**: a padlock on the card frame, visible to the opponent. Lock state freezes at enqueue
  together with the deck, so the pre-match stakes display is always honest.
- The Heist skips locked cards. **A card below rank 2 cannot be locked at all** (`Global.MinLockRank`, default 2), so
  slots are spent where the tension is — on the cards you raised. At the floor a lock freezes nothing and the floor
  already protects the card, so locking one would be a free denial play: it takes the card off a winner's Heist menu
  without the owner giving anything up, and heist-acquisition is how the economy spreads cards in exactly the era
  every card is at the floor. Requiring a raised card makes every lock cost something real. The rule is validated in
  shared code, so both sides refuse identically, and the lock affordance is simply absent on a card that is too low.
- Frozen-both-ways is what keeps every exploit boring: lock-swapping churn buys nothing because frozen cards don't grow,
  and a bomb behind a lock is a bomb that stopped growing.

### The Heist (post-match steal)

After every **ranked** match against a human opponent:

1. **Eligibility:** cards the loser *played* during the match (not merely drawn or seen), excluding locked cards; only
   cards from the loser's own deck list count — tokens, summons and in-match copies of unowned cards are never
   eligible. **Locks subtract from both sides:** a card the *loser* has locked is exempt from the wager, and a card the
   *winner* has locked their own copy of is subtracted too — the winner's copy is frozen, so taking that rank would
   destroy one for nothing. Both are shown on the pick screen, padlocked and unpickable, so the winner can see what
   their own lock cost them.
2. **The Pick:** the winner sees the eligible cards and picks one.
   - Winner's copy of that card: **+1 rank** (if unowned: acquired at rank 1; if already rank 5: nothing moves).
   - Loser's copy: **−1 rank**, floored at rank 1. **Cards are never removed from a collection** — rank 1 is safe.
   - The two sides are applied independently, and the loser's is applied first, so a pick the winner cannot benefit
     from still costs the loser a rank. The self-lock case is off the menu (above); the rank-5 case is what is left,
     and it is the one place the design pays for the two applications not being one transaction.
   - Nothing eligible (everything played was locked): nothing moves, and the end screen says which case it was.
3. Presentation matters: The Heist is its own full screen — the raccoon-mascot moment the whole game is named after. The
   loser owes nothing here: with no post-match decision on their side, losing ends cleanly. The end screen names the
   stakes tier that applied — a covered human seat keeps human stakes; only a formation-time bot opponent plays at
   practice stakes.

The steps above describe an even matchup; the payouts scale with the matchup, below.

### Asymmetric stakes (the Elo lesson)

The Heist's stakes scale with the **Power Score gap** between the decks, chess-rating style: punching down pays nothing,
upsets pay double. With the gap measured as winner's Power Score minus loser's (threshold ±15):

| Matchup | Winner gets | Loser loses |
|---|---|---|
| Even (gap within ±15) | Standard Heist: pick one, +1 rank | −1 rank on the pick (floor 1) |
| Favorite wins (gap above +15) | Nothing — no rank steal | Nothing |
| Underdog wins (gap below −15) | Picks **two** played cards, +1 rank each | −1 rank on each pick (floor 1) |

This makes farming weaker collections EV-negative — little to win, real ranks to lose — and turns every upset into
redistribution. The division of labor: *rating* gaps are matchmaking's problem, *Power Score* gaps are the stakes'
problem. The tier is shown on the pre-match screen (see Information rules), so the wager is always agreed sight-seen.

Because wins inject rank progress while losses only remove ranks *above* the floor, the economy injects value at the
bottom and stays zero-sum-ish at the top: collections inflate slowly through play, but rank-5 cards are perpetually
contested. Progression is meaningful and literally *is* the core gameplay.

### Safety nets

- **Rank-1 floor:** you can never lose a card outright.
- **Newcomer shield:** no rank loss for a player's first N ranked matches (current config: 2). A shielded match is its
  own pre-match-visible stakes tier: no rank moves in either direction.
- **Locks:** the cards you choose are permanently exempt — and frozen (see Locks above).
- **Practice:** no stakes. Each deck plays at its real ranks, as it does against the ranked bot fallback; only the
  transfer is off.

### Keeping the economy honest

- **Rich-get-richer:** attacked from three sides. Matchmaking pairs on rating *and* Power Score band, so pumped decks
  meet pumped decks; asymmetric stakes make punching down pay nothing; and Weather periodically favors the leaner deck.
  Stealing pressure at the top redistributes ranks naturally.
- **Sandbagging** (keeping Power Score low on purpose to farm the underdog bonus): mostly self-defeating —
  Power-Score-band matchmaking pairs light decks with light decks, so the underdog tier rarely triggers by choice, and
  dumped ranks are real in-match power dumped.
- **Win-trading** (two accounts pumping one): ranked pairing is matchmaker-only — no direct challenges, no lobby — so
  two accounts cannot arrange to meet, and rating is always at stake.
- **Concede-dodging** (quitting early to expose nothing): impossible by construction. A conceded or deserted seat is
  covered by a bot and the match is **played out to a real result**, so leaving costs exactly what staying would — the
  Heist pool is what a full game exposes, not what a rage-quit chose to show. The surviving player watches the
  play-out and makes their Heist pick.
- Server-authoritative everything: match resolution, rank transfers, and collection state live on the server; the client
  is a view.

---

## Modes & meta

- **Ranked (the game):** matchmade, stakes on, rated. Your collection is your long-term identity; rating is the ladder.
  Home shows a global rating leaderboard, its top 20 and your own position ([leaderboard design](community.md)). The
  queue bands on the same rating. If no human opponent arrives within the fill wait, ranked falls back to a labelled
  bot at practice stakes: rating moves, ranks never do.
- **Practice:** against a bot of a chosen difficulty, for onboarding and for testing decks.

### Collection & acquisition

- New players receive the **45 cards marked `InStarterCollection`**, at the minimum rank, and six premade deck lists
  using those cards. There is no clan-kit choice at account creation. The remaining 20 collectibles start unowned.
- **Card gifts** seed unowned cards into circulation. By default, every three completed matches earns one uniformly
  random unowned collectible at rank 1. Wins, losses and draws count; practice counts by default. Config controls
  the interval and whether practice counts; zero disables gifts. Abandoned matches do not count.
- The metagame displays progress and automatically grants a ready gift. Its notice remains until acknowledged and
  survives reloads. A full collection earns no duplicates. Gifts grant ownership at the floor, never extra ranks.
- **Heisting** a card you do not own also grants a copy at rank 1. The loser retains their copy, so Heists spread
  existing ownership; gifts provide the first owners for cards outside the starter grant.
- **No packs, no crafting, no dust, no purchases.** Acquisition widens the toolbox — access at rank 1, the power
  floor — and never deepens it: every rank above the floor is won at the table, and the Heist is the only place ranks
  move.

---

## UI

Browser-first (Blazor/WASM), landscape layout, mouse + touch.

Screens: **Home** (queue buttons, deck picker, leaderboard, gift progress) · **Searching** (a dialog over the current
screen, not a screen of its own) · **Pre-match reveal** (Weather, Power Scores, lock sets, stakes tier) · **Match
board** · **The Heist** (post-match) · **Collection / deckbuilder** (including lock management).

Match board layout: each battlefield row has its Den as the central anchor, with up to three critters on either side
(max 6 each). The hand is fanned at the bottom, mana acorns sit bottom-left, and each seat's graveyard and unseen-pool
inspectors stack at the far left. Weather, mode and the turn/timer button form a single column on the right.

Art direction: manga-cute chibi animals, thick outlines, pastel clan palettes (red/orange Kitsune Flames, teal
Tidepool Otters, moss-green Mossback Bears, warm-yellow Sunny Pups, indigo Moonlight Raccoons), rounded everything.

The card frame, clan languages and motion rules are [`art.md`](art.md).

---

## Why this fits Metaplay

Cards, clans, keywords, Weathers, rank tracks and starter decks are pure config data (game configs); matches are
server-authoritative turn-based multiplayer with deterministic, low-RNG logic (easy to validate and replay); the Heist
is a server-side economy transaction between two player states (impossible to forge client-side); matchmaking needs
rating and Power Score bands (a singleton service entity); the newcomer shield, gift cadence and match pacing are
LiveOps levers in config, runtime options and the dashboard. Every distinctive feature of the design lands on a core
SDK system — which is the point of a sample.
