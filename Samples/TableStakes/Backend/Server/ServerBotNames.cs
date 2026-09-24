using Game.Logic;
using Metaplay.Server;

namespace Game.Server
{
    /// <summary>
    /// The bot names the server reads from the config it is serving. The matchmaker names bot seats from them, and
    /// the LiveOps Dashboard's rename refuses them, so both read them here and agree on which names are reserved.
    /// </summary>
    public static class ServerBotNames
    {
        /// <summary>
        /// Returns the bot names from the active baseline game config, or an empty roster if this node has not
        /// received a game config yet.
        /// </summary>
        public static BotNameRoster FromActiveBaseline()
        {
            ActiveGameConfig active = GlobalStateProxyActor.ActiveGameConfig.Get();
            return BotConfig.ReservedNames(active?.BaselineGameConfig?.SharedConfig as SharedGameConfig);
        }
    }
}
