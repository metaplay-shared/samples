// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.BotClient;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Client;
using Metaplay.Core.Config;
using Metaplay.Core.InAppPurchase;
using Metaplay.Core.InGameMail;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;
using System.Threading.Tasks;

namespace Game.BotClient
{
    public enum BotClientState
    {
        Connecting,
        Main,
    }

    // BotClient

    [EntityConfig]
    internal class BotClientConfig : BotClientConfigBase
    {
        public override Type EntityActorType => typeof(BotClient);
    }

    public class BotClient : BotClientBase
    {
        BotClientState State { get; set; } = BotClientState.Connecting;

        PlayerModel _playerModel => (PlayerModel)PlayerContext.Model;

        protected override void PreStart()
        {
            base.PreStart();
        }

        protected override void RegisterHandlers()
        {
            base.RegisterHandlers();
        }

        protected override string GetCurrentStateLabel() => State.ToString();

        protected override Task OnUpdate()
        {
            // Tick current state (when connected)
            switch (State)
            {
                case BotClientState.Main:
                    TickMainState();
                    break;
            }

            return Task.CompletedTask;
        }

        protected override Task OnNetworkMessage(MetaMessage message)
        {
            //_log.Debug("OnNetworkMessage: {Message}", PrettyPrint.Compact(message));
            switch (message)
            {
                default:
                    _log.Warning("Unknown message received: {Message}", PrettyPrint.Compact(message));
                    break;
            }

            return Task.CompletedTask;
        }

        protected override Task OnSessionStartedAsync(BotSessionStartedArgs args)
        {
            State = BotClientState.Main;
            return Task.CompletedTask;
        }

        void TickMainState()
        {
            _log.Debug("Tick player (currentTick={CurrentTick})..", _playerModel.CurrentTick);
        }

        [MessageHandler]
        void HandleInitializeBot(BotCoordinator.InitializeBot _)
        {
            _log.Debug("Initializing bot");
        }

        static async Task<int> Main(string[] cmdLineArgs)
        {
            using (BotClientMain program = new BotClientMain())
                return await program.RunBotsAsync(cmdLineArgs);
        }
    }
}
