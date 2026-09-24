using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for <see cref="MatchEngine"/>: the seeded deal, the turn flow, the play index, move refusals and a
    /// full game. Every test that deals puts its seed in the assertion message, so a failure can be reproduced
    /// from the message.
    /// </summary>
    [TestFixture]
    public class MatchEngineTests
    {
        // Seeds for the deals. Tests put the seed they used in their assertion messages.
        const ulong SeedA = 20260831UL;
        const ulong SeedB = 1UL;
        const ulong SeedC = 0xDEADBEEFUL;
        const ulong SeedD = 987654321987654321UL;

        static readonly ulong[] Seeds = new ulong[] { SeedA, SeedB, SeedC, SeedD, 2UL, 3UL, 42UL, 777UL };

        static readonly MetaTime T0 = MatchTestDeals.T0;

        static MatchTimings TestTimings => MatchTimings.Default;

        static MatchEngine Deal(ulong seed) => MatchEngine.Create(seed, TestTimings, T0);

        #region Helpers

        /// <summary>
        /// Plays the first legal card of the seat on turn and returns the move result. The caller must first
        /// check that the engine is awaiting a move.
        /// </summary>
        static MoveResult PlayFirstLegalCard(MatchEngine engine, MetaTime now)
        {
            int        seat  = engine.SeatOnTurn;
            List<Card> legal = engine.GetLegalPlays(seat);
            return engine.PlayCard(seat, engine.PlayIndex, legal[0], now);
        }

        /// <summary>Plays the four cards of the current trick with the first legal card at each turn.</summary>
        static void PlayFirstTrick(MatchEngine engine, MetaTime now)
        {
            for (int ndx = 0; ndx < MatchEngine.CardsPerTrick; ndx++)
                PlayFirstLegalCard(engine, now);
        }

        /// <summary>
        /// Plays a whole game to its finish with the first legal card at every turn. Time advances past each
        /// resolve pause so the game does not stall.
        /// </summary>
        static void PlayOutGreedily(MatchEngine engine, MetaTime now)
        {
            int guard = 0;
            while (!engine.IsFinished)
            {
                if (engine.TurnPhase == MatchTurnPhase.AwaitingMove)
                {
                    MoveResult result = PlayFirstLegalCard(engine, now);
                    Assert.That(result.Accepted, Is.True, $"greedy play refused: {result.Refusal}");
                }
                else
                {
                    now = engine.ResolvePauseEndsAt;
                    Assert.That(engine.Advance(now), Is.True);
                }

                if (++guard > 100)
                    Assert.Fail("game did not finish");
            }
        }

        /// <summary>Returns a card that <paramref name="seat"/> does not hold, to test the not-in-hand refusal.</summary>
        static Card CardNotHeldBy(MatchEngine engine, int seat)
        {
            IReadOnlyList<Card> hand = engine.GetHand(seat);
            foreach (Card card in Deck.CreateOrdered())
            {
                bool held = false;
                foreach (Card heldCard in hand)
                {
                    if (heldCard == card)
                        held = true;
                }
                if (!held)
                    return card;
            }
            throw new InvalidOperationException("every card is held");
        }

        #endregion

        #region The deal

        [Test]
        public void Deal_FollowsTheFixedOrderShuffleDealRevealThenDrawLeader()
        {
            // Reproduces the deal by hand in the documented order: shuffle, deal, reveal trump, draw the leader.
            // The server and the browser host must follow this order for one seed to produce the same game.
            RandomPCG  rng  = RandomPCG.CreateFromSeed(SeedA);
            List<Card> deck = Deck.CreateOrdered();
            rng.ShuffleInPlace(deck);
            Card expectedTrumpCard    = deck[20];
            int  expectedLeaderSeat   = rng.NextInt(MatchEngine.NumSeats);

            MatchEngine engine = Deal(SeedA);

            Assert.That(engine.TrumpCard, Is.EqualTo(expectedTrumpCard), $"seed {SeedA}");
            Assert.That(engine.TrumpSuit, Is.EqualTo(expectedTrumpCard.Suit), $"seed {SeedA}");
            Assert.That(engine.StartingLeaderSeat, Is.EqualTo(expectedLeaderSeat), $"seed {SeedA}");

            for (int seat = 0; seat < MatchEngine.NumSeats; seat++)
            {
                List<Card> expectedHand = deck.GetRange(seat * MatchEngine.CardsPerSeat, MatchEngine.CardsPerSeat);
                Assert.That(engine.GetHand(seat), Is.EqualTo(expectedHand), $"seed {SeedA}, seat {seat}");
            }
        }

        [Test]
        public void Deal_MatchesItsPinnedGoldenValues()
        {
            // The by-hand test above uses the same algorithm as the engine, so it still passes if both change
            // together or if an SDK upgrade changes RandomPCG. These literals are the expected deal for SeedA.
            MatchEngine engine = Deal(SeedA);

            Assert.That(engine.TrumpCard, Is.EqualTo(new Card(Suit.Spades, Rank.Seven)), $"seed {SeedA}");
            Assert.That(engine.StartingLeaderSeat, Is.EqualTo(0), $"seed {SeedA}");

            Assert.That(engine.GetHand(0), Is.EqualTo(new List<Card>
            {
                new Card(Suit.Clubs, Rank.Five), new Card(Suit.Diamonds, Rank.Six), new Card(Suit.Clubs, Rank.Six),
                new Card(Suit.Hearts, Rank.Jack), new Card(Suit.Hearts, Rank.Ace),
            }), $"seed {SeedA}, seat 0");

            Assert.That(engine.GetHand(1), Is.EqualTo(new List<Card>
            {
                new Card(Suit.Clubs, Rank.Two), new Card(Suit.Diamonds, Rank.Five), new Card(Suit.Spades, Rank.Six),
                new Card(Suit.Hearts, Rank.Four), new Card(Suit.Spades, Rank.Queen),
            }), $"seed {SeedA}, seat 1");

            Assert.That(engine.GetHand(2), Is.EqualTo(new List<Card>
            {
                new Card(Suit.Clubs, Rank.Queen), new Card(Suit.Diamonds, Rank.Queen), new Card(Suit.Diamonds, Rank.Ten),
                new Card(Suit.Clubs, Rank.Three), new Card(Suit.Hearts, Rank.Three),
            }), $"seed {SeedA}, seat 2");

            Assert.That(engine.GetHand(3), Is.EqualTo(new List<Card>
            {
                new Card(Suit.Spades, Rank.Two), new Card(Suit.Clubs, Rank.Four), new Card(Suit.Spades, Rank.King),
                new Card(Suit.Hearts, Rank.Eight), new Card(Suit.Spades, Rank.Ace),
            }), $"seed {SeedA}, seat 3");
        }

        [TestCaseSource(nameof(Seeds))]
        public void Deal_GivesEachSeatFiveDistinctCardsAndRevealsATrumpNobodyHolds(ulong seed)
        {
            MatchEngine    engine = Deal(seed);
            HashSet<Card> dealt  = new HashSet<Card>();

            for (int seat = 0; seat < MatchEngine.NumSeats; seat++)
            {
                IReadOnlyList<Card> hand = engine.GetHand(seat);
                Assert.That(hand, Has.Count.EqualTo(MatchEngine.CardsPerSeat), $"seed {seed}, seat {seat}");
                Assert.That(engine.GetCardsRemaining(seat), Is.EqualTo(MatchEngine.CardsPerSeat), $"seed {seed}, seat {seat}");
                foreach (Card card in hand)
                    Assert.That(dealt.Add(card), Is.True, $"seed {seed}: {card} was dealt twice");
            }

            Assert.That(dealt, Has.Count.EqualTo(20), $"seed {seed}");
            Assert.That(dealt.Contains(engine.TrumpCard), Is.False, $"seed {seed}: the revealed trump card is in a hand");
        }

        [Test]
        public void Deal_DifferentSeedsProduceDifferentGames()
        {
            MatchEngine a = Deal(SeedA);
            MatchEngine b = Deal(SeedB);

            bool anyDifference = a.TrumpCard != b.TrumpCard || a.StartingLeaderSeat != b.StartingLeaderSeat;
            for (int seat = 0; seat < MatchEngine.NumSeats; seat++)
            {
                IReadOnlyList<Card> handA = a.GetHand(seat);
                IReadOnlyList<Card> handB = b.GetHand(seat);
                for (int ndx = 0; ndx < handA.Count; ndx++)
                {
                    if (handA[ndx] != handB[ndx])
                        anyDifference = true;
                }
            }
            Assert.That(anyDifference, Is.True, $"seeds {SeedA} and {SeedB} produced the same deal");
        }

        [Test]
        public void Deal_LeavesTheGameAwaitingTheStartingLeadersMove()
        {
            MatchEngine engine = Deal(SeedA);

            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.AwaitingMove), $"seed {SeedA}");
            Assert.That(engine.PlayIndex, Is.EqualTo(0), $"seed {SeedA}");
            Assert.That(engine.TrickIndex, Is.EqualTo(0), $"seed {SeedA}");
            Assert.That(engine.SeatOnTurn, Is.EqualTo(engine.StartingLeaderSeat), $"seed {SeedA}");
            Assert.That(engine.CurrentLeaderSeat, Is.EqualTo(engine.StartingLeaderSeat), $"seed {SeedA}");
            Assert.That(engine.LedSuit, Is.Null, $"seed {SeedA}");
            Assert.That(engine.Plays, Is.Empty, $"seed {SeedA}");
            Assert.That(engine.MoveDeadlineAt, Is.EqualTo(T0 + TestTimings.MoveDeadline), $"seed {SeedA}");
        }

        [Test]
        public void Deal_TheLeaderMayPlayAnyOfTheirCards()
        {
            MatchEngine engine = Deal(SeedA);

            Assert.That(engine.GetLegalPlays(engine.SeatOnTurn), Is.EqualTo(engine.GetHand(engine.SeatOnTurn)), $"seed {SeedA}");
        }

        #endregion

        #region Playing

        [Test]
        public void PlayCard_RemovesTheCardFromTheHandAndPassesTheTurnClockwise()
        {
            MatchEngine engine  = Deal(SeedA);
            int         leader  = engine.SeatOnTurn;
            Card        card    = engine.GetHand(leader)[0];

            MoveResult result = engine.PlayCard(leader, 0, card, T0);

            Assert.That(result.Accepted, Is.True, $"seed {SeedA}");
            Assert.That(result.TrickCompleted, Is.False, $"seed {SeedA}");
            Assert.That(engine.GetHand(leader), Does.Not.Contain(card), $"seed {SeedA}");
            Assert.That(engine.GetCardsRemaining(leader), Is.EqualTo(4), $"seed {SeedA}");
            Assert.That(engine.PlayIndex, Is.EqualTo(1), $"seed {SeedA}");
            Assert.That(engine.SeatOnTurn, Is.EqualTo((leader + 1) % MatchEngine.NumSeats), $"seed {SeedA}");
            Assert.That(engine.LedSuit, Is.EqualTo(card.Suit), $"seed {SeedA}");
            Assert.That(engine.Plays, Has.Count.EqualTo(1), $"seed {SeedA}");
            Assert.That(engine.Plays[0].Seat, Is.EqualTo(leader), $"seed {SeedA}");
            Assert.That(engine.Plays[0].Card, Is.EqualTo(card), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_ArmsTheNextSeatsMoveDeadlineFromTheTimeOfTheMove()
        {
            MatchEngine engine   = Deal(SeedA);
            MetaTime    playedAt = T0 + MetaDuration.FromSeconds(7);

            engine.PlayCard(engine.SeatOnTurn, 0, engine.GetHand(engine.SeatOnTurn)[0], playedAt);

            Assert.That(engine.MoveDeadlineAt, Is.EqualTo(playedAt + TestTimings.MoveDeadline), $"seed {SeedA}");
        }

        [Test]
        public void ArmMoveDeadline_ReArmsTheDeadlineWhenPlayActuallyBegins()
        {
            MatchEngine engine       = Deal(SeedA);
            MetaTime    playBeginsAt = T0 + MetaDuration.FromSeconds(10);

            engine.ArmMoveDeadline(playBeginsAt, TestTimings.MoveDeadline);

            Assert.That(engine.MoveDeadlineAt, Is.EqualTo(playBeginsAt + TestTimings.MoveDeadline), $"seed {SeedA}");
        }

        [Test]
        public void AHostThatSuppliesNoMoveDeadlineArmsNoneAtAll()
        {
            // A zero MoveDeadline means no deadline is in force, not a deadline that has already lapsed. The
            // engine stores MetaTime.Epoch, the same value it uses in every phase where no seat is on turn.
            MatchTimings timings = MatchTimings.Default;
            timings.MoveDeadline = MetaDuration.Zero;

            MatchEngine engine = MatchEngine.Create(SeedA, timings, T0);
            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.AwaitingMove), $"seed {SeedA}");
            Assert.That(engine.MoveDeadlineAt, Is.EqualTo(MetaTime.Epoch), $"seed {SeedA}");

            // The deadline stays unset after the next play. The play must be accepted, because a refused play
            // would also leave the stamp at Epoch.
            MoveResult result = PlayFirstLegalCard(engine, T0 + MetaDuration.FromSeconds(3));
            Assert.That(result.Accepted, Is.True, $"seed {SeedA}: the engine refused a legal card");
            Assert.That(engine.PlayIndex, Is.EqualTo(1), $"seed {SeedA}");
            Assert.That(engine.MoveDeadlineAt, Is.EqualTo(MetaTime.Epoch), $"seed {SeedA}");

            engine.ArmMoveDeadline(T0 + MetaDuration.FromSeconds(9), timings.MoveDeadline);
            Assert.That(engine.MoveDeadlineAt, Is.EqualTo(MetaTime.Epoch), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_ForcesTheSeatToFollowTheLedSuitWhenItCan()
        {
            // Plays a whole game and checks every card in the hand against MatchRules at every turn. The test
            // also asserts that a must-follow turn occurred, so it cannot pass without testing that rule.
            MatchEngine engine        = Deal(SeedA);
            MetaTime    now           = T0;
            int         mustFollowHit = 0;

            while (!engine.IsFinished)
            {
                if (engine.TurnPhase == MatchTurnPhase.ResolvingTrick)
                {
                    now = engine.ResolvePauseEndsAt;
                    engine.Advance(now);
                    continue;
                }

                int                 seat    = engine.SeatOnTurn;
                IReadOnlyList<Card> hand    = engine.GetHand(seat);
                Suit?               ledSuit = engine.LedSuit;
                List<Card>          legal   = engine.GetLegalPlays(seat);

                foreach (Card card in hand)
                {
                    bool expectedLegal = MatchRules.IsLegalPlay(hand, ledSuit, card);
                    if (expectedLegal)
                    {
                        Assert.That(legal, Does.Contain(card), $"seed {SeedA}, play {engine.PlayIndex}: {card} should be playable");
                        continue;
                    }

                    Assert.That(legal, Does.Not.Contain(card), $"seed {SeedA}, play {engine.PlayIndex}: {card} should not be playable");
                    MoveResult refusal = engine.PlayCard(seat, engine.PlayIndex, card, now);
                    Assert.That(refusal.Accepted, Is.False, $"seed {SeedA}, play {engine.PlayIndex}");
                    Assert.That(refusal.Refusal, Is.EqualTo(MoveRefusalReason.MustFollowSuit), $"seed {SeedA}, play {engine.PlayIndex}");
                    mustFollowHit++;
                }

                Assert.That(engine.PlayCard(seat, engine.PlayIndex, legal[0], now).Accepted, Is.True, $"seed {SeedA}");
            }

            Assert.That(mustFollowHit, Is.GreaterThan(0), $"seed {SeedA} never produced a follow-suit situation; pick another seed");
        }

        #endregion

        #region Refusals

        [Test]
        public void PlayCard_RefusesAMoveFromASeatThatIsNotOnTurn()
        {
            MatchEngine engine    = Deal(SeedA);
            int         wrongSeat = (engine.SeatOnTurn + 1) % MatchEngine.NumSeats;

            MoveResult result = engine.PlayCard(wrongSeat, 0, engine.GetHand(wrongSeat)[0], T0);

            Assert.That(result.Accepted, Is.False, $"seed {SeedA}");
            Assert.That(result.Refusal, Is.EqualTo(MoveRefusalReason.NotYourTurn), $"seed {SeedA}");
            Assert.That(engine.PlayIndex, Is.EqualTo(0), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_RefusesACardTheSeatDoesNotHold()
        {
            MatchEngine engine = Deal(SeedA);
            int         seat   = engine.SeatOnTurn;

            MoveResult result = engine.PlayCard(seat, 0, CardNotHeldBy(engine, seat), T0);

            Assert.That(result.Accepted, Is.False, $"seed {SeedA}");
            Assert.That(result.Refusal, Is.EqualTo(MoveRefusalReason.CardNotInHand), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_RefusesAStalePlayIndex()
        {
            MatchEngine engine = Deal(SeedA);
            int         leader = engine.SeatOnTurn;
            engine.PlayCard(leader, 0, engine.GetHand(leader)[0], T0);

            int  seat = engine.SeatOnTurn;
            Card card = engine.GetLegalPlays(seat)[0];

            MoveResult stale  = engine.PlayCard(seat, 0, card, T0);
            MoveResult future = engine.PlayCard(seat, 2, card, T0);

            Assert.That(stale.Refusal, Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {SeedA}");
            Assert.That(future.Refusal, Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {SeedA}");
            Assert.That(engine.PlayIndex, Is.EqualTo(1), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_RefusesTheIndexBeforeConsultingLegality()
        {
            // A move with a stale index, the wrong seat and a card nobody holds is refused for the stale index.
            // The engine checks the index first because the index is what resolves races between moves.
            MatchEngine engine = Deal(SeedA);
            int         leader = engine.SeatOnTurn;
            engine.PlayCard(leader, 0, engine.GetHand(leader)[0], T0);

            int  offTurnSeat = (engine.SeatOnTurn + 1) % MatchEngine.NumSeats;
            Card unheldCard  = CardNotHeldBy(engine, offTurnSeat);

            MoveResult result = engine.PlayCard(offTurnSeat, 0, unheldCard, T0);

            Assert.That(result.Refusal, Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_CannotPlayTheSameCardTwice()
        {
            MatchEngine engine = Deal(SeedA);
            int         leader = engine.SeatOnTurn;
            Card        card   = engine.GetHand(leader)[0];

            Assert.That(engine.PlayCard(leader, 0, card, T0).Accepted, Is.True, $"seed {SeedA}");
            MoveResult replay = engine.PlayCard(leader, 0, card, T0);

            Assert.That(replay.Refusal, Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {SeedA}");
            Assert.That(engine.PlayIndex, Is.EqualTo(1), $"seed {SeedA}");
            Assert.That(engine.Plays, Has.Count.EqualTo(1), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_RefusesDuringTheResolvePause()
        {
            MatchEngine engine = Deal(SeedA);
            MetaTime    now    = T0;
            PlayFirstTrick(engine, now);

            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.ResolvingTrick), $"seed {SeedA}");
            Assert.That(engine.SeatOnTurn, Is.EqualTo(-1), $"seed {SeedA}");

            int  seat = 0;
            Card card = engine.GetHand(seat)[0];

            MoveResult result = engine.PlayCard(seat, engine.PlayIndex, card, now);

            Assert.That(result.Refusal, Is.EqualTo(MoveRefusalReason.NotInPlayablePhase), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_RefusesOnceTheGameIsFinished()
        {
            MatchEngine engine = Deal(SeedA);
            PlayOutGreedily(engine, T0);

            MoveResult atCurrentIndex = engine.PlayCard(0, engine.PlayIndex, new Card(Suit.Spades, Rank.Ace), T0);
            MoveResult atStaleIndex   = engine.PlayCard(0, 0, new Card(Suit.Spades, Rank.Ace), T0);

            Assert.That(atCurrentIndex.Refusal, Is.EqualTo(MoveRefusalReason.NotInPlayablePhase), $"seed {SeedA}");
            Assert.That(atStaleIndex.Refusal, Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {SeedA}");
        }

        [Test]
        public void PlayCard_RejectsASeatOutsideTheTable()
        {
            MatchEngine engine = Deal(SeedA);

            Assert.That(() => engine.PlayCard(4, 0, new Card(Suit.Spades, Rank.Ace), T0), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => engine.GetHand(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        #endregion

        #region Trick resolution and the turn flow

        [Test]
        public void Trick_CreditsTheWinnerAndGivesThemTheLead()
        {
            MatchEngine engine = Deal(SeedA);
            MetaTime    now    = T0;
            PlayFirstTrick(engine, now);

            int expectedWinner = MatchRules.ResolveTrick(engine.GetCurrentTrickPlays(), engine.TrumpSuit);

            Assert.That(engine.TrickWinnerSeats, Has.Count.EqualTo(1), $"seed {SeedA}");
            Assert.That(engine.TrickWinnerSeats[0], Is.EqualTo(expectedWinner), $"seed {SeedA}");
            Assert.That(engine.GetTricksWon(expectedWinner), Is.EqualTo(1), $"seed {SeedA}");
            Assert.That(engine.CurrentLeaderSeat, Is.EqualTo(expectedWinner), $"seed {SeedA}");

            engine.Advance(engine.ResolvePauseEndsAt);

            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.AwaitingMove), $"seed {SeedA}");
            Assert.That(engine.SeatOnTurn, Is.EqualTo(expectedWinner), $"seed {SeedA}");
            Assert.That(engine.TrickIndex, Is.EqualTo(1), $"seed {SeedA}");
            Assert.That(engine.LedSuit, Is.Null, $"seed {SeedA}");
        }

        [Test]
        public void ResolvePause_IsHeldAsAnAbsoluteEndTimeAndOnlyEndsWhenItIsReached()
        {
            MatchEngine engine     = Deal(SeedA);
            MetaTime    lastPlayAt = T0 + MetaDuration.FromSeconds(3);
            for (int ndx = 0; ndx < MatchEngine.CardsPerTrick - 1; ndx++)
                PlayFirstLegalCard(engine, T0);
            MoveResult fourth = PlayFirstLegalCard(engine, lastPlayAt);

            Assert.That(fourth.TrickCompleted, Is.True, $"seed {SeedA}");
            Assert.That(fourth.TrickWinnerSeat, Is.EqualTo(engine.TrickWinnerSeats[0]), $"seed {SeedA}");
            Assert.That(engine.ResolvePauseEndsAt, Is.EqualTo(lastPlayAt + TestTimings.ResolvePause), $"seed {SeedA}");

            Assert.That(engine.Advance(lastPlayAt), Is.False, $"seed {SeedA}");
            Assert.That(engine.Advance(engine.ResolvePauseEndsAt - MetaDuration.FromMilliseconds(1)), Is.False, $"seed {SeedA}");
            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.ResolvingTrick), $"seed {SeedA}");

            MetaTime pauseEndsAt = engine.ResolvePauseEndsAt;
            Assert.That(engine.Advance(pauseEndsAt), Is.True, $"seed {SeedA}");
            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.AwaitingMove), $"seed {SeedA}");
            Assert.That(engine.MoveDeadlineAt, Is.EqualTo(pauseEndsAt + TestTimings.MoveDeadline), $"seed {SeedA}");
            Assert.That(engine.Advance(pauseEndsAt), Is.False, $"seed {SeedA}: advancing again should do nothing");
        }

        [Test]
        public void CurrentTrickPlays_HoldTheFourCardsThroughTheResolvePauseAndClearAfterIt()
        {
            MatchEngine engine = Deal(SeedA);
            PlayFirstTrick(engine, T0);

            Assert.That(engine.GetCurrentTrickPlays(), Has.Count.EqualTo(4), $"seed {SeedA}");

            engine.Advance(engine.ResolvePauseEndsAt);

            Assert.That(engine.GetCurrentTrickPlays(), Is.Empty, $"seed {SeedA}");
            Assert.That(engine.Plays, Has.Count.EqualTo(4), $"seed {SeedA}: the play history keeps every card");
        }

        #endregion

        #region Whole games

        [TestCaseSource(nameof(Seeds))]
        public void FullGame_PlaysTwentyCardsInFiveTricksThatSumToFive(ulong seed)
        {
            MatchEngine engine = Deal(seed);
            PlayOutGreedily(engine, T0);

            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.Finished), $"seed {seed}");
            Assert.That(engine.PlayIndex, Is.EqualTo(MatchEngine.NumPlays), $"seed {seed}");
            Assert.That(engine.Plays, Has.Count.EqualTo(20), $"seed {seed}");
            Assert.That(engine.TrickWinnerSeats, Has.Count.EqualTo(MatchEngine.NumTricks), $"seed {seed}");
            Assert.That(engine.SeatOnTurn, Is.EqualTo(-1), $"seed {seed}");

            int totalTricks = 0;
            HashSet<Card> playedCards = new HashSet<Card>();
            for (int seat = 0; seat < MatchEngine.NumSeats; seat++)
            {
                totalTricks += engine.GetTricksWon(seat);
                Assert.That(engine.GetHand(seat), Is.Empty, $"seed {seed}, seat {seat}");
                Assert.That(engine.GetCardsRemaining(seat), Is.EqualTo(0), $"seed {seed}, seat {seat}");
            }
            foreach (PlayRecord play in engine.Plays)
                Assert.That(playedCards.Add(play.Card), Is.True, $"seed {seed}: {play.Card} was played twice");

            Assert.That(totalTricks, Is.EqualTo(MatchEngine.NumTricks), $"seed {seed}");
            Assert.That(playedCards, Has.Count.EqualTo(20), $"seed {seed}");
        }

        [Test]
        public void FullGame_WithZeroTimingsResolvesWithoutWaiting()
        {
            // A table that has lost every human runs on MatchTimings.Instant, so it finishes without time
            // advancing.
            MatchEngine engine = MatchEngine.Create(SeedC, MatchTimings.Instant, T0);

            while (!engine.IsFinished)
            {
                if (engine.TurnPhase == MatchTurnPhase.AwaitingMove)
                    PlayFirstLegalCard(engine, T0);
                else
                    Assert.That(engine.Advance(T0), Is.True, $"seed {SeedC}");
            }

            Assert.That(engine.PlayIndex, Is.EqualTo(MatchEngine.NumPlays), $"seed {SeedC}");
            Assert.That(engine.ComputeStandings(), Has.Count.EqualTo(MatchEngine.NumSeats), $"seed {SeedC}");
        }

        #endregion
    }
}
