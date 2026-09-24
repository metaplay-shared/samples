using Game.Logic;
using Metaplay.Core;
using System;
using System.Threading.Tasks;

namespace Game.Server.Match
{
    public sealed partial class MatchActor
    {
        /// <summary>
        /// The stamp a timer is armed for, so re-arming the same stamp does not add a second callback that
        /// would also dispatch. Nothing is ever cancelled.
        /// </summary>
        MetaTime? _armedFor;

        /// <summary> When an armed bot decision is due. </summary>
        MetaTime? _botDelayAt;

        /// <summary> When to re-offer a result nobody has acknowledged. </summary>
        MetaTime? _resultRetryAt;

        /// <summary> Both host-held stamps, in the shape the attention fold takes them. </summary>
        HostPendingWork HostPending => new HostPendingWork(_botDelayAt, _resultRetryAt);

        // ---------------------------------------------------------------- arming

        /// <summary>
        /// Re-arm from the model's own absolute stamps, folded together with the host's own pending work by
        /// <see cref="MatchDeadlinePolicy.NextAttentionAt"/>.
        /// </summary>
        /// <param name="noProgress">
        /// True when the dispatch that just ran left the table waiting on the same stamp. The retry is spaced
        /// out and logged rather than spinning.
        /// </param>
        void RearmFromModel(bool noProgress = false)
        {
            ReportPopulation();
            MetaTime? next = MatchDeadlinePolicy.NextAttentionAt(Model, HostPending);
            if (next == null)
            {
                _armedFor = null;
                return;
            }

            MetaTime armedFor = next.Value;
            if (_armedFor.HasValue && _armedFor.Value == armedFor)
                return;

            DateTime at = WallClockAt(armedFor) + _options.TimerPadding;

            if (noProgress)
            {
                _log.Warning("A lapsed stamp at {Stamp} did not move the table; retrying in a second rather than spinning", armedFor);
                at = DateTime.UtcNow + TimeSpan.FromSeconds(1);
            }

            _armedFor = armedFor;
            ScheduleExecuteOnActorContext(at, () => OnTimerFiredAsync(armedFor));
        }

        /// <summary>
        /// The wall-clock instant a stamp on the model's clock falls due. On this actor the model's clock is
        /// <see cref="MetaTime.Now"/> floored to the tick — the SDK runs the pending ticks before every action
        /// — so a timer set here fires once the model has reached the stamp, and the lapse the callback stages
        /// executes at or after it. <see cref="MetaTime.Now"/> carries the debug time offset and the scheduler
        /// does not.
        /// </summary>
        static DateTime WallClockAt(MetaTime stamp) => stamp.ToDateTime() - MetaTime.DebugTimeOffset.ToTimeSpan();

        /// <summary>
        /// A scheduled callback fired. It no-ops if the table has since moved to a different stamp, so a stale
        /// callback cannot defeat a re-armed deadline (<c>Docs/protocol.md</c>, "Timers are never
        /// cancelled").
        /// </summary>
        async Task OnTimerFiredAsync(MetaTime armedFor)
        {
            MetaTime? current = MatchDeadlinePolicy.NextAttentionAt(Model, HostPending);
            if (!current.HasValue || current.Value != armedFor)
                return;

            _armedFor = null;
            await DispatchAttentionAsync(armedFor);

            MetaTime? after = MatchDeadlinePolicy.NextAttentionAt(Model, HostPending);
            RearmFromModel(noProgress: after.HasValue && after.Value == armedFor);
        }

        /// <summary> What the lapsed stamp <em>was</em>. Only a result delivery awaits anything. </summary>
        Task DispatchAttentionAsync(MetaTime stamp)
        {
            MatchPacing pacing = Model.Pacing;

            if (pacing.JoinWindowEndsAt.HasValue && pacing.JoinWindowEndsAt.Value == stamp
                && MatchDeadlinePolicy.JoinWindowIsInForce(Model))
            {
                OnJoinWindowExpired();
                return Task.CompletedTask;
            }

            if (pacing.DeadlineAt.HasValue && pacing.DeadlineAt.Value == stamp)
            {
                OnDeadlineLapsed(pacing.DeadlineKind);
                return Task.CompletedTask;
            }

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                MetaTime? grace = pacing.Grace(seat);
                if (grace.HasValue && grace.Value == stamp)
                {
                    OnGraceLapsed(seat);
                    return Task.CompletedTask;
                }
            }

            if (_botDelayAt.HasValue && _botDelayAt.Value == stamp)
            {
                RunArmedBotAction();
                return Task.CompletedTask;
            }

