# AGENTS.md

Instructions for developers and AI coding agents working in Table Stakes.

- [`README.md`](README.md): what the project is, and the index of all docs.
- [`docs/architecture.md`](docs/architecture.md): how the project is built on Metaplay.
- [`docs/testing.md`](docs/testing.md): the test levels, the rules for writing tests, and how the browser test
  harness works.
- [`docs/README.md`](docs/README.md#writing-documentation): the rules for writing documentation.

## Project context

- The project builds against the SDK in `../../MetaplaySDK`. Run every command in this file from this directory.
- `SharedCode/` (namespace `Game.Logic`) holds the game logic that both the server and the client run. It must be
  deterministic: the same input must give the same result on both.

## Build and run

### Server, web client and bot client

```bash
metaplay dev server                                    # game server
dotnet run   --project WebClient/WebClient.csproj      # web client on http://localhost:5290
dotnet watch --project WebClient/WebClient.csproj      # web client that rebuilds on file changes
metaplay dev botclient                                 # bot clients, count set in Backend/BotClient/Config/Options.base.yaml
metaplay dev botclient -- -MaxBots=40 -MaxBotId=200 -SpawnRate=4
```

- `http://localhost:5290/?env=offline` runs the web client without a game server.
- Pressing `K` in the server console moves server time forward (`Backend/Server/ServerMain.cs`).
- To test the web client as the game server serves it in the cloud: run
  `dotnet publish WebClient/WebClient.csproj -c Release`, start the server, and open `http://localhost:5560/`.

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
dotnet test Backend/Server.Tests/Server.Tests.csproj --filter "FullyQualifiedName~MatchesTheSheets"
```

The game reads the built archives, not the CSV files. After editing a CSV file, run the build and commit both
archives it writes. The `MatchesTheSheets` test fails if the committed archives do not match the CSV files. Details:
[`docs/game-config.md`](docs/game-config.md#the-build-tool).

### Pre-built WASM serializer

```bash
dotnet run --project tools/SerializerGen -- WebClient/Serializer
```

Building `WebClient` runs this generator when needed. If the build fails because the serializer DLL "was not
resolved", build again, or run the command above first. Details:
[`docs/web-client.md`](docs/web-client.md#the-pre-built-serializer).

### Server image

```bash
dotnet run tools/ServerImageBuild.cs                     # publish the web client, copy it into the server, build the image
dotnet run tools/ServerImageBuild.cs -- mygame:1a27c25   # arguments after -- go to `metaplay build image`
dotnet run tools/ServerImageBuild.cs -- --stage-only     # publish the web client and copy it into the server only
```

This needs the `wasm-tools` workload: `dotnet workload install wasm-tools`. Do not run `metaplay build image`
directly: it does not build the web client, so the image contains an old web client or none. Details:
[`docs/architecture.md`](docs/architecture.md#server-image).

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

# Render tests (bUnit) only. No browser or server needed.
dotnet test WebClient.Tests/WebClient.Tests.csproj --filter "FullyQualifiedName~RenderTests"

# All WebClient.Tests that need no browser: client logic tests and render tests.
dotnet test WebClient.Tests/WebClient.Tests.csproj --filter "FullyQualifiedName!~LiveServer & FullyQualifiedName!~PageTests & FullyQualifiedName!~OfflineMode & FullyQualifiedName!~TableRobustness"

# Browser tests (Playwright), run from a worktree. See "Browser tests" below.
tools/run-e2e.sh
```

- Add or update tests in the same change as the feature. Read the rules in
  [`docs/testing.md`](docs/testing.md#rules) before writing tests.

## Browser tests

The default ports belong to the main checkout. In a worktree, run the browser tests with `tools/run-e2e.sh`. It
builds and starts a game server and a web client on free ports, runs the tests, and stops both. It needs Python 3
on `PATH`, and it refuses to run in the main checkout.

```bash
dotnet build WebClient.Tests/WebClient.Tests.csproj
pwsh WebClient.Tests/bin/Debug/net10.0/playwright.ps1 install chromium   # once per machine

tools/run-e2e.sh                                                # all browser tests
tools/run-e2e.sh --filter "FullyQualifiedName~ShellPageTests"   # other arguments are passed to dotnet test
tools/run-e2e.sh --trimmed          # test the published (Release, trimmed) web client
tools/run-e2e.sh --serve            # run the tests and leave the server and web client running
tools/run-e2e.sh --no-test          # start the server and web client without running tests
tools/run-e2e.sh --stop             # stop the server and web client that --serve left running
tools/run-e2e.sh --shipped-pacing   # start the server with production match timings, for playing by hand
```

In the main checkout, start the server and web client yourself. The tests then use the default ports:

```bash
metaplay dev server -- --TestRoutes:Enabled=true     # terminal 1; without this flag the test/ routes return 404
dotnet run --project WebClient/WebClient.csproj      # terminal 2
dotnet test WebClient.Tests/WebClient.Tests.csproj   # terminal 3
```

- `OfflineModeTests`, `TablePageTests` and `TableRobustnessTests` need only the web client, not the server.
- Run `--trimmed` after changing the `<TrimmerRootAssembly>` list in `WebClient/WebClient.csproj` or after adding a
  project that the web client references. Only the published web client is trimmed, so a missing entry fails only
  there ([`docs/web-client.md`](docs/web-client.md#the-published-client)).
- Do not run two browser test runs against the same server at once. Two `tools/run-e2e.sh` runs are safe only from
  different worktrees.

## Known problems and fixes

- **Every browser test fails at startup with "An unhandled error has occurred".** The web client was rebuilt while
  its dev server was running. Restart the web client after every build. Running too many test workers at once
  causes the same error, because the WebAssembly startup gets too little CPU and fails.
- **Test setup fails with "is not the one built from this working tree".** Another process is using port 5290,
  such as an old web client or one running inside WSL, or the web client was not rebuilt. Stop the other process,
  or rebuild and restart the web client.
- **Build warning `CS8784: Generator 'MetaplayGenerator' failed to initialize`, then at run time "Integration root
  assembly 'SharedCode.Client' doesn't contain source generated Metaplay integration type info".** The generator
  cannot find `Metaplay.Attributes.dll`. Build `MetaplaySDK/Backend/Attributes/Metaplay.Attributes.csproj`, run
  `dotnet build-server shutdown`, delete `obj/` and `bin/` in `SharedCode`, `WebClientBase` and `WebClient`, and
  build again. Rebuilding without deleting those directories does not fix it.
- **The server fails to start after you add an analytics event.** The event has a `ulong` field, which the BigQuery
  export does not accept. Use a signed type of the same size instead. `AnalyticsBigQueryFormatTests` in
  `Server.Tests` detects this.
- **After an SDK update, dashboard lint or typecheck fails inside SDK components.** Update `Backend/Dashboard/` from
  `MetaplaySDK/Frontend/DefaultDashboard` ([`docs/architecture.md`](docs/architecture.md#liveops-dashboard)).
- **A browser test is slow, flaky, or does nothing when it taps.** See
  [`docs/testing.md`](docs/testing.md#writing-deterministic-tests).

## Code conventions

- Use explicit types, not `var`. Iterate dictionaries with tuple deconstruction:
  `foreach ((KeyType key, ValueType value) in dict)`.
- Comments describe what the code does now. They do not describe the change that was made or earlier behavior.
- When a comment says why something is safe, name the SDK behavior that makes it safe. Do not rely on something
  that is only true of the repository today.
- Build links and navigation in the web client with `EnvironmentLink` (`WebClientBase/Utilities/EnvironmentLink.cs`).
  It keeps the page's `?env=` parameter. A plain `NavigateTo` or `href` drops it, and the next page load connects
  to a different environment.
