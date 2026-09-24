using Game.Logic;
using Game.Server.Matchmaking;
using Metaplay.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    public sealed partial class MatchActor
    {
        /// <summary>
        /// The game decided. The outcome and the Heist eligibility lists go on the model at once, because a
        /// non-null result is what bars a seat reclaim.
        /// </summary>
        void RecordResult()
        {
            MatchResult rulesResult = Model.Rules.Result;

            List<bool> presentAtFinish = new List<bool>(MatchSeats.Count);
            foreach (MatchSeat seat in Model.Seats)
                presentAtFinish.Add(seat.IsConnectedHuman);

            MatchOutcomeRecord record = new MatchOutcomeRecord(
                rulesResult.Outcome,
                rulesResult.WinnerSeat,
                rulesResult.FinalTurn,
                rulesResult.Cause,
                presentAtFinish,
                Model.Stakes.IsRanked,
                MetaTime.Now);

            ExecuteMatchAction(new MatchSetResult(record, MatchHeistPolicy.Eligibility(rulesResult, Model.Stakes)));

            // Armed the moment the result exists: the retry stamp is what keeps delivery in the attention fold.
            EnsureResultRetryArmed();
            TakeDeliveryWakelock();

            _log.Info("The game is over: {Outcome} on turn {Turn} ({Cause})", rulesResult.Outcome, rulesResult.FinalTurn, rulesResult.Cause);
        }

        /// <summary>
        /// The result-delivery window. A wakelock keeps the actor alive whether or not anybody is subscribed:
        /// both clients can be gone when a deserted table plays itself out, and a table evicted still owing a
        /// delivery loses it (<see cref="MatchOptions.ResultDeliveryWindow"/>).
        /// </summary>
        Wakelock _deliveryWakelock;

        void TakeDeliveryWakelock()
        {
            // CreateWakelock throws once a shutdown is enqueued. Not guarded on the field: an expired wakelock
            // disposes itself without nulling it, and the terminal phase deliberately takes a fresh window.
            if (IsShutdownEnqueued)
                return;

            _deliveryWakelock = CreateWakelock(_options.ResultDeliveryWindow);
        }

        /// <summary> Hand the actor back to its ordinary linger. </summary>
        void ReleaseDeliveryWakelock()
        {
            _deliveryWakelock?.Dispose();
            _deliveryWakelock = null;
        }

        /// <summary>
        /// The table stops, straight from the result or at the end of the Heist. The record is final here, so
        /// the retry stamp and a fresh delivery window are taken again (<see cref="MatchDeadlinePolicy.OwesDelivery"/>).
        /// </summary>
        void EnterTerminalPhase()
        {
            ExecuteMatchAction(new MatchSetPhase(MatchTablePhase.Ended));

            EnsureResultRetryArmed();
            TakeDeliveryWakelock();

            EnqueueOnActorContext(DeliverResultsAsync);
        }

        /// <summary>
        /// The result-less terminal: a table whose humans never arrived inside the join window. There is no
        /// result, but both accounts' pointers still have to be cleared, so it arms delivery itself.
        /// </summary>
        void EnterAbandonedPhase()
        {
            ExecuteMatchAction(new MatchSetPhase(MatchTablePhase.Abandoned));

            EnsureResultRetryArmed();
            TakeDeliveryWakelock();
            EnqueueOnActorContext(DeliverResultsAsync);
        }

        /// <summary>
        /// Tell both accounts what to apply, <b>loser first, stopping at the first failure</b>: a rank lost but
        /// not gained is survivable, one gained but not lost is not (<c>Docs/match.md</c>, "Two
        /// mutations, deliberately not one transaction"). A match lives only as long as this actor, so the
        /// window is a wakelock, the attempts are counted, and giving up is an <c>Error</c>.
        /// </summary>
        async Task DeliverResultsAsync()
        {
            if (!MatchDeadlinePolicy.OwesDelivery(Model))
            {
                _resultRetryAt = null;
                ReleaseDeliveryWakelock();
                return;
            }

            _deliveryAttempts++;

            // Armed before the asks go out, so a retry is already scheduled whatever the asks do.
            _resultRetryAt = MetaTime.Now + MetaDuration.FromTimeSpan(_options.ResultRetryInterval);

            foreach (int seat in DeliveryOrder(Model.Result))
            {
                if (!await TryDeliverResultAsync(seat))
                {
                    _log.Info("Seat {Seat} could not be told the result, so the other seat is not told either", seat);
                    break;
                }
            }

            if (Model.AllSeatsAcked)
            {
                _resultRetryAt = null;
                ReleaseDeliveryWakelock();
            }
            else if (_deliveryAttempts >= _options.ResultDeliveryAttempts)
                GiveUpOnDelivery();

            RearmFromModel();
        }

        /// <summary> The seats in the order they are told: the loser first, and seat order for a draw. </summary>
        internal static int[] DeliveryOrder(MatchOutcomeRecord result)
        {
            int winner = result != null && !result.IsDraw && MatchSeats.IsValid(result.WinnerSeat) ? result.WinnerSeat : MatchSeats.None;
            return winner == MatchSeats.None ? new[] { 0, 1 } : new[] { MatchSeats.Other(winner), winner };
        }

        /// <summary> How many times the whole delivery round has been attempted. </summary>
        int _deliveryAttempts;

        /// <summary>
        /// Tell one account what to apply. False only when the ask failed; a seat with nothing owed is true.
        /// </summary>
        async Task<bool> TryDeliverResultAsync(int seat)
        {
            if (!MatchDeadlinePolicy.OwesDelivery(Model) || Model.ResultAcked[seat])
                return true;

            EntityId playerId = Model.Seats[seat].PlayerId;
            if (!playerId.IsValid)
                return true;

            try
            {
                await EntityAskAsync(playerId, new InternalMatchDeliverResultRequest(_entityId, seat, TerminalKind, Model.Result, RatingDeltaFor(seat)));

                ExecuteMatchAction(new MatchSetResultAck(seat));
                _log.Info("Seat {Seat} ({PlayerId}) has recorded the result", seat, playerId);
                return true;
            }
            catch (Exception ex)
            {
                _log.Warning("Offering the result to seat {Seat} ({PlayerId}) failed ({Error}); it will be offered again", seat, playerId, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// What one seat's rating moves by, from the two ratings frozen at formation. Gated on
        /// <see cref="MatchStakes.IsRanked"/> rather than the tier: shielded and fallback-bot matches move
        /// rating even though they move no rank.
        /// </summary>
        int RatingDeltaFor(int seat)
        {
            if (Model.Result == null || !Model.Stakes.IsRanked)
                return 0;

            return RatingPolicy.Delta(
                _seatRatings[seat],
                _seatRatings[MatchSeats.Other(seat)],
                Model.Result.OutcomeForSeat(seat),
                SharedConfig.Global.RatingKFactor);
        }

        /// <summary>
        /// The retry budget is spent and nothing else will try: a rank transfer was lost, so it is an
        /// <c>Error</c> naming the match, the seat and the account.
        /// </summary>
        void GiveUpOnDelivery()
        {
            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                if (Model.Seats[seat].PlayerId.IsValid && !Model.ResultAcked[seat])
                {
                    _log.Error("Gave up delivering the result of {MatchId} to seat {Seat} ({PlayerId}) after "
                             + "{Attempts} attempts; nothing else will try",
                               _entityId, seat, Model.Seats[seat].PlayerId, _deliveryAttempts);
                }
            }

            _resultRetryAt = null;
            ReleaseDeliveryWakelock();
        }

        /// <summary>
        /// Keep a retry stamp armed for as long as a result is outstanding, and drop it the moment nothing is.
        /// This stamp is the only thing keeping an outstanding result in the attention fold.
        /// </summary>
        void EnsureResultRetryArmed()
        {
            if (MatchDeadlinePolicy.OwesDelivery(Model))
                _resultRetryAt ??= MetaTime.Now + MetaDuration.FromTimeSpan(_options.ResultRetryInterval);
            else
                _resultRetryAt = null;
        }

        /// <summary>
        /// Which errand a terminal table owes its accounts: a result to apply, or, for an abandoned table, only
        /// a pointer to clear. One ack array and one retry stamp serve both.
        /// </summary>
        MatchTerminalKind TerminalKind => Model.Phase == MatchTablePhase.Abandoned ? MatchTerminalKind.Abandoned : MatchTerminalKind.Result;
    }
}
