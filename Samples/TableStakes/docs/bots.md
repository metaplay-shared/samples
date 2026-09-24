# Bots

[Documentation index](../README.md#documentation) · Read first: [`match.md`](match.md)

A bot is a seat the match host plays itself. It is not a client, an account or a separate process. When a bot seat
is on turn, the host asks [`BotPolicy`](../SharedCode/Match/BotPolicy.cs) for a card and submits it through the same
validation a human move goes through. This doc covers where bots play, how they choose a card, their profiles, names
and looks, and their pacing. The match that drives bot seats is described in [`match.md`](match.md).

## Where bots play

- **Empty seats.** The matchmaker fills every seat no human takes ([`matchmaking.md`](matchmaking.md#bot-fill)). Each
  such bot plays with a profile drawn for its seat.
- **Covering a human.** A bot takes over a human seat whose grace ran out, who left, or who missed too many deadlines
  in a row.
- **Auto-play.** When a connected human's move deadline lapses, a bot plays that one card for them.
- **Play-out.** When no human is coming back, bots finish the rest of the game at once.

Cover, auto-play and play-out always use the strongest profile, because those seats play a human's own cards. How and
when they happen is in [`match.md`](match.md#when-players-stop-playing). The offline host seats the player with three
bots, using the same policy and the same config.

## Seat policy

A bot decides from a `MatchSeatView`: the public board plus one seat's own hand. The type holds exactly one hand and
one seat index, filled from the same argument, so a bot cannot read another seat's cards.

`BotPolicy` picks only from the legal cards, so a bot never plays an illegal card. Its choice is a pure function of
the view, the profile and a seed. The random stream comes from the seed, the seat and the play index, never from the
engine's own random state.

The heuristic looks one trick ahead. When leading, it prefers a trump that no unseen card outranks, then a card that
no unseen card of its suit outranks, and otherwise its cheapest card. When following, it wins the trick with the
cheapest card that does, and otherwise throws its cheapest card, keeping trumps back while a plain card will do. A
profile's mistake chance sometimes makes the bot play its second choice instead. Tests can select simpler decision
modes, and a profile published in config always uses the heuristic.

Every decision names the play index it was made for. A decision held across a think delay is refused as stale if the
table has moved on, for example after a reclaim ([`match.md`](match.md#the-play-index)).

## Profiles

A profile sets how often a bot makes a mistake and how often it takes a long think. The published profiles are the
`BotProfiles` game config entry ([`BotConfig.cs`](../SharedCode/GameConfigs/BotConfig.cs)).

- When a table is set up, each bot seat draws one of the published profiles. With none published, the seat gets the
  strongest profile.
- The drawn profiles are copied into the table's server-only state. A config publish therefore affects the next
  table, never one in progress, and a table stays readable if a profile is removed from config.
- The strongest profile (`BotProfiles.Strongest`) is defined in code: the heuristic with no deliberate mistakes.
  `BotPolicy.MayPlayForAnAbsentHuman` states the rule for playing an absent human's cards, and the strongest profile
  meets it.

## Names

Bot seats carry names from the `BotNames` game config entry. A seat plaque shows the name, and the table shows no
separate bot marker.

- The matchmaker draws a table's bot names from the roster without repeats, so one table never shows the same bot
  name twice. The offline host draws its own the same way.
- Players cannot take a bot name, including through a rename from the LiveOps Dashboard. Names are compared in a
  normalized form, so case and punctuation do not get around it ([`player.md`](player.md#name-rules)).
- A covered human seat keeps the human's name, so on screen the human still appears to be playing.
- The roster is append-only in practice ([`game-config.md`](game-config.md#editing-rules)). Adding a name does not
  rename a player who already has it.

The config build refuses a roster too small to fill a table, and the matchmaker forms no table while the active
config has fewer bot names than seats ([`matchmaking.md`](matchmaking.md#forming-a-table)).

## Cosmetics

A bot seat also wears an avatar and, sometimes, a name effect. The table composes the seat's public identity with
`BotSeatIdentity`, from the draws in [`CosmeticDraws`](../SharedCode/Cosmetics/CosmeticDraws.cs):

- A bot wears only items that are for sale, so no bot is dressed in the starting three
  ([`cosmetics.md`](cosmetics.md#the-starting-three)).
- No draw awards a **frame**. The champion frame is a tournament placement prize, and a bot wearing one would make
  that claim false.
- A config with no catalogue produces an undressed seat, not a failed deal.

The tournament's derived bots use the same draws with their own seed, so a catalogue item's share of the bots a
player meets is one number rather than one per feature, and a new avatar reaches both at once.

The table draws the looks when it deals, from its own seed and the config it was dealt under, with a separate stream
for each draw so the strength and the looks do not correlate. A table therefore keeps its bots' looks across a config
publish, and every host of the same table computes the same ones.

## Pacing

A bot move is held for a think delay before it is played. The delay is measured from when the bot seat came on turn,
not added to other processing time. Most delays fall in an ordinary band, and a profile sets how often a bot takes an
occasional longer think. The bands are table timings the host supplies
([`match.md`](match.md#timings-come-from-the-host)), so a test host with zero timings gets instant bots whatever the
config says.

A held move is not saved, so a table evicted during a think delay decides the bot move again on its next wake.

## Load-test bot client

`Backend/BotClient` is a different kind of bot: a load-testing client that logs in, queues and plays over the network,
so its seats are human seats. It reuses `BotPolicy` to choose its cards. See [`architecture.md`](architecture.md).
