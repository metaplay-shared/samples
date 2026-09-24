# Seasonal tournament

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md), [`game-config.md`](game-config.md)

The seasonal tournament is a points race in groups, built on the Metaplay SDK's
[Leagues](https://docs.metaplay.io/feature-cookbooks/leagues/leagues-quick-guide) feature. A player joins a season by
asking and is placed in a fixed-size group. A capped number of their finished matches count: a win scores and a loss
uses up an attempt. Seats no human holds are filled with computed bots. Players earn milestone rewards for matches
scored, and a placement reward when the season ends.

## How the game uses Leagues

The game runs one league with a single rank, so there is no promotion or demotion. Every participant is removed at
the season boundary, so a player joins each new season by asking rather than being carried over. A division model
holds only the human participants.

The server types are in [`Backend/Server/Tournament/`](../Backend/Server/Tournament/): `TableStakesLeagueRegistry`
declares the league, `TournamentLeagueManagerActor` runs the seasons, and `TournamentDivisionActor` runs one group.
The shared types are in [`SharedCode/Tournament/`](../SharedCode/Tournament/).

The season schedule is the `Tournament` runtime options section (`TournamentOptions`), not game config, because
changing it changes what the SDK's between-seasons migration job does and when. The rest period between seasons must
stay longer than that job takes, because joins are refused while it runs.

Player state is split in two:

- The SDK's league integration holds the player's current division and the concluded ones (`TournamentClientState`).
- The game holds the player's run in `PlayerModel.Tournament` (`PlayerTournamentState`): the current season's
  progress and claims, and the records that outlive a season.

The client reads its group through a `LeagueClient<TournamentDivisionModel>` created in `MetaplayClientService`.

## Joining

The player actor joins through the SDK's league integration and then enqueues `PlayerTournamentJoined`, which stamps
the season and the active reward table on the player. A second join is refused, and the action refuses the season
the player is already in, so a duplicate cannot reset a run.

The client waits for the answer to a join or a claim only until its session ends or a timeout passes, and then shows
a "try again" message. Both requests are safe to repeat, so a lost answer costs the player a retry rather than a stuck
screen.

## Scoring

`PlayerTournamentState` receives each finished match through the match-completion fact
([`player.md`](player.md#the-match-completion-fact)). A match counts when the player has joined a season, the match
finished before that season ends, the cap is not yet reached, and the match has not been counted before. Matches past
the cap are ordinary play.

Standings rank by wins. Ties go to fewer losses, then to the earlier last win, and the seat number makes the order
total. The client draws the standings and the division actor decides placement with the same
`TournamentDivisionModel.Standings` call.

The group size, the scored-match cap, the points per win and the bots' maximum wins are constants in
[`TournamentRules`](../SharedCode/Tournament/TournamentRules.cs), not config. The league manager sizes a division with
no player or config to read, and the cap limits both the player's run and the derived bots, which are computed in
different places. A value read from config in two places can differ between archives.

The player actor sends its score to the division after each counted match and on every session start. The event
carries totals, not deltas, so a repeated event changes nothing and a lost one is repaired by the next. The actor
sends it only when the league's current group for the player is the group the run was joined in. A join points the
league integration at the new season's group before `PlayerTournamentJoined` resets the run, and the previous season's
totals sent in that gap would hold the new group's copy above the new run until the run caught up.

## Derived bots

`TournamentBots.Fill` fills every seat no human holds. Bots are not participants and are not stored. Everything about
a bot is computed from the division, the seat number and the clock:

- **Targets.** Each bot has a final win total drawn from a seed of the league, season, rank and division. The totals
  are sorted, so the weakest bot holds the lowest free seat. A joining human takes the lowest free seat, so each join
  replaces the weakest remaining bot and leaves the others unchanged.
- **Progress.** Each bot moves toward its target at its own pace, faster or slower than the season clock, and stands
  on its target once the season has ended.
- **Identity.** A bot's name comes from the published player name vocabulary, and its avatar and name effect come
  from the same draws a table's computer players use ([`bots.md`](bots.md#cosmetics)). Publishing a new vocabulary
  renames bots mid-season. Placement, targets and progress do not change.

## Rewards

The `TournamentRewards` config holds milestone rewards (for a number of scored matches) and placement rewards (for a
rank band, optionally with a cosmetic). `Global.ActiveTournamentRewardTable` names the active table. The player keeps
the table they joined under, so a publish during a season does not move their milestones.

**Milestones.** A reached milestone is claimed with `PlayerTournamentMilestoneClaim`. Joining a new season clears the
run and its claimed milestones, so a milestone left unclaimed is lost when the player joins the next season.

**Placements.** When a division concludes, `TournamentDivisionActor` ranks humans and bots together at the season end,
finds the placement band in the reward table active at that moment, and records the result in the player's league
history. A later config publish does not change a resolved result. `PlayerTournamentPlacementClaim` grants the reward
through the wallet, counts a first place as a lifetime victory, and adds the cosmetic to the wardrobe unworn
([`cosmetics.md`](cosmetics.md)). A result stays claimable until claimed, across any number of later seasons.

**Result row.** `PlayerTournamentSeasonConcluded` writes the one analytics row per concluded season. The SDK adds a
concluded group to the history with an enqueued synchronized action, during session start or during the join of the
next season. So the player actor enqueues the observation behind it: at session start from `OnNewOwnerSession`, which
the SDK calls after it has executed pending synchronized actions, and after a join for the group the join left.

All tournament mutations are synchronized server actions, because the claims write the checksummed wallet
([`player.md`](player.md#action-base-classes)).

### Why the SDK claim path is not used

The SDK's historical division reward claim applies the rewards with no commit flag, no correlation id and no result,
and then marks them claimed. The game's wallet can refuse a grant that would pass a currency cap, and on the SDK path
a refused grant would still be marked claimed.

So the game settles placements with its own action and leaves the SDK's reward types empty. The SDK's claim action,
which a client can call, finds no rewards and grants nothing. `IsClaimed` on a division therefore means the result was
delivered to the player, and whether the player collected it is in `PlayerTournamentState`. A league that put real
contents in the SDK's reward type would turn that client-callable action into a grant.
