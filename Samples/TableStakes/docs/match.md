# The match

[Documentation index](../README.md#documentation) · Read first: [`architecture.md`](architecture.md), [`player.md`](player.md), [`game-rules.md`](game-rules.md)

A match is one game of Table Stakes, hosted by a persisted
[multiplayer entity](https://docs.metaplay.io/feature-cookbooks/custom-multiplayer-features/introduction-to-multiplayer-entities).
This doc explains the split between the rules engine and its hosts, how hidden hands stay secret on a replicated
timeline, how turns and timers work, what happens when players leave, and how a table is saved and woken again.

The card rules are in [`game-rules.md`](game-rules.md). Tables are created by the matchmaker
([`matchmaking.md`](matchmaking.md)). Seats without a human are played by bots ([`bots.md`](bots.md)). The shared
code is in [`SharedCode/Match/`](../SharedCode/Match/) and the server side in
[`Backend/Server/Match/`](../Backend/Server/Match/).

## Engine and host shells

The match code has three layers:

- **The engine** (`MatchEngine`, `MatchRules`, `MatchBoard`) is pure. The same seed and the same moves produce the
  same game. Time enters as a parameter, and the engine reads no configuration.
- **The turn driver** (`MatchHost`) is shared by every host. It decides what happens next (begin play, lapse a grace,
  play the table out, end a resolve pause, auto-play a card, play a bot seat) and turns each decision into a
  `MatchAction`. The seat rules it applies are pure functions in `MatchSeatPolicy`.
- **A host shell** (`IMatchHostEnvironment`) owns timers, sessions, saving and transport. The game server's shell is in
  `MatchActor`. The browser's shell for offline mode is in `TableStakesOfflineServer`.

Both shells run the whole turn flow through `MatchHost.RunTable`. The actor calls it after every state change and from
its timers, and the offline server calls it every frame. The offline server hosts the match over the same multiplayer
entity protocol, so offline mode exercises the same replication, private state and serialization as a live table
([`web-client.md`](web-client.md#offline-mode)).

### Timings come from the host

Every duration a table uses is a field of `MatchTimings`. Runtime options exist only in the server assembly, and the
engine also runs in the browser, so the host hands the durations in. The server builds them from its runtime options
([Runtime options](#runtime-options)). The offline server starts from `MatchTimings.Default` with no move deadline,
and tests can override individual windows ([`testing.md`](testing.md#forcing-timers)).

Settings that are not per table, such as the matchmaking fill wait, stay runtime options of the actors that use them.

## Model, actor and client

- **`MatchModel`** is the replicated state. Only `MatchAction`s change it.
- **`MatchActor`** is the server's host shell. The matchmaker creates a table by asking a new random entity id to set
  itself up, so the actor spawns on that first message. It stays alive for a while after its last subscriber leaves,
  and that linger must be longer than the disconnect grace and the join window, or the actor would stop before those
  timers ran out. `MatchOptions` refuses to load a linger that is not.
- **`MatchClient`** attaches the web client and the bot client to the table. When a timeline update fails, the web
  client closes the connection with a transient error, so the reconnect fetches a fresh model instead of stopping
  ([`web-client.md`](web-client.md#connection-trouble)).

## Seat identity

Each seat carries its player's public identity ([`player.md`](player.md#public-identity)): the display name and the
equipped cosmetics. A client holds only its own player model, so the other seats' identities travel in the replicated
seat list. The table draws an opponent from the same projection as the tournament standings do
([`cosmetics.md`](cosmetics.md#at-the-table)).

A human seat's identity comes from the player's own actor when it commits to the seat, so a rename or an equip during
the search reaches the table ([`matchmaking.md`](matchmaking.md#seat-reservation)). The matchmaker names a bot seat,
and the table dresses it when it is dealt ([`bots.md`](bots.md#cosmetics)).

**The roster is a snapshot, pinned when the table is dealt and never refreshed.** An equip that lands while a table is
forming shows up in the next game. Why a table does not follow identity changes while the tournament does is in
[`player.md`](player.md#public-identity).

### Tables saved before seat identity

A seat in an older saved row holds only a player id and a display name. Those members are **kept as
`LegacyPlayerId` and `LegacyDisplayName`** and migrated, not retired. A schema migration on `MatchModel` composes an
identity out of them, wearing nothing, because nothing recorded what anybody had on when that table was dealt.

Retiring them instead would be a silent failure rather than a loud one. Tables are saved with a live game in them and
are expected to survive a redeploy mid-hand ([Persistence](#persistence)). Tagged serialization skips a member the
code no longer knows, so an old row would load **successfully** with no owner on any seat: no move accepted, no
subscriber placed, no result owed, and nothing to detect because the row reads as healthy.

## Public state and private state

The SDK sends one checksummed timeline to every subscriber, so state that differs per viewer cannot be on it. The
match state is split three ways:

- **Public:** the board, the seats, the phase and the stamps. Every subscriber receives them identically.
- **Server only:** every hand, the deck order, the undealt cards and the engine's random state, plus the bot
  profiles, the captured results and the seat loss reasons. These are `[ServerOnly]` members: saved with the row,
  never sent and never checksummed.
- **One seat's own hand:** sent only to that seat's client ([Delivering a hand](#delivering-a-hand)).

`MatchBoard.Build` copies each public field out of the engine by name, so no engine field reaches the board unless a
line of code puts it there. Counts that follow from the play history, such as cards remaining and tricks won, are
computed rather than stored. On the client, the viewer's own hand is also kept out of the checksum, because four
clients hold four different hands and must still agree on the timeline.

### The deal seed

The deal seed comes from a cryptographic random number generator. The actor draws it itself, and the setup message
does not carry one. A seed a client could derive, for example from the entity id, a timestamp or a process-wide
`Random`, would let that client recompute the shuffle and read all four hands. The engine's random state stays
server-only for the same reason.

Bot choices use a separate generator on the host. The only way to pin a deal is a test knob of the offline host,
where the browser already knows every hand ([`testing.md`](testing.md#seeds)).

### Delivering a hand

A seat's hand reaches its client when the seat subscribes, which covers the deal and every reconnect. It is sent
again whenever the host changes the hand without the seat playing it: a deadline auto-play, a covering bot's move or a
reclaim. Between deliveries, the client derives its live hand as the delivered cards minus every card in the public
play history.

Each delivery names the play index it was built at, and the client applies it only once its board has reached that
index. A directed message is dispatched at once, while a timeline update is flushed on the actor's next turn, so a
hand often arrives before the board update that explains it.

The play index on the payloads, the client's hold and its retry exist because SDK 38 has no way to order a per-player
payload against the timeline. A later SDK release adds `ExecuteActionPerMember`, an action on the timeline that one
member's client executes while every other replica executes a no-op at the same position. A hand delivered that way
cannot arrive early, so the directed message, the stamp, the hold and the retry go, replaced by one action that
carries the whole hand.

## Actions

Every change to the match model is a `MatchAction` ([`MatchActions.cs`](../SharedCode/Match/MatchActions.cs)), and
only the host publishes them.

### Only the host writes the timeline

By default the SDK lets clients enqueue actions on a multiplayer entity. The game closes that route in three places: the
match action's execute flags, the actor's validation of client actions, and the offline server's message handler.
`MatchModelTests` checks the flags of every concrete action, and `MatchActorRefusesClientActionsTests` checks the
actor's answer. Client intents travel as messages instead ([Client messages](#client-messages)). The client shows only
moves the host has confirmed, so a refused move never appears on any board and nothing has to be rolled back. The cost
is one round trip before a played card lands.

### Action payload rules

A host action is replayed on every client against a model whose server-only members are empty. Each match action
therefore carries every value it needs in its payload, reads and writes only public state, and takes its timestamps
from the payload rather than from a clock. Writes to server-only state happen in `MatchHost`, outside actions. Those
members are not checksummed, so writing them directly causes no desync.

### Publish, then commit

The engine separates deciding a move from applying it. `MatchHost` builds the action from the plan, publishes it, and
commits the engine only if the publish landed. The reverse order would leave the engine one play ahead of the board
after a failed publish. The engine is not checksummed, so nothing would detect it, and the table would refuse every
later move as stale.

Executing an action reports no result, so both shells dry-run each action before publishing it. After a refused
publish, the actor runs the table again a few times, then stops driving it and logs an error until a later publish
lands.

## Client messages

Clients send their intents to the table as directed messages: play a card, leave the table, and clock sync
([`MatchMessages.cs`](../SharedCode/Match/MatchMessages.cs)). A move names the card and the play index it answers,
never a position in the hand. The seat named on the message is not trusted: the host checks it against the seat the
sender occupies. Every move that is not played is answered with a refusal, which tells the client to release the card
it lifted.

## Turn flow

A new table is dealt at once, but play waits for the join window. Play begins when every human seat has subscribed
at least once or the window ends, whichever comes first. A human seat that never arrived is covered by a bot at that
moment. Seats start as not arrived because the table exists before any client knows about it.

From then on, one seat at a time owes one card. A connected human has a move deadline, and a bot seat has a think
delay. The fourth card of a trick starts a resolve pause with an absolute end stamp, so every client shows the same
pause. When it ends, the trick winner leads the next trick. After the fifth trick, the phase becomes Ended and the
standings are stored on the board.

### The play index

The play index is the number of cards played, 0 to 20. Every move names the index it answers, and the engine refuses
a mismatch as stale before it checks the phase, the turn or legality. This one check settles every race:

- a human card arriving after a deadline auto-play took that turn,
- a bot move held across a reclaim or the end of the table,
- a double tap.

A move from a seat's owner counts as presence even when it is refused. It resets the strike count and takes back a
covered seat. The covering bot often plays the contested index first, so a reclaim that required an accepted move
would rarely succeed.

### Phases

- **Playing:** the game is running.
- **Ended:** all five tricks are played. The standings are stored, and results are captured and delivered.
- **Abandoned:** the join window ended and no human ever subscribed. There are no standings and nothing is recorded.

The end stamp is written once, by the action that enters a terminal phase.

## Timers

The match does not tick. Every wait is an absolute timestamp in the model: the join window, each seat's grace, the
move deadline and the resolve pause. Clients count down against these stamps. The actor arms one wake for the
earliest thing the table waits on, which also covers a held bot move and pending retries. Wakes are never cancelled.
A wake whose stamp is no longer the armed one does nothing.

Tests can make a table's stamps expire on demand ([`testing.md`](testing.md#forcing-timers)).

### Runtime options

The server takes a table's durations from the `Match` runtime options in
[`MatchOptions`](../Backend/Server/Match/MatchOptions.cs). Their defaults are `MatchTimings.Default`, so the offline
host and an unconfigured server start from the same timings. The actor's linger after its last subscriber is the one
option the engine never sees ([Model, actor and client](#model-actor-and-client)).

## When players stop playing

A seat is held by a human, a bot, or a bot covering a human. A covered seat keeps its owner's identity, so on screen
the owner still appears to be playing. A connected seat is governed by the move deadline and a disconnected seat by a
grace, never both, so a disconnected player never collects strikes for turns they could not take.

### Disconnect grace

When a player's session ends, their seat gets a grace. A reconnect within it clears the grace, resets the strikes and
returns a covered seat to its owner. When the grace runs out, a bot covers the seat.

When a player subscribes, the actor notes them present and runs the table **before** the SDK snapshots the state for
the new subscriber. An action published after that snapshot would be in neither the snapshot nor the updates the
client receives, which is a checksum mismatch on the first frame.

### Move deadlines and strikes

When a connected player's deadline lapses, the table counts a strike, plays a card for them with the strongest bot
profile and sends them a hand correction. Enough consecutive strikes hand the seat to a covering bot. Any move from the
owner resets the count.

### Leaving the table

Leave covers the seat at once, with no grace. The actor then releases the player, whose actor clears its pointer to
the table and detaches the client with no results screen. The release goes out before the table runs again, because at
a table of bots that run finishes the game and records the result, which clears the pointer the release checks. The
game is still played out and recorded. The offline server covers the seat and removes the table.

### Bot cover and seat reclaim

A covered seat is played with the strongest bot profile ([`bots.md`](bots.md)). While the owner is still connected,
for example after striking out, the covering bot waits a reclaim delay before each move, long enough for the owner to
play a card and take the seat back. Any subscribe or move from the owner reclaims the seat and sends them their hand.

The first loss reason recorded for a seat stays until the seat is reclaimed. Each loss is reported to the player's
actor for analytics ([`analytics.md`](analytics.md)).

### Play-out

Once play has begun, a table with a human seat where every owned seat is disconnected with no grace left has nobody
coming back. The table then finishes the rest of the game in the same pass, with no deadlines or pauses, and every
human dealt in gets a result.

A table is never abandoned after play begins. At the most common table, one human and three bots, abandoning it would
let a player erase a loss by closing the tab.

## One game per table

A match plays one game and then stays in a terminal phase. The next game is a new trip through matchmaking. There is
no reset path, so there is no ordering problem between the next game's private state and a reset, and no per-game
client state to clear.

## Results

On the frame the game ends, the table captures one result per human seat in server-only state. It then asks every
seat's player actor to record its result, all at once, and marks each seat that acknowledges. It asks rather than
sends a one-way message, so that after a restart the table still knows which seats are owed a result. Unacknowledged
seats are retried while the table is awake. The retry is not saved, so a table evicted while it still owes a result
offers it again on its next wake.

An abandoned table sends the request with no result, which clears the player's pointer to the table. How the player
records each match exactly once is in [`player.md`](player.md).

## Player association

The player's actor remembers its current table in server-only state and associates the table with the client's match
slot, so the session attaches the client to the table. The client never looks for a table. On every session start,
the player's actor probes the table and associates it again, so a player who reloads the page is put back at the same
table.

The pointer is cleared when the result is delivered, when the player leaves, and in the lock-out cases below.

### Lock-out cases

A stale pointer would make the association fail on every login, so two cases clear it:

- **The table cannot answer the probe**, because its row cannot be read or no row exists. A timeout keeps the pointer,
  because a timeout is what a cold wake under load looks like. An unreachable table also keeps it, because a rolling
  deploy makes a live table unreachable while its shard drains.
- **The table refuses the subscribe**, because the player is not seated there.

A probe for an id with no row spawns an actor with no model. That actor stands down shortly after
(`MatchActor.ShutDownIfStillEmpty`) instead of holding a shard slot for the SDK's initial subscriber wait.

## Persistence

A table is saved as one database row ([`PersistedMatch`](../Backend/Server/Match/PersistedMatch.cs)) whose payload is
the whole model, server-only state included, so a live game survives a redeploy or an eviction mid-hand, and a
finished one stays readable afterwards. The row has no columns of its own.

### Restart and cold wake

A table restored from the database has stale connected flags and no subscribers. When the actor starts a table it did
not deal, it clears every connected flag and puts every owned seat whose player was connected, and that is not already
in grace, on the restart grace. It then re-arms its timers from the saved stamps. A held bot move and the retry
counters are not saved, so a restored table decides them again.

Without the restart grace, the table would look deserted on the frame it wakes, and it would be played out before any
client could reconnect.

## Waking and retention

Nothing scans the saved tables. A table wakes only on its own timers or when something asks it. A table whose actor
stopped with a timer pending, for example after a restart with no client reconnecting, stays asleep until something
asks it. The usual ask is a seated player's session start, which probes the table
([Player association](#player-association)). The woken actor then applies the cold-wake rule and re-arms its timers
from the saved stamps.

Nothing deletes match rows. A finished table's row stays in the database so the game can be viewed afterwards, and
the `Matches` table grows with every game played.

## Client clock offset

Clients count down against stamps written from the server's clock, and a device clock can be off by minutes. The SDK
offers no ongoing estimate of the server clock to game code, so the client measures the offset itself in
[`ServerClockEstimate`](../SharedCode/Match/ServerClockEstimate.cs).

`MatchClient` exchanges clock sync messages with the table when it activates and at intervals after that. Each sample
estimates the server time as the reported time plus half the round trip. A sample replaces the estimate when its error
is no larger or the estimate has grown old, and a sample with an implausible round trip is dropped. With no sample,
the estimate is the device clock. The actor limits how often each session may ask, and the offline server answers
with the device clock.
