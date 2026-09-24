# Daily rewards

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md), [`game-config.md`](game-config.md)

The player claims one reward per player-local day. Rewards follow a repeating cycle of steps that pay coins, each
step at least as much as the one before it, and the last step adds a spin token. A streak grows with consecutive
claims, and one missed day per cycle is bridged automatically. Nothing is granted automatically: the player claims
each day's reward.

Daily rewards are implemented by the game on `PlayerModel`, in
[`SharedCode/DailyRewards/`](../SharedCode/DailyRewards/). They use no SDK activable. The day boundary comes from the
published `Global.DailyResetSchedule`, a local-time recurring calendar schedule.

## The claim

The server decides a claim, because the day comes from the server clock at the player's UTC offset. The client sends
a request to its player actor. `PlayerActor.HandleDailyRewardClaimRequest` checks it with `DailyRewardPolicy`, the
same policy the client uses to draw the screen, and checks the grant against the wallet caps. An accepted claim is
enqueued as the synchronized server action `PlayerDailyRewardClaimed`. The daily reward state is checksummed, like the
wallet it moves with, so the grant and the streak move together on both sides
([`player.md`](player.md#action-base-classes)).

The action re-derives the step, streak, skip day and reward from the player's state and the day. It settles the wallet
before writing anything, so a refused grant leaves the day claimable. The state also records what the last claim
paid, so a client that reconnects during the reveal can still show it.

The actor's model does not change when a synchronized server action is enqueued. `DailyRewardClaimInFlight` therefore
answers a second request for the same day as already claimed while the first is in flight. The in-flight record ends
when the claim action runs, whatever its result, because a refused claim leaves the day claimable. The actor learns that
from the SDK's `OnAfterAction` hook, which runs whether the client or the server executed the action. The guarantee that
a day is claimed once is the model's own guard, not the in-flight record.

The offline host has no handler for the claim request, so a claim needs a live server.

## Day numbering

`DailyRewardCalendar` asks the schedule for the current day, then numbers it as whole local calendar days since a
fixed epoch. The schedule decides where the boundary falls, and the epoch decides the number. The epoch is a code
constant, not config: a stored day number is compared against numbers computed from the epoch, and a moved epoch
would make every player read as already claimed.

`DailyRewardPolicy` compares the new day with the last claimed one. The next day continues the streak, one skipped day
spends the cycle's skip day, and a longer gap restarts the cycle. Completing the cycle restores the skip day.

## Game config

Each version of the cycle is a `DailyRewardTableInfo` in the `DailyRewards` library, and
`Global.ActiveDailyRewardTable` selects one. A published table is never edited
([`game-config.md`](game-config.md#editing-rules)). The config build enforces the cycle's shape described above, and
the reset schedule's shape, which missions rely on ([`missions.md`](missions.md#calendar-and-rollover)).
