# Analytics

[Documentation index](../README.md#documentation) · Read first: [`player.md`](player.md), [`economy.md`](economy.md)

This doc covers the rules Table Stakes follows for analytics events: how an event is defined and registered, the
Dashboard keywords, correlation ids, the events the client reports, and what a payload may carry. Tests enforce most
of these rules. How the SDK defines and delivers events is in
[Implementing analytics events in game logic](https://docs.metaplay.io/feature-cookbooks/game-analytics/implementing-analytics-events-in-game-logic).

## Defining an event

Every game event is a player event, derived from `PlayerEventBase`. A new event:

- has a code from the registry and an alias in snake case ([Codes and the registry](#codes-and-the-registry)),
- has keywords from the closed keyword list ([Dashboard keywords](#dashboard-keywords)),
- overrides `EventDescription` with a past-tense sentence built from the payload.

The action that commits a state change emits its event. UI code does not.

Match and matchmaking facts are also player events, emitted by each human seat's player actor, because the player
actor already has the SDK's analytics handler. A table with no human seats emits nothing. The server assembly
declares no event of its own.

## Codes and the registry

[`AnalyticsEventCodes.cs`](../SharedCode/TypeCodes/AnalyticsEventCodes.cs) holds every event code and is the list of
the game's events. The SDK leaves a code range to the game, and the registry splits it into one range per feature. Each
entry records the alias, the type name, the owner and whether the event is live, reserved or retired.

- A code or alias is never reused. A removed event stays in the registry as retired.
- An alias never changes. A new alias is the type name in snake case, without the `PlayerEvent` prefix. An entry
  whose type was renamed keeps its old alias and says so.
- No type name or alias contains a config id, currency, screen or version. Those are payload fields.
- Payload member ids are append-only. A change of meaning needs a new type, alias and code.

## Dashboard keywords

[`AnalyticsKeywords`](../SharedCode/Analytics/AnalyticsKeywords.cs) is a closed list of domains and qualifiers. Each
event has exactly one domain and at most two qualifiers. No keyword is built from a config id, currency name, screen
or player data. That detail goes in payload fields.

A keyword that varies per row, such as whether a currency row is a source or a sink, comes from
`KeywordsForEventInstance` rather than from the type.

### SDK event customizations

Where the SDK already emits a fact, the game adds its keywords to the SDK event rather than defining a similar one.
Two rows for one fact would disagree the first time something happens on a path the game did not write.
[`GameAnalyticsEventCustomizations`](../SharedCode/Analytics/GameAnalyticsEventCustomizations.cs) adds the game's
keywords to SDK events and keeps the SDK's own, so one Dashboard filter finds both game and SDK rows.

The rename is the exception. The SDK emits its name-change event only from the LiveOps Dashboard's rename path, so the
game emits its own event for a rename the player makes. Each path writes one row.

## Correlation id

[`AnalyticsCorrelationId`](../SharedCode/Analytics/AnalyticsCorrelationId.cs) links the rows one action produces, for
example a claim event and the currency rows it caused.

- It is derived from the entity id, a time and a cause string. It is deterministic, so the client and server copies
  of an action compute the same id without a random source.
- The value is a signed 64-bit integer, because the BigQuery export has no unsigned 64-bit column.
- The id is scoped to one player's log.

An action that emits several related rows creates one id and passes it to `ApplyWallet` and to its own event.
Match-completion observers derive theirs from the table's completion time. An event that stands alone carries no id.

## Client-submitted events

The client reports only two events, a screen view and the selection of a promoted entry. Everything else a client
could report, such as connection or app start, is already an SDK event, and UI detail is not recorded.

The client cannot write to the player's event log. Its route onto the player timeline is a player action, which the
server replays. So each event is emitted by a client action in
[`ShellObservationActions.cs`](../SharedCode/Analytics/ShellObservationActions.cs). Both actions change no model
state and refuse any value outside their closed enums, so the client gains no ability to modify the player beyond
adding these rows.

On the client, [`ShellObservationPolicy`](../WebClient/Meta/ShellObservationPolicy.cs) maps routes to screens and
removes the duplicates that Blazor re-renders, history replacement and reconnects would cause. It sends nothing before
a session exists.

## Payload rules

- **No free text.** No payload field may reach a `string`, including through lists, nullables, nested game types or a
  `StringId`. A `StringId` counts as free text unless it is a config id type on the vetted list in
  `AnalyticsContractTests`.
- **The same rule applies to SDK events the game constructs.** If game code creates an SDK event, the game owns what
  it carries.
- **No `ulong` anywhere in a payload.** The BigQuery export writes integers as signed 64-bit values, and the server
  builds its BigQuery formatters at startup, so an unsigned 64-bit field stops the server from booting. Use a signed
  type with the same bits, as the correlation id does.
- **No field names that suggest private data**, such as `name`, `playerId`, `email` or `displayName`. The event
  envelope already identifies the player.
- **No name text.** The identity events carry lengths, a reason or an origin.
- **Every event goes to both the player's event log and analytics.**
- **Descriptions are words and do not depend on culture.**

## Tests

`AnalyticsContractTests` checks the rules in this doc: the registry, aliases, keywords, payload types and field names.
`AnalyticsBigQueryFormatTests` checks that the server's BigQuery formatters build over every event.

`AnalyticsDemoRowCountTests` and `LiveServerAnalyticsDemoTests` walk the same demo path of a new player and assert its
exact row count. Adding an event to any step of that path fails both. Update the constant in both in the same change.
