using Metaplay.Core;
using Metaplay.Core.Model;
using System;

namespace Game.Logic
{
    /// <summary>
    /// How a player appears to other players: id, display name, and equipped cosmetics. A live player's identity is
    /// built only in <see cref="PlayerModel.BuildPublicIdentity"/>, so every screen shows a player the same way.
    /// <para>
    /// It is computed from the model, not stored, so a holder of an identity holds a snapshot. The tournament
    /// division refreshes its copies on <see cref="IPlayerModelServerListener.OnPublicIdentityChanged"/>, because it
    /// shows them for a whole season. A table keeps the snapshot from when the player sat down. Every member is sent
    /// to other players, so add only data that is safe to show to them.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class PlayerPublicIdentity
    {
        /// <summary>
        /// The player's entity id. Lists such as standings and table seats use it to identify each row, for
        /// example to highlight the local player.
        /// <para>
        /// It is never displayed (<c>docs/player.md</c>). The match already puts the same id on every seat.
        /// </para>
        /// </summary>
        [MetaMember(1)] public EntityId PlayerId { get; private set; }

        /// <summary>
        /// The player's display name, in the canonical form the server accepted.
        /// <para>
        /// Render it only as text: never as markup, a CSS class, part of a URL, or an analytics dimension. This
        /// also applies where a name effect styles the name (<c>docs/cosmetics.md</c>).
        /// </para>
        /// </summary>
        [MetaMember(2)] public string DisplayName { get; private set; }

        /// <summary>
        /// The equipped avatar, or null if none is equipped. For null the client draws its built-in default
        /// avatar.
        /// </summary>
        [MetaMember(3)] public CosmeticId AvatarId { get; private set; }

        /// <summary>
        /// The equipped avatar frame, or null if none is equipped. For null the client draws its built-in
        /// default frame.
        /// </summary>
        [MetaMember(4)] public CosmeticId FrameId { get; private set; }

        /// <summary>
        /// The equipped name effect, or null for plain text.
        /// <para>
        /// The client takes the style from the item's catalogue entry, never from player input, and still draws
        /// the name as text (<c>docs/cosmetics.md</c>).
        /// </para>
        /// </summary>
        [MetaMember(5)] public CosmeticId NameEffectId { get; private set; }

        public PlayerPublicIdentity() { }

        /// <summary>
        /// Used by <see cref="PlayerModel.BuildPublicIdentity"/> and <see cref="ForSeat"/>. It is
        /// <c>internal</c> rather than public so code outside this assembly cannot build an identity from loose
        /// fields.
        /// </summary>
        internal PlayerPublicIdentity(EntityId playerId, string displayName, CosmeticId avatarId, CosmeticId frameId, CosmeticId nameEffectId)
        {
            PlayerId     = playerId;
            DisplayName  = displayName;
            AvatarId     = avatarId;
            FrameId      = frameId;
            NameEffectId = nameEffectId;
        }

        /// <summary>
        /// Builds an identity from fields, for a seat with no player model.
        /// <para>
        /// It is <c>internal</c> for the same reason as the constructor. Production code uses the public
        /// <see cref="ForBot"/>, which cannot set a frame. Test fixtures that set up tables without players call
        /// this method directly (<c>SharedCode/AssemblyInfo.cs</c>).
        /// </para>
        /// </summary>
        internal static PlayerPublicIdentity ForSeat(EntityId playerId, string displayName, CosmeticId avatarId = null, CosmeticId frameId = null, CosmeticId nameEffectId = null) =>
            new PlayerPublicIdentity(playerId, displayName, avatarId, frameId, nameEffectId);

        /// <summary>
        /// The identity of a bot seat: a tournament bot (<see cref="TournamentBots"/>) or a bot at a table
        /// (<see cref="BotSeatIdentity"/>).
        /// <para>
        /// <paramref name="avatarId"/> and <paramref name="nameEffectId"/> come from the caller's seeded draws
        /// from the catalogue. <paramref name="nameEffectId"/> is null for plain text. The frame is always null,
        /// because bots are never given one. Whether a seat is a bot is stored next to the identity
        /// (<see cref="TournamentEntrant.IsBot"/>, <see cref="MatchSeat.Occupancy"/>), never derived from it.
        /// </para>
        /// </summary>
        public static PlayerPublicIdentity ForBot(EntityId botPlayerId, string displayName, CosmeticId avatarId, CosmeticId nameEffectId = null) =>
            ForSeat(botPlayerId, displayName, avatarId: avatarId, frameId: null, nameEffectId: nameEffectId);

        /// <summary>
        /// Value equality over every field. A holder of a snapshot compares it with a freshly built identity to
        /// decide whether anything changed. Each build creates a new object, so reference equality would always
        /// report a change.
        /// </summary>
        public override bool Equals(object obj) =>
            obj is PlayerPublicIdentity other
            && PlayerId == other.PlayerId
            && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
            && Equals(AvatarId, other.AvatarId)
            && Equals(FrameId, other.FrameId)
            && Equals(NameEffectId, other.NameEffectId);

        public override int GetHashCode() =>
            (PlayerId, DisplayName, AvatarId?.Value, FrameId?.Value, NameEffectId?.Value).GetHashCode();

        public override string ToString() => $"{DisplayName} ({PlayerId})";
    }
}
