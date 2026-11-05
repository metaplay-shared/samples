# Sticky Paws

Sticky Paws is a sample game for the [Metaplay SDK](https://metaplay.io): a two-player, turn-based collectible card
game with a server-authoritative match and a client that runs entirely in the browser as a Blazor WebAssembly app.

Players get a starter collection, build decks from it, and take a deck into a ranked or practice match against another
player or a bot. After a ranked match, the winner takes one rank from a card the loser played. Around the match are
the screens a card game needs: the collection, a deckbuilder, card locks and a ranked leaderboard.

This is a toy game. Its rules and cards are the smallest set that exercises the systems below, with no attempt at
balance or depth.

> [!NOTE]
> **Built with AI.** The code, tests, docs and art were produced with AI tools (Claude Code, ChatGPT image generation),
> directed and reviewed by a Metaplay engineer, with the review focused on the shared game logic and the Metaplay SDK
> integration.

**Play it in your browser:** a deployment of this sample runs at
<https://silver-cobras-hunt-quickly-public.p2-eu.metaplay.dev/>. No install or account setup is needed; matches pair you
with other visitors or with a bot.

## Why a card game on Metaplay

Metaplay's architecture makes it easy to build a game whose core loop and metagame depend on each other, and a
collectible card game is a clear example: a player's collection decides the deck they take into a match, and the match
result changes the collections of both players. On Metaplay, the match and both player accounts run as entities in the
same server cluster, on game logic shared with the client. The match reads the players' state when it is formed and
writes the result back to both accounts exactly once.

Because a match moves value between players, it has to be cheat-proof. The server runs the rules and is the only writer
of the match state. A client sends intents, which the server validates, and it never receives the opponent's hand or
deck order. The result travels from server to server and never through a client.

Both loops are built on the same pattern: a model, and actions that change it, written once in shared C#. In the match,
the server decides and issues each action, and each client runs the same action on its own copy of the match. There is
no separate client code for updating the board and no second copy of the rules to drift from the server's, and the
network carries small actions rather than state. A client's copy can only disagree with the server's through a game bug
or a tampered client, and the SDK's checksums catch it. The server's authoritative state is never affected: the client
discards its copy and resumes from the server's. The metagame uses the pattern with the roles reversed: the client runs
a player action on the player's own state at once, and the server runs it again to validate it.

The same shape suits any game where live multiplayer play and persistent player progression affect each other: PvP
battlers, competitive puzzle games, or co-op games with shared rewards.

## What the sample shows

The sample shows how to:

- **Share the rules between server and client.** The match is a model and actions that the server issues and both
  clients re-execute; the player account uses the same pattern, predicted on the client and validated on the server
  ([`rules.md`](Docs/rules.md), [`match.md`](Docs/match.md), [`meta.md`](Docs/meta.md)).
- **Keep each player's hand secret from the other.** The server's copy of the match holds both hands and both deck
  orders in one member the SDK never sends to a client. Each player receives only their own hand: whole when they join
  the match, then one card at a time as it changes, with `ExecuteActionPerMember`
  ([`hidden-information.md`](Docs/hidden-information.md)).
- **Validate every move on the server.** A client's moves are requests, checked against the match as it stands when
  they arrive. A refused move is answered explicitly, and a client never changes the match state itself
  ([`protocol.md`](Docs/protocol.md#the-timeline-has-one-writer)).
- **Move a value between two player entities exactly once.** The match delivers the result to both accounts, with
  retries, and each account applies a given match only once
  ([`match.md`](Docs/match.md#delivering-the-result-and-the-heist)).
- **Finish every match that starts.** A bot covers a seat whose player disconnects or stops acting, and a match that
  loses all its players is played out to a real result, so leaving cannot avoid a loss
  ([`match.md`](Docs/match.md#when-players-stop-playing), [`bots.md`](Docs/bots.md)).
- **Matchmake two live players** with a singleton service entity that pairs by rating and deck strength and falls back
  to a labelled bot. The SDK's async matchmaker is built for asynchronous PvP, where a player fights a stored snapshot
  of another, so a live match needs a queue of its own
  ([`matchmaking.md`](Docs/matchmaking.md#why-not-the-sdks-matchmaker)).
- **Keep a global leaderboard in a persisted service entity**, filled on first start from existing accounts with the
  SDK's paginated database reader ([`community.md`](Docs/community.md)).
- **Test the rules and the secrecy model at scale.** Self-play runs thousands of bot-versus-bot games and checks that
  the rules are deterministic, that every client's copy of the match agrees with the server's, and that nothing a
  player receives reveals the other player's hand ([`bots.md`](Docs/bots.md#self-play)).
- **Run the Metaplay client in the browser** with Blazor WebAssembly, including an offline mode for the screens outside
  a match ([`client.md`](Docs/client.md)).
- **Author cards as data.** Cards, keywords and their effects are CSV game config over a small set of effect primitives,
  validated at build time ([`effects.md`](Docs/effects.md)).
- **Extend the LiveOps Dashboard** with game-specific player cards and a `[PlayerDashboardAction]`
  (`Backend/Dashboard/src/gameSpecific.ts`).

## Prerequisites

- The .NET SDK 10.0.100 or a later feature band (`global.json`).
- The [Metaplay CLI](https://github.com/metaplay/cli), for `metaplay dev server` and the dashboard commands.
- Node.js and pnpm, only to work on the dashboard.

## Run it

Everything below runs from this directory. `dotnet build StickyPaws.slnx` builds the whole sample and the SDK projects
it references. The first server run creates a local database, so it takes longer than later ones.

**The game server and the web client** — two terminals, then Home → pick a deck → **Practice** or **Ranked**:

```bash
metaplay dev server                          # or: dotnet run --project Backend/Server
dotnet run --project WebClient/WebClient.csproj   # → http://localhost:5290
```

In Rider, the checked-in **Server + Client** configuration under `.run/` does both. A locally run server lengthens the
turn deadline well past the shipped 60 s, so the clock you see is not the shipped clock.

**Offline mode** — run only the client and open <http://localhost:5290/?offline>. The collection, the deckbuilder
and the locks run against an in-process offline server, kept in the browser between reloads; **Reset** in the top bar
is the way back to a fresh account. Matches never run offline. `/dev/board?scene=<name>&offline` draws the real
board in a seeded state, with no server and no match.

**The LiveOps Dashboard** is on <http://localhost:5550> while the game server runs, served from the prebuilt copy in
`Backend/PrebuiltDashboard/`. To work on it: `metaplay dev dashboard` (vite on <http://localhost:5551>) or
`metaplay build dashboard` (then served on 5550).

**The bot client** — `metaplay dev botclient`, against a running server, starts load-test bots that log in and play
over the real protocol (`Backend/BotClient/`).

**The published client** — `python3 run_e2e.py --trimmed --no-test` serves the trimmed client players get, and
prints its URL. Deploying is in [`Backend/Deployments/README.md`](Backend/Deployments/README.md).

The sample is built on the [Blazor starter project](https://github.com/metaplay-shared/blazor-starter-project): its project structure, the web client's
base (`WebClientBase/`), the browser test harness and the tools are the starter project's, and documented there.

## Documentation

Start with [`match.md`](Docs/match.md), then [`hidden-information.md`](Docs/hidden-information.md) and
[`protocol.md`](Docs/protocol.md). [`game-design.md`](Docs/game-design.md) is the reference for the game's rules.

The docs use the game's own terms: the *Heist* is the post-match rank pick, the *Den* is a player's life total, a
*Weather* is a per-match rules modifier, and a deck's *Power Score* is the sum of its card ranks.

| Doc | Covers | Metaplay features shown |
|---|---|---|
| [`match.md`](Docs/match.md) | The match entity: seats, turn flow, disconnects and bot cover, the actor's lifetime, result delivery | Ephemeral multiplayer entity, entity asks, synchronized server actions on the player, runtime options |
| [`hidden-information.md`](Docs/hidden-information.md) | What is secret, how a hand reaches only its owner, and the tests that prove nothing leaks | `ServerOnly` members, per-member private state, `ExecuteActionPerMember` |
| [`protocol.md`](Docs/protocol.md) | The SDK's constraints and defaults the match depends on, and how each one fails | The replicated timeline, action execution on followers, message codes, entity lifetime and wakelocks |
| [`rules.md`](Docs/rules.md), [`engine-rulings.md`](Docs/engine-rulings.md) | The rules engine as a pure, seeded state machine, and the rulings it makes | Deterministic shared game logic |
| [`effects.md`](Docs/effects.md) | Card behaviour as data: effect primitives, triggers, resolution | Game config libraries, build-time content validation |
| [`matchmaking.md`](Docs/matchmaking.md) | The queue, rating and deck-strength bands, bot fill | Singleton service entities |
| [`bots.md`](Docs/bots.md) | Bot seats, bot profiles, the self-play harness | Driving the match model without a server |
| [`meta.md`](Docs/meta.md) | The player account: collection, decks, locks, card gifts | `PlayerModel`, player actions |
| [`community.md`](Docs/community.md) | The ranked leaderboard and live player counts | A persisted entity, a paginated database read |
| [`client.md`](Docs/client.md) | The Blazor client: screens, the board, connection loss | `Metaplay.Client` on `net10.0-browser`, offline mode |
| [`game-design.md`](Docs/game-design.md), [`art.md`](Docs/art.md) | The game itself, and its art direction | None |

To work on the code, read [`AGENTS.md`](AGENTS.md): the layout, build and test commands, port offsets for running two
copies, the development-only shortcuts, and the regeneration steps that are easy to forget.

### Where to look in the code

- **The shared rules** — `SharedCode/Match/MatchModel.cs`, the actions in `SharedCode/Match/Actions/`, and the host
  that stages them, `Backend/Server/Match/MatchActor.Actions.cs` (`StageMatchAction`).
- **Hidden information** — `SharedCode/Match/MatchSecrets.cs` (the secret root),
  `SharedCode/Match/Rules/SecretOps.cs` (its only reader and writer), `SharedCode/Match/Actions/MatchOwnCardActions.cs`
  (the per-member actions). `MatchModel.Secret` is `internal` to the shared rules, and within them it is read only
  through `SecretOps`, by convention.
- **The player account** — `SharedCode/Player/PlayerModel.cs` and the actions in `SharedCode/Player/PlayerActions.*.cs`.
- **Validating client moves** — `SharedCode/Match/Intents/MatchIntents.cs` (`MatchIntent.Prepare`).
- **The two-account result** — `Backend/Server/Match/MatchActor.Result.cs` (`DeliveryOrder`), and
  `PlayerApplyMatchResult` in `SharedCode/Player/PlayerActions.Match.cs`, applied once per match against
  `PlayerModel.AppliedMatchResults`.
- **Matchmaking** — `Backend/Server/Matchmaking/MatchmakerActor.cs` and the pure `MatchmakingPolicy.cs`.
- **Bots and self-play** — `SharedCode/Match/Bots/BotPolicy.cs` and `Backend/SharedCode.Tests/SelfPlay/`.
- **The Blazor client** — `WebClient/Components/Pages/MatchBoardPage.razor` and
  `WebClient/Components/Board/BoardView.razor`, on the starter project's `WebClientBase/`.

## Known limits

Read these before copying a part of the sample into a game.

- **A deploy ends every match in flight.** Matches are ephemeral entities with no database row, so a rolling deploy or
  a lost node ends the match for both players, records nothing and moves no ranks.
  [`match.md`](Docs/match.md#actor-lifetime) explains the trade and when a real game would persist its matches.
- **`PlayerModel.AppliedMatchResults` grows forever.** It is the idempotence key for result delivery, one `EntityId`
  per completed match for the life of the account. A real game needs an age stamp, or a cap with eviction.
- **The leaderboard is small-sample.** All participants fit in one persisted state and are sorted for each read; a
  large player count needs a real leaderboard index.
- **There are no analytics events.**

## License

Copyright Metaplay. Sticky Paws is licensed under the [Apache License 2.0](LICENSE). The Metaplay SDK it builds on,
including its dashboard packages, is licensed separately, under the Metaplay SDK License. Third-party fonts keep their
own licenses, which are next to them.
