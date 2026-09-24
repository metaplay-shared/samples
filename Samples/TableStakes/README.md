# Table Stakes

Table Stakes is a sample game for the [Metaplay SDK](https://metaplay.io). It is a realtime trick-taking card game
for four players. Each player gets five cards, one suit is trump, and the player who wins the most tricks wins. Bots
fill any seat that no person takes. The client is a Blazor WebAssembly app that runs the Metaplay client in the
browser.

> [!NOTE]
> **Built with AI.** The code, tests and docs were written primarily by AI from specs, and people have only
> spot-checked the result. Read the [known issues](#known-issues) before copying a part of the sample into a game.

Around the card game, the sample builds the live-service features of a free-to-play game: currencies, daily rewards,
missions, a spin wheel, a weekly event, a seasonal tournament, cosmetics and a shop.

**Play it in your browser:** a deployment of this sample runs at
<https://public-cycles-beam-quickly-public.p2-eu.metaplay.dev/>. No install or account setup is needed. Bots fill any
seat that no other visitor takes.

The sample shows how to:

- Build a server-authoritative realtime multiplayer game with hidden information on Metaplay's multiplayer entities.
- Run the Metaplay client in the browser with .NET WebAssembly, including an offline mode with no server.
- Implement live-service features, both on the player model and on the SDK's LiveOps Events, Leagues, MetaOffers,
  in-app purchase validation, player segments and analytics.
- Author game config as CSV sheets, validate it at build time, and publish it over the air.
- Add game-specific player data and cards to the LiveOps Dashboard through its Integration API.
- Test the game at several layers, from unit tests to browser tests against a real server.
- Build a server image that contains the web client, and deploy it to a cloud environment.

## Browser-only client

The game has only a browser client. A web client works well with generative AI: an AI agent can build it, run it,
and test and inspect it in a browser without a game engine editor. The sample is not intended to be published on app
stores, so it has no iOS or Android builds.

## Run it

### Prerequisites

- The .NET SDK version in [`Backend/global.json`](Backend/global.json), or a later feature band of the same major
  version.
- The [Metaplay CLI](https://github.com/metaplay/cli), for `metaplay dev server`, `metaplay dev botclient`, image
  builds and deploys.
- The `wasm-tools` workload, only to publish the web client (`dotnet workload install wasm-tools`). A normal build
  and `dotnet run` do not need it.

The test suites have further requirements. See [`docs/testing.md`](docs/testing.md).

### Game server and web client

Run every command from the `Samples/TableStakes` directory.

```bash
metaplay dev server                               # terminal 1: the game server
dotnet run --project WebClient/WebClient.csproj   # terminal 2: the web client
```

Open <http://localhost:5290> and tap **Play**. For the game's LiveOps Dashboard, run `metaplay dev dashboard` in a
third terminal and open <http://localhost:5551>. It needs Node and pnpm at the versions in
`MetaplaySDK/version.yaml`.

### Offline mode

Open <http://localhost:5290/?env=offline> with only the web client running. The browser runs the match entity
in-process through the SDK's offline server, with the same game logic, bots and serializer. It is a development
and test tool. See [`docs/web-client.md`](docs/web-client.md#offline-mode).

### Bot client

```bash
metaplay dev botclient                                # bot clients, as many as Options.base.yaml sets
metaplay dev botclient -- -MaxBots=40 -MaxBotId=200   # a larger load run
```

Bot clients queue, get seated and play whole games over the real protocol. The matchmaker treats them as people,
not as bot seats.

## Reading order

If you are new to the project, read these first, in this order:

1. [`docs/architecture.md`](docs/architecture.md): the projects, the server entities, game config, the web client
   and deployment.
2. [`docs/player.md`](docs/player.md): the player model, the kinds of player action, and the match-completion fact
   that most features build on.
3. [`docs/game-rules.md`](docs/game-rules.md), then [`docs/match.md`](docs/match.md): the rules, then how a match
   runs on a multiplayer entity with hidden hands.
4. [`docs/web-client.md`](docs/web-client.md): how the Metaplay client runs in the browser.
5. [`docs/game-config.md`](docs/game-config.md): how config is authored, built, validated and published.

After that, read the docs for the features that interest you, in any order. Each doc names the docs it builds on
under its title.

To work on the code, read [`AGENTS.md`](AGENTS.md) and [`docs/testing.md`](docs/testing.md).

## Documentation

### The game and the server

| Doc | Covers | Metaplay features shown |
|---|---|---|
| [`architecture.md`](docs/architecture.md) | Projects, shared code, server entities, endpoints, runtime options, web client hosting, deployment | Server programming, runtime options, PublicWebApi, database migrations, a custom LiveOps Dashboard, image build and deploy |
| [`game-rules.md`](docs/game-rules.md) | The card game's rules, and which information is public or private | None. Background for the match doc |
| [`player.md`](docs/player.md) | `PlayerModel`, player actions, names, match history, the match-completion fact | `PlayerModelBase`, client actions, synchronized and unsynchronized server actions, schema migrations, `PlayerRequirementsValidator` |
| [`match.md`](docs/match.md) | The match entity: public and private state, turn flow, timers, disconnects, persistence, cleanup | `MultiplayerModelBase`, `PersistedMultiplayerEntityActorBase`, per-member private state, entity associations |
| [`matchmaking.md`](docs/matchmaking.md) | The matchmaker, table formation and bot fill | Singleton service entities, entity asks |
| [`bots.md`](docs/bots.md) | How bot seats choose cards, bot profiles and names | Game config libraries |

### The client

| Doc | Covers | Metaplay features shown |
|---|---|---|
| [`web-client.md`](docs/web-client.md) | The Blazor WebAssembly client: environments, connection handling, serializer, offline mode, trimming, hosting and updates after a deploy | `Metaplay.Client` on `net10.0-browser`, WebSocket transport, pre-built serializer, the SDK's offline server |
| [`meta-shell.md`](docs/meta-shell.md) | The screens around the card table and how they get their state | Reading `PlayerModel` and running player actions from UI code |

### Live-service features

| Doc | Covers | Built on |
|---|---|---|
| [`game-config.md`](docs/game-config.md) | Config classes, CSV sheets, validation, the build tool, publishing | `SharedGameConfigBase`, CSV build sources, build-time validation, over-the-air publishing |
| [`economy.md`](docs/economy.md) | Currencies, the wallet, sources and sinks | Game code on `PlayerModel` |
| [`daily-rewards.md`](docs/daily-rewards.md) | One reward claim per player-local day | Game code on `PlayerModel`, a synchronized server action |
| [`first-week-event.md`](docs/first-week-event.md) | A personal event for a new player's first week | Game code on `PlayerModel`, the match-completion fact |
| [`missions.md`](docs/missions.md) | Daily and weekly missions | Game code on `PlayerModel`, the match-completion fact |
| [`spin-wheel.md`](docs/spin-wheel.md) | A prize wheel paid for with spin tokens | Game code on `PlayerModel`, server-side randomness |
| [`weekly-event.md`](docs/weekly-event.md) | A weekly themed event, and seeding weeks onto the LiveOps timeline | LiveOps Events |
| [`seasonal-tournament.md`](docs/seasonal-tournament.md) | A weekly points race in groups | Leagues |
| [`cosmetics.md`](docs/cosmetics.md) | The cosmetic catalogue, the wardrobe, and cosmetics shown to other players | Game code on `PlayerModel`, league avatars |
| [`offers.md`](docs/offers.md) | Shop offers, in-app purchases, and the player segments that target offers | MetaOffers, offer groups, the Development purchase platform, player segments, typed player properties |
| [`analytics.md`](docs/analytics.md) | Analytics events, keywords and correlation ids | `PlayerEventBase`, analytics event customizations |

### Working on the project

| Doc | Covers |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Commands, tests, gotchas and conventions |
| [`docs/testing.md`](docs/testing.md) | The test layers and the E2E harness |
| [`WebClient/README.md`](WebClient/README.md), [`GameConfigSource/README.md`](GameConfigSource/README.md), [`tools/GameConfigGen/README.md`](tools/GameConfigGen/README.md), [`Backend/Server.Tests/README.md`](Backend/Server.Tests/README.md), [`Backend/Server/Config/README.md`](Backend/Server/Config/README.md) | What each directory is, with links to the docs above |

## Known issues

The project was written primarily by AI from specs, and people have only spot-checked it. It may have shortcomings
beyond the ones listed here. Read these before copying a part of the sample into a game.

### Scalability

The server has not been load tested beyond small bot client runs, and it is not built for a large player count:

- **The matchmaker is one entity that forms one table at a time**, so it limits how many tables the game can start.
- **Match rows are kept for good.** Nothing deletes the `Matches` table, so it grows with every game played
  ([`match.md`](docs/match.md#waking-and-retention)).
- **The game server hosts the web client.** A real game serves its client from a CDN, so the client can be updated
  separately from the server and a deploy does not have every player download it from the game server at once
  ([`web-client.md`](docs/web-client.md#serving-the-client)).

### Other limits

- **A changed hand is sent as a separate message to its player, tagged with a play index.** SDK 38 cannot order a
  per-player payload against the replicated timeline, so the client holds a delivered hand until its board reaches
  that play index. A later SDK
  release replaces this with a per-member timeline action ([`match.md`](docs/match.md#delivering-a-hand)).
- **There are no real payments.** Purchases use the SDK's Development platform: the client writes its own receipt,
  and the server accepts it only with development features enabled. No store or web payment provider is wired
  ([`offers.md`](docs/offers.md)).
- **Accounts are guest accounts only.** The credential is stored in the browser, so clearing site data or switching
  device starts a new account. There is no account linking, social login or recovery
  ([`web-client.md`](docs/web-client.md#connection-service)).
- **The published client formats dates and numbers in the invariant culture.** It ships no ICU data. A game that
  formats per locale keeps ICU
  ([`web-client.md`](docs/web-client.md#the-published-client)).
- **Offline mode is a development tool.** It cannot exercise connection loss, reconnects, disconnect grace, the
  association handshake or server restarts ([`web-client.md`](docs/web-client.md#offline-mode)).

## Project layout

| Path | Contents |
|---|---|
| `SharedCode/` | Game logic shared by the server and the client (namespace `Game.Logic`) |
| `Backend/SharedCode/` | Compiles `SharedCode/` for the server against `Metaplay.Cloud` |
| `Backend/Server/` | The game server |
| `Backend/BotClient/` | The load-test bot client |
| `Backend/SharedCode.Tests/`, `Backend/Server.Tests/` | NUnit unit tests |
| `Backend/Dashboard/` | The custom LiveOps Dashboard (Vue, built on the SDK's dashboard packages) |
| `Backend/Deployments/` | Helm values for cloud deployments |
| `SharedCode/SharedCode.Client.csproj` | Compiles `SharedCode/` for the web client against `Metaplay.Client` |
| `WebClientBase/` | The game-independent Blazor host, connection service and shared components |
| `WebClient/` | The game's Blazor WebAssembly app |
| `WebClient.Tests/` | Pure, render and browser tests for the web client |
| `GameConfigSource/` | CSV sheets the game config is built from |
| `tools/GameConfigGen/` | The game config build tool |
| `tools/SerializerGen/` | Generates the pre-built serializer the web client loads |
| `tools/ServerImageBuild.cs` | Builds the server image with the web client in it |
| `tools/run-e2e.sh`, `tools/run-e2e.py` | The E2E test harness |
| `docs/` | Documentation |
