# AGENTS.md

How to develop in this repo. This is the canonical dev guide; `CLAUDE.md` imports it. For what the
project is and how to adopt it see [`README.md`](README.md); for SDK concepts and APIs, use the Metaplay
docs.

Shared game logic lives in `SharedCode/` (namespace `Game.Logic`) and runs deterministically on both
client and server. `SharedCode/` holds only code that both the client and the server run; code only the
server runs lives in `Backend/Server/` (namespace `Game.Server.*`), even when that makes it testable only
from `Backend/Server.Tests/`. Designer-tunable values live in `GameConfigSource/` as CSV sheets, one per game config
entry. The Metaplay SDK is at `../../MetaplaySDK/` — do not modify it.

## Build & run

```bash
dotnet run tools/DevStack.cs        # preferred dev loop: watched server + watched WebClient in one shell
                                    # Ctrl+C stops both complete process trees. A WebClient build from outside
                                    # the watcher is caught: the web watcher is restarted when its dev server
                                    # can no longer serve the client it hands out (see Gotchas).
dotnet run tools/DevStack.cs -- --release   # Release publish of the WebClient, served by the server alone at
                                            # http://localhost:5560 and over the LAN; the build to measure
                                            # performance on. Nothing is watched.
metaplay dev server                 # run only the server (add --watch to auto-restart on changes)
metaplay dev botclient              # run the load-testing bot client

# Web client — targets net10.0-browser, which selects the SDK's browser flavor across the graph
dotnet run   --project WebClient/WebClient.csproj
dotnet build WebClient/WebClient.csproj

# Game config. Builds the archive from the CSV sheets in GameConfigSource/ and writes both copies: the
# server's StaticGameConfig.mpa and the client's built-in SharedGameConfig.mpa. A validation error fails the
# build: nothing is written and the command exits non-zero — for a parse failure, which throws, and for the
# SDK's own item validators, which only report into the build report and would otherwise be written into the
# archive. --dry-run is the same build with nothing written, which is the form to run in CI. Run it from the
# repo root.
dotnet run --project tools/GameConfigGen
dotnet run --project tools/GameConfigGen -- --dry-run

# Read an archive back, to see what a client is actually being served
dotnet run --project tools/GameConfigGen -- --print Backend/Server/GameConfig/StaticGameConfig.mpa

# Server image for the cloud. Publishes the WebClient, stages it into Backend/Server/publicwebapp/ (which
# Server.csproj copies into the publish output, and so into the image at /gameserver/publicwebapp), then
# runs `metaplay build image`. Anything after -- that it does not recognize is forwarded to that command;
# --stage-only stops after staging. Use this rather than `metaplay build image` — the SDK's Dockerfile
# cannot build the WebClient, so on its own that command ships whatever was staged last, or no client at
# all. Run it from the repo root.
dotnet run tools/ServerImageBuild.cs
dotnet run tools/ServerImageBuild.cs -- mygame:1a27c25
dotnet run tools/ServerImageBuild.cs -- --stage-only
dotnet run tools/ServerImageBuild.cs -- --aot mygame:1a27c25   # client compiled ahead of time to WebAssembly
```

## Tests

Two layers, unit and end-to-end, and **every feature should have both** — add or update tests in the same
change that adds or changes the feature. Unit tests go in the project matching where the code lives:

- **Shared-code unit tests** (`Backend/SharedCode.Tests/`, NUnit) — deterministic game logic: actions,
  model state, `GameTick` / fast-forward, game-config-driven behavior, and the game config build itself
  (`GameConfigBuildTests`). Fast; no server needed.
- **Server unit tests** (`Backend/Server.Tests/`, NUnit) — logic only the server runs, in
  `Backend/Server/`: the map registry, the course ghost pools and their house field, the player name
  validator. It references `Server.csproj` and links the game config and test course helpers from
  `SharedCode.Tests/`. Also fast; it does not start a server.
- **End-to-end tests** (`WebClient.Tests/`, Playwright) — player-visible behavior through the real web
  client against a live server: the feature's UI renders, its action runs, and the model + UI update.

Copy the time trial cases — `PlayerActionTests` and `TimeTrialSimulationTests` (unit), `HomePageTests`
(E2E). Prefer role/text locators; for a value with no accessible role, add a `data-testid` (as on the
Orb balance).

```bash
dotnet test Backend/SharedCode.Tests/SharedCode.Tests.csproj   # unit — run on every shared-code change
dotnet test Backend/Server.Tests/Server.Tests.csproj           # unit — run on every server-logic change, and on a shared-code change it uses

# E2E needs a running server AND web client, plus the Playwright browsers (installed once):
metaplay dev server                                                           # terminal 1
dotnet run --project WebClient/WebClient.csproj                               # terminal 2 → http://localhost:5290
dotnet build WebClient.Tests/WebClient.Tests.csproj                           # then install the browser once:
pwsh WebClient.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
dotnet test WebClient.Tests/WebClient.Tests.csproj                           # terminal 3

# OfflineModeTests are the exception: they drive ?env=offline, so they need only the web client.
dotnet test WebClient.Tests/WebClient.Tests.csproj --filter "FullyQualifiedName~OfflineModeTests"
```

## Gotchas

- **Build the WebClient after changing it.** Its `net10.0-browser` target framework is what gives the SDK
  the right transport and compiles out runtime codegen; publish output lands under
  `bin/<config>/net10.0-browser/`. See `WebClient/README.md`.
