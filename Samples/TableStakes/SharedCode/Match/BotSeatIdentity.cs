using Metaplay.Core;

namespace Game.Logic
{
    /// <summary>
    /// Draws the cosmetics of a table's bot seats (<c>docs/bots.md</c>, <c>docs/cosmetics.md</c>).
    /// <para>
    /// The cosmetics are drawn once from the table seed and the current game config and copied onto the seat at setup,
    /// so a config update during the match does not change a bot's appearance, and clients read them from the
    /// replicated seat roster. <see cref="CosmeticDraws"/> defines which cosmetics a bot gets. This class only
    /// supplies the per-table random streams.
    /// </para>
    /// </summary>
    public static class BotSeatIdentity
    {
        /// <summary>
        /// Each per-seat draw at a table uses its own stream key. <see cref="BotProfiles.DrawForSeat"/> uses its
        /// own constant, and the avatar and name effect draws use these. Two draws that shared a key would be
        /// correlated, for example a given profile would always come with the same avatar. A new per-seat draw
        /// needs a new odd constant here.
        /// </summary>
        const ulong AvatarStream     = 0xA24BAED4963EE407UL;
        const ulong NameEffectStream = 0x2545F4914F6CDD1DUL;

        /// <summary>
        /// The public identity of the bot at <paramref name="seat"/>: <paramref name="displayName"/> plus a drawn
        /// avatar and name effect. When the config has no cosmetics catalogue, the bot gets no cosmetics.
        /// <para>
        /// The frame is always null, as for a tournament bot, because frames are season prizes and not drawn
        /// (<c>docs/cosmetics.md</c>).
        /// </para>
        /// </summary>
        public static PlayerPublicIdentity Identity(SharedGameConfig config, ulong tableSeed, int seat, string displayName) =>
            PlayerPublicIdentity.ForBot(
                EntityId.None,
                displayName,
                avatarId:     CosmeticDraws.PurchasableAvatar(config, Stream(tableSeed, seat, AvatarStream)),
                nameEffectId: CosmeticDraws.RolledNameEffect(config, Stream(tableSeed, seat, NameEffectStream)));

        /// <summary>
        /// The random stream for one seat and one cosmetic slot. The seat is offset by one so that seat 0 still
        /// mixes the key into the seed.
        /// </summary>
        static RandomPCG Stream(ulong tableSeed, int seat, ulong key) =>
            SeedStreams.Stream(tableSeed, seat + 1, key);
    }
}
