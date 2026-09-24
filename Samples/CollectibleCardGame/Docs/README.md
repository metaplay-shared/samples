# Design docs

How Sticky Paws is designed, one document per system. Start with [`game-design.md`](game-design.md), the game itself
— a deliberately minimal skeleton for the technology, not a finished design. The [project README](../README.md) says
what the sample demonstrates and maps the code, and [`AGENTS.md`](../AGENTS.md) is the developer guide.

| Topic | Doc |
|---|---|
| The game: rules, cards, economy | [`game-design.md`](game-design.md) |
| The match: entity, turn flow, the Heist phase, robustness | [`match.md`](match.md) |
| Protocol and SDK mechanics | [`protocol.md`](protocol.md) |
| Hidden information (private hands, open zones) | [`hidden-information.md`](hidden-information.md) |
| Matchmaking: rating and Power Score bands, bot fill | [`matchmaking.md`](matchmaking.md) |
| Bots and self-play: policy, profiles, invariants, secrecy proof | [`bots.md`](bots.md) |
| Client: board, interaction, presentation | [`client.md`](client.md) |
| Effect system: primitives, triggers, resolution | [`effects.md`](effects.md) |
| The rules: zones, turn flow, combat, determinism | [`rules.md`](rules.md) + [`engine-rulings.md`](engine-rulings.md) |
| Player state and meta screens: collection, decks, locks | [`meta.md`](meta.md) |
| Leaderboard and activity counts | [`community.md`](community.md) |
| Art direction: style, card frame, clans, motion | [`art.md`](art.md) |
