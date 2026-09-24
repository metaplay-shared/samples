using System;
using System.Collections.Generic;

namespace Game.Logic
{
    // Client presentation logic. It lives in SharedCode so Backend/SharedCode.Tests can cover it. The server and
    // the match engine do not use it. The types are not [MetaSerializable] because they are never sent over the
    // network, so changing them does not require regenerating the WASM serializer.

    /// <summary>
    /// The four screen positions a seat can be drawn at. The viewer is always <see cref="South"/>.
    /// <para>
    /// The values are the clockwise offset from South, so <see cref="SeatRotation"/> needs only a modulo.
    /// Clockwise from South on screen is South, West, North, East. Seat indices also increase clockwise, so the
    /// clockwise turn order appears clockwise on screen for every viewer.
    /// </para>
    /// </summary>
    public enum TableScreenPosition
    {
        /// <summary>The bottom of the table. Always the viewer's own seat.</summary>
        South = 0,

        /// <summary>The left of the table: the seat one step clockwise from the viewer, which plays after them.</summary>
        West = 1,

        /// <summary>The top of the table: the seat opposite the viewer.</summary>
        North = 2,

        /// <summary>The right of the table: the seat one step counter-clockwise, which plays before the viewer.</summary>
        East = 3,
    }

    /// <summary>
    /// Maps model seat indices to screen positions and back, rotated so the viewer's seat is South. Each client
    /// rotates differently, and nothing depends on clients agreeing on screen positions.
    /// </summary>
    public static class SeatRotation
    {
        /// <summary>
        /// The value <see cref="MatchModel.OwnSeat"/> returns when this client has no seat: a spectator, or a
        /// player whose hand has not arrived yet.
        /// </summary>
        public const int NoSeat = -1;

        /// <summary>
        /// The seat drawn at <see cref="TableScreenPosition.South"/> when the viewer has no seat.
        /// <para>
        /// The table renders before the hand arrives, so a viewer without a seat is normal and must not throw.
        /// Using seat 0 still gives every seat one position in the correct clockwise order, and the layout switches
        /// to the real rotation when the hand arrives.
        /// </para>
        /// </summary>
        public const int UnseatedViewerSeat = 0;

        /// <summary>The four screen positions in clockwise order, starting at the viewer's.</summary>
        public static readonly IReadOnlyList<TableScreenPosition> ScreenPositionsClockwise = new TableScreenPosition[]
        {
            TableScreenPosition.South,
            TableScreenPosition.West,
            TableScreenPosition.North,
            TableScreenPosition.East,
        };

        /// <summary>Whether <paramref name="ownSeat"/> is a valid seat index.</summary>
        public static bool IsSeated(int ownSeat) => ownSeat >= 0 && ownSeat < MatchRules.NumSeats;

        /// <summary>
        /// The seat drawn at South: the viewer's own seat, or <see cref="UnseatedViewerSeat"/> when they have none.
        /// </summary>
        public static int ResolveViewerSeat(int ownSeat) => IsSeated(ownSeat) ? ownSeat : UnseatedViewerSeat;

        /// <summary>
        /// Where seat <paramref name="seat"/> is drawn for a viewer in <paramref name="ownSeat"/>.
        /// </summary>
        /// <param name="seat">A model seat index, checked with <see cref="MatchRules.ThrowIfInvalidSeat"/>.</param>
        /// <param name="ownSeat">The viewer's own seat, or <see cref="NoSeat"/> if they have none.</param>
        public static TableScreenPosition ToScreenPosition(int seat, int ownSeat)
        {
            MatchRules.ThrowIfInvalidSeat(seat);
            int clockwiseOffset = (seat - ResolveViewerSeat(ownSeat) + MatchRules.NumSeats) % MatchRules.NumSeats;
            return (TableScreenPosition)clockwiseOffset;
        }

        /// <summary>
        /// Which seat is drawn at <paramref name="position"/> for a viewer in <paramref name="ownSeat"/>. The
        /// inverse of <see cref="ToScreenPosition"/>.
        /// </summary>
        /// <param name="position">One of the four screen positions.</param>
        /// <param name="ownSeat">The viewer's own seat, or <see cref="NoSeat"/> if they have none.</param>
        public static int ToSeat(TableScreenPosition position, int ownSeat)
        {
            CheckScreenPosition(position);
            return (ResolveViewerSeat(ownSeat) + (int)position) % MatchRules.NumSeats;
        }

        /// <summary>
        /// The lowercase name of the position, used in CSS class names and in the <c>data-position</c> attribute
        /// that the end-to-end tests locate seats by. Both, and the stylesheet, must use the same spelling.
        /// <c>CssNames</c> is indexed by the enum value.
        /// </summary>
        public static string ToCssName(TableScreenPosition position)
        {
            CheckScreenPosition(position);
            return CssNames[(int)position];
        }

        static readonly string[] CssNames = { "south", "west", "north", "east" };

        static void CheckScreenPosition(TableScreenPosition position)
        {
            if (position < TableScreenPosition.South || position > TableScreenPosition.East)
                throw new ArgumentOutOfRangeException(nameof(position), position, "Not a table screen position");
        }
    }
}
