# Leaderboard and player activity

Home shows the global top 20 and the player's own position, even when it falls outside that list. A completed ranked
match qualifies an account. Rating orders the ladder; ranked wins break rating ties, then player ID makes otherwise
equal entries stable. Practice never changes this ladder. There are no seasons, resets or leaderboard rewards.

The activity strip counts online players, ranked queue tickets and connected human-owned seats in active matches.
Practice and the Heist phase count as matches; finished tables and bot opponents do not. A disconnected seat covered
by a bot does not count until its owner reconnects. Counts are sampled, not a transactionally consistent partition:
online uses the SDK's cluster-wide concurrent-player estimate, which can lag a disconnect by about a minute. The
client refreshes every ten seconds; queue/online reads are cached server-side for five seconds. A stale or failed
response shows an unavailable state rather than an invented zero. Offline mode offers neither feature.

## Server ownership

`CommunityActor` uses the SDK's persisted entity base, singleton service sharding and entity messages. The leaderboard
survives a graceful restart. Player actors publish authoritative records when a ranked result is applied, a name
changes and a session starts. Clients can request a snapshot through their own player actor, but cannot submit scores
or choose whose private request context to use. Requests are rate-limited per player.

The first startup imports existing ranked accounts through the SDK's paginated database reader and player-model
loader, without waking those players. The import runs in small batches and skips entries already reported by live
players. Once finished it is marked complete in persisted state. Unloadable player records are logged and skipped.
Subsequent live reports are idempotent replacements; an older completed-match count cannot overwrite a newer one.

Queue counts come directly from the singleton matchmaker. Match actors publish replacement counts on state changes
and every ten seconds. Reports expire after thirty seconds, so a dead match process cannot leave a permanent count.
Activity is deliberately not persisted.

The leaderboard is a small-sample implementation: all participants fit in one persisted state and are sorted for each
read. It demonstrates SDK primitives, not a large-population leaderboard index. Snapshots are saved every thirty
seconds and on graceful shutdown; after a hard crash, a player report repairs that player's entry on their next login
or ranked result. Neither collection ownership nor ranked player records depend on this derived index.

## Validation

`Backend/Server.Tests/CommunityTests.cs` covers ordering, ties, own position beyond the displayed page, falling ratings,
duplicate/stale updates and population lease expiry. `Client.Tests/CommunityTests.cs` owns an isolated server and
SQLite directory and drives two browser accounts through queue entry/cancellation, a ranked bot match, its result and
a server restart. The ranked account remains offline after restart so the observer must read the persisted ladder.
