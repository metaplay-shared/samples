# Spin wheel

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md), [`game-config.md`](game-config.md)

The player spends one spin token to spin a wheel of equally likely sectors. The server draws the sector and settles
the token cost and the prize in one wallet exchange before the client animates anything. The wheel has no cooldown,
so availability is the token balance.

The spin wheel is implemented by the game on `PlayerModel`, in [`SharedCode/SpinWheel/`](../SharedCode/SpinWheel/). It
uses no SDK activable or schedule.

## A spin

The client asks its player actor for a spin and names the spin's ordinal: the number of spins resolved so far, plus
one. `PlayerActor.HandleWheelSpinRequest` checks the request with `SpinWheelPolicy`, draws the sector from a
generator the actor owns, and enqueues the synchronized server action `PlayerWheelSpinResolved` with the drawn sector.
No seed, roll or angle comes from the client. The action exchanges one token for the sector's prize in a single
wallet transaction, so both happen or neither does, and writes a receipt of the spin. The wheel state is checksummed,
like the wallet it moves with ([`player.md`](player.md#action-base-classes)).

Before drawing, the actor checks that every sector's prize would fit under the wallet caps. If one would not, the
spin is refused and the token is kept.

The actor answers requests that arrive too quickly with a refusal rather than dropping them, because the client
unlocks its Spin control only on a result or a refusal.

The offline host has no handler for the spin request, so a spin needs a live server.

## Interrupted-spin recovery

The prize is paid when the action commits, before the client animates. A player who disconnects mid-reveal has been
paid and has not seen the result.

- A spin whose receipt is newer than the last one the player acknowledged is pending. On return, the wheel page shows
  the pending receipt before it offers another spin.
- A pending receipt blocks new spins.
- The client acknowledges the result with the client action `PlayerAcknowledgeWheelSpin`, which moves no balance.
- The receipt stores its own table, sector and prize, so a config publish between the spin and the reveal does not
  change what is shown.
- A request naming any ordinal other than the next one is refused, which blocks replays.

## Why a client cannot choose its prize

A synchronized server action settles when the **client** puts it on its timeline. The client can therefore read the
sector out of the action before its own model has moved, and a modified client can wait as long as the SDK's action
deadline allows before running it. Two rules make that freedom worth nothing.

- **A spin is drawn once.** While a draw for the player's next spin is outstanding, `PendingWheelDraw` holds the sector
  it came up on, and every further request is answered with that same sector. Asking again, after a reconnect or from a
  client that stalls its timeline, is never a second look at the wheel. The pending draw has no deadline, because
  releasing the ordinal on a timer would be a second draw for one spin. A draw whose action is refused on the model
  leaves the ordinal where it was, so the next request is sent the same draw again.
- **A draw settles the spin it was made for.** `PlayerWheelSpinResolved` carries its ordinal and is refused when the
  player is no longer on it. Whichever draw runs first resolves the spin for one token, and every other draw for that
  ordinal is refused for good, whether or not the result has been acknowledged.

The second rule is what stops the prize being chosen. If a draw were bound only by the unacknowledged receipt, a
client could hold several draws and acknowledge in between them. A draw it liked would meet a clear model and pay,
while the rest would meet a pending receipt and be refused for free. That is one token per prize, with the prize
picked after seeing every sector. Bound to an ordinal, the draws behind the first are dead.

What a client keeps is timing. It may delay its own reveal, and it may drop the connection instead of running the
draw. Neither changes what it gets. The SDK persists an enqueued synchronized action with the model and runs it when
the session ends, when the next one starts, or when the actor wakes, so the draw that was made resolves whatever the
client does with it.

The remaining exposure is an actor restart. The pending draw is in memory, so a draw whose action ran and was refused is
forgotten, and the next request draws fresh. Reaching that state needs the wallet or the config to change between the
draw and the action running, which no client controls on demand.

## Game config

Each version of the prize table is a `WheelTableInfo` in the `WheelTables` library, selected by
`Global.ActiveWheelTable`. A published table is never edited ([`game-config.md`](game-config.md#editing-rules)), and
receipts name the table they were drawn from. The table has no weight or percentage column: every sector is equally
likely, and `WheelTableInfo.Odds()` counts sectors into the odds the client shows. Prize tiers are presentation
only.

The config build checks the wheel's shape and its expected payout per spin against bands defined in
`WheelTableInfo`, and checks that every prize fits under the wallet caps.
