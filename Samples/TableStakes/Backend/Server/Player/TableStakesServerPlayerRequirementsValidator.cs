using Game.Logic;

namespace Game.Server.Player
{
    /// <summary>
    /// The server's player name validator, used by the LiveOps Dashboard's rename. It also refuses bot names.
    /// <para>
    /// The SDK passes no game config to <c>ValidatePlayerName</c>, so this override reads the bot names from the
    /// config the server is serving. The integration registry uses the <b>most derived</b> implementation, so the
    /// server uses this class and other builds use <see cref="TableStakesPlayerRequirementsValidator"/>.
    /// </para>
    /// </summary>
    public class TableStakesServerPlayerRequirementsValidator : TableStakesPlayerRequirementsValidator
    {
        /// <summary>
        /// Returns the bot names from the active baseline game config, or an empty roster if this node has not
        /// received a game config yet. The empty case does not happen in practice, because the player being
        /// renamed was loaded with a game config.
        /// <para>
        /// It uses the baseline config, not a player's experiment specialization, because an operator rename
        /// does not happen inside any player's session.
        /// </para>
        /// </summary>
        protected override BotNameRoster ReservedBotNames() => ServerBotNames.FromActiveBaseline();
    }
}
