using Metaplay.Core;
using Metaplay.Core.Config;
using Metaplay.Core.Model;
using System.Collections.Generic;
using System.Linq;

namespace Game.Logic
{
    /// <summary>
    /// The cosmetic slots (<c>docs/cosmetics.md</c>). Every slot is visible to other players. Slots are an enum
    /// rather than config because the client must implement rendering for each one.
    /// </summary>
    [MetaSerializable]
    public enum CosmeticKind
    {
        None = 0,
        Avatar = 1,
        Frame = 2,
        NameEffect = 3,
    }

    /// <summary>
    /// The visual a cosmetic item uses. Each value matches one rendering rule the client implements.
    /// <para>
    /// It is an enum rather than a value derived from the config id, because a CSS class that matches no rule
    /// renders as nothing, with no error. Validation refuses an item with no style, and the client's coverage test
    /// refuses a style with no rendering rule. A designer can also change an item's visual without changing its id.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public enum CosmeticStyle
    {
        None = 0,

        FrameSapphire = 1,
        FrameGold     = 2,
        FrameEmerald  = 3,
        FrameAmethyst = 4,
        FrameRuby     = 5,
        FramePlatinum = 6,
        FrameSilver   = 7,
        FrameBronze   = 8,

        // The plain frame and plain name that every player starts with. They are real styles rather than
        // CosmeticStyle.None, so a player always wears an owned item and can return to the plain look by
        // equipping it.
        FramePlain = 9,

        NamePrism   = 20,
        NameAmber   = 21,
        NameAzure   = 22,
        NameViolet  = 23,
        NameEmerald = 24,
        NameBloom   = 25,
        NameFrost   = 26,
        NamePlain   = 27,

        // Avatar faces. Each value names one glyph drawn by the client's AvatarIcon. AvatarIcon also draws the
        // spade for an empty slot, and the catalogue gives it to new players as their starting avatar.
        AvatarSpade   = 40,
        AvatarHeart   = 41,
        AvatarDiamond = 42,
        AvatarClub    = 43,
        AvatarAce     = 44,
        AvatarJoker   = 45,
        AvatarCrown   = 46,
        AvatarDice    = 47,
        AvatarChip    = 48,
        AvatarTrump   = 49,
    }

    /// <summary>
    /// Maps a <see cref="CosmeticStyle"/> to its slot and to the token the client renders it with. A style missing
    /// from a mapping falls into the default case, which the tests detect.
    /// </summary>
    public static class CosmeticStyles
    {
        /// <summary>
        /// The slot <paramref name="style"/> can be worn in, or <see cref="CosmeticKind.None"/> for an unmapped
        /// style. Only that slot's markup draws the style, so validation requires an item's kind to match.
        /// </summary>
        public static CosmeticKind SlotOf(CosmeticStyle style) => style switch
        {
            CosmeticStyle.AvatarSpade   => CosmeticKind.Avatar,
            CosmeticStyle.AvatarHeart   => CosmeticKind.Avatar,
            CosmeticStyle.AvatarDiamond => CosmeticKind.Avatar,
            CosmeticStyle.AvatarClub    => CosmeticKind.Avatar,
            CosmeticStyle.AvatarAce     => CosmeticKind.Avatar,
            CosmeticStyle.AvatarJoker   => CosmeticKind.Avatar,
            CosmeticStyle.AvatarCrown   => CosmeticKind.Avatar,
            CosmeticStyle.AvatarDice    => CosmeticKind.Avatar,
            CosmeticStyle.AvatarChip    => CosmeticKind.Avatar,
            CosmeticStyle.AvatarTrump   => CosmeticKind.Avatar,

            CosmeticStyle.FrameSapphire => CosmeticKind.Frame,
            CosmeticStyle.FrameGold     => CosmeticKind.Frame,
            CosmeticStyle.FrameEmerald  => CosmeticKind.Frame,
            CosmeticStyle.FrameAmethyst => CosmeticKind.Frame,
            CosmeticStyle.FrameRuby     => CosmeticKind.Frame,
            CosmeticStyle.FramePlatinum => CosmeticKind.Frame,
            CosmeticStyle.FrameSilver   => CosmeticKind.Frame,
            CosmeticStyle.FrameBronze   => CosmeticKind.Frame,
            CosmeticStyle.FramePlain    => CosmeticKind.Frame,

            CosmeticStyle.NamePrism   => CosmeticKind.NameEffect,
            CosmeticStyle.NameAmber   => CosmeticKind.NameEffect,
            CosmeticStyle.NameAzure   => CosmeticKind.NameEffect,
            CosmeticStyle.NameViolet  => CosmeticKind.NameEffect,
            CosmeticStyle.NameEmerald => CosmeticKind.NameEffect,
            CosmeticStyle.NameBloom   => CosmeticKind.NameEffect,
            CosmeticStyle.NameFrost   => CosmeticKind.NameEffect,
            CosmeticStyle.NamePlain   => CosmeticKind.NameEffect,

            _ => CosmeticKind.None,
        };

