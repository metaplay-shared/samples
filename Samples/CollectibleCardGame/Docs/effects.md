# The Effect System

What makes a card do something. Cards, keywords, Weathers and rank tracks are pure config data
([`game-design.md`](game-design.md)), so card behavior must be data too: a **small closed set of effect primitives**,
implemented once in the engine, composed per card in game config. That choice comes first because it fixes
signatures: a hardcoded-card engine would be rewritten, not extended. This doc is the
system's design — what the vocabulary is, how it resolves, and what keeps it deterministic. The concrete schema and
the primitive parameter lists are implementation material and deliberately not here.

## Philosophy: a closed vocabulary, composed in data

The engine implements a fixed set of primitive verbs — on the order of ten: damage, heal, draw, gain mana, change
stats, grant a keyword, summon, bounce, copy from a graveyard, peek at the deck — and knows nothing about any specific
card. A card is a config entry that binds sequences of primitive invocations to triggers; a Weather binds the same
vocabulary to match-wide hooks. Adding a card is a config change. Adding a *primitive* is an engine change, and the
bar for one is deliberately high: if two candidate primitives can be one verb with a parameter, they are one verb —
temporary and permanent mana gain are the same primitive, a stat change up and a stat change down are the same
primitive. The primitive set only grows when a card genuinely cannot be composed from what exists.

The payoff is threefold. Content lands through the config pipeline with build-time validation rather than through code
review; the client can render any card it has never seen, because everything a card does is data it already knows how
to display; and the effects engine gets fuzzed for free by the seeded self-play harness, because every card is just
another walk over the same small interpreter.

The composition model is intentionally poorer than a scripting language: an effect is an **ordered list of primitive
steps**, nothing more. No conditionals, no loops, no nested sub-effects. Every card in the pool fits this
shape, and an interpreter that walks a flat list is one a deterministic engine, a bot policy and a UI can all reason
about cheaply.

## Resolution: one FIFO queue, no reactions

Effect resolution is a single FIFO queue: an action enqueues its
steps, each step resolves against the *current* state when dequeued, and anything a step causes — a death, a triggered
effect — enqueues at the tail. There are **no priority windows, no reactions, and no interrupts**: the opponent
never acts inside a resolution, and neither does the acting player, with one exception below. This is what
keeps determinism cheap and the client's job tractable — a resolution is a straight line the match actor publishes as
a unit ([`match.md`](match.md)).

Because there are no reactions, every ordering question has a fixed answer, stated once and obeyed everywhere:

- Steps bound to one trigger resolve in their authored order.
- When one event triggers several cards at once, their steps enqueue in the order the cards **entered play** — board
  position is cosmetic ([`game-design.md`](game-design.md)), so it is never an ordering.
- When a Weather and cards trigger on the same event, **the Weather goes first** — the sky acts before the critters.
  This is scoped to one event, not batched across several: when a sweep kills three critters at once, the queue reads
  Weather-then-Goodbye for the first death, then for the second, then for the third, in the canonical death order —
  not all three Weather steps followed by all three Goodbyes.
- Deaths are swept after every step; the dead leave the board, then their Goodbye effects enqueue, again in
  entered-play order.
- A step whose target no longer exists resolves as a no-op — never an error, never a skip of what follows it.

### The one interactive resolution

One effect — look at the top of your deck, keep part of it — cannot make its choice at cast time, because the
information does not exist until the effect reveals it, and it cannot be made by rule, because choosing is the card's
whole point. So this primitive, alone, **pauses the queue for its owner's choice**: the revealed cards travel on the
seat's private channel (they are revealed to one player, so they never touch the public unseen pool —
[`hidden-information.md`](hidden-information.md)), the board publicly shows a resolution held on that seat, and a
directed intent supplies the choice. This is not a reaction and not a priority window — it is the acting seat's own
action, still in flight — and it obeys the same clocks as everything else: the turn deadline keeps running, and an
absent or covered seat has the choice made by the bot policy's deterministic rule, so the pause can never stall a
table. Nothing else may pause a resolution, and a new effect that wants to should be measured against this one's cost.

## Triggers

A card's effects are bound to triggers, and the trigger vocabulary is fixed:

- **Hello** — the card is played from hand. This is also how tricks work: a trick is a card whose whole body is its
  Hello, resolved on cast, after which the card goes to the graveyard. Tricks may bind nothing else.
- **Goodbye** — the critter is destroyed.
- **Attack** — the critter attacks, resolving after its combat damage is dealt and deaths are swept; it fires even if
  the attacker died attacking.
- **Turn start / turn end** — the boundaries of the owner's own turn.

Hello and Goodbye are also two of the seven player-facing keywords ([`game-design.md`](game-design.md)) — they are the
same concept wearing its display name. Weathers use the same event vocabulary plus one global event (a critter died),
scoped as described below.

## Targets and amounts

A step's target is either **fixed** — the source critter itself, either Den, everything on one side or both sides, a
graveyard — or **chosen at cast time**. A card declares at most **one chosen target**, picked by the player as part of
the play intent and addressed by match instance identity like everything else
([`hidden-information.md`](hidden-information.md)); every step of that card that wants a choice shares the one
choice. Target legality (which side, critter or Den, Sneaky exclusions) is part of the shared legality rules both
client and server run.

There are **no random targets and no random effects**, and adding one would mean revisiting a pillar: the game's
randomness is input-shaped — the deal and the Weather — never dice on outcomes ([`game-design.md`](game-design.md)). No
primitive draws from the RNG at all. Where a card needs to select something itself (the cheapest trick in a graveyard),
it selects **deterministically** — an explicit ordering with an explicit tie-break — so the selection is a pure
function of public state that both players could have computed.

