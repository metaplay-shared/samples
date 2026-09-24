# The Rules

The part of Sticky Paws that knows how to play a game of Sticky Paws, and nothing else. It owns the zones, the seeded
deal, mana, the mulligan, the turn flow, combat, the keywords, the Weather hooks, the rank-track stats, Tuckered Out and
the result. It does not own the network, the database, the clock, the players, or the wager.

**The rules of Sticky Paws are the replicated match model and the actions that mutate it:** pure, seeded and timed by
the model's own clock, with no engine object and no second state — the model the clients render is the state the rules
run on.
`MatchModel` is a `MultiplayerModelBase`, a rule is a `ModelAction` the match actor issues, and both clients re-execute
it. What is secret hangs beneath one member.

[`match.md`](match.md#two-layers-deliberately-separated) draws the line between the rules and the actor that hosts
them; this doc is the rules' own side of that line. The game they implement is [`game-design.md`](game-design.md); the data
schema and resolution semantics of card effects are [`effects.md`](effects.md), which the rules treat as a component
behind a stated boundary rather than as part of themselves.

## A pure, seeded state machine on the model's clock

Three properties, each of which rules something out.

**Pure** — the rules are a function of their inputs. They read no wall clock, no configuration of their own, no ambient
random source, no environment. Nothing in them references an entity, a session, or a socket. They compile and run
inside a unit test with no server, no client and no network, which is what makes a thousand games a second possible and
therefore what makes the self-play harness in [`bots.md`](bots.md#self-play) affordable.

**Seeded** — every random decision the game makes comes from one seeded stream, and the seed is an input. Same seed and
same actions in, same game out, byte for byte. This is not merely a testing convenience: the seed is also the secret
that protects both hands ([`hidden-information.md`](hidden-information.md#the-seed-is-part-of-the-secret)), so the
stream's discipline is a security property as much as a determinism one. The stream lives under the secret member, so
only a host has one.

**On the model's clock** — a rule that arms a deadline stamps it from the model's current time plus a duration its host
wrote onto the model ([`protocol.md`](protocol.md#timings-are-host-supplied)); no action carries a time. The clock
moves only by ticks, which the server runs as time passes and every client plays off the timeline, so a follower
re-running the action on the same tick reaches the same stamp ([`protocol.md`](protocol.md#the-models-clock)). The
model never decides that a deadline lapsed: its host does, and issues the action that answers it. The unit and self-play
hosts tick the clock themselves and mostly write zero durations.

There is one more property worth naming because it is what makes the whole arrangement cheap: **the rules are small
enough to play a whole remaining game inside one actor wake**. That is what lets a deserted table play itself out in
microseconds rather than sitting through eight bot turns of real time
([`match.md`](match.md#a-table-that-loses-everyone-is-played-out-not-abandoned)).

### Two kinds of number, two different homes

That timings are host-supplied parameters is often heard as "the rules take no configuration", which is the wrong
reading and would put balance numbers in source. The line is between *pacing* and *content*:

> **Durations are host parameters. Rules numbers are game config.** How long a seat may think, how long a resolution is
> held on screen, how long a mulligan lasts — those are the host's. Den hit points, deck size, the hand and board caps,
> the mana ramp, the Tuckered Out schedule, every card, keyword, Weather and rank track — those are game config the
> model is handed and reads like any other content.

Neither is ever a constant in the rules' own source. A number in source is a number nobody can tune, and this sample
exists partly to show what game config is for.

The line has one crossing, and it is deliberate: **how much of its turn reserve a seat has already spent is game
state, not pacing.** The size of the bank is a host duration like every other; the gating rule — only a seat that has
already acted this turn may draw on it ([`match.md`](match.md#when-players-stop-playing)) — is a rule, and putting
the counter in the rules state is what stops a host from reimplementing that rule slightly differently. It is
replicated and hashed with everything else.

## The model, the actions and the host

There is one state. `MatchModel` carries it, the SDK replicates it, and every rule that changes it is a
`ModelAction<MatchModel>` the actor issues.

**The deal** is the one entry point that is not an action, because it runs before there is a timeline and before there
is a subscriber to send one to. The state a subscriber first receives is what the deal built. Its inputs are the whole
of what the host decides — the seed, the shared game config, the timing parameters, and the two decks already resolved
to cards-with-ranks.

**In:** ordered player intents — mulligan, play a card, attack, end the turn, answer a peek — addressed by match
instance identity, never by a hand index or a board slot; plus the host's own signals — a deadline expired, play out the
rest. The actor turns each of them into an action.

**Out:** the model itself, and an **ordered event stream** describing exactly how it got there. The events are the
rules' real product. They are appended to the model's own history, the client renders its board changes and its beats
from them, the bots read the tail of them, and the tests assert on them. A state diff would have carried the same facts
and none of the meaning; a card died is a different event from a card was destroyed by its own Goodbye, and the board
only ever shows the difference if somebody emitted it.

> **Every event is public-safe by construction.** An event never carries an identity that is not already public. The
> draw event says which seat drew and what the counts became; it does not say what was drawn. There is no filtering
> step and no richer internal log behind it, and therefore no future event that leaks by being forgotten — which is
> also what makes the history safe to replicate as part of the model.

A seat's own hand is not an event: it is a baseline at subscribe plus operations addressed to that seat alone
([`hidden-information.md`](hidden-information.md#delivering-a-hand)), which is why it may name cards an event never
may.

**The server prepares an action; everyone executes it.** An action a seat asked for is produced by
`MatchIntent.Prepare`, which validates it and hands back the action; one the host issues alone carries its own
`ServerPrepare`. Either way something checks before anything is staged, because the SDK's own `ExecuteAction` only
warns on a refused action, and because that verdict is also the refusal the client is told
([`protocol.md`](protocol.md#the-server-prepares-an-action-everyone-executes-it)). `Execute` does not re-ask: it runs
on the leader and on both followers, and a follower runs only actions the leader already checked. There is no
publisher, no separate step object and no rollback: the action either applied or was never issued.

## The rules run twice

Every rule action executes on the server, on the timeline, against the authoritative model — and again on both clients,
against a copy with no secrets in it. That is the SDK's guarantee and this game's central discipline, and it is the
rule a new rule is most likely to break:

> **No shared action takes server-only state as an input to a public mutation.** A public mutation is a function of the
> action's own payload and the public model only. Server-only state may drive server-only mutations only. Stated as a
> property: every action must execute identically on its public side against a model whose server-only members are
> default.

Three corollaries carry most of the weight in practice:

- **A derived public value must be stored.** A follower has no hand or deck list, so the counts are members every
  zone move maintains.
- **A hidden identity becomes public only through a payload.** The server resolves "which card is this" from the
  secret *before* the action exists, and the action carries the answer. A follower re-runs the same mutation from the
  same payload.
- **Secret reads live in one place.** Every read or write of the secret goes through `SecretOps`, whose every method
  resolves the accessor and returns harmlessly when it is null. A follower calls the same methods and they do nothing.

The discipline is enforced rather than reviewed: the follower-equality suite replays **every action of every self-play
game** against a network-masked clone and compares the SDK's checksum bytes and the events after each one, with
deliberately broken actions as negative controls ([`bots.md`](bots.md#follower-offline-dual-execution)).

## Zones

Each seat has four zones and one derivation. Each of the two secret ones has a public half and a secret half, and the
halves are named separately on purpose.

- **Deck** — ordered and secret. Order is the only thing about a deck that is secret. The order is a list under the
  secret member; the size is a public counter.
- **Hand** — contents secret, size public. Same shape: a list under the secret, a counter in the open.
- **Board** — public in full, capped, unordered in meaning. There is no positioning, so board order is a
  rendering concern and the rules treat it only as a stable, deterministic iteration order.
- **Graveyard** — public in full, in the order things arrived.
- **Unseen pool** — not a zone but a derivation: the multiset of that seat's cards that have not yet become public. It
  is the deck plus the hand minus everything already seen, it shrinks monotonically, and it is the public answer to
  "what is left in there". A card that becomes public — played, destroyed, revealed — leaves the pool permanently and
  does not re-enter it when a bounce puts it back in a hand. The reasoning, and the subtraction attack that forces this
  shape, is [`hidden-information.md`](hidden-information.md#public-deck-contents-and-the-subtraction-problem).

Every card in the match is a **match instance** with an identity minted once and never reused. Its public entry names
the seat that owns it and where it is; while it is in a deck or a hand and has not become public, *where it is* reads
`Unseen` and the entry names no card and no rank. Identities for the deal are minted in a canonical,
shuffle-independent order over the authored deck lists, before the shuffle; instances created during play take theirs
from a counter, which is safe because they are created by public events in a public order. An identity minted in draw
order would be the deck order in disguise, so this is a rule the code implements rather than a convention it follows.

One operation moves a card between zones, and it maintains the pool, respects the caps, emits the event, enqueues any
trigger and moves the card in the secret list if the move touched a deck or a hand. Nothing writes a zone directly —
not combat, not the effect interpreter, not an action's own body. The one exception is the deal itself, which fills the
zones before there is a game for the invariants to be about: it mints the instances and lays out the opening hands, and
everything from the first mulligan onward goes through the one operation.

## Determinism discipline

Determinism here means something stronger than "usually reproducible". It means a stated equality: **the same seed and
the same ordered intents produce the same final state, on any machine, in any host, in any process.** The disciplines
that hold it up are all negative, and all of them are the kind that a single careless line breaks silently:

- **One RNG stream, one order of draws.** There is exactly one seeded generator, its position is part of the secret
  state, and the order in which the deal consumes it is fixed by design rather than by whatever the code happened to do
  first. Anything else that wants randomness — a bot's seeded imperfection, a test's deck shuffle — gets its own
  generator from its own host. Nothing outside the rules ever draws from the rules' stream, and no effect can reach it
  at all.
- **No public value is a raw draw.** The Weather and the first-player decision are the only two draws that surface, and
  both destroy nearly all of what they consumed. The rule and the reasoning are in
  [`hidden-information.md`](hidden-information.md#the-seed-is-part-of-the-secret); the job here is to keep it true as
  effects are added.
- **No wall clock.** The rules never ask what time it is. Times arrive in an action's payload and are stored so the
  host can re-arm them; the rules never compare two of them.
- **No unordered iteration.** No hash-set or dictionary enumeration on any rules path, no ordering that depends on an
  object's hash code or its address. Multi-target effects, death sweeps and board scans all walk one canonical order.
- **No floating point.** Card stats are integers and stay integers. Where a proportion is ever needed the SDK's
  fixed-point types are the answer.

The property is asserted rather than assumed: replaying a recorded script against a fresh model must reproduce a hash
of the final state exactly, and the bulk determinism suite does it over thousands of seeds.

**Timing stamps are outside the rules hash.** The model has a rules half and a pacing half — the deadline stamps the
host wrote — and only the first is what determinism is about. The rules hash covers `Rules` and not
`Pacing`, which is what lets the same game be replayed at zero timings in a test and at real ones on a server and still
be called the same game. The SDK's own checksum covers both, and that is the second and stronger check: it is computed
on every host and a disagreement ends the session instead of drawing two boards. The one thing that looks like pacing
and is hashed anyway is the reserve spend, for the reason above: it is the outcome of a rule.

## The turn flow

The rules walk a game from deal to result, and it is a state machine with exactly one seat able to act at a time —
except for the one place where both can, which is the mulligan.

**The deal** happens in a fixed order because the same seed must reproduce the same game in every host: the Weather is
drawn, each deck is shuffled, the first player is decided, and the opening hands are dealt — three cards and four, per
[`game-design.md`](game-design.md). Fixing the order is not tidiness; a host that shuffled before drawing the Weather would
produce a different game from the same seed, and nothing would report it. The second seat's Acorn is not dealt: it is
granted when the mulligan ends, below, so both opening hands are starting-deck cards and nothing else.

**The mulligan** is the one simultaneous step. Both seats may replace any subset of the hand they were dealt; the
replaced cards go back, the deck is reshuffled and the same number is redrawn, in the action that submits them. Both
seats may submit, in either order, and each may submit once. The order they answer in decides which swap draws from
the stream first, which is harmless: the seed is secret, so neither player has a lever, and the timeline fixes the order
for every replay. A seat that never submits keeps what it was dealt, and the host says when that moment arrived. The
Weather was revealed before any of this, because mulliganing around it is the entire reason the Weather is public.

Once both seats have answered, or the deadline has lapsed, the phase ends and the second seat is granted its Acorn —
public from that moment and never in anybody's unseen pool, because it was never in a deck and so has no pool to leave.
Granting it here rather than at the deal is what keeps the mulligan's question honest: every card the step is asked
about is a card it could put back. Both ways the phase ends grant it, the lapsed deadline included, so going quiet does
not cost a seat its compensation.

**A turn** is a start-of-turn package the rules drive — ramp, refill, draw or Tuckered Out, wake — then a main phase of
many player actions in any order, then an explicit end. There is no implicit end and no priority window: the opponent
never acts inside somebody else's turn, which is what keeps this one state machine rather than two interleaved ones.

### Legality settles every race

An intent carries nothing that says which board its sender saw. It is judged against the state it arrives at, by the
same legality checks that would judge it at any other moment, and that alone settles every race the game has: a
double-tapped card is refused because it has left the hand, a second attack because the critter has attacked, an action
that lands after the turn passed because it is not that seat's turn, a second mulligan because the seat has answered.
There is no staleness check to keep consistent with the legality checks, and so no second notion of "where the game is"
to disagree with the first.

**The peek's answer is the one exception.** It names indices within one reveal, and indices mean nothing across two, so
every held choice is stamped with its own id and the answer echoes it. An answer to an earlier reveal is refused rather
than applied to the one that replaced it.

The model also keeps a plain count of accepted seat actions. No rule reads it and no intent carries it; it is there for
the hosts, which need to know whether anything has happened since — a bot's delayed decision, the board's presented
step — and it moves inside the same action as everything else, so every copy of the model agrees on it.

### Deadlines are data, not decisions

The model holds a deadline because the host wrote one. It evaluates it never. The host says *the deadline expired*, and
the rules do the consequence — play the rest of the turn out.

This is what makes the whole timing budget in [`match.md`](match.md#timing-budget) a set of knobs rather than a set of
behaviours. It is also what makes the tests honest: a suite that forced time forward by sleeping would be testing the
test runner, and one that reached inside to flip a flag would not be testing the rules at all.

## Combat

Combat is the smallest part of the rules and the one with the most edge cases, because everything simultaneous has two
of everything.

An awake critter may attack once on its owner's turn, into an enemy critter or into the enemy Den. The Den is reachable
only when the enemy has no Guard the attacker could legally have attacked instead — Guard protects the Den, never the
team, so every critter on the board is a legal target unless something else says otherwise. The exchange is
simultaneous: both critters deal damage equal to their attack, both take it, and only then does anything die. The Den
never answers back.

Order matters in exactly three places, and all three are fixed rather than emergent: damage is applied to both
participants before any death is checked; deaths are then collected in one pass in a canonical order with the active
seat's critters first; and each death's Goodbye enters the effect queue in that same order. Anything else makes "who
died first" depend on which local variable the implementation happened to write to first, which is a determinism bug
that only shows up once a Goodbye starts mattering.

The keyword interactions that fall out of simultaneity — a dying attacker whose damage still landed and still healed, a
Bubble that pops without absorbing a second hit, a Sneaky critter that stops being sneaky the instant it connects — are
consequences of that ordering rather than special cases in it. Each is enumerated with worked numbers in the engine
suites, because each one is a test.

## Keywords, Weathers and rank tracks are all modifiers

Three features that look unrelated in [`game-design.md`](game-design.md) are one mechanism here, which is why the card pool
can grow to a hundred cards without the rules growing at all.

- **Keywords** are properties a critter has, whether printed on the card, granted by an effect, or granted by the
  Weather. The rules ask "does this critter have Guard" and never ask where it got it.
- **Rank tracks** are stat deltas applied when an instance is created, from the config track for that card and the rank
  its owner brought it at. A rank-5 critter is simply a critter with different numbers; nothing downstream of instance
  creation knows ranks exist. The numbers are public the moment the card is played, which is also what makes an absent
  winner's defaulted Heist pick predictable rather than secret.
- **Weathers** are the same vocabulary in two halves. The half that triggers — a critter died, so heal its owner's Den —
  is an effect like any other and goes through the interpreter. The half that modifies — this trick costs one less, all
  critters have Bubble, the ramp gives one more — is a small set of queries the rules ask while computing costs,
  legality, ramp and keywords. Both halves are config; neither is a branch on a Weather's identity.

The test of whether this decomposition is right is that adding a Weather or a keyword to the config pool should require
no code change. Where a new one would, that is the signal that the modifier surface is missing a query rather than
that the Weather needs a special case.

## Tuckered Out is the clock

Uncapped mana has no natural end, so the game needs an external one, and [`game-design.md`](game-design.md) makes it running
out of cards: drawing from an empty deck deals escalating damage to your own Den instead. It is a per-seat counter that
ticks on every draw it cannot satisfy, not only the turn draw — a draw effect against an empty deck is the same event.
It is public information, which the unseen pool makes unavoidable anyway.

**The clock has a hole, and it is the hand cap.** A drawn card that overflows a full hand goes to the bottom of the deck
rather than being burned, so the deck never empties and Tuckered Out never begins. Two players content to hold a full
hand and pass therefore play forever. The rules close it by treating a draw the hand refused exactly like a draw the
deck refused: the card still goes to the bottom of the deck, and the seat still takes its tick. The rule the design was
reaching for is that a draw you cannot take costs you, and a full hand is a way of not taking one.

## Winning, losing and the draw

A Den at zero ends the game, and it is checked once per resolution rather than the instant a number changes. That is a
real ruling and not an implementation detail: it means a Den taken to zero and healed back inside the same resolution
survives, and it means both Dens reaching zero in one resolution is a single, well-defined, symmetric event — a draw,
with no winner and therefore no Heist.

Deaths are checked more eagerly than that, between items in the queue, because combat cannot resolve otherwise and a
Goodbye has to fire before the next thing reads the board. The asymmetry is deliberate: a critter's death is a fact
other rules depend on mid-resolution, and the end of the game is not.

The rules produce the **result** — who won, who lost, or that nobody did — together with the list of cards each seat
played from hand this match. What that list is worth is not their business: locks, stakes tiers, the pick and the
transfer are all the host's, and all of them were decided before the first card was dealt
([`match.md`](match.md#the-heist-phase)). The terminal state is "the game is over and here is what happened".

## The effect interpreter is a component behind a boundary

Card behaviour is config data, so the rules cannot contain it. What they contain is the **queue**, the **resolution
points**, and the **mutation surface** — the three things that have to be the same for every effect ever authored. The
vocabulary of effects, their triggers, targets and amounts, and what each one means, are
[`effects.md`](effects.md)'s. The two docs meet exactly here, and this section is the rules' half of the contract.

**What the rules guarantee the interpreter:**

- **Exclusive execution.** Resolution runs with the table accepting nothing from anybody. There are no priority windows
  and no reactions, so an effect never has to consider being interrupted. The one exception is
  [`effects.md`](effects.md#the-one-interactive-resolution)'s owner's-choice pause: the queue holds for a directed
  intent from the acting seat — or the deterministic default when the host says the wait is over — and nobody else acts
  meanwhile, so exclusivity survives the pause.
- **Defined resolution points.** The queue is drained to empty at each of them and never left partly drained (held, not
  abandoned, across the owner's-choice pause): after a card is played, after an attack's damage exchange, inside the
  start-of-turn package once mana and the draw are done, at end of turn, and after any death the previous item caused.
- **A stable view of a dead source.** A Goodbye reads its critter as it was at the moment it died, so an effect does not
  have to defend against its own source having been swept off the board.
- **State-based cleanup between items.** Deaths and their Goodbyes are taken in canonical order between queue items.
  The interpreter never has to check whether the thing it just damaged should now be in a graveyard.
- **Every invariant, on every mutation.** The caps, the unseen pool, the event, the instance bookkeeping, the secret
  lists and the follow-on triggers are all maintained by the one operation, identically no matter which effect asked.

**What the rules require of the interpreter:**

- **Purity.** No clock, no I/O, no generator of its own — and no access to the seeded stream. There is no `Random` on
  the effect context to reach for: a follower re-executing the action has no stream, so an effect that drew from one
  would produce a different outcome on a client than on the server. An effect that genuinely needs a random choice must
  therefore have the *outcome* chosen by the host and carried in the action's payload, which is the same rule that
  makes a card's identity travel.
- **Mutation only through the one operation.** No writes to state the interpreter was handed to read. This is the clause
  the whole boundary rests on: an interpreter that could write a zone directly would be able to break the pool, the
  caps, the counts and the event stream all at once, and the leak would be invisible.
- **Deterministic expansion.** An effect that hits several things resolves them in the canonical order it is offered,
  never in whatever order a lookup produced.
- **Termination.** New triggers go to the tail of the queue and the drain must reach the end. The drain is capped and
  hitting the cap is a failure rather than a policy, and self-play asserts it never happens.

The one thing the boundary deliberately does not fix is the shape of an effect. The rules name an interpreter, a
read-only context and a mutation surface; what an effect *is* — its fields, its trigger vocabulary, how a target is
described, how an amount is computed — belongs to [`effects.md`](effects.md) and can change without the rules
noticing.

## Where game-design.md is silent

A rules layer cannot be silent: every gap in the game design becomes a line of code that decides something. Those gaps
— whether a Sneaky Guard gates the Den, whether a dying attacker's damage counts, what a summon does to a full board,
whether a full hand stalls the clock — are resolved in [`engine-rulings.md`](engine-rulings.md), each with the test
that pins it. The rule about the rules: **a silence is resolved once, in a named test, and never twice in two call
sites.**

## What the rules do not know

Stating the exclusions is half of what keeps the rules pure. Nothing in them references an entity, a session or a
socket, and nothing persists. A seat is 0 or 1 — no player id, no connection flag, no bot mark — so the rules cannot
tell a human from a bot, which is exactly what makes a play-out produce a real result. Concede, disconnect, grace and
strikes are occupancy and belong to the actor; what reaches the rules is at most "this deadline expired" and "play the
rest out". The wager is not theirs either: they apply a rank track to a stat and report the cards each seat played.
And they know nothing of the UI — no animation lengths, no board slots that mean anything.

## Testing

The rules are the most testable thing in the project. Every rule in [`game-design.md`](game-design.md) and every ruling in
[`engine-rulings.md`](engine-rulings.md) has a named unit test, findable from the rule. Determinism is a suite rather
than an assertion: thousands of seeds, each replayed through independently constructed models and compared on a hash
of the final rules state. Follower equality asks the other question — whether a host and a *client* agree — and bulk
self-play doubles as the effect system's fuzzer, the secrecy proof and the balance harness
([`bots.md`](bots.md#self-play)). Timings are zeroed in every unit test — never raced, never slept through.

## See also

- [`game-design.md`](game-design.md) — the rules this implements.
- [`match.md`](match.md) — the host: seats, timers, the Heist phase.
- [`engine-rulings.md`](engine-rulings.md) — every place game-design.md was silent, what was decided, and the test
  that pins it.
- [`effects.md`](effects.md) — the effect data schema and resolution semantics behind the boundary above.
- [`hidden-information.md`](hidden-information.md) — the secrecy rules the zones, the seed and the events obey.
- [`bots.md`](bots.md) — the seat view, and the self-play harness: invariants, the secrecy proof, follower equality.
- [`protocol.md`](protocol.md) — the two serialization masks, host-supplied timings, and server-side validation.
