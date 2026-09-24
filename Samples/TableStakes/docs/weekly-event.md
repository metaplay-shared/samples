# Weekly event

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md), [`game-config.md`](game-config.md)

Each week has a named event with a theme, a points target and a reward. A finished match scores one point per trick
taken, plus a win bonus set by that week's content. Crossing the target makes the reward claimable until the event
concludes. All players share the same UTC week.

The weekly event is built on SDK
[LiveOps Events](https://docs.metaplay.io/feature-cookbooks/in-game-events/implementing-liveops-events). The SDK owns
the schedule, phases, audience and authored content. The game owns player progress and the claim, and a server-side
seeder keeps upcoming weeks on the LiveOps timeline.

## Template config

`WeeklyEventContent` ([`WeeklyEventTemplates.cs`](../SharedCode/GameConfigs/WeeklyEventTemplates.cs)) is the event's
content type: theme, tagline, points target, win bonus and reward. Authors write it as templates in the
`WeeklyEventTemplates` library. The SDK copies the content into an event when the event is created, so a template
edit or a config publish reaches only weeks created after it. The scoring rule itself is code, in
[`WeeklyEventScoring`](../SharedCode/WeeklyEvent/WeeklyEventScoring.cs).

The content's setters are private. The match-completion fact hands the content to observers by reference, and it lives
under the checksummed `PlayerModelBase.LiveOpsEvents`, so a public setter would put checksummed state within reach of
an unsynchronized action.

Content is validated in two places. The config build checks every template, because the seeder rotates through all
of them. `WeeklyEventContent.Validate` runs when an event is created, and the seeder runs it on every week it plans,
because the SDK import does not. The creation check leaves out the balance floor, so the test endpoint can create a
one-point week for end-to-end tests. It also leaves out the wallet caps, which live in `Global`, so a hand-created
event that pays over a cap is refused at claim time by the wallet.

## Player progress

Progress lives on `PlayerModel`, in
[`PlayerWeeklyEventState`](../SharedCode/WeeklyEvent/PlayerWeeklyEventState.cs), not on the SDK's per-player event
model. Finished games arrive on an unsynchronized server action, which may write only unchecksummed state, and
`PlayerModelBase.LiveOpsEvents` is checksummed ([`player.md`](player.md#action-base-classes)). The per-player event
model, `WeeklyEventPlayerModel`, therefore has no members. Its one job is to remove the event's progress when the
event concludes.

Progress is keyed by the SDK's event id, because a player can hold two weekly events at once: next week's preview
overlaps this week's review. `PlayerWeeklyEventState.OutlookAt` picks the one event the screens show. It prefers a
claimable reward, then a scoring week, then a week in review, then a week in preview.

`PlayerWeeklyEventState` receives finished games through the match-completion fact
([`player.md`](player.md#the-match-completion-fact)). A game scores for a held weekly event that has not concluded when
its completion stamp falls inside the event's scheduled window. The window comes from the SDK schedule, and the
observer derives no phase or window from the stamp, so a late result cannot move anything backwards. A game played
inside the window still scores after the phase has changed. Each match counts once per event, and scoring stops once
the target is reached.

## Phases and the claim

Seeded weeks use these SDK phases:

- **Preview:** the week is shown and nothing scores yet.
- **Active and ending soon:** games score, and a reached target is claimable.
- **Review:** nothing new scores, and a reached target is still claimable.
- **Concluded:** the progress is removed, and an unclaimed reward is gone.

The player claims with the client action `PlayerClaimWeeklyEventReward`. The reward comes from the content stored on
the event. A client action is safe here for the reason in [`player.md`](player.md#action-base-classes): the client's
points never lead the server's, the claimed flag changes only through the claim, the event's presence and phase change
only through the SDK's synchronized actions, and the wallet check runs on the checksummed wallet.

## Audience

Every event the game creates is untargeted, both from the seeder and from the test endpoint. An operator can still
target an event from the LiveOps Dashboard. `WeeklyEventContent` makes audience membership sticky, so a player who
leaves a targeted audience mid-week keeps the event.

## Seeding

A LiveOps event cannot be created from game config. It must be created on the timeline, ahead of time, in every
environment. [`WeeklyEventSeederActor`](../Backend/Server/WeeklyEvent/WeeklyEventSeederActor.cs) does that on the
server. It is a singleton service entity that imports the coming weeks into the LiveOps timeline with an in-cluster
ask, which needs no Admin API credentials. The plan comes from
[`WeeklyEventSeeding`](../SharedCode/WeeklyEvent/WeeklyEventSeeding.cs): each week is its own event with a one-week UTC
schedule and preview, ending-soon and review periods, and the templates rotate from week to week. The current week is
created with a start in the past, so a new environment has an active week as soon as the first pass runs. Games
finished before the event existed do not score.

- **Deterministic ids.** Each week's event ids are derived from the week's start and fixed constants, so the same week
  has the same ids in every environment and on every pass. The import keeps an event whose id already exists, so a
  pass only creates missing weeks and never reads the timeline to decide what is missing. The week anchor and the id
  constants are frozen: changing one renames every future week, and progress is keyed by event id.
- **Abutting weeks.** Consecutive weeks' scoring windows abut exactly, so only one weekly event scores at any instant.
  The scoring path does not enforce this and relies on the schedule.
- **Repeated passes.** The seeder runs a pass periodically, because the horizon is measured from each pass. A single
  pass at startup would stop extending weeks on a server that runs longer than the horizon.
- **What it leaves alone.** The seeder never edits or concludes events. A week an operator edited or concluded keeps
  its state. A template, rotation or horizon change reaches only weeks not yet created.
- **One event per week.** The SDK rejects recurring schedules for LiveOps Events. A separate event per week also lets
  an operator edit or conclude one week without touching the others.

The seeding horizon and pass schedule are runtime options
([`WeeklyEventSeedingOptions`](../Backend/Server/WeeklyEvent/WeeklyEventSeedingOptions.cs)) rather than game config,
because no player model reads them and a change should not be a config publish that clients download.

For tests, development-only endpoints create a week on demand and run or report a seeding pass
([`testing.md`](testing.md)).
