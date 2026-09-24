using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// The SDK's player name validator, using this game's rules from <see cref="DisplayNamePolicy"/>. The player's
    /// rename (<see cref="PlayerRenameRequest"/>) calls <see cref="DisplayNamePolicy"/> directly so it can return a
    /// reason. A LiveOps Dashboard rename uses only this class, so without it the Dashboard could set a name the game
    /// refuses (<c>docs/player.md</c>, "The LiveOps Dashboard rename"). The length properties are overridden because
    /// Dashboard tooling reads them without calling <c>ValidatePlayerName</c>.
    /// </summary>
    public class TableStakesPlayerRequirementsValidator : PlayerRequirementsValidator
    {
        public override int MinPlayerNameLength => DisplayNamePolicy.MinLength;

        public override int MaxPlayerNameLength => DisplayNamePolicy.MaxLength;

        /// <summary>
        /// Whether the admin path may store <paramref name="playerName"/>.
        /// <para>
        /// The game's rename handler stores the canonical form of a name, but the SDK's admin path stores the
        /// string unchanged. A name with extra spaces could pass <see cref="DisplayNamePolicy.Validate"/>, which
        /// checks the canonical form, and still be stored longer than the seat plaques fit. So the raw length
        /// in UTF-16 code units is first limited to <see cref="DisplayNamePolicy.MaxLength"/>.
        /// </para>
        /// </summary>
        public override bool ValidatePlayerName(string playerName)
        {
            if (playerName != null && playerName.Length > DisplayNamePolicy.MaxLength)
                return false;

            return DisplayNamePolicy.Validate(playerName, ReservedBotNames()) == DisplayNameRefusal.None;
        }

        /// <summary>
        /// The reserved bot names to refuse. The SDK passes no config to the validator, so this base
        /// implementation returns <see cref="BotNameRoster.Empty"/>.
        /// <para>
        /// On the server, where the admin rename runs, <c>Game.Server.Player.TableStakesServerPlayerRequirementsValidator</c>
        /// overrides it to return the roster from the active config. The SDK's integration registry uses the
        /// most derived implementation, so the admin path gets the server's roster. Elsewhere, every other name
        /// rule is still enforced.
        /// </para>
        /// </summary>
        protected virtual BotNameRoster ReservedBotNames() => BotNameRoster.Empty;
    }
}
