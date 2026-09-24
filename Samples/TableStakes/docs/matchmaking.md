# Matchmaking

[Documentation index](../README.md#documentation) · Read first: [`match.md`](match.md)

Matchmaking puts a player who taps Play into a match within seconds. This doc covers the game's own matchmaker, how it
forms a table, how bots fill empty seats, how cancel and timeouts work, what the client sees, and why the SDK's
matchmaker is not used. The match that matchmaking creates is described in [`match.md`](match.md).

The server side is [`MatchmakerActor`](../Backend/Server/Matchmaking/MatchmakerActor.cs) and the player's side is in
[`PlayerActor`](../Backend/Server/Player/PlayerActor.cs). The decisions are pure functions in
[`MatchmakingPolicy`](../SharedCode/Matchmaking/MatchmakingPolicy.cs), tested without an actor.

## The matchmaker entity

`MatchmakerActor` is a single service entity for the whole cluster. Its queue is in memory only and is not saved, so a
restart loses every entry. The players' own search timeouts return those players to the menu ([Timeouts](#timeouts)).

The matchmaker handles one message at a time, and each handler runs to completion before the next one starts. A
formation therefore finishes before any other arrival, cancel or timer is handled.

## Queue and fill wait

The client asks its own player actor to enter matchmaking. The player actor marks itself as searching, arms its
search timeout, adds the player to the matchmaker's queue and tells the client.

The matchmaker forms a table when four players are queued, or when the fill wait has passed since the **oldest**
waiter arrived, whichever comes first. A later arrival does not restart the wait. The matchmaker looks at the queue
again whenever anyone is still waiting, including the remainder left after a formation.

One entry per player is enforced twice. The player actor refuses a second request while it is searching or seated,
and refuses a player who is still at a table. The queue refuses a second entry for the same player. Both cases occur
in normal use: a double tap, and a second browser tab logged in as the same player.

## Forming a table

The matchmaker takes the waiters off the queue before it waits on anything, so a cancel that arrives during the
formation finds no entry and does nothing. It then reserves a seat with every waiter's player actor, creates the table
if at least one player accepted, and tells each seated player which table they are at.

The reservation requests go out together, because the matchmaker is a single actor. Asked one at a time, each
unresponsive player would hold up every other tap, cancel and timer.

If the active game config has fewer bot names than seats, the waiters go back to the head of the queue and the
matchmaker tries again shortly. A config the build accepted always has enough names ([`bots.md`](bots.md#names)).

### Seat reservation

The player's actor decides whether it takes an offered seat. It declines if the player cancelled, is already at a
table, or has no connected client. Liveness requires a connected client, not only a session, because a session
outlives its connection for a while, and a player who closed the tab right after tapping Play still has one.

The reply carries the player's current public identity, so the seat shows the name **and the cosmetics** the player
has when seated, not the ones they had when they entered the queue ([`match.md`](match.md#seat-identity)).

A reservation whose reply never reaches the matchmaker may still have been committed by the player's actor. When the
formation ends without a failed mint, the matchmaker releases those players and does not put them back in the queue,
because an actor that could not answer once would stall the next formation too.

If no player accepts, no table is formed. A table is never created without a live human.

### Minting the table

The matchmaker creates the table by asking a new random match id to set itself up. The table saves its row on its
first save, so minting needs no database write. An id that is already in use is refused, and the matchmaker tries
another. Any other failure is not retried, because it is unknown whether that setup completed, and a retry could
create a second table for the same formation. A table left behind this way has no players pointed at it, so it
holds nobody up.

If the mint fails, every waiter who accepted or did not answer goes back to the head of the queue with their original
enqueue time, and the matchmaker tries again shortly.

The table is created before any seat is assigned, so a player is never pointed at a table that does not exist.

### Bot fill

The four seats are drawn in random order. Confirmed humans take the first drawn seats, so which seat each human gets
is random. The remaining seats are bots, named from the reserved bot roster without repeats, so a table never shows
the same bot name twice. The table itself draws each bot's strength and looks when it is dealt
([`bots.md`](bots.md)).

## Cancel

The client can cancel while searching. The player's actor settles the race between a cancel and a formation, and the
seat reservation is its commit point:

- **Cancel before the reservation.** The player leaves the queue. A later reservation finds them not searching and is
  declined.
- **Cancel after the reservation.** The cancel is refused, and the client is told the player is seated. The table
  arrives shortly after.

A player therefore ends up either cancelled or seated, never both and never neither.

A player whose session ends while searching leaves the queue. A player whose seat is already reserved is left alone,
because the table is being created around that seat.

## Timeouts

The two waiting states live on the player's actor, and each is bounded because it waits on another entity's memory:

- **Searching** waits on a queue entry, which a matchmaker restart loses.
- **Reserved** waits on a table being created.

When either times out, the client is told matchmaking is unavailable. A timeout does nothing if the state has already
moved on, and a seat assignment that arrives after the player is already at another table is declined. The declined
table still holds the seat, covers it with a bot and delivers a result, so the player's actor remembers the table and
does not record that result ([`player.md`](player.md#what-the-player-actor-does)).

The [`MatchmakingOptions`](../Backend/Server/Matchmaking/MatchmakingOptions.cs) are ordered so that no timeout fires
while the step it guards is still running: the fill wait is shorter than the longest a formation may take (the
reservation ask timeout plus the mint ask timeout), which is shorter than the reservation timeout, which is shorter
than the search timeout. If the reservation timeout were shorter than a worst-case formation, a player could be told
the search failed and then be seated a moment later.

## What the client sees

The client sends its requests to its own player actor and receives status updates as messages
([`MatchmakingMessages.cs`](../SharedCode/Matchmaking/MatchmakingMessages.cs)). The status is not searching, searching,
seated, or unavailable. Cancel is allowed only while searching. Unavailable covers a player already at a table, a
formation that dropped them, and a timeout.

The status is a message rather than a player model field because the queue is in memory. A saved "searching" flag
would outlive the queue entry after a restart.

A searching status carries a stamp by which a table will form: the player's enqueue time plus the fill wait. It is an
upper bound, because the wait runs from the oldest waiter. The client counts down against its estimate of the server
clock ([`match.md`](match.md#client-clock-offset)). `MatchmakingWaitText` turns the status into what the searching
dialog shows ([`meta-shell.md`](meta-shell.md#screens-and-routes)).

When the seat is assigned, the player's actor points the player at the table and the session attaches the client to
it ([`match.md`](match.md#player-association)). The client never lists or picks tables.

## Why not the SDK matchmaker

The SDK's matchmaker is asynchronous: it matches an attacking player against a stored defender
([Introduction to matchmaking](https://docs.metaplay.io/feature-cookbooks/matchmaking/introduction-to-matchmaking)). A
Table Stakes match needs four players present in the same live game at the same moment, formed within seconds, with
bots in the remaining seats. The SDK matchmaker does not form groups of present players, so the game uses its own
queue.
