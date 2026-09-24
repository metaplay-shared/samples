# Bots and Self-Play

What plays a seat with no human behind it, why it is always labelled as such, and the harness that plays the game
against itself by the thousand to keep the rules, the secrecy model and the bot honest.

## What a bot is here

A bot is **a seat the match actor plays itself**. It is not a client, not an account, and not a separate process: when
play comes to rest on a bot seat, the actor asks a policy for an action and submits it through the same validation path
a human's action takes. To the rules, a bot seat and a human seat are identical.

One mechanism covers every reason a seat has no human acting on it:

- the opponent the [matchmaker](matchmaking.md) seats when no compatible human arrived before the fill wait, at
  practice stakes;
- the opponent in **Practice**, which skips the queue entirely;
- a seat whose human is present but let the turn deadline lapse — **auto-play**;
- a seat whose human disconnected or conceded and whose grace lapsed — **cover**, reclaimable by returning
  ([`match.md`](match.md));
- both seats, when a match that lost its humans is played out to a real result;
- the winner's Heist pick, when the winner is gone;
- every seat, in the self-play tests.

There is no "bot mode". A match is a match; some of its seats have people at them.

**Play-out is a rule of this game, not a convenience** ([`game-design.md`](game-design.md)): a conceded or deserted seat
is covered and the match runs to a real result, so leaving costs exactly what staying would. That promise is only as
good as the thing playing the abandoned seat, which makes the bot load-bearing for the economy. The Heist has exactly
one decision in it — the winner's pick — and when nobody is there to take it, the same policy takes it from the same
seat view. The loser has nothing to decide after the last turn: what they were willing to risk was settled by their
locks at enqueue.

> Not to be confused with `Backend/BotClient`, this sample's load client, built on the SDK's bot-client framework. It
> drives real sessions against the server to measure it, so **its seats are human seats**: a load bot logs in, queues
> with a real deck, is paired by the matchmaker and plays over the network, exercising the liveness check, the join
> window, the turn deadline and the rank-transfer delivery — none of which a seat the actor plays itself touches. It
> borrows only the action-choosing policy and the think delay from this doc.

## Labelled, not disguised

Bot seats are **marked as computer players**, with names from a reserved roster that human display names cannot match
in either direction, and the names read as machines on purpose. A human's seat under cover keeps **the human's name
plus the mark** — in a two-seat game that is the most common bot case there is.

A disguised bot that plays a strange card, or plays instantly, reads as a person behaving oddly, and once a player
suspects one seat is fake they suspect all of them. A labelled bot that plays well and takes a moment to think is
simply an opponent. What matters for the feel of a match is **pacing and competence**, not concealment.

In this game there is a second reason: **the stakes depend on who is in the seat.** Only a human opponent puts ranks at
risk; the fallback bot plays at practice stakes, where rating moves and ranks never do. A disguised bot would be a lie
about the wager, and the pre-match panel carries the label and the stakes tier together, before the mulligan.

Cover mid-match is the mirror image. **The stakes were fixed at formation and do not move when a bot takes over.** The
mark tells the surviving player what they are now playing against; the unchanged tier tells them the wager did not
shrink.

## Deciding an action

A bot decides from **its own seat's view**: its own hand, and everything the rules call public — both boards, both
Dens, both graveyards, both unseen pools, the Weather, mana and the action history. The view type **names the public
members and nothing else**, and a reflection test enumerates its members and fails on one that is not on the public
list ([`hidden-information.md`](hidden-information.md)). That is weaker than "does not compile" and it is what is
true: the view is assembled from the model, so what keeps the opponent's hand out of it is the list plus the test.

A turn is a sequence — play a critter, cast a trick, attack, then end — so the policy is asked for **one action at a
time** from the legal set until it chooses to end the turn. That is the shape a human's turn has, so auto-play can step
into a half-finished turn without a special case.

The policy is a **greedy heuristic, not a search**: it exists to make a legal, plausible move. In order of weight it
checks for lethal first, spends the turn's mana on the best curve, takes favourable trades or goes face depending on
who is racing whom, weighs clearing a Guard against the trade it costs, aims damage at the largest thing it kills and
healing at what is about to die, and mulligans on a keep-cheap curve rule. The Weather is a modifier over all of that
rather than a domain of its own, because its effects already show up in the legal set. A CCG's branching factor is far
past what fits in a think delay, and at this pool size the improvements that matter are heuristic knowledge rather than
depth.

Two properties are required, and self-play asserts both:

- **A bot never takes an illegal action.** The policy chooses from the legal set the shared rules produce and never
  constructs an action of its own.
- **A decision is a pure function of (seat view, seed).** The same state and seed give the same action. This makes
  bot play testable, makes a *delayed* decision safe to discard and re-derive, and is what the indistinguishability
  proof below is built on.

## Personalities

