using Metaplay.Core.Client;
using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Registry for game-specific <see cref="ClientSlot"/> values. A ClientSlot identifies a multiplayer
    /// entity association on the client: the server associates an entity with a slot, and the sub-client that
    /// declares the same slot is the one the state and the updates are delivered to. Core slots use the low
    /// range (1-4), so game slots start well above them.
    /// </summary>
    [MetaSerializable]
    public class ClientSlotGame : ClientSlot
    {
        public ClientSlotGame(int id, string name) : base(id, name) { }

        /// <summary>
        /// The match the player is in. The client is never told to go find a match; it is told which one it
        /// is in, by the association arriving on this slot (<c>Docs/client.md</c>).
        /// </summary>
        public static readonly ClientSlot Match = new ClientSlotGame(20, nameof(Match));
    }
}
