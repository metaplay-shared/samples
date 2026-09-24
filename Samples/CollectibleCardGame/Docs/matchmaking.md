# Matchmaking

How tapping **Play** becomes a seat in a stakes-bearing 1v1 match, within a minute, every time.

## The requirement

Ranked matches carry real stakes. The winner takes a rank off a card the loser played, and the size of that transfer is
set by the gap between the two decks' Power Scores ([`game-design.md`](game-design.md)). So the queue is not only deciding
*when* to stop waiting, as a lobby-free queue for a stakeless game would be — it is deciding *who is a fair person to
put a wager against*. Matchmaking here is a **band-matching** problem wrapped around a timing problem.

> **Pair two waiting players whose rating gap and whose deck Power Score gap both fall inside bands that widen the
> longer they have waited. If no compatible human has arrived by the fill wait, seat a labelled bot at practice stakes.
> The queue always produces a game.**

The two bands do different jobs, and the division of labour is the one from the game design: **rating gaps are
matchmaking's problem, Power Score gaps are the stakes' problem.** Rating pairing makes the game worth playing. Power
Score pairing is what makes the economy hold — it is the structural answer to both rich-get-richer (pumped decks meet
pumped decks) and sandbagging (a deliberately light deck is paired with light decks, so the underdog tier rarely
triggers by choice). Neither is a preference the matchmaker can trade away for throughput without an economic
consequence.

A match runs five to ten minutes, so a wait measured in tens of seconds is a small fraction of the loop — far smaller
than it would be in a game of forty-five-second rounds. That buys the bands time to work. It does not buy unlimited
time: the player is standing at a menu with nothing to do, and a player who waits long enough to wonder whether anything
is happening is a player who taps away.

## Modes, and what the queue owes each

| Mode | Pairing | Stakes |
|---|---|---|
| **Ranked** | Matchmaker only | Full Heist against a human; practice stakes against the fallback bot |
| **Practice** | No queue — a bot match is minted directly | None; the deck plays at its real ranks |

**Ranked pairing is matchmaker-only.** This is the win-trading defence, and it is a hard rule rather than a default: any
pairing route a player can steer is a route to hand a chosen account a rank. A friendly direct challenge could only be
added with no stakes and flat ranks, allowed *because* nothing would ride on it. Practice does not touch the queue at
all; it mints a bot match on the spot, and appears here only because the seat it produces is the same kind of seat the
fallback produces ([`bots.md`](bots.md)).

## The band policy

The decision — *given the queue and an elapsed time, form now or keep waiting, and with whom* — is a **pure function**.
It takes a snapshot of who is waiting with what rating, what Power Score and what arrival stamp, plus the current
instant, and returns one of: pair these two, seat this one against a bot, or wait. It reads no clock of its own, touches
no actor, and is unit-tested with neither.

Two waiters are **compatible** when both gaps fall inside the applicable band:

- the **rating gap**, which controls whether the game is worth playing;
- the **Power Score gap**, which controls what the Heist will pay out.

The bands widen with wait time:

| Waited | Rating band (±) | Power Score band (±) |
|---|---|---|
| 0–10 s | 100 | 10 |
| 10–25 s | 250 | 25 |
| 25–45 s | unbounded | 40 |
| At the fill wait (45 s) | — | bot fallback |

Three things about that table are deliberate rather than arbitrary:

- **The narrowest Power Score band sits inside the even-stakes threshold.** The game design calls a matchup even when
  the Power Score gap is within ±15, so the opening band of ±10 guarantees that a fast pairing is always a plain,
  symmetric Heist. Tiered stakes appear only after the player has waited for them.
- **The rating band opens all the way; the Power Score band never does.** A rating mismatch produces an unpleasant game
  and nothing worse. A Power Score mismatch produces a *distorted wager* — the favourite wins nothing, the underdog wins
  double — and a matchmaker that manufactured those pairings routinely would be building the sandbagging route it exists
  to close. Past the widest Power Score band, a bot is a better opponent than a human, and that is what the fill wait
  hands out.
