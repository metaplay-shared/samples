using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// Whether the player allows segment-targeted featured offers (<c>docs/offers.md</c>, "Segment targeting"). Every
    /// offer segment requires it to be true. It defaults to enabled, so existing players keep their targeting and no
    /// migration is needed. Only <see cref="PlayerModel.SetPersonalizedOffersEnabled"/> writes it, from a control on
    /// the Shop screen. The derived type code is in the game's block, which starts at 1000
    /// (see <see cref="PlayerPropertyCoins"/>).
    /// </summary>
    [MetaSerializableDerived(1008)]
    public class PlayerPropertyPersonalizedOffersEnabled : TypedPlayerPropertyId<bool>
    {
        public override string DisplayName => "Personalized offers enabled";

        public override bool GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model && model.PersonalizedOffersEnabled;
    }
}
