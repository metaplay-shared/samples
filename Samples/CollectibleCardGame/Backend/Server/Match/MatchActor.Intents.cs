using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Server;
using System.Collections.Generic;

namespace Game.Server.Match
{
    public sealed partial class MatchActor
    {
        /// <summary>
        /// One rules intent from one seat, in a fixed order:
        /// <list type="number">
        /// <item><b>Resolve the seat.</b> A sender who is not a seat is refused.</item>
        /// <item><b>Mark presence</b> before anything can refuse: a refused intent still clears the strikes and,
        /// on a covered seat, asks for the seat back from the next turn boundary.</item>
        /// <item><b>Refuse a covered seat's intent</b>: the bot has the seat until that boundary, so the owner
        /// never races it mid-turn.</item>
        /// <item><b>Prepare the rule action</b>, whose gate judges it against the current state, so the loser
        /// of a race is refused and nothing is played twice.</item>
        /// <item><b>Refuse explicitly</b>, to the submitter only, or the client's hand stays locked.</item>
        /// </list>
        /// </summary>
        [PubSubMessageHandler]
        void HandleMatchIntentMessage(EntitySubscriber session, MatchIntentMessage message)
        {
            int seat = ResolveSeat(session);
            if (seat < 0)
            {
                Refuse(session, message.RequestId, MatchRefusalCode.NotASeat);
                return;
            }

            MarkSeatPresent(seat);

            if (message.Intent == null)
            {
                Refuse(session, message.RequestId, MatchRefusalCode.NotASeat);
                return;
            }

            if (MatchSeatPolicy.RefusesIntents(Model.Seats[seat], MatchDeadlinePolicy.HasDecided(Model)))
            {
                _log.Debug("Seat {Seat}'s {Intent} was refused: a bot has the seat until the next turn", seat, message.Intent.GetType().ToGenericTypeString());
                Refuse(session, message.RequestId, MatchRefusalCode.SeatCovered);
                RearmFromModel();
                return;
            }

            MatchIntentResult result = SubmitIntent(seat, message.Intent);
            AfterActions();

            if (!result.IsSuccess)
            {
                MatchRefusalCode code = MatchRefusals.FromEngine(result);

                if (MatchRefusals.IsBugSignal(code))
                {
                    // The client runs the same legality check, so this is a bug signal.
                    _log.Warning("Seat {Seat}'s {Intent} was refused {Result}, which the client's own legality check should have prevented",
                        seat, message.Intent.GetType().ToGenericTypeString(), result);
                }
                else
                    _log.Debug("Seat {Seat}'s {Intent} was refused {Result}", seat, message.Intent.GetType().ToGenericTypeString(), result);

                Refuse(session, message.RequestId, code);
            }
        }

        /// <summary> One seat intent: leave, or one of the two that are declared and refused. </summary>
        [PubSubMessageHandler]
        void HandleMatchSeatIntentMessage(EntitySubscriber session, MatchSeatIntentMessage message)
        {
            int seat = ResolveSeat(session);
            if (seat < 0)
            {
                Refuse(session, message.RequestId, MatchRefusalCode.NotASeat);
                return;
            }

            MarkSeatPresent(seat);

            switch (message.Intent)
            {
                case LeaveMatchIntent _:
                    OnSeatLeft(seat);
                    return;

                case DebugWinMatchIntent _:
                    bool isDeveloper = GlobalStateProxyActor.ActiveDevelopers.Get()?.IsPlayerDeveloper(Model.Seats[seat].PlayerId) == true;
                    if (!isDeveloper || Model.Phase != MatchTablePhase.Playing || Model.Rules.Phase == MatchPhase.Complete || Model.Result != null)
                    {
                        Refuse(session, message.RequestId, MatchRefusalCode.NotAvailableYet);
                        return;
                    }
                    ExecuteMatchAction(new MatchDebugWin(seat));
                    AfterActions();
                    return;

                case HeistPickIntent pick:
                    OnHeistPick(session, message.RequestId, seat, pick);
                    return;

                default:
                    Refuse(session, message.RequestId, MatchRefusalCode.NotASeat);
                    return;
            }
        }

        // ---------------------------------------------------------------- the pieces

        /// <summary> The seat this session's player owns, or <see cref="MatchSeats.None"/>. </summary>
        int ResolveSeat(EntitySubscriber session)
        {
            ClientPeerState peer = TryGetClientPeer(session);
            if (peer == null)
                return MatchSeats.None;

            return Model.SeatIndexOfPlayer(peer.PlayerId);
        }

        /// <summary>
        /// The sender is here: lift its grace, ask for a covered seat back from the next turn, and clear its
        /// strikes.
        /// </summary>
        void MarkSeatPresent(int seat)
        {
            if (Model.Pacing.Grace(seat).HasValue)
                ExecuteMatchAction(new MatchSetGrace(seat, null));

            RequestReclaimIfCovered(seat);
            ForgiveStrikes(seat);
        }

        void Refuse(EntitySubscriber session, int requestId, MatchRefusalCode code)
            => SendToClient(session, new MatchIntentRefused(requestId, code));

        /// <summary>
        /// Leaving is a disconnect that skips grace: the seat is covered at once, and the game is still played
        /// out to a real result. Leaving during the Heist resolves the picks that seat owes instead
        /// (<see cref="ResolveHeistOnSeatGone"/>).
        /// </summary>
        void OnSeatLeft(int seat)
        {
            _log.Info("Seat {Seat} left the table", seat);

            ExecuteMatchAction(new MatchSetGrace(seat, null));
            CancelReclaimIfPending(seat);

            if (!ResolveHeistOnSeatGone(seat))
            {
                CoverSeat(seat);
                MaybePlayOutDesertedTable();
            }

            RearmFromModel();
        }
    }
}
