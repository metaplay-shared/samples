# Architecture

[Documentation index](../README.md#documentation)

This doc describes how Table Stakes is put together on the Metaplay SDK: its projects, how the shared game code is
compiled for the server and the client, the server's entities, the web client and how the server hosts it, and
deployment. The feature docs linked below cover each system in depth. Commands are in [`AGENTS.md`](../AGENTS.md).

## Projects

| Project | Role |
|---|---|
| [`Backend/SharedCode`](../Backend/SharedCode/SharedCode.csproj) | Compiles `SharedCode/` for the server |
| [`Backend/Server`](../Backend/Server/Server.csproj) | The game server |
| [`Backend/BotClient`](../Backend/BotClient/BotClient.csproj) | The load-test bot client |
| [`Backend/Dashboard`](../Backend/Dashboard) | The LiveOps Dashboard ([LiveOps Dashboard](#liveops-dashboard)) |
| [`SharedCode/SharedCode.Client`](../SharedCode/SharedCode.Client.csproj) | Compiles `SharedCode/` for the client |
| [`WebClientBase`](../WebClientBase/WebClientBase.csproj) | Game-independent Blazor host, connection service and components |
| [`WebClient`](../WebClient/WebClient.csproj) | The game's Blazor WebAssembly app |
| [`tools/GameConfigGen`](../tools/GameConfigGen/GameConfigGen.csproj) | Builds the game config archives ([`game-config.md`](game-config.md)) |
| [`tools/SerializerGen`](../tools/SerializerGen/SerializerGen.csproj) | Generates the pre-built serializer the web client loads |
| `Backend/SharedCode.Tests`, `Backend/Server.Tests`, `WebClient.Tests` | Tests ([`testing.md`](testing.md)) |

`WebClient` and `WebClientBase` target `net10.0-browser`, so a `net10.0` project cannot reference them.
`WebClient.Tests` compiles their sources in instead.

## Shared code

`SharedCode/` holds the game logic that runs on both the server and the client: models, actions, game config types,
the match rules and turn flow, bot policy, and the type code registries in `SharedCode/TypeCodes/`. It must be
deterministic, because the server and the client run the same actions and compare the results. Its namespace is
`Game.Logic`, which [`GlobalOptions.cs`](../SharedCode/GlobalOptions.cs) declares as the shared namespace.
`GlobalOptions` also turns on the SDK's player leagues for the seasonal tournament.

The same source tree is compiled into two assemblies:

- **Server.** `Backend/SharedCode/SharedCode.csproj` includes the shared sources and references `Metaplay.Cloud`.
- **Client.** `SharedCode/SharedCode.Client.csproj` compiles the directory it sits in and references `Metaplay.Client`. It
  is a real assembly, not source compiled into `WebClient`, because the pre-built serializer refers to game types by
  assembly identity. Its `net10.0-browser` build is the one the web client uses, and its `net10.0` build is the one
  `tools/SerializerGen` loads.

The two builds cannot be one assembly, because `Metaplay.Cloud` and `Metaplay.Client` each compile the SDK's core
types into themselves. The server project excludes the client project's `bin/` and `obj/` from its source glob.

## Server

### Entities

| Entity | Actor | Role | Doc |
|---|---|---|---|
| Player | [`PlayerActor`](../Backend/Server/Player/PlayerActor.cs) | The player's state and every player-facing request | [`player.md`](player.md) |
| Match | [`MatchActor`](../Backend/Server/Match/MatchActor.cs) | One table, persisted so a game survives a redeploy | [`match.md`](match.md) |
| Matchmaker | [`MatchmakerActor`](../Backend/Server/Matchmaking/MatchmakerActor.cs) | One queue per cluster that forms tables | [`matchmaking.md`](matchmaking.md) |
| Weekly event seeder | [`WeeklyEventSeederActor`](../Backend/Server/WeeklyEvent/WeeklyEventSeederActor.cs) | Keeps upcoming weekly events on the LiveOps timeline | [`weekly-event.md`](weekly-event.md) |
| League manager and divisions | [`TournamentLeagueManagerActor`](../Backend/Server/Tournament/TournamentLeagueManagerActor.cs), [`TournamentDivisionActor`](../Backend/Server/Tournament/TournamentDivisionActor.cs) | The seasonal tournament on SDK Leagues | [`seasonal-tournament.md`](seasonal-tournament.md) |

The session actor is the SDK's, with no changes.

The client reaches the match and the tournament division through its session. `PlayerActor` associates the player's
current table with the session, the SDK's league integration does the same for the division, and the client's
sub-clients attach to them.

### Database

[`GameDbContext`](../Backend/Server/Database/GameDbContext.cs) declares no tables of its own. The game's persisted
types, `PersistedMatch` and `PersistedTournamentDivision`, register through their `[Table]` attributes. The SDK throws
at startup if a type is also mapped with a `DbSet` in `GameDbContext`. EF Core migrations are in
`Backend/Server/Migrations/`. A change to a persisted type's columns, or an SDK upgrade that changes the SDK's tables,
needs a new migration.

### HTTP endpoints

The game's endpoints run on the public, unauthenticated PublicWebApi host. The `test/` routes, which the E2E suite
uses to force match timers and to create or seed weekly events, answer 404 unless `TestRoutes:Enabled` is on
([`testing.md`](testing.md), [`weekly-event.md`](weekly-event.md)). It is off in every environment, and only the E2E
harness turns it on. Neither the environment type nor `Environment:EnableDevelopmentFeatures` is the gate, because
both are on in deployed development environments that anyone can reach. A catch-all route serves the web client
([Web client hosting](#web-client-hosting)).

### LiveOps Dashboard

The project has a custom LiveOps Dashboard in [`Backend/Dashboard`](../Backend/Dashboard). It is the SDK's dashboard
app with game additions registered through the Integration API in
[`gameSpecific.ts`](../Backend/Dashboard/src/gameSpecific.ts): the wallet, the lifetime record and the current table
on the player overview, and cards for the player's meta systems, match history and cosmetics. See
[Customizing the LiveOps Dashboard frontend](https://docs.metaplay.io/liveops-dashboard/how-to-guides/customizing-the-liveops-dashboard-frontend).

Only `src/`, `tests/`, `scripts/` and the two `tsconfig` files under `Backend/Dashboard/` belong to this project. The
rest is the SDK's dashboard template and must match `MetaplaySDK/Frontend/DefaultDashboard`, because the SDK's
frontend packages are compiled from source with the dashboard's own tools. After an SDK update, copy the files in the
root of `DefaultDashboard` and its `package.json` (keeping this project's `name`) into `Backend/Dashboard/`, then run
`pnpm install`. If they do not match, lint or typecheck fails inside SDK components, or prettier fails with "Missing
visitor keys".

The cards read the player model as the Admin API serializes it: C# member names in camelCase, with private
`[MetaMember]` fields keeping their underscore prefix. Neither the C# build nor the dashboard typecheck compares them
with the C# model, so a renamed member shows up as an empty or zero value on the dashboard.

The project has no AdminApi controllers of its own. The server adds information to the Dashboard through SDK hooks,
such as display names for tables and tournament groups, the price text of wallet-priced offers, detail strings from
the league code, analytics keywords, and descriptions on runtime options.

### Runtime options

Each server system that needs tuning has its own runtime options section. Timings a table uses reach the shared
match engine through `MatchTimings` ([`match.md`](match.md)).

| Section | Class | Controls | Doc |
|---|---|---|---|
| `Match` | [`MatchOptions`](../Backend/Server/Match/MatchOptions.cs) | Table timings | [`match.md`](match.md) |
| `Matchmaking` | [`MatchmakingOptions`](../Backend/Server/Matchmaking/MatchmakingOptions.cs) | Fill wait and ask timeouts | [`matchmaking.md`](matchmaking.md) |
| `Tournament` | [`TournamentOptions`](../Backend/Server/Tournament/TournamentOptions.cs) | Season schedule | [`seasonal-tournament.md`](seasonal-tournament.md) |
| `WeeklyEventSeeding` | [`WeeklyEventSeedingOptions`](../Backend/Server/WeeklyEvent/WeeklyEventSeedingOptions.cs) | Seeding horizon and pass schedule | [`weekly-event.md`](weekly-event.md) |
| `WebClientHosting` | [`WebClientHostingOptions`](../Backend/Server/WebClientHosting/WebClientHostingOptions.cs) | The directory the web client is served from | [Web client hosting](#web-client-hosting) |
| `TestRoutes` | [`TestRoutesOptions`](../Backend/Server/TestRoutes/TestRoutesOptions.cs) | Whether the `test/` routes answer | [HTTP endpoints](#http-endpoints) |

The options files and the environments that use them are described in
[`Backend/Server/Config/README.md`](../Backend/Server/Config/README.md).

## Game config

Every tunable value is authored as a CSV sheet in `GameConfigSource/` and built by `tools/GameConfigGen` into an
archive for the server and a copy for the client's offline mode. Operators publish a new config from the LiveOps
Dashboard. See [`game-config.md`](game-config.md).

## Web client

The client is a Blazor WebAssembly app that runs the Metaplay client in the browser. `MetaplayClientService` creates
the SDK client with two sub-clients: `MatchClient` for the table and a league client for the tournament. The client
picks its environment from the page host, so the same build connects to a local server, a cloud server, or runs
offline with `?env=offline`. See [`web-client.md`](web-client.md) for the client and
[`meta-shell.md`](meta-shell.md) for its screens.

## Web client hosting

The game server serves the published web client itself, from the PublicWebApi host.
`WebClientHostingController` serves the files in the directory set by `WebClientHosting:WebRootPath`: the image's
`publicwebapp` directory in the cloud, and the local publish output under `Options.local.yaml`. How the client gets
into the cloud image is in [Server image](#server-image), and the caching rules that let running clients update are in
[`web-client.md`](web-client.md).

## Bot client

[`Backend/BotClient/BotClient.cs`](../Backend/BotClient/BotClient.cs) is a load-test client. Each bot claims its daily
reward at session start, enters matchmaking, plays the cards `BotPolicy.ChooseCard` picks after a think delay, and
queues again. The matchmaker treats bot clients as people, not as bot seats ([`bots.md`](bots.md)).

## Deployment

### Project configuration

[`metaplay-project.yaml`](../metaplay-project.yaml) configures the Metaplay CLI. The project builds against the SDK
two directories up, and it has one Metaplay-hosted environment, `Demo`.

The browser client connects through the server's WebSocket gateway, which the server chart serves by default, so it
needs no Helm configuration. [`Backend/Deployments/example-server.yaml`](../Backend/Deployments/example-server.yaml)
holds comments only.

### Server image

The SDK's `Dockerfile.server` cannot build the web client: its build stage has neither the client sources nor the
`wasm-tools` workload. [`tools/ServerImageBuild.cs`](../tools/ServerImageBuild.cs) therefore publishes the client
first, stages it in `Backend/Server/publicwebapp/`, and then runs `metaplay build image`. `Server.csproj` copies that
directory into the server's publish output, where the cloud default of `WebClientHosting:WebRootPath` points.

Two details of the staging matter:

- The staging directory is emptied first. The publish's asset file names contain content hashes, and a copy over an
  old publish would leave unreferenced files.
- The build writes a build ID into `index.html` and into `build-info.json` next to it. Running clients poll that file
  to notice a new deploy ([`web-client.md`](web-client.md)).

Running `metaplay build image` directly ships a stale client or none.

### Deploying

Build the image with `tools/ServerImageBuild.cs` and deploy it with `metaplay deploy server`. The client is served
from the PublicWebApi host, whose name is the server hostname with `-public` added to its first label.
