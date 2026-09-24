using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Tests for what a table does when its players stop playing: the join window, move deadlines versus
    /// disconnect grace, auto-play, strikes and cover, reclaiming a seat, playing out, abandonment, and cold wakes.
    /// <para>
    /// The tests run the real <see cref="MatchHost"/> driver against a real engine with a fixed seed. Nothing
    /// sleeps or polls: every duration is a parameter, and each later moment is passed as a different <c>now</c>.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchSeatPolicyTests
    {
        const ulong Seed = 7717UL;

        static readonly MetaTime     T0              = MatchTestDeals.T0;
        static readonly MetaDuration MoveDeadline    = MatchTestDeals.SeatTimings().MoveDeadline;
        static readonly MetaDuration DisconnectGrace = MatchTestDeals.SeatTimings().DisconnectGrace;
        static readonly MetaDuration RestartGrace    = MatchTestDeals.SeatTimings().RestartReconnectGrace;
        static readonly MetaDuration JoinWindow      = MatchTestDeals.SeatTimings().JoinWindow;

        static MatchTimings Timings() => MatchTestDeals.SeatTimings();

        static EntityId Player(int seat) => MatchTestDeals.Player(seat);

        static MatchModel NewTable(MatchTimings timings, int numHumanSeats = 1) => MatchTestDeals.NewTable(Seed, timings, numHumanSeats);

        static MatchModel StartedTable(MatchTimings timings, out TestMatchHost host, int numHumanSeats = 1) =>
            MatchTestDeals.StartedTable(Seed, timings, out host, numHumanSeats);

        static MetaTime Later(MetaDuration by) => T0 + by;

        #region The join window

        [Test]
        public void PlayDoesNotBeginUntilEveryHumanSeatHasArrived()
        {
            // Bots can play as soon as a table is dealt. If play began at creation, the bots could play the first
            // cards while a player's WebAssembly client is still loading.
            MatchModel      model = NewTable(Timings(), numHumanSeats: 2);
            TestMatchHost   host  = new TestMatchHost(model, Seed);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            MatchHost.RunTable(model, pendingBotMove, T0, host);
            Assert.That(model.PlayHasBegun, Is.False, $"seed {Seed}: play began with nobody at the table");
            Assert.That(model.Board.PlayIndex, Is.Zero, $"seed {Seed}");

            MatchHost.NoteSeatPresent(model, 0, T0, host);
            MatchHost.RunTable(model, pendingBotMove, T0, host);
            Assert.That(model.PlayHasBegun, Is.False, $"seed {Seed}: play began with one of two humans still loading");

            MatchHost.NoteSeatPresent(model, 1, T0, host);
            MatchHost.RunTable(model, pendingBotMove, T0, host);
            Assert.That(model.PlayHasBegun, Is.True, $"seed {Seed}: play did not begin once everybody had arrived");
        }

        [Test]
        public void PlayBeginsWhenTheJoinWindowExpires_AndASeatThatNeverArrivedIsCovered()
        {
            // A human seat that never arrived is treated as absent with no grace, so a bot covers it immediately
            // instead of the table waiting out the disconnect grace.
            MatchModel      model = NewTable(Timings(), numHumanSeats: 2);
            TestMatchHost   host  = new TestMatchHost(model, Seed);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            MatchHost.NoteSeatPresent(model, 0, T0, host);
            MatchHost.RunTable(model, pendingBotMove, Later(JoinWindow), host);

            Assert.That(model.PlayHasBegun, Is.True, $"seed {Seed}");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.Human), $"seed {Seed}: the seat that arrived was covered");
            Assert.That(model.GetSeat(1).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot), $"seed {Seed}: the seat that never arrived was not covered");
            Assert.That(model.GetSeat(1).IsInGrace, Is.False, $"seed {Seed}: a seat that never arrived is not then given grace as well");
        }

        [Test]
        public void TheTableIsWaitingOnTheJoinWindow()
        {
            MatchModel model = NewTable(Timings());

            Assert.That(MatchHost.GetNextWakeAt(model, null), Is.EqualTo(T0 + JoinWindow), $"seed {Seed}");
        }

        [Test]
        public void ALeaderWhoArrivesEarlyGetsTheirWholeDeadlineOncePlayBegins()
        {
            // Every seat is human, so the starting leader is a human seat whatever the deal.
            MatchModel          model          = NewTable(Timings(), numHumanSeats: 4);
            TestMatchHost       host           = new TestMatchHost(model, Seed);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
            int                 leader         = model.Board.SeatOnTurn;

            MatchHost.NoteSeatPresent(model, leader, Later(MetaDuration.FromSeconds(1)), host);
            MatchHost.RunTable(model, pendingBotMove, Later(MetaDuration.FromSeconds(1)), host);
            Assert.That(model.Board.MoveDeadlineAt, Is.EqualTo(MetaTime.Epoch), $"seed {Seed}: the leader's clock started during the join window");

            MatchHost.RunTable(model, pendingBotMove, Later(JoinWindow), host);
            Assert.That(model.PlayHasBegun, Is.True, $"seed {Seed}");
            Assert.That(model.Board.MoveDeadlineAt, Is.EqualTo(Later(JoinWindow) + MoveDeadline), $"seed {Seed}: the leader's deadline was not counted from the start of play");
        }

        [Test]
        public void AJoinWindowLongerThanTheDeadlineDoesNotAutoPlayTheLeader()
        {
            MatchTimings timings = Timings();
            timings.JoinWindow = MoveDeadline + MetaDuration.FromSeconds(10);

            MatchModel          model          = NewTable(timings, numHumanSeats: 4);
            TestMatchHost       host           = new TestMatchHost(model, Seed);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
            int                 leader         = model.Board.SeatOnTurn;

            MatchHost.NoteSeatPresent(model, leader, Later(MetaDuration.FromSeconds(1)), host);
            MatchHost.RunTable(model, pendingBotMove, Later(timings.JoinWindow), host);

            Assert.That(model.PlayHasBegun, Is.True, $"seed {Seed}");
            Assert.That(model.Board.PlayIndex, Is.Zero, $"seed {Seed}: the leader was auto-played as play began");
            Assert.That(model.GetSeat(leader).ConsecutiveMissedDeadlines, Is.Zero, $"seed {Seed}: the leader was given a strike as play began");
        }

        [Test]
        public void ACardPlayedBeforePlayBeginsStartsNoClockForTheNextSeat()
        {
            MatchModel    model  = NewTable(Timings(), numHumanSeats: 4);
            TestMatchHost host   = new TestMatchHost(model, Seed);
            int           leader = model.Board.SeatOnTurn;
            int           next   = model.Engine.SeatOnTurnAfterNextPlay;

            MatchHost.NoteSeatPresent(model, leader, T0, host);
            MatchHost.NoteSeatPresent(model, next, T0, host);

            Card              card    = model.Engine.GetHand(leader)[0];
            MoveRefusalReason refusal = MatchHost.TrySubmitMove(model, Player(leader), leader, model.Board.PlayIndex, card, Later(MetaDuration.FromSeconds(1)), host);

            Assert.That(refusal, Is.EqualTo(MoveRefusalReason.None), $"seed {Seed}");
            Assert.That(model.PlayHasBegun, Is.False, $"seed {Seed}");
            Assert.That(model.Board.MoveDeadlineAt, Is.EqualTo(MetaTime.Epoch), $"seed {Seed}: the next seat's clock started during the join window");
        }

        #endregion

        #region Abandonment

        [Test]
        public void ATableNobodyEverJoinedIsAbandoned()
        {
            // A table is abandoned only when the join window runs out and no human ever arrived. The same rule
            // cleans up a table left by a failed mint, which has a full roster but no player pointing at it.
            MatchModel    model = NewTable(Timings());
            TestMatchHost host  = new TestMatchHost(model, Seed);

            MatchHost.RunTable(model, new MatchPendingBotMove(), Later(JoinWindow), host);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Abandoned), $"seed {Seed}");
            Assert.That(model.EndedAt, Is.EqualTo(Later(JoinWindow)), $"seed {Seed}: the end stamp is the host's");
            Assert.That(model.Board.PlayIndex, Is.Zero, $"seed {Seed}: an abandoned table played a card");
            Assert.That(model.Board.Standings, Is.Empty, $"seed {Seed}: an abandoned table has no result to show");
            Assert.That(host.CountOf<MatchAbandoned>(), Is.EqualTo(1), $"seed {Seed}");
        }

        [Test]
        public void ATableThatLostItsOnlyHumanAfterStartingIsPlayedOutRatherThanAbandoned()
        {
            // Most tables have one human. If a table with no connected human were abandoned, closing the tab would
            // erase the game, and quitting would be a way to avoid recording a loss.
            MatchModel      model = StartedTable(Timings(), out TestMatchHost host);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);
            MatchHost.RunTable(model, pendingBotMove, Later(DisconnectGrace), host);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}: the game was erased instead of finished");
            Assert.That(model.Board.PlayIndex, Is.EqualTo(MatchRules.NumPlays), $"seed {Seed}: the table did not play itself out");
            Assert.That(model.Board.TrickWinnerSeats, Has.Count.EqualTo(MatchRules.NumTricks), $"seed {Seed}");
            Assert.That(model.Board.Standings, Has.Count.EqualTo(MatchRules.NumSeats), $"seed {Seed}: no result was computed");
            Assert.That(host.CountOf<MatchAbandoned>(), Is.Zero, $"seed {Seed}: a started table was abandoned");
        }

        [Test]
        public void APlayedOutTableHoldsNoBeatAndNoDeadline()
        {
            // Nobody is watching, so the remaining tricks resolve in one call with no resolve pause or deadline.
            MatchTimings timings = Timings();
            timings.ResolvePause = MetaDuration.FromMilliseconds(1500);

            MatchModel model = StartedTable(timings, out TestMatchHost host);
            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: true, host);
            MatchHost.RunTable(model, new MatchPendingBotMove(), T0, host);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}");
            Assert.That(model.Board.HasMoveDeadline, Is.False, $"seed {Seed}");
        }

        #endregion

        #region A connected seat is on a deadline; a disconnected one is on grace

        [Test]
        public void OnlyAConnectedHumanSeatIsGivenAMoveDeadline()
        {
            // A disconnected player must not collect strikes for turns they could not take. MoveDeadlineFor
            // guarantees this by never giving a disconnected seat a deadline.
            MatchTimings timings = Timings();

            MatchSeat connected    = new MatchSeat(0, PlayerPublicIdentity.ForSeat(Player(0), "A"), MatchSeatOccupancy.Human, hasArrived: true);
            MatchSeat disconnected = new MatchSeat(1, PlayerPublicIdentity.ForSeat(Player(1), "B"), MatchSeatOccupancy.Human, hasArrived: false);
            MatchSeat covered      = new MatchSeat(2, PlayerPublicIdentity.ForSeat(Player(2), "C"), MatchSeatOccupancy.HumanCoveredByBot, hasArrived: true);
            MatchSeat bot          = new MatchSeat(3, PlayerPublicIdentity.ForBot(EntityId.None, "Bot", avatarId: null), MatchSeatOccupancy.Bot, hasArrived: false);

            Assert.That(MatchSeatPolicy.MoveDeadlineFor(connected, timings), Is.EqualTo(MoveDeadline), "a connected human is on the clock");
            Assert.That(MatchSeatPolicy.MoveDeadlineFor(disconnected, timings), Is.EqualTo(MetaDuration.Zero), "a disconnected human was given a deadline as well as grace");
            Assert.That(MatchSeatPolicy.MoveDeadlineFor(covered, timings), Is.EqualTo(MetaDuration.Zero), "a covered seat was given a deadline; the bot playing it is on a think delay");
            Assert.That(MatchSeatPolicy.MoveDeadlineFor(bot, timings), Is.EqualTo(MetaDuration.Zero), "a bot seat was given a deadline");
        }

        [Test]
        public void ASeatThatDropsWhileOnTurnLosesItsDeadlineAndGainsGrace()
        {
            MatchModel model = StartedTable(Timings(), out TestMatchHost host);
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);

            Assert.That(model.Board.HasMoveDeadline, Is.True, $"seed {Seed}: a connected seat on turn has no deadline");

            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);

            Assert.That(model.Board.HasMoveDeadline, Is.False, $"seed {Seed}: the deadline survived the disconnect");
            Assert.That(model.GetSeat(0).GraceEndsAt, Is.EqualTo(T0 + DisconnectGrace), $"seed {Seed}");
            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.Zero, $"seed {Seed}");
        }

        [Test]
        public void ASeatThatComesBackWhileOnTurnGetsAWholeFreshDeadline()
        {
            MatchModel model = StartedTable(Timings(), out TestMatchHost host);
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);

            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);
            MetaTime backAt = Later(MetaDuration.FromSeconds(5));
            MatchHost.NoteSeatPresent(model, 0, backAt, host);

            Assert.That(model.GetSeat(0).IsInGrace, Is.False, $"seed {Seed}: the grace timer kept running after they came back");
            Assert.That(model.Board.MoveDeadlineAt, Is.EqualTo(backAt + MoveDeadline), $"seed {Seed}: they came back to a turn already half gone");
        }

        [Test]
        public void ADisconnectedSeatOnTurnCollectsNoStrikes()
        {
            // Run the table almost to the end of the grace and check that the absent seat has no strikes.
            MatchModel      model = StartedTable(Timings(), out TestMatchHost host);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);

            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);
            MatchHost.RunTable(model, pendingBotMove, Later(MetaDuration.FromSeconds(19)), host);

            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.Zero, $"seed {Seed}: an absent seat was struck for turns it could not take");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.Human), $"seed {Seed}: grace was not respected");
        }

        #endregion

        #region Auto-play, strikes and cover

        [Test]
        public void ALapsedDeadlineAutoPlaysThroughTheStrongestProfile()
        {
            // Auto-play must not make deliberate mistakes with a player's cards, so it uses the strongest
            // profile. The position is written out so the strongest profile and the lowest legal card give
            // different answers (see VoidWithTrumpsTable). Otherwise the check could not tell them apart.
            MatchModel      model = VoidWithTrumpsTable(out Card cheapestTrump, out Card lowestLegal);
            TestMatchHost   host  = new TestMatchHost(model, Seed);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            Assert.That(model.Board.SeatOnTurn, Is.EqualTo(0), $"seed {Seed}");
            Assert.That(cheapestTrump, Is.Not.EqualTo(lowestLegal), $"seed {Seed}: the position offers no wrong answer, so the right one proves nothing");

            MatchHost.RunTable(model, pendingBotMove, model.Board.MoveDeadlineAt, host);

            Assert.That(model.Board.PlayIndex, Is.EqualTo(4), $"seed {Seed}: the lapsed deadline played nothing and the table froze");
            Assert.That(model.Board.Plays[3].Seat, Is.EqualTo(0), $"seed {Seed}");
            Assert.That(model.Board.Plays[3].Card, Is.EqualTo(cheapestTrump), $"seed {Seed}: the auto-play did not use the strongest profile");
            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.EqualTo(1), $"seed {Seed}");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.Human), $"seed {Seed}: one lapse handed the seat away");
            Assert.That(host.HandCorrectedSeats, Does.Contain(0), $"seed {Seed}: the seat's hand changed without a correction being sent");
        }

        /// <summary>
        /// A table where seat 0 is on turn, has no card of the led suit, and holds two trumps. The heuristic wins
        /// the trick with its cheapest trump, and the lowest legal card is a club that cannot win. Both cards are
        /// returned so the test can check that they differ.
        /// </summary>
        static MatchModel VoidWithTrumpsTable(out Card cheapestTrump, out Card lowestLegal)
        {
            cheapestTrump = MatchTestDeals.Spades(Rank.Five);
            lowestLegal   = MatchTestDeals.Clubs(Rank.Two);

            List<Card> deck = MatchTestDeals.BuildDeck(
                seat0:     new Card[] { MatchTestDeals.Clubs(Rank.Two), MatchTestDeals.Clubs(Rank.Three), MatchTestDeals.Spades(Rank.Five), MatchTestDeals.Spades(Rank.Nine), MatchTestDeals.Diamonds(Rank.Four) },
                seat1:     new Card[] { MatchTestDeals.Hearts(Rank.Five), MatchTestDeals.Hearts(Rank.Six), MatchTestDeals.Hearts(Rank.Seven), MatchTestDeals.Hearts(Rank.Eight), MatchTestDeals.Hearts(Rank.Nine) },
                seat2:     new Card[] { MatchTestDeals.Hearts(Rank.Ten), MatchTestDeals.Hearts(Rank.Jack), MatchTestDeals.Hearts(Rank.Queen), MatchTestDeals.Hearts(Rank.King), MatchTestDeals.Hearts(Rank.Ace) },
                seat3:     new Card[] { MatchTestDeals.Hearts(Rank.Two), MatchTestDeals.Hearts(Rank.Three), MatchTestDeals.Hearts(Rank.Four), MatchTestDeals.Diamonds(Rank.Two), MatchTestDeals.Diamonds(Rank.Three) },
                trumpCard: MatchTestDeals.Spades(Rank.Two));

            MatchEngine engine = MatchTestDeals.Engine(Seed, deck, startingLeaderSeat: 1, Timings());
            MatchTestDeals.PlayInOrder(engine, MatchTestDeals.Hearts(Rank.Five), MatchTestDeals.Hearts(Rank.Ten), MatchTestDeals.Hearts(Rank.Two));
            engine.ArmMoveDeadline(T0, MoveDeadline);

            return MatchTestDeals.Model(engine, Player(0));
        }

        [Test]
        public void TwoConsecutiveLapsesHandTheSeatToACoveringBot()
        {
            // One lapse only records a strike. The second consecutive lapse covers the seat.
            MatchModel      model = StartedTable(Timings(), out TestMatchHost host);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            PlayerPublicIdentity before = model.GetSeat(0).Identity;

            LapseOneDeadline(model, pendingBotMove, host);
            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.EqualTo(1), $"seed {Seed}");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.Human), $"seed {Seed}");

            LapseOneDeadline(model, pendingBotMove, host);
            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.EqualTo(2), $"seed {Seed}");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot), $"seed {Seed}: two lapses did not cover the seat");
            Assert.That(model.GetSeat(0).PlayerId, Is.EqualTo(Player(0)), $"seed {Seed}: the cover lost the human's identity");
            Assert.That(model.GetSeat(0).DisplayName, Is.EqualTo("Player 0"), $"seed {Seed}: the cover is anonymous");

            // The cover keeps the player's whole identity, so a covered seat never looks like a computer player
            // (docs/bots.md, "Names").
            Assert.That(model.GetSeat(0).Identity, Is.EqualTo(before), $"seed {Seed}: the cover changed how the person at the seat looks");
        }

        [Test]
        public void AMoveResetsTheStrikeCount()
        {
            MatchModel      model = StartedTable(Timings(), out TestMatchHost host);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            LapseOneDeadline(model, pendingBotMove, host);
            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.EqualTo(1), $"seed {Seed}");

            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);
            Card card = model.Engine.GetLegalPlays(0)[0];
            Assert.That(MatchHost.TrySubmitMove(model, Player(0), 0, model.Board.PlayIndex, card, T0, host), Is.EqualTo(MoveRefusalReason.None), $"seed {Seed}");

            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.Zero, $"seed {Seed}: a lapse and a move later, the count had not started over");
        }

        #endregion

        #region Reclaim

        [Test]
        public void AMoveTakesACoveredSeatBackEvenWhenTheEngineRefusesIt()
        {
            // The covering bot and the human race to play the same turn, and the bot often wins. If reclaiming
            // required an accepted move, it would rarely work. Here the move is refused for a stale play index
            // and the seat still returns to the human.
            MatchModel      model = StartedTable(Timings(), out TestMatchHost host);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            LapseOneDeadline(model, pendingBotMove, host);
            LapseOneDeadline(model, pendingBotMove, host);
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot), $"seed {Seed}");

            // A move with a stale play index, as when the tap arrives just after another play took the turn.
            Card anyCard = model.Engine.GetHand(0).Count > 0 ? model.Engine.GetHand(0)[0] : model.Board.TrumpCard;
            MoveRefusalReason refusal = MatchHost.TrySubmitMove(model, Player(0), 0, model.Board.PlayIndex - 1, anyCard, T0, host);

            Assert.That(refusal, Is.EqualTo(MoveRefusalReason.StalePlayIndex), $"seed {Seed}: this test needs a refused move");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.Human), $"seed {Seed}: the reclaim depended on the move being accepted");
            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.Zero, $"seed {Seed}");
        }

        [Test]
        public void ACoveredSeatWhoseOwnerIsWatchingPlaysOnTheSlowReclaimDelay()
        {
            // With a normal short think delay, the covering bot would take every turn before its connected owner
            // could move, so the owner could never reclaim the seat. The cover waits CoveredSeatReclaimDelay
            // while the owner is connected, and plays at normal speed when the owner is gone.
            MatchTimings timings = Timings();
            timings.BotThinkDelayMin           = MetaDuration.FromMilliseconds(100);
            timings.BotThinkDelayMax           = MetaDuration.FromMilliseconds(100);
            timings.BotThinkDelayOccasionalMax = MetaDuration.FromMilliseconds(100);
            timings.CoveredSeatReclaimDelay    = MetaDuration.FromSeconds(3);

            MatchModel model = StartedTable(timings, out TestMatchHost host);

            // Compare the cover's think delay with the owner connected and with the owner gone.
            MatchSeatView view = MatchSeatView.ForSeat(model.Engine, model.Board.SeatOnTurn);

            BotDecision watching = BotPolicy.DecideCover(view, timings, Seed, ownerConnected: true);
            BotDecision gone     = BotPolicy.DecideCover(view, timings, Seed, ownerConnected: false);

            Assert.That(watching.ThinkDelay, Is.EqualTo(timings.CoveredSeatReclaimDelay), $"seed {Seed}: a watching owner has no window to reclaim in");
            Assert.That(gone.ThinkDelay, Is.EqualTo(MetaDuration.FromMilliseconds(100)), $"seed {Seed}: a table with nobody at it was slowed down for nobody");
            Assert.That(watching.Card, Is.EqualTo(gone.Card), $"seed {Seed}: the delay changed which card the cover plays");
        }

        [Test]
        public void ACoveredSeatIsDrivenThroughTheStrongestProfile()
        {
            MatchModel model = StartedTable(Timings(), out TestMatchHost host);
            int        seat  = model.Board.SeatOnTurn;

            List<MatchSeatState> states = MatchSeatPolicy.CurrentStates(model);
            states[seat] = MatchSeatPolicy.Cover(states[seat]);
            MatchTestDeals.Apply(model, new MatchSeatsUpdated(states, MetaTime.Epoch, playHasBegun: false, joinWindowEndsAt: MetaTime.Epoch));

            BotDecision decision = MatchHost.DecideBotMove(model, seat, Seed);
            Card        expected = BotPolicy.ChooseCard(MatchSeatView.ForSeat(model.Engine, seat), BotProfiles.Strongest, Seed);

            Assert.That(decision.Card, Is.EqualTo(expected), $"seed {Seed}: a covering bot may make a deliberate mistake with someone else's cards");
        }

        #endregion

        #region Leaving deliberately

        [Test]
        public void LeavingSkipsGraceAndIsStillRecorded()
        {
            MatchModel model = StartedTable(Timings(), out TestMatchHost host);

            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: true, host);

            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot), $"seed {Seed}: leaving waited out a grace timer");
            Assert.That(model.GetSeat(0).IsInGrace, Is.False, $"seed {Seed}");

            MatchHost.RunTable(model, new MatchPendingBotMove(), T0, host);
            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}: leaving erased the game");
            Assert.That(model.Board.Standings, Has.Count.EqualTo(MatchRules.NumSeats), $"seed {Seed}");
        }

        #endregion

        #region Restart

        [Test]
        public void AColdWakeClearsEveryConnectedFlagAndHoldsTheSeatsOnTheRestartGrace()
        {
            MatchModel model = StartedTable(Timings(), out TestMatchHost host, numHumanSeats: 2);

            MetaTime wokeAt = Later(MetaDuration.FromMinutes(5));
            MatchHost.NoteColdWake(model, wokeAt, host);

            Assert.That(model.GetSeat(0).IsConnected, Is.False, $"seed {Seed}");
            Assert.That(model.GetSeat(1).IsConnected, Is.False, $"seed {Seed}");
            Assert.That(model.GetSeat(0).GraceEndsAt, Is.EqualTo(wokeAt + RestartGrace), $"seed {Seed}: the seat was not held on the restart grace");
            Assert.That(model.GetSeat(0).HasEverConnected, Is.True, $"seed {Seed}: a wake made a played table look like one nobody joined");
            Assert.That(model.Board.HasMoveDeadline, Is.False, $"seed {Seed}: a stale deadline survived the wake");
        }

        [Test]
        public void NobodyIsComingBackIsFalseOnTheFrameATableWakes()
        {
            // A cold wake clears every connected flag, so a table in the middle of a game looks deserted when it
            // wakes. A rule based on connection state alone would end the games that persistence is meant to keep.
            MatchModel model = StartedTable(Timings(), out TestMatchHost host, numHumanSeats: 2);

            MetaTime wokeAt = Later(MetaDuration.FromMinutes(5));
            MatchHost.NoteColdWake(model, wokeAt, host);

            // Negative control: right after the wake, every human seat is disconnected, so a rule based on
            // connection state alone would play the table out here.
            foreach (MatchSeat seat in model.Seats)
                Assert.That(!seat.HasOwner || !seat.IsConnected, Is.True, $"seed {Seed}: this test needs a table that looks deserted");

            Assert.That(MatchSeatPolicy.NobodyIsComingBack(model, wokeAt), Is.False, $"seed {Seed}: the table was played out on the frame it woke");
            Assert.That(MatchSeatPolicy.NobodyIsComingBack(model, wokeAt + RestartGrace - MetaDuration.FromSeconds(1)), Is.False, $"seed {Seed}: the table was played out a second before the restart grace lapsed");
            Assert.That(MatchSeatPolicy.NobodyIsComingBack(model, wokeAt + RestartGrace), Is.True, $"seed {Seed}: the table was never collected");
        }

        [Test]
        public void AColdWakeHoldsACoveredSeatWhoseOwnerIsStillWatching()
        {
            // MatchSeatPolicy.Cover clears a seat's grace. A covered seat whose owner is still connected therefore
            // has no grace, and the wake clears its connection. The wake must still give it the restart grace.
            MatchModel model = CoveredButWatchingTable(out TestMatchHost host);

            MetaTime wokeAt = Later(MetaDuration.FromMinutes(5));
            MatchHost.NoteColdWake(model, wokeAt, host);

            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot), $"seed {Seed}: the wake changed who is playing the seat");
            Assert.That(model.GetSeat(0).IsConnected, Is.False, $"seed {Seed}");
            Assert.That(model.GetSeat(0).GraceEndsAt, Is.EqualTo(wokeAt + RestartGrace), $"seed {Seed}: a covered seat whose owner was watching was left with no grace at all");
            Assert.That(MatchSeatPolicy.NobodyIsComingBack(model, wokeAt), Is.False, $"seed {Seed}: the table was collected on the frame it woke");

            // The owner reconnects within the restart grace and takes the seat back.
            MatchHost.NoteSeatPresent(model, 0, wokeAt + MetaDuration.FromSeconds(1), host);
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.Human), $"seed {Seed}: the seat did not come back to its owner");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ATableWokenColdIsNotPlayedOutUntilItsRestartGraceHasLapsed(bool coveredButWatching)
        {
            // With coveredButWatching, the owner of seat 0 is watching a bot play for them and can take the seat
            // back with one move, so the wake must not end the game before they can either.
            TestMatchHost host;
            MatchModel    model = coveredButWatching ? CoveredButWatchingTable(out host) : StartedTable(Timings(), out host);

            MetaTime wokeAt    = Later(MetaDuration.FromMinutes(5));
            int      playIndex = model.Board.PlayIndex;

            // Use a new pending bot move, as a restarted actor does. The pending bot decision was lost with the
            // old process.
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            MatchHost.NoteColdWake(model, wokeAt, host);
            MatchHost.RunTable(model, pendingBotMove, wokeAt, host);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Playing), $"seed {Seed}: a woken table was played out before anyone could reconnect");
            Assert.That(model.Board.PlayIndex, Is.EqualTo(playIndex), $"seed {Seed}: the wake frame played the table out");

            // The table is still played out once the restart grace ends and nobody has come back.
            MatchHost.RunTable(model, pendingBotMove, wokeAt + RestartGrace, host);
            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {Seed}: the table was never collected");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ASecondColdWakeDoesNotPushTheGraceOutAgain(bool coveredButWatching)
        {
            // The actor can shut down before the restart grace ends, so a later wake is the one that acts on the
            // lapse. If every wake re-armed the grace, it would always end after the current wake and the table
            // would never be played out. The first wake clears the connected flag it reads, so a later wake finds
            // nothing to arm, including for a covered seat whose owner was watching.
            TestMatchHost host;
            MatchModel    model = coveredButWatching ? CoveredButWatchingTable(out host) : StartedTable(Timings(), out host);

            MetaTime firstWakeAt = Later(MetaDuration.FromMinutes(5));
            MatchHost.NoteColdWake(model, firstWakeAt, host);
            MetaTime graceEndsAtAfterFirstWake = model.GetSeat(0).GraceEndsAt;

            MatchHost.NoteColdWake(model, firstWakeAt + MetaDuration.FromMinutes(4), host);

            Assert.That(model.GetSeat(0).GraceEndsAt, Is.EqualTo(graceEndsAtAfterFirstWake), $"seed {Seed}: the grace was re-armed and the table can never be collected");
        }

        [Test]
        public void ATableIsWaitingOnTheEarliestGraceItHolds()
        {
            MatchModel model = StartedTable(Timings(), out TestMatchHost host, numHumanSeats: 2);

            MatchHost.NoteSeatAbsent(model, 1, Later(MetaDuration.FromSeconds(5)), skipGrace: false, host);
            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: false, host);

            Assert.That(MatchHost.GetNextWakeAt(model, null), Is.EqualTo(T0 + DisconnectGrace), $"seed {Seed}: the table is not waiting on the grace that lapses first");
        }

        #endregion

        #region Activity

        [Test]
        public void OnlyAnAcceptedMoveStampsActivity()
        {
            // Otherwise a connected player who never moves would keep an idle table looking active forever.
            MatchModel model = StartedTable(Timings(), out TestMatchHost host);
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);

            MetaTime initialLastActivityAt = model.LastActivityAt;
            Card     card                  = model.Engine.GetLegalPlays(0)[0];

            // A refused move, a reconnect and a cold wake change the table without advancing the game.
            MetaTime refusedAt = Later(MetaDuration.FromMinutes(1));
            Assert.That(MatchHost.TrySubmitMove(model, Player(0), 0, model.Board.PlayIndex - 1, card, refusedAt, host), Is.Not.EqualTo(MoveRefusalReason.None), $"seed {Seed}");
            MatchHost.NoteSeatPresent(model, 0, refusedAt, host);
            MatchHost.NoteColdWake(model, refusedAt, host);
            Assert.That(model.LastActivityAt, Is.EqualTo(initialLastActivityAt), $"seed {Seed}: something other than a move stamped activity");

            // An accepted move does update the activity time.
            MatchHost.NoteSeatPresent(model, 0, refusedAt, host);
            MetaTime playedAt = Later(MetaDuration.FromMinutes(2));
            Assert.That(MatchHost.TrySubmitMove(model, Player(0), 0, model.Board.PlayIndex, card, playedAt, host), Is.EqualTo(MoveRefusalReason.None), $"seed {Seed}");
            Assert.That(model.LastActivityAt, Is.EqualTo(playedAt), $"seed {Seed}: an accepted move did not stamp activity");
        }

        #endregion

        #region Forcing the timers

        [Test]
        public void ForceExpireBringsEveryPendingStampForwardAndCountsThem()
        {
            // Timers configured long enough never to fire by accident cannot fire on demand either.
            // ForceExpireTimers moves every pending deadline to now and returns how many it moved, so a test can
            // check the result instead of sleeping.
            MatchModel      model = NewTable(Timings());
            TestMatchHost   host  = new TestMatchHost(model, Seed);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            Assert.That(MatchHost.ForceExpireTimers(model, pendingBotMove, T0, host), Is.EqualTo(1), $"seed {Seed}: the join window was not among the stamps");
            MatchHost.RunTable(model, pendingBotMove, T0, host);
            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Abandoned), $"seed {Seed}: forcing the join window did not run the rule it gates");
        }

        [Test]
        public void ForceExpireLapsesADeadlineThatWasNowhereNearRunningOut()
        {
            MatchModel      model = StartedTable(Timings(), out TestMatchHost host);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);

            int playIndex = model.Board.PlayIndex;
            Assert.That(model.Board.MoveDeadlineAt, Is.GreaterThan(T0), $"seed {Seed}: this test needs a deadline in the future");

            int expired = MatchHost.ForceExpireTimers(model, pendingBotMove, T0, host);
            MatchHost.RunTable(model, pendingBotMove, T0, host);

            Assert.That(expired, Is.GreaterThanOrEqualTo(1), $"seed {Seed}");
            Assert.That(model.Board.PlayIndex, Is.GreaterThan(playIndex), $"seed {Seed}: the forced deadline did not auto-play the seat");
            Assert.That(model.GetSeat(0).ConsecutiveMissedDeadlines, Is.EqualTo(1), $"seed {Seed}: the forced lapse skipped the rule it gates");
        }

        [Test]
        public void ForceExpireOnATableWaitingForNothingMovesNothing()
        {
            MatchModel model = StartedTable(Timings(), out TestMatchHost host);
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);
            MatchHost.NoteSeatAbsent(model, 0, T0, skipGrace: true, host);

            // The seat is covered with no grace and no deadline, so no timer is pending.
            Assert.That(MatchHost.ForceExpireTimers(model, new MatchPendingBotMove(), T0, host), Is.Zero, $"seed {Seed}");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// A table whose human seat is covered by a bot while its owner is still connected. A player reaches this
        /// state only by letting two deadlines in a row lapse while staying connected, so that is how it is built.
        /// </summary>
        static MatchModel CoveredButWatchingTable(out TestMatchHost host)
        {
            // Give the bots a real think delay. Without one, every seat plays instantly once the human seat is
            // covered, and a table that wrongly played out on the wake would look the same as one that waited.
            MatchTimings timings = Timings();
            timings.BotThinkDelayMin           = MetaDuration.FromSeconds(5);
            timings.BotThinkDelayMax           = MetaDuration.FromSeconds(5);
            timings.BotThinkDelayOccasionalMax = MetaDuration.FromSeconds(5);

            MatchModel      model = StartedTable(timings, out host);
            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();

            LapseOneDeadline(model, pendingBotMove, host);
            LapseOneDeadline(model, pendingBotMove, host);

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Playing), $"seed {Seed}: this table finished before it could be covered");
            Assert.That(model.GetSeat(0).Occupancy, Is.EqualTo(MatchSeatOccupancy.HumanCoveredByBot), $"seed {Seed}: this test needs a covered seat");
            Assert.That(model.GetSeat(0).IsConnected, Is.True, $"seed {Seed}: this test needs an owner who is still watching");
            Assert.That(model.GetSeat(0).IsInGrace, Is.False, $"seed {Seed}: the cover left a grace running, so the wake has one to leave alone");
            return model;
        }

        /// <summary>
        /// Play until seat 0 is on turn, then run the table at its move deadline so seat 0 lapses once.
        /// </summary>
        static void LapseOneDeadline(MatchModel model, MatchPendingBotMove pendingBotMove, TestMatchHost host)
        {
            MatchTestDeals.PlayUntilSeatIsOnTurn(model, 0, host, Seed);
            Assert.That(model.Board.HasMoveDeadline, Is.True, $"seed {Seed}: no deadline to lapse");

            MatchHost.RunTable(model, pendingBotMove, model.Board.MoveDeadlineAt, host);
        }

        #endregion
    }
}
