# Protocol and SDK Mechanics

The Metaplay-specific rules the match is built on, separated from [`match.md`](match.md) so the game's design is
readable without them, and so the mechanics are in one place when they need checking against an SDK upgrade. The match
is an **ephemeral** multiplayer entity: the mechanics it rests on are replication, private state, directed messages and
the actor's own lifetime, and persistence is not among them.

Everything here is a constraint the SDK imposes or a default it supplies that is wrong for this game. Each one has a
failure mode, and most of them fail silently.

## What replication actually guarantees

> **The replicated timeline and its checksums are identical for every subscriber.** A model member whose value differs
> per viewer cannot ride the timeline *as a value*. It can ride the subscribe-time private-state channel, a directed
> message, or an operation addressed to one member, which every other member receives as a no-op at the same position.

This game uses all three, for three different jobs:

- **Subscribe-time private state delivers the baseline.** The model exposes a per-member private payload, the actor
  serializes it per subscriber, and the client applies it before the checksum check — the same mechanism the SDK's
  guild model uses for per-member invites. It covers the deal and every reconnect with no custom protocol.
- **An addressed timeline operation maintains it.** `ExecuteActionPerMember(member, action)` stages an operation that
  one member receives and every other subscriber — and the server itself — runs as a `NoopAction`, at the same position,
  under the same checksum. This call is not in the Metaplay SDK R38 release: the sample carries a vendored copy of the
  SDK change that adds it, in `Backend/Server/SdkPreview/` — read [its
  README](../Backend/Server/SdkPreview/README.md) before copying the pattern.
- **A directed message answers a request** — `MatchIntentRefused`. It has nothing to order against the timeline.

