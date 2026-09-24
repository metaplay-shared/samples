using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="MatchRules"/>: the deck, card ordering, the follow-suit rule, trick resolution, the
    /// currently winning card, and the standings order. Tests state their cards explicitly, and the one test
    /// that shuffles puts its seed in the assertion message.
    /// </summary>
    [TestFixture]
    public class MatchRulesTests
    {
        static Card C(Suit suit, Rank rank) => new Card(suit, rank);

        static List<PlayRecord> Trick(params (int Seat, Card Card)[] plays)
        {
            List<PlayRecord> trick = new List<PlayRecord>(plays.Length);
            foreach ((int seat, Card card) in plays)
                trick.Add(new PlayRecord(seat, card));
            return trick;
        }

        #region Cards and deck

        [Test]
        public void Deck_HasFiftyTwoDistinctCards()
        {
            List<Card> deck = Deck.CreateOrdered();

            Assert.That(deck, Has.Count.EqualTo(52));
            Assert.That(new HashSet<Card>(deck), Has.Count.EqualTo(52));
        }

        [Test]
        public void Deck_OrderIsDeterministicSuitThenRank()
        {
            List<Card> first  = Deck.CreateOrdered();
            List<Card> second = Deck.CreateOrdered();

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first[0], Is.EqualTo(C(Suit.Clubs, Rank.Two)));
            Assert.That(first[12], Is.EqualTo(C(Suit.Clubs, Rank.Ace)));
            Assert.That(first[13], Is.EqualTo(C(Suit.Diamonds, Rank.Two)));
            Assert.That(first[51], Is.EqualTo(C(Suit.Spades, Rank.Ace)));

            for (int ndx = 1; ndx < first.Count; ndx++)
                Assert.That(first[ndx - 1].CompareTo(first[ndx]), Is.LessThan(0), $"deck is not in ascending order at index {ndx}");
        }

        [Test]
        public void Card_EqualityIsByIdentity()
        {
            Card sevenOfSpades = C(Suit.Spades, Rank.Seven);

            Assert.That(sevenOfSpades == new Card(Suit.Spades, Rank.Seven), Is.True);
            Assert.That(sevenOfSpades.GetHashCode(), Is.EqualTo(new Card(Suit.Spades, Rank.Seven).GetHashCode()));
            Assert.That(sevenOfSpades, Is.Not.EqualTo(C(Suit.Hearts, Rank.Seven)));
            Assert.That(sevenOfSpades, Is.Not.EqualTo(C(Suit.Spades, Rank.Eight)));
        }

        #endregion

        #region Legality

        [Test]
        public void Legality_LeaderMayPlayAnything()
        {
            List<Card> hand = new List<Card> { C(Suit.Hearts, Rank.Two), C(Suit.Spades, Rank.Ace) };

            foreach (Card card in hand)
                Assert.That(MatchRules.IsLegalPlay(hand, ledSuit: null, card), Is.True, $"{card} should be legal as a lead");

            Assert.That(MatchRules.GetLegalPlays(hand, ledSuit: null), Is.EqualTo(hand));
        }

        [Test]
        public void Legality_MustFollowLedSuitWhenAble()
        {
            List<Card> hand = new List<Card>
            {
                C(Suit.Hearts, Rank.Three),
                C(Suit.Hearts, Rank.King),
                C(Suit.Spades, Rank.Ace),
                C(Suit.Clubs, Rank.Two),
            };

            Assert.That(MatchRules.IsLegalPlay(hand, Suit.Hearts, C(Suit.Hearts, Rank.Three)), Is.True);
            Assert.That(MatchRules.IsLegalPlay(hand, Suit.Hearts, C(Suit.Hearts, Rank.King)), Is.True);
            Assert.That(MatchRules.IsLegalPlay(hand, Suit.Hearts, C(Suit.Spades, Rank.Ace)), Is.False);
            Assert.That(MatchRules.IsLegalPlay(hand, Suit.Hearts, C(Suit.Clubs, Rank.Two)), Is.False);

            Assert.That(MatchRules.GetLegalPlays(hand, Suit.Hearts), Is.EqualTo(new List<Card>
            {
                C(Suit.Hearts, Rank.Three),
                C(Suit.Hearts, Rank.King),
            }));
        }

        [Test]
        public void Legality_AnyCardWhenVoidInLedSuit()
        {
            List<Card> hand = new List<Card>
            {
                C(Suit.Spades, Rank.Ace),
                C(Suit.Clubs, Rank.Two),
            };

            foreach (Card card in hand)
                Assert.That(MatchRules.IsLegalPlay(hand, Suit.Hearts, card), Is.True, $"{card} should be legal when void in hearts");

            Assert.That(MatchRules.GetLegalPlays(hand, Suit.Hearts), Is.EqualTo(hand));
        }

        #endregion

        #region Trick resolution

        [Test]
        public void ResolveTrick_HighestOfLedSuitWinsWhenNoTrumpIsPlayed()
        {
            List<PlayRecord> trick = Trick(
                (0, C(Suit.Hearts, Rank.Ten)),
                (1, C(Suit.Hearts, Rank.Ace)),
                (2, C(Suit.Hearts, Rank.Three)),
                (3, C(Suit.Hearts, Rank.King)));

            Assert.That(MatchRules.ResolveTrick(trick, trumpSuit: Suit.Spades), Is.EqualTo(1));
        }

        [Test]
        public void ResolveTrick_HighestTrumpWinsWhenTrumpsWerePlayedButNotLed()
        {
            // The example in docs/game-rules.md, "Winning a trick". Hearts are led and two seats play spades, which
            // are trump. The higher spade wins, although the ace of hearts is the highest card in the trick.
            List<PlayRecord> trick = Trick(
                (0, C(Suit.Hearts, Rank.Ten)),
                (1, C(Suit.Hearts, Rank.Ace)),
                (2, C(Suit.Spades, Rank.Two)),
                (3, C(Suit.Spades, Rank.King)));

            Assert.That(MatchRules.ResolveTrick(trick, trumpSuit: Suit.Spades), Is.EqualTo(3));
        }

        [Test]
        public void ResolveTrick_TrumpedInFirstStillLosesToAHigherLaterTrump()
        {
            List<PlayRecord> trick = Trick(
                (2, C(Suit.Clubs, Rank.Nine)),
                (3, C(Suit.Diamonds, Rank.Queen)),
                (0, C(Suit.Clubs, Rank.Ace)),
                (1, C(Suit.Diamonds, Rank.Three)));

            Assert.That(MatchRules.ResolveTrick(trick, trumpSuit: Suit.Diamonds), Is.EqualTo(3));
        }

        [Test]
        public void ResolveTrick_HighestCardOfTheTrickCannotWinWhenItIsNeitherTrumpNorLedSuit()
        {
            // Clubs are led and diamonds are trump. The ace of spades is the highest card played but is neither
            // trump nor the led suit, so the highest club wins.
            List<PlayRecord> trick = Trick(
                (0, C(Suit.Clubs, Rank.Four)),
                (1, C(Suit.Spades, Rank.Ace)),
                (2, C(Suit.Clubs, Rank.Nine)),
                (3, C(Suit.Hearts, Rank.King)));

            Assert.That(MatchRules.ResolveTrick(trick, trumpSuit: Suit.Diamonds), Is.EqualTo(2));
        }

        [Test]
        public void ResolveTrick_LeaderWinsWhenEveryoneElseIsOffSuitAndNobodyTrumps()
        {
            List<PlayRecord> trick = Trick(
                (1, C(Suit.Clubs, Rank.Two)),
                (2, C(Suit.Spades, Rank.Ace)),
                (3, C(Suit.Hearts, Rank.Ace)),
                (0, C(Suit.Spades, Rank.King)));

            Assert.That(MatchRules.ResolveTrick(trick, trumpSuit: Suit.Diamonds), Is.EqualTo(1));
        }

        [Test]
        public void ResolveTrick_HighestTrumpWinsWhenTrumpIsLed()
        {
            List<PlayRecord> trick = Trick(
                (0, C(Suit.Spades, Rank.Five)),
                (1, C(Suit.Spades, Rank.Jack)),
                (2, C(Suit.Hearts, Rank.Ace)),
                (3, C(Suit.Spades, Rank.Nine)));

            Assert.That(MatchRules.ResolveTrick(trick, trumpSuit: Suit.Spades), Is.EqualTo(1));
        }

        #endregion

        #region The currently-winning card

        [Test]
        public void BestPlay_OfALoneLeadIsTheLead()
        {
            List<PlayRecord> plays = Trick((2, C(Suit.Hearts, Rank.Four)));

            Assert.That(MatchRules.GetBestPlayNdx(plays, Suit.Spades), Is.EqualTo(0));
        }

        [Test]
        public void BestPlay_TracksTheTrickAsEachCardLands()
        {
            // Hearts are led and spades are trump. The result must be correct after every card, because the
            // client marks the winning card during the trick and a seat yet to play asks what it must beat.
            Card lead      = C(Suit.Hearts, Rank.Four);
            Card higher    = C(Suit.Hearts, Rank.Ten);
            Card offSuit   = C(Suit.Diamonds, Rank.Ace);
            Card lowTrump  = C(Suit.Spades, Rank.Two);

            Assert.That(MatchRules.GetBestPlayNdx(Trick((0, lead), (1, higher)), Suit.Spades), Is.EqualTo(1), "a higher card of the led suit takes it");
            Assert.That(MatchRules.GetBestPlayNdx(Trick((0, lead), (1, higher), (2, offSuit)), Suit.Spades), Is.EqualTo(1), "an off-suit ace cannot take it");
            Assert.That(MatchRules.GetBestPlayNdx(Trick((0, lead), (1, higher), (2, offSuit), (3, lowTrump)), Suit.Spades), Is.EqualTo(3), "the lowest trump takes it from any plain card");
        }

        [Test]
        public void BestPlay_AgreesWithResolveTrickOnEveryFullTrick()
        {
            // ResolveTrick calls GetBestPlayNdx. This test checks that both name the same winning seat for random
            // full tricks under every trump suit.
            RandomPCG  rng  = RandomPCG.CreateFromSeed(4242UL);
            List<Card> deck = Deck.CreateOrdered();

            foreach (Suit trumpSuit in new Suit[] { Suit.Clubs, Suit.Diamonds, Suit.Hearts, Suit.Spades })
            {
                for (int iteration = 0; iteration < 1000; iteration++)
                {
                    rng.ShuffleInPlace(deck);
                    List<PlayRecord> trick = new List<PlayRecord>(MatchRules.CardsPerTrick);
                    for (int seat = 0; seat < MatchRules.CardsPerTrick; seat++)
                        trick.Add(new PlayRecord(seat, deck[seat]));

                    Assert.That(trick[MatchRules.GetBestPlayNdx(trick, trumpSuit)].Seat,
                        Is.EqualTo(MatchRules.ResolveTrick(trick, trumpSuit)),
                        $"seed 4242, trump {trumpSuit}, iteration {iteration}");
                }
            }
        }

        [Test]
        public void Beats_RanksTrumpAboveTheLedSuitAndTheLedSuitAboveEverythingElse()
        {
            Suit led   = Suit.Hearts;
            Suit trump = Suit.Spades;

            Assert.That(MatchRules.Beats(C(Suit.Spades, Rank.Two), C(Suit.Hearts, Rank.Ace), led, trump), Is.True, "trump takes it from the led suit");
            Assert.That(MatchRules.Beats(C(Suit.Hearts, Rank.Ace), C(Suit.Spades, Rank.Two), led, trump), Is.False, "the led suit does not take it back from trump");
            Assert.That(MatchRules.Beats(C(Suit.Spades, Rank.King), C(Suit.Spades, Rank.Two), led, trump), Is.True, "the higher trump takes it");
            Assert.That(MatchRules.Beats(C(Suit.Hearts, Rank.Ten), C(Suit.Hearts, Rank.Four), led, trump), Is.True, "the higher card of the led suit takes it");
            Assert.That(MatchRules.Beats(C(Suit.Diamonds, Rank.Ace), C(Suit.Hearts, Rank.Four), led, trump), Is.False, "a card that is neither trump nor of the led suit cannot win");
            Assert.That(MatchRules.Beats(C(Suit.Hearts, Rank.Four), C(Suit.Hearts, Rank.Four), led, trump), Is.False, "an equal card does not displace the one already there");
        }

        [Test]
        public void Beats_HandlesTrumpBeingTheLedSuit()
        {
            Suit trump = Suit.Hearts;

            Assert.That(MatchRules.Beats(C(Suit.Hearts, Rank.Ten), C(Suit.Hearts, Rank.Four), Suit.Hearts, trump), Is.True);
            Assert.That(MatchRules.Beats(C(Suit.Clubs, Rank.Ace), C(Suit.Hearts, Rank.Four), Suit.Hearts, trump), Is.False);
        }

        #endregion

        #region Standings

        static int[] SeatsInOrder(IReadOnlyList<SeatStanding> standings)
        {
            int[] seats = new int[standings.Count];
            for (int ndx = 0; ndx < standings.Count; ndx++)
                seats[ndx] = standings[ndx].Seat;
            return seats;
        }

        [Test]
        public void Standings_AreATotalOrderOverAllFourSeats()
        {
            // The list holds the winning seat of each trick, in trick order.
            List<SeatStanding> standings = MatchRules.ComputeStandings(new List<int> { 2, 2, 0, 3, 2 });

            Assert.That(SeatsInOrder(standings), Is.EqualTo(new int[] { 2, 3, 0, 1 }));
            Assert.That(standings[0].TricksWon, Is.EqualTo(3));
            Assert.That(standings[1].TricksWon, Is.EqualTo(1));
            Assert.That(standings[2].TricksWon, Is.EqualTo(1));
            Assert.That(standings[3].TricksWon, Is.EqualTo(0));
            for (int position = 0; position < standings.Count; position++)
                Assert.That(standings[position].Position, Is.EqualTo(position));
        }

        [Test]
        public void Standings_TieAtFirstPlaceGoesToTheMostRecentTrick()
        {
            // The example in docs/game-rules.md, "Ties": Anna (seat 0) won tricks 1 and 3, and Dana (seat 3) won
            // tricks 2 and 5. Dana ranks first because she won the later trick. Seat 1 won trick 4.
            List<SeatStanding> standings = MatchRules.ComputeStandings(new List<int> { 0, 3, 0, 1, 3 });

            Assert.That(standings[0].Seat, Is.EqualTo(3));
            Assert.That(standings[0].TricksWon, Is.EqualTo(2));
            Assert.That(standings[1].Seat, Is.EqualTo(0));
            Assert.That(standings[1].TricksWon, Is.EqualTo(2));
            Assert.That(standings[0].LastTrickWonIndex, Is.EqualTo(4), "the last trick won is the one that broke the tie");
            Assert.That(standings[1].LastTrickWonIndex, Is.EqualTo(2));
        }

        [Test]
        public void Standings_FourWayTieIsBrokenByRecency()
        {
            // Five tricks cannot split evenly over four seats, so a four-way tie happens only during a game.
            // These are the standings after four tricks, one per seat, so the seats rank in reverse order of
            // the trick each won.
            List<SeatStanding> standings = MatchRules.ComputeStandings(new List<int> { 1, 3, 0, 2 });

            Assert.That(SeatsInOrder(standings), Is.EqualTo(new int[] { 2, 0, 3, 1 }));
            foreach (SeatStanding standing in standings)
                Assert.That(standing.TricksWon, Is.EqualTo(1));
        }

        [Test]
        public void Standings_SeatsThatWonNothingAreOrderedBySeatIndex()
        {
            // Seats 3 and 0 win every trick. Seats 1 and 2 won nothing, so recency cannot separate them and they
            // are ordered by seat index. Seat index is the only arbitrary rule in the order.
            List<SeatStanding> standings = MatchRules.ComputeStandings(new List<int> { 3, 0, 3, 0, 3 });

            Assert.That(SeatsInOrder(standings), Is.EqualTo(new int[] { 3, 0, 1, 2 }));
            Assert.That(standings[2].TricksWon, Is.EqualTo(0));
            Assert.That(standings[2].LastTrickWonIndex, Is.EqualTo(-1));
            Assert.That(standings[3].TricksWon, Is.EqualTo(0));
            Assert.That(standings[3].LastTrickWonIndex, Is.EqualTo(-1));
        }

        [Test]
        public void Standings_AllFourSeatsLosingIsImpossibleButAnEmptyHistoryOrdersBySeatIndex()
        {
            List<SeatStanding> standings = MatchRules.ComputeStandings(new List<int>());

            Assert.That(SeatsInOrder(standings), Is.EqualTo(new int[] { 0, 1, 2, 3 }));
        }

        #endregion
    }
}
