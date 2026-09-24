# Game rules

[Documentation index](../README.md#documentation)

Table Stakes is a trick-taking card game for exactly four players. This doc states the rules as the engine
implements them, and which information is public and which is private. How a match runs on the server is in
[`match.md`](match.md).

The rules are pure functions in [`SharedCode/Match/MatchRules.cs`](../SharedCode/Match/MatchRules.cs), and the
state machine that applies them is `MatchEngine` in
[`SharedCode/Match/MatchEngine.cs`](../SharedCode/Match/MatchEngine.cs).

## Cards

- A standard 52-card deck without jokers (`Deck` in [`SharedCode/Match/Cards.cs`](../SharedCode/Match/Cards.cs)).
- Four suits: clubs, diamonds, hearts, spades. No suit outranks another except the trump suit.
- Ranks from highest to lowest: A, K, Q, J, 10, 9, 8, 7, 6, 5, 4, 3, 2. Ace is always high.
- Cards have no special abilities.

## Setup

`MatchEngine.Create` deals a game from a seed in this fixed order:

1. Shuffle the deck.
2. Draw the starting leader seat.
3. Deal five consecutive cards to each seat, in seat order.
4. Reveal the next card. Its suit is the **trump suit** for the whole game.

The revealed card and the 31 undealt cards take no further part in the game. The order is fixed so that the same
seed produces the same deal in every host. The seed itself must be unguessable (see
[`match.md`](match.md#the-deal-seed)).

## Playing a trick

A trick is four cards, one from each seat.

- The seat that starts the trick is the **leader**. The leader may play any card.
- The suit of the leader's card is the **led suit**.
- The other seats play in seat order after the leader.
- A seat that holds at least one card of the led suit must play that suit. A seat that holds none may play any
  card.
- A turn is exactly one card. There are no passes, draws, bids or other actions.

`MatchRules.IsLegalPlay` and `MatchRules.GetLegalPlays` implement the follow-suit rule. The server validates moves
with them and the client highlights playable cards with the same functions.

## Winning a trick

When the fourth card is played:

- If any trump was played, the highest trump wins.
- Otherwise, the highest card of the led suit wins.
- A card that is neither trump nor of the led suit cannot win, however high its rank.

The winner gains one trick and leads the next trick. No cards are drawn during the game.
`MatchRules.ResolveTrick` and `MatchRules.Beats` implement this.

### Example

Trump is spades.

| Seat | Card |
|---|---|
| North (leads) | 10♥ |
| East | A♥ |
| South | 2♠ |
| West | K♠ |

Hearts were led. A♥ is the highest heart, but two spades were played, so the highest spade wins. West wins the
trick with K♠ and leads the next trick.

## End of the game and standings

The game ends after the fifth trick. `MatchRules.ComputeStandings` orders all four seats, best first:

1. More tricks won ranks higher.
2. Between seats with the same number of tricks, the seat that won a trick most recently ranks higher.
3. Between seats that won no tricks, the lower seat index ranks higher.

The third rule only ever orders seats that won nothing. Each `SeatStanding` records which of the three rules put it
ahead of the seat below it, so the results screen can explain a tie without recomputing the order.

### Ties

If two seats finish on the same number of tricks, the one that won the later trick wins. For example, if Anna won
tricks 1 and 3 and Dana won tricks 2 and 5, both have two tricks, and Dana ranks higher because trick 5 is later
than trick 3.

## Public and private information

| Information | Visibility |
|---|---|
| Trump card and trump suit | Public |
| Seat on turn, current leader, starting leader | Public |
| Every card played so far, with the seat that played it, in order | Public |
| Number of cards remaining in each hand | Public |
| Tricks won by each seat, and the final standings | Public |
| Each seat's remaining cards | Private to that seat |
| Deck order, undealt cards, RNG position and deal seed | Server only |

A played card becomes public the moment it is played. The full play history is public, not only the current trick,
so a reconnecting player sees every card played before they returned.

How the public board, the server-only state and each seat's hand are carried over the network is in
[`match.md`](match.md#public-state-and-private-state).
