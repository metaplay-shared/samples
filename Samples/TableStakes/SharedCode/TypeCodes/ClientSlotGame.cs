using Metaplay.Core.Client;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Registry of game-specific <see cref="ClientSlot"/> values. A client slot links a multiplayer entity to
    /// the client: the server sends an entity's initial state to a slot, and the sub-client that declares the
    /// same slot receives it. Game slots start at 5, above the SDK's core slots.
    /// <para>
    /// Like an <see cref="EntityKindGame"/> value, a slot ID must never change, because the client and the
    /// server both use it in the session start protocol.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class ClientSlotGame : ClientSlot
    {
        /// <summary>The table the player is seated at. Delivers the match model and the player's own hand.</summary>
        public static readonly ClientSlot Match = new ClientSlotGame(5, nameof(Match));

        /// <summary>
        /// The player's seasonal tournament group. Delivers the SDK League division the player participates
        /// in, which the standings are built from (<c>docs/seasonal-tournament.md</c>).
        /// </summary>
        public static readonly ClientSlot Tournament = new ClientSlotGame(6, nameof(Tournament));

        public ClientSlotGame(int id, string name) : base(id, name) { }
    }
}
