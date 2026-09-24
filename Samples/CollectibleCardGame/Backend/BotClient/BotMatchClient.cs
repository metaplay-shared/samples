using Game.Logic;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.MultiplayerEntity;
using Metaplay.Core.MultiplayerEntity.Messages;
using System;

namespace Game.BotClient
{
    /// <summary>
    /// The load client's own match sub-client. It is the browser client's shape without the browser: the same
    /// slot, the same model, the same directed messages — and the same listener-binding rule, because the SDK
    /// clears a channel's listeners on every activation and flushes the buffered messages before any
    /// activation hook runs.
    /// <para>
    /// It is a separate type from the browser's because the two answer a desync differently: the browser names
    /// it as a transient error the app shell recognizes, and a load bot has nobody to tell, so it throws.
    /// </para>
    /// </summary>
    public class BotMatchClientContext : MultiplayerEntityClientContext<MatchModel>
    {
        public BotMatchClientContext(ClientMultiplayerEntityContextInitArgs args) : base(args)
        {
            // Bound here, in the model-context build step, for the reason the design gives: the activation
            // flushes this channel's buffered messages immediately after this constructor returns. The hand
            // itself needs no listener: it arrives as subscribe-time private state and is maintained by
            // addressed timeline operations, which the journal applies like any other.
            _messageDispatcher.AddListener<MatchIntentRefused>(OnIntentRefused);
        }

        // The match actor accepts no direct (UDP) connection; everything travels over the session.
        protected override bool EnableDirectConnection => false;

        /// <summary> The last refusal, so the bot can tell its in-flight intent was answered. </summary>
        public MatchIntentRefused LastRefusal { get; private set; }

        public void Send(MetaMessage message) => _messageDispatcher.SendMessage(message);

        void OnIntentRefused(MatchIntentRefused refusal)
        {
            LastRefusal = refusal;
        }
    }

    /// <summary> The sub-client on the match slot. </summary>
    public class BotMatchClient : MultiplayerEntityClientBase<MatchModel, BotMatchClientContext>
    {
        public override ClientSlot ClientSlot => ClientSlotGame.Match;

        protected override string LogChannelName => "match";

        protected override BotMatchClientContext CreateActiveModelContext(EntityInitialState state, ISharedGameConfig gameConfig)
        {
            MatchModel model = DefaultDeserializeModel(state.State, gameConfig);
            return new BotMatchClientContext(DefaultInitArgs(model, state));
        }

        /// <summary>
        /// A timeline update did not hash to what the server said applying it would produce. A load bot has
        /// nobody to report it to and nothing useful to do with a stale copy, so it throws — which is what the
        /// SDK's own guidance says a bot client should do.
        /// </summary>
        public override void OnTimelineUpdateFailed()
        {
            throw new InvalidOperationException($"The match timeline diverged from the server's for {Model?.EntityId}");
        }
    }
}
