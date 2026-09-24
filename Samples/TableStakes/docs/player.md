# Player

[Documentation index](../README.md#documentation) · Read first: [`architecture.md`](architecture.md)

This doc covers the player entity: `PlayerModel`, the rule for which kind of action may change which state, names,
the lifetime record and match history, how a finished match reaches the player, and the match-completion fact that
most features build on. The wallet is in [`economy.md`](economy.md) and the wardrobe is in
[`cosmetics.md`](cosmetics.md).

## The player model

[`SharedCode/Player/PlayerModel.cs`](../SharedCode/Player/PlayerModel.cs) declares `PlayerModel`, which derives from
the SDK's `PlayerModelBase`. It holds the player's name, wallet, lifetime record, match history, wardrobe and the
state of each live-service feature. No player state advances with time, so `GameTick` and `GameFastForwardTime` are
empty.

A new account gets a generated name, the starting wallet, the three starting cosmetics, and the start of its
first-week event. The analytics rows for the starting wallet and the new identity are written on the account's first
login instead, because model creation has no analytics handler attached.

The model reports changes through two listeners, defined in
[`PlayerModelListeners.cs`](../SharedCode/Player/PlayerModelListeners.cs). `PlayerActor` listens for changes to the
player's public identity. The web client's `MetaplayClientService` listens for changes to each feature's state and
redraws.

### Which members are excluded from the checksum

The rule is: **a member that an unsynchronized server action writes is `[NoChecksum]`. A member written only by client
actions and synchronized server actions stays checksummed.**

The SDK runs an unsynchronized server action on the server first and on the client at a later tick. A checksummed
member written by one would differ between the two copies for a few ticks, which the SDK reports as a checksum
mismatch and which closes the session. A `[NoChecksum]` member still replicates to the client, so screens can draw it.

`CurrentMatchId`, the table the player is seated at, is `[ServerOnly]` because the actor writes it directly, outside
any action. The client learns which table it is at from the entity association, not from this field.

### Adding state to existing accounts

Saved players outlive releases, so state added to `PlayerModel` must also work for an account saved before it
existed. The game uses two tools:

- **A null-guarded getter** when an empty state is correct for an existing account. Most features added after
  launch work this way.
- **A schema migration** in `PlayerModel` when an existing account needs data moved or something granted. For
  example, accounts created before the first-week event start their run, and accounts created before the starting
  cosmetics receive them ([`cosmetics.md`](cosmetics.md#the-starting-three)).

## Actions

### Action base classes

The game has three action base classes. Action codes are allocated in `ActionCodes`, which starts in
[`PlayerActions.cs`](../SharedCode/Player/PlayerActions.cs). See
[Queuing server actions](https://docs.metaplay.io/game-server-programming/how-to-guides/queuing-server-actions) for
the SDK's action kinds.

| Base class | Sent by | May write |
|---|---|---|
| `PlayerAction` | The client | Any member |
| `PlayerSynchronizedServerAction` | `PlayerActor`, with `EnqueueServerAction` | Any member |
| `PlayerUnsynchronizedServerAction` | `PlayerActor`, with `ExecuteServerActionImmediately` | `[NoChecksum]` members only |

How the game chooses:

- **Client action** when every input is state both copies hold at the same timeline position and there is no server
  secret. Buying a cosmetic is one: the price is in config.
- **Synchronized server action** when the server decides something the client cannot (the server clock for the daily
  reward, the server RNG for the wheel, the league manager's answer for the tournament) and the action writes
  checksummed state such as the wallet. Both copies run it at the same model position.
- **Unsynchronized server action** when a fact from outside the player must apply at once and writes only
  `[NoChecksum]` state: a finished match and a rename.

A claim that reads state written by the match-completion fact may be a client action if its guard cannot pass on the
client while failing on the server. That state is server-led, so the client is never ahead of the server. The
missions, first-week and weekly-event claims use this.

**Do not send a synchronized server action with `ExecuteServerActionImmediately`.** That call uses the unsynchronized
path. If the action writes the wallet, the SDK reports an illegal modification on both sides and the client's journal
checker stops reporting for the rest of the session.

## Names

### Generated names

A new player gets a name without being asked. `DisplayNameGenerator` in
[`DisplayNameGenerator.cs`](../SharedCode/Player/DisplayNameGenerator.cs) builds an adjective, a noun and a number from
the `PlayerIdentity` config entry.

- The choice is seeded from the player's entity id and the vocabulary's version. The same account and vocabulary
  version always give the same name, which lets the first-login analytics row re-derive the identity instead of
  storing extra state. Publishing a new version changes the names of new accounts only. Existing names never change.
- If the vocabulary is missing or the generated name breaks the name rules, the player gets a deterministic
  `Guest` name instead.
- Names are not unique. The SDK does not require it.

The config build checks every name the vocabulary can produce against the name rules and the bot names.

### Name rules

[`DisplayNamePolicy.cs`](../SharedCode/Player/DisplayNamePolicy.cs) holds the rules as pure functions. They cover
length, allowed characters, reserved names and punctuation. Reserved names include every published bot name, so a
player cannot pass as a computer player. A rename also refuses an unchanged name and a rename inside a cooldown. The
client and the server run the same code, and the server's answer is the one that counts. The rules are in code, not
config.

Homoglyphs are not folded, and there is no Unicode normalization or profanity filter.

### Renaming

The client sends a rename request to its player actor, and `PlayerActor.HandleRenameRequest` decides it:

- It drops requests that arrive too quickly after the previous one, which bounds the work a client can cause with
  refused renames.
- It checks the rules with the server clock and the bot names of the player's config.
- A refusal is logged by length and reason, never by the name.
- An accepted name is applied with the unsynchronized server action `PlayerRenamed`, and the actor persists.

### The LiveOps Dashboard rename

The LiveOps Dashboard renames through the SDK's own path, which asks the game's `PlayerRequirementsValidator`. The
game's validators run the same name rules, and the server's validator adds the bot names of the active config, so a
Dashboard rename refuses bot names too. That path does not go through `PlayerRenamed`, so it does not count as the
player customizing their name.

## Public identity

[`PlayerPublicIdentity`](../SharedCode/Player/PlayerPublicIdentity.cs) is how other players see a player: the player
id, the display name and the three equipped cosmetics. Every surface that draws a player reads this one type:
the Profile preview, the tournament standings and a match's seat roster ([`match.md`](match.md)).

- It is built from the model by `PlayerModel.BuildPublicIdentity` on each call, not stored. Bots get one from
  `ForBot`.
- It compares by value, so a holder can tell whether anything changed.
- A holder of a copy is holding a snapshot, and decides for itself whether to refresh it.

The model raises `OnPublicIdentityChanged` on a rename and on equipping a cosmetic. `PlayerActor` answers it by sending
the tournament division a fresh avatar. A match's seat roster does **not** follow the hook: it is pinned when the
table is dealt. What decides the question is how long a holder shows its snapshot: a standings row is on screen for a
season, a seat for one game.

`PlayerActor` also returns the identity with its seat reservation reply
([`matchmaking.md`](matchmaking.md#seat-reservation)).

## Lifetime record and match history

[`MatchHistory.cs`](../SharedCode/Player/MatchHistory.cs) defines the lifetime record (games played, games won,
tricks won) and the match history, which keeps the most recent games.

- The record counts every completed game for every human who was dealt in, including a game a bot finished for a
  player who left.
- A history entry also records how many human opponents the game had and whether the player finished it themselves.
  Both are captured when the table reaches its result. They cannot be read back later, because a table restored from
  the database has cleared every connected flag.
- The opponent count is the seats whose owner actually subscribed to the table. A seat the matchmaker gave to a
  player who never arrived, which the join window covers with a bot, is not a human opponent to anybody else at
  the table ([`matchmaking.md`](matchmaking.md#timeouts)).

## Recording a finished match

### Delivery from the match

The table asks each human seat's player actor to record its result, and retries until the player actor answers
([`match.md`](match.md#results)).

### What the player actor does

`PlayerActor.HandleRecordResult` records a finished game once. It applies the result with the unsynchronized server
action `PlayerRecordMatchResult`, which moves the record and the history and dispatches the match-completion fact. It
then publishes the tournament score, clears the player's match pointer, persists, and answers. The answer is the
acknowledgement, so it is sent only after the state is durable.

A table whose late seat assignment the player declined ([`matchmaking.md`](matchmaking.md#timeouts)) is answered
without recording. The player never saw that game, so it moves no record, tournament attempt, mission or weekly event.
The actor keeps a short, server-only list of declined tables. An entry stays after the result arrives, because a lost
answer makes the table offer the result again.

The association with the finished table stays until the session ends, so the result stays on screen.

### Idempotence and its limit

The history is the duplicate check, so a result is recognized only while its game is still in the history.
Normally a player whose match pointer is set cannot enter matchmaking, which keeps an unrecorded result from ageing
off the history. Three paths clear the pointer without recording:

- the probe at session start gets an error from the table,
- the table refuses the association,
- the table releases a player who used Leave.

The table keeps offering the result in each of these cases, so no game is lost. A result that arrives after its entry
has aged off the history is recorded a second time. `MatchCompletionTests` covers this case.

## The match-completion fact

Four features advance when the player finishes a match: missions, the seasonal tournament, the first-week event and
the weekly event. They share one seam in [`MatchCompletion.cs`](../SharedCode/Player/MatchCompletion.cs).

- `MatchCompletion` carries the match id, the table's completion stamp, the player's rank and tricks, and whether
  they won.
- `MatchCompletionContext` adds the player's game config, their LiveOps events, the stamp in the player's local time,
  and helpers for the feature's own analytics event. It has no wallet, no model and no clock.
- A feature's state class implements `IMatchCompletionObserver.OnMatchCompleted` and is added in
  `PlayerModel.CollectMatchCompletionObservers`.

The observers run after the record is written, each inside its own `try`/`catch`. A throwing observer is logged and
skipped. The dispatch runs inside the ask that acknowledges the table's result, and an exception escaping it would
leave the result unacknowledged and the player's match pointer set, which blocks matchmaking.

The context hands out copies of the player's LiveOps events rather than the SDK's own objects, because those sit
inside checksummed state and expose mutable members. A copy's event content is still shared and must only be read.

### Observer rules

1. **Write only your own `[NoChecksum]` member.** The fact arrives on an unsynchronized server action.
2. **Decide only from the completion stamp.** The model's own time differs between client and server for an
   unsynchronized action.
3. **Never grant.** Completing a goal marks a reward ready. The player's claim action pays it.
4. **Be monotone.** A re-delivered fact carries its original, older stamp. Never move a window, day or phase backwards
   because of it. To deduplicate, keep a set of match ids scoped to the feature's own window.

`MatchCompletionTests` fails if a model member implements `IMatchCompletionObserver` and is not registered, if a
registered member is checksummed, or if the context or its event copy gains a member.

## Match association

`PlayerActor` owns the link between a player and a table. It stores the table in `CurrentMatchId` and adds the entity
association that attaches the client to the table. How the link is restored on each session and when it is cleared
is in [`match.md`](match.md#player-association).

Matchmaking state lives in the actor's memory, not in the model ([`matchmaking.md`](matchmaking.md)).

## Results screen

[`ResultsOverlay.razor`](../WebClient/Components/TableUI/ResultsOverlay.razor) draws the result from the match's
standings, not from the player model. It shows all four seats in rank order,
not only the winner. When a tie was broken by the more recent trick, or between seats with no tricks, it says so,
using the rule the standings recorded rather than working it out again ([`game-rules.md`](game-rules.md)).

The player's record moves separately, when the record action reaches the client.