            if (_resultRetryAt.HasValue && _resultRetryAt.Value == stamp)
                return DeliverResultsAsync();

            _log.Debug("A timer fired for {Stamp} with nothing left to answer it", stamp);
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------- deadlines

        /// <summary>
        /// Each lapse becomes one ordinary rule action; the actor routes on pure queries both sides can ask.
        /// The kind is read off the model when the stamp matches, so a deadline replaced at the same stamp
        /// (a turn timer re-armed as a peek's choice deadline) is routed by the kind it has now.
        /// </summary>
        void OnDeadlineLapsed(MatchDeadlineKind kind)
        {
            switch (kind)
            {
                case MatchDeadlineKind.Mulligan:
                    // A seat that let the deadline lapse keeps the hand it was dealt.
                    ExecuteMatchAction(new MatchMulliganResolve(), refusalIsBug: false);
                    break;

                case MatchDeadlineKind.Turn:
                    OnTurnDeadlineLapsed(Model.Rules.SeatOnTurn);
                    break;

                case MatchDeadlineKind.EffectChoice:
                    // The bot policy's own deterministic keep rule, so the pause can never stall a table.
                    SubmitDefaultChoice();
                    break;

                case MatchDeadlineKind.HeistPick:
                    // The Heist is the table's own phase, answered by the table's own actions.
                    OnHeistDeadlineLapsed();
                    break;
            }

            AfterActions();
        }

        /// <summary>
        /// A connected seat ran out of time: it takes a <b>strike</b>, the strongest profile plays out the
        /// <b>rest of its turn</b>, and at the configured count a bot <b>covers</b> the seat
        /// (<c>Docs/match.md</c>, "When players stop playing"). Strike, then play-out, then cover, so the
        /// bot takes over from the seat's next turn, and a play-out that finishes the game leaves nothing to
        /// cover.
        /// </summary>
        void OnTurnDeadlineLapsed(int seat)
        {
            // A disconnected player must not take strikes for turns they could not take; grace holds the seat.
            if (!MatchSeatPolicy.TurnDeadlineGoverns(Model.Seats[seat]))
            {
                _log.Info("Seat {Seat}'s turn deadline lapsed while its player was away; grace has the seat, not the deadline", seat);
                return;
            }

            // A seat that has already acted this turn may draw on its reserve; one that sat still may not.
            if (TurnRules.CanExtendFromReserve(Model, seat))
            {
                ExecuteMatchAction(new MatchExtendReserve(seat), refusalIsBug: false);
                _log.Debug("Seat {Seat} drew on its reserve; {Remaining} left", seat, TurnRules.ReserveRemaining(Model, seat));
                return;
            }

            if (MatchSeatPolicy.LapseCountsAsAStrike(Model.Seats[seat], _options.StrikesBeforeCover))
            {
                EditSeat(seat, s => s.Strikes++);

                _log.Info("Seat {Seat} let its turn deadline lapse: strike {Strikes} of {Limit}",
                    seat, Model.Seats[seat].Strikes, _options.StrikesBeforeCover);
            }

            PlayOutTurn();

            // HasDecided rather than Model.Result: the play-out can finish the game, and the record is written
            // after this returns. The table is not played out even if both seats are now bot-driven: this
            // seat's owner is connected, and may ask for the seat back by pressing anything.
            if (MatchSeatPolicy.ShouldCoverAfterStrike(Model.Seats[seat], _options.StrikesBeforeCover, MatchDeadlinePolicy.HasDecided(Model)))
                CoverSeat(seat);
        }

        // ---------------------------------------------------------------- the join window

        /// <summary>
        /// The join window closed. A human seat that never subscribed is covered by a bot and the game goes on;
        /// if no human ever arrived, the table is abandoned and both pointers are cleared through delivery.
        /// It does not play the table out: a seat covered here has not arrived yet, which is not a seat whose
        /// player left (<see cref="MatchDeadlinePolicy.ShouldPlayOut"/>).
        /// </summary>
        void OnJoinWindowExpired()
        {
            if (MatchDeadlinePolicy.NeverStarted(Model))
            {
                _log.Info("The join window closed with nobody at the table; abandoning it");
                EnterAbandonedPhase();
                AfterActions();
                return;
            }

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                if (!MatchDeadlinePolicy.SeatHasNotArrived(Model, seat))
                    continue;

                _log.Info("Seat {Seat} never arrived inside the join window; a bot covers it", seat);
                CoverSeat(seat);
            }

