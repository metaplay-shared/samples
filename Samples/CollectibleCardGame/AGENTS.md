# AGENTS.md

How to develop this sample. This is the canonical dev guide. For what the sample is, see [`README.md`](README.md); for
the game design, [`Docs/game-design.md`](Docs/game-design.md); for the per-feature architecture,
[`Docs/README.md`](Docs/README.md). For SDK concepts and APIs, use the Metaplay docs.

Sticky Paws is built on the
[Blazor starter project](https://github.com/metaplay-shared/blazor-starter-project). Its
[`AGENTS.md`](https://github.com/metaplay-shared/blazor-starter-project/blob/main/AGENTS.md) and
[docs](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/project-structure.md) cover the
project structure, the web client's base, the browser test harness, deploying, and the shared gotchas and conventions.
This file covers what is Sticky Paws' own.

Markdown in this sample is hard-wrapped at 120 columns.

## The SDK

The sample builds against the Metaplay SDK in `MetaplaySDK`. Two consequences shape everything below:

- **Never modify anything under `MetaplaySDK/`.** It is the installed SDK; a change there is lost on the next install
  and is not part of the sample.
- **The analyzers come from `Directory.Build.props`.** The sample's one MSBuild configuration is the
  `Directory.Build.props` at its root, which applies to the backend, the client, the shared code and the tools alike.
  It imports `Metaplay.Analyzers.targets`, so the projects get the source generators from the SDK's prebuilt DLLs; a
  new project needs nothing for them. The server image build copies the file along with `Backend/`.

## Layout

- **`SharedCode/`** — namespace `Game.Logic`. Player model, actions, config classes, type codes, the match model with
  its rules and actions (`Match/`) and the bot policy (`Match/Bots/`). Deterministic; no server-only or client-only
  types.
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
- **`WebClientBase/`** — the starter project's game-independent web client base; the game keeps it as it is.
- **`WebClient/`** — namespace `WebClient`. The game's Blazor WebAssembly app: the board, the collection, the Heist.
- **`WebClient.Tests/`** — Playwright end-to-end tests.
- **`WebClient.UnitTests/`** — the client's pure policy tests, no browser.
- **`tools/`** — `SerializerGen` and `GameConfigGen` (Gotchas).

`SharedCode/` is compiled twice, as in the starter project: `SharedCode/SharedCode.Client.csproj` builds it against
`Metaplay.Client`, and `Backend/SharedCode/` source-includes the same tree against `Metaplay.Cloud`
([project structure](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/project-structure.md)).

## Build & run

Every command runs from this directory unless it says otherwise. `StickyPaws.slnx` is the one solution for the whole
sample, the SDK projects it references included; `dotnet build StickyPaws.slnx` builds everything. `global.json` pins
the .NET SDK feature band.

```bash
metaplay dev server                     # or: dotnet run --project Backend/Server
metaplay dev botclient                  # the load client; needs a running server
dotnet run --project WebClient/WebClient.csproj   # → http://localhost:5290; the build generates the serializer
```

In Rider, the checked-in **Server + Client** compound configuration under `.run/` starts both.

The server listens for game clients on 9339 (TCP) and 9380 (WebSocket, which the browser uses), serves the dashboard
and its API on 5550, the public web API on 5560 and the config CDN on 5552. The first run creates a local database.

**Offline mode.** Work that only touches the player loop — meta screens, anything driven by the player model and game
config — needs no server: open <http://localhost:5290/?offline> and the session runs against the in-process
offline server and the client's built-in config archive. The offline player is kept in the browser between reloads
([offline mode](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/web-client.md#offline-mode)).
Matches never run offline. **Reset** in the top bar deletes the state and the credentials; it is also the remedy when a
`PlayerModel` change leaves an offline account behind, since `GameInitializeNewPlayerModel` never runs again for it.

**In-app links are plain paths.** The client reads its start query once
([the start query](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/web-client.md#the-start-query)),
and the game reads its own switches once too, through `WebClient/Services/StartupQuery.cs`.

**The published client** — `python3 run_e2e.py --trimmed --no-test` serves the trimmed client and prints its URL.
Deployment is [`Backend/Deployments/README.md`](Backend/Deployments/README.md).

**The dashboard.** Port 5550 serves `Backend/Dashboard/dist/` once it is built, and otherwise
`Backend/PrebuiltDashboard/`, which does not follow edits to `src/`. To work on it (Node.js, pnpm and a running game
server):

```bash
pnpm install                            # once
metaplay dev dashboard                  # vite on http://localhost:5551, /api proxied to 5550
metaplay build dashboard                # then http://localhost:5550 serves the built dist/

# From Backend/Dashboard/: the full check — lint (eslint --fix), typecheck, unit tests, build
pnpm lint && pnpm typecheck && pnpm test-unit && pnpm build
```

The sample adds three cards to the stock player page's **Game State** tab — newcomer shield, collection, saved decks —
plus overview items and a card-name decorator. The shield waiver is a `[PlayerDashboardAction]`, so it also gets the
SDK's generated modal in Admin Actions with no dashboard code.

## Tests

Five layers. Add or update tests in the same change that adds or changes the feature.

- **Shared-code unit tests** (`Backend/SharedCode.Tests/`) — actions, model state and every pure policy both sides read.
  The rules suites are in `Engine/`, the match model's wire shape in `Match/`, and the self-play harness in `SelfPlay/`
  ([`Docs/bots.md`](Docs/bots.md#self-play)).
- **Server unit tests** (`Backend/Server.Tests/`) — the server's **pure policies**: the next-attention fold, the
  result-delivery order, seat occupancy, the Heist eligibility subtraction, formation outcomes, the matchmaking policy
  and ticket, rating, stakes tiers, the community ladder and the cross-option invariants. Anything needing a live actor
  is a Playwright fixture.
- **Client unit tests** (`WebClient.UnitTests/`) — the client's **pure** files (`BoardPresentation.cs`,
  `HandFanGeometry.cs` and others, linked in the csproj), compiled against the SDK's `net10.0` flavour because
  `WebClientBase` and `WebClient` are browser-only and nothing else can reference them. A new pure file gets linked
  there, and its tests go there too.
- **End-to-end tests** (`WebClient.Tests/`, Playwright) — player-visible behaviour through the real web client.
- **Dashboard tests** (`Backend/Dashboard/`) — `tests/unit/*.test.ts` (vitest) and `tests/e2e/*.spec.ts` (the Node
  Playwright, against the **built** dashboard). They are separate from `run_e2e.py`.

```bash
dotnet test Backend/SharedCode.Tests/SharedCode.Tests.csproj
dotnet test Backend/Server.Tests/Server.Tests.csproj

# The game config sources: the full build and every content rule, without writing the archives
dotnet run --project tools/GameConfigGen -- --dry-run

# The deep run: one switch scales every deep-capable suite (the determinism replay, the per-step invariant walk, the
# pacing check and all of self-play, 20,000 games) and takes about a minute. Run it over the whole project when you
# change the RNG order, an iteration order, the deal, the bot policy, the seat view or an action's public half.
STICKYPAWS_DETERMINISM_DEEP=1 dotnet test Backend/SharedCode.Tests/SharedCode.Tests.csproj

# The client's pure tests: no browser, no server
dotnet test WebClient.UnitTests/WebClient.UnitTests.csproj
```

The browser tests install their Playwright browser before the first test. The dashboard's Node Playwright needs its
own: `npx playwright install chromium` from `Backend/Dashboard`.

**`python3 run_e2e.py`** is the starter project's
[harness](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/testing.md#the-e2e-harness). The phases in
[`e2e.toml`](e2e.toml) run the fixtures on the shared server first, then stop it, and finish with the suites that own a
server of their own.

```bash
python3 run_e2e.py                                     # every suite
python3 run_e2e.py --phase MatchTests                  # one phase, by its name in e2e.toml
python3 run_e2e.py --output-dir /tmp/e2e               # keep the logs (otherwise kept only on failure)
python3 run_e2e.py --serve                             # leave the server and web client running; --stop stops them
```

One fixture by hand, against the server and web client it needs:

```bash
dotnet test WebClient.Tests/WebClient.Tests.csproj --settings WebClient.Tests/playwright.runsettings \
    --filter "FullyQualifiedName~ProgressionTests"
```

### Which fixtures need what

| Fixtures | Needs | In `run_e2e.py` |
|---|---|---|
| `CollectionTests`, `DeckbuilderTests`, `LockTests`, `CollectionPortraitTests` | Web client only, `?offline` | Yes |
| `MetagameScreenTests` | Web client only, `?offline` | Yes |
| `BoardChromeTests`, `BoardEffectsTests`, `BoardInteractionTests`, `BoardPlaySequenceTests`, `BoardReadabilityTests`, `CardFrameCompositionTests`, `DenGuardCueTests` | Web client only, via `/dev/board` | Yes |
| `BoardGeometryCssTests`, `BoardMotionCssTests`, `BoardMotionResizeTests`, `BoardTargetingTests` | A browser only: they inject the real CSS and JS into their own markup | Yes |
| `HomePageTests` | Server and web client | Yes |
| `MatchTests`, `MatchmakingTests`, `MatchStrikeTests` | Server and web client; `[NonParallelizable]` | Yes |
| `ProgressionTests` | Server and web client | Yes |
| `MatchAbandonTests`, `TransportOutageTests`, `HeistTests`, `CommunityTests` | Web client only; each **owns** its game server | Yes, after the shared server stops |
| `BoardScreenshots` | Web client only; `[Explicit]`, a capture tool that also checks every scene builds | No; run it by name |
| `WebClient.UnitTests` | Nothing | No |

Notes on the ones with a reason:

- **`HomePageTests`** plays the real loop over the WebSocket transport. Anything whose point is *the server's own
  persistence* belongs here, since offline mode's browser-local persistence proves nothing about that path.
- **`MatchmakingTests`** drives **two browser contexts** — two credential blobs, so two guest accounts. There is **one
  global queue**, so two taps from two suites inside one fill wait are pooled into one match with two humans in it:
  the product behaving correctly and every "the opponent is a bot" assertion failing. Its fill wait comes from
  `Options.e2e.yaml`, short but never zero, which would make pairing two players impossible.
- **The suites that own a server** start it through `GameServerProcess`, whose `StartAsync` takes runtime-option
  overrides (`Session:SessionLingerDuration=00:00:00`, no leading dashes) that win over the port set and survive a
  restart. `MatchAbandonTests` restarts the server to kill an ephemeral match, with a real **SIGTERM** via
  `/bin/kill` (`Process.Kill` sends SIGKILL on Unix); `TransportOutageTests` sets `Session:SessionLingerDuration` to
  zero, so the WebSocket it closes from the page always ends in a new session; `HeistTests` shortens the result
  retry and the pick clock and is two-browser;
  `CommunityTests` owns an isolated SQLite directory and proves the leaderboard persists across a restart; it turns
  off the `Community:` request and sample limits and polls Home with `?poll=`, so each count it reads moves within a
  second, and its fill wait is three seconds. The online count still trails a session start by up to the SDK's
  fixed ten-second stats tick, so it is the last count the fixture checks. `GameServerProcess` starts every server
  with `Environment:EnableKeyboardInput=false`: the SDK's console poll blocks on the test runner's redirected input,
  and a server blocked there logs a SIGTERM and never shuts down.

**Dashboard e2e**, against the built dashboard and a server at the same port offset:

```bash
metaplay build dashboard
cd Backend/Dashboard
DASHBOARD_BASE_URL=http://localhost:5550 API_URL=http://localhost:5550/api pnpm test-e2e
```

A unit test importing from `src/` must be a `*.test.ts`: `tsconfig.node.json`, part of the SDK's template, claims
`tests/**/*.spec.ts` and cannot see `src/`. The e2e loop has no HMR; a card change needs a rebuild first.

### Ports and parallel copies

`run_e2e.py` gives each listener a free port and passes them to the fixtures in `E2E_*` environment variables, so two
checkouts can run it at once. The suites that own a server (`GameServerProcess`) start it with the harness's
`E2E_SERVER_PORT_ARGS`, so it binds the ports the web client already dials. Run without the harness, the fixtures use
the stock ports. For a second stack by hand, `python3 run_e2e.py --serve --no-test` starts one on free ports and
prints its URL.

### Test-only affordances

`/dev/board?scene=<name>&offline` draws the real board in a named state with **no server and no match**: a scene is
a seeded deal driven by the same rule actions and `BotPolicy` the server uses, so it is a state the game could really
be in. Scenes are whatever `BoardScenes.All` lists (`WebClient/Dev/BoardScenes.cs`); the page refuses to draw outside a
local or offline environment. `BoardScreenshots` saves full-page PNGs of each scene:

```bash
dotnet run --project WebClient/WebClient.csproj   # no game server needed
STICKYPAWS_SHOT_DIR=/tmp/shots dotnet test WebClient.Tests/WebClient.Tests.csproj \
    --settings WebClient.Tests/playwright.runsettings --filter "FullyQualifiedName~BoardScreenshots"
```

| Flag | Where | What it does |
|---|---|---|
| `?scene=<name>` | `/dev/board` | Draws a named board state |
| `?clock=<seconds>` | `/dev/board` | Poses the deadline in force with that many seconds left, so the deadline ring can be read at any point of its window (`BoardChromeTests`) |
| `?animation=<name>` | `/dev/board` | Holds one resolution beat open over a settled scene (`play`, `opponent-trick`, `attack`, `damage`, `heal`, `den-damage`, `death`) |
| `?beat=<ms>`, `?motion=reduced` | Match board | Beat length and reduced motion, so a test can observe a beat |
| `?stale=1` | Match board | Sends an End turn ahead of every intent, so the intent arrives after the turn passed — the only route to the refusal path. Turn it on late, with `ReloadBoardWithAsync`: the helpers' own presses are affected too |
| `?dev=ranks` | Collection | A rank row in card detail sending the development-only `PlayerDevSetCardRank`; how lock fixtures raise cards above `Global.MinLockRank` (`MetaScreenTestBase.SetCardRankAsync`). A row above the grid sends `PlayerDevSetAllCardRanks`, every unlocked card at once (`HeistTests`) |
| `?dev=shield` | Home | A button sending the development-only `PlayerDevWaiveNewcomerShield`: the only route for a test or a load bot to a stakes tier that moves ranks |
| `?poll=<ms>` | Home | The leaderboard and activity refresh, shortened from ten seconds (`CommunityTests`). Held by the client service, so it outlives in-app navigation |
| `?dev=gift` | Home | A button sending the development-only `PlayerDevSetCardGiftProgress`, one match short of the next card gift (`ProgressionTests`) |

Each flag is documented at its reader. The client reads them once, when it starts (`StartupQuery`), so they hold on
pages reached later. An account
marked as a developer also gets **Win now → Heist** in the match menu, which ends the game in its favour through the
real result path — ranks and records move for real (`ProgressionTests` uses it) — and **Run out the clock**, which
lapses the deadline in force now, whichever seat it is for (`MatchStrikeTests` lapses the other seat's turns with it).

A newcomer shield on *either* seat shields the match, so two fresh accounts never reach a Heist without `?dev=shield`
on both. The SDK refuses the `PlayerDev*` actions outside a development environment.

### Match pacing

`Backend/Server/Config/Options.local.yaml` carries the `Match:` block any locally run server gets: animation holds at
zero, short bot think delays, and a turn deadline far past any run, because no fixture is about the turn deadline.
`Backend/Server/Config/Options.e2e.yaml` is the second layer, and only the suites get it: it zeroes the bot's think
delay, shortens the mulligan deadline and the fill wait, and deals every table from fixed seeds
(`Match:FixedDealSeed`, `Match:FixedBotSeed`), so each run plays the same game; its comments say what breaks if a value
changes. Client animation is not in it — the client's beat length is `?beat=`.

**Every positive decision clock arms three seconds longer than it reads**, because `Match:PresentationAllowance`
(default 3 s) is added to it: a 60 s turn deadline arms at 63 s.

The e2e profile reaches the server as the last entry of `METAPLAY_OPTIONS` —
`Config/Options.base.yaml;Config/Options.local.yaml;Config/Options.e2e.yaml` — set by `run_e2e.py` and by
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
- `WebClient.Tests/playwright.runsettings` holds the browser settings (chromium, headless, a 5 s expect timeout); flip
  `Headless` or add `SlowMo` there when debugging, and pass it with `--settings`.

## Gotchas

- **The root-level files under `Backend/Dashboard/` are the SDK's dashboard template** — copies of
  `MetaplaySDK/Frontend/DefaultDashboard`, as is `src/main.ts` — and stay identical to it, so an SDK upgrade replaces
  them wholesale. **Everything customizable lives in four places**: `src/`, `tests/`, `package.json`'s `name`, and
  `tsconfig.json` / `tsconfig.app.json`. So the vite dev server keeps its port, and the dashboard has no vitest config
  of its own.
- **Regenerate the game config archives whenever a config class or a CSV in `GameConfigSource/` changes.** The server
  refuses to start without `Backend/Server/GameConfig/StaticGameConfig.mpa`, and offline mode serves the client's
  built-in copy; both are checked in, and one command regenerates both:

  ```bash
  dotnet run --project tools/GameConfigGen
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
- **The game server needs a database.** It creates and migrates a local SQLite database on first run. Bumping
  `Database.MasterVersion` in `Backend/Server/Config/Options.base.yaml` resets it, which is this sample's answer to
  rows a model change made unloadable and to a new grant that existing accounts would miss
  (`GameInitializeNewPlayerModel` runs once per account). A bump is the answer to *unloadable rows*, not to a table you
  no longer want: dropping a table is a forward migration.

## Conventions

The starter project's [conventions](https://github.com/metaplay-shared/blazor-starter-project/blob/main/AGENTS.md#conventions) apply.

## Design docs

[`Docs/game-design.md`](Docs/game-design.md) is what the game is. The per-system design documents sit beside it in
`Docs/`, indexed by [`Docs/README.md`](Docs/README.md). Keep them describing the sample as built, and add a new one —
linked from the index — as a system arrives; ideas not built are not documented. They stay high-level: what and why,
not how, and never a restatement of code or config specifics.
