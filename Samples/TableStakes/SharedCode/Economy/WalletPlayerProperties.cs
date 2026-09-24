using Metaplay.Core.Model;
using Metaplay.Core.Player;

namespace Game.Logic
{
    /// <summary>
    /// A wallet balance exposed as a targetable player property, so segments, offers and activables can use
    /// it as a condition.
    /// <para>
    /// There is one property per currency, because the Dashboard's property picker is a plain list. The derived
    /// type codes start at 1000 to stay clear of the SDK's own player properties.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1000)]
    public class PlayerPropertyCoins : TypedPlayerPropertyId<int>
    {
        public override string DisplayName => "Coin balance";

        public override int GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model ? model.Wallet.Coins : 0;
    }

    /// <inheritdoc cref="PlayerPropertyCoins"/>
    [MetaSerializableDerived(1001)]
    public class PlayerPropertyGems : TypedPlayerPropertyId<int>
    {
        public override string DisplayName => "Gem balance";

        public override int GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model ? model.Wallet.Gems : 0;
    }

    /// <inheritdoc cref="PlayerPropertyCoins"/>
    [MetaSerializableDerived(1002)]
    public class PlayerPropertySpinTokens : TypedPlayerPropertyId<int>
    {
        public override string DisplayName => "Spin token balance";

        public override int GetTypedValueForPlayer(IPlayerModelBase player) =>
            player is PlayerModel model ? model.Wallet.SpinTokens : 0;
    }
}
