using System;
using System.Collections.Generic;

namespace Game.Client.Components.Board;

/// <summary> Where one card sits in the fan, relative to the fan's own centre. </summary>
public readonly struct HandCardPlacement
{
    /// <summary> Sideways offset in fan-width units, negative to the left of centre. </summary>
    public readonly double OffsetX;
    /// <summary> Lift in card-height units. Positive is up: the middle of the fan rides highest. </summary>
    public readonly double OffsetY;
    /// <summary> Rotation in degrees, negative anticlockwise. </summary>
    public readonly double RotationDeg;

    public HandCardPlacement(double offsetX, double offsetY, double rotationDeg)
    {
        OffsetX     = offsetX;
        OffsetY     = offsetY;
        RotationDeg = rotationDeg;
    }
}

/// <summary>
/// The hand fan's geometry. A pure function of how many cards are in the hand, so it is unit-tested away from
/// the browser (<c>Docs/client.md</c>, "Testability") — the alternative is a layout nobody can check
/// without looking at it.
/// <para>
/// The shape is an <b>arc</b>: the cards are points on one circle whose centre sits far
/// below the board, so each card's tilt and lift are consequences of where it sits on that circle rather
/// than two curves tuned to look like one. The board then hides the cards' lower parts below its own bottom
/// edge, and the hovered card grows and rises out of the fan.
/// </para>
/// <para>
/// Two authored numbers describe it — how wide the fan may get and how far the outermost card may tilt — and
/// the circle's radius follows from them, which is what makes "the widest fan tilts exactly
/// <see cref="MaxRotationDeg"/> and no fan tilts further" true by construction rather than by clamping. A
/// consequence worth having: a small hand is nearly upright, because two cards on a large circle barely
/// turn, where a tilt proportional to position turned a hand of two as hard as a hand of nine.
/// </para>
/// </summary>
public static class HandFanGeometry
{
    /// <summary> How far apart adjacent cards sit, in card widths, before crowding. </summary>
    public const double Spread = 0.78;

    /// <summary> The widest the whole fan may get, in card widths. Past this, cards overlap more. </summary>
    public const double MaxWidth = 6.4;

    /// <summary> How far the outermost card of the <em>widest</em> fan tilts. No fan tilts further. </summary>
    public const double MaxRotationDeg = 11.0;

    /// <summary> A card is five wide by seven tall, which is what converts a lift between the two units. </summary>
    const double CardAspect = 7.0 / 5.0;

    /// <summary>
    /// The arc's radius in card widths, derived so that the widest fan's outermost card tilts exactly
    /// <see cref="MaxRotationDeg"/>: a chord of <see cref="MaxWidth"/> subtends that angle at the centre.
    /// </summary>
    public static readonly double ArcRadius = MaxWidth / 2.0 / Math.Sin(MaxRotationDeg * Math.PI / 180.0);

    /// <summary> The fan, in hand order. An empty hand fans nothing. </summary>
    public static List<HandCardPlacement> Layout(int cardCount)
    {
        List<HandCardPlacement> placements = new List<HandCardPlacement>(Math.Max(cardCount, 0));
        if (cardCount <= 0)
            return placements;

        if (cardCount == 1)
        {
            placements.Add(new HandCardPlacement(0.0, 0.0, 0.0));
            return placements;
        }

        // Crowd rather than widen once the fan would outgrow the board's rectangle.
        double step   = Math.Min(Spread, MaxWidth / (cardCount - 1));
        double centre = (cardCount - 1) / 2.0;

        // The outermost card's angle. Every lift is measured from it, so the ends of the fan sit at zero and
        // the middle rides highest — which is what makes the fan read as held rather than laid out.
        double edgeAngle = AngleAt((cardCount - 1 - centre) * step);
        double edgeDrop  = Math.Cos(edgeAngle);

        for (int ndx = 0; ndx < cardCount; ndx++)
        {
            double offsetX = (ndx - centre) * step;
            double angle   = AngleAt(offsetX);

            placements.Add(new HandCardPlacement(
                offsetX:     offsetX,
                offsetY:     ArcRadius * (Math.Cos(angle) - edgeDrop) / CardAspect,
                rotationDeg: angle * 180.0 / Math.PI));
        }

        return placements;
    }

    /// <summary> Where a card this far from the fan's centre sits on the arc, in radians. </summary>
    static double AngleAt(double offsetX) => Math.Asin(offsetX / ArcRadius);
}
