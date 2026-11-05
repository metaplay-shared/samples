# AGENTS.md

Instructions for developers and AI coding agents working in Table Stakes. Table Stakes is built on the
[Blazor starter project](https://github.com/metaplay-shared/blazor-starter-project): its [`AGENTS.md`](https://github.com/metaplay-shared/blazor-starter-project/blob/main/AGENTS.md) and
[docs](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/project-structure.md) cover the project structure, the web client's base, the browser test harness,
deploying, and the shared gotchas and conventions. This file covers what is Table Stakes' own.

- [`README.md`](README.md): what the project is, and the index of all docs.
- [`docs/architecture.md`](docs/architecture.md): how the project is built on Metaplay.
- [`docs/testing.md`](docs/testing.md): the test levels, the rules for writing tests, and how the browser test
  harness works.
- [`docs/README.md`](docs/README.md#writing-documentation): the rules for writing documentation.

## Project context

- The project builds against the SDK in `MetaplaySDK`. Run every command in this file from this directory.
- `SharedCode/` (namespace `Game.Logic`) holds the game logic that both the server and the client run. It must be
  deterministic: the same input must give the same result on both.

## Build and run

### Server, web client and bot client

```bash
dotnet build TableStakes.slnx                          # everything: server, web client, tools, tests and the SDK projects
metaplay dev server                                    # game server
dotnet run   --project WebClient/WebClient.csproj      # web client on http://localhost:5290
dotnet watch --project WebClient/WebClient.csproj      # web client that rebuilds on file changes
metaplay dev botclient                                 # bot clients, count set in Backend/BotClient/Config/Options.base.yaml
metaplay dev botclient -- -MaxBots=40 -MaxBotId=200 -SpawnRate=4
```

- `http://localhost:5290/?offline` runs the web client without a game server.
- To try the published (trimmed) web client that players get: `python3 run_e2e.py --trimmed --no-test`.

### LiveOps Dashboard

```bash
metaplay dev dashboard                    # dashboard on http://localhost:5551; needs the game server running
metaplay build dashboard                  # production build into Backend/Dashboard/dist/
pnpm --dir Backend/Dashboard lint         # eslint; fixes what it can
pnpm --dir Backend/Dashboard typecheck    # vue-tsc
node Backend/Dashboard/scripts/smoke-player-details.mjs [playerUrl]   # checks that the game's cards render
```

Add game-specific dashboard UI in `Backend/Dashboard/src/gameSpecific.ts`. Do not edit `MetaplaySDK/Frontend`. When
you rename a C# member of the player model, update the dashboard cards that read it in the same change, because
nothing checks them. Details and the SDK update procedure: [`docs/architecture.md`](docs/architecture.md#liveops-dashboard).

### Game config

```bash
dotnet run --project tools/GameConfigGen                # build both archives from GameConfigSource/*.csv
dotnet run --project tools/GameConfigGen -- --dry-run   # build and validate, write nothing
dotnet run --project tools/GameConfigGen -- --print Backend/Server/GameConfig/StaticGameConfig.mpa
dotnet test tools/GameConfigGen.Tests --filter "FullyQualifiedName~MatchesTheSheets"
```

The game reads the built archives, not the CSV files. After editing a CSV file, run the build and commit both
archives it writes. The `MatchesTheSheets` test fails if the committed archives do not match the CSV files. Details:
[`docs/game-config.md`](docs/game-config.md#the-build-tool).

### Pre-built WASM serializer

Building `WebClient` runs `tools/SerializerGen` when the game types have changed, and writes the serializer under
`WebClient/obj/`. There is no separate step. Details:
[the pre-built serializer](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/web-client.md#the-pre-built-serializer).

### Deploying

```bash
metaplay build image                                   # the game server image
metaplay deploy server Demo <image>
rm -rf WebClient/bin/Release/net10.0-browser/publish   # a publish never deletes files, so start from an empty directory
dotnet publish WebClient/WebClient.csproj -c Release
metaplay deploy webclient Demo                         # upload the web client to the environment's CDN
metaplay deploy webclient Demo --dry-run               # what would be uploaded, with which headers
```

The server image does not contain the web client. Deploy the server for a server change and the web client for a
client change. Details: [`docs/architecture.md`](docs/architecture.md#deployment).

### Database migrations

```bash
dotnet ef migrations add MetaplayRelease<N> --project Backend/Server/Server.csproj
```

This needs the `dotnet-ef` tool. Add a migration when the local server stops with "Database schema has pending
changes that require a new migration". This usually happens after an SDK upgrade.

## Tests

```bash
# Shared code unit tests. No server needed.
dotnet test Backend/SharedCode.Tests/SharedCode.Tests.csproj

# Server unit tests. No server needed.
dotnet test Backend/Server.Tests/Server.Tests.csproj

# The game config build and the committed archives against the sheets.
dotnet test tools/GameConfigGen.Tests

# Render tests (bUnit) only. No browser or server needed.
dotnet test WebClient.Tests/WebClient.Tests.csproj --filter "FullyQualifiedName~RenderTests"

# All WebClient.Tests that need no browser: client logic tests and render tests.
dotnet test WebClient.Tests/WebClient.Tests.csproj --filter "FullyQualifiedName!~LiveServer & FullyQualifiedName!~PageTests & FullyQualifiedName!~OfflineMode & FullyQualifiedName!~TableRobustness & FullyQualifiedName!~ConnectionOverlay & FullyQualifiedName!~CloudHosting"

# Browser tests (Playwright). See "Browser tests" below.
python3 run_e2e.py
```

- Add or update tests in the same change as the feature. Read the rules in
  [`docs/testing.md`](docs/testing.md#rules) before writing tests.

## Browser tests

Run the browser tests with `python3 run_e2e.py`, the starter project's
[harness](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/testing.md#the-e2e-harness), configured by [`e2e.toml`](e2e.toml). `python3 run_e2e.py --help`
lists its options; `--plain-server` starts the server with production match timings, for playing by hand.

```bash
python3 run_e2e.py                                              # all browser tests
python3 run_e2e.py --filter "FullyQualifiedName~ShellPageTests" # other arguments are passed to dotnet test
python3 run_e2e.py --trimmed        # test the published (Release, trimmed) web client
```

To run the tests against a server and web client you started yourself, run `dotnet test` with no harness. The
tests then use the default ports:

```bash
pwsh WebClient.Tests/bin/Debug/net10.0/playwright.ps1 install chromium   # once; the harness does this itself
metaplay dev server -- --TestRoutes:Enabled=true     # terminal 1; without this flag the test/ routes return 404
dotnet run --project WebClient/WebClient.csproj      # terminal 2
dotnet test WebClient.Tests/WebClient.Tests.csproj   # terminal 3
```

- `OfflineModeTests`, `TablePageTests` and `TableRobustnessTests` need only the web client, not the server.
- Run `--trimmed` after changing the `<TrimmerRootAssembly>` list in `WebClient/WebClient.csproj` or after adding a
  project that the web client references. Only the published web client is trimmed, so a missing entry fails only
  there ([the published client](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/publishing.md#the-published-client)).
- Do not run two browser test runs against the same server at once. Two `run_e2e.py` runs are safe only from
  different checkouts, because runs from one checkout share build output.

## Known problems and fixes

- The starter project's [gotchas](https://github.com/metaplay-shared/blazor-starter-project/blob/main/AGENTS.md#gotchas) apply, including restarting a running web client after
  building it from elsewhere.
- **The server fails to start after you add an analytics event.** The event has a `ulong` field, which the BigQuery
  export does not accept. Use a signed type of the same size instead. `AnalyticsBigQueryFormatTests` in
  `Server.Tests` detects this.
- **After an SDK update, dashboard lint or typecheck fails inside SDK components.** Update `Backend/Dashboard/` from
  `MetaplaySDK/Frontend/DefaultDashboard` ([`docs/architecture.md`](docs/architecture.md#liveops-dashboard)).
- **A browser test is slow, flaky, or does nothing when it taps.** See
  [`docs/testing.md`](docs/testing.md#writing-deterministic-tests).

## Code conventions

- The starter project's [conventions](https://github.com/metaplay-shared/blazor-starter-project/blob/main/AGENTS.md#conventions) apply.
- When a comment says why something is safe, name the SDK behavior that makes it safe. Do not rely on something
  that is only true of the repository today.
- Reload with `StartPage.Reload`, which goes back to the query the page was loaded with
  ([the start query](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/web-client.md#the-start-query)). Read a query setting from
  `QueryParameters.AtStart` (`WebClient/Integration/QueryParameters.cs`), not from the current address.
