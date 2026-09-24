using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Server.Match
{
    public sealed partial class MatchActor
    {
        /// <summary>
        /// The Heist phase: the winner picks and the table applies. Every rule is
        /// <see cref="MatchHeistPolicy"/>'s; what is here is routing.
        /// </summary>
        MatchHeistPosition HeistPosition()
        {
            int  winner  = Model.Result != null ? Model.Result.WinnerSeat : MatchSeats.None;
            // The pick clock is armed only for a person who is here; a covered winner's pick is the bot's default.
            bool present = MatchSeats.IsValid(winner) && Model.Seats[winner].IsConnectedHuman;

            return MatchHeistPosition.Of(Model, present);
        }

        /// <summary>
        /// Whether this finished table owes a pick (<c>Docs/match.md</c>, "The Heist phase"). A draw, an
        /// unranked game, a favourite's win, a shielded match and an empty menu all go straight to
        /// <see cref="MatchTablePhase.Ended"/>.
        /// </summary>
        bool HeistPickIsOwed()
        {
            // Needs the outcome record, which says who won; asked from the post-action step after RecordResult.
            if (!MatchDeadlinePolicy.HasDecided(Model) || Model.Result == null || Model.HeistEligibility == null)
                return false;

            int winner = Model.Result.WinnerSeat;
            if (!MatchSeats.IsValid(winner))
                return false;

            return MatchHeistPolicy.PhaseRuns(Model.Stakes, Model.Result, Model.HeistEligibility[MatchSeats.Other(winner)]);
        }

        /// <summary>
        /// The match does not end at lethal. The client holds the finish on screen before it shows the Heist.
        /// A winner who is not at the table has every pick defaulted rather than clocked.
        /// </summary>
        void EnterHeistPick()
        {
            ExecuteMatchAction(new MatchSetPhase(MatchTablePhase.HeistPick));

            // The result is not final during the phase, so the delivery retry armed at RecordResult comes off.
            EnsureResultRetryArmed();

            MatchHeistPosition position = HeistPosition();

            _log.Info("The Heist: seat {Seat} owes {Owed} pick(s) from {Menu} eligible card(s)",
                position.WinnerSeat, position.PicksOwed, position.Eligible.Count);

            if (position.WinnerIsPresent)
                ArmHeistPickDeadline();
            else
                ApplyHeistVerdict(MatchHeistPolicy.Resolve(position, MatchSeats.None, picked: null, SharedConfig));
        }

        /// <summary>
        /// One pick's clock, per pick rather than per phase, at exactly <c>Match:HeistPickDeadline</c> with no
        /// presentation allowance: there is no board to catch up to (<c>Docs/match.md</c>, "Timing
        /// budget").
        /// </summary>
        void ArmHeistPickDeadline()
        {
            int winner = Model.Result.WinnerSeat;

            ExecuteMatchAction(new MatchArmHeistDeadline(
                winner, MetaDuration.FromTimeSpan(_options.HeistPickDeadline)));

            _log.Info("Seat {Seat} has {Deadline} to pick", winner, _options.HeistPickDeadline);
        }

        /// <summary>
        /// Take the pick clock off the table. A stamp left on a terminal table would stay in the attention fold
        /// and re-arm once a second for as long as the delivery window holds the actor awake.
        /// </summary>
        void ClearHeistPickDeadline()
        {
            int seat = Model.Pacing.DeadlineSeat;

            if (Model.Pacing.DeadlineAt.HasValue && MatchSeats.IsValid(seat))
                ExecuteMatchAction(new MatchArmHeistDeadline(seat, null));
        }

        /// <summary> The picks are in; the table stops and the result goes out to both accounts. </summary>
        void EndHeistPhase()
        {
            ClearHeistPickDeadline();
            EnterTerminalPhase();
        }

        /// <summary>
        /// The winner's pick. A pick off the menu is refused explicitly rather than dropped
        /// (<c>Docs/client.md</c>, "a refusal is an explicit message, not a timeout").
        /// </summary>
        void OnHeistPick(EntitySubscriber session, int requestId, int seat, HeistPickIntent intent)
        {
            if (intent.Card == null)
            {
                // Resolve reads a null card as the auto-default, which a malformed message must not reach.
                Refuse(session, requestId, MatchRefusalCode.NotEligible);
                return;
            }

            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(HeistPosition(), seat, intent.Card, SharedConfig);

            if (verdict.Step == MatchHeistStep.Refuse)
            {
                _log.Debug("Seat {Seat}'s pick of {Card} was refused {Reason}", seat, intent.Card, verdict.Refusal);
                Refuse(session, requestId, verdict.Refusal);
                return;
            }

            ApplyHeistVerdict(verdict);
            AfterActions();
        }

        /// <summary>
        /// The pick clock ran out. One slot takes the strongest profile's default and, while the winner is
        /// still there, the next slot is clocked again.
        /// </summary>
        void OnHeistDeadlineLapsed()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(HeistPosition(), MatchSeats.None, picked: null, SharedConfig);

            if (verdict.Step == MatchHeistStep.Nothing)
            {
                // A stamp that outlived its phase. Clearing it is what keeps the fold from offering it again.
                ClearHeistPickDeadline();
                return;
            }

            ApplyHeistVerdict(verdict);
        }

        /// <summary>
        /// A seat's owner has gone mid-Heist. Returns whether the phase handled the departure, in which case
        /// the caller arms no grace and covers nothing: grace would hand a covered winner its pick clock back,
        /// and covering a decided game marks a plaque its owner cannot reclaim.
        /// </summary>
        bool ResolveHeistOnSeatGone(int seat)
        {
            if (Model.Phase != MatchTablePhase.HeistPick)
                return false;

            // The loser owes nothing here at all: whatever they protected, they protected before they queued.
            if (Model.Result == null || seat != Model.Result.WinnerSeat)
                return true;

            _log.Info("Seat {Seat} is gone mid-Heist; the picks it owes are defaulted rather than clocked", seat);

            // Absent by definition: a leave arrives from a session that is still marked connected.
            MatchHeistPosition position = MatchHeistPosition.Of(Model, winnerIsPresent: false);
            ApplyHeistVerdict(MatchHeistPolicy.Resolve(position, MatchSeats.None, picked: null, SharedConfig));

            return true;
        }

        // ---------------------------------------------------------------- publishing a step

        /// <summary> Put one verdict on the timeline: the picks it takes, and then the clock or the finish. </summary>
        void ApplyHeistVerdict(MatchHeistVerdict verdict)
        {
            if (verdict.Step == MatchHeistStep.Nothing || verdict.Step == MatchHeistStep.Refuse)
                return;

            if (verdict.Picks.Count > 0)
                RecordHeistPicks(verdict.Picks, verdict.AnyAutoDefaulted);

            if (verdict.Step == MatchHeistStep.TakeAndArm)
                ArmHeistPickDeadline();
            else
                EndHeistPhase();
        }

        /// <summary>
        /// Fold the picks into the outcome record by re-publishing it through <see cref="MatchSetResult"/>:
        /// the record is public and checksummed, so it is never edited in place. The payload is fresh objects,
        /// not the lists the model already holds.
        /// </summary>
        void RecordHeistPicks(List<CardId> picks, bool anyAutoDefaulted)
        {
            MatchOutcomeRecord current = Model.Result;

            List<CardId> all = new List<CardId>();
            if (current.Heist != null && current.Heist.Picks != null)
                all.AddRange(current.Heist.Picks);
            all.AddRange(picks);

            MatchOutcomeRecord record = new MatchOutcomeRecord(
                current.Outcome,
                current.WinnerSeat,
                current.FinalTurn,
                current.Cause,
                new List<bool>(current.WasPresentAtFinish),
                current.WasRanked,
                current.DecidedAt)
            {
                Heist = new HeistResult(all, (current.Heist != null && current.Heist.AnyPickAutoDefaulted) || anyAutoDefaulted),
            };

            ExecuteMatchAction(new MatchSetResult(record, CopyOfHeistEligibility()));

            _log.Info("Seat {Seat} takes {Picks}{Defaulted}", current.WinnerSeat,
                string.Join(", ", picks), anyAutoDefaulted ? " (defaulted)" : "");
        }

        /// <summary> A copy of the eligibility lists, safe to put on a timeline as a value. </summary>
        List<List<HeistEligibleCard>> CopyOfHeistEligibility()
        {
            List<List<HeistEligibleCard>> copy = new List<List<HeistEligibleCard>>(MatchSeats.Count);

            for (int seat = 0; seat < MatchSeats.Count; seat++)
                copy.Add(new List<HeistEligibleCard>(Model.HeistEligibility[seat]));

            return copy;
        }
    }
}