        /// <summary>
        /// The token the client renders <paramref name="style"/> with: the CSS class suffix for a frame or name
        /// effect (<c>m-frame--{token}</c>, <c>m-name--{token}</c>), or the glyph for an avatar. Empty for
        /// <see cref="CosmeticStyle.None"/>, in which case the client emits no modifier class.
        /// </summary>
        public static string TokenOf(CosmeticStyle style) => style switch
        {
            CosmeticStyle.AvatarSpade   => "avatar-spade",
            CosmeticStyle.AvatarHeart   => "avatar-heart",
            CosmeticStyle.AvatarDiamond => "avatar-diamond",
            CosmeticStyle.AvatarClub    => "avatar-club",
            CosmeticStyle.AvatarAce     => "avatar-ace",
            CosmeticStyle.AvatarJoker   => "avatar-joker",
            CosmeticStyle.AvatarCrown   => "avatar-crown",
            CosmeticStyle.AvatarDice    => "avatar-dice",
            CosmeticStyle.AvatarChip    => "avatar-chip",
            CosmeticStyle.AvatarTrump   => "avatar-trump",

            CosmeticStyle.FrameSapphire => "frame-sapphire",
            CosmeticStyle.FrameGold     => "frame-gold",
            CosmeticStyle.FrameEmerald  => "frame-emerald",
            CosmeticStyle.FrameAmethyst => "frame-amethyst",
            CosmeticStyle.FrameRuby     => "frame-ruby",
            CosmeticStyle.FramePlatinum => "frame-platinum",
            CosmeticStyle.FrameSilver   => "frame-silver",
            CosmeticStyle.FrameBronze   => "frame-bronze",
            CosmeticStyle.FramePlain    => "frame-plain",

            CosmeticStyle.NamePrism   => "name-prism",
            CosmeticStyle.NameAmber   => "name-amber",
            CosmeticStyle.NameAzure   => "name-azure",
            CosmeticStyle.NameViolet  => "name-violet",
            CosmeticStyle.NameEmerald => "name-emerald",
            CosmeticStyle.NameBloom   => "name-bloom",
            CosmeticStyle.NameFrost   => "name-frost",
            CosmeticStyle.NamePlain   => "name-plain",

            _ => "",
        };

        /// <summary>
        /// The slots the client renders. Validation refuses a purchasable or styled item in any other slot,
        /// because the player would pay for something that never appears (<c>docs/cosmetics.md</c>).
        /// </summary>
        public static readonly IReadOnlyList<CosmeticKind> ShippedSlots = new[] { CosmeticKind.Avatar, CosmeticKind.Frame, CosmeticKind.NameEffect };

        /// <summary>Whether <paramref name="kind"/> is in <see cref="ShippedSlots"/>.</summary>
        public static bool IsShipped(CosmeticKind kind) => ShippedSlots.Contains(kind);
    }

    [MetaSerializable]
    public class CosmeticId : StringId<CosmeticId> { }

