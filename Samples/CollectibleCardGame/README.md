# Sticky Paws

A player-versus-player collectible card game, built as a Metaplay sample: a server-authoritative game whose client runs
entirely in the browser as a Blazor WebAssembly app.

> [!NOTE]
> **Built with AI.** The code, tests, docs and art were produced with AI tools (Claude Code, ChatGPT image generation),
> directed and reviewed by a Metaplay engineer, with the review focused on the shared game logic and the Metaplay SDK
> integration.

Two players, singleton 25-card decks, five animal clans, and a twist that makes the collection and the ladder one
circulating economy — after a ranked match the winner steals a rank from a card the loser actually played. Putting your
best cards on the table is both how you win and what you wager.

The whole loop is playable, against a person or a computer: log in, receive a starter collection of 45 cards and six
starter decks, browse the collection, lock cards, build a deck of your own, and take it into the **ranked queue**,
which pairs you with somebody in your rating and Power Score band or seats a labelled bot when nobody suitable turns
up. The wager is stated before the mulligan, and on a tier that pays, the winner takes a rank from a card the loser
played while the loser's copy drops one. **Practice** mints a bot table on the spot with nothing at stake. The pool has
65 collectible cards and six Weathers; every few completed matches gift a random unowned card, and Home shows a global
ranked leaderboard and live player counts.

**Play it in your browser:** a deployment of this sample runs at
<https://silver-cobras-hunt-quickly-public.p2-eu.metaplay.dev/>. No install or account setup is needed; matches pair you
with other visitors or with a bot.

**The game itself is deliberately minimal, and it is not meant to be fun.** The cards, keywords and Weathers are just
enough to exercise every system end to end: hidden hands, targeted effects, peeks, deadlines, bots and the post-match
economy. There is no attempt at interesting mechanics, card synergies or balance. A real game would build those on
this foundation; what the sample offers is the technical skeleton a card game needs, built on Metaplay.

## What it demonstrates about Metaplay

- **A multiplayer entity whose replicated model *is* the rules engine.** The match model carries the game, the rules
  are the actions the server issues, and both clients re-execute them — so the SDK's checksum covers the game rather
  than a picture of it. That trade holds because almost everything in this game is public; face-down cards, secret
  choices or on-draw triggers would flip it ([`Docs/hidden-information.md`](Docs/hidden-information.md)).
  [`Docs/rules.md`](Docs/rules.md).
- **Hidden information on a replicated timeline.** One `ServerOnly` root, reached only through `SecretOps`; a seat's
  hand delivered as a subscribe-time baseline and then maintained by **addressed timeline operations** — one member
  receives the real operation, every other a no-op, at one position under one checksum.
  [`Docs/hidden-information.md`](Docs/hidden-information.md).
- **An economy transaction between two player entities** — a rank moved out of one account and into another, applied
  exactly once per side, loser first. A dealt match always finishes: a table that loses its players is played out by
  bots to a real result, so leaving cannot dodge the Heist. [`Docs/match.md`](Docs/match.md).
- **A Blazor WebAssembly client** on the SDK's browser flavour, with an offline mode for the screens that need no
  server. [`Docs/client.md`](Docs/client.md).

**One API here is not in a released SDK.** The addressed operations call `ExecuteActionPerMember`, an SDK change that is
not in the Metaplay SDK R38 release. Until it ships, the sample carries a copy of the SDK's multiplayer-entity actor
base with that change in `Backend/Server/SdkPreview/` — read [its README](Backend/Server/SdkPreview/README.md) before
copying this pattern. Everything else builds against the released SDK, installed at the samples repository root
and referenced by relative path (`../../MetaplaySDK`).

### SDK features used

| Feature | Where |
|---|---|
| Ephemeral multiplayer entity, per-member private state, `ServerOnly` members | `Backend/Server/Match/`, `SharedCode/Match/` |
| Synchronized server actions and entity asks between actors | `Backend/Server/Player/`, `SharedCode/Player/PlayerActions.Match.cs` |
| Singleton service entities | `Backend/Server/Matchmaking/`, `Backend/Server/Community/` |
| A persisted entity and a paginated database read | `Backend/Server/Community/CommunityActor.cs` |
| Game configs from CSV sheets, with content validation | `GameConfigSource/`, `SharedCode/GameConfigs/` |
| Runtime options | `Backend/Server/Match/MatchOptions.cs`, `Backend/Server/Config/` |
| `[PlayerDashboardAction]` and a custom LiveOps Dashboard | `Backend/Dashboard/src/gameSpecific.ts` |
| Offline mode and the browser (WebAssembly) client flavour | `ClientBase/`, `Client/` |
| The bot-client framework, as a load client | `Backend/BotClient/` |

