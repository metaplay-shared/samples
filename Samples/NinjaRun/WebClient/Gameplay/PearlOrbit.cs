using System.Collections.Generic;
using System.Globalization;

namespace WebClient.Gameplay;

/// <summary>
/// The pearls that orbit the main menu medallion, one per Orb: where each starts on the ring and how fast it
/// goes round. The ring's radius is the CSS token <c>--orbit-radius</c> (<c>wwwroot/css/base.css</c>), which the
/// pearls' orbit, their resting position and a landing Orb's stopping point all read.
/// </summary>
public static class PearlOrbit
{
    /// <summary>The ring holds one pearl per Orb up to this many, and glows brighter once it is full.</summary>
    public const int MaxPearls = 40;

    /// <summary>
    /// Where the landed pearls' speed slots start, so a landed pearl never shares a golden pearl's speed draw.
    /// </summary>
    public const int LandedSpeedSlot = 1000;

    const double GoldenAngleDegrees = 137.508;

    /// <summary>One turn of the base orbit, in seconds; css/menu.css <c>.pearl</c> divides the same figure by a pearl's speed.</summary>
    const double OrbitSeconds = 14.0;

    /// <summary>How much faster than the base orbit the quickest pearl goes, as a share of it.</summary>
    const double SpeedSpread = 0.4;

    /// <summary>Where the golden pearl at <paramref name="index"/> starts: the balance the page loaded with steps round by the golden angle.</summary>
    public static double GoldenAngle(int index) => index * GoldenAngleDegrees;

    /// <summary>
    /// The inline style of one pearl (css/menu.css <c>.pearl</c>): it starts at <paramref name="angleDegrees"/>
    /// on the ring, clockwise from straight up, orbits up to <see cref="SpeedSpread"/> faster than the base
    /// orbit by <paramref name="speedSlot"/>'s draw, and breathes out of step by <paramref name="phaseIndex"/>.
    /// </summary>
    public static string Style(double angleDegrees, int phaseIndex, int speedSlot)
    {
        double speed        = 1.0 + SpeedSpread * SpeedDraw(speedSlot);
        double startTurn    = ((angleDegrees % 360.0) + 360.0) % 360.0 / 360.0;

        // The negative delay starts the orbit part-way through, at the start angle. The transform holds the
        // same position still, for reduced motion.
        double delaySeconds = -startTurn * OrbitSeconds / speed;

        string angle     = angleDegrees.ToString("0.###", CultureInfo.InvariantCulture);
        string phase     = (-phaseIndex * 0.21).ToString("0.##", CultureInfo.InvariantCulture);
        string speedText = speed.ToString("0.####", CultureInfo.InvariantCulture);
        string delay     = delaySeconds.ToString("0.###", CultureInfo.InvariantCulture);
        return $"transform: rotate({angle}deg) translateY(calc(-1 * var(--orbit-radius))); --phase: {phase}s; --orbit-speed: {speedText}; --orbit-delay: {delay}s;";
    }

    /// <summary>
    /// A fixed number in [0, 1) for a pearl slot: an integer hash of the slot, so a slot keeps its speed across
    /// renders and page loads while neighbouring slots differ.
    /// </summary>
    static double SpeedDraw(int slot)
    {
        uint hash = unchecked((uint)slot * 2654435761u);
        hash ^= hash >> 16;
        hash  = unchecked(hash * 0x85EBCA6Bu);
        hash ^= hash >> 13;
        hash  = unchecked(hash * 0xC2B2AE35u);
        hash ^= hash >> 16;
        return (hash & 0xFFFFFF) / 16777216.0;
    }

    /// <summary>
    /// Adds an Orb that landed on the ring at <paramref name="angleDegrees"/>, clockwise from straight up, with
    /// <paramref name="displayedBefore"/> Orbs counted before it, to <paramref name="landedAngles"/> (oldest
    /// first) as a pearl of its own. An Orb with no angle, or one landing on a full ring, gets no pearl.
    /// </summary>
    public static void RecordLanding(List<double> landedAngles, int displayedBefore, double? angleDegrees)
    {
        if (angleDegrees is not double angle || displayedBefore >= MaxPearls)
            return;

        // Only the landed pearls the ring still shows are kept, so the one added now is drawn.
        int shownBefore = System.Math.Min(displayedBefore, MaxPearls);
        if (landedAngles.Count > shownBefore)
            landedAngles.RemoveRange(shownBefore, landedAngles.Count - shownBefore);
        landedAngles.Add(angle);
    }
}
