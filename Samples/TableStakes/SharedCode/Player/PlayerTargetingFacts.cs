using Metaplay.Core;
using Metaplay.Core.Model;
using System;

namespace Game.Logic
{
    /// <summary>
    /// The checksummed copy of the profile values that the targeting properties read, such as
    /// <see cref="PlayerPropertyGamesPlayed"/>. The source values, <see cref="PlayerModel.Record"/> and
    /// <see cref="PlayerModel.HasCustomizedName"/>, are <c>[NoChecksum]</c> because unsynchronized server actions write
    /// them, so the server and the client can briefly disagree. A client action that evaluates a segment writes
    /// checksummed offer state from the result, so it must read this copy. Only the synchronized server action
    /// <see cref="PlayerTargetingFactsSynced"/> writes it, so it changes at the same timeline position on both sides
    /// (<c>docs/offers.md</c>, "Player properties").
    /// </summary>
    [MetaSerializable]
    public class PlayerTargetingFacts : IEquatable<PlayerTargetingFacts>
    {
        [MetaMember(1)] public int  GamesPlayed       { get; private set; }
        [MetaMember(2)] public int  GamesWon          { get; private set; }
        [MetaMember(3)] public bool HasCustomizedName { get; private set; }

        public PlayerTargetingFacts() { }

        public PlayerTargetingFacts(int gamesPlayed, int gamesWon, bool hasCustomizedName)
        {
            GamesPlayed       = gamesPlayed;
            GamesWon          = gamesWon;
            HasCustomizedName = hasCustomizedName;
        }

        /// <summary>The current values of the player's unchecksummed source members.</summary>
        public static PlayerTargetingFacts Of(PlayerModel player) =>
            new PlayerTargetingFacts(player.Record.GamesPlayed, player.Record.GamesWon, player.HasCustomizedName);

        public bool Equals(PlayerTargetingFacts other) =>
            other != null
            && GamesPlayed == other.GamesPlayed
            && GamesWon == other.GamesWon
            && HasCustomizedName == other.HasCustomizedName;

        public override bool Equals(object obj) => obj is PlayerTargetingFacts other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(GamesPlayed, GamesWon, HasCustomizedName);
    }

    /// <summary>
    /// Sets <see cref="PlayerModel.TargetingFacts"/>. The server enqueues it after each unsynchronized action
    /// that changes one of the source values. The values are in the action's payload, because the client's
    /// source values may differ from the server's at the timeline position where this runs.
    /// </summary>
    [ModelAction(ActionCodes.PlayerTargetingFactsSynced)]
    public class PlayerTargetingFactsSynced : PlayerSynchronizedServerAction
    {
        public PlayerTargetingFacts Facts { get; private set; }

        public PlayerTargetingFactsSynced() { }

        public PlayerTargetingFactsSynced(PlayerTargetingFacts facts)
        {
            Facts = facts;
        }

        public override MetaActionResult Execute(PlayerModel player, bool commit)
        {
            if (commit)
                player.SettleTargetingFacts(Facts);

            return MetaActionResult.Success;
        }
    }
}