An amount is either a **literal** or a **count of something public** — cards you have played this match, critters on a
side — optionally filtered by clan or card type. Counting public state keeps scaling cards honest: the number a card
resolves for is one both players can read off the board.

## Keywords

Keywords come in two kinds, and the config says which each is:

- **Engine flags** — Guard, Zoomies, Bubble, Sneaky, Snacktime. Their semantics are combat and targeting rules,
  implemented in the engine's code, not composable from primitives; the config entry contributes identity and display.
  A primitive exists to *grant* one in play.
- **Trigger labels** — Hello and Goodbye, the display names of two triggers. A card shows the label because it has
  effects bound to the trigger, never as an independent property.

The flag interactions are engine rules, defined once: Sneaky hides a critter until it deals damage, and a hidden
critter's Guard is suppressed — an untargetable bodyguard would otherwise lock the Den behind a critter nobody may
attack. Bubble absorbs the first *damage*, not the first anything — a stat reduction is not damage and does not pop
it. Snacktime applies to damage the critter *sources* — its combat damage and its own triggered effects.

## Weathers

A Weather is a public, symmetric, match-wide modifier drawn before the mulligan ([`game-design.md`](game-design.md)). In
effect-system terms it is a config entry with up to three hooks, all drawn from vocabulary that already exists:

- a **keyword aura** — every critter in play has some keyword while the Weather holds;
- a **cost rule** — a scoped discount or surcharge, such as the first trick each turn costing less;
- a **triggered effect** — the same primitive steps cards use, bound to turn boundaries or to any critter dying, and
  resolved *as the seat the event belongs to*: the seat whose turn it is, or the owner of the critter that died. That
  scoping is what makes one config row symmetric by construction.

Weathers are the only source of auras and cost modification. Card-borne auras and cost modifiers are deliberately
out of scope — they are the two mechanisms that turn a flat interpreter into a rules engine with a dependency graph —
and the Weather versions stay cheap precisely because there is exactly one Weather, known before the first card is
played.

## Rank tracks

A rank track is a config entry describing how a card grows with its rank ([`game-design.md`](game-design.md)): **numeric
deltas only** — attack, health, cost, or the magnitude of the card's effect — at the track's threshold ranks. Tracks are
applied when the match resolves each deck against its owner's collection at the deal ([`match.md`](match.md)); inside
the engine a card's numbers are simply what the track made them, and no primitive ever consults a rank. Tracks apply in
every mode. They never add keywords, triggers or steps, because a track
that changes *what a card does* (rather than how hard it does it) breaks the promise that reading a card is reading the
card.

## Determinism

Effects run inside the seeded, pure rules and obey their discipline ([`rules.md`](rules.md#determinism-discipline)): no
wall clock, no unordered iteration, and **no RNG** — there is no `Random` on the effect context at all, because a client
re-executing the action has no stream ([`rules.md`](rules.md#the-effect-interpreter-is-a-component-behind-a-boundary)).
Every rule-made choice — a graveyard selection, the peek default, a bot's pick — is a pure function of visible state
with stated tie-breaks.

## What config-time validation proves

Content is only as safe as its build. The config build must fail — not warn — when the data cannot mean anything:

- **Every reference resolves**: clans, keywords, rank tracks, effect steps, summoned cards, the second-player
  compensation card, Weather hooks.
- **Nothing names vocabulary the engine does not implement**: every primitive, trigger, target kind, counter, selector
  and engine-flag keyword parses against the closed sets.
- **Every step is shape-legal for its primitive**: required parameters present, inapplicable ones absent, targets of a
  kind the primitive accepts.
- **Every card is shape-legal for its type**: critters have stats, tricks do not and bind only Hello; a Snack points
  only at its own side; a card that declares a chosen target uses it, and one that uses it declares it.
- **Rank tracks stay in range**: no track pushes any card that references it below zero cost or out of legal stats,
  and a track that scales an effect is only referenced by cards with an effect it can scale.
- **The pool can build legal decks** — the singleton pool sanity check: enough distinct collectible cards exist within
  every allowed clan combination to build a legal deck, non-collectible cards (tokens, the second-player compensation)
  are excluded from deck building and from the starter collection, and the starter collection itself can build a legal
  deck on day one.
- **Weathers are well-formed**: hooks internally consistent, steps compatible with Weather scoping (no chosen
  targets — there is nobody to choose).
- **Globals are mutually consistent**: opening hands fit in the hand limit, deck size covers the opening draw, numeric
  knobs are in range.

Per the project's testing strategy, every one of these checks ships with a negative control — a fixture that must fail
— because a validator that rejects nothing passes everything.

## What the vocabulary leaves out

Deliberately, so the interpreter stays flat: reactions and priority windows; card-borne auras and cost modifiers
(Weather-only); temporary until-end-of-turn stat changes (all stat changes are permanent); random effects of any kind;
hard removal, discard, silence and transform primitives; choosing targets from graveyards (deterministic selectors
only); more than one chosen target per card; face-down or secretly-committed effects; rank-track perks beyond numeric
deltas; and persistent enchantment or relic card types. Each has a known cost the pool does not pay for.

## See also

- [`game-design.md`](game-design.md) — the cards, keywords and Weathers this system exists to express.
- [`match.md`](match.md) — the host that drives resolution and publishes it.
- [`rules.md`](rules.md) — the queue, the resolution points and the mutation surface behind the boundary.
- [`hidden-information.md`](hidden-information.md) — the private-reveal channel the peek uses; the RNG rules.
- [`bots.md`](bots.md) — the policy that supplies deterministic defaults for absent players.
