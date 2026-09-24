using Metaplay.Core;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The cosmetics every player starts out owning and wearing, one per slot (<c>docs/cosmetics.md</c>).
    /// <para>
    /// The defaults are real catalogue items, so a slot always holds an item the player owns. If one is renamed in
    /// the config sheet, rename it here too. The config build fails when the sheet does not contain one of them
    /// (<see cref="GameConfigValidation.ValidateCosmetics"/>).
    /// </para>
    /// </summary>
    public static class CosmeticDefaults
    {
        public static readonly CosmeticId Avatar     = CosmeticId.FromString("avatar.spade");
        public static readonly CosmeticId Frame      = CosmeticId.FromString("frame.plain");
        public static readonly CosmeticId NameEffect = CosmeticId.FromString("name.plain");

        /// <summary>All defaults in slot order, which is also the order of the cosmetics grid tabs.</summary>
        public static readonly IReadOnlyList<CosmeticId> All = new[] { Avatar, Frame, NameEffect };
    }
}
