using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// The one definition of where a raced segment starts and ends. The run's own result, the opponents it is
    /// placed against, the entries they are drawn from and the browser's provisional placements all measure a
    /// segment through here, so the player and the field are never compared over different stretches of course.
    /// <para>
    /// A segment ends at a climb's bottom torii, where the course takes the runner off the button, and
    /// the next segment starts at that climb's top torii, where it hands them back. The slope and its
    /// run-out belong to no segment: they cost every runner the same and nobody can gain on them, so nothing
    /// that happens between the two torii is raced. The start line opens the first segment and the finish line
    /// closes the last, which makes a course with <c>n</c> climbs a race of <c>n + 1</c> segments.
    /// </para>
    /// </summary>
    public static class TimeTrialSegments
    {
        /// <summary>How many segments a course with <paramref name="climbCount"/> climbs is raced in.</summary>
        public static int CountFor(int climbCount) => climbCount + 1;

        /// <summary>
        /// How long segment <paramref name="segment"/> took, on the clock every crossing time here is measured
        /// on: from the top torii behind it (the start line, for the first) to the bottom torii in front of
        /// it, or the finish line for the last.
        /// </summary>
        public static int TimeMs(IReadOnlyList<int> gateTimesMs, IReadOnlyList<int> climbEntryTimesMs, int finishTimeMs, int segment)
        {
            int start = segment > 0 ? gateTimesMs[segment - 1] : 0;
            int end   = segment < climbEntryTimesMs.Count ? climbEntryTimesMs[segment] : finishTimeMs;
            return end - start;
        }

        /// <summary>
        /// Whether a runner's two lists time both torii of every climb on a course raced in
        /// <paramref name="segmentCount"/> segments, and so can be placed segment by segment against another
        /// runner's: each list holds exactly one time per climb, which is one fewer than the segment count.
        /// </summary>
        public static bool Covers(IReadOnlyList<int> gateTimesMs, IReadOnlyList<int> climbEntryTimesMs, int segmentCount)
            => gateTimesMs != null
            && climbEntryTimesMs != null
            && gateTimesMs.Count == segmentCount - 1
            && climbEntryTimesMs.Count == segmentCount - 1;
    }
}
