# WebClient (Blazor WebAssembly)

This web client runs **entirely in the browser** as a Blazor WebAssembly app. The Metaplay
game client (connection, `PlayerModel`, actions) executes client-side and talks to the game server
over a WebSocket — there is no server-side Blazor circuit.

Modelled on the SDK reference `MetaplaySDK` sample `Samples/MultiClient/BlazorWasmClient`.

## Projects involved

- **WebClient** (this project) — the Blazor WebAssembly app (`Microsoft.NET.Sdk.BlazorWebAssembly`).
- **GameLogic** (`../GameLogic`) — the shared game-logic assembly. Source-includes `../SharedCode`
  and references `Metaplay.Client`. The WASM client references this *as an assembly* (not source) so
  the pre-built serializer's assembly identity matches at runtime. (It is the client-side counterpart
  of `Backend/SharedCode/SharedCode.csproj`, which references the server-only `Metaplay.Cloud`.)
- **WebClientBase** (`../WebClientBase`) — generic Blazor WASM framework library (host builder,
  shared UI, connection service).
- **tools/SerializerGen** (`../tools/SerializerGen`) — build-time tool that emits the pre-built
  serializer (see below).
- **tools/GameConfigGen** (`../tools/GameConfigGen`) — build-time tool that builds both game config
  archives from the CSV sheets in `../GameConfigSource` (see below).

## The `net10.0-browser` target framework

This project targets `net10.0-browser`, and that TFM is what selects the SDK's browser flavor across
the whole `WebClient -> GameLogic/WebClientBase -> Metaplay.Client` reference graph: the compiler
defines `BROWSER`, which switches the SDK to the `ClientWebSocket` transport and the IndexedDB blob
store, and compiles out the runtime serializer codegen (unsupported on mono-wasm). `Metaplay.Client`
multi-targets `net10.0;net10.0-browser`; `GameLogic` does too, so `tools/SerializerGen` can reference
its plain-`net10.0` flavor. No extra command-line properties are needed.

Note that the publish output lives under `bin/<config>/net10.0-browser/`.

## The pre-built serializer

mono-wasm cannot generate the Metaplay serializer at runtime, so the client loads a pre-built
`Serializer/Metaplay.Generated.Browser.dll` — the name the browser build's engine integration reports
as its generated-serializer assembly. It is not committed: the WebClient build generates it through
`tools/SerializerGen/BrowserSerializer.targets`, and regenerates it whenever the assemblies it is built from
change. The tool skips the compile when the serializable types have not changed.

A build does not always resolve a DLL the same build generated — on Linux the first build after a clean
checkout can succeed with a client that fails at startup, and the targets turn that case into a build error.
`tools/DevStack.cs` and `tools/ServerImageBuild.cs` therefore generate it in a process of their own first. To
generate it by hand:

```
dotnet run --project tools/SerializerGen -- WebClient/Serializer
```

## The built-in game config archive

