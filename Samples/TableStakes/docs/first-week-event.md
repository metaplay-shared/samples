# First-week event

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md), [`game-config.md`](game-config.md)

A new player gets a week of personal days. Each day has a fixed reward and a small match-completion goal, one a
returning player can finish in the session they come back for. The last day is the big one: only it pays gems and spin
tokens, and it asks for the least play, because what it rewards is coming back. A day that ends with its goal unmet is
missed, but the next day still opens and the last day stays reachable. Once a goal is met, the reward stays claimable
for good.

The first-week event is implemented by the game on `PlayerModel`, in
[`SharedCode/FirstWeek/`](../SharedCode/FirstWeek/). It is not a LiveOps Event and uses no SDK schedule.

## The player's week

[`PlayerFirstWeekState`](../SharedCode/FirstWeek/PlayerFirstWeekState.cs) stores the personal start time, the schedule
the player was pinned to at the start, the progress of each day, and what it needs to count each game once. Nothing
derivable is stored: the active day, the time left, missed days and claimable rewards are derived from those
([`FirstWeekDay.cs`](../SharedCode/FirstWeek/FirstWeekDay.cs)).

The week starts at account creation. An account created before the feature existed starts it through a schema
migration ([`player.md`](player.md#adding-state-to-existing-accounts)). An account still unstarted after that, because
the config named no schedule at the time, starts from its next finished game. Starting pins the schedule that
`Global.ActiveFirstWeekSchedule` names at that moment, and the player keeps it for the whole week.

A day is 24 elapsed hours from the personal start, not a local calendar day.

## Counting finished games

`PlayerFirstWeekState` receives finished games through the match-completion fact and follows its rules
([`player.md`](player.md#the-match-completion-fact)). Two rules are specific to this feature:

- **The game's completion stamp decides the day**, even after that day's window has closed. A game played on day
  three and recorded on day five still counts for day three, as long as no game has counted into a later day.
- **Progress only moves forward.** A stamp from a day before the furthest day already reached counts nowhere.

Wins and losses count the same. Crossing a goal marks the day's reward ready, and the claim pays it.

## The claim

The player claims a completed day's reward with the client action `PlayerClaimFirstWeekReward`, which grants through
the wallet ([`player.md`](player.md#action-base-classes)). The reward comes from the pinned schedule. There is no
claim deadline.

## Game config

Each version of the schedule is a `FirstWeekScheduleInfo` in the `FirstWeekSchedules` library, selected by
`Global.ActiveFirstWeekSchedule`. A published schedule is never edited
([`game-config.md`](game-config.md#editing-rules)). Players stay pinned to the schedule they started with, so a retired
schedule must stay in the archive until no player is owed a reward from it. For the same reason, the config build
checks every published schedule against the wallet caps, not only the active one. It also enforces the week's shape
described above.