A strength **profile** is drawn per bot seat when the match forms, so a player who meets the fallback bot three
evenings running does not meet the identical opponent three times. Imperfection is **seeded and defensible**: a weaker
profile picks among the plausible candidates rather than always the top-ranked one, and its mistakes are the kind a
person makes — going face when the trade was better, over-committing into a board clear — never a random discard. Even
the weakest profile never makes a move with no plausible reading behind it and never throws a won game.

| Profile | Used by |
|---|---|
| **Strongest** | Auto-play, cover, play-out and the Heist default, always; offered in Practice |
| **Practiced**, **Casual**, **Sloppy** | Practice, as the difficulty choice on Home; the fill-wait bot, drawn from the waiting player's rating |
| **Strictly deterministic** | Pinned-decision tests |
| **Pure-random-legal** | Self-play fuzzing |

**Auto-play and cover always use the strongest profile.** A seat played *on behalf of an absent human* is a service to
that human, and playing a deliberate mistake with their cards — their collection is on the table — is indefensible. The
same rule governs the Heist default: an absent winner's pick takes the highest-ranked eligible card, ties broken by
rarity, mana cost and a stable card order, which is a function of public state the other player could have predicted.

The ranked fallback draws its profile from the waiting player's rating, and the player does not choose it: a knob on
the thing that pays out is the one affordance a ranked flow must not have.

## Pacing

A bot's action is chosen in microseconds and must not arrive that way. Every bot action is held to a **think delay**
drawn per action — short for an obvious one, longer for a real decision — and delays are host-supplied parameters,
zeroed in tests. A CCG turn holds several actions, so the draw is a **per-turn budget** with per-action delays inside
it, and chained actions that are really one decision are paced as one.