- **Bands are steps, not a continuous ramp.** A step schedule is exactly as expressive at this scale, is trivial to
  unit-test at its boundaries, and gives the queue's timer a small set of instants at which the answer can change (see
  [The queue service](#the-queue-service)).

When two waiters have waited for different lengths of time, they have different bands. The pair is judged by the **more
tolerant** of the two — the longer waiter's. Requiring both bands to hold would mean a fresh arrival can never be paired
outside its opening band, which starves precisely the person the widening exists for: the long waiter would watch
compatible-to-them arrivals pass through the queue untouched. The cost is that a player who just tapped Play is
sometimes handed a wider matchup than their own band would have allowed. That is the right trade — they are being
offered a game immediately, and the pre-match screen shows them the Power Scores and the stakes tier before anything is
at risk.

Selection scans oldest-first, so the longest waiter gets first refusal, and among that waiter's compatible partners
takes the **closest** rather than the first found. Burning a near-perfect pairing on a marginal one is cheap to avoid at
this queue size, and the quadratic scan it implies is not a cost worth thinking about below numbers this sample will
never see.

## The fill wait and the bot fallback

At 45 seconds a waiter has passed every band the schedule has. Waiting longer buys a diminishing chance of a human and a
rising chance of a player who quits, so at that point the queue stops waiting and forms a match against a **labelled bot
at practice stakes**: rating moves, ranks never do.

That single decision closes a whole class of problems by construction:

- **The queue always produces a game.** A ranked tap has a bounded, honest worst case, which is what lets there be no
  lobby and no population display.
- **Bot-farming ranks is impossible.** Not discouraged, not rate-limited — impossible, because a bot has no collection
  to take a rank from and none is minted. Real stakes require a human on the other side.
- **The wager is never a surprise.** The pre-match screen carries the bot label and the stakes tier together, before the
  mulligan.

The fallback bot brings one of the config-authored starter decks, chosen off the human's own clan pair where the pool
allows, at a uniform rank that puts its Power Score near the waiter's — so the game is a real game rather than a
formality or a mirror. The human plays their own deck at its real ranks — their rating moves, so the match must be
their actual match; only the Heist is switched off.

## The queue service

A single always-running service entity holds the queue, in memory only. That is acceptable because a waiting player is
holding a live connection anyway: there is nothing in a queue entry worth surviving a restart, and re-entering costs one
tap. What the choice costs is paid on the player's side rather than the matchmaker's — see [Nothing waits
forever](#nothing-waits-forever). Those bounds are the price of the in-memory queue, and without them the stated cost is
not the cost actually paid.

1. The client asks to play. The request goes to the **player's own actor**, which validates the chosen deck (25
   singleton cards, at most 2 clans), computes its Power Score with shared code, and enters the player into the queue
   carrying rating, Power Score and an arrival stamp.
2. The matchmaker appends the entry and evaluates the policy. If it forms, it forms at once. Otherwise it **arms a timer
   for the next instant at which the answer could change**.
3. On a formation, the matchmaker takes the two entries (or the one entry and a bot), decides who goes first, and
   creates a [match](match.md).
4. It tells each seated player's actor which match they are in. Each actor associates the match onto the client's match
   slot; the session attaches and the client subscribes.

The deck is frozen at step 1, and **so is its lock set**. Three versions of one hole close together here: a Power Score
the client asserted would make every band and every stakes tier a client-side claim; a deck chosen after pairing would
let a player queue light and play heavy; and a padlock toggled after pairing would let a player quietly withdraw a card
from the pool the pre-match screen had already shown as at risk. The fix for all three is the same — the deck the queue
matched on, at the ranks and behind the padlocks it carried when it was matched, is the deck the match is dealt from.
That is what makes the pre-match stakes display a statement rather than an estimate.

**The newcomer shield is frozen with them, and can be waived per account.** The shield is decided at formation from each
seat's own ranked-match count, and a shield on either side shields the match — so two fresh accounts cannot see the
even or asymmetric tiers until both have played their way out of it. A LiveOps Dashboard control waives one account's
*own* shield without forging its count, which the tier and the Heist read; seating two humans at an unshielded table
means waiving both. It is a **live** lever gated by the dashboard permission alone — the SDK's development-only marking
applies to actions from a *client*, and the dashboard enqueues a server action directly — so it is placed as
disruptive: its next ranked match moves rating and ranks for real. The dashboard card beside it shows the ranked-match
count against the threshold first, because once the count has passed it the waiver decides nothing.

### The timer is armed from the oldest waiter — and re-armed

The timer is armed from the **oldest waiter**, not reset by later arrivals, so a player joining a queue thirty seconds
old inherits the remaining fifteen. It is re-armed **whenever the queue is non-empty and no timer is running** —
including immediately after a formation that left a remainder. Arming only when the queue goes from empty to non-empty
is the obvious rule and it is wrong: a third player arriving as two form would sit in the queue with no timer at all,
waiting for an arrival that may never come, which is precisely the never-filling queue this design exists to prevent.

The band schedule makes the timer do one more job than a pure fill timer would. Because compatibility changes with
elapsed time, a queue that goes completely quiet still needs to wake up and re-evaluate: two waiters who were
incompatible on arrival become compatible at a step boundary with nothing having happened in between. So the timer is
armed for the **earlier of** the next band step for any waiter and the earliest fill deadline. A fill-only timer would
leave two perfectly pairable players sitting in the same queue until one of them timed out into a bot.

The queue holds no invariant about its own size. Concurrent arrivals and arrivals during formation both break "at most
one waiter", and none of the rules above depend on it.

### Formation is atomic, and validated

Formation removes its waiters from the queue **before** anything else, so a cancel arriving mid-formation is a no-op
against an entry that is already gone rather than a race that produces a ghost seat.

The roster is then re-validated by **asking each waiter's own actor to commit to a seat**. An actor that has since
cancelled, is already in a match, or has nobody on the other end of its connection declines. The asks go out
**together**, not one after another: serially, one unresponsive player would hold the whole singleton — every other
player's tap, cancel and timer — for a full ask timeout.

With two seats, a decline does not merely thin the roster, it **dissolves the pairing**, so the surviving committer has
to be handled explicitly rather than incidentally:

- If the survivor **answered**, its reservation is released and it is re-queued **with its original arrival stamp**. It
  answered, so it is demonstrably responsive, and it keeps the band it had earned; it loses a fraction of a second. If
  it had already passed the fill wait, it does not go back to waiting — it takes the bot fallback immediately.
- If the survivor's **answer was lost** — the reservation commits when the actor answers, not when the answer lands — it
  is told the seat is gone and is **not** re-queued. An actor that could not answer once would be re-taken by the next
  formation and stall that one too. It pays one tap; everyone else pays nothing.

The match entity is minted **before** the assignment messages go out, and only a successful mint commits the formation.
A mint that fails releases both committed seats and leaves the waiters queued with the stamps they arrived with, rather
than leaving two players staring at a searching screen forever.

**Liveness means a live connection, not a live session.** A session outlives the connection that opened it by the
session linger, roughly a minute — comparable to the entire window a search can occupy — so "does this player still have
a session" answers yes for someone who closed their tab a second after tapping Play. It is the wrong question, and here
the seat it hands out is worse than a missing seat. In a 1v1 game a dead human is *the whole opposition*: cover puts a
bot in that seat and the match plays out fine, but it plays out as a bot game that both the pre-match screen and the
Heist were told was a human game with real ranks on it. The liveness check is protecting the wager, not the ambience.

#### A cancel and a formation cannot both win

The queue removal above closes the window but does not decide the race, because the cancel and the formation reach two
different entities. What decides it is that **the player's own actor is the single arbiter, and the seat reservation is
its commit point.**

> A cancel that reaches the actor before the reservation leaves it not searching, so it declines the seat and the player
> is cancelled. A cancel that arrives after is **refused**, and the player is seated.

Exactly one of the two, never both and never neither. The refusal is not a rough edge: the client keeps waiting rather
than being shown a menu it is about to be pulled off. The searching dialog therefore drops its Cancel the moment the
player has asked to leave, which is the closest a client can get — nothing tells it the seat committed, by design,
because there is no "you have been matched" message and a board arriving is what ends the wait. The guarantee is
server-side either way: a cancel that arrives after the commit is a harmless no-op whatever the button did. What keeps
the refusal from being a trap is that a player pulled into a match this way is never surprised by what it costs — the
pre-match screen states the wager before the mulligan, and there is nothing at stake until a card is played.

### Nothing waits forever

Both states a player can wait in live on the **player's own actor**, and both are bounded there, because both wait on
something held in another entity's memory:

- **Searching** waits on a queue entry. The matchmaker holding it can restart — a crash, or an ordinary rolling deploy
  that moves the singleton — and the queue goes with it, with nothing to rebuild it from and nobody to re-enqueue. The
  bound is what turns that into "each waiter taps Play again" instead of a room full of clients spinning on a queue they
  are not in, cancelling into the void.
- **Seated** waits on a match being minted. This is the one way a committed seat could otherwise strand a player, since
  there is genuinely nothing left to cancel.

Both land the player back on the menu with a way to try again. **The order of the bounds is the point:** each is longer
than the whole of the step below it, so a backstop cannot fire while the thing it backs is still running. An actor that
gave up on a search the matchmaker still went on to honour would tell a player the search failed and then pull them into
a match a moment later. The searching bound is therefore sized against the fill wait plus a formation allowance, not
against a flat few seconds.

The client carries one more bound of the same kind, for the case none of the server's can reach: the searching dialog
goes up optimistically on the tap, so a request lost on an already-dead connection would otherwise leave it up with a
Cancel that goes exactly where the request went.

### One entry per player

A player who is already queued, or already in a live match, is refused rather than queued again. This is not defensive
coding: a double-tapped Play, or a second browser tab (which logs in as the same player and force-terminates the older
session), reaches this path in ordinary use. Without the guard a player is seated twice in the same match, or in two
matches at once.

"Already in a live match" runs through the end of the Heist, not the end of the last turn. The winner's pick is a match
phase ([`match.md`](match.md)), and a player who could queue during it would either abandon a rank transfer or be
holding two matches while one of them mutates their collection.

### Leaving the queue

A player may cancel, and a player whose session ends is removed. If the removal empties the queue the running timer
stops mattering — it is re-armed on the next arrival by the rule above, so nothing needs cancelling.

### What the player sees

The wait is a **dialog over whatever the player tapped Play on** — Home, or a finished match under *Play again* — and
the only screen change a match start makes is the one that puts the board up ([`client.md`](client.md)). It says
which of the two waits is running and **counts down** the server's own bound, sent as the time remaining so no device
clock has to agree with the server's; the bound is an upper bound rather than a prediction, so the copy finishes early
gracefully and says "any moment now" rather than restarting. What the dialog says for a given status, and how the
countdown rounds, is a pure function with its own tests.

The dialog shows **no population count and no band state**. Both would be honest, and both would be mistakes: a count
invites the player to read a small number as a dead game, and a visibly widening band says "the match you are offered
is getting worse the longer you wait" to someone who cannot act on it. The honesty the player is owed is about the
wager, and that is paid in full one screen later.

The **pre-match panel** shows the Weather, both names, both Power Scores, the stakes tier and whether the opponent is a
computer seat, before the mulligan. It is a **panel over the mulligan rather than a screen to dismiss**, because the
mulligan's deadline is already running while a client cold-boots — a screen to acknowledge would spend the mulligan's
clock on something else. It takes no input, the hand underneath is live from the first frame, and it leaves on the
first mulligan interaction or a short timeout.

Two tiers come from who is in the seats rather than from the decks, and both are stated for **each seat**: **practice
stakes** against the fallback bot, and the **newcomer shield** while either player is inside their first ranked matches
— a shield on the other side changes what this player stands to win as much as their own changes what they stand to
lose. Locks are not a tier, but they bound the pool a tier applies to, and both lock sets froze at enqueue: what you
were willing to risk was declared before you knew the outcome.

### The client never picks an opponent

The client asks to play and is *told* where it was seated. There is no lobby, no table browsing, no opponent selection.
That keeps the surface to two messages each way, and it means a reconnecting player is re-seated by the same mechanism
that seated them: their account remembers the match and their actor re-associates it on session start.

## Why not the SDK's matchmaker

The SDK's async matchmaker pairs a player against a stored **snapshot** of another player — the right shape for
attack-a-defender PvP, where the defender is not present. This game needs two live players in the same live match at the
same moment, which the snapshot shape cannot express at all. There is a second mismatch worth naming: one of our two
band inputs is the Power Score of the deck the player queued *with*, a property of this session's choice rather than a
durable player attribute a snapshot index would carry. And the bot fallback is a queue-time decision about a wait, which
no index answers. So this queue is ours. It is also small.

## Why there is no lobby

A lobby makes the *player* responsible for the wait. Every table they can see is a table that has not started, and a
sparse service shows an empty list, which reads as a dead product even when nothing is wrong. The queue with a bot floor
inverts that: the player never sees the population, only the result, and the result is always a game.

In this game there is a second reason, and it is the stronger one. **A browsable lobby is a win-trading tool.** Two
accounts that can find each other on purpose can move ranks between themselves at will, and every anti-abuse measure in
the design assumes they cannot. No-lobby is an economic property here, not only a UX one — which is also why a friendly
mode, the one place two players would choose each other, would have to carry no stakes and flatten ranks.

The cost is that friends cannot deliberately play a ranked game together. That is a real loss and it is accepted: it is
the same loss as the win-trading defence, seen from the other side.

## Load

The queue is one actor forming at most one match per burst, and a five-to-ten minute match means a given player passes
through it far less often than in a game of short rounds. That ceiling is far above any load this sample sees. Sharding
a single-key queue is a later and easy change mechanically, but it is not a free one here: bands and thin pools interact
badly, and a queue split across shards widens everybody's bands to buy throughput nobody needed. Shard by nothing until
the numbers force it.

## Testing

**The policy is unit-tested with no actor and no server.** Form-or-wait, each band step and its boundaries, the
longer-waiter tolerance rule, the closest-partner selection, the fill wait producing a bot, and the whole schedule run
at a zero fill wait. Left for integration testing: that the formed roster reaches the right entities, and the release
paths around a dissolved pairing.

End to end, in `MatchmakingTests` (whose isolation rules are in the sample's `AGENTS.md`): one player alone is seated
against a labelled bot and the pre-match screen says practice stakes; two browsers tapping Play inside one fill wait are
seated against each other, see the same Weather, and see each other's Power Score and the stakes tier it implies; and a
player who taps Play and closes their tab has the other player seated against a bot instead, which is the liveness
re-check observed from outside — a check that asked only whether a session existed would seat the ghost and put real
ranks on a match with nobody on the other side.

There is deliberately **no end-to-end test of band separation**. Proving that a rating or Power Score gap keeps two
players apart needs two contrived accounts and a real wall-clock wait, and it would be a slow, flaky restatement of
something the pure policy already asserts exactly. The bands are unit-tested; end-to-end only proves that a decision
reaches the right seats.

## See also

- [`match.md`](match.md) — what is created, and its lifecycle from there.
- [`bots.md`](bots.md) — who takes the seat when nobody arrives.
- [`client.md`](client.md) — the searching dialog and the pre-match screen.
- [`protocol.md`](protocol.md) — the singleton service and minting mechanics.
- [`game-design.md`](game-design.md) — ranks, Power Score, and the stakes tiers the bands protect.
