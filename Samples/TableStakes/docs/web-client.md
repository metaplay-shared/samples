# Web client

[Documentation index](../README.md#documentation) · Read first: [`architecture.md`](architecture.md)

The Table Stakes client is a Blazor WebAssembly app built on the Blazor starter project's web client. The starter project's [web client guide](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/web-client.md) describes the browser build, the start query, the frame loop, rendering, the client service base, the connection dialog and the pre-built serializer, and its [publishing guide](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/publishing.md) the published client, choosing the game server, CDN hosting and the LAN. This doc covers what Table Stakes adds.

## Connection service

[`MetaplayClientService`](../WebClient/Services/MetaplayClientService.cs) is the game's subclass. It creates the SDK client with two sub-clients, `MatchClient` for the table and a `LeagueClient` for the tournament. It is the `PlayerModel` client listener. The model callbacks request a render, and `ModelChanged` and the events that depend on it are raised at the end of the frame. It wraps the game's requests and actions, and each wrapper returns `null` with no session, which is what a fixture-drawn screen gets. The service keeps the default of rendering on each frame (`RenderEveryFrame`).

The game's test settings and the `?meta=` scenario are read through [`QueryParameters.AtStart`](../WebClient/Integration/QueryParameters.cs), the query the page was loaded with, so they hold on screens reached later.

## Offline mode

`?offline` runs a session with no game server. The SDK's offline server hosts the session, the player model and a real match entity in the browser, over the same protocol and serializer as a live session. It is a development and test tool.

[`TableStakesOfflineServer`](../WebClient/Integration/TableStakesOfflineServer.cs) hosts the match with the same `MatchHost`, engine and bots as `MatchActor`, through `IMatchHostEnvironment` ([`match.md`](match.md#engine-and-host-shells)). It seats the player with three bots, handles the table's client messages the way the actor does, and runs the table every frame. The offline player is saved in browser storage, so it survives a reload. The built-in config archive that `tools/GameConfigGen` writes is what offline mode loads ([`game-config.md`](game-config.md)).

Offline mode differs from a live table on purpose: there is no move deadline and the player's seat starts as arrived. `MetaLayout` sends a seated offline player to `/table` only from Home, so the other meta screens stay reachable.

Offline mode cannot exercise connection loss, reconnects, disconnect grace, the association handshake or server restarts. `TableStakesOfflineServer` answers the rename, daily reward claim and wheel spin requests with the same shared policies as the player actor, without its throttles. The offline server accepts every purchase without validating it, so a refused purchase needs a live server.

## The table's two clocks

[`MatchClocks.cs`](../SharedCode/Match/MatchClocks.cs) defines two clock types with no conversion between them, so mixing them in one answer does not compile:

- `AuthoritativeTime` is the server's clock, as estimated by the client ([`match.md`](match.md#client-clock-offset)). It answers anything the server also decides, such as whether a move can still be submitted.
- `PresentedTime` is the animated board's clock, which trails authoritative time while a trick animation plays. It answers anything the player sees: who is on turn, the deadline ring and the terminal phase.

Countdowns compare against absolute timestamps in the model, never model time, because the match does not tick.

## Installing as a home-screen app

[`manifest.webmanifest`](../WebClient/wwwroot/manifest.webmanifest) and the tags in `index.html` let the client install as a standalone portrait app. Icon links are absolute, so a client-side route does not resolve them to the `index.html` fallback. There is no service worker, so the app needs a network.

On iOS, add it with Safari's Share, then Add to Home Screen. It opens without browser chrome only from the home-screen icon. Icons must be opaque, because iOS draws transparency over black. iOS caches the icon when it is added, so re-add the app after changing it.
