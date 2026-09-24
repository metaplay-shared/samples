using System;

namespace Game.Logic
{
    // Client presentation logic. It lives in SharedCode so the shared-code unit tests can cover it. The types are
    // not [MetaSerializable], and the server and the match engine do not use them.

    /// <summary>
    /// Where one card sits in a fanned hand: its tilt and how high it sits above the ends of the fan.
    /// <para>
    /// The lift is a fraction of the arc depth rather than a pixel value, so the renderer can use one layout
    /// for every hand at the table regardless of card size.
    /// </para>
    /// </summary>
    public readonly struct CardArcPlacement
    {
        /// <summary>The card's tilt, negative to the left of centre and positive to the right.</summary>
        public double AngleDegrees { get; }

        /// <summary>
        /// How far above the fan's outermost cards this card sits, as a fraction of the arc's depth: 1 at the
        /// middle of the fan and 0 at either end.
        /// </summary>
        public double LiftFraction { get; }

        public CardArcPlacement(double angleDegrees, double liftFraction)
        {
            AngleDegrees = angleDegrees;
            LiftFraction = liftFraction;
        }

        public override string ToString() => $"{AngleDegrees:0.##}deg, lift {LiftFraction:0.###}";
    }

    /// <summary>
    /// Lays out a fanned hand: cards tilted evenly around the middle and placed along an arc.
    /// </summary>
    public static class CardArc
    {
        /// <summary>The tilt between neighbouring cards when the hand fits within <see cref="DefaultMaxSpreadDegrees"/>.</summary>
        public const double DefaultStepDegrees = 9.0;

        /// <summary>
        /// The largest angle between the leftmost and rightmost card. When a hand would exceed it at the full
        /// step, <see cref="GetPlacement(int, int, double, double)"/> reduces the step instead.
        /// </summary>
        public const double DefaultMaxSpreadDegrees = 40.0;

        /// <summary>
        /// The card's position in the deal animation. Cards are dealt one at a time, clockwise from the viewer.
        /// The formula relies on <see cref="TableScreenPosition"/>'s values being the clockwise offset from the
        /// viewer. The result is an ordinal, and the renderer converts it to a time.
        /// </summary>
        /// <param name="cardIndex">Which of the seat's cards this is, counting from the first dealt.</param>
        /// <param name="position">The screen position of the seat being dealt to.</param>
        public static int GetDealOrder(int cardIndex, TableScreenPosition position)
        {
            if (cardIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(cardIndex), cardIndex, "A card's place in a hand cannot be negative");

            return cardIndex * MatchRules.NumSeats + (int)position;
        }

        /// <summary>The placement of card <paramref name="index"/> in a hand of <paramref name="count"/>.</summary>
        public static CardArcPlacement GetPlacement(int index, int count)
            => GetPlacement(index, count, DefaultStepDegrees, DefaultMaxSpreadDegrees);

        /// <param name="index">The card's position in the hand, left to right.</param>
        /// <param name="count">How many cards the hand holds.</param>
        /// <param name="stepDegrees">The tilt between neighbouring cards, before the spread cap applies.</param>
        /// <param name="maxSpreadDegrees">The widest the whole fan may open.</param>
        public static CardArcPlacement GetPlacement(int index, int count, double stepDegrees, double maxSpreadDegrees)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "A hand cannot hold fewer than no cards");
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Card index must be 0..{count - 1}");

            // A single card is drawn upright at full lift. This also avoids dividing by zero below.
            if (count == 1)
                return new CardArcPlacement(0.0, 1.0);

            double step   = Math.Min(stepDegrees, maxSpreadDegrees / (count - 1));
            double middle = (count - 1) / 2.0;
            double offset = index - middle;

            // -1 at the left end, 0 at the middle, +1 at the right end. The lift is a parabola over this value,
            // which is close to a circular arc at these small angles.
            double fromMiddle = offset / middle;

            return new CardArcPlacement(offset * step, 1.0 - fromMiddle * fromMiddle);
        }
    }
}
