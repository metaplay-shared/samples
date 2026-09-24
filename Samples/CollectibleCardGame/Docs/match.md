# The Match

The table that hosts one game of Sticky Paws — where the rules run, who is seated, what happens when a player drops or
stalls, and how the result and the Heist reach the two players' accounts.

A match is one **ephemeral multiplayer entity**: a server-authoritative actor that owns the game state and advances it
by issuing the actions that are the rules, which the SDK replicates to the two subscribed clients and both clients
re-execute. It is created by the [matchmaker](matchmaking.md) with its seats and its stakes already decided, plays one
game, resolves the Heist, and ends. **A match lives exactly as long as its actor**: it has no database row, and if the
process it is on goes away the match goes with it and both players are told so. The SDK mechanics are in
[`protocol.md`](protocol.md), the secrecy rules in [`hidden-information.md`](hidden-information.md), and the game
itself in [`game-design.md`](game-design.md). This doc is the table's own design.

**Exactly two seats.** There is no seat-rotation generality anywhere in the match: not in the model, not in the board
UI, not in the Heist. Every place that would have carried a seat count instead carries "you" and "them" — and a card
game whose stakes are a transaction between two collections gains nothing from pretending otherwise.

## Two layers, deliberately separated

The line is between the shared rules over a shared model and the actor that hosts them.

- **The rules over the model** (shared game code, no entity types) are the game: zones, the seeded shuffle and deal,
  mana, the effect system and its FIFO queue, combat, keywords, the Weather hooks, rank-track stats, Tuckered Out, and
  the state machine that walks a game from deal to result. They are pure — same seed and same actions in, same game
  out — and every one of them is a `ModelAction` somebody has to issue. They are what the unit tests test, what the
  [bots](bots.md) reason over, and what self-play runs in bulk ([`rules.md`](rules.md)).
- **The match actor** (server) is the only thing that issues them, and it supplies everything the rules have no
  business knowing: seats and player identities, timers, disconnects, its own lifetime, the wager, and the delivery of
  the result.

