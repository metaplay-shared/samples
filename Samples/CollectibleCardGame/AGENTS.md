# AGENTS.md

How to develop this sample. This is the canonical dev guide. For what the sample is, see [`README.md`](README.md); for
the game design, [`Docs/game-design.md`](Docs/game-design.md); for the per-feature architecture,
[`Docs/README.md`](Docs/README.md). For SDK concepts and APIs, use the Metaplay docs.

Markdown in this sample is hard-wrapped at 120 columns.

## The SDK

The sample sits at `Samples/CollectibleCardGame/` in the samples repository and builds against the Metaplay SDK
installed at `MetaplaySDK/` in that repository's root, by relative path (`../../MetaplaySDK`). The SDK is not checked
in. Getting it takes a [Metaplay Developer Portal](https://portal.metaplay.dev/) account and the
[Metaplay CLI](https://github.com/metaplay/cli); then, at the samples repository root:

```bash
metaplay init sdk --sdk-version=38
```

Three consequences shape everything below:

- **Never modify anything under `MetaplaySDK/`.** It is the installed SDK; a change there is lost on the next install
  and is not part of the sample.
- **The analyzers are built from source.** A standalone Metaplay project gets the source generators from the SDK's
  prebuilt DLLs via an import in `Backend/Directory.Build.props`. This sample has no such import, so *every* csproj —
  test projects included — carries explicit `ProjectReference`s to `Metaplay.CodeAnalyzers` and
  `Metaplay.CodeAnalyzers.Shared` with `OutputItemType="Analyzer"`. Copy that block when you add a project.
- **One exception is vendored.** `Backend/Server/SdkPreview/` is a temporary copy of the SDK's multiplayer-entity actor
  base carrying `ExecuteActionPerMember`, an SDK change not in the R38 release. Do not edit it except to re-sync; its
  [README](Backend/Server/SdkPreview/README.md) says how, and how it goes away.

## Layout

- **`SharedCode/`** — namespace `Game.Logic`. Player model, actions, config classes, type codes, the match model with
  its rules and actions (`Match/`) and the bot policy (`Match/Bots/`). Deterministic; no server-only or client-only
  types. `GameLogic.csproj` compiles it for the browser (`net10.0;net10.0-browser`).
- **`GameConfigSource/`** — the CSV sheets the game config build reads, one per config entry.
- **`Backend/SharedCode/`** — the same `SharedCode/` tree compiled against `Metaplay.Cloud` for the server.
- **`Backend/Server/`** — the game server: entity actors, EF migrations, runtime options, and the checked-in config
  archive it boots from.
- **`Backend/BotClient/`** — the load client.
- **`Backend/SharedCode.Tests/`**, **`Backend/Server.Tests/`** — NUnit tests over shared logic and over the server's
  pure policies.
- **`Backend/Dashboard/`** — the custom LiveOps Dashboard (Node/Vue/TS), a member of the samples repository's root
  pnpm workspace, so its `workspace:^` dependencies resolve against `MetaplaySDK/Frontend/*`. `src/gameSpecific.ts` is
  the only entry point the SDK offers; the root-level files are the SDK's template (Gotchas).
  `Backend/PrebuiltDashboard/`, where present, is a built copy the server falls back to.
- **`ClientBase/`** — namespace `Game.ClientBase`. The game-agnostic Blazor shell: connection lifecycle, the
  connection-trouble app shell, environment selection, theme, UI primitives. Anything that knows a card from a clan
  belongs in `Client/`.
- **`Client/`** — namespace `Game.Client`. The game half of the Blazor WebAssembly app. Its build also generates the
  pre-built WASM serializer (Gotchas).
- **`Client.Tests/`** — Playwright end-to-end tests, plus the client's pure policy tests.
- **`Tools/`** — `SerializerGen` and `GameConfigGen` (Gotchas), and `build-cloud-image.sh`
  ([`Backend/Deployments/README.md`](Backend/Deployments/README.md)).

`SharedCode/` is compiled twice on purpose: `GameLogic.csproj` builds it against `Metaplay.Client`, and
`Backend/SharedCode/` source-includes the same tree against `Metaplay.Cloud`. Add a file and both pick it up; neither
may reference anything the other cannot see. One project cannot serve both: the SDK compiles its core types into both
assemblies, so the base types the game models derive from are a different .NET type on each side.

## Build & run

Every command runs from this directory unless it says otherwise. `StickyPaws.slnx` is the one solution for the whole
sample, the SDK projects it references included; `dotnet build StickyPaws.slnx` builds everything. `global.json` pins
the .NET SDK feature band.

```bash
metaplay dev server                     # or: dotnet run --project Backend/Server
metaplay dev botclient                  # the load client; needs a running server
dotnet run --project Client/Client.csproj      # → http://localhost:5290; the build generates the serializer
```

In Rider, the checked-in **Server + Client** compound configuration under `.run/` starts both.

The server listens for game clients on 9339 (TCP) and 9380 (WebSocket, which the browser uses), serves the dashboard
and its API on 5550, the public web API on 5560 and the config CDN on 5552. The first run creates a local database.

**Offline mode.** Work that only touches the player loop — meta screens, anything driven by the player model and game
config — needs no server: open <http://localhost:5290/?env=offline> and the session runs against the in-process
offline server and the client's built-in config archive. State persists in browser `localStorage`, written by the
client service when the page goes off screen (the SDK's own write path is an application pause the browser lacks).
Matches never run offline. **Reset** in the top bar deletes the state and the credentials; it is also the remedy when a
`PlayerModel` change leaves an offline account behind, since `GameInitializeNewPlayerModel` never runs again for it.

**In-app links must keep the `?env=` override.** It is read once at startup, so a hardcoded relative `href` drops it
and the next reload boots into a different environment. Build hrefs with `EnvironmentLink.Href(NavigationManager,
path)` and navigate with `EnvironmentLink.NavigateTo(...)`.

**The deployed shape** — publish first; a plain build produces no self-contained `wwwroot`:

```bash
dotnet publish Client/Client.csproj -c Release
metaplay dev server                     # → http://localhost:5560 serves the app
```

`Backend/Server/Config/Options.local.yaml` points `WebClientHosting.WebRootPath` at that output. Cloud images and
deployment are [`Backend/Deployments/README.md`](Backend/Deployments/README.md).

**The dashboard.** Port 5550 serves `Backend/Dashboard/dist/` once it is built, and otherwise
`Backend/PrebuiltDashboard/`, which does not follow edits to `src/`. To work on it (Node.js, pnpm and a running game
server):

```bash
pnpm install                            # at the samples repository root, once
metaplay dev dashboard                  # vite on http://localhost:5551, /api proxied to 5550
metaplay build dashboard                # then http://localhost:5550 serves the built dist/

# From Backend/Dashboard/: the full check — lint (eslint --fix), typecheck, unit tests, build
pnpm lint && pnpm typecheck && pnpm test-unit && pnpm build
```

The sample adds three cards to the stock player page's **Game State** tab — newcomer shield, collection, saved decks —
plus overview items and a card-name decorator. The shield waiver is a `[PlayerDashboardAction]`, so it also gets the
SDK's generated modal in Admin Actions with no dashboard code.

## Tests

Four layers. Add or update tests in the same change that adds or changes the feature.

- **Shared-code unit tests** (`Backend/SharedCode.Tests/`) — actions, model state and every pure policy both sides read.
  The rules suites are in `Engine/`, the match model's wire shape in `Match/`, and the self-play harness in `SelfPlay/`
  ([`Docs/bots.md`](Docs/bots.md#self-play)).
- **Server unit tests** (`Backend/Server.Tests/`) — the server's **pure policies**: the next-attention fold, the
  result-delivery order, seat occupancy, the Heist eligibility subtraction, formation outcomes, the matchmaking policy
  and ticket, rating, stakes tiers, the community ladder and the cross-option invariants. Anything needing a live actor
  is a Playwright fixture.
- **End-to-end tests** (`Client.Tests/`, Playwright) — player-visible behaviour through the real web client. The project
  also compiles the client's **pure** files (`ConnectionTrouble.cs`, `BoardPresentation.cs`, `HandFanGeometry.cs` and
  others, linked in the csproj) against the SDK's `net10.0` flavour, because `ClientBase` and `Client` are browser-only
  and nothing else can reference them.
- **Dashboard tests** (`Backend/Dashboard/`) — `tests/unit/*.test.ts` (vitest) and `tests/e2e/*.spec.ts` (the Node
  Playwright, against the **built** dashboard). They are separate from `run-e2e.sh`.

```bash
dotnet test Backend/SharedCode.Tests/SharedCode.Tests.csproj
dotnet test Backend/Server.Tests/Server.Tests.csproj

# The game config sources: the full build and every content rule, without writing the archives
dotnet run --project Tools/GameConfigGen -- --dry-run

# The deep run: one switch scales every deep-capable suite (the determinism replay, the per-step invariant walk, the
# pacing check and all of self-play, 20,000 games) and takes about a minute. Run it over the whole project when you
# change the RNG order, an iteration order, the deal, the bot policy, the seat view or an action's public half.
STICKYPAWS_DETERMINISM_DEEP=1 dotnet test Backend/SharedCode.Tests/SharedCode.Tests.csproj

# The client's pure tests: no browser, no server
dotnet test Client.Tests/Client.Tests.csproj \
    --filter "FullyQualifiedName~ConnectionTroublePolicyTests|FullyQualifiedName~BoardPresentationTests\
|FullyQualifiedName~BoardImpactTests|FullyQualifiedName~BoardSlotLayoutTests|FullyQualifiedName~CardArtCatalogTests\
|FullyQualifiedName~CollectionArtCoverageTests|FullyQualifiedName~HandFanTests|FullyQualifiedName~HeistServiceTests\
|FullyQualifiedName~LocalDevelopmentPortsTests|FullyQualifiedName~PracticeDeckPickTests\
|FullyQualifiedName~MatchmakingServiceTests|FullyQualifiedName~ModelClockTests|FullyQualifiedName~ManaRampTests\
|FullyQualifiedName~TargetPreviewTests"
```

Playwright needs its browser once: `pwsh Client.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`, or without
PowerShell, after a build, `./.playwright/node/<rid>/node ./.playwright/package/cli.js install chromium` from
`Client.Tests/bin/Debug/net10.0`. The package pins an exact Chromium build; the failure names the one it wants. The
dashboard's Node Playwright needs its own: `npx playwright install chromium` from `Backend/Dashboard`.

**`Client.Tests/run-e2e.sh <log-directory> [match-runs] [matchmaking-runs] [heist-runs]`** (zsh) runs the .NET
end-to-end suites in the one order that works: it builds, starts a game server and a web client, runs the offline,
board and `HomePageTests` fixtures, then `MatchTests` and `MatchmakingTests` as many times as asked, stops the server,
and finishes with the suites that own a server of their own. Each suite's output goes to its own file, and the script
prints each suite's wall time and tally. One fixture by hand, against the server and web client it needs:

```bash
dotnet test Client.Tests/Client.Tests.csproj --settings Client.Tests/playwright.runsettings \
    --filter "FullyQualifiedName~ProgressionTests"
```

### Which fixtures need what

| Fixtures | Needs | In `run-e2e.sh` |
|---|---|---|
| `OfflineModeTests`, `CollectionTests`, `DeckbuilderTests`, `LockTests`, `CollectionPortraitTests` | Web client only, `?env=offline` | Yes |
| `MetagameScreenTests` | Web client only, `?env=offline` | No — run by name |
| `BoardChromeTests`, `BoardEffectsTests`, `BoardInteractionTests`, `BoardPlaySequenceTests`, `BoardReadabilityTests`, `CardFrameCompositionTests`, `DenGuardCueTests` | Web client only, via `/dev/board` | Yes |
| `BoardGeometryCssTests`, `BoardMotionCssTests`, `BoardMotionResizeTests`, `BoardTargetingTests` | A browser only: they inject the real CSS and JS into their own markup | Yes |
| `MatchDriverTests` | A browser only: it tests the fixtures' own driver | No — run by name |
| `HomePageTests` | Server and web client | Yes |
| `MatchTests`, `MatchmakingTests` | Server and web client; `[NonParallelizable]` | Yes, repeatable |
| `ProgressionTests` | Server and web client | No — run by name |
| `MatchAbandonTests`, `HeistTests`, `MatchStrikeTests`, `CommunityTests` | Web client only; each **owns** its game server | Yes, after the shared server stops |
| `BoardScreenshots` | Web client only; `[Explicit]`, a capture tool that also checks every scene builds | Yes, by name |
| The pure tests above | Nothing | No |

Notes on the ones with a reason:

- **`HomePageTests`** plays the real loop over the WebSocket transport. Anything whose point is *the server's own
  persistence* belongs here, since offline mode's browser-local persistence proves nothing about that path.
- **`MatchmakingTests`** drives **two browser contexts** — two credential blobs, so two guest accounts. There is **one
  global queue**, so two taps from two suites inside one fill wait are pooled into one match with two humans in it:
  the product behaving correctly and every "the opponent is a bot" assertion failing. Its fill wait comes from
  `Options.e2e.yaml`, short but never zero, which would make pairing two players impossible.
- **The suites that own a server** start it through `GameServerProcess`, whose `StartAsync` takes runtime-option
  overrides (`Match:TurnDeadline=00:00:05`, no leading dashes) that win over the port set and survive a restart.
  `MatchAbandonTests` restarts the server to kill an ephemeral match, with a real **SIGTERM** via `/bin/kill`
  (`Process.Kill` sends SIGKILL on Unix); `MatchStrikeTests` needs a turn deadline of seconds, which
  `Options.e2e.yaml` cannot have; `HeistTests` shortens the result retry and the pick clock and is two-browser;
  `CommunityTests` owns an isolated SQLite directory and proves the leaderboard persists across a restart.

**Dashboard e2e**, against the built dashboard and a server at the same port offset:

```bash
metaplay build dashboard
cd Backend/Dashboard
DASHBOARD_BASE_URL=http://localhost:5550 API_URL=http://localhost:5550/api pnpm test-e2e
```

A unit test importing from `src/` must be a `*.test.ts`: `tsconfig.node.json`, part of the SDK's template, claims
`tests/**/*.spec.ts` and cannot see `src/`. The e2e loop has no HMR; a card change needs a rebuild first.

### Ports and parallel copies

Two working copies can share a machine by shifting ports. The web client reads an ignored
`Client/wwwroot/appsettings.Development.json` containing `{"LocalServer":{"PortOffset":10000}}`; Development builds
apply it to the localhost TCP, WebSocket and CDN endpoints (19339, 19380, 15552 here), and publish excludes it. Launch
the client with `ASPNETCORE_ENVIRONMENT=Development`, `--no-launch-profile` and a unique `--urls`.

**Write that file before you build, and rebuild if you add it afterwards.** Static web assets are served through a
build-time manifest; a file added later reaches `curl` but not the app's configuration load (the dev server logs *"was
not found in the built time manifest"*), and **the client silently dials the stock ports** — somebody else's server.
The symptom is every live fixture failing on `display-name` or `Connected` while your own server logs no session.

The server needs a matching final `METAPLAY_OPTIONS` source overriding `System.ClientPorts`, `WebSockets.ListenPorts`
and `CdnEmulator.ListenPort`, plus `AdminApi.ListenPort`, `PublicWebApi.ListenPort`, `Clustering.RemotingPort`,
`Environment.SystemHttpPort`, `Environment.MetricPort` and a separate `Clustering.Cookie`.

`STICKYPAWS_PORT_OFFSET` is read by `run-e2e.sh`, which shifts every port it binds, probes and frees (it frees them with
`kill -9`, so a sweep on stock ports under an offset would kill somebody else's server), and by `GameServerProcess`,
which shifts every port its server binds — client listeners, CDN, both web APIs, remoting, readiness (8899), metrics —
and its clustering cookie. `STICKYPAWS_WEB_BASE` overrides which web client every fixture drives (default
`http://localhost:5290`). Pair them:

```bash
STICKYPAWS_PORT_OFFSET=10000 STICKYPAWS_WEB_BASE=http://127.0.0.1:15290 \
    dotnet test Client.Tests/Client.Tests.csproj --no-build \
    --filter "FullyQualifiedName~MatchAbandonTests" --settings Client.Tests/playwright.runsettings
```

The dev-mode dashboard is **not offsettable**: `vite.config.ts`, part of the SDK's template, pins 5551 with
`strictPort` and proxies to 5550. Under an offset, use the built dashboard on 5550 + offset.

### Test-only affordances

`/dev/board?scene=<name>&env=offline` draws the real board in a named state with **no server and no match**: a scene is
a seeded deal driven by the same rule actions and `BotPolicy` the server uses, so it is a state the game could really
be in. Scenes are whatever `BoardScenes.All` lists (`Client/Dev/BoardScenes.cs`); the page refuses to draw outside a
local or offline environment. `BoardScreenshots` saves full-page PNGs of each scene:

```bash
dotnet run --project Client/Client.csproj        # no game server needed
STICKYPAWS_SHOT_DIR=/tmp/shots dotnet test Client.Tests/Client.Tests.csproj \
    --settings Client.Tests/playwright.runsettings --filter "FullyQualifiedName~BoardScreenshots"
```

| Flag | Where | What it does |
|---|---|---|
| `?scene=<name>` | `/dev/board` | Draws a named board state |
| `?animation=<name>` | `/dev/board` | Holds one resolution beat open over a settled scene (`play`, `opponent-trick`, `attack`, `damage`, `heal`, `den-damage`, `death`) |
| `?beat=<ms>`, `?motion=reduced` | Match board | Beat length and reduced motion, so a test can observe a beat |
| `?stale=1` | Match board | Sends an End turn ahead of every intent, so the intent arrives after the turn passed — the only route to the refusal path. Turn it on late, with `ReloadBoardWithAsync`: the helpers' own presses are affected too |
| `?dev=ranks` | Collection | A rank row in card detail sending the development-only `PlayerDevSetCardRank`; how lock fixtures raise cards above `Global.MinLockRank` (`MetaScreenTestBase.SetCardRankAsync`) |
| `?dev=shield` | Home | A button sending the development-only `PlayerDevWaiveNewcomerShield`: the only route for a test or a load bot to a stakes tier that moves ranks |

Each flag is documented at its reader. In-app links keep `?env=` only, so these are dropped by navigation. An account
marked as a developer also gets **Win now → Heist** in the match menu, which ends the game in its favour through the
real result path — ranks and records move for real (`ProgressionTests` uses it).

A newcomer shield on *either* seat shields the match, so two fresh accounts never reach a Heist without `?dev=shield`
on both. The SDK refuses the `PlayerDev*` actions outside a development environment.

### Match pacing

`Backend/Server/Config/Options.local.yaml` carries the `Match:` block any locally run server gets: animation holds at
zero, short bot think delays, and a turn deadline far past any run, because no fixture is about the turn deadline.
`Backend/Server/Config/Options.e2e.yaml` is the second layer, and only the suites get it: it zeroes the bot's think
delay and shortens the mulligan deadline and the fill wait; its comments say what breaks if a value changes. Client
animation is not in it — the client's beat length is `?beat=`.

**Every positive decision clock arms three seconds longer than it reads**, because `Match:PresentationAllowance`
(default 3 s) is added to it: a 60 s turn deadline arms at 63 s. `MatchOptionsInvariantTests` pins it.

The e2e profile reaches the server as the last entry of `METAPLAY_OPTIONS` —
`Config/Options.base.yaml;Config/Options.local.yaml;Config/Options.e2e.yaml` — set by `run-e2e.sh` and by
`GameServerProcess`. `metaplay dev server` sets no such variable, so a fixture driven by hand gets the local pacing.
It is **`METAPLAY_OPTIONS`, not `METAPLAY_EXTRA_OPTIONS`**: the latter appends but binds without the SDK's strict
checks, so a misspelled option silently does not apply; `METAPLAY_OPTIONS` replaces the list — hence the two restated
files — and a typo stops the server. To read back what a running server bound, `curl
http://localhost:5550/api/runtimeOptions`.

### Rules for fixtures

- **Never use `Assert.Inconclusive`.** NUnit counts it in no bucket, not even the total, so it reads like a crash. Use
  `Assert.Ignore`, or better, drive the board to a state where the assertion is reachable.
- **Read a count off a board that has caught up.** End turn goes live as soon as the *model* says the seat is on turn,
  while the presented board may still be playing beats; a count read in between is a count from the past.
  `MatchTestBase.WaitUntilCaughtUpAsync` waits on the turn indicator's `data-trailing`. `data-turn` is presented, not
  authoritative, so where the *server* moved the turn (a lapsed deadline), wait for it to change rather than reading
  it once.
- **One suite at a time.** Two live suites on one machine throttle each other into actor-scheduling delays that look
  like actor faults. Check `pgrep -fl testhost.dll` before a run.
- `Client.Tests/playwright.runsettings` holds the browser settings (chromium, headless, a 5 s expect timeout); flip
  `Headless` or add `SlowMo` there when debugging, and pass it with `--settings`.

## Gotchas

- **The root-level files under `Backend/Dashboard/` are the SDK's dashboard template** — copies of
  `MetaplaySDK/Frontend/DefaultDashboard`, as is `src/main.ts` — and stay identical to it, so an SDK upgrade replaces
  them wholesale. **Everything customizable lives in four places**: `src/`, `tests/`, `package.json`'s `name`, and
  `tsconfig.json` / `tsconfig.app.json`. So the vite dev server keeps its port, and the dashboard has no vitest config
  of its own.
- **Restart the web client after rebuilding it while it runs.** The dev server resolves the app's assets once at
  startup; rebuild underneath it and `/_framework/Client.wasm` returns an empty body — a page that never boots and every
  Playwright test failing at once. Static `wwwroot` files are served live and need no restart.
- **The pre-built WASM serializer is a build artifact.** mono-wasm has no `Reflection.Emit`, so the browser loads a
  serializer built ahead of time: Client.csproj's `GenerateBrowserSerializer` target runs `Tools/SerializerGen` into
  `Client/obj/`, skipped when neither the game types nor the generator changed. Nothing is checked in. If the browser
  reports `Could not load Metaplay.Generated.Browser.dll`, a plain rebuild of Client regenerates it.
- **Regenerate the game config archives whenever a config class or a CSV in `GameConfigSource/` changes.** The server
  refuses to start without `Backend/Server/GameConfig/StaticGameConfig.mpa`, and offline mode serves the client's
  built-in copy; both are checked in, and one command regenerates both:

  ```bash
  dotnet run --project Tools/GameConfigGen
  ```

  A cell that does not parse or a content rule that fails leaves both archives untouched and exits non-zero, naming the
  row; `-- --dry-run` checks without writing. The content rules are `SharedCode/GameConfigs/ContentValidator.cs`, each
  with a negative-control test in `Backend/SharedCode.Tests/ContentValidatorTests.cs` — add both halves together. Every
  config entry needs a source sheet named after it, even an empty one, or the build fails with a
  `FileNotFoundException`.
- **Add an EF Core migration when the database model changes** — on an SDK upgrade, or when the game adds a persisted
  table — or the server refuses to start: `dotnet ef migrations add <Name> --project Backend/Server/Server.csproj`. On
  macOS and Linux this leaves a stray directory literally named `bin\Debug` next to `Backend/Server/bin/`; delete it
  (`.gitignore` has a rule for it).
- **Killing `dotnet run` leaves its child holding the port.** The app it launches is a child process, and killing the
  wrapper leaves it listening; the next run fails to bind, or a readiness probe is answered by the **stale** process
  and every locator fails. Free ports by owner — `for p in 5290 9339 9380 8899; do lsof -ti:$p | xargs -r kill -9; done`
  — before believing a red run; `run-e2e.sh` does this itself. A killed `dotnet test` leaves a `testhost` behind the
  same way (`pkill -f testhost.dll`).
- **The game server needs a database.** It creates and migrates a local SQLite database on first run. Bumping
  `Database.MasterVersion` in `Backend/Server/Config/Options.base.yaml` resets it, which is this sample's answer to
  rows a model change made unloadable and to a new grant that existing accounts would miss
  (`GameInitializeNewPlayerModel` runs once per account). A bump is the answer to *unloadable rows*, not to a table you
  no longer want: dropping a table is a forward migration.

## Conventions

- Explicit typing; avoid `var`. Iterate dictionaries with tuples: `foreach ((KeyType k, ValueType v) in dict)`.
- Comments describe the code's current behaviour — never narrate the change or the prior state.
- Player-visible values that carry no accessible role or label get a `data-testid` so Playwright can locate them;
  otherwise prefer role and text locators.

## Design docs

[`Docs/game-design.md`](Docs/game-design.md) is what the game is. The per-system design documents sit beside it in
`Docs/`, indexed by [`Docs/README.md`](Docs/README.md). Keep them describing the sample as built, and add a new one —
linked from the index — as a system arrives; ideas not built belong in the README's Follow-up work. They stay
high-level: what and why, not how, and never a restatement of code or config specifics.
