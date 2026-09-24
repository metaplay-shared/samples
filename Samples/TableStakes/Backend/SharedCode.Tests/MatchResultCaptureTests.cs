using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests the <see cref="MatchSeatResult"/> a finished table captures for each human seat, and when it is
    /// captured.
    /// <para>
    /// Results are captured once, when the table reaches its result, because two inputs change afterwards: a
    /// table restored from the database starts with every connected flag cleared, and a seat's occupancy keeps
    /// changing while a table is played out. Capturing at the finish keeps the result the same however long
    /// delivery takes (<c>docs/player.md</c>, "Lifetime record and match history").
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchResultCaptureTests
    {
        const ulong Seed = 7717UL;

        static readonly MetaTime     T0           = MatchTestDeals.T0;
        static readonly MetaDuration MoveDeadline = MatchTestDeals.SeatTimings().MoveDeadline;
        static readonly MetaDuration JoinWindow   = MatchTestDeals.SeatTimings().JoinWindow;

        static EntityId Player(int seat) => MatchTestDeals.Player(seat);

        /// <summary>Creates a table that is dealt but not started, with <paramref name="numHumanSeats"/> human
        /// seats.</summary>
        static MatchModel NewTable(int numHumanSeats) => MatchTestDeals.NewTable(Seed, MatchTestDeals.SeatTimings(), numHumanSeats);

        /// <summary>Creates a table where every human seat arrives, and plays it to the finish.</summary>
        static MatchModel PlayedOutTable(int numHumanSeats, out TestMatchHost host) =>
            PlayedOutTable(numHumanSeats, numArrivedSeats: numHumanSeats, out host);

        /// <summary>
        /// Creates a table where only the first <paramref name="numArrivedSeats"/> human seats arrive, and plays it to
        /// the finish. A bot covers each remaining human seat when the join window ends.
        /// </summary>
        static MatchModel PlayedOutTable(int numHumanSeats, int numArrivedSeats, out TestMatchHost host)
        {
            MatchModel          model          = NewTable(numHumanSeats);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
            host = new TestMatchHost(model, Seed);

            for (int seat = 0; seat < numArrivedSeats; seat++)
                MatchHost.NoteSeatPresent(model, seat, T0, host);
            MatchHost.RunTable(model, pendingBotMove, T0, host);

            // Every human misses every deadline, so bots cover the human seats and finish the game. The cards
            // played do not matter here, only that the table reaches Ended.
            MetaTime now = T0;
            for (int step = 0; step < 60 && model.Phase == MatchPhase.Playing; step++)
            {
                now += MoveDeadline + MetaDuration.FromSeconds(1);
                MatchHost.RunTable(model, pendingBotMove, now, host);
            }

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), "the table never reached a result");
            return model;
        }

        /// <summary>Creates a table with one human seat, whose owner arrives at T0, and starts it.</summary>
        static MatchModel StartedSoloTable(out TestMatchHost host, out MatchPendingBotMove pendingBotMove) =>
            MatchTestDeals.StartedTable(Seed, MatchTestDeals.SeatTimings(), out host, out pendingBotMove);

        static MatchSeatResult ResultForSeat(MatchModel model, int seat)
        {
            foreach (MatchSeatResult result in model.SeatResults)
            {
                if (result.Seat == seat)
                    return result;
            }
            return null;
        }

        #region What is captured

        [Test]
        public void AFinishedTableCapturesOneResultPerHumanSeat()
        {
            MatchModel model = PlayedOutTable(numHumanSeats: 2, out TestMatchHost _);

            Assert.That(model.HasCapturedResults, Is.True);
            Assert.That(model.SeatResults, Has.Count.EqualTo(2), "a bot seat was given a result to record");
            Assert.That(ResultForSeat(model, 0).PlayerId, Is.EqualTo(Player(0)));
            Assert.That(ResultForSeat(model, 1).PlayerId, Is.EqualTo(Player(1)));
        }

        [Test]
        public void TheCapturedRankAndTricksAreTheTablesOwnStandings()
        {
            // The result copies the board's standings instead of computing them again, so it cannot disagree
            // with the results screen (docs/game-rules.md, "End of the game and standings").
            MatchModel model = PlayedOutTable(numHumanSeats: 2, out TestMatchHost _);

            foreach (SeatStanding standing in model.Board.Standings)
            {
                MatchSeatResult result = ResultForSeat(model, standing.Seat);
                if (result == null)
                    continue;

                Assert.That(result.Position, Is.EqualTo(standing.Position), $"seat {standing.Seat}");
                Assert.That(result.TricksWon, Is.EqualTo(standing.TricksWon), $"seat {standing.Seat}");
                Assert.That(result.IsWin, Is.EqualTo(standing.Position == 0), $"seat {standing.Seat}");
            }
        }

        [Test]
        public void TheHumanOpponentCountIsTheOtherSeatsWithAPersonBehindThem()
        {
            // The count is the other humans who arrived, not those still playing at the end. Bots cover every
            // seat before these games finish, and the count is unchanged.
            MatchModel alone = PlayedOutTable(numHumanSeats: 1, out TestMatchHost _);
            Assert.That(ResultForSeat(alone, 0).HumanOpponents, Is.Zero);

            MatchModel three = PlayedOutTable(numHumanSeats: 3, out TestMatchHost _);
            for (int seat = 0; seat < 3; seat++)
                Assert.That(ResultForSeat(three, seat).HumanOpponents, Is.EqualTo(2), $"seat {seat}");
        }

        [Test]
        public void ASeatItsOwnerNeverArrivedAtIsNotAHumanOpponent()
        {
            // Seat 2's owner never subscribes, for example because they were already at another table, so a bot
            // covers the seat when the join window ends. Each of the two players who arrived had one human
            // opponent (docs/matchmaking.md, "Timeouts").
            MatchModel model = PlayedOutTable(numHumanSeats: 3, numArrivedSeats: 2, out TestMatchHost _);

            Assert.That(model.GetSeat(2).HasEverConnected, Is.False, "the third seat's owner turned up after all");
            Assert.That(ResultForSeat(model, 0).HumanOpponents, Is.EqualTo(1));
            Assert.That(ResultForSeat(model, 1).HumanOpponents, Is.EqualTo(1));
        }

        [Test]
        public void ASeatABotFinishedIsNotMarkedAsFinishedByItsPlayer()
        {
            // In PlayedOutTable, bots cover every human seat after missed deadlines and finish the game. The game
            // still counts for the players. FinishedByPlayer only records who finished it
            // (docs/player.md, "Lifetime record and match history").
            MatchModel model = PlayedOutTable(numHumanSeats: 2, out TestMatchHost _);

            Assert.That(ResultForSeat(model, 0).FinishedByPlayer, Is.False);
            Assert.That(ResultForSeat(model, 1).FinishedByPlayer, Is.False);
        }

        [Test]
        public void ASeatItsPlayerPlayedToTheEndIsMarkedAsTheirs()
        {
            MatchModel model = StartedSoloTable(out TestMatchHost host, out MatchPendingBotMove pendingBotMove);

            // Seat 0 plays all of its cards, so it is still connected and not covered by a bot at the finish.
            for (int step = 0; step < 60 && model.Phase == MatchPhase.Playing; step++)
            {
                if (model.Board.TurnPhase == MatchTurnPhase.AwaitingMove && model.Board.SeatOnTurn == 0)
                {
                    List<Card> legal = MatchRules.GetLegalPlays(model.Engine.GetHand(0), model.Board.LedSuit);
                    MatchHost.TrySubmitMove(model, Player(0), 0, model.Board.PlayIndex, legal[0], T0, host);
                }
                MatchHost.RunTable(model, pendingBotMove, T0, host);
            }

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended));
            Assert.That(ResultForSeat(model, 0).FinishedByPlayer, Is.True);
            Assert.That(ResultForSeat(model, 0).SeatLossReason, Is.EqualTo(MatchSeatLossReason.None));
        }

        [Test]
        public void AnAbandonedTableCapturesNothing()
        {
            // A table that never started has no standings and records nothing, so it must not reach the results
            // screen (docs/match.md, "Phases").
            MatchModel    model = NewTable(numHumanSeats: 1);
            TestMatchHost host  = new TestMatchHost(model, Seed);

            MatchHost.RunTable(model, new MatchPendingBotMove(), T0 + JoinWindow + MetaDuration.FromSeconds(1), host);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Abandoned));
            Assert.That(model.HasCapturedResults, Is.False, "an abandoned table captured a result nobody played for");
            Assert.That(model.SeatResults, Is.Null);
        }

        [Test]
        public void ResultsAreCapturedOnceAndNeverRecomputed()
        {
            // A table restored from the database starts with every connected flag cleared, so capturing again
            // would set FinishedByPlayer to false for every seat.
            MatchModel model = PlayedOutTable(numHumanSeats: 1, out TestMatchHost host);

            IReadOnlyList<MatchSeatResult> first = model.SeatResults;
            MatchHost.RunTable(model, new MatchPendingBotMove(), T0 + MetaDuration.FromHours(1), host);

            Assert.That(model.SeatResults, Is.SameAs(first), "the results were captured a second time");
        }

        #endregion

        #region Why a seat was lost

        [Test]
        public void ADeliberateLeaveIsNotADisconnect()
        {
            // The reason must be recorded when it happens, because a Leave and a closed tab produce the same seat
            // state a moment later (docs/match.md, "When players stop playing").
            MatchModel model = StartedSoloTable(out TestMatchHost host, out MatchPendingBotMove _);
            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: true, host);

            Assert.That(model.GetSeatLossReason(0), Is.EqualTo(MatchSeatLossReason.DeliberateLeave));
            Assert.That(host.SeatLosses, Is.EqualTo(new[] { (0, MatchSeatLossReason.DeliberateLeave) }));
        }

        [Test]
        public void ADroppedConnectionIsADisconnect()
        {
            MatchModel model = StartedSoloTable(out TestMatchHost host, out MatchPendingBotMove _);
            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);

            Assert.That(model.GetSeatLossReason(0), Is.EqualTo(MatchSeatLossReason.Disconnect));
        }

        [Test]
        public void ASessionEndingAfterALeaveDoesNotRewriteTheReason()
        {
            // A player who taps Leave and then navigates away calls NoteSeatAbsent twice. The second call must
            // not change DeliberateLeave to Disconnect.
            MatchModel model = StartedSoloTable(out TestMatchHost host, out MatchPendingBotMove _);

            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: true, host);
            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);

            Assert.That(model.GetSeatLossReason(0), Is.EqualTo(MatchSeatLossReason.DeliberateLeave));
            Assert.That(host.SeatLosses, Has.Count.EqualTo(1), "one loss was reported twice");
        }

        [Test]
        public void APlayerWhoStopsPlayingWhileConnectedLosesTheSeatToTheDeadline()
        {
            // DeadlineLapse is the only reason that connection state cannot show: the player is connected but has
            // stopped playing.
            MatchModel model = StartedSoloTable(out TestMatchHost host, out MatchPendingBotMove pendingBotMove);

            MetaTime now = T0;
            for (int step = 0; step < 6 && model.GetSeat(0).Occupancy != MatchSeatOccupancy.HumanCoveredByBot; step++)
            {
                now += MoveDeadline + MetaDuration.FromSeconds(1);
                MatchHost.RunTable(model, pendingBotMove, now, host);
            }

            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot));
            Assert.That(model.GetSeatLossReason(0), Is.EqualTo(MatchSeatLossReason.DeadlineLapse));
        }

        [Test]
        public void ComingBackClearsTheReason()
        {
            // A returning player's seat loss reason is cleared. A stale reason would be captured at the finish
            // and reported as if the player never returned.
            MatchModel model = StartedSoloTable(out TestMatchHost host, out MatchPendingBotMove _);

            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);
            Assert.That(model.GetSeatLossReason(0), Is.EqualTo(MatchSeatLossReason.Disconnect));

            MatchHost.NoteSeatPresent(model, 0, T0 + MetaDuration.FromSeconds(1), host);
            Assert.That(model.GetSeatLossReason(0), Is.EqualTo(MatchSeatLossReason.None));
        }

        #endregion

        #region The tie-break the standings carry

        [Test]
        public void TheWinnersSeparationSaysWhichRuleDecidedFirstPlace()
        {
            // Seat 0 wins every trick, so first place is decided on trick count.
            List<SeatStanding> onCount = MatchRules.ComputeStandings(new List<int> { 0, 0, 0, 0, 0 });
            Assert.That(onCount[0].SeparatedFromNextBy, Is.EqualTo(StandingSeparation.MoreTricks));

            // Seats 0 and 1 win two tricks each, and seat 1 won the later trick. Seat 1 is first because of the
            // recency rule, not the count.
            List<SeatStanding> onRecency = MatchRules.ComputeStandings(new List<int> { 0, 1, 0, 1, 2 });
            Assert.That(onRecency[0].Seat, Is.EqualTo(1));
            Assert.That(onRecency[0].TricksWon, Is.EqualTo(2));
            Assert.That(onRecency[1].TricksWon, Is.EqualTo(2));
            Assert.That(onRecency[0].SeparatedFromNextBy, Is.EqualTo(StandingSeparation.MoreRecentTrick),
                "a tie broken on recency was reported as a win on count");
        }

        [Test]
        public void TheSeatsThatWonNothingSayTheyAreLevel()
        {
            // Seats that won no trick are ordered by seat index. Their separation is NoTricksEither rather than a
            // reason the sort did not use.
            List<SeatStanding> standings = MatchRules.ComputeStandings(new List<int> { 0, 0, 0, 0, 0 });

            Assert.That(standings[1].SeparatedFromNextBy, Is.EqualTo(StandingSeparation.NoTricksEither));
            Assert.That(standings[2].SeparatedFromNextBy, Is.EqualTo(StandingSeparation.NoTricksEither));
            Assert.That(standings[3].SeparatedFromNextBy, Is.EqualTo(StandingSeparation.None), "the last seat has nothing below it");
        }

        #endregion
    }
}
