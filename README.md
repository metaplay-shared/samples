# Metaplay Basic Samples

This repository contains basic sample projects for using the Metaplay SDK.

For a full list of all Metaplay samples and their descriptions, see the [Sample Projects](https://docs.metaplay.io/introduction/sample-projects-overview.html) page in the documentation.

## Prerequisites

This repository stores images and other binaries with [Git LFS](https://git-lfs.com/). Install it before cloning (`git lfs install`), or the binaries check out as small pointer files. Downloading the repository as a ZIP from GitHub includes the real files.

To run the samples, you'll need to get the Metaplay SDK first:

1. Create an account in the [Metaplay Developer Portal](https://portal.metaplay.dev/).

2. Install the [Metaplay CLI](https://github.com/metaplay/cli).

3. Download the Metaplay SDK using the CLI:

   <!-- \todo Fill in SDK version from script -->
   ```shell
   samples$ metaplay init sdk --sdk-version=38
   ```

## Unity samples

| Sample | What it is |
|---|---|
| [`HelloWorld`](Samples/HelloWorld) | The minimal starter: the Metaplay client, shared code and backend connected, and nothing else. `metaplay init project` is built from it. |
| [`HelloNFT`](Samples/HelloNFT) | A starter for Metaplay's NFT features. |
| [`Wordle`](Samples/Wordle) | The completed project of the [Wordle tutorial](https://docs.metaplay.io/introduction/samples/wordle-tutorial/). |
| [`Idler`](Samples/Idler) | A live-service reference game: leagues, events, in-game purchases, analytics, parties and a customized dashboard. |

To run one:

1. Start the sample's server:

    ```shell
    Samples/<Sample>$ metaplay dev server
    ```

2. Open its LiveOps Dashboard at [http://localhost:5550](http://localhost:5550).

3. Run the client in Unity:

    * Open the sample project (`Samples/<Sample>`) in Unity.
    * Open Unity menu **Metaplay** → **Environment Configs**, and set **Active Environment** to **Localhost** to ensure the client connects to the locally running server.
    * Press **Play** in Unity to run the client within the Unity Editor.

## Browser samples

These have no Unity project: their clients are written in C# with Blazor WebAssembly and run entirely in the browser, connecting to the server over WebSocket.

### HelloBlazorWasm

A preview of the Metaplay game client running as a Blazor WebAssembly app. It needs its serializer assembly generated before the client is run; see [`Samples/HelloBlazorWasm/README.md`](Samples/HelloBlazorWasm/README.md) for its build and run steps.

### Sticky Paws

[`Samples/CollectibleCardGame`](Samples/CollectibleCardGame) is a player-versus-player collectible card game: a server-authoritative multiplayer match with a private hand for each player, matchmaking with bot fill, a post-match economy between two players' accounts, and a custom LiveOps Dashboard.

The game itself is deliberately minimal and not meant to be fun: its cards exist to exercise the systems, and a real game would build its own mechanics and card synergies on this foundation.

To run it, start the server with `metaplay dev server` and the web client with `dotnet run --project Client/Client.csproj` (both from `Samples/CollectibleCardGame`), then open [http://localhost:5290](http://localhost:5290). Its dashboard needs Node.js and pnpm (`pnpm install` once, at the repository root). See [`Samples/CollectibleCardGame/README.md`](Samples/CollectibleCardGame/README.md) for the full steps and tests.

### Table Stakes

[`Samples/TableStakes`](Samples/TableStakes) is a realtime trick-taking card game for four players: a match on a multiplayer entity that keeps each player's hand secret, a matchmaker that fills empty seats with bots, and live-service features built on LiveOps Events, Leagues, MetaOffers and player segments, with a custom LiveOps Dashboard. A deployment runs at <https://public-cycles-beam-quickly-public.p2-eu.metaplay.dev/>.

The project was written primarily by AI from specs, and people have only spot-checked it. Read the known issues in its README before copying a part of it into a game.

To run it, start the server with `metaplay dev server` and the web client with `dotnet run --project WebClient/WebClient.csproj` (both from `Samples/TableStakes`), then open [http://localhost:5290](http://localhost:5290). See [`Samples/TableStakes/README.md`](Samples/TableStakes/README.md) for the dashboard, offline mode, bots and tests.