            AfterActions();
        }

        // ---------------------------------------------------------------- grace and cover

        /// <summary>
        /// Hold a seat whose player has gone. A seat already covered is not put back on grace: its owner takes
        /// it back by coming back.
        /// </summary>
        void ArmGraceFor(int seat)
        {
            if (Model.Seats[seat].Occupancy != SeatOccupancy.Human)
                return;

            ExecuteMatchAction(new MatchSetGrace(seat, MetaDuration.FromTimeSpan(_options.DisconnectGrace)));

            _log.Info("Seat {Seat} is on {Grace} of grace", seat, _options.DisconnectGrace);
        }

        /// <summary>
        /// Nobody came back. The seat is covered, and if that was the last connected human the table plays
        /// itself out.
        /// </summary>
        void OnGraceLapsed(int seat)
        {
            ExecuteMatchAction(new MatchSetGrace(seat, null));
            CoverSeat(seat);
            MaybePlayOutDesertedTable();
        }

        /// <summary>
        /// A bot takes over a human's seat, keeping the human's identity. The stakes do not move: they were
        /// fixed at formation.
        /// </summary>
        void CoverSeat(int seat)
        {
            if (!MatchSeatPolicy.CanCover(Model.Seats[seat]))
                return;

            EditSeat(seat, MatchSeatPolicy.Cover);

            // A covered seat is governed by the turn deadline again, so the held stamp is offered.
            RearmFromModel();

            _log.Info("Seat {Seat} is covered by a bot", seat);
        }

        /// <summary>
        /// A covered seat's owner is here: the seat is marked to be handed back at the next turn boundary and its
        /// strikes are cleared. The bot keeps the turn it is playing, so the owner never races it.
        /// </summary>
        void RequestReclaimIfCovered(int seat)
        {
            bool      decided = MatchDeadlinePolicy.HasDecided(Model);
            MatchSeat probe   = CopyOfSeat(seat);

            if (!MatchSeatPolicy.RequestReclaim(probe, decided))
                return;

            EditSeat(seat, s => MatchSeatPolicy.RequestReclaim(s, decided));

            _log.Info("Seat {Seat}'s owner is back; the seat is theirs from the next turn", seat);
        }

        /// <summary> A covered seat's owner went again before the boundary, so the seat stays covered. </summary>
        void CancelReclaimIfPending(int seat)
        {
            if (!Model.Seats[seat].ReclaimPending)
                return;

            EditSeat(seat, MatchSeatPolicy.CancelReclaim);
        }

        /// <summary> The turn the last boundary check ran on. Turn 0 is the mulligan, so turn 1's start is a boundary. </summary>
        int _reclaimCheckedTurn;

        /// <summary>
        /// A turn began — whichever seat's — so every seat whose owner asked for it back has it, before any bot
        /// is armed for the new turn.
        /// </summary>
        void HandBackSeatsAtTurnBoundary()
        {
            if (Model.Rules.Turn == _reclaimCheckedTurn)
                return;

            _reclaimCheckedTurn = Model.Rules.Turn;

            bool decided = MatchDeadlinePolicy.HasDecided(Model);
            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                if (!MatchSeatPolicy.ReclaimsAtTurnBoundary(Model.Seats[seat], decided))
                    continue;

                EditSeat(seat, s => MatchSeatPolicy.Reclaim(s, decided));
                _log.Info("Seat {Seat} was handed back to its owner at the start of turn {Turn}", seat, Model.Rules.Turn);
            }
        }

        /// <summary>
        /// The seat did something, so its run of lapsed deadlines is over. Publishes nothing for a seat with
        /// no strikes, which is every intent in an ordinary game.
        /// </summary>
        void ForgiveStrikes(int seat)
        {
            if (Model.Seats[seat].Strikes == 0)
                return;

            EditSeat(seat, MatchSeatPolicy.ClearStrikes);

            _log.Debug("Seat {Seat} acted, so its strike count is back to zero", seat);
        }

        /// <summary>
        /// A table that loses everyone is <b>played out, not abandoned</b>: leaving costs exactly what staying
        /// would (<see cref="MatchDeadlinePolicy.ShouldPlayOut"/>, <see cref="PlayOutRemainder"/>).
        /// </summary>
        void MaybePlayOutDesertedTable()
        {
            if (!MatchDeadlinePolicy.ShouldPlayOut(Model))
                return;

            _log.Info("The table has lost every human; playing the rest of the game out to a real result");
            PlayOutRemainder();
            AfterActions();
        }
    }
}
