using Game.Logic;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Server.MultiplayerEntity;
using Metaplay.Server.MultiplayerEntity.InternalMessages;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Game.Server.Match
{
    /// <summary>
    /// The database row of one table. The payload is the whole model, including the server-only game state, so
    /// a table in progress survives a redeploy or an actor shutdown, and a finished table can still be read.
    /// <para>
    /// The <c>[Table]</c> attribute is enough to register the row, because the SDK scans for
    /// <c>IPersistedItem</c> implementations. Also adding a <c>DbSet</c> for it is an error
    /// (<c>docs/architecture.md</c>, "Database").
    /// </para>
    /// </summary>
    [Table("Matches")]
    public class PersistedMatch : PersistedMultiplayerEntityBase
    {
    }

    /// <summary>
    /// The setup of one seat, chosen by the table's creator: who sits there and how the other seats see them.
    /// The match engine uses only seat indices and never reads this. It is only sent in a setup message and never
    /// stored, so blocking retired member ids is enough and there is no old data to migrate.
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(1, 2)]
    public class MatchSeatSetup
    {
        /// <summary>
        /// How the other seats see this seat: the player's public identity, or only a name for a bot seat. A
        /// bot's cosmetics are drawn by the table from its own seed, not set here (<see cref="BotSeatIdentity"/>).
        /// It is read from the player's model when the seat is reserved, not when they pressed Play.
        /// </summary>
        [MetaMember(3)] public PlayerPublicIdentity Identity { get; private set; }

        MatchSeatSetup() { }

        public MatchSeatSetup(PlayerPublicIdentity identity)
        {
            Identity = identity;
        }

        /// <summary>
        /// Creates the initial <see cref="MatchSeat"/> for this setup. The logic is in
        /// <see cref="MatchSeat.NotYetArrived"/> in shared code, where the shared-code tests can reach it.
        /// </summary>
        public MatchSeat ToSeat(int seat) => MatchSeat.NotYetArrived(seat, Identity);
    }

    /// <summary>
    /// The setup message for a new table: its seats, in seat order. It does not include the deal seed, which
    /// the table's actor generates itself so that no other entity knows it (<c>docs/match.md</c>, "The deal
    /// seed").
    /// </summary>
    [MetaSerializableDerived(200)]
    public class MatchSetupParams : IMultiplayerEntitySetupParams
    {
        public List<MatchSeatSetup> Seats { get; private set; }

        MatchSetupParams() { }

        public MatchSetupParams(List<MatchSeatSetup> seats)
        {
            if (seats == null || seats.Count != MatchRules.NumSeats)
                throw new ArgumentException($"A table has exactly {MatchRules.NumSeats} seats", nameof(seats));
            Seats = seats;
        }
    }
}