## Prerequisites

- The .NET SDK 10.0.100 or a later feature band (`global.json`).
- The Metaplay SDK, installed at the samples repository root. It takes an account in the
  [Metaplay Developer Portal](https://portal.metaplay.dev/) and the [Metaplay CLI](https://github.com/metaplay/cli),
  which also provides `metaplay dev server` and the dashboard commands:

  ```bash
  metaplay init sdk --sdk-version=38        # at the samples repository root
  ```

- Node.js and pnpm, only to work on the dashboard (`pnpm install` once, at the samples repository root).

## Running it

Everything below runs from this directory. `dotnet build StickyPaws.slnx` builds the whole sample and the SDK projects
it references. The first server run creates a local database, so it takes longer than later ones.

**The game, live** — two terminals, then Home → pick a deck → **Practice** or **Ranked**:

```bash
metaplay dev server                          # or: dotnet run --project Backend/Server
dotnet run --project Client/Client.csproj    # → http://localhost:5290
```

In Rider, the checked-in **Server + Client** configuration under `.run/` does both. A locally run server lengthens the
turn deadline well past the shipped 60 s, so the clock you see is not the shipped clock.

**The meta loop, with no server** — run only the client and open <http://localhost:5290/?env=offline>. The collection,
the deckbuilder and the locks run against an in-process offline server, persisted to browser `localStorage`; **Reset**
in the top bar is the way back to a fresh account. Matches are never hosted offline.
`/dev/board?scene=<name>&env=offline` draws the real board in a seeded state, with no server and no match.

**The LiveOps Dashboard** is on <http://localhost:5550> while the game server runs, served from the prebuilt copy in
`Backend/PrebuiltDashboard/`. To work on it: `metaplay dev dashboard` (vite on <http://localhost:5551>) or
`metaplay build dashboard` (then served on 5550). The sample adds newcomer-shield, collection and
saved-deck cards to the stock player page, plus a decorator that shows card ids as card names; `gameSpecific.ts` is the
whole integration.

**The deployed shape** — the web client served off the game server:

```bash
dotnet publish Client/Client.csproj -c Release
metaplay dev server                          # → http://localhost:5560 serves the app
```

`Tools/build-cloud-image.sh <tag>` builds a server image that includes the published client; see
[`Backend/Deployments/README.md`](Backend/Deployments/README.md). Tests, port offsets for running two copies, and the
developer-only levers (`?dev=shield` on Home, `?dev=ranks` on Collection, **Win now** in the match menu) are in
[`AGENTS.md`](AGENTS.md).

## Where to look in the code

- **The replicated rules engine** — `SharedCode/Match/MatchModel.cs`, the actions in `SharedCode/Match/Actions/`, and
  the host that stages them, `Backend/Server/Match/MatchActor.Actions.cs` (`StageMatchAction`).
- **Hidden information** — `SharedCode/Match/MatchSecrets.cs` (the one `ServerOnly` root),
  `SharedCode/Match/Rules/SecretOps.cs` (the only reader and writer), `SharedCode/Match/Actions/MatchOwnCardActions.cs`
  (the addressed operations), and `Backend/SharedCode.Tests/Match/SecretAccessTests.cs` (what enforces it).
- **Intents and refusals** — `SharedCode/Match/Intents/MatchIntents.cs` (`MatchIntent.Prepare`).
- **The two-account transaction** — `Backend/Server/Match/MatchActor.Result.cs` (`DeliveryOrder`: loser first), and
  `PlayerApplyMatchResult` in `SharedCode/Player/PlayerActions.Match.cs`, applied once per match against
  `PlayerModel.AppliedMatchResults`.
- **Matchmaking** — `Backend/Server/Matchmaking/MatchmakerActor.cs` and the pure `MatchmakingPolicy.cs`.
- **Bots** — `SharedCode/Match/Bots/BotPolicy.cs`, and the self-play harness in `Backend/SharedCode.Tests/SelfPlay/`.
- **The Blazor client** — `Client/Components/Pages/MatchBoardPage.razor`, `Client/Components/Board/BoardView.razor`,
  `Client/Components/Heist/HeistScreen.razor`, and the game-agnostic shell in `ClientBase/`.
- **The vendored SDK base** — `Backend/Server/SdkPreview/`.

## Limits worth knowing

Read these before copying anything out of the sample.

1. **`PlayerModel.AppliedMatchResults` grows forever.** It is the idempotence key for result delivery — one `EntityId`
   per completed match, for the life of the account — and since the match is not persisted there is no retention
   window to prune it against. A real game needs an age stamp, or a cap with eviction.
2. **A deploy ends every match in flight.** Matches are ephemeral entities with no database row: a rolling deploy or a
   lost node tells both players, records nothing and moves no ranks. [`Docs/match.md`](Docs/match.md#actor-lifetime)
   sizes that trade and says when a real game would choose otherwise.
3. **The web client is hosted by the game server.** `WebClientHosting` serves the published `wwwroot` off the public
   web host, which is a fine development convenience and the wrong production shape: a client has to be updatable
   separately from the server, through a CDN.
4. **The starter-deck balance matrix is a measurement, not a pass.** `StarterDeckBalanceTests` pins only that no deck
   is hopeless; over six decks and 6,000 games, three rows sit twelve to sixteen points off even. The explicit
   `StarterDeckWinRateMatrix_Printed` test reprints the matrix, and diffing a fresh print against the previous one is
   what catches an authoring regression.
5. **The match is landscape-only.** Compact landscape reflows the controls; portrait shows a rotation note.
6. **daisyUI and Tailwind come from CDN tags**, the first thing a real project would self-host.

## Follow-up work

The sample is complete as a game and as a demonstration. These are the natural ways to extend it:

- **Seasons and a ladder screen.** Rating already moves, bands the queue and orders the Home leaderboard; seasons would
  soft-reset it — never ranks — and a profile screen would show season standing and a lifetime record.
- **A friendly mode.** A direct challenge with no stakes and every rank flattened to 1, which would double as a
  tournament-legal flat mode. Ranked stays matchmaker-only, because a pairing a player can steer is a route to hand a
  chosen account a rank.
- **A per-opponent steal cap** — say, one Heist gain per opponent per day — as the third win-trading defence beside
  matchmaker-only pairing and rating at stake, enforced at the Heist payout, with the queue preferring a partner the
  player has not already beaten as a tie-break.
- **Fast-forward.** Once the opponent's seat is covered by a bot, the remaining player could resolve the play-out at
  once and keep their Heist pick, rather than watching it.
- **Account progression and cosmetics.** Levels that widen the toolbox — extra lock slots, a choice among card offers —
  and a cosmetic currency from rank-5 overflow and daily wins, spent on card backs and Den skins. Match-count card
  gifts cover acquisition today; progression would never grant ranks.
- **Grudge matches.** After a Heist, a token to challenge the thief to a rematch with the stolen rank as the stake.
- **More content.** Card-borne auras, enchantment or relic card types, positional combat, a larger Weather pool with
  rarer swingy entries, and a ranked Power Score budget that would cap the collection gap mechanically.
- **Balance and test tooling.** Aggregating self-play records into per-card win rates and a clan matchup table, and a
  development endpoint that expires a match's deadlines on demand, so every lapse — not only the turn deadline, the
  Heist pick and a delivery retry — is proven end to end rather than as policy.
- **Analytics events**, which the sample does not emit.

## Where to read next

1. [`Docs/game-design.md`](Docs/game-design.md) — what the game *is*: rules, clans, the Heist, the economy.
2. [`Docs/README.md`](Docs/README.md) — the index of the per-system design documents.
3. [`AGENTS.md`](AGENTS.md) — the developer guide: layout, build and test commands, the fixture inventory, and the
   regeneration steps that are easy to forget.

[`Docs/art.md`](Docs/art.md) is the art direction, and [`Client/wwwroot/art/README.md`](Client/wwwroot/art/README.md)
records how the art was made, its size and its loading behaviour.
