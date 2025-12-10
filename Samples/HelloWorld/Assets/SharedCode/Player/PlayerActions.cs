// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Game-specific player action class, which attaches all game-specific actions to <see cref="PlayerModel"/>.
    /// </summary>
    public abstract class PlayerAction : PlayerActionCore<PlayerModel>
    {
    }
    
    /// <summary>
    /// Game-specific <see cref="PlayerUnsynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerUnsynchronizedServerAction : PlayerUnsynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Game-specific <see cref="PlayerSynchronizedServerActionCore{TModel}"/>
    /// </summary>
    public abstract class PlayerSynchronizedServerAction : PlayerSynchronizedServerActionCore<PlayerModel>
    {
    }

    /// <summary>
    /// Registry for game-specific ActionCodes, used by the individual PlayerAction classes.
    /// </summary>
    public static class ActionCodes
    {
        public const int PlayerClickButton = 5000;
    }

    /// <summary>
    /// Game-specific results returned from <see cref="PlayerActionCore.Execute(PlayerModel, bool)"/>.
    /// </summary>
    public static class ActionResult
    {
        // Shadow success result
        public static readonly MetaActionResult Success             = MetaActionResult.Success;

        // Game-specific results
        public static readonly MetaActionResult UnknownError        = new MetaActionResult(nameof(UnknownError));
    }

    // Game-specific example action: bump PlayerModel.NumClicked, triggered by button tap

    [ModelAction(ActionCodes.PlayerClickButton)]
    public class PlayerClickButton : PlayerAction
    {
        public PlayerClickButton() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
            {
                player.NumClicks += 1;
                player.Log.Info("Button clicked!");
            }

            return ActionResult.Success;
        }
    }
}