- **The pre-built WASM serializer is generated, not committed.** The WebClient build generates
  `WebClient/Serializer/` through `tools/SerializerGen/BrowserSerializer.targets`. If a build reports that the
  serializer was not resolved, build again, or generate it first:
  `dotnet run --project tools/SerializerGen -- WebClient/Serializer`.
- **Build cloud images with `dotnet run tools/ServerImageBuild.cs`, not `metaplay build image`.**
  The SDK's Dockerfile has no WebClient build stage, so the tool publishes the client and stages it into
  `Backend/Server/publicwebapp/` first. It rebuilds that directory from scratch every run, and stamps
  `build-info.json` (served at `<public-url>/build-info.json`) with the commit the client came from.
- **Add an EF Core migration when the database model changes** — on an SDK upgrade, or when the game adds a
  persisted entity of its own — or the server refuses to start:
  `dotnet ef migrations add <Name> --project Backend/Server/Server.csproj`. A persisted entity's table comes
  from the `[Table]` attribute on its persisted class; declaring a `DbSet` for it in `GameDbContext` as well
  is an error ("mapped both in DB context and via Table attribute") and the migration cannot be generated.
- **Rebuild the game-config archive after editing a file in `GameConfigSource/` or a config class**:
  `dotnet run --project tools/GameConfigGen`. Commit both copies it writes, so offline mode cannot serve an
  older config than the server. The archives are not byte-reproducible — each build stamps its own version
  hash — so what holds them to the sources is `GameConfigBuildTests`, which compares the committed archives
  against a fresh build item by item (both halves of the static archive), not a file diff.
- **A served art or audio file is named after its content**: `WebClient/wwwroot/art/<name>.sha256-<hash>.webp`
  and `wwwroot/audio/<name>.sha256-<hash>.wav`, and the server caches such a name as immutable. A changed
  file therefore needs a new name — the SHA-256 of its bytes, first 16 hex digits — and every reference to
  it updated in the same change (`js/renderer/artSpecs.js`, `courseDecorations.js`, `timeTrialAudio.js`,
  `css/menu.css`, `css/customize.css`, `css/transition.css`, `index.html`,
  `Components/Menu/MainMenuView.razor`).
- **A new `[GameConfigEntry]` needs a CSV file of the same name in `GameConfigSource/`**, or the build fails
  from inside that entry's own build step. [`GameConfigSource/README.md`](GameConfigSource/README.md) is how
  a sheet is written; the build tool and `Backend/SharedCode.Tests/` both name the missing file instead. The
  exception is an entry listed in `GameConfigBuild.JsonSourcedEntries` — `CourseParts` — which is read from
  `GameConfigSource/CourseParts.json` through its own build source (`GameBuildParameters.CoursePartsSource`)
  and parsed by `SharedCode/TimeTrial/CoursePartJson.cs`, because a part's nested lists do not fit a sheet.
  A part is authored in that file, optionally drafted in the part editor of the developer course tools,
  which writes the object to paste. It has no sheet, and giving it one fails the build instead.
- **A `CourseId` config cell is parsed by the game's own `ConfigParserProvider`** (`CourseConfigParsers` in
  `SharedCode/TimeTrial/Course.cs`), so a map id — part ids joined by `+` — is legal in a sheet. The SDK's
  default `StringId` token stops at the `+` and would read the cell as its first part alone.
- **Let the watcher build the WebClient while DevStack runs.** The web dev server reads its asset route table
  once, at startup, and serves the app assembly under a content-hashed name, so a build from a second shell,
  an IDE or an agent renames that file out from under the running server: the browser is told to load a
  file the server 404s, and the page fails its integrity check on zero bytes (computed hash
  `47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=`, the hash of nothing) on every reload. DevStack catches
  it — when the server can no longer serve the client its boot config names, the launcher kills the web
  watcher's whole process tree and starts it again, which rebuilds and relaunches the dev server with a fresh
  route table; that costs the watcher's startup build and a page reload — so the stack heals itself, but a
  source save the watcher picks up costs nothing, and the watcher's console is where a compile error shows.
  Build the WebClient in a second shell only when no stack is running (`dotnet run tools/DevStack.cs` has
  a `blazor-devserver` process listening on port 5290 while it is).

## Conventions

- Explicit typing; avoid `var`. Iterate dictionaries with tuples: `foreach ((KeyType k, ValueType v) in dict)`.
- Comments describe the code's current behavior — never narrate the change or prior state.
- Prefer Read / Glob / Grep over `cat` / `find` / `grep`; reserve the shell for real commands.
- **Name the core loop `TimeTrial`.** The timed attempt on a map is a `TimeTrial` everywhere in
  code — `TimeTrial` (the attempt/lifecycle), `TimeTrialInput` (one recorded press / hold / release
  transition at a simulation step), `TimeTrialRecording` (the ordered list of inputs submitted for
  validation), `TimeTrialSimulation` (the deterministic fixed-step sim), `TimeTrialTicket`,
  `TimeTrialResult`. Describe server-side validation by what it does — the server recomputes the result
  and accepts or rejects the recording by comparing it to its own simulation — keeping docs, comments,
  and prompts in plain gameplay terms. The game is a ninja autorunner; this vocabulary is deliberate,
  not shorthand. Do not use `Session` for the attempt — it collides with the SDK's `SessionActor`.
  `Course` is reserved for the level geometry, never the attempt.

## Validation docs

The [How a run is validated](README.md#how-a-run-is-validated) section of `README.md` describes the
ticket, recording, replay and commit path and what it does and does not prove. Keep it current in the same
change as the implementation; describe what and why, and leave exact fields, codes and values in code and
config.