- **A covered seat is paced like any other bot seat.** Its owner never races it: their press asks for the seat back and
  the bot hands it over at the next turn boundary
  ([`match.md`](match.md#a-covered-seat-is-handed-back-between-turns)). A strike is recorded only against a seat
  still occupied by a human, so a covered seat's lapse never costs its owner a strike.
- **A delayed action is re-derived if the table moved while it waited.** It is armed with the action count it was
  decided at, and a table whose count has moved — or a seat that is no longer bot-driven — is decided afresh rather
  than handed a stale answer. An intent that still loses a race is refused by legality, like every other
  ([`rules.md`](rules.md#legality-settles-every-race)).
- **A played-out match has no delays at all.** When a match finishes itself after losing its humans, the remaining
  turns resolve in one call — nobody is watching.

## Self-play

The rules are pure shared code, so bots can play the whole game with no server, no client and no network. The
harness in `Backend/SharedCode.Tests/SelfPlay/` does exactly that, as an ordinary unit-test project, and it buys three
things: a fuzzer for the effect system, a proof of the secrecy model, and a data source for balance. It drives **the
same policy code the match actor calls**, not a stand-in, as one more host of the rules beside the server and the unit
tests.

Every game is driven from **one seed**, which produces the deal, every profile's seeded imperfection and every
tie-break, so one integer reproduces a failing game bit for bit. A run derives its per-game seeds from a checked-in
master seed: the harness needs reproducibility, not secrecy.

**The game count scales with what is asking.** The default run, wired into CI, plays a count sized to a wall-clock
budget. `STICKYPAWS_DETERMINISM_DEEP=1` scales every deep-capable suite at once — the determinism replay, the per-step
invariant walk, the pacing check and all of self-play (20,000 games) — and is run deliberately rather than on every
commit.

The pure-random-legal profile is the sharpest instrument for the rules: every legality hole and effect interaction a
sensible heuristic politely never walks into, a random player walks into by lunchtime. Running it in bulk means the
engine meets adversarial, legal-but-arbitrary play in a process where it is cheap to debug.

### The invariant catalog

Asserted after every accepted action of every game, so a violation is caught where it happened:

- **State sanity.** Zone bounds hold — hand at most nine, board at most six a side, Den HP and mana never negative,
  mana never above the turn's maximum — and no two live instances share an identity.
- **Conservation.** The state agrees with its own event stream: every mana movement, point of damage and heal is
  reconstructed from the events and compared with what the state holds, and every zone a card moves between is announced
  by something in that step. **Every stored public count agrees with the secret list it mirrors** — hand size, deck size
  and a held peek's revealed count are members the rules maintain, so a count that drifts is a follower drawing a hand
  size the server does not have.
- **Legality.** The action taken is always in the legal set, and the same seat view and seed always produce the same
  action.
- **Termination.** Every game ends inside the ceiling Tuckered Out implies, the effect queue always drains, and the end
  state is one Den at zero, or both at once as a draw.

### The secrecy proof

[`hidden-information.md`](hidden-information.md) states an invariant with two clauses — nothing secret is sent, and
nothing secret is derivable from what is sent — and each needs its own kind of check. This section is the one
catalogue of those checks.

- **The seat view.** A reflection test pins the view's member list; self-play is its most demanding consumer, and each
  game is a fresh witness that a competitive policy is expressible against it.
- **The wire form.** The payload compared is `SerializeTagged` under the SDK's network mask — the bytes a subscriber
  actually receives. The mask is what *defines* what travels, so the positive case is close to a tautology; the
  negative controls carry the proof.
- **Delivery integrity.** A hand is a baseline plus a stream of addressed operations, and a stream can drift in ways a
  checksum cannot see: an operation dropped, applied twice or out of order leaves every public member identical and
  the client's hand wrong. The follower mirror holds seat 0's own hand, seeded from the baseline a subscriber would be
  handed, and after **every action of every game** asserts that replaying the operations reproduces the authority's
  hand card for card, in order.
- **Indistinguishability.** At a decision point, the harness builds a second authoritative state that differs only
  inside the equivalence class hidden-information.md defines — deck order and the opponent's deck/hand split mutated,
  everything the seat may see held fixed — and asserts that the two seat views are byte-identical and the policy's
  decision from each is the same. This catches what an identity scan cannot: a count in the wrong place, an ordering
  that should be a multiset, a leftover RNG position, an identity minted in draw order. Its limit: every public member
  is *stored*, so what it really compares is what the sweep re-derives — the pools, the delivered hand and the legal
  set. A stored member written from hidden state passes it, and the next check catches that.

#### Follower-offline dual execution

Every action of every game is executed a second time against a clone of the model round-tripped through the network
mask — a *follower*, with no secret and no stream — and after each action the SDK's own `ComputeChecksum` bytes and the
emitted events are compared, naming the member that diverged. This is a **replication equality** check rather than a
secrecy one: it makes "no public mutation reads a secret" a measured property on every game at every action, and it
catches a stored public member written from hidden state on the first action that writes it. It costs about a tenth of
the deep run, so it is always on.

Several actions never occur at zero timings — nothing is armed, so nothing lapses — so every invariant suite also runs
a smaller **paced** batch at shipped durations, and the deadline-driven and table actions get mirrored fixtures of their
own. The mirror records every action type it executed, and a test requires the whole action block to appear in that
set, so a new action cannot join the registry without a mirrored path.

#### Negative controls

"Nothing leaked" is also what a check that walked nothing would report, so every check is paired with deliberately
broken variants that must fail, and a control that passes is a build failure:

- **Five wire-mask plants:** the opponent's hand in the projection, an identity minted in draw order, the opponent's
  deck published as its own list, a count computed over hidden state, and a leftover RNG position.
- **Five follower plants**, each a broken *action*: one reads a hand to compute a public number, one draws from the
  seeded stream, one emits an extra event, one stops maintaining a stored count, and one carries a duration it never
  put in its payload. Each runs at zero and at real timings.

### Profiles, and the one pinned win rate

Every profile is covered by the legality and termination invariants. A weaker profile's mistakes are a deterministic
function of (seat view, seed), so a replay makes the same "mistake" in the same place and a heuristic change shows up
as a diff. **Pinned decisions** — the lethal on board, the free favourable trade, the Guard that has to be cleared first
— are unit tests against the strictly deterministic profile.

**The one win rate that is pinned is about decks, not bots.** The starter-deck matrix (`StarterDeckBalanceTests`)
holds the player constant — the strongest profile on both seats, which reads no seed and plays the same game as the
strictly deterministic one — and varies only the deck and the deal. It plays each pair from both seat orders on the
same seeds, so the first-player advantage cancels within the cell, and pins only that no deck is hopeless.

**There is deliberately no bot-strength arena.** Playing a candidate profile against the current one and comparing win
rates is a trap at this scale: a bare win rate is unresolvable without a confidence interval, and an unrotated schedule
reports the first-player advantage — which The Acorn exists to offset — as a strength difference. What self-play does
check is blunter: that the strongest profile does not lose to a badly degraded one, first player rotated, at a rate
that would only make sense if a profile were wired backwards. Strength is tuned by playing the game.

### Balance-harness hooks

The balance harness is not an arena: it holds **one fixed profile on both seats** and asks about *cards* — per-card
win-rate deltas, the clan matchup table, match length against the five-to-ten-minute target. The per-game record
self-play produces already carries which cards each side played, which clans each deck drew from, how the game ended
and how many turns it took, and a suite can name the seats' decks instead of taking the rotation (the starter-deck
matrix is the first consumer). Aggregating those records into per-card win rates and a matchup table is
[follow-up work](../README.md#follow-up-work).

## See also

- [`match.md`](match.md) — the actor that drives bot seats, arms their delays, and owns cover and reclaim.
- [`hidden-information.md`](hidden-information.md) — the seat view, and the secrecy model the proof checks.
- [`rules.md`](rules.md) — the pure rules the harness runs, and the discipline the follower check measures.
- [`matchmaking.md`](matchmaking.md) — when a bot is seated in the first place, and at what stakes.
- [`game-design.md`](game-design.md) — the Heist, play-out, and Tuckered Out's termination ceiling.
