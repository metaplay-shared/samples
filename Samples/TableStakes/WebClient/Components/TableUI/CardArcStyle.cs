using Game.Logic;
using System;
using System.Globalization;

namespace WebClient.Components.TableUI
{
    /// <summary>
    /// Formats a card's position in a fan as CSS custom properties, which the stylesheet turns into a transform.
    /// The geometry comes from <see cref="CardArc"/> in shared code, which is unit-tested.
    /// <para>
    /// The lift is a fraction. The stylesheet sets the length it multiplies, because the viewer's cards are
    /// drawn larger than the other seats' card backs and need a deeper arc.
    /// </para>
    /// </summary>
    public static class CardArcStyle
    {
        /// <summary>The delay before the first card is dealt, after the seats have appeared.</summary>
        const double DealStartSeconds = 0.3;

        /// <summary>The delay between consecutive dealt cards, across all seats.</summary>
        const double DealStepSeconds = 0.028;

        /// <summary>
        /// The style attribute for card <paramref name="index"/> in a fan of <paramref name="count"/> cards at
        /// <paramref name="position"/>: its rotation and lift in the fan, and its deal animation delay.
        /// <para>
        /// Both come from the card's index, because a hand is fanned left to right in deal order.
        /// </para>
        /// </summary>
        public static string For(int index, int count, TableScreenPosition position)
        {
            if (count < 0 || count > MatchRules.CardsPerSeat)
                throw new ArgumentOutOfRangeException(nameof(count), count, $"A fan holds 0..{MatchRules.CardsPerSeat} cards");
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Card index must be in [0, {count})");

            return FanStyles[(int)position][count][index];
        }

        /// <summary>
        /// The style attribute with the entrance animation delay for the plaque at <paramref name="position"/>.
        /// Every seat appears before the first card is dealt.
        /// </summary>
        public static string ForSeat(TableScreenPosition position) => SeatStyles[(int)position];

        // Every string For and ForSeat can return, built once on first use. The set is small (positions x fan
        // sizes x cards), and formatting on demand would allocate on every card on every render.

        static readonly string[][][] FanStyles  = BuildFans();
        static readonly string[]     SeatStyles = BuildSeats();

        static string[][][] BuildFans()
        {
            string[][][] fans = new string[MatchRules.NumSeats][][];
            for (int position = 0; position < MatchRules.NumSeats; position++)
            {
                fans[position] = new string[MatchRules.CardsPerSeat + 1][];
                for (int count = 0; count <= MatchRules.CardsPerSeat; count++)
                {
                    fans[position][count] = new string[count];
                    for (int index = 0; index < count; index++)
                    {
                        CardArcPlacement placement        = CardArc.GetPlacement(index, count);
                        double           dealDelaySeconds = DealStartSeconds
                            + CardArc.GetDealOrder(index, (TableScreenPosition)position) * DealStepSeconds;

                        fans[position][count][index] = string.Format(
                            CultureInfo.InvariantCulture,
                            "--ts-arc-rot: {0:0.##}deg; --ts-arc-lift: {1:0.###}; --ts-deal-delay: {2:0.###}s;",
                            placement.AngleDegrees,
                            placement.LiftFraction,
                            dealDelaySeconds);
                    }
                }
            }
            return fans;
        }

        static string[] BuildSeats()
        {
            string[] seats = new string[MatchRules.NumSeats];
            for (int position = 0; position < MatchRules.NumSeats; position++)
                seats[position] = string.Format(CultureInfo.InvariantCulture, "--ts-seat-delay: {0:0.###}s;", 0.06 + position * 0.07);
            return seats;
        }
    }
}