The client looks for a built-in config archive at `wwwroot/Assets/SharedGameConfig.mpa` — the same file
the Unity editor writes into StreamingAssets during a player build. Online it is only a cache the SDK
falls back from to the CDN, but [offline mode](#offline-mode) has nothing else to load the config from.

The config build writes it alongside the server's `StaticGameConfig.mpa`, in the form a live server hands
out — re-serialized so `[ServerOnly]` members are stripped, rather than the raw entry. **Rebuild both
whenever a sheet in `GameConfigSource/` or a config class changes:**

```
dotnet run --project tools/GameConfigGen
```

## The game view and its frame loop

`Components/Pages/Home.razor` is the one routed game page. It owns which view is on screen and swaps them behind one
screen curtain (`Components/ScreenCurtain.razor`, `Gameplay/ScreenTransition.cs`): the menu
(`Components/Menu/`: the offline practice course, the button that opens a run, the medallion with the Orb balance
and the loadout), the customize view (`Components/Customize/`), and the game view the run is played in
(`Components/Run/`) with its score screen (`Components/Result/`). Every view is a child component of the page,
never a route of its own. What is played is `Gameplay/TimeTrial.cs` — one
attempt, holding the ticket it was opened with, the shared simulation, and the inputs it records for the
server.

The run is driven from JavaScript, not from Blazor's render loop:

- `wwwroot/js/timeTrialHost.js` runs a `requestAnimationFrame` loop. Each frame it calls the attempt's
  `[JSInvokable]` frame method **synchronously**, through a `DotNetObjectReference` the page hands it, so a
  frame is simulated and drawn inside that one callback rather than a promise later.
- C# turns the frame's timestamp into whole 1/60 s simulation steps, applies the presses and releases that
  happened since the last frame at the steps they land on, and hands back a plain snapshot of the runner.
  Catch-up is capped, so a tab that was switched away from resumes rather than fast-forwarding.
- `wwwroot/js/timeTrialRenderer.js` and its modules under `wwwroot/js/renderer/` draw that snapshot onto the canvas, against the art
  in `wwwroot/art/`. It is handed the course geometry once when the run starts.
- The host's `start` does not return until the renderer reports the course's art ready (`whenReady`): every
  sheet the course draws from arrived and decoded, the hazards sheet scaled to the viewport and every
  backdrop plane's base measured. The wait is capped (`ART_WAIT_CAP_MS`), so a sheet that fails or never
  arrives cannot hold the run, and it reports its progress to the page (`OnRunArtProgress`), which holds the
  curtain covered meanwhile and shows a loading notice once the wait has lasted a beat.
- `wwwroot/js/timeTrialAudio.js` is handed the same snapshot and plays the run (see [Audio](#audio)).
- The host and the renderer publish the run's state on the canvas as data attributes for the end-to-end
  tests, but only when `globalThis.hsrDiagnostics` is set before the page loads. `WebClient.Tests/GamePageTest.cs`,
  the base of every browser test, sets it. A player's page has no flag and writes none of them, so a new
  attribute a test reads goes behind the same check (`diagnosticsEnabled`), and a new test class derives
  from `GamePageTest`.
- The whole stage is the jump button: pointer events on it, and the space bar on a desktop keyboard, call
  back into C# as presses and releases. The HUD over it is plain DOM, so the timer and the result are
  ordinary text.

The loop stops itself when the run ends. The finished run is then crossed to the score screen behind the
same bamboo-shadow curtain the menu and the race are swapped behind, while the ticket and the recording go
to the server. The score screen leads with one gate per segment, sized and lit by the placement it came in
at and carrying what that place paid; it is dimmed and marked provisional until the server has run the
recording for itself. In offline mode there is nothing to submit to, so the run stops at its own time and
says so.

The page is not rendered per frame. The service raises `OnStateChanged` only when the connection state changes or
the player model does — an action ran on it, the client's own or one the server sent — so a run being played is
redrawn only when something on it changed, never on a timer: rendering the whole page between animation frames
stalls them on a phone. What changes with time alone has a trigger of its own: the segment callout listens to the
run's `SegmentPlaced`, and a developer run handed over from the tools is announced by `DeveloperRunQueued`.

## Offline mode

`?env=offline` runs the client with no game server: the session, player model and ticking are all
served in-process by `WebClientBase/Integration/BlazorOfflineServer.cs` (the SDK's `DefaultOfflineServer`
with a clearer diagnostic for a missing config archive), against the built-in config archive above.

```
dotnet run --project WebClient/WebClient.csproj      # then open http://localhost:5290/?env=offline
```

The mode is selected by the `offline` environment in
`../WebClientBase/Configuration/StaticEnvironmentConfigProvider.cs`, whose empty `ServerHost` is what
the SDK reads as offline. It is never auto-detected from the page host, so `?env=` (the browser's
stand-in for the `--env` argument) is the only way in. Offline player state persists in IndexedDB
between reloads; "Reset" clears it.

## Build & run

For the normal full-stack development loop, run this once from the repository root:

```
dotnet run tools/DevStack.cs
```

It runs `metaplay dev server --watch` and this client under `dotnet watch` in one foreground console,
stops the sibling if either watcher fails, and owns both process trees for reliable Ctrl+C/launcher-close
cleanup. It also guards the client's dev server: that server reads its asset route table once, at startup,
and serves the app assembly under a content-hashed name, so a build of this project from outside the
watcher (an IDE, a second shell) renames the file out from under it and every page load then fails its
integrity check on an empty response. When the server can no longer serve the client its boot config
names, the launcher restarts the watcher, which rebuilds and relaunches the server; reload the page when
the console says it is back. The commands below remain useful when deliberately running only one side.

That is a Debug build: interpreted with the debugger attached, and no measure of how the game performs. For
the build players get, run `dotnet run tools/DevStack.cs -- --release` instead. It publishes this client in
Release and runs the server alone, which serves the publish output at `http://localhost:5560` and prints the
LAN URL a phone can open (see [Served by the game server](#served-by-the-game-server)). Nothing is watched.

1. **Start the game server** (`metaplay dev server`). It exposes the WebSocket listener on port 9380
   by default; the `localhost` environment in
   `../WebClientBase/Configuration/StaticEnvironmentConfigProvider.cs` points at it.

2. **Run the client:**

   ```
   dotnet run --project WebClient/WebClient.csproj
   ```

   Open the printed URL. The page boots as WebAssembly, connects, and creates a guest session.

   For day-to-day development, run the whole stack from the repository root instead — it watches both
   the game server and this client (hot reload), and the dev server binds to all interfaces (see
   [Running on a phone / over the LAN](#running-on-a-phone--over-the-lan)):

   ```
   dotnet run tools/DevStack.cs
   ```

3. **Build / publish:**

   ```
   dotnet build   WebClient/WebClient.csproj
   dotnet publish WebClient/WebClient.csproj -c Release
   ```

   Serve the published `wwwroot` over HTTP (a Blazor WASM app cannot be opened via `file://`).

## Served by the game server

The game server can serve this client itself (publicly, unauthenticated) from its **PublicWebApi**
host — no separate dev server needed to view it. This is handled game-side by
`Backend/Server/WebClientHosting/WebClientHostingController.cs` (a `PublicWebApiController` catch-all)
and the `WebClientHosting.WebRootPath` runtime option.

Every file whose name carries no hash — `index.html`, the `_framework` files the publish leaves unhashed
(`dotnet.js`, which embeds the boot config, and `blazor.webassembly.js`), and the ES modules and
stylesheets under `js/` and `css/` — is served `no-cache`, so a browser revalidates it on every load (a
`Last-Modified` check that answers `304` when nothing changed) and picks up a redeploy at once, never
pairing a stale module or stylesheet with fresh ones. The fingerprinted `_framework` assets and every file named
`<name>.sha256-<hash>.<ext>` — the art and audio — are served
`public, max-age=31536000, immutable`. That is safe because the name carries a hash of the content: a
changed sheet or clip is a new name that the revalidated entry points reference, so a cached copy can never
be stale. (The Blazor dev server used during development adds no such header.)

- **Locally:** *publish* the client (a plain `dotnet build` does not produce a self-contained
  `wwwroot` — `index.html` and static assets are resolved via a manifest, not copied — so a publish is
  required), then run the server:

  ```
  dotnet publish WebClient/WebClient.csproj -c Release
  metaplay dev server
  ```

  or both in one step, from the repository root, with the LAN URL printed:

  ```
  dotnet run tools/DevStack.cs -- --release
  ```

  Open `http://localhost:5560/` (the PublicWebApi port). `Backend/Server/Config/Options.local.yaml`
  points `WebClientHosting.WebRootPath` at this project's `bin/Release/net10.0-browser/publish/wwwroot`, and
  binds the PublicWebApi host to all interfaces so a phone on the LAN can load the page from it too. The
  watched stack (`dotnet run tools/DevStack.cs`, port 5290) remains the one for hot-reload development.

- **In the cloud:** build the server image with `dotnet run tools/ServerImageBuild.cs` instead of
  `metaplay build image`. The SDK's `Dockerfile.server` does not build this client — its
  build stage has neither these sources nor the `wasm-tools` workload — so the tool publishes the
  client first and stages it into `Backend/Server/publicwebapp/`, which `Server.csproj` copies into the
  server's publish output and so into the image at `/gameserver/publicwebapp`. That is where the
  cloud-default `WebClientHosting.WebRootPath = "publicwebapp"` looks.

  The tool deletes and rebuilds the staging directory every run, so an image cannot pick up a client
  from an earlier build. It also writes `build-info.json` next to `index.html`, served at
  `<public-url>/build-info.json`, naming the commit the deployed client was built from. Arguments after
  `--` are forwarded to `metaplay build image`; `--stage-only` stages the client and stops, for a
  pipeline that builds the image itself; `--aot` publishes the client compiled ahead of time to
  WebAssembly.

  Calling `metaplay build image` directly is the one way to get this wrong: it ships whatever is
  staged, or no client at all. The PublicWebApi host and its `-public` ingress are enabled by
  `publicWebApi.enabled: true` in the environment's Helm values (`Backend/Deployments/develop-server.yaml`
  for Develop), which puts the client at `https://<env>-public.<stack-domain>/`.

## Running on a phone / over the LAN

To open the client on another device on the same network (e.g. an iPhone for app-like testing):

1. **Start the stack** from the repository root. The game server exposes the client WebSocket on port
   **9380**, and the web dev server binds to `0.0.0.0:5290` (`Properties/launchSettings.json`):

   ```
   dotnet run tools/DevStack.cs
   ```

2. **Find the dev machine's LAN address** (`ipconfig` on Windows), e.g. `192.168.x.x`.
3. **Open `http://192.168.x.x:5290` on the phone** (same Wi-Fi as the dev machine). No separate publish is needed —
   the dev server builds and serves the client, with hot reload.

That is the Debug build. To judge performance on the phone, run the Release publish from the game server
instead — `dotnet run tools/DevStack.cs -- --release` from the repository root prints the LAN URL, on port
**5560** — and compare like with like: the Debug build is interpreted with the debugger attached and can
be several times slower.

How the connection finds its way back: `StaticEnvironmentConfigProvider.UsePageHostForLocalEnvironment`
(called from `WebClientHostBuilder`) rewrites the `localhost` environment's server/CDN host to
whatever host the page was served from. So a page loaded from `192.168.x.x` connects its WebSocket
to `192.168.x.x:9380` instead of the phone's own `localhost`.

**Windows Firewall** must allow inbound TCP on the port the page is served from — the dev server (5290)
or the game server's public web host (5560) — *and* the game server WebSocket (9380). If the page loads
but won't connect, this is almost always why. Allow them (run PowerShell as admin):

```powershell
New-NetFirewallRule -DisplayName "Metaplay WebClient dev (5290)" -Direction Inbound -Protocol TCP -LocalPort 5290 -Action Allow -Profile Private
New-NetFirewallRule -DisplayName "Metaplay WebClient release (5560)" -Direction Inbound -Protocol TCP -LocalPort 5560 -Action Allow -Profile Private
New-NetFirewallRule -DisplayName "Metaplay game server WS (9380)" -Direction Inbound -Protocol TCP -LocalPort 9380 -Action Allow -Profile Private
```

(Use `Private` if your network is classified Private; add `Public` if it's a Public network.)

## Installing as a home-screen app (PWA)

`index.html` and `wwwroot/manifest.webmanifest` configure the client as an installable web app:
`display: standalone`, theme/background colors, and home-screen icons. On iOS the
`apple-mobile-web-app-capable` meta tag plus the `apple-touch-icon` links provide a chrome-free,
full-screen launch — **but only when launched from the home screen**, not while browsing in Safari
(Safari's address bar can't be hidden during normal browsing).

To use it on iPhone: open the site in **Safari** (Chrome/Firefox on iOS can't create standalone web
apps) → **Share → Add to Home Screen** → launch from the icon. It opens full-screen with no address
bar. Add-to-Home-Screen works over plain HTTP from a LAN IP, so no HTTPS is needed for testing.

**Icons** are the game's own — a hooded runner under a torii gate. They live in `wwwroot/icons/`
(`apple-touch-icon.png` 180×180, `icon-192.png`, `icon-512.png`), with a copy at the root well-known
path `wwwroot/apple-touch-icon.png` that iOS auto-discovers and the 32×32 `wwwroot/favicon.png`. They
must be **fully opaque** (no alpha) — iOS composites any transparency over black — and the shipped
ones are. To regenerate them, resize the master into those sizes with the background flattened; the
icon link tags use **absolute** paths (`/icons/...`, `/favicon.png`) so they resolve to the site root
regardless of the current SPA route. `theme-color` in `index.html` and `theme_color` /
`background_color` in `manifest.webmanifest` are the icon's deep blue (`#061752`), so the browser
chrome and the splash match it. After changing icons, restart the dev server (a running instance
won't serve new `wwwroot` files) and re-add to the home screen — iOS caches the icon at add time.

## Audio

Sound lives in `wwwroot/js/timeTrialAudio.js`, an ES module with no dependencies that plays the
placeholder clips in `wwwroot/audio/`, under content-hashed names, through one shared Web Audio
`AudioContext`.

`createTimeTrialAudio({ baseUrl })` returns `{ unlock, onSnapshot, dispose }`; the menu tick is the
module's own `playMenuTap` export.
There are no gameplay events to subscribe to: `onSnapshot` is handed the same per-frame snapshot the
renderer draws and derives what to play by comparing it with the previous one — the charge loop
tracking the meter's pitch and level, launch on letting go, land on touching down, the hit on a
stumble, a heavier thud on landing on a pit floor, a scrape on the mantle out of it, and the sting at the finish, over a quiet ambient loop and
footsteps that keep pace with the runner. Levels are the two tables at the top of the module. Nothing
here can change what the run does or what time it produces.

`wwwroot/js/timeTrialHost.js` owns the instance: it creates it with the run, feeds it every snapshot,
and disposes it when the run stops. As with the renderer, every call into it is wrapped — a client
that cannot make a noise still plays the run.

- **Unlock gesture.** Browsers refuse to start audio without one, so the context is only created and
  resumed from `unlock()`, which is reached from the "Start run" click and from the first press of
  the run. The clips are fetched and decoded on that same gesture and kept for the rest of the page's
  life, so a second run makes no further requests.
- **Mute.** The speaker button on the menu and over the stage flips a flag the module keeps, applied
  as master gain so loops keep running silently rather than restarting. It is remembered in
  `localStorage` (wrapped, so blocked site data only costs the switch its memory) and defaults to
  unmuted. `Home.razor` reads and flips it through the host module's `isMuted()` / `toggleMuted()`, by way of
  `Services/RunHostInterop.cs`.

## Notes

- Credentials persist across reloads: the SDK's browser blob store keeps them in IndexedDB, so the
  same guest account is reused. "Reset" deletes that blob.
- The wire-level `ClientPlatform` reported to the server is `WebGL` — a coarse telemetry signal the
  SDK uses for both in-browser runtimes (Unity WebGL and managed WASM).
- Static assets under `wwwroot/` are loaded over HTTP via `HttpClient`, not the server filesystem.
