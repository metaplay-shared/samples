// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.BotClient;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Core.Message;
using Metaplay.Core.Model;
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

        readonly RandomPCG _rnd = RandomPCG.CreateNew();

        PlayerModel _playerModel => (PlayerModel)PlayerContext.Model;

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

        /// <summary>
        /// Play the game the same way a human would in the Blazor client: tap the button, then buy
        /// the auto-clicker as soon as it is affordable.
        /// </summary>
        void TickMainState()
        {
            // Buy the auto-clicker once we can afford it.
            if (!_playerModel.AutoClickerPurchased && _playerModel.NumClicks >= PlayerModel.AutoClickerCost)
            {
                _log.Info("Purchasing the auto-clicker (numClicks={NumClicks})", _playerModel.NumClicks);
                PlayerContext.ExecuteAction(new PlayerPurchaseAutoClicker());
                return;
            }

            // Click the button at roughly 2 clicks/second.
            if (_rnd.NextInt(100) < 20)
                PlayerContext.ExecuteAction(new PlayerClickButton());
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
