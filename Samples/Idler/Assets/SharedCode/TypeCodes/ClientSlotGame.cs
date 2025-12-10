// This file is part of Metaplay SDK which is released under the Metaplay SDK License.

using Metaplay.Core.Client;
using Metaplay.Core.Model;

namespace Game.Logic.TypeCodes
{
    [MetaSerializable]
    public class ClientSlotGame : ClientSlot
    {
        public ClientSlotGame(int id, string name) : base(id, name) { }

        public static readonly ClientSlot Matchmaker = new ClientSlotGame(11, nameof(Matchmaker));
        public static readonly ClientSlot IdlerLeague = new ClientSlotGame(12, nameof(IdlerLeague));
        public static readonly ClientSlot IdlerPvPLeague = new ClientSlotGame(13, nameof(IdlerPvPLeague));
        public static readonly ClientSlot Party = new ClientSlotGame(14, nameof(Party));

        // Add any game-specific client slots here...

        // public static readonly ClientSlot PvpBattle = new ClientSlotGame(12, nameof(PvpBattle));
    }
}

