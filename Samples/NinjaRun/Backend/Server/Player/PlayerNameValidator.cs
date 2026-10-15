using Game.Logic;
using Metaplay.Core.Player;

namespace Game.Server.Player
{
    /// <summary>
    /// Holds every route to a new player name (a dashboard rename and the SDK's own rename request as well as
    /// <see cref="PlayerSetName"/>) to the one rule <see cref="PlayerModel.IsValidPlayerName"/>, so no route can
    /// give a player a name the game would refuse, nor the <see cref="TimeTrialSkills.MaskedNinjaName"/> that
    /// marks a player as having none.
    /// </summary>
    public class PlayerNameValidator : PlayerRequirementsValidator
    {
        public override int MinPlayerNameLength => 1;
        public override int MaxPlayerNameLength => PlayerModel.MaxPlayerNameLength;

        public override bool ValidatePlayerName(string playerName) => PlayerModel.IsValidPlayerName(playerName);
    }
}
