using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Picks the cosmetics a bot wears from the published catalogue (<c>docs/cosmetics.md</c>), for both
    /// <see cref="TournamentBots"/> and <see cref="BotSeatIdentity"/>, each with its own seed.
    /// <para>
    /// Each draw depends only on the <see cref="RandomPCG"/> passed in and the catalogue. Candidates are sorted
    /// by ID before the pick, because the catalogue's iteration order is not defined.
    /// </para>
    /// </summary>
    public static class CosmeticDraws
    {
        /// <summary>
        /// The range of the name-effect roll in <see cref="RolledNameEffect"/>. One roll value in this range
        /// gives a premium (gem-priced) name effect, four give a basic (coin-priced) one, and the rest give
        /// plain text.
        /// <para>
        /// The rate is chosen so a standings board or table shows a few name effects without looking like an
        /// advertisement for the shop.
        /// </para>
        /// </summary>
        public const int NameEffectRollRange = 40;

        /// <summary>
        /// Returns a random purchasable avatar from the catalogue, or null when the catalogue has none. A bot
        /// with a null avatar is drawn with the client's default avatar.
        /// </summary>
        public static CosmeticId PurchasableAvatar(SharedGameConfig config, RandomPCG random)
        {
            List<CosmeticId> avatars = Purchasable(config, CosmeticKind.Avatar, priceCurrency: null);
            return random.Choice(avatars);
        }

        /// <summary>
        /// Returns a random name effect, or null for plain text. Roll 0 picks uniformly among the purchasable
        /// name effects priced in gems, rolls 1 to 4 among those priced in coins, and every other roll returns
        /// null. When the catalogue has no name effect in the chosen currency, the method returns null instead
        /// of throwing.
        /// </summary>
        public static CosmeticId RolledNameEffect(SharedGameConfig config, RandomPCG random)
        {
            int roll = random.NextInt(NameEffectRollRange);

            if (roll > 4)
                return null;

            CurrencyType     priceCurrency = roll == 0 ? CurrencyType.Gems : CurrencyType.Coins;
            List<CosmeticId> choices       = Purchasable(config, CosmeticKind.NameEffect, priceCurrency);

            return random.Choice(choices);
        }

        /// <summary>
        /// Returns the purchasable IDs of one kind, optionally only those priced in <paramref name="priceCurrency"/>,
        /// sorted by ID so a seeded pick does not depend on the catalogue's iteration order.
        /// </summary>
        static List<CosmeticId> Purchasable(SharedGameConfig config, CosmeticKind kind, CurrencyType? priceCurrency)
        {
            List<CosmeticId> ids = new List<CosmeticId>();
            if (config?.Cosmetics == null)
                return ids;

            foreach ((CosmeticId id, CosmeticInfo info) in config.Cosmetics)
            {
                if (info == null || info.Kind != kind || !info.IsPurchasable)
                    continue;
                if (priceCurrency.HasValue && (info.Price == null || info.Price.Currency != priceCurrency.Value))
                    continue;

                ids.Add(id);
            }

            ids.Sort((CosmeticId left, CosmeticId right) => string.CompareOrdinal(left.Value, right.Value));
            return ids;
        }
    }
}
