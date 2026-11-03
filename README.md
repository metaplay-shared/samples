# Metaplay Samples

This repository contains sample projects for using the Metaplay SDK.

For a full list of all Metaplay samples and their descriptions, see the [Sample Projects](https://docs.metaplay.io/introduction/sample-projects-overview.html) page in the documentation.

## Prerequisites

This repository stores images and other binaries with [Git LFS](https://git-lfs.com/). Install it before cloning (`git lfs install`), or the binaries check out as small pointer files. Downloading the repository as a ZIP from GitHub includes the real files.

To run the samples, you'll need the Metaplay SDK and the tools to build the server and the client:

1. Create an account in the [Metaplay Developer Portal](https://portal.metaplay.dev/).

2. Install the [Metaplay CLI](https://github.com/metaplay/cli).

3. Download the Metaplay SDK into the sample's directory using the CLI:

   ```shell
   Samples/<Sample>$ metaplay init sdk --sdk-version=39
   ```

4. Install the [.NET SDK](https://dotnet.microsoft.com/download), version 10. It builds and runs the server, and the clients of the browser projects.

5. Install the client tools:

    * **Unity projects**
        * You need [Unity Editor](https://unity.com/download) to run the client.
    * **Browser projects**:
        * No Unity engine is needed.
        * Clients are written in C# with Blazor WebAssembly and run entirely in the browser, connecting to the server over WebSocket.

## Running

1. Start the sample's server:

    ```shell
    Samples/<Sample>$ metaplay dev server
    ```

2. Open its LiveOps Dashboard at [http://localhost:5550](http://localhost:5550).

3. Run the client:

    * **Unity projects**:
        * Open the sample project (`Samples/<Sample>`) in Unity.
        * Open Unity menu **Metaplay** → **Environment Configs**, and set **Active environment** to **localhost** to ensure the client connects to the locally running server.
        * Press **Play** in Unity to run the client within the Unity Editor.
    * **Browser projects**:
        * Start the web client
            ```shell
            Samples/<Sample>$ dotnet run --project WebClient/WebClient.csproj
            ```
        * Open [http://localhost:5290](http://localhost:5290).

## Starter Projects

### HelloWorld

<img align="right" src=".github/images/helloworld.png" width="180" alt="Hello World screenshot" />

> * **Platform:** Unity
> * **Source:** [`Samples/HelloWorld`](Samples/HelloWorld)
> * **Documentation:** [Hello World Sample](https://docs.metaplay.io/introduction/samples/helloworld/)

This is the tiny, friendly starter for Unity that gets you from zero to a running Metaplay scene. Open the project, press play, and you'll see how the Metaplay client, shared code, and backend all connect without any of the extra noise. It features a simple click counter game where pressing a button increments a server-validated counter, showing how actions flow from client to server and back. We use it to initialize new projects when running the `metaplay init project` command.

<br clear="right" />

### Blazor Starter Project

<img align="right" src=".github/images/blazor-starter-project.png" width="320" alt="Blazor starter project screenshot" />

> * **Platform:** Browser (Blazor WebAssembly)
> * **Source:** [`metaplay-shared/blazor-starter-project`](https://github.com/metaplay-shared/blazor-starter-project)
> * **Documentation:** [Blazor Starter Project](https://docs.metaplay.io/introduction/samples/blazor-starter-project/)

This is an AI-friendly starter project for the Blazor web framework. It lets you build games that compile to WebAssembly and run in the browser with no game engine. When a coding agent sets it up by following its getting-started instructions, it installs [Metaplay Agent](https://docs.metaplay.io/miscellaneous/ai-assistants/metaplay-agent.html) for you. The browser samples below, Sticky Paws, Table Stakes and Ninja Time Trial, are built on it.

<br clear="right" />

## Samples

### Wordle

<img align="right" src=".github/images/wordle.png" width="180" alt="A solved Wordle puzzle" />

> * **Platform:** Unity
> * **Source:** [`Samples/Wordle`](Samples/Wordle)
> * **Tutorial:** [Wordle Tutorial](https://docs.metaplay.io/introduction/samples/wordle-tutorial/)

Follow the [Wordle Tutorial](https://docs.metaplay.io/introduction/samples/wordle-tutorial/) if you like learning by doing. It shows how to make a simple game (a Wordle clone) from scratch using Metaplay. You'll build up features gradually, so it's great when you want a guided, step-by-step walkthrough.

<br clear="right" />

### Idler

<img align="right" src=".github/images/idler.png" width="180" alt="Idler screenshot" />

> * **Platform:** Unity
> * **Source:** [`Samples/Idler`](Samples/Idler)
> * **Documentation:** [Idler Sample](https://docs.metaplay.io/introduction/samples/idler/)

Idler is a simple game where you invest coins to unlock and upgrade "producers" that generate coins over time. It is a testing ground for Metaplay features: a complete live-service example that covers leagues, events, in-game purchases, analytics, party systems, and a highly customizable dashboard. Use it when you want to see how all the moving parts fit together in a simple idle game.

<br clear="right" />

### Orca

<img align="right" src=".github/images/orca.png" width="180" alt="Merge board partially visible" />

> * **Platform:** Unity
> * **Source:** [`metaplay-shared/orca`](https://github.com/metaplay-shared/orca)
> * **Documentation:** [Orca Sample](https://docs.metaplay.io/introduction/samples/orca/)
> * [**▶️ Play the demo**](https://orca-demo-assets.p1.metaplay.io/)

Orca is a real merge-2 game developed on Metaplay and open-sourced by [Zaibatsu Interactive](https://zaibatsu.fi/) for demo use. It shows the same live-service features as Idler but tailored to merge gameplay: custom dashboards, extensive game configs, data-driven design, and the kinds of systems a casual mobile game needs.

If you're building a merge-style game or want to see how a full game can be built on Metaplay, Orca is a good place to start. It lives in its own repository. Its code is licensed under Apache 2.0, but its art and other assets may only be used within the sample.

<br clear="right" />

### HelloNFT

> * **Platform:** Unity
> * **Source:** [`Samples/HelloNFT`](Samples/HelloNFT)

HelloNFT is a sample demonstrating Metaplay's NFT features.

### Sticky Paws

<img align="right" src=".github/images/sticky-paws.jpg" width="320" alt="Attacking an enemy unit" />

> * **Platform:** Browser (Blazor WebAssembly)
> * **Source:** [`Samples/CollectibleCardGame`](Samples/CollectibleCardGame)
> * [**▶️ Play now**](https://silver-cobras-hunt-quickly-assets.p2-eu.metaplay.dev/)

Sticky Paws is a player-versus-player collectible card game. It shows a server-authoritative multiplayer match with a private hand for each player, matchmaking with bot fill, a post-match economy that moves a card rank from one player's account to the other's, bots and self-play testing, and a custom dashboard.

The game itself is deliberately minimal. The cards are there to show off these systems, and a real game would add its own mechanics and card synergies. The sample's README explains what the sample demonstrates and how to run it.

<br clear="right" />

### Table Stakes

<img align="right" src=".github/images/table-stakes.jpg" width="220" alt="My turn to play a card" />

> * **Platform:** Browser (Blazor WebAssembly)
> * **Source:** [`Samples/TableStakes`](Samples/TableStakes)
> * [**▶️ Play now**](https://public-cycles-beam-quickly-assets.p2-eu.metaplay.dev/)

Table Stakes is a realtime trick-taking card game for four players. It shows a realtime match on a multiplayer entity that keeps each player's hand secret, a matchmaker that seats four players within seconds and fills empty seats with bots, and bots that take over when a player disconnects or stops playing. Around the card game it builds live-service features on LiveOps Events, Leagues, MetaOffers and player segments, with game configs authored as CSV sheets, a custom dashboard, and tests up to browser tests against a real server.

The project was written primarily by AI from specs, people have only spot-checked it, and it has not been load tested beyond small bot client runs. The sample's README lists the known issues to read before copying a part of the sample into a game, and how to run it.

<br clear="right" />

### Ninja Time Trial

<img align="right" src=".github/images/ninja-run.jpg" width="220" alt="The ninja running a course, with an opponent's ghost ahead" />

> * **Platform:** Browser (Blazor WebAssembly)
> * **Source:** [`Samples/NinjaRun`](Samples/NinjaRun)
> * [**▶️ Play now**](https://whole-impalas-strive-quickly-assets.p2-eu.metaplay.dev/)

Ninja Time Trial is a one-button autorunner. It shows how a conventional fast-paced real-time game is integrated with a Metaplay backend. The run is simulated on the client at 60 Hz with no server round trips. The server validates the finished run afterwards and rewards the player. The sample also shows per-course ghost pools that collect accepted runs and pick each run's opponents.

Use it when your game has real-time gameplay that cannot be expressed as player actions but whose outcome the server still needs to validate. The sample's README explains how a run is validated, what recomputation does and does not prove, and how to run the sample.

<br clear="right" />

## License

Copyright Metaplay. The samples in this repository are licensed under the [Apache License 2.0](LICENSE). The Metaplay SDK they build on, including its dashboard packages, is licensed separately, under the Metaplay SDK License. Third-party fonts and code keep their own licenses, which are next to them.