**The rules take their timings as parameters from their host**, written onto the model at the deal; the server host
reads runtime options and the test hosts supply zeros ([`protocol.md`](protocol.md#timings-are-host-supplied)). Every
deadline is stamped from the model's own clock, which ticks once a second
([`protocol.md`](protocol.md#the-models-clock)).

**There is no offline match mode.** The match exists only as the real server match actor. The client holds the rules
code and still never hosts a game: it re-executes a timeline it did not write, and it cannot write one. An in-browser
host would be a second answer to every robustness rule below — a second set of timings, a second seat-occupancy story —
in exchange for a demo that hides the exact problems this sample exists to show.

## Public state and authoritative state

The replicated match model **is** the authoritative state. What is hidden is one `ServerOnly` submodel tree hanging off
it; everything else is what a spectator of both seats could legitimately be shown, and it is carried in full:

- the **Weather** in force, and the stakes the match was formed with — ranked or not, both Power Scores, the tier, each
  player's **locked cards**, and whether a Heist will happen at all;
- the two **seats** — name, occupancy kind, connected flag, cover mark, Den HP, current and maximum mana, hand *count*,
  deck *count*, and the reserve left on their turn clock;
- each seat's **graveyard**, in full, and its **unseen pool** — the multiset of that player's cards that have not yet
  become public ([`hidden-information.md`](hidden-information.md#public-deck-contents-and-the-subtraction-problem));
- the **board** — every critter in play with its instance identity, current attack and health, damage, keywords,
  whether it is sleepy and whether it has attacked this turn;
- the **phase**, whose turn it is, the turn number, the count of accepted actions, and the absolute stamp of whatever
  the table is currently waiting for;
- the **action history**: every accepted action and every rules-driven step, in order, with the seat that owned it;
- the **Heist eligibility list** per seat — the cards that seat played this match, minus the ones its owner had locked
  at enqueue.

The action history is kept whole rather than only for the current turn. A game is bounded by Tuckered Out at a little
over twenty turns a side, so it is a few hundred small entries at worst, and it pays for itself three times: the client
renders its event feed from the tail, a player reconnecting at turn eight can read the game they missed, and the Heist
eligibility list is a projection of it and the locked set. The list is nonetheless kept explicitly, because it has to
outlive the board — it is read during the Heist and again in the result payload.

**The locked set is frozen at enqueue, with the deck.** That is the whole security property of locks: a player who is
losing cannot padlock at turn nine the card they are about to be robbed of. The match reads the snapshot it was formed
with, so a lock taken mid-match applies to the *next* match. Locks are open information from the pre-match panel
onward, which is what makes the wager legible before it is agreed rather than after it is lost.

Beneath the one `ServerOnly` member are both deck orders, both hands, the seeded stream, the bot seed, the hidden half
of the card registry and a held peek's reveal. Each seat's own hand reaches its own
client separately ([`hidden-information.md`](hidden-information.md#delivering-a-hand)). Everything else is public,
replicated identically and checksummed — **including the rules**. Two hosts that come to disagree about the game end the
session rather than drawing two boards, which is stronger than a projection could offer: a projection can only be wrong
about the picture, and this can only be wrong about the game.

## The turn flow

1. **Deal.** The actor seeds the match from a cryptographically random seed
   ([`hidden-information.md`](hidden-information.md#the-seed-is-part-of-the-secret)), draws the Weather, resolves both
   decks from the players' saved lists with their owners' ranks applied, shuffles each, decides the first player, and
   deals — three cards to the first player, four to the second, whose Acorn arrives when the mulligan ends. The
   order is fixed so the same seed reproduces the same game in every host.
2. **The join window.** The first turn arms when both human seats have subscribed, or when a **join window** expires,
   whichever comes first. A human seat that never subscribed is treated as absent from that moment: its grace is
   already spent and a bot covers it.

   > **A table is minted with nobody at it.** Its seats say who owns them, not who has arrived, and the two are
   > different facts. A seat seeded as present because it *has* an owner makes "everybody has arrived" true at the
   > moment the table comes into being, and the window then closes on the actor's first pass — before a single client
   > has been told which table it is in.

3. **The Weather reveal and the mulligan.** The Weather is published before either seat chooses — mulliganing around it
   is the plan it exists to enable, and revealing it after would make it dice on an outcome. Both seats mulligan
   **simultaneously** under one shared deadline: each may replace any subset of its opening hand once, and its swap
   happens when it answers — the deck is reshuffled and the same number redrawn. A seat that lets the deadline lapse
   keeps the hand it was dealt.
4. **Start of turn.** The seat on turn gains one maximum mana, refills, draws a card — or takes escalating Tuckered Out
   damage if its deck is empty — and its critters wake. All of this is rules-driven and lands as one action.
5. **The main phase.** Exactly one seat is on turn and may take **many actions in any order**: play a critter, cast a
   trick, attack with an awake critter. A human seat is given a **turn deadline** with a reserve behind it; a bot seat
   is given the pacing in [`bots.md`](bots.md). There is no priority window — the opponent never acts inside somebody
   else's turn.
6. **An action is submitted.** It addresses every card and critter **by its match instance identity**. The actor
   accepts it only from the seat on turn, and only if the shared legality rules accept it against the authoritative
   state as it stands when the intent arrives. Anything else is refused with an explicit message, so the client can put
   down the card it lifted.
7. **The action resolves.** Effects run through the FIFO queue — triggers, deaths, Goodbye effects, Weather hooks — and
   the whole resolution is published as one step. The server immediately accepts the next decision; each client queues
   the effects for presentation.

   > **One primitive splits that step.** The peek-and-keep effect cannot decide at cast time, because what it acts on
   > does not exist until it reveals it — so it **pauses the queue mid-resolution** for the acting seat's directed
   > choice ([`effects.md`](effects.md#the-one-interactive-resolution)). Exclusivity holds across the pause, the queue
   > is held rather than abandoned, and the pause has its own deadline with a deterministic default, so it can never
   > stall a table. What the seat was shown is addressed to that seat alone and never leaves the unseen pool.

8. **End of turn.** The seat on turn ends it explicitly. End-of-turn effects resolve and the other seat starts its turn.
   A seat with nothing left to do still has to say so, because "the deadline lapsed" and "I am finished" must stay
   distinguishable — one of them counts as a strike.
9. **The end of the game.** A Den at zero ends it; both at zero in the same resolution is a draw. The actor computes the
   result and moves to the Heist phase or straight to `Ended`, depending on stakes fixed before the deal. The client
   withholds its result until the final effects have been presented.

**New decision clocks include a fixed presentation allowance.** The server adds three seconds to a positive turn,
mulligan or effect-choice duration so a client can catch up; it never waits for a client to finish animating. Ordinary
actions do not renew it, and a disabled deadline stays disabled.

### Legality settles every race

Every race in the game — an action arriving just after the deadline handed the turn to a bot, a covering bot's delayed
action landing after the table moved, a double-tapped card — is settled by judging each intent against the state it
arrives at: the card has left the hand, the critter has attacked, the turn has passed. That mechanism belongs to the
rules ([`rules.md`](rules.md#legality-settles-every-race)).

**A refused action still counts as its sender being present.** It resets that seat's strike count, whatever refused it.

### A covered seat is handed back between turns

While a bot covers a seat, the bot plays it and the table refuses the owner's intents (`SeatCovered`). The owner coming
back — any intent from them, or their client arriving — resets the strike count and **marks the seat to be handed
back**; the board says the seat is theirs from the next turn. The hand-back happens at the **next turn boundary**,
whichever seat's turn begins, and the start of turn 1 counts as one, so an owner back during the mulligan plays from the
first turn. The bot finishes the turn it is playing: the owner never races it for the same move and never inherits half
a turn. An owner who goes again before the boundary leaves the seat covered, and a game that has decided hands nothing
back.

### The actor is the only writer

Every change to the replicated model is made by the match actor. A client never issues an action on the match timeline;
its intents — play a card, attack, end the turn, mulligan, concede, pick — are **directed messages** to the match
entity. The client is a pure follower: an action the server refuses never appears on either board, with nothing to roll
back and no way for the two clients to disagree. This is enforced, not merely intended
([`protocol.md`](protocol.md#the-timeline-has-one-writer)). The cost is one round trip before a played card lands,
which the client covers with local feedback ([`client.md`](client.md)).

## Seats

A seat is a position at the table, numbered 0 and 1, and it is the only thing the game refers to. A player identity
appears in exactly one place, the seat roster, so changing who occupies a seat is a one-field change. A seat is one of
three things:

- **A human** — a player id, a display name, and a connected flag.
- **A bot** — no player id, a name from the reserved bot roster, shown as a computer player. A ranked match filled with
  a bot because no human arrived is labelled as such and plays at practice stakes: rating moves, ranks never do.
- **A human covered by a bot** — the human's identity is retained while the bot plays for them, so the seat is theirs
  again from the turn after they come back, if the game has not ended. **The cover is visible to both players**: the
  plaque keeps the human's name and adds a computer-player mark, because a bot playing anonymously under a human's name
  would misrepresent who is wagering their collection.

Which seat goes first is decided by the seed and is public. Each client renders itself at the bottom.

## Phases

`Playing → HeistPick → Ended`, or `Playing → Ended` where there is nothing to steal, or `Abandoned`.

**Ended** is the result-bearing finish: the outcome is computed, and each human seat's player actor is told to apply it.
**Abandoned** is the result-less one, and it is deliberately narrow:

> **A match is abandoned only if it never started** — the join window expired with no human ever subscribed. Once the
> first card is played, the game is always finished and always recorded.

The predicate also requires the table to **have** a human seat: two bots would satisfy "no human ever subscribed"
vacuously. The matchmaker never forms such a table, so this is a guard rather than a case. **A table that stops existing
is not `Abandoned`; it is gone** — there is nothing left to hold a phase.

### The Heist phase

When the game is ranked, has a winner and a loser, and the stakes tier awards a steal, the table runs the Heist itself,
as one phase with its own deadline and its own default: **the winner is shown the loser's eligible cards** — played
this match and not locked — **and picks one**, or two in the underdog tier. Then the table Ends.

**The protecting decision is not a phase, because it already happened.** A player locks cards before they queue, and a
locked card can neither lose nor gain ranks. An end-of-match protect phase would ask its most important question of the
player least likely to still be at the screen — the one who just lost — and a mechanism whose modal outcome is a
default is a mechanism pretending to be a decision. Locks are the same choice made when it can actually be made, with
the cost that makes it a choice: a locked card cannot grow either.

**Whether the phase happens, and what it pays, is fixed before the first card is dealt.** The tier follows from the two
Power Scores, known at formation; the newcomer shield is a property of the two accounts; the locked sets froze at
enqueue. The matchmaker resolves all of it into the match's stakes
record, public from the pre-match panel onward, so nobody discovers after winning that the Heist pays less than they
thought. Four consequences:

- **A draw runs no Heist.** Neither seat is the loser; the table goes straight to `Ended` with reduced rating movement.
- **A favourite's win runs no Heist.** That tier moves no ranks; punching down pays nothing.
- **The newcomer shield is its own tier.** A match where either player is shielded is formed as a no-rank-movement
  match and says so on the pre-match panel.
- **A short eligible list takes what is there.** A loser whose played cards were all locked, or who played nothing,
  leaves nothing to pick, and the end screen says which case it was. In the underdog tier a list of one pays one rank.

**Rank-1 cards cannot be locked, and do not need to be**: the floor already means a rank-1 card cannot be taken down,
and a lock would only ban the growth that is the point of playing it. A card the *winner* has locked is subtracted from
the menu too — taking it would cost the loser a rank and pay the winner nothing — and the pick screen still draws it,
padlocked, saying whose padlock it is. The re-read at apply time covers a winner who locks the card between the pick and
the delivery: they gain nothing, and the loser's side is applied on its own.

**The deadline defaults in the absent player's own interest**, using the strongest bot profile's pick — the same rule
cover and play-out use everywhere else: the highest-ranked eligible card, ties broken by rarity, then mana cost, then a
stable card order. A played card's rank is already public, so a defaulted choice is one the other player could have
predicted. **The deadline is armed only for a seat whose owner is present**; an absent winner's picks are defaulted at
once, which stops a match that lost both humans from sitting through a clock for nobody.

### A table that loses everyone is played out, not abandoned

When the last connected human's grace lapses mid-game, the actor **plays the game out immediately** — bots at both
seats, no think delays — computes the result, resolves the Heist by default, and Ends the match.

This is a design pillar with a timer attached. The economy's answer to concede-dodging is that **leaving costs exactly
what staying would**: the Heist pool is what a full game exposes, not what a rage-quit chose to show. A table that
abandoned itself when its humans left would make "close the tab at turn three" the cheapest way to protect a
collection. Playing out costs microseconds — the rules are pure and the rest of the game is a few hundred actions with
no deadlines in them. Whether a player was still at the table at the finish is recorded on the result rather than
changing what is counted.

## When players stop playing

Three failures can stall a table, and the seat's connection state decides which one applies:

> **A connected seat is governed by the turn deadline. A disconnected seat is governed by grace.** Never both — a
> disconnected player must not accumulate strikes for turns they could not take.

A seat that is **already covered** is not put back on grace when its owner drops again; the owner takes it back by
coming back.

- **A player disconnects.** The seat is held on a grace timer. Reconnecting within it re-attaches the player and the
  game continues. On expiry a bot **covers** the seat, keeping the human's identity so they can reclaim it any time
  before the match ends — **reconnecting asks for it back** without waiting for an action, and it is theirs from the
  next turn. If that was the last connected human, the table is played out as above.
- **A player stays connected but never acts.** A seat on turn that lets its deadline lapse has the **rest of its turn
  played by the bot policy**, which then ends the turn — half a turn is not a state to leave the game in. Two
  consecutive lapses hand the seat to a covering bot on the same reclaimable terms; any action resets the count, a
  refused one included. The cover lands **after** the lapsed turn is played out, and the owner's next intent — refused
  while the bot has the seat — asks for it back from the next turn.
- **A player leaves deliberately.** The board has a Leave control, and conceding is the same thing: a disconnect that
  skips grace. The seat is covered immediately; the game is still played out, the result is still real, and their
  played cards are still in the Heist pool.

**The reserve is only drawn by a seat that is doing something.** A lapsing turn deadline is extended from the reserve
bank only if the seat has acted this turn; a seat that sat still for the whole sixty seconds takes its strike at once.
The reserve is for the player deep in a puzzle — without this rule an absent player would cost their opponent the
deadline *and* the whole bank before the first strike.

## Timing budget

Every duration below is a knob supplied by the host, and the values are the shipped defaults — they make the game's
pace a stated design target. The fill wait is [`matchmaking.md`](matchmaking.md)'s knob and the
bot pacing [`bots.md`](bots.md)'s; both are named here only where the budget depends on them.

| Knob | Value | Why |
|---|---|---|
| Join window | 15 s | A cold WebAssembly boot plus a config download is slow. On expiry a human seat that never subscribed is covered, and a table **nobody** came to is `Abandoned` |
| Mulligan deadline | 30 s (armed at 33 s) | Both seats at once, so it costs the match once. Lapsing keeps the dealt hand |
| Turn deadline | 60 s (armed at 63 s) | The design's turn timer. Zero means **no deadline in force**, not one already lapsed. Armed **per seat** — a connected seat gets it and nobody else does, which makes the exclusivity rule above a property of the arming |
| Turn reserve bank | 60 s per player per match, spent in 15 s extensions | Only by a seat that has already acted this turn |
| Effect-choice deadline | 20 s (armed at 23 s) | The peek holds the whole table, so its clock is shorter than a turn. Lapsing takes the deterministic default |
| Presentation allowance | 3 s per new decision clock | Added to every positive clock above, which is why those rows name both values |
| Bot action delay | [`bots.md`](bots.md) | Owned there |
| Disconnect grace | 30 s | A tab switch or a network blip, not a walk-away |
| Strikes before cover | 2 consecutive lapsed turn deadlines | One lapse is being slow; two is being gone. Only a seat whose owner was connected for the whole clock accrues one. Zero turns the count off |
| Heist pick deadline | 45 s | The winner may be reading cards they have never owned. Armed only for a present seat, and **per pick**. It takes **no** presentation allowance: there is no board to catch up to |
| Actor linger | 150 s | Both windows: how long a minted table waits for its first subscriber, and how long it outlives its last. Must exceed the disconnect grace, the join window, and the queue's two player-side bounds together |
| Matchmaking fill wait | 45 s | Named here because the searching bound is sized against it |
| Searching bound | 75 s | The player-side bound on a queue entry, which a matchmaker restart loses. Longer than the fill wait plus a seat reservation |
| Seated bound | 30 s | The player-side bound on a committed seat. Longer than a reservation plus a mint |
| Result delivery window | 120 s | How long the table is held awake to deliver an unacknowledged result. Must exceed the last attempt's instant, `(attempts − 1) × retry interval + padding` |
| Result delivery attempts | 4 | Then given up, and logged as an error |

**The design target is 5–10 minutes**, measured with human decisions and bot thinking; the server publishes every
resolution immediately and the client owns presentation. **The deadline bounds the worst case, not the typical one**:
two players who each play a hair inside every deadline and spend their whole reserve make a match of about twenty-two
minutes. What the budget bounds is the *idle* player, through the strike rule — two lapses cost two minutes and then a
bot has the seat.

## Actor lifetime

**A match lives exactly as long as its actor.** There is no row, nothing pages a table and nothing wakes one. Three
windows bound that life:

- **Before anybody arrives.** The SDK's default wait for a first subscriber is thirty seconds, inside the range a cold
  WebAssembly boot takes under load, and a table that dies before its players reach it is lost outright. So the wait
  is set explicitly, to the linger, and must exceed the join window.
- **While a seat is empty and owed grace.** The linger must exceed the disconnect grace, so a deserted table lives to
  play itself out.
- **While a result is owed.** A table that played itself out owes two accounts a delivery, with nothing behind the
  linger to find it again; that window is held open and bounded explicitly
  ([below](#delivering-the-result-and-the-heist)).

The Heist phase does not extend the linger, and that is only true *because* of the arming rule: a table in `HeistPick`
with a human present has a subscriber, and one with nobody present defaults the pick at once. Change that rule and the
linger has to change with it.

### The trade: a rolling deploy ends every match in flight

**The match is not persisted, so a rolling deploy ends every match in flight.** Both players are told, nothing is
recorded, no rank moves, and each starts a new game. That is a deliberate, sized trade: a match is five to ten minutes,
a deploy happens on the order of days, and a lost match costs five minutes rather than an entry fee. A node loss — an
eviction, a crashed shard, an OOM — is the same event. What it buys is the whole of the alternative: a table, indexed
columns, a sweeper actor, a wake path, a restart grace, an outage push over every clock in the model, and stamping rules
each of which fails silently when wrong.

**A real game would choose otherwise when any of three things is true:** matches long enough that a deploy is likely to
land inside one; stakes a player paid real money or a limited entry for; or a result other players are waiting on (a
tournament bracket, a ladder with a scheduled close). Ephemeral is right for a short session whose result nobody outside
it is waiting on.

## Delivering the result and the Heist

The match is where the outcome happens; the two player accounts are where it means something. What travels is a card
rank moving between two collections, so this is the most durability-sensitive thing in the game. A client never reports
its own result: the table tells each human seat's player actor what to apply, server to server.

**Durability lives on the match; idempotence lives on the player.**

- **On the match:** the outcome and the Heist result sit on the model with a **per-seat acknowledgement**. Delivery is
  an *ask*, and the answer — not the send — sets the acknowledgement. It happens **loser first**, up to a bounded number
  of attempts inside an explicitly held delivery window, and is then **given up with an error naming the match, the
  seat and the account**. It does not begin until the table has finished deciding: a delivery sent mid-Heist would be
  acknowledged as the whole result, and later picks could never be delivered.
- **On the player:** application is keyed by the match. A result for a match the account has already applied moves
  nothing, however many times it arrives.

**A delivery that exhausts its budget is lost** — a rank transfer did not happen, and nothing will notice.

### Two mutations, deliberately not one transaction

The Heist is **two independent mutations** on two different actors:

- the **winner** gains a rank on the picked card — or acquires it at rank 1, or gains nothing if it is already rank 5
  or they had locked their own copy;
- the **loser** loses a rank on that card, floored at rank 1. Cards are never removed from a collection.

**Each side re-reads its own lock state when it applies**, rather than trusting the snapshot the match carries. A retry
can arrive minutes after the pick into an account that locked the card in between, and refusing to move a locked rank
is the whole promise the feature makes. A delivery that finds a lock applies nothing and acknowledges.

They are not applied atomically across the two accounts. A distributed transaction between two player actors would
need a coordinator, a prepared state on each side and recovery for a coordinator that dies mid-commit, to close a crash
window that occurs approximately never. Instead each side applies independently and idempotently, and a crash between
the two leaves a real asymmetry — that window is **accepted rather than healed**. Neither mutation can produce an
illegal collection on its own.

**Which goes first is therefore a decision, and it is the loser.** Loser-first leaves a rank lost that was never gained:
deflationary, floored, and taken from the player who accepted the stakes. Winner-first would leave a rank — or a card —
minted from nothing. What makes this safe to say out loud is the **floor, the ceiling and the locks**: each is simply a
mutation that does not happen, so a half-applied Heist is never a state to undo, only one to finish.

### The match pointer

A player's account points at the match they are in; that pointer re-seats a reconnecting player and keeps them out of
matchmaking while a result is outstanding. The acknowledgement that applies the result clears it, so recording the game
and releasing the player are one errand, and it is cleared on **either** terminal phase. A pointer naming a table that
is gone is an ordinary event with an ordinary answer — the account asks the table first and clears the pointer before
associating — and the rules for that, the refusal belt behind it and why a timeout keeps the pointer are in
[`protocol.md`](protocol.md#two-ways-a-match-pointer-locks-a-player-out).

## See also

- [`game-design.md`](game-design.md) — the game the rules implement.
- [`rules.md`](rules.md) — the rules' side of the line above: the model, the actions and the discipline.
- [`protocol.md`](protocol.md) — the SDK mechanics this design sits on.
- [`hidden-information.md`](hidden-information.md) — the public/private split.
- [`matchmaking.md`](matchmaking.md) — where a match comes from, who is seated, and how the stakes are decided.
- [`bots.md`](bots.md) — what drives a seat with no human behind it.
- [`client.md`](client.md) — how the board is rendered and played.
