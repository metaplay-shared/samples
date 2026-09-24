using Metaplay.Core.Model;

namespace Game.Logic
{
    public static partial class ActionCodes
    {
        // Cosmetic actions. Every player action code is also listed in SharedCode/Player/PlayerActions.cs, so
        // a new feature can see which codes are taken without reading every feature's file.
        public const int PlayerBuyCosmetic          = 5014;
        public const int PlayerEquipCosmetic        = 5015;
        public const int PlayerAcknowledgeCosmetics = 5016;
    }

    public static partial class ActionResults
    {
        /// <summary>The ID is not in the published catalogue. Nothing is spent or equipped.</summary>
        public static readonly MetaActionResult NoSuchCosmetic = new MetaActionResult(nameof(NoSuchCosmetic));

        /// <summary>The item exists but is not for sale, because it is earned or retired.</summary>
        public static readonly MetaActionResult CosmeticNotPurchasable = new MetaActionResult(nameof(CosmeticNotPurchasable));

        /// <summary>The player already owns the item. Ownership is permanent, so buying it again would only charge twice.</summary>
        public static readonly MetaActionResult CosmeticAlreadyOwned = new MetaActionResult(nameof(CosmeticAlreadyOwned));

        /// <summary>The player does not own the item they tried to equip.</summary>
        public static readonly MetaActionResult CosmeticNotOwned = new MetaActionResult(nameof(CosmeticNotOwned));

        /// <summary>The item is already equipped. A repeated Equip tap is refused and changes nothing.</summary>
        public static readonly MetaActionResult CosmeticAlreadyEquipped = new MetaActionResult(nameof(CosmeticAlreadyEquipped));

        /// <summary>No cosmetic is waiting to be acknowledged. A repeated acknowledgement is refused and changes nothing.</summary>
        public static readonly MetaActionResult NoCosmeticToAcknowledge = new MetaActionResult(nameof(NoCosmeticToAcknowledge));
    }

    /// <summary>
    /// Buys one cosmetic and equips it.
    /// <para>
    /// It is an ordinary client action, unlike the spin wheel's server action, because a purchase has no secret:
    /// the price and the item come from the shared game config, so client and server compute the same result. It
    /// is not the client-callable currency grant that <c>offers.md</c> forbids, because it only spends currency.
    /// Buying also equips the item, because the player bought it from a preview that shows them wearing it.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerBuyCosmetic)]
    public class PlayerBuyCosmetic : PlayerAction
    {
        /// <summary>The catalogue ID to buy. Both client and server look it up in the catalogue before use.</summary>
        public CosmeticId Cosmetic { get; private set; }

        public PlayerBuyCosmetic() { }

        public PlayerBuyCosmetic(CosmeticId cosmetic)
        {
            Cosmetic = cosmetic;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit) =>
            player.BuyCosmetic(Cosmetic, commit);

        public override string ToString() => $"buy cosmetic {Cosmetic}";
    }

    /// <summary>
    /// Equips a cosmetic the player already owns. Equipping is free.
    /// <para>
    /// <b>The action does not name the slot.</b> The slot is read from the catalogue item's
    /// <see cref="CosmeticInfo.Kind"/>, so a client cannot put, for example, a name effect in the frame slot.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerEquipCosmetic)]
    public class PlayerEquipCosmetic : PlayerAction
    {
        public CosmeticId Cosmetic { get; private set; }

        public PlayerEquipCosmetic() { }

        public PlayerEquipCosmetic(CosmeticId cosmetic)
        {
            Cosmetic = cosmetic;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit) =>
            player.EquipCosmetic(Cosmetic, commit);

        public override string ToString() => $"equip cosmetic {Cosmetic}";
    }

    /// <summary>
    /// Marks every cosmetic the player received without buying it as seen, which clears the Profile badge.
    /// <para>
    /// It only empties <see cref="PlayerCosmeticsState.Unacknowledged"/>, so it cannot become a second way to grant
    /// items. It has no parameters, so a client cannot name an item that was never received.
    /// </para>
    /// </summary>
    [ModelAction(ActionCodes.PlayerAcknowledgeCosmetics)]
    public class PlayerAcknowledgeCosmetics : PlayerAction
    {
        public PlayerAcknowledgeCosmetics() { }

        public override MetaActionResult Execute(PlayerModel player, bool commit) =>
            player.AcknowledgeCosmetics(commit);

        public override string ToString() => "acknowledge newly acquired cosmetics";
    }
}
