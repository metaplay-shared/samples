using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The client side of the match protocol. It connects to the table on <see cref="ClientSlotGame.Match"/>,
    /// holds the replicated <see cref="MatchModel"/>, sends the player's moves as entity messages, and applies the
    /// hand deliveries that arrive on the same channel. The server tells the client which match it is in.
    /// </summary>
    public class MatchClient : MultiplayerEntityClientBase<MatchModel>
    {
        public override ClientSlot ClientSlot => ClientSlotGame.Match;

        /// <summary>Raised when the host refused a move this client submitted.</summary>
        public event Action<MatchMoveRefused> MoveRefused;

        /// <summary>Raised when a hand delivery has been applied to the model.</summary>
        public event Action HandChanged;

        /// <summary>
        /// How many times this client has activated a table. The table page watches this instead of the entity id,
        /// because a reconnect to the same table is a new activation with a full state and no previous frame to
        /// animate from (<c>docs/web-client.md</c>). It is a counter rather than an event so that a page created
        /// after the activation can still detect it.
        /// </summary>
        public int ActivationCount { get; private set; }

        /// <summary>
        /// A hand delivery that arrived before the board reached its play index. Held until
        /// <see cref="MatchModel.TryDeliverOwnHand"/> accepts it.
        /// </summary>
        MatchOwnHand _pendingHand;

        /// <summary>The nonce of the latest clock-sync request, and when it was sent by the device clock.</summary>
        int      _clockSyncNonce;
        MetaTime _clockSyncSentAt;

        /// <summary>
        /// The earliest device time at which the next clock-sync request may be sent. It is set when a request is
        /// sent, not when the reply arrives, so lost replies cause at most one request per
        /// <see cref="ServerClockEstimate.ResampleInterval"/>.
        /// </summary>
        MetaTime _clockSyncNextAt = MetaTime.Epoch;

        /// <summary>
        /// Registers the entity message listeners when the model context is created. Registering them anywhere else
        /// drops messages without an error:
        /// <list type="bullet">
        /// <item>The entity message dispatcher removes all its listeners when its peer resets on every activation, so
        /// listeners registered once at startup stop working after the first reconnect.</item>
        /// <item><c>ActivateWithState</c> dispatches messages buffered during activation, including hand deliveries and
        /// move refusals, before it calls <c>OnActivatingModel</c>. This method runs before that dispatch.</item>
        /// </list>
        /// </summary>
        protected override MultiplayerEntityClientContext<MatchModel> CreateActiveModelContext(EntityInitialState state, ISharedGameConfig gameConfig)
        {
            // A held hand belongs to the entity being replaced. Clear it before registering the listeners, so a
            // delivery dispatched from the activation buffer is kept.
            _pendingHand = null;

            MultiplayerEntityClientContext<MatchModel> context = base.CreateActiveModelContext(state, gameConfig);

            _entityMessageDispatcher.AddListener<MatchHandDelivered>(OnHandDelivered);
            _entityMessageDispatcher.AddListener<MatchMoveRefused>(OnMoveRefused);
            _entityMessageDispatcher.AddListener<MatchClockSyncResponse>(OnClockSyncResponse);

            return context;
        }

        /// <summary>
        /// Called when the model is active. The SDK has already applied the seat's hand from the subscribe's private
        /// state. This counts the activation, applies any held hand, and measures the host's clock, because every
        /// countdown on the table compares against a time the host wrote from its own clock.
        /// </summary>
        protected override void OnActivatingModel()
        {
            base.OnActivatingModel();

            ActivationCount++;
            TryApplyPendingHand();

            // This first round trip is measured during activation, when it is slowest. It is still better than
            // no estimate, which falls back to the device clock, and UpdateLogic takes more samples later.
            RequestClockSync(MetaTime.Now);
        }

        /// <summary>
        /// Re-measures the host's clock while a table is active, when <see cref="ServerClockEstimate"/> wants a sample.
        /// A single sample per activation would keep the slow first measurement and would miss a device clock
        /// change during the game (<c>docs/match.md</c>, "Client clock offset").
        /// </summary>
        public override void UpdateLogic(MetaTime timeNow)
        {
            base.UpdateLogic(timeNow);

            if (Phase != MultiplayerEntityClientPhase.EntityActive)
                return;
            if (timeNow < _clockSyncNextAt || !ServerClockEstimate.Shared.WantsSampleAt(timeNow))
                return;

            RequestClockSync(timeNow);
        }

        /// <summary>
        /// Send a move for the current play index. The request names the seat so the host can check it against
        /// the seat this player occupies. Does nothing when there is no model or this client has no seat.
        /// </summary>
        public void PlayCard(Card card)
        {
            MatchModel match = Model;
            if (match == null)
                return;

            int seat = match.OwnSeat;
            if (seat < 0)
                return;

            _entityMessageDispatcher.SendMessage(new MatchPlayCardRequest(seat, match.Board.PlayIndex, card));
        }

        /// <summary>
        /// Tell the host that this player is leaving the table. It is a message rather than an action because only
        /// the host writes the match timeline.
        /// </summary>
        public void Leave()
        {
            if (Model == null || Model.OwnSeat < 0)
                return;

            _entityMessageDispatcher.SendMessage(new MatchLeaveRequest());
        }

        /// <summary>The cards this client may legally play right now. Empty unless it is this seat's turn.</summary>
        public List<Card> GetLegalPlays()
        {
            MatchSeatView view = Model?.GetOwnSeatView();
            return view == null ? new List<Card>() : view.GetLegalPlays();
        }

        public override void OnAdvancedOnTimeline()
        {
            base.OnAdvancedOnTimeline();

            // The board advanced, so a held hand delivery may now match its play index.
            TryApplyPendingHand();
        }

        /// <summary>
        /// Raised when a timeline update from the host does not match the host's checksum, so this client's model
        /// has diverged from the host's. Reconnecting fetches a fresh copy of the model.
        /// <para>
        /// Without a handler, the SDK default runs, which closes the connection with a generic terminal error. The
        /// client would report an unreachable server and would not reconnect. A handler replaces that default.
        /// </para>
        /// </summary>
        public event Action<EntityId> TimelineUpdateFailed;

        /// <inheritdoc cref="TimelineUpdateFailed"/>
        public override void OnTimelineUpdateFailed()
        {
            Action<EntityId> handler = TimelineUpdateFailed;
            if (handler == null)
            {
                base.OnTimelineUpdateFailed();
                return;
            }

            handler(Model != null ? Model.EntityId : EntityId.None);
        }

        void OnHandDelivered(MatchHandDelivered message)
        {
            _pendingHand = message.ToOwnHand();
            TryApplyPendingHand();
        }

        void OnMoveRefused(MatchMoveRefused message)
        {
            MoveRefused?.Invoke(message);
        }

        /// <summary>
        /// Send a clock-sync request. <see cref="OnClockSyncResponse"/> passes the reply to
        /// <see cref="ServerClockEstimate"/>. The next request waits <see cref="ServerClockEstimate.ResampleInterval"/>, which
        /// matches the rate limit the host applies to this message.
        /// </summary>
        void RequestClockSync(MetaTime deviceNow)
        {
            _clockSyncNonce++;
            _clockSyncSentAt = deviceNow;
            _clockSyncNextAt = deviceNow + ServerClockEstimate.ResampleInterval;
            _entityMessageDispatcher.SendMessage(new MatchClockSyncRequest(_clockSyncNonce));
        }

        void OnClockSyncResponse(MatchClockSyncResponse message)
        {
            // Ignore a reply to an older request, because _clockSyncSentAt belongs to the latest one.
            if (message.Nonce != _clockSyncNonce)
                return;

            ServerClockEstimate.Shared.TryTakeSample(_clockSyncSentAt, message.ServerTime, MetaTime.Now);
        }

        void TryApplyPendingHand()
        {
            if (_pendingHand == null)
                return;

            MatchModel match = Model;
            if (match == null || !match.TryDeliverOwnHand(_pendingHand))
                return;

            _pendingHand = null;
            HandChanged?.Invoke();
        }
    }
}
