// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Model;
using Metaplay.Core.Player;
using System;

namespace Game.Logic
{
    /// <summary>
    /// Example of a game-defined <see cref="ICustomComparable"/> type. Represents an ordered VIP
    /// tier that segment authors can reference by name (e.g. <c>"Silver"</c>) rather than by an
    /// underlying numeric value. Comparisons follow the natural ordering of the underlying
    /// <see cref="Level"/> enum, so a segment can require a range like
    /// <c>min=Silver, max=Platinum</c> in its <c>PropMin</c> / <c>PropMax</c> columns.
    /// <para>
    /// The tier is derived from <see cref="IPlayerModelBase.PlayerLevel"/> in this sample so no
    /// extra persisted state is needed. A real game would typically persist the tier directly on
    /// the player model.
    /// </para>
    /// </summary>
    [MetaSerializableDerived(1)]
    public class PlayerVipTier : ICustomComparable
    {
        [MetaSerializable]
        public enum Tier
        {
            Bronze   = 0,
            Silver   = 1,
            Gold     = 2,
            Platinum = 3,
            Diamond  = 4,
        }

        [MetaMember(1)] public Tier Value { get; private set; }

        PlayerVipTier() { }
        public PlayerVipTier(Tier value) { Value = value; }

        public static PlayerVipTier FromPlayerLevel(int playerLevel) => playerLevel switch
        {
            >= 80 => new PlayerVipTier(Tier.Diamond),
            >= 40 => new PlayerVipTier(Tier.Platinum),
            >= 20 => new PlayerVipTier(Tier.Gold),
            >= 5  => new PlayerVipTier(Tier.Silver),
            _     => new PlayerVipTier(Tier.Bronze),
        };

        public int CompareTo(object other)
        {
            if (other is PlayerVipTier o)
                return ((int)Value).CompareTo((int)o.Value);
            throw new ArgumentException($"Cannot compare {nameof(PlayerVipTier)} to {other?.GetType().Name ?? "null"}");
        }

        // Used for the textual representation shown in segment descriptions and the LiveOps
        // dashboard. Returning the enum name lets us round-trip with the ConfigParser below.
        public override string ToString() => Value.ToString();
    }
}
