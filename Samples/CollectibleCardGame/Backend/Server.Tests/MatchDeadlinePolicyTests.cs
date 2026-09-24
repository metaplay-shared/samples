using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The fold that answers "when should this actor wake". Every case below is a way a table could be left
    /// waiting on a stamp nobody will offer again.
    /// </summary>
    [TestFixture]
    public class MatchDeadlinePolicyTests
    {
        static readonly MetaTime Now = MetaTime.FromMillisecondsSinceEpoch(1_000_000);

        static MetaTime At(int secondsFromNow) => Now + MetaDuration.FromSeconds(secondsFromNow);

        /// <summary>
        /// A table with two seated humans, mid-game, both <b>present</b>, waiting on nothing in particular.
        /// Connected matters: a deadline is only in force while the seat that owes it is here, so a helper
        /// that left the seats disconnected would suppress every deadline in the file.
        /// </summary>
        static MatchModel Table()
        {
            return new MatchModel
            {
                Phase       = MatchTablePhase.Playing,
                Pacing      = new MatchPacing(),
                ResultAcked = new List<bool> { false, false },
                Seats = new List<MatchSeat>
                {
                    new MatchSeat(EntityId.Create(EntityKindCore.Player, 1), "A", SeatOccupancy.Human, BotProfileId.Strongest) { IsConnected = true },
                    new MatchSeat(EntityId.Create(EntityKindCore.Player, 2), "B", SeatOccupancy.Human, BotProfileId.Strongest) { IsConnected = true },
                },
            };
        }

        [Test]
        public void ATableWaitingOnNothingNeedsNoAttention()
        {
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.Null);
        }

        [Test]
        public void TheFoldTakesTheEarliestOfEverythingOutstanding()
        {
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            // Seat 1 has not arrived, which is the one state a join window is still waiting on somebody in.
            table.Seats[1].IsConnected = false;

            table.Pacing.ArmDeadline(MatchDeadlineKind.Turn, At(60), 0);
            table.Pacing.ArmJoinWindow(At(15));
            table.Pacing.SetGrace(0, At(30));
            table.Pacing.SetGrace(1, null);

            // Every one of them can be the earliest, and the whole point is that none is skipped.
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.EqualTo(At(15)));

            table.Pacing.ArmJoinWindow(null);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.EqualTo(At(30)));

            table.Pacing.SetGrace(0, null);
            table.Pacing.SetGrace(1, null);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.EqualTo(At(60)));
        }

        [Test]
        public void AHostHeldStampIsAnAttentionPointLikeAnyOther()
        {
            // A bot's think delay and a delivery retry live in actor memory rather than on the model, and a
            // fold that only looked at the model would report a table waiting on either as needing nothing.
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.Null,
                "the model alone says nothing is outstanding, which is the trap");

            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, new HostPendingWork(At(2), null)), Is.EqualTo(At(2)));

            // A delivery retry is only outstanding once there is a result to deliver: a retry stamp on a
            // mid-game table would make every live table report that it needs attention.
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, new HostPendingWork(null, At(5))), Is.Null);

            table.Result      = Outcome();
            table.ResultAcked = new List<bool> { true, false };
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, new HostPendingWork(null, At(5))), Is.EqualTo(At(5)));
        }

        [Test]
        public void AFinishedTableWithAnUnacknowledgedResultStillNeedsAttention()
        {
            // A finished table is not exempt. It is waiting on a result its accounts have not folded in, and
            // nobody else will ever ask — so if the fold went null here, somebody's result would be lost
            // quietly and for good.
            MatchModel table = Table();
            table.Phase       = MatchTablePhase.Ended;
            table.Result      = Outcome();
            table.ResultAcked = new List<bool> { true, false };

            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, new HostPendingWork(null, At(30))), Is.EqualTo(At(30)));
        }

        [Test]
        public void AResultWithNoRetryStampFoldsToNull()
        {
            // The fold has no fallback stamp: the actor's EnsureResultRetryArmed keeps an outstanding result in
            // the fold, and a stand-in stamp in the past would win every fold and spin the timer.
            MatchModel table = Table();
            table.Phase       = MatchTablePhase.Ended;
            table.Result      = Outcome();
            table.ResultAcked = new List<bool> { true, false };

            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.Null,
                "an outstanding result with no retry stamp is the actor's omission, not a stamp in 1970");
        }

        [Test]
        public void AFinishedTableWithEverythingDeliveredNeedsNothing()
        {
            MatchModel table = Table();
            table.Phase       = MatchTablePhase.Ended;
            table.Result      = Outcome();
            table.ResultAcked = new List<bool> { true, true };

            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.Null);
        }

        [Test]
        public void ABotSeatIsNotOwedAnAcknowledgement()
        {
            // Only a seat with an owner can acknowledge anything. A table that waited for a bot's account to
            // answer would wait forever and be woken forever.
            MatchModel table = Table();
            table.Seats[1]    = new MatchSeat(EntityId.None, "Bot", SeatOccupancy.Bot, BotProfileId.Practiced);
            table.Phase       = MatchTablePhase.Ended;
            table.Result      = Outcome();
            table.ResultAcked = new List<bool> { true, false };

            Assert.That(table.AllSeatsAcked, Is.True);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.Null);
        }

        [Test]
        public void ATableWithNoResultYetIsNotWaitingOnAnAcknowledgement()
        {
            // Mid-game, ResultAcked is all false and means nothing. Reading it as "outstanding" would make
            // every live table report that it needs attention immediately.
            MatchModel table = Table();

            Assert.That(table.Result, Is.Null);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.Null);
        }

        [Test]
        public void AnAbandonedTableIsWaitingOnBothPointersBeingCleared()
        {
            // It has no result to apply and still owes both accounts an errand: an account still pointing at a
            // table that never started is an account locked out of matchmaking for good.
            MatchModel table = Table();
            table.Phase       = MatchTablePhase.Abandoned;
            table.ResultAcked = new List<bool> { false, false };

            Assert.That(table.Result, Is.Null);
            Assert.That(MatchDeadlinePolicy.OwesDelivery(table), Is.True);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, new HostPendingWork(null, At(30))), Is.EqualTo(At(30)));

            table.ResultAcked = new List<bool> { true, true };
            Assert.That(MatchDeadlinePolicy.OwesDelivery(table), Is.False);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, new HostPendingWork(null, At(30))), Is.Null);
        }

        static MatchOutcomeRecord Outcome()
            => new MatchOutcomeRecord(MatchOutcome.Seat0Wins, 0, 12, MatchEndCause.DenAtZero, new List<bool> { true, false }, wasRanked: false, decidedAt: Now);

        // ---------------------------------------------------------------- the join window

        [Test]
        public void TheJoinWindowIsOfferedOnlyWhileSomebodyHasNotArrived()
        {
            // The stamp is written once, when the table is set up, and never cleared — so the predicate is
            // what stops the fold naming a long-past instant as the next thing the table is waiting for.
            MatchModel table = Table();
            table.Pacing.ArmJoinWindow(At(15));

            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.False, "both seats are here");
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.Null);

            table.Seats[1].IsConnected = false;
            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.True);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.EqualTo(At(15)));
        }

        [Test]
        public void ASeatOnGraceIsNotASeatThatNeverArrived()
        {
            // A player who arrived and dropped inside the window is governed by grace, and treating them as
            // never-arrived would cover them at the join window instead of at the end of their grace — a
            // different rule wearing this one's clothes.
            MatchModel table = Table();
            table.Pacing.ArmJoinWindow(At(15));
            table.Seats[1].IsConnected = false;
            table.Pacing.SetGrace(1, At(30));

            Assert.That(MatchDeadlinePolicy.SeatHasNotArrived(table, 1), Is.False);
            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.False);
        }

        [Test]
        public void ACoveredSeatEndsTheJoinWindowsInterestInIt()
        {
            // Covering is what the expiry does, so the predicate has to go false afterwards or the table wakes
            // on the same past stamp for the rest of its life.
            MatchModel table = Table();
            table.Pacing.ArmJoinWindow(At(15));
            table.Seats[1].IsConnected = false;
            table.Seats[1].Occupancy   = SeatOccupancy.HumanCoveredByBot;

            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.False);
        }

        [Test]
        public void ATerminalTableWaitsForNobodyToJoin()
        {
            MatchModel table = Table();
            table.Pacing.ArmJoinWindow(At(15));
            table.Seats[1].IsConnected = false;
            table.Phase = MatchTablePhase.Abandoned;

            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.False);
        }

        [Test]
        public void ATableNobodyCameToNeverStarted()
        {
            MatchModel table = Table();
            table.Seats[0].IsConnected = false;
            table.Seats[1].IsConnected = false;

            Assert.That(MatchDeadlinePolicy.NeverStarted(table), Is.True);

            // One human arriving is enough to make it a game that started, however the other seat goes.
            table.Seats[0].IsConnected = true;
            Assert.That(MatchDeadlinePolicy.NeverStarted(table), Is.False);
        }

        [Test]
        public void ABotSeatIsNotAHumanWhoFailedToArrive()
        {
            // A table of two bots satisfies "no human ever subscribed" vacuously and would be abandoned by the
            // letter of the rule, having done nothing wrong. The matchmaker never forms one, so this is a
            // guard rather than a case — but the predicate carries it.
            MatchModel table = Table();
            table.Seats[0] = new MatchSeat(EntityId.None, "Bot", SeatOccupancy.Bot, BotProfileId.Strongest);
            table.Seats[1] = new MatchSeat(EntityId.None, "Bot", SeatOccupancy.Bot, BotProfileId.Strongest);

            Assert.That(MatchDeadlinePolicy.HasAHumanSeat(table), Is.False);
            Assert.That(MatchDeadlinePolicy.NeverStarted(table), Is.False);
            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.False);
        }

        [Test]
        public void AFallbackBotTableNobodyCameToStillNeverStarted()
        {
            // One human seat and one bot seat is exactly what the fill wait forms, and a human who never
            // arrives at one leaves a table that never started.
            MatchModel table = Table();
            table.Seats[1] = new MatchSeat(EntityId.None, "Clockwork Cub", SeatOccupancy.Bot, BotProfileId.Casual);
            table.Seats[0].IsConnected = false;
            table.Pacing.ArmJoinWindow(At(15));

            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.True);
            Assert.That(MatchDeadlinePolicy.NeverStarted(table), Is.True);
        }

        // ---------------------------------------------------------------- a deadline that outlived its seat

        [Test]
        public void ADeadlineOlderThanAnAwaySeatsReturnIsNotAnAttentionPoint()
        {
            // A seat is mid-turn when its client drops. Grace takes the seat, and the turn deadline the seat
            // owed is an absolute stamp that goes on lapsing while nobody can act on it.
            //
            // Offered as an attention point, that stamp is dispatched the moment it passes and the rest of
            // the away player's turn is played out for them by the strongest bot profile — while they are
            // reconnecting. The seat would be governed by grace AND by the turn deadline at once, which
            // match.md declares mutually exclusive. The clock push at MatchPushClocks is the other half of
            // the same rule: the time their absence ate is handed back when they return.
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            table.Seats[0].IsConnected = false;
            table.Seats[1].IsConnected = false;

            table.Pacing.ArmDeadline(MatchDeadlineKind.Turn, At(-60), 0);
            table.Pacing.SetGrace(0, At(120));
            table.Pacing.SetGrace(1, At(120) );

            // Grace, and only grace, is what the table is waiting on.
            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.False);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.EqualTo(At(120)));
        }

        [Test]
        public void ADeadlineIsInForceAgainOnceTheSeatIsBack()
        {
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            table.Pacing.ArmDeadline(MatchDeadlineKind.Turn, At(30), 0);

            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.True);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.EqualTo(At(30)));
        }

        [Test]
        public void ACoveredSeatCarriesItsDeadlineAgain()
        {
            // Covering the seat spends its grace: from then on a bot has it, and a bot seat is governed by the
            // deadline. This is what stops the suppression turning into a table nobody ever plays.
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            table.Seats[0].IsConnected = false;
            MatchSeatPolicy.Cover(table.Seats[0]);

            table.Pacing.ArmDeadline(MatchDeadlineKind.Turn, At(-60), 0);

            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.True);
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, HostPendingWork.None), Is.EqualTo(At(-60)));
        }

        [Test]
        public void TheOtherSeatsAbsenceDoesNotSuppressTheSeatOnTurn()
        {
            // The deadline belongs to whoever owes it. Seat 1 walking away is seat 1's problem, and grace is
            // holding that seat; it must not buy seat 0 unlimited thinking time.
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            table.Seats[1].IsConnected = false;

            table.Pacing.ArmDeadline(MatchDeadlineKind.Turn, At(30), 0);

            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.True);
        }

        [Test]
        public void TheSharedMulliganDeadlineIsSuppressedWhileEitherSeatIsAway()
        {
            // Both seats mulligan under one deadline, so it belongs to neither and DeadlineSeat is None. A
            // restart during the mulligan would otherwise resolve it the instant the actor came back, silently
            // keeping both dealt hands — the same fault as spending a returning player's turn.
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };

            table.Pacing.ArmDeadline(MatchDeadlineKind.Turn, At(-5), MatchSeats.None);
            table.Pacing.SetGrace(0, At(120));
            table.Pacing.SetGrace(1, null);

            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.True, "both present: the deadline stands");

            table.Seats[1].IsConnected = false;
            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.False, "one away: nobody can be held to a shared deadline");
        }

        [Test]
        public void ABotOnlyTableKeepsItsDeadlineWithNobodyConnected()
        {
            // Nothing here is a human who might come back, so there is nothing for grace to hold and the
            // deadline is the only thing that moves the table at all.
            MatchModel table = Table();
            table.ResultAcked = new List<bool> { true, true };
            table.Seats = new List<MatchSeat>
            {
                new MatchSeat(EntityId.None, "Bot A", SeatOccupancy.Bot, BotProfileId.Strongest),
                new MatchSeat(EntityId.None, "Bot B", SeatOccupancy.Bot, BotProfileId.Casual),
            };

            table.Pacing.ArmDeadline(MatchDeadlineKind.Turn, At(-1), 0);

            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.True);
        }

        [Test]
        public void NoDeadlineIsNeverInForce()
        {
            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(Table()), Is.False);
        }

        // ---------------------------------------------------------------- has the game decided?

        /// <summary> A rules state in one phase, which is all the predicates below read off it. </summary>
        static MatchRulesState RulesIn(MatchPhase phase)
        {
            MatchRulesState rules = new MatchRulesState();
            rules.SetPhase(phase);
            return rules;
        }

        [Test]
        public void ADecidedGameIsRecognisedBeforeItsResultIsRecorded()
        {
            // The window this predicate exists for. The outcome record is written by the actor's post-action
            // step, which runs only after the handler that moved the game returns — so inside the handler that
            // lapsed a turn deadline and auto-played the rest of that turn, the game can be over with Result
            // still null. Reading the record alone left the escalation's own guard dead in exactly that case,
            // and what it guards is a seat covered into a game its owner cannot reclaim into.
            MatchModel table = Table();
            table.Rules = RulesIn(MatchPhase.Complete);

            Assert.That(table.Result, Is.Null, "the point of the case: the record is not written yet");
            Assert.That(MatchDeadlinePolicy.HasDecided(table), Is.True);
        }

        [Test]
        public void AGameStillBeingPlayedHasNotDecided()
        {
            MatchModel table = Table();
            table.Rules = RulesIn(MatchPhase.Playing);

            Assert.That(MatchDeadlinePolicy.HasDecided(table), Is.False);
            Assert.That(MatchDeadlinePolicy.HasDecided(Table()), Is.False, "and a table with no rules yet has decided nothing");
        }

        // ---------------------------------------------------------------- should the table play itself out?

        /// <summary> A table nobody is at: one covered seat, and one human who is gone and past their grace. </summary>
        static MatchModel DesertedTable()
        {
            MatchModel table = Table();
            table.Rules = RulesIn(MatchPhase.Playing);
            table.Seats[0].Occupancy = SeatOccupancy.HumanCoveredByBot;
            table.Seats[1].IsConnected = false;
            return table;
        }

        [Test]
        public void ATableThatHasLostEveryHumanIsPlayedOut()
        {
            // The design pillar: leaving costs exactly what staying would, so the game finishes rather than
            // the table tearing itself down. Nobody is connected and nobody is owed grace.
            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(DesertedTable()), Is.True);
        }

        [Test]
        public void ATableIsNotPlayedOutInsideALiveJoinWindow()
        {
            // The clause that was missing while the rule lived inside the actor as three loops. Every human
            // seat that has not arrived yet is disconnected and owed no grace, so seat by seat it is
            // indistinguishable from a seat whose player left — and a ranked table whose second human is
            // mid-boot would be played to a real result against two bots, with both accounts taking a rating
            // change for a game neither played.
            MatchModel table = DesertedTable();
            table.Seats[0].Occupancy   = SeatOccupancy.Human;
            table.Seats[0].IsConnected = false;
            table.Pacing.ArmJoinWindow(At(15));

            Assert.That(MatchDeadlinePolicy.JoinWindowIsInForce(table), Is.True, "the window is still waiting on both of them");
            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(table), Is.False);

            // And that stamp is the whole of the difference: with it gone, seat for seat this is a deserted
            // table. Which is why the clause has to live in the rule — nothing about the seats themselves
            // says "has not arrived yet" rather than "left".
            table.Pacing.ArmJoinWindow(null);
            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(table), Is.True);
        }

        [Test]
        public void ATableOfTwoFormationBotsHasLostNobody()
        {
            // It satisfies "nobody is here" vacuously, exactly as NeverStarted would. The matchmaker never
            // forms one, so this is a guard rather than a case — and it belongs in the rule for the same
            // reason that one does.
            MatchModel table = DesertedTable();
            table.Seats = new List<MatchSeat>
            {
                new MatchSeat(EntityId.None, "Bot A", SeatOccupancy.Bot, BotProfileId.Strongest),
                new MatchSeat(EntityId.None, "Bot B", SeatOccupancy.Bot, BotProfileId.Casual),
            };

            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(table), Is.False);
        }

        [Test]
        public void OneConnectedHumanIsEnoughToKeepPlaying()
        {
            MatchModel table = DesertedTable();
            table.Seats[1].IsConnected = true;

            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(table), Is.False);
        }

        [Test]
        public void ASeatStillOwedGraceIsNotASeatThatHasGone()
        {
            // Grace is the window in which leaving is not yet leaving, so a table inside one has lost nobody.
            MatchModel table = DesertedTable();
            table.Pacing.SetGrace(1, At(30));

            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(table), Is.False);
        }

        [Test]
        public void ADecidedOrTerminalTableIsNotPlayedOutAgain()
        {
            // Both facts, for HasDecided's own reason: a play-out that ran from inside a timer handler leaves
            // the phase Complete with the outcome record not yet written.
            MatchModel byGamePhase = DesertedTable();
            byGamePhase.Rules = RulesIn(MatchPhase.Complete);
            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(byGamePhase), Is.False);

            MatchModel byTablePhase = DesertedTable();
            byTablePhase.Phase = MatchTablePhase.Ended;
            Assert.That(MatchDeadlinePolicy.ShouldPlayOut(byTablePhase), Is.False);

        }

        // ---------------------------------------------------------------- delivery waits for the phase

        [Test]
        public void ATableInTheHeistPhaseOwesNoDeliveryYet()
        {
            // The record is not final while the phase runs: the picks are still arriving. A delivery here
            // ships a result with Heist null — or with only the picks taken so far — and both accounts
            // acknowledge it; the account-side gate is keyed on the match id alone, so the picks that land
            // afterwards can never be delivered at all. This is the predicate every delivery path asks, so it
            // is where the waiting is said.
            MatchModel table = Table();
            table.Phase  = MatchTablePhase.HeistPick;
            table.Result = Decided();

            Assert.That(MatchDeadlinePolicy.OwesDelivery(table), Is.False);

            // And the moment the phase ends it is owed, on the same record.
            table.Phase = MatchTablePhase.Ended;
            Assert.That(MatchDeadlinePolicy.OwesDelivery(table), Is.True);
        }

        [Test]
        public void AHeistPhaseKeepsTheRetryStampOutOfTheFold()
        {
            // The stamp is armed at the result, before the phase moves, so the fold has to hold it back as
            // well — otherwise the actor wakes for a delivery it must not make and spins on it.
            MatchModel table = Table();
            table.Phase  = MatchTablePhase.HeistPick;
            table.Result = Decided();

            HostPendingWork retryDue = new HostPendingWork(null, At(30));

            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, retryDue), Is.Null);

            table.Phase = MatchTablePhase.Ended;
            Assert.That(MatchDeadlinePolicy.NextAttentionAt(table, retryDue), Is.EqualTo(At(30)));
        }

        // ---------------------------------------------------------------- the Heist clock in the fold

        [Test]
        public void AHeistClockWhoseWinnerWalkedAwayFoldsToNull()
        {
            // The fold suppresses a seat-owned deadline while that seat's owner is away, whatever kind it is —
            // so a winner who closes their tab mid-pick takes the Heist clock out of the schedule with them.
            MatchModel table = Table();
            table.Phase  = MatchTablePhase.HeistPick;
            table.Result = Decided();

            table.Pacing.ArmDeadline(MatchDeadlineKind.HeistPick, At(45), 0);
            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.True, "a present winner owes the clock");

            table.Seats[0].IsConnected = false;
            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.False);
        }

        [Test]
        public void AHeistClockIsInForceAgainOnceTheWinnersSeatIsCovered()
        {
            // This is the trap, and it is why the phase resolves on departure rather than waiting.
            // Covering the seat lifts the suppression — a covered seat is bot-driven and bot-driven seats are
            // governed by the deadline (ACoveredSeatCarriesItsDeadlineAgain says the same thing for a turn) —
            // so a table that armed a Heist clock, lost its winner, and let the grace lapse would be handed
            // that stamp back on a seat nobody is sitting at. What stops it is the actor resolving the picks
            // the moment the winner goes, and arming no grace for a HeistPick winner at all.
            MatchModel table = Table();
            table.Phase  = MatchTablePhase.HeistPick;
            table.Result = Decided();

            table.Pacing.ArmDeadline(MatchDeadlineKind.HeistPick, At(-1), 0);
            table.Seats[0].IsConnected = false;

            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.False, "away: the stamp is held out");

            MatchSeatPolicy.Cover(table.Seats[0]);

            Assert.That(MatchDeadlinePolicy.DeadlineIsInForce(table), Is.True,
                "covered: the stamp is offered again, on a seat whose pick is already decided");
            Assert.That(table.Seats[0].IsConnectedHuman, Is.False,
                "and the arming predicate disagrees with the fold, which is the whole hazard");
        }

        /// <summary> A decided ranked game, so the Heist phase has a winner to name. </summary>
        static MatchOutcomeRecord Decided()
            => new MatchOutcomeRecord(
                MatchOutcome.Seat0Wins, winnerSeat: 0, finalTurn: 12, MatchEndCause.DenAtZero,
                new List<bool> { true, true }, wasRanked: true, decidedAt: Now);
    }
}
