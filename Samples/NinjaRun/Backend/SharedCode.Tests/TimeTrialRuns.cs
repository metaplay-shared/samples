using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Made-up runs for the fixtures that test what is done with a run's times rather than how they are
    /// produced. A run is described by what the design cares about (how long each segment took and how long
    /// the climb between two segments held the runner) and the two lists of torii crossing times are derived
    /// from that, so a fixture never writes them out by hand.
    /// </summary>
    public static class TimeTrialRuns
    {
        /// <summary>
        /// The result a runner who spends <paramref name="segmentTimesMs"/> on the segments and
        /// <paramref name="climbMs"/> between each climb's two torii would record. There is one climb between
        /// every pair of consecutive segments, so a course raced in <c>n</c> segments has <c>n - 1</c> of them.
        /// </summary>
        public static TimeTrialResult ResultOf(IReadOnlyList<int> segmentTimesMs, int climbMs)
        {
            List<int> climbEntryTimesMs = new List<int>();
            List<int> gateTimesMs       = new List<int>();
            int       clock             = 0;

            for (int segment = 0; segment < segmentTimesMs.Count; segment++)
            {
                clock += segmentTimesMs[segment];
                if (segment == segmentTimesMs.Count - 1)
                    break;

                climbEntryTimesMs.Add(clock);
                clock += climbMs;
                gateTimesMs.Add(clock);
            }

            return new TimeTrialResult(clock, steps: 1, gateTimesMs, climbEntryTimesMs);
        }

        /// <summary>The same runner as an opponent to be placed against, with an empty recording behind it.</summary>
        public static TimeTrialGhost GhostOf(CourseId courseId, string displayName, TimeTrialResult run)
            => new TimeTrialGhost(
                courseId,
                TimeTrialConstants.RulesetVersion,
                new TimeTrialRecording(),
                displayName,
                run.FinishTimeMs,
                TimeTrialGhostKind.Player,
                run.GateTimesMs,
                climbEntryTimesMs: run.ClimbEntryTimesMs);

        /// <summary>The same segment time on every segment of a course raced in <paramref name="segmentCount"/> of them.</summary>
        public static List<int> EvenSegments(int segmentCount, int segmentMs)
        {
            List<int> segments = new List<int>();
            for (int segment = 0; segment < segmentCount; segment++)
                segments.Add(segmentMs);
            return segments;
        }
    }
}
