# WebClient (Blazor WebAssembly)

NinjaRun's Blazor WebAssembly app. It is built on the Blazor starter project's web client: the target
framework, the pre-built serializer, the built-in config archive, the frame loop, rendering, the client service and
connection handling are as the starter project's [web client guide](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/web-client.md) describes, and
publishing, deploying and running on a phone as its [publishing guide](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/publishing.md) does. This file
covers what is NinjaRun's own.

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

`?offline` plays the practice course with no game server, through the SDK's offline server, as the starter project's
[offline mode](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/web-client.md#offline-mode) does. Offline play submits nothing, and says so on the result.
`?course=<map id>`, read when the client starts, picks the practice map.

## Hosting

The client is deployed to the CDN with `metaplay deploy webclient`. The `webClients` entry in
`metaplay-project.yaml` adds a rule to the SDK's preset: art and audio named after their content are cached as
immutable. The game server answers the root of its public web host with a redirect to the client on the CDN, keeping
the query (`Backend/Server/WebClientRedirect/`).

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

## Installing as a home-screen app

The client installs as a standalone portrait app ([installing as an app](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/publishing.md#installing-as-an-app)).
On iOS, the `apple-mobile-web-app-capable` meta tag and the `apple-touch-icon` links give a full-screen launch
from the home screen: open the site in Safari, then **Share → Add to Home Screen**, and launch from the icon. Adding
it works over plain HTTP from a LAN address, so no HTTPS is needed for testing.

The icons are the game's own, a hooded runner under a torii gate, in `wwwroot/icons/` (`apple-touch-icon.png`
180×180, `icon-192.png`, `icon-512.png`), with a copy at `wwwroot/apple-touch-icon.png`, where iOS looks for it, and
the 32×32 `wwwroot/favicon.png`. They are fully opaque, because iOS draws transparency over black. `theme-color` in
`index.html` and `theme_color` / `background_color` in `manifest.webmanifest` are the icon's deep blue (`#061752`),
so the browser chrome and the splash match it. iOS caches the icon when the app is added, so re-add the app after
changing it.
