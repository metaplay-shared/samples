// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Game.Logic;
using Metaplay.Cloud.Entity;
using Metaplay.Core;
using Metaplay.Server;
using System;
using Metaplay.Core.Config;
using static System.FormattableString;

namespace Game.Server.Player
{
    [EntityConfig]
    public class PlayerConfig : PlayerConfigBase
    {
        public override Type EntityActorType => typeof(PlayerActor);
    }

    /// <summary>
    /// Entity actor class representing a player.
    /// </summary>
    public sealed class PlayerActor : PlayerActorBase<PlayerModel>, IPlayerModelServerListener
    {
        /// <summary>
        /// The SharedGameConfig that contains any A/B tests that affect this user player.
        /// This is only available after PostLoad has been invoked, or rather in most methods outside Initialize, InitializePersisted, PostLoad, and the constructor!
        ///
        /// Feel free to change return value to your own SharedGameConfig type when you implement game config! 
        /// </summary>
        private SharedGameConfigBase SharedGameConfig => (SharedGameConfigBase)_specializedGameConfig.SharedConfig;
        
        /// <summary>
        /// The ServerGameConfig that contains any A/B tests that affect this user player.
        /// This is only available after PostLoad has been invoked, or rather in most methods outside Initialize, InitializePersisted, PostLoad, and the constructor!
        ///
        /// Feel free to change return value to your own ServerGameConfig type when you implement game config! 
        /// </summary>
        private ServerGameConfigBase ServerGameConfig => (ServerGameConfigBase)_specializedGameConfig.ServerConfig;

        protected override string RandomNewPlayerName()
        {
            return Invariant($"Guest {new Random().Next(100_000)}");
        }

        protected override void OnSwitchedToModel(PlayerModel model)
        {
            model.ServerListener = this;
        }
    }
}