    /// <summary>
    /// One item in the cosmetic catalogue.
    /// <para>
    /// Never delete a published entry. Ownership is permanent, and players who own a deleted item would hold
    /// an id that no longer resolves. To retire an item, set <see cref="IsPurchasable"/> to false.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class CosmeticInfo : IGameConfigData<CosmeticId>, IValidatedConfigItem
    {
        [MetaMember(1)] public CosmeticId     Id          { get; private set; }
        [MetaMember(2)] public CosmeticKind   Kind        { get; private set; }
        [MetaMember(3)] public string         DisplayName { get; private set; }
        [MetaMember(4)] public CurrencyAmount Price       { get; private set; }

        /// <summary>Whether the shop sells this item. An item that is not purchasable stays in the catalogue and can still be equipped.</summary>
        [MetaMember(5)] public bool           IsPurchasable { get; private set; }

        /// <summary>
        /// The visual this item renders with. See <see cref="CosmeticStyle"/> for why this is not derived from
        /// <see cref="Id"/>.
        /// </summary>
        [MetaMember(6)] public CosmeticStyle  Style { get; private set; }

        /// <summary>The text shown under the name on the preview card.</summary>
        [MetaMember(7)] public string         Flavour { get; private set; }

        /// <summary>
        /// Player-facing text that says how to earn an item that is not for sale, for example "Win a tournament
        /// season". Empty for a purchasable item and for a retired item that can no longer be obtained.
        /// </summary>
        [MetaMember(8)] public string         UnlockRequirement { get; private set; }

        public CosmeticId ConfigKey => Id;

        public CosmeticInfo() { }

        /// <summary>
        /// Built from <c>GameConfigSource/Cosmetics.csv</c>. The optional parameters let the sheet leave a cell
        /// blank for the default: purchasable, no flavour text, and no unlock requirement.
        /// </summary>
        [MetaGameConfigBuildConstructor]
        public CosmeticInfo(
            CosmeticId     id,
            CosmeticKind   kind,
            string         displayName,
            CurrencyAmount price             = null,
            bool           isPurchasable     = true,
            CosmeticStyle  style             = CosmeticStyle.None,
            string         flavour           = "",
            string         unlockRequirement = "")
        {
            Id                = id;
            Kind              = kind;
            DisplayName       = displayName;
            Price             = price;
            IsPurchasable     = isPurchasable;
            Style             = style;
            Flavour           = flavour;
            UnlockRequirement = unlockRequirement;
        }

        /// <summary>The rendering token for <see cref="Style"/>. See <see cref="CosmeticStyles.TokenOf"/>.</summary>
        public string StyleToken => CosmeticStyles.TokenOf(Style);

        public void Validate(ConfigItemValidation validation)
        {
            validation.Require(Kind != CosmeticKind.None, "has no slot", nameof(Kind));
            validation.Require(!string.IsNullOrWhiteSpace(DisplayName), "has no display name", nameof(DisplayName));

            ValidateStyle(validation);

            // An earned (not purchasable) cosmetic may have no price. A purchasable one must have a price.
            if (Price == null)
            {
                if (IsPurchasable)
                    validation.Error("has no price", nameof(Price));
                return;
            }

            validation.RequirePositive(Price.Amount, nameof(Price));

            // Spin tokens are only for wheel spins, so cosmetics are priced in coins or gems.
            validation.Require(
                Price.Currency == CurrencyType.Coins || Price.Currency == CurrencyType.Gems,
                $"is priced in {Price.Currency}; cosmetics are priced in coins or gems",
                nameof(Price));
        }

        /// <summary>
        /// Validates <see cref="Style"/> against <see cref="Kind"/>.
        /// <para>
        /// An item in a slot the client does not render must not be purchasable and must have no style, because
        /// buying it would change nothing on screen. An item in a rendered slot must have a style of that slot,
        /// or it would render as nothing when equipped.
        /// </para>
        /// </summary>
        void ValidateStyle(ConfigItemValidation validation)
        {
            if (!CosmeticStyles.IsShipped(Kind))
            {
                validation.Require(!IsPurchasable, $"is in the {Kind} slot, which this build does not render, so it cannot be sold", nameof(IsPurchasable));
                validation.Require(Style == CosmeticStyle.None, $"is in the {Kind} slot, which has no styles", nameof(Style));
                return;
            }

            if (Style == CosmeticStyle.None)
            {
                validation.Error("names no style, so nothing would be drawn when it is worn", nameof(Style));
                return;
            }

            CosmeticKind styleSlot = CosmeticStyles.SlotOf(Style);
            validation.Require(styleSlot == Kind, $"is a {Kind} wearing the {styleSlot} style {Style}", nameof(Style));

            // An item with no price that is not purchasable must say how it is earned. A retired item keeps its
            // price with IsPurchasable false, and the cosmetics grid shows it as "Not available".
            if (!IsPurchasable && Price == null)
            {
                validation.Require(
                    !string.IsNullOrWhiteSpace(UnlockRequirement),
                    "cannot be bought and says nothing about how it is earned",
                    nameof(UnlockRequirement));
            }
        }

        public override string ToString() => Id?.Value ?? "(no cosmetic)";
    }
}
