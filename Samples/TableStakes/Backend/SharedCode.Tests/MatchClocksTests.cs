using Metaplay.Core;
using Metaplay.Core.Model;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests <see cref="AuthoritativeTime"/>, <see cref="PresentedTime"/> and the <see cref="MatchTableTime"/>
    /// queries built on them: the move-deadline ring, the resolve-pause beat, the withheld terminal phase, and
    /// whether the server would still accept a move.
    /// </summary>
    [TestFixture]
    public class MatchClocksTests
    {
        const ulong Seed = 4242UL;

        static readonly MetaTime T0 = MatchTestDeals.T0;

        static AuthoritativeTime AuthoritativeAt(MetaDuration offsetFromT0) => AuthoritativeTime.At(T0 + offsetFromT0);

        static PresentedTime PresentedAt(MetaDuration offsetFromT0) => PresentedTime.CaughtUpWith(AuthoritativeAt(offsetFromT0));

        /// <summary>A board with seat <see cref="MatchBoard.SeatOnTurn"/> on turn and its deadline armed at T0.</summary>
        static MatchBoard AwaitingMoveBoard() => MatchBoard.Build(MatchEngine.Create(Seed, MatchTimings.Default, T0));

        /// <summary>A board holding the resolve pause of the first trick, all four cards played at T0.</summary>
        static MatchBoard ResolvingTrickBoard()
        {
            MatchEngine engine = MatchEngine.Create(Seed, MatchTimings.Default, T0);
            for (int ndx = 0; ndx < MatchRules.CardsPerTrick; ndx++)
            {
                int        seat   = engine.SeatOnTurn;
                MoveResult result = engine.PlayCard(seat, engine.PlayIndex, engine.GetLegalPlays(seat)[0], T0);
                Assert.That(result.Accepted, Is.True, $"seed {Seed}: the engine refused a legal card at play {ndx}");
            }
            Assert.That(engine.TurnPhase, Is.EqualTo(MatchTurnPhase.ResolvingTrick), $"seed {Seed}");
            return MatchBoard.Build(engine);
        }

        /// <summary>A board whose host supplied no move deadline: a seat is on turn, with no clock on it.</summary>
        static MatchBoard NoDeadlineBoard()
        {
            MatchTimings timings = MatchTimings.Default;
            timings.MoveDeadline = MetaDuration.Zero;
            return MatchBoard.Build(MatchEngine.Create(Seed, timings, T0));
        }

        #region The clock types themselves

        [TestCase(5000,  15000, false)]
        [TestCase(19999, 1,     false)]
        [TestCase(20000, 0,     true)]
        [TestCase(60000, 0,     true)]
        public void BothClocksCountDownToADeadlineAndClampAtZero(int offsetMs, int expectedRemainingMs, bool expectedReached)
        {
            MetaTime          deadline      = T0 + MetaDuration.FromSeconds(20);
            AuthoritativeTime authoritative = AuthoritativeAt(MetaDuration.FromMilliseconds(offsetMs));
            PresentedTime     presented     = PresentedAt(MetaDuration.FromMilliseconds(offsetMs));

            Assert.That(authoritative.RemainingUntil(deadline), Is.EqualTo(MetaDuration.FromMilliseconds(expectedRemainingMs)));
            Assert.That(authoritative.HasReached(deadline), Is.EqualTo(expectedReached));
            Assert.That(presented.RemainingUntil(deadline), Is.EqualTo(MetaDuration.FromMilliseconds(expectedRemainingMs)));
            Assert.That(presented.HasReached(deadline), Is.EqualTo(expectedReached));
        }

        [TestCase(1500,  1500, false)]
        [TestCase(-3000, 0,    true)]
        public void PresentedTimeTrailsByANonNegativeBeat(int trailingMs, int expectedTrailingMs, bool expectedCaughtUp)
        {
            AuthoritativeTime now       = AuthoritativeAt(MetaDuration.FromSeconds(10));
            PresentedTime     presented = PresentedTime.Trailing(now, MetaDuration.FromMilliseconds(trailingMs));

            Assert.That(presented.TrailingBy, Is.EqualTo(MetaDuration.FromMilliseconds(expectedTrailingMs)));
            Assert.That(presented.IsCaughtUp, Is.EqualTo(expectedCaughtUp));
            Assert.That(presented.Timestamp, Is.EqualTo(now.Timestamp - MetaDuration.FromMilliseconds(expectedTrailingMs)));
        }

        [Test]
        public void CaughtUpPresentedTimeMatchesAuthoritativeTime()
        {
            AuthoritativeTime now       = AuthoritativeAt(MetaDuration.FromSeconds(3));
            PresentedTime     presented = PresentedTime.CaughtUpWith(now);

            Assert.That(presented.IsCaughtUp, Is.True);
            Assert.That(presented.TrailingBy, Is.EqualTo(MetaDuration.Zero));
            Assert.That(presented.Timestamp, Is.EqualTo(now.Timestamp));
        }

        #endregion

        #region The move-deadline ring

        [TestCase(5,   15)]
        [TestCase(20,  0)]
        [TestCase(120, 0)]
        public void TheDeadlineRingDrainsTowardsTheBoardsTimestampAndStaysZeroPastIt(int presentedSeconds, int expectedRemainingSeconds)
        {
            MatchBoard board = AwaitingMoveBoard();
            Assert.That(board.MoveDeadlineAt, Is.EqualTo(T0 + MatchTimings.Default.MoveDeadline));

            Assert.That(MatchTableTime.TryGetMoveDeadlineRemaining(board, PresentedAt(MetaDuration.FromSeconds(presentedSeconds)), out MetaDuration remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(MetaDuration.FromSeconds(expectedRemainingSeconds)));
        }

        [Test]
        public void TheDeadlineRingIsWithheldWhileTheBoardTrails()
        {
            MatchBoard    board    = AwaitingMoveBoard();
            PresentedTime trailing = PresentedTime.Trailing(AuthoritativeAt(MetaDuration.FromSeconds(5)), MetaDuration.FromMilliseconds(1500));

            Assert.That(MatchTableTime.TryGetMoveDeadlineRemaining(board, trailing, out MetaDuration remaining), Is.False);
            Assert.That(remaining, Is.EqualTo(MetaDuration.Zero));
        }

        [Test]
        public void TheDeadlineRingIsWithheldWhenTheHostStampedNoDeadline()
        {
            // The offline browser host passes MetaDuration.Zero as the move deadline unless a test sets one, so
            // the board has no deadline. A ring shown here would count down to a deadline that nothing enforces.
            MatchBoard board = NoDeadlineBoard();

            Assert.That(board.TurnPhase, Is.EqualTo(MatchTurnPhase.AwaitingMove), $"seed {Seed}");
            Assert.That(board.HasMoveDeadline, Is.False, $"seed {Seed}");
            Assert.That(MatchTableTime.TryGetMoveDeadlineRemaining(board, PresentedAt(MetaDuration.Zero), out MetaDuration remaining), Is.False);
            Assert.That(remaining, Is.EqualTo(MetaDuration.Zero));
            Assert.That(MatchTableTime.TryGetMoveDeadlineRemaining(board, PresentedAt(MetaDuration.FromSeconds(600)), out MetaDuration later), Is.False);
            Assert.That(later, Is.EqualTo(MetaDuration.Zero));
        }

        [Test]
        public void AMoveIsStillAcceptedAtATableWithNoDeadlineInForce()
        {
            // A board with no deadline stores MetaTime.Epoch. Reading Epoch as a lapsed deadline would refuse
            // every move for the whole game.
            MatchBoard board = NoDeadlineBoard();

            Assert.That(MatchTableTime.CanStillSubmitMove(board, AuthoritativeAt(MetaDuration.Zero)), Is.True);
            Assert.That(MatchTableTime.CanStillSubmitMove(board, AuthoritativeAt(MetaDuration.FromSeconds(600))), Is.True);
        }

        [Test]
        public void TheDeadlineRingIsWithheldWhileNoSeatIsOnTurn()
        {
            MatchBoard board = ResolvingTrickBoard();

            Assert.That(MatchTableTime.TryGetMoveDeadlineRemaining(board, PresentedAt(MetaDuration.Zero), out MetaDuration remaining), Is.False);
            Assert.That(remaining, Is.EqualTo(MetaDuration.Zero));
        }

        #endregion

        #region The resolve-pause beat

        [Test]
        public void ABeatShorterThanThePauseRunsItsWholeDuration()
        {
            MatchBoard board = ResolvingTrickBoard();
            Assert.That(board.ResolvePauseEndsAt, Is.EqualTo(T0 + MatchTimings.Default.ResolvePause));

            MetaDuration beat    = MetaDuration.FromMilliseconds(1100);
            MetaTime     endsAt  = MatchTableTime.GetBeatEndsAt(board, AuthoritativeAt(MetaDuration.Zero), beat);

            Assert.That(endsAt, Is.EqualTo(T0 + beat));
        }

        [Test]
        public void ABeatLongerThanThePauseIsCutShortByIt()
        {
            MatchBoard board = ResolvingTrickBoard();

            MetaDuration beat   = MetaDuration.FromSeconds(30);
            MetaTime     endsAt = MatchTableTime.GetBeatEndsAt(board, AuthoritativeAt(MetaDuration.Zero), beat);

            Assert.That(endsAt, Is.EqualTo(board.ResolvePauseEndsAt), "a beat cannot outlive the pause the host holds open for it");
            Assert.That(MatchTableTime.IsBeatPlaying(T0, endsAt, AuthoritativeAt(MatchTimings.Default.ResolvePause)), Is.False);
        }

        [Test]
        public void ABeatStartedInsideAPauseThatIsAlreadyOverNeverPlays()
        {
            MatchBoard board = ResolvingTrickBoard();

            // The clock is at the end of the resolve pause, so no time is left for the beat.
            AuthoritativeTime now    = AuthoritativeAt(MatchTimings.Default.ResolvePause);
            MetaTime          endsAt = MatchTableTime.GetBeatEndsAt(board, now, MetaDuration.FromMilliseconds(1100));

            Assert.That(MatchTableTime.IsBeatPlaying(now.Timestamp, endsAt, now), Is.False);
        }

        [Test]
        public void AHostHoldingNoPauseOpenLeavesNoWindowForABeat()
        {
            MatchBoard        board  = AwaitingMoveBoard();
            AuthoritativeTime now    = AuthoritativeAt(MetaDuration.Zero);
            MetaTime          endsAt = MatchTableTime.GetBeatEndsAt(board, now, MetaDuration.FromMilliseconds(1100));

            Assert.That(endsAt, Is.EqualTo(now.Timestamp));
            Assert.That(MatchTableTime.IsBeatPlaying(now.Timestamp, endsAt, now), Is.False);
        }

        [Test]
        public void ABeatRunsFromItsStartStampUpToButNotIncludingItsEnd()
        {
            MetaTime startedAt = T0;
            MetaTime endsAt    = T0 + MetaDuration.FromMilliseconds(1100);

            Assert.That(MatchTableTime.IsBeatPlaying(startedAt, endsAt, AuthoritativeAt(MetaDuration.Zero)), Is.True);
            Assert.That(MatchTableTime.IsBeatPlaying(startedAt, endsAt, AuthoritativeAt(MetaDuration.FromMilliseconds(1099))), Is.True);
            Assert.That(MatchTableTime.IsBeatPlaying(startedAt, endsAt, AuthoritativeAt(MetaDuration.FromMilliseconds(1100))), Is.False);
        }

        [Test]
        public void NoBeatIsPlayingWhenNoneHasStarted()
        {
            Assert.That(MatchTableTime.IsBeatPlaying(MetaTime.Epoch, MetaTime.Epoch, AuthoritativeAt(MetaDuration.Zero)), Is.False);
        }

        [Test]
        public void ABeatEndsRatherThanHangingWhenTheClockMovesUnderIt()
        {
            // A playing beat withholds the terminal phase, so a beat that never ends would hide the result
            // forever. A clock reading before the start stamp means the clock moved back after the beat started,
            // and IsBeatPlaying treats the beat as over.
            MetaTime startedAt = T0;
            MetaTime endsAt    = T0 + MetaDuration.FromMilliseconds(1100);

            Assert.That(MatchTableTime.IsBeatPlaying(startedAt, endsAt, AuthoritativeAt(-MetaDuration.FromSeconds(120))), Is.False);
            Assert.That(MatchTableTime.IsBeatPlaying(startedAt, endsAt, AuthoritativeAt(MetaDuration.FromSeconds(120))), Is.False);
        }

        #endregion

        #region The presented turn indicator

        [Test]
        public void TheSeatOnTurnIsShownOnceTheBoardHasCaughtUp()
        {
            MatchBoard board = AwaitingMoveBoard();
            Assert.That(board.SeatOnTurn, Is.GreaterThanOrEqualTo(0), $"seed {Seed}");

            Assert.That(MatchTableTime.GetPresentedSeatOnTurn(board, PresentedAt(MetaDuration.Zero)), Is.EqualTo(board.SeatOnTurn));
        }

        [Test]
        public void NoSeatIsShownOnTurnWhileTheBoardStillTrails()
        {
            // While the screen still animates the previous trick, the board already names the next trick's
            // leader. Showing that seat on turn would reveal a turn the player has not seen start.
            MatchBoard    board    = AwaitingMoveBoard();
            PresentedTime trailing = PresentedTime.Trailing(AuthoritativeAt(MetaDuration.Zero), MetaDuration.FromMilliseconds(700));

            Assert.That(board.SeatOnTurn, Is.GreaterThanOrEqualTo(0), $"seed {Seed}");
            Assert.That(MatchTableTime.GetPresentedSeatOnTurn(board, trailing), Is.EqualTo(SeatRotation.NoSeat));
        }

        [Test]
        public void NoSeatIsShownOnTurnDuringTheResolvePause()
        {
            MatchBoard board = ResolvingTrickBoard();

            Assert.That(MatchTableTime.GetPresentedSeatOnTurn(board, PresentedAt(MetaDuration.Zero)), Is.EqualTo(SeatRotation.NoSeat));
        }

        #endregion

        #region The terminal phase, and what the server would accept

        [Test]
        public void TheTerminalPhaseIsWithheldUntilTheFinishHasPlayedOut()
        {
            MatchModel model = EndedModel();

            PresentedTime trailing = PresentedTime.Trailing(AuthoritativeAt(MetaDuration.FromSeconds(30)), MetaDuration.FromMilliseconds(1500));
            Assert.That(MatchTableTime.GetPresentedPhase(model, trailing), Is.EqualTo(MatchPhase.Playing));

            PresentedTime caughtUp = PresentedAt(MetaDuration.FromSeconds(30));
            Assert.That(MatchTableTime.GetPresentedPhase(model, caughtUp), Is.EqualTo(MatchPhase.Ended));
        }

        [Test]
        public void APlayingPhaseIsNeverWithheld()
        {
            MatchModel model = MatchTestDeals.Model(MatchEngine.Create(Seed, MatchTimings.Default, T0), MatchTestDeals.Seat0PlayerId);

            PresentedTime trailing = PresentedTime.Trailing(AuthoritativeAt(MetaDuration.FromSeconds(5)), MetaDuration.FromMilliseconds(1500));
            Assert.That(MatchTableTime.GetPresentedPhase(model, trailing), Is.EqualTo(MatchPhase.Playing));
        }

        [Test]
        public void AMoveIsAcceptedBeforeTheDeadlineAndNotAtIt()
        {
            MatchBoard board = AwaitingMoveBoard();

            Assert.That(MatchTableTime.CanStillSubmitMove(board, AuthoritativeAt(MetaDuration.FromSeconds(19))), Is.True);
            Assert.That(MatchTableTime.CanStillSubmitMove(board, AuthoritativeAt(MatchTimings.Default.MoveDeadline)), Is.False);
            Assert.That(MatchTableTime.CanStillSubmitMove(board, AuthoritativeAt(MetaDuration.FromSeconds(60))), Is.False);
        }

        [Test]
        public void NoMoveIsAcceptedDuringTheResolvePause()
        {
            MatchBoard board = ResolvingTrickBoard();

            Assert.That(MatchTableTime.CanStillSubmitMove(board, AuthoritativeAt(MetaDuration.Zero)), Is.False);
        }

        static MatchModel EndedModel()
        {
            MatchModel       model  = MatchTestDeals.Model(MatchEngine.Create(Seed, MatchTimings.Default, T0), MatchTestDeals.Seat0PlayerId);
            MetaActionResult result = new MatchAdvanced(MatchTurnPhase.Finished, MatchPhase.Ended, MetaTime.Epoch, T0).InvokeExecute(model, commit: true);

            Assert.That(result.IsSuccess, Is.True, $"seed {Seed}: MatchAdvanced was refused: {result}");
            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended));
            return model;
        }

        #endregion
    }
}
