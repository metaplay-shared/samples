# AGENTS.md

How to develop in this repo. For what the project is see [`README.md`](README.md); for SDK concepts and APIs, use the
Metaplay docs.

NinjaRun is built on the Blazor starter project. The starter project's
[`AGENTS.md`](https://github.com/metaplay-shared/blazor-starter-project/blob/main/AGENTS.md) and [docs](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/project-structure.md) cover the project structure, the web
client's base, building and running, the test layers, the browser test harness, deploying, and the shared gotchas and
conventions. This file covers what is NinjaRun's own. The SDK is at `MetaplaySDK/`; do not modify it.

## Build & run

```bash
dotnet build NinjaRun.slnx          # build everything: server, web client, tools, tests and the SDK projects

metaplay dev server                 # the server
dotnet watch --project WebClient/WebClient.csproj   # the web client, rebuilt on changes; http://localhost:5290
python3 run_e2e.py --trimmed --no-test               # the published client, the build to measure performance on

# Game config: builds the archives from the CSV sheets and CourseParts.json in GameConfigSource/
dotnet run --project tools/GameConfigGen
dotnet run --project tools/GameConfigGen -- --dry-run
```

Deploying the server and the web client is in the starter project's
[publishing guide](https://github.com/metaplay-shared/blazor-starter-project/blob/main/docs/publishing.md#deploying-to-the-cloud).

## Tests

Give a feature unit tests and end-to-end tests, in the same change that adds or changes it. Unit tests go in the
project matching where the code lives:

- **Shared-code unit tests** (`Backend/SharedCode.Tests/`, NUnit) — deterministic game logic: actions,
  model state, `GameTick` / fast-forward, game-config-driven behavior. Fast; no server needed.
- **Game config tests** (`tools/GameConfigGen.Tests/`, NUnit) — the game config build itself
  (`GameConfigBuildTests`), the rules the build enforces on the Skills sheet, and the course parts' JSON
  format. They read `GameConfigSource/` and the committed archives, which are outside `Backend/`.
- **Server unit tests** (`Backend/Server.Tests/`, NUnit) — logic only the server runs, in
  `Backend/Server/`: the map registry, the course ghost pools and their house field, the player name
  validator. It references `Server.csproj` and links the game config and test course helpers from
  `SharedCode.Tests/`. Also fast; it does not start a server.
- **End-to-end tests** (`WebClient.Tests/`, Playwright) — player-visible behavior through the real web
  client against a live server: the feature's UI renders, its action runs, and the model + UI update.

Copy the time trial cases — `PlayerActionTests` and `TimeTrialSimulationTests` (unit), `HomePageTests`
(E2E). Prefer role/text locators; for a value with no accessible role, add a `data-testid` (as on the
Orb balance).

A browser test that plays a run to the finish line takes over a minute in real time, so such tests are marked
`[RunTimeScale(RunTimeScaleAttribute.Fastest)]` (on the test, or on a fixture made of them, like
`HomePageTests`) and play at 4x through the client's slow-motion knob; `GamePageTest` explains why that leaves
the recording and the server's result unchanged. A test that times the run's clock against the page's own, as
the run-in and run-out tests do, keeps real time. A full run is the expensive part of a test, so check what a
test needs a run for before adding one: assert on the run another test already plays when it is the same kind of
run, and reach the Skill tree's upper tiers with the developer course tools' Orb grant
(`PlayerDeveloperGrantOrbs`) rather than with more runs.

```bash
dotnet test Backend/SharedCode.Tests/SharedCode.Tests.csproj   # unit — run after a shared-code change
dotnet test Backend/Server.Tests/Server.Tests.csproj           # unit — run after a server-logic change, and after a shared-code change it uses
dotnet test tools/GameConfigGen.Tests                          # game config — run after a change to GameConfigSource/ or a config class

# E2E: the starter project's harness, configured by e2e.toml
python3 run_e2e.py
python3 run_e2e.py --filter "FullyQualifiedName~HomePageTests"   # other arguments are passed to dotnet test
```

## Gotchas

- **A served art or audio file is named after its content**: `WebClient/wwwroot/art/<name>.sha256-<hash>.webp`
  and `wwwroot/audio/<name>.sha256-<hash>.wav`, and the CDN caches such a name as immutable (the `webClients`
  entry in `metaplay-project.yaml`). A changed file therefore needs a new name — the SHA-256 of its bytes, first 16
  hex digits — and the references to it updated in the same change (`js/renderer/artSpecs.js`,
  `courseDecorations.js`, `timeTrialAudio.js`, `css/menu.css`, `css/customize.css`, `css/transition.css`,
  `index.html`, `Components/Menu/MainMenuView.razor`).
- **`CourseParts` has no sheet.** It is listed in `GameConfigBuild.JsonSourcedEntries` and read from
  `GameConfigSource/CourseParts.json` through its own build source (`GameBuildParameters.CoursePartsSource`), parsed
  by `SharedCode/TimeTrial/CoursePartJson.cs`, because a part's nested lists do not fit a sheet. A part is authored in
  that file, optionally drafted in the part editor of the developer course tools, which writes the object to paste.
  Giving it a sheet fails the build.
- **A `CourseId` config cell is parsed by the game's own `ConfigParserProvider`** (`CourseConfigParsers` in
  `SharedCode/TimeTrial/Course.cs`), so a map id — part ids joined by `+` — is legal in a sheet. The SDK's
  default `StringId` token stops at the `+` and would read the cell as its first part alone.

## Conventions

- The starter project's conventions apply.
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
