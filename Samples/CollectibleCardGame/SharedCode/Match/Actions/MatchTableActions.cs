using Metaplay.Core;
using Metaplay.Core.Model;
using System.Collections.Generic;

namespace Game.Logic
{
    // The table's own state, which is not the game: who is seated, what phase the table is in, the outcome
    // as the accounts are told it, the acknowledgements, the grace stamps, the Heist-pick clock and the clock
    // push a returning seat is owed. None of these reads MatchModel.Secret and none of them is a rule; the
    // rules are in MatchRuleActions.cs.

    /// <summary> Who is seated, whether they are here, and whether a bot is covering for them. </summary>
    [ModelAction(ActionCodes.MatchSetSeats)]
    public class MatchSetSeats : MatchHostAction
    {
        public List<MatchSeat> Seats { get; private set; }

        public MatchSetSeats() { }

        public MatchSetSeats(List<MatchSeat> seats)
        {
            Seats = seats;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (Seats == null || Seats.Count != MatchSeats.Count)
                return MatchIntentResults.InvalidHostAction;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Seats = Seats;
            match.ClientListener.OnSeatsChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The table moved to a new phase.
    /// </summary>
    [ModelAction(ActionCodes.MatchSetPhase)]
    public class MatchSetPhase : MatchHostAction
    {
        public MatchTablePhase Phase { get; private set; }

        public MatchSetPhase() { }

        public MatchSetPhase(MatchTablePhase phase)
        {
            Phase = phase;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Phase = Phase;
            match.ClientListener.OnPhaseChanged(Phase);
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The outcome, with the Heist eligibility lists that have to outlive the board. Written at the moment
    /// the game decides, before any end-of-match beat or Heist phase — <c>Result != null</c> is what bars a
    /// seat reclaim, so it must be true as early as the game is actually over.
    /// </summary>
    [ModelAction(ActionCodes.MatchSetResult)]
    public class MatchSetResult : MatchHostAction
    {
        public MatchOutcomeRecord            Result           { get; private set; }
        public List<List<HeistEligibleCard>> HeistEligibility { get; private set; }

        public MatchSetResult() { }

        public MatchSetResult(MatchOutcomeRecord result, List<List<HeistEligibleCard>> heistEligibility)
        {
            Result           = result;
            HeistEligibility = heistEligibility;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (Result == null || HeistEligibility == null || HeistEligibility.Count != MatchSeats.Count)
                return MatchIntentResults.InvalidHostAction;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Result           = Result;
            match.HeistEligibility = HeistEligibility;
            match.ClientListener.OnResult(Result);
            return MetaActionResult.Success;
        }
    }

    /// <summary> One seat's account has folded the result in. The answer to the ask sets this, never the send. </summary>
    [ModelAction(ActionCodes.MatchSetResultAck)]
    public class MatchSetResultAck : MatchHostAction
    {
        public int Seat { get; private set; }

        public MatchSetResultAck() { }

        public MatchSetResultAck(int seat)
        {
            Seat = seat;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (!MatchSeats.IsValid(Seat) || match.ResultAcked == null)
                return MatchIntentResults.InvalidHostAction;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.ResultAcked[Seat] = true;
            match.ClientListener.OnResultAcked(Seat);
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// Put one seat on grace for a duration, or take it off grace with null. The grace timers are the actor's
    /// own concern — nothing in the rules knows what a disconnect is — but they are <em>state</em>, so they
    /// move through an action like everything else on the model. The stamp is the model's clock plus the
    /// duration, so a follower reaches the same one.
    /// </summary>
    [ModelAction(ActionCodes.MatchSetGrace)]
    public class MatchSetGrace : MatchHostAction
    {
        public int           Seat { get; private set; }
        public MetaDuration? For  { get; private set; }

        public MatchSetGrace() { }

        public MatchSetGrace(int seat, MetaDuration? duration)
        {
            Seat = seat;
            For  = duration;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (!MatchSeats.IsValid(Seat))
                return MatchIntentResults.InvalidHostAction;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Pacing.SetGrace(Seat, For.HasValue ? match.CurrentTime + For.Value : (MetaTime?)null);
            match.ClientListener.OnPacingChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// The winner's Heist-pick clock, or null to take it off. Modelled on <see cref="MatchSetGrace"/> and for
    /// the same reason: <see cref="MatchPacing"/> is public and therefore checksummed, so the stamp reaches a
    /// follower by that follower running this body.
    /// <para>
    /// <b>The duration rides the payload</b> rather than living on <see cref="MatchModel.Timings"/>, which is
    /// where the rule actions read theirs: the Heist phase is the table's rather than the game's.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchArmHeistDeadline)]
    public class MatchArmHeistDeadline : MatchHostAction
    {
        /// <summary> The winner's seat: the one seat that owes a pick. </summary>
        public int           Seat { get; private set; }
        /// <summary> How long the pick has, from the model's clock, or null to clear the deadline. </summary>
        public MetaDuration? For  { get; private set; }

        public MatchArmHeistDeadline() { }

        public MatchArmHeistDeadline(int seat, MetaDuration? duration)
        {
            Seat = seat;
            For  = duration;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (!MatchSeats.IsValid(Seat))
                return MatchIntentResults.InvalidHostAction;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            if (For.HasValue)
                match.Pacing.ArmDeadline(MatchDeadlineKind.HeistPick, match.CurrentTime + For.Value, Seat);
            else
                match.Pacing.ClearDeadline();

            match.ClientListener.OnPacingChanged();
            return MetaActionResult.Success;
        }
    }

    /// <summary>
    /// A seat could not reach the table for a while, so nothing that was waiting on a clock spent that time.
    /// Every absolute stamp moves forward by as much and nothing else changes.
    /// <para>
    /// <b>A seat that could not reach the table cannot have been deciding.</b> A player's client drops, their
    /// deadline is held out of the attention fold while grace has the seat
    /// (<see cref="MatchDeadlinePolicy.DeadlineIsInForce"/>), and offering that stamp again unchanged when
    /// they come back would hand them a deadline that had already run out in their absence. So the clocks
    /// are pushed at the moment they return — <c>MatchActor.GiveBackTimeSpentAway</c> is the one caller.
    /// </para>
    /// <para>
    /// It is an <b>action</b> rather than a direct write because <see cref="MatchPacing"/> is public and
    /// therefore checksummed: a follower learns the pushed stamps by running the same body.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.MatchPushClocks)]
    public class MatchPushClocks : MatchHostAction
    {
        public MetaDuration By { get; private set; }

        public MatchPushClocks() { }

        public MatchPushClocks(MetaDuration by)
        {
            By = by;
        }

        public override MatchIntentResult ServerPrepare(MatchModel match)
        {
            if (By <= MetaDuration.Zero)
                return MatchIntentResults.InvalidHostAction;
            return MatchIntentResults.Success;
        }

        protected override MetaActionResult Execute(MatchModel match)
        {
            match.Pacing.PushAllClocks(By);
            match.ClientListener.OnPacingChanged();
            return MetaActionResult.Success;
        }
    }
}