**An addressed action may not touch the checksummed model.** The server runs a `NoopAction` in its place, so a public
mutation there is one the other members never make — and because the server runs the no-op, that is a divergence for
every member who received one rather than a silent one. In this game an addressed action writes only the client's own
view of its hand and of a held peek. Why the private half rides the timeline rather than a message of its own, and
everything about how a hand is kept in step, is [`hidden-information.md`](hidden-information.md#delivering-a-hand).

**And the checksum covers the game, not a picture of it.** With the rules on the timeline, a public rules divergence
is a checksum mismatch — the loudest failure the SDK has. The report travels one way: the client sends its own
`ComputeChecksum` bytes to the server (`EntityChecksumMismatchDetails`), and the server logs a member-by-member diff
when it is checksumming every operation, which the match does in development. Nothing sends a *server* model dump to a
client on the multiplayer-entity route — which matters for the `ServerOnly`-over-`Hidden` argument below, since the
bug-report route that argument is about is the *player* entity's.

## The timeline has one writer

The SDK accepts a client-enqueued action only if it is declared `FollowerSynchronized`. The match action base is
**leader-synchronized only** and no match action declares otherwise, so the actor is the only writer. Every client
intent — play, attack, end turn, mulligan, concede, pick — is a directed message instead, and every refusal is an
explicit message back.

## Actions replay on clients against a stripped model

A server action is broadcast verbatim and re-executed on every client's copy of the model — where every server-only
member is null or default. In this game the actions *are* the rules, so that is the design rule everything follows
from:

> **An action carries every value it needs in its own payload, and its public mutations are a function of (payload,
> public state) only. A server-only input may drive a server-only mutation and nothing else.**

Stated as a property it is checkable, and self-play checks it on every action of every game against a network-masked
clone ([`bots.md`](bots.md#the-secrecy-proof)). An action that dereferences a hand on a follower throws, which ends
that client's session; one that reads a defaulted value diverges and is caught at the next checksum. In a game with an
effect system the natural way to write a resolution — "just read the deck" — is the broken one.

Two corollaries the game needed:

- **A derived public value must be stored.** A hand count that is `hand.Count` is one a follower cannot compute, so
  hand size, deck size and a held peek's revealed count are members the rules maintain.
- **A hidden identity becomes public only through a payload.** The host resolves "which card is this" from the secret
  before the action exists; the action carries the answer and every follower mints the same public card from it.

## `ServerOnly`, never `Hidden`

`ServerOnly` means *never sent, never checksummed* — exactly what the authoritative game state needs. `Hidden` alone
means *not sent over the normal wire, but still checksummed*, and the desync diagnostic serializes under a mask that
excludes only the checksum flag. A member marked `Hidden` alone therefore ships to a client inside a bug report — here,
both hands, both deck orders, the RNG position and the seed.

`ServerOnly` **is** `Hidden | NoChecksum`, so writing `[NoChecksum]` beside it is a no-op, and there is no standalone
`[Hidden]` attribute to reach for by mistake. The masks are per member at any depth, so marking one member `ServerOnly`
strips that whole subtree: the member is the root of the secret, and every hidden thing in this game hangs beneath it
([`hidden-information.md`](hidden-information.md#what-keeps-it-honest)).

## The server prepares an action; everyone executes it

There is one state and `ExecuteAction` is the commit. What is left is a trap in the SDK's contract: **`ExecuteAction`
only logs a warning when an action's `Execute` refuses.** It does not throw or roll back, and it has already staged the
action — so a host that issued actions unchecked would accumulate refused actions every client faithfully replays into
the same refusal, a table stuck for no stated reason.

So nothing is staged unchecked, and **where the check lives follows where the action came from**:

- **A seat asked for it.** `MatchIntent.Prepare` validates the intent and returns the action it becomes, or the refusal
  the client is told, judged against the state the intent arrives at — which is why a refusal never names a card. An
  intent names no seat: the actor passes in the seat of the authenticated sender, and an intent from a seat a bot is
  covering is refused before it is prepared, while still counting as its owner coming back
  ([`match.md`](match.md#a-covered-seat-is-handed-back-between-turns)).
- **The host issued it alone** — a lapsed deadline, a seat change, the table's own phase. A `MatchHostAction` carries
  its own `ServerPrepare`, and a refusal there is a server bug.

Both run **on the server only**, so they **may read `ServerOnly` state** — whether these are your cards, in your hand —
where `Execute` may not. **And an intent may hand its action a secret**, because the intent never crosses the timeline:

- **onto the payload**, when the secret is *becoming* public — the card you played is secret until you play it;
- **on a member absent from the serialized format**, when it stays secret — the mulligan's submit action carries the
  named set that way (live on the leader, null on a follower), swaps those cards in its own body, and publishes only the
  count; the owner learns its new cards from the addressed hand operations the swap queues.

**The best answer is to have no secret to carry.** A peek's answer names **indices in the reveal**, not cards, and the
id of the choice it answers. An index is meaningless without the revealed list, which only its owner and the server
have, so the answer is an ordinary public payload: validating it needs no secret (an index is legal if it is inside the
public revealed count), and "keep a card I was never shown" is *unspellable* rather than refused. Reach for that shape
first; the mulligan cannot take it, because the cards it names are hand cards the opponent must not learn.

The SDK never dry-runs a multiplayer-entity action either: running an action commits, on the leader and on every
follower alike.

## Timings are host-supplied

Runtime options live in the server assembly. The shared game code does not reference it, and it has to run with no
server at all: the rules tests, the self-play harness and the balance harness drive the model directly.

> Every timing is a parameter the **host** writes onto the model. The server host reads runtime options; the unit-test
> and self-play hosts supply constants, usually zero.

**The durations sit on the model, and the host still owns them.** A follower has to derive the same deadline stamp from
the same tick the leader used, so the durations are model members, replicated and checksummed. The host writes
them once, when it sets the table up: editing a runtime option reaches the tables formed after the edit, and a match
never changes its pacing mid-game.

The knobs only the server uses — the matchmaking fill wait, the disconnect grace, the actor's linger and its delivery
window — stay runtime options. The knobs the rules consume — the turn deadline and its reserve, the mulligan deadline,
the peek's — are written onto the model. **The Heist pick deadline is on the server's side**: the rules end at the
result, and the Heist is a phase of the *table*, armed only for a present seat, which is occupancy the rules cannot
see ([`match.md`](match.md#the-heist-phase)).

**Game config is the wrong home for any of them.** A config change needs a config build and an archive deploy, which is
the cycle these knobs exist to avoid, and it would put pacing where a designer editing card data has to step around it.
Game config holds *content* — cards, clans, keywords, Weathers, rank tracks and starter decks — and nothing in it is a
duration the table waits out.

There is no offline match host ([`match.md`](match.md#two-layers-deliberately-separated)), so the browser never
supplies these at all — the client reads the stamps the server wrote and draws them.

## Entity channel listeners must be rebound on every activation, *before* the buffer is flushed

The entity message dispatcher clears all listeners whenever its peer resets, and the peer resets on every channel
activation. Binding once at startup works until the first reconnect and then silently stops. **Rebind every
activation.**

Where in the activation matters. A match attached mid-session makes the client buffer everything on the new channel
while it loads config, and the activation *flushes that buffer before it hands control to the game's "model is
activating" hook*. A listener bound in that hook is one step too late: the buffered messages are dispatched to nobody.
The binding belongs in the step that builds the model context, which runs before the flush. The refusal rides that
path, so binding late leaves a card hanging lifted at exactly the moment a player was moved onto a board mid-session.
The hand does not: timeline operations are applied by the journal whether or not the game bound a listener — one of the
quieter arguments for putting a private payload on the timeline.

## Two ways a match pointer locks a player out

A player's account points at the match they are in, and the association is re-established on every session start. Two
things make it fail, and only one of them is a defect:

- **The match is gone.** A table lives exactly as long as its actor, so after a redeploy, an eviction or a lost node the
  pointer names an id with nothing behind it. This is the **designed route**: an ask to an id nobody set up spawns a
  fresh actor whose model is null and which answers "not set up" — ask handlers run regardless of setup state, only
  subscribes are gated — so the account clears the pointer *before* it associates and the player lands on Home with a
  notice.
- **The seat is no longer theirs.** The table is alive and refuses the association. The probe passes, the association
  fails inside session start, and **every later login re-attempts and re-fails** unless the refusal handler clears the
  pointer. It is the quieter case, and the one queue re-formation and an expired join window produce.

Both raise the same notice, and the pointer is also cleared when a match reaches **either** terminal phase. **A timeout
is neither**: a cold shard under load and a table that is gone both answer slowly, and reading the first as the second
throws a player out of a live match. A slow answer keeps the pointer and associates anyway; the refusal is the belt
behind it. Clearing without applying a result loses nothing: a table that is gone had nothing to deliver, and a live one
is covered by its own retry budget ([`match.md`](match.md#delivering-the-result-and-the-heist)).

## The model's clock

The match ticks once a second and nothing happens on a tick: the ticks exist to advance the model's clock, which every
deadline is stamped from. No action carries a time.

- **On the server, model time is the wall clock floored to the tick.** The SDK runs the pending ticks before every
  action, so a rule that arms a deadline stamps it from the current second, and a follower that plays the same ticks
  reaches the same stamp. The actor schedules its wake for the wall-clock instant the model reaches a stamp, and its own
  host-held stamps — a bot's think delay, a result retry — are on that same clock.
- **The rate is the lowest that serves.** Every tick is a timeline operation flushed to every subscriber. The shipped
  durations are whole seconds, so a finer tick buys no precision a stamp can use, and a deadline armed mid-tick runs up
  to a tick short, inside the presentation allowance every decision clock carries.
- **A client's countdowns read the model's clock, never the device's.** Device clocks are wrong by minutes often enough
  to decide real games, and a client that compared one against a server stamp would read every deadline as lapsed or
  still live. The client plays the server's ticks off the timeline, so its copy of the model's clock is the server's,
  late by the delivery latency; between ticks it carries the clock forward by how long ago the last tick was presented,
  capped at one tick, so a ring drains smoothly and stops where the timeline stops. Only elapsed device time is ever
  read, never its absolute value.
- **Outside a match there is no model clock**, so the one countdown there — the matchmaking wait — is sent as a
  remaining duration and counted down from its arrival on the device clock.

## Timers are never cancelled

A scheduled callback captures the deadline it was armed for and no-ops if the model has since moved to a different one.
Nothing is ever cancelled, so a re-armed deadline cannot be defeated by a stale callback and there is no cancellation
bookkeeping. Stale callbacks are constant here: a turn deadline is extended by the reserve, displaced by a peek's own
deadline and put back, replaced by the next turn's, superseded by grace, and replaced again by a Heist deadline.

Pad the schedule slightly past the deadline, or a punctual timer fires a hair early, the expiry check no-ops, and the
table waits on a stamp nothing will offer again.

## Message codes and namespaces

There is **no reserved message-code range for games** in the SDK, and its own codes are scattered up to roughly 19 800.
A duplicate is a startup crash naming the code.

- Allocate a high band (30 000+) in fixed-width per-subsystem blocks — match intents, match notifications,
  matchmaking, result delivery — documented in one registry with the allocation rule beside it.
- **Retire numbers; never recycle them.** A retired code stays in the registry marked reserved.
- **Namespace placement is enforced at startup.** Client-facing messages live in the shared namespace; server-internal
  ones must not. So the intents and the refusals are shared code, while the matchmaker's internal traffic and the
  result-delivery ask are not.

## Type identity freezes at first live deployment

The serializer folds a type's assembly and namespace into its identity. Moving a serializable type between assemblies
or namespaces is free before the first live deploy and a migration after it. This game has a large surface to settle —
the match model and its secret, every intent and notification, the effect primitives, the config types and the player
collection — and the effect system in particular should grow inside a namespace that is never going to move. The SDK's
multiplayer model base also reserves a band of member tags, which the match model avoids.

## Ephemeral entities and their windows

An ephemeral multiplayer entity needs no table type, no EF migration and no persisted-entity config; the entity config
base class enforces the pairing, so moving between the ephemeral and persisted variants is a compile-time change.

`ShutdownAfterSubscribersGone(linger)` names one window and hides another: it supplies a **thirty-second wait for the
first subscriber**. For a persisted entity that is a performance detail. For an **ephemeral** one minted before its
participants arrive, it is the window in which the entity — the only copy of its state — can be lost outright, and a
cold WebAssembly boot under load runs into tens of seconds. The two-argument overload is the fix; both windows are set
to the same number. There is also a smoothing addition of up to half the *linger*.

**An entity that owes the world an errand needs a third window.** The shutdown policy knows only about subscribers, and
a table that has computed a result other entities must be told can have none. A **wakelock** keeps the actor alive
regardless, self-releases after its lifetime, and returns the actor to the ordinary linger when disposed.

**Do not reach for `RequestShutdown` as the counterpart.** It has no subscriber check, so using it to end a delivered
table closes the board of a player still reading the result. The linger path re-checks subscribers *and* wakelocks, so
releasing the wakelock is the whole mechanism; `RequestShutdown` is right only for a table that provably has nobody at
it.

## Services and entity minting

The matchmaker is a singleton service entity. It needs an explicit **never shut down** policy and placement on the
service node set.

Matches are minted directly: generate a random entity id and send it a setup request, retrying a few times on a
collision — a second setup is refused, which is what the retry keys on. An ephemeral entity has no "no row for this id"
branch to gate: it comes up with a null model and waits to be set up, which is the state the abandon path reads. The
entity is a side effect that cannot be un-created, so it is minted **before** anything is recorded against it: a crash
between the mint and the assignment leaves a table with nobody at it, where the other order would point a player at a
table that does not exist. **It must also be subscribed before its initial wait expires**, or it is lost with nothing
to page it.

**The waiters come out of the queue first**, before the mint, because the ordering that matters there is the cancel
race — an entry already gone is one a cancel cannot half-remove ([`matchmaking.md`](matchmaking.md)). The queue is in
memory, so a crash loses it whatever the order; a mint that merely *fails* puts the waiters back where they were.

## Testing the timers

A knob set long enough never to fire by accident cannot also fire on demand. Tests use two tools:

- **Zero** a duration, where the test wants it gone — the decision clocks, bot think delays, the fill wait. This is
  how the self-play and balance harnesses run thousands of matches in seconds.
- **Lengthen** a duration far past the run, where the test must not be overtaken by it — the join window, the turn
  deadline in any test not about the turn deadline. Cold WebAssembly boots under parallel load make this the direction
  the end-to-end suites need most. There is no endpoint that forces a deadline to expire on demand, so each lapse is
  covered by pure `Server.Tests` cases over the policy that decides it, and a lapse is proven to *fire* end to end only
  where a fixture brings a server of its own with shortened timings: the turn deadline in `MatchStrikeTests`, and the
  Heist pick clock and a delivery retry in `HeistTests`. The other lapses are asserted as policy only.

## See also

- [`match.md`](match.md) — the design these mechanics serve.
- [`hidden-information.md`](hidden-information.md) — the secrecy rules, and how a hand is delivered.
- [`matchmaking.md`](matchmaking.md) — the singleton service and formation.
- [`client.md`](client.md) — the other end of both channels.
