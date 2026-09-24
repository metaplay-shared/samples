# Missions

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md), [`game-config.md`](game-config.md)

The player has a small set of daily missions and a small set of weekly missions at once. A mission counts either
completed matches or won matches. Daily missions pay coins and weekly missions pay spin tokens. A finished mission's
reward is claimed one mission at a time. A finished, unclaimed reward stays claimable for a late-claim window after its
day or week ends. Unfinished progress expires at the reset.

Missions are implemented by the game on `PlayerModel`, in [`SharedCode/Missions/`](../SharedCode/Missions/). They use
no SDK activable.

## Calendar and rollover

A daily window starts at local midnight and a weekly window at local midnight on Monday.
[`PlayerCalendar`](../SharedCode/Player/PlayerCalendar.cs) defines both in code. The daily reward reads its day
boundary from the published `Global.DailyResetSchedule` instead. The two agree because the config build requires that
schedule to start at local midnight every day, and `MissionTests` compares the two boundaries.

Each window is a `MissionActivation` that pins the mission set that was active when the window began, so a newly
published set applies from the next window. A claimable mission is identified by its cadence, its window start and the
mission id, so tomorrow's copy of the same mission is a separate claim.

`PlayerMissionState.OutlookAt` computes the windows at a given time. It is a pure function that reads no clock of its
own and never moves backwards. The screen calls it to draw, and the match-completion observer calls it and adopts the
result. When a window rolls over, the previous one is kept only while it still holds a finished, unclaimed mission,
which is what makes the late-claim window. The late-claim snapshot it replaces is kept as the retired snapshot until the
next rollover, on the same terms. Anything older is dropped.

## Counting finished games

[`PlayerMissionState`](../SharedCode/Missions/PlayerMissionState.cs) receives finished games through the
match-completion fact ([`player.md`](player.md#the-match-completion-fact)). It rolls the windows forward to the game's
completion stamp, then counts the game toward every unfinished mission it applies to in the current daily and weekly
windows, each only if it contains the stamp. A result delivered again with an old stamp counts into no window,
because rollover never moves backwards, so missions keep no record of processed matches.

## The claim

The player claims one finished mission at a time with the client action `PlayerClaimMissionReward`, which grants
through the wallet ([`player.md`](player.md#action-base-classes)). The claim is judged against the player's model time
and is refused once the late-claim window has passed.

The retired snapshot exists for the claim. Rollover runs on the unsynchronized match-completion action, so the server
can roll over before a claim the client issued just before midnight executes there. If that rollover dropped the
snapshot the client claimed from, the client would pay and the server would refuse, and the checksummed wallets would
differ. The SDK kicks a client whose model time falls more than `PlayerOptions.ClientTimeMaxBehind` behind the
server, and a window is at least a day, so by the next rollover no client can still hold a claim on the retired
snapshot. The screen never shows it.

## Game config

The `Missions` library holds every mission, `MissionSets` groups them into daily and weekly sets, and
`Global.ActiveDailyMissionSet` and `Global.ActiveWeeklyMissionSet` select the active sets. A published set is never
edited ([`game-config.md`](game-config.md#editing-rules)). Cadences and objectives are enums in code. Config decides
targets, rewards and which missions a set holds.

The config build enforces the reward split described above, and no mission pays gems.
