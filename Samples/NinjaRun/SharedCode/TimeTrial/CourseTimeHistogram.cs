using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// One bar of the score screen's histogram: how many players have their best in it, and the span of par it
    /// covers. An open-ended bar has no edge on its open side: lower edge 0 at the fast end, upper edge
    /// <see cref="int.MaxValue"/> at the slow end, the same as <see cref="CourseTimeBuckets.LowerEdgePermille"/>
    /// and <see cref="CourseTimeBuckets.UpperEdgePermille"/>.
    /// </summary>
    public readonly struct CourseTimeHistogramBar
    {
        public readonly int  Count;
        public readonly int  LowerEdgePermille;
        public readonly int  UpperEdgePermille;
        public readonly bool IsOpenEnd;

        public CourseTimeHistogramBar(int count, int lowerEdgePermille, int upperEdgePermille, bool isOpenEnd)
        {
            Count             = count;
            LowerEdgePermille = lowerEdgePermille;
            UpperEdgePermille = upperEdgePermille;
            IsOpenEnd         = isOpenEnd;
        }
    }

    /// <summary>
    /// Condenses the <see cref="CourseTimeBuckets.BucketCount"/> player-best buckets a
    /// <see cref="TimeTrialStanding"/> carries into the bars the score screen draws, and says where on that
    /// axis a time sits.
    /// <para>
    /// The window is fixed: bar 0 is the open bucket faster than ×0.98 of par, bars 1 to
    /// <see cref="WindowUpperBucket"/> are the ranged buckets one to one, and the last bar gathers every slower
    /// bucket into one open end. All bars are drawn the same width, so the axis is linear inside the window and
    /// has no scale at the open ends.
    /// </para>
    /// </summary>
    public static class CourseTimeHistogram
    {
        /// <summary>
        /// The last bucket that keeps a bar of its own, up to ×1.14 of par. Real times fall in roughly par to
        /// par + 10%, so a
        /// window that ends a little past that shows the field where it is instead of a row of empty bars out
        /// to ×1.30.
        /// </summary>
        public const int WindowUpperBucket = 16;

        /// <summary>The fast open end, the ranged bars inside the window, and the slow open end.</summary>
        public const int BarCount = WindowUpperBucket + 2;

        /// <summary>
        /// The bars for a standing's <paramref name="playerBestCounts"/>. A null or wrong-length array gives
        /// <see cref="BarCount"/> empty bars, so a caller can always draw the axis.
        /// </summary>
        public static IReadOnlyList<CourseTimeHistogramBar> Bars(int[] playerBestCounts)
        {
            bool valid = playerBestCounts != null && playerBestCounts.Length == CourseTimeBuckets.BucketCount;
            List<CourseTimeHistogramBar> bars = new List<CourseTimeHistogramBar>(BarCount);

            for (int bucket = 0; bucket <= WindowUpperBucket; bucket++)
            {
                bars.Add(new CourseTimeHistogramBar(
                    valid ? playerBestCounts[bucket] : 0,
                    CourseTimeBuckets.LowerEdgePermille(bucket),
                    CourseTimeBuckets.UpperEdgePermille(bucket),
                    isOpenEnd: bucket == 0));
            }

            int slower = 0;
            if (valid)
            {
                for (int bucket = WindowUpperBucket + 1; bucket < CourseTimeBuckets.BucketCount; bucket++)
                    slower += playerBestCounts[bucket];
            }
            bars.Add(new CourseTimeHistogramBar(slower, CourseTimeBuckets.LowerEdgePermille(WindowUpperBucket + 1), int.MaxValue, isOpenEnd: true));

            return bars;
        }

        /// <summary>Which bar <paramref name="timeMs"/> falls in, or -1 if there is no par to measure it against.</summary>
        public static int BarFor(int timeMs, int parTimeMs)
        {
            int bucket = CourseTimeBuckets.BucketFor(timeMs, parTimeMs);
            if (bucket < 0)
                return -1;
            return bucket > WindowUpperBucket ? BarCount - 1 : bucket;
        }

        /// <summary>
        /// Where the marker for <paramref name="timeMs"/> sits along the axis, in permille of its width: 0 is the
        /// fast edge of the first bar and 1000 the slow edge of the last, every bar the same width. Inside a
        /// ranged bar the marker moves linearly with the time; in an open-end bar it sits at the bar's centre,
        /// since the ends have no scale. -1 if there is no par to measure against.
        /// </summary>
        public static int MarkerPermille(int timeMs, int parTimeMs)
        {
            int bar = BarFor(timeMs, parTimeMs);
            if (bar < 0)
                return -1;

            if (bar == 0 || bar == BarCount - 1)
                return (int)((2L * bar + 1) * 1000 / (2L * BarCount));

            int permille = (int)((long)timeMs * 1000 / parTimeMs);
            int lower    = CourseTimeBuckets.LowerEdgePermille(bar);
            int width    = CourseTimeBuckets.UpperEdgePermille(bar) - lower;
            int within   = permille < lower ? 0 : (permille - lower > width ? width : permille - lower);

            return (int)(((long)bar * width + within) * 1000 / ((long)BarCount * width));
        }

        /// <summary>The tallest bar's count, which the screen normalises heights against; 0 when every bar is empty.</summary>
        public static int MaxCount(IReadOnlyList<CourseTimeHistogramBar> bars)
        {
            int max = 0;
            if (bars == null)
                return max;

            for (int ndx = 0; ndx < bars.Count; ndx++)
            {
                if (bars[ndx].Count > max)
                    max = bars[ndx].Count;
            }
            return max;
        }
    }
}
