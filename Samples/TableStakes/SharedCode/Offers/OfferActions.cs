using Metaplay.Core;
using Metaplay.Core.Model;

namespace Game.Logic
{
    public static partial class ActionCodes
    {
        // Offer action. Buying an offer uses the SDK's PlayerPurchaseInGameCurrencyMetaOffer, so the only game
        // action for offers is the personalization toggle. SharedCode/Player/PlayerActions.cs lists every
        // player action code, so check it before taking a new code.
        public const int PlayerSetPersonalizedOffersEnabled = 5012;
    }

    /// <summary>
    /// Turns segment-targeted featured offers on or off (<c>docs/offers.md</c>, "Segment targeting"). Always
    /// succeeds, because it only changes the player's own preference.
    /// </summary>
    [ModelAction(ActionCodes.PlayerSetPersonalizedOffersEnabled)]
    public class PlayerSetPersonalizedOffersEnabled : PlayerAction
    {
        public bool Enabled { get; private set; }

        public PlayerSetPersonalizedOffersEnabled() { }

        public PlayerSetPersonalizedOffersEnabled(bool enabled)
        {
            Enabled = enabled;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
                player.SetPersonalizedOffersEnabled(Enabled);

            return MetaActionResult.Success;
        }

        public override string ToString() => $"set personalized offers {(Enabled ? "on" : "off")}";
    }
}
