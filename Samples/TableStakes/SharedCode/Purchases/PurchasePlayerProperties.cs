using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// How many demo purchases the server has validated for the player, as a targetable player property, so a
    /// segment can separate buyers from non-buyers (<c>docs/offers.md</c>, "Player properties"). It counts
    /// <see cref="PlayerModel.DemoPurchases"/>, which grows only when a validated receipt's contents were granted.
    /// Spending currency, a refused grant and a refund do not change it.
    /// <para>
    /// It does not read <c>FullInAppPurchaseHistory</c> because that field is <c>ServerOnly</c>: a condition on
    /// it would differ between client and server. <see cref="PlayerModel.DemoPurchases"/> is checksummed, so both
    /// sides agree. The type code is in the game's block (see <see cref="PlayerPropertyCoins"/>).
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1007)]
    public class PlayerPropertyValidatedPurchases : TypedPlayerPropertyId<int>
    {
        public override string DisplayName => "Validated purchases";

        public override int GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model ? model.DemoPurchases.Count : 0;
    }
}
