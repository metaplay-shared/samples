# Web client

[Documentation index](../README.md#documentation) · Read first: [`architecture.md`](architecture.md)

The Table Stakes client is a Blazor WebAssembly app. The Metaplay game client (connection, `PlayerModel`, actions, sub-clients) runs in the browser and talks to the game server over a WebSocket. This doc covers what running in the browser changes, connecting and recovering, the pre-built serializer, offline mode, the published build, and how the game server serves the client and updates it after a deploy. The meta-game screens are in [`meta-shell.md`](meta-shell.md). The projects and why the client's shared code is its own assembly are in [`architecture.md`](architecture.md#shared-code).

## Running in the browser

`WebClientBase`, `WebClient` and the browser build of `SharedCode.Client` target `net10.0-browser`, which selects the browser build of `Metaplay.Client`: a WebSocket transport, browser storage and no run-time serializer generation. `WebClientBase` holds the parts that are not specific to this game, such as the host builder, the environments, the connection service base class, the connection shell and the offline server base class. `WebClient` holds the game's pages, meta shell, client service and offline server.

## Environments

[`StaticEnvironmentConfigProvider`](../WebClientBase/Configuration/StaticEnvironmentConfigProvider.cs) has three environments, and the page host picks one:

- A page served from `localhost` or an IP address connects to a local server on the same host, so a phone on the LAN that opens the dev machine's address reaches the dev machine's server.
- A page served from any other host connects to the cloud server whose name is derived from the page host. For a page on `<env>-public.<domain>`, the server is `<env>.<domain>` and the CDN is `<env>-assets.<domain>`.
- `offline` runs with no server ([Offline mode](#offline-mode)). Only `?env=offline` selects it.

`?env=<id>` overrides the choice. `?wsPort=` and `?cdnPort=` override the local ports, which the E2E harness uses to reach port-isolated servers ([`testing.md`](testing.md#the-e2e-harness)).

The `?env=` override is read once, at startup. In-app links and navigations go through [`EnvironmentLink`](../WebClientBase/Utilities/EnvironmentLink.cs), which appends the page's `?env=` to the target, so a reload after navigating stays in the selected environment instead of booting into the auto-detected one.

## Connection service

[`MetaplayClientServiceBase`](../WebClientBase/Services/MetaplayClientServiceBase.cs) owns the SDK client's lifecycle, and a game subclasses it.

- **Frame pump.** WebAssembly runs on one thread, so the SDK's thread-based frame loop cannot run. The service calls `FrameLoop.RunFrame()` on a short timer and awaits a delay in between, which lets the WebSocket continuations and the connection loop run.
- **Connection loop.** It connects, starts the session, and waits for the session to end. A retryable loss is retried with exponential backoff, and the loop stops in an error state after a bounded number of attempts. `RetryNow()` cuts a wait short or restarts a stopped loop.
- **Link health.** A dead socket inside a session leaves the SDK reporting a connection while it tries to resume, so the pump checks the connection's health each frame and reports a lost link once it has been unhealthy for a moment.
- **Page visibility.** A hidden tab throttles the pump, so a page that becomes visible with the link down retries at once.
- **Reset.** `Reset()` disposes the client and deletes the stored guest credentials. The next connection creates a new guest account.

[`MetaplayClientService`](../WebClient/Services/MetaplayClientService.cs) is the game's subclass. It creates the SDK client with two sub-clients, `MatchClient` for the table and a `LeagueClient` for the tournament. It is the `PlayerModel` client listener and raises `ModelChanged` only when drawn state changed. It wraps the game's requests and actions, and each wrapper returns `null` with no session, which is what a fixture-drawn screen gets. When the match's timeline update fails, it closes the connection with the game's own desync error so the reconnect fetches a fresh model.

## Connection trouble

[`ConnectionTroublePolicy`](../WebClientBase/Services/ConnectionTrouble.cs) classifies each lost connection:

- **Session superseded** (the same player connected from elsewhere) is not retried. Two tabs on one browser profile are the same player, and if both reconnected they would terminate each other indefinitely.
- **Desync** (a checksum mismatch, or the game's own `EntityTimelineDesyncConnectionError`) is retried, because the reconnect fetches a fresh model. The game uses its own error because the SDK's default handler for a failed multiplayer entity timeline closes with `TerminalError.Unknown`, which would read as an unreachable server and stop the retry.
- **Maintenance** is retried. Other terminal errors are not. Anything else is a lost link and is retried.

[`ConnectionOverlay.razor`](../WebClientBase/Components/ConnectionOverlay.razor) draws the trouble over the app in both layouts, on a layer above every game layer. While the tab is hidden or has only just come back, a lost link shows a small reconnecting indicator, because there a lapsed link is more likely a throttled frame pump than an outage. Otherwise the trouble is a modal, which offers a retry button once the client stops retrying by itself. Nothing is shown before the first session has started.

## The pre-built serializer

The WebAssembly runtime cannot generate the Metaplay serializer at run time, so [`tools/SerializerGen`](../tools/SerializerGen/Program.cs) generates `Metaplay.Generated.Browser.dll` from `SharedCode.Client` on the build machine. It uses member-access trampolines, because the WebAssembly interpreter enforces member accessibility and direct access to a private field throws. The DLL is not committed.

Building `WebClient` regenerates the DLL when a `[MetaSerializable]` change has made it stale ([`BrowserSerializer.targets`](../tools/SerializerGen/BrowserSerializer.targets)). A build does not always resolve a DLL it generated during the same build, so pipelines generate it first in a separate process ([`AGENTS.md`](../AGENTS.md#pre-built-wasm-serializer)).

## Offline mode

`?env=offline` runs a session with no game server. The SDK's offline server hosts the session, the player model and a real match entity in the browser, over the same protocol and serializer as a live session. It is a development and test tool.

[`TableStakesOfflineServer`](../WebClient/Integration/TableStakesOfflineServer.cs) hosts the match with the same `MatchHost`, engine and bots as `MatchActor`, through `IMatchHostEnvironment` ([`match.md`](match.md#engine-and-host-shells)). It seats the player with three bots, handles the table's client messages the way the actor does, and runs the table every frame. The offline player is saved in browser storage, so it survives a reload. The built-in config archive that `tools/GameConfigGen` writes is what offline mode loads ([`game-config.md`](game-config.md)).

Offline mode differs from a live table on purpose: there is no move deadline and the player's seat starts as arrived. `MetaLayout` sends a seated offline player to `/table` only from Home, so the other meta screens stay reachable.

Offline mode cannot exercise connection loss, reconnects, disconnect grace, the association handshake or server restarts. Requests that the player actor answers, such as the daily reward claim and the wheel spin, have no offline handler, and the offline server accepts every purchase without validating it. Those need a live server.

## The table's two clocks

[`MatchClocks.cs`](../SharedCode/Match/MatchClocks.cs) defines two clock types with no conversion between them, so mixing them in one answer does not compile:

- `AuthoritativeTime` is the server's clock, as estimated by the client ([`match.md`](match.md#client-clock-offset)). It answers anything the server also decides, such as whether a move can still be submitted.
- `PresentedTime` is the animated board's clock, which trails authoritative time while a trick animation plays. It answers anything the player sees: who is on turn, the deadline ring and the terminal phase.

Countdowns compare against absolute timestamps in the model, never model time, because the match does not tick.

## The published client

`dotnet publish` differs from `dotnet build` and the dev server: it is IL-trimmed and uses invariant globalization.

- **Trimming.** The app is wired by reflection: Blazor creates components by type, the integration scans assemblies, and the serializer names game types. The assemblies reached this way are kept whole as `<TrimmerRootAssembly>` entries in [`WebClient.csproj`](../WebClient/WebClient.csproj). The SDK has no trim annotations, so a missing root gives no build warning and throws at run time, only in the published client. Test the published build with the harness's trimmed mode before a release and after changing the roots or the client's project references ([`AGENTS.md`](../AGENTS.md#browser-tests)).
- **Invariant globalization.** No ICU data ships, and culture-sensitive formatting uses the invariant culture in every locale. This suits the English-only UI. A game that formats per locale must turn it off.

## Serving the client

The game server serves the published client itself. [`WebClientHostingController`](../Backend/Server/WebClientHosting/WebClientHostingController.cs) runs on the public, unauthenticated PublicWebApi host and serves the directory set by `WebClientHosting:WebRootPath`. It serves `index.html` for client-side routes, `.wasm` with its content type, and the pre-compressed file the publish wrote when the browser accepts it. Serving is off when the path is empty. Locally the path points at the publish output, and in the cloud at the client staged into the server image ([`architecture.md`](architecture.md#deployment)).

## Updating clients after a deploy

A deploy replaces the client files on the server. A player gets the new build on their next page load, and a running client reloads itself.

- **Cache headers.** [`WebClientCachePolicy`](../Backend/Server/WebClientHosting/WebClientCachePolicy.cs) marks only the `_framework/` files whose names contain the publish's content hash as immutable. Every other file is sent as `no-cache`, so the browser revalidates it on each load. That includes `index.html`, `build-info.json`, the stylesheets and scripts, and the two framework files without a hash. One of those, `dotnet.js`, holds the boot manifest that names the hashed assemblies, so a cached copy would boot the build it came from.
- **Build ID in the script URLs.** `index.html` carries a build ID placeholder, which the server image build replaces with the build's ID and also writes to `build-info.json`. The boot script loads the framework scripts with the build ID in their URLs, so no browser cache holds a file under a new build's URL. A dev build keeps the placeholder and loads them without it.
- **Automatic reload.** [`client-update.js`](../WebClient/wwwroot/client-update.js) polls `build-info.json` and reloads the page when its build ID differs from the page's. It waits until the player is not at `/table`, no modal is open, and there has been no input for a while or the tab is hidden. A reload at the table interrupts a game, and a reload right after an action can discard it before the server receives it.

## Running on a phone over the LAN

The local server listens on all interfaces (`WebSockets:ListenHost` in [`Options.local.yaml`](../Backend/Server/Config/Options.local.yaml)), and the dev server binds all interfaces ([`launchSettings.json`](../WebClient/Properties/launchSettings.json)). Open the dev machine's LAN address and the client's port on the phone. The page host rewrite points the client at the same machine ([Environments](#environments)). If the page loads but does not connect, the machine's firewall is usually blocking the server's WebSocket port. Allow inbound TCP on the client's port, the WebSocket port and, if config downloads fail, the CDN port.

## Installing as a home-screen app

[`manifest.webmanifest`](../WebClient/wwwroot/manifest.webmanifest) and the tags in `index.html` let the client install as a standalone portrait app. Icon links are absolute, so a client-side route does not resolve them to the `index.html` fallback. There is no service worker, so the app needs a network.

On iOS, add it with Safari's Share, then Add to Home Screen. It opens without browser chrome only from the home-screen icon. Icons must be opaque, because iOS draws transparency over black. iOS caches the icon when it is added, so re-add the app after changing it.
