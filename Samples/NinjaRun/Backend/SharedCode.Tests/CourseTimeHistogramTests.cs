using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The score screen's histogram: how the distribution's buckets condense into bars, and where on that axis
    /// a run's marker is drawn. Pure shared maths, so these run without a server.
    /// </summary>
    [TestFixture]
    public class CourseTimeHistogramTests
    {
        const int Par = 30_000;

        /// <summary>A distinct count in every bucket, so each bar's sum shows which buckets went into it.</summary>
        static int[] NumberedCounts()
        {
            int[] counts = new int[CourseTimeBuckets.BucketCount];
            for (int bucket = 0; bucket < counts.Length; bucket++)
                counts[bucket] = bucket + 1;
            return counts;
        }

        [Test]
        public void BarsCondenseTheBucketsIntoAFixedWindow()
        {
            int[] counts = NumberedCounts();
            IReadOnlyList<CourseTimeHistogramBar> bars = CourseTimeHistogram.Bars(counts);

            Assert.That(bars[0].Count, Is.EqualTo(counts[0]), "bar 0 is the open bucket at the fast end");
            for (int bucket = 1; bucket <= CourseTimeHistogram.WindowUpperBucket; bucket++)
                Assert.That(bars[bucket].Count, Is.EqualTo(counts[bucket]), $"bucket {bucket} keeps a bar of its own");

            int slower = 0;
            for (int bucket = CourseTimeHistogram.WindowUpperBucket + 1; bucket < CourseTimeBuckets.BucketCount; bucket++)
                slower += counts[bucket];
            Assert.That(bars[CourseTimeHistogram.BarCount - 1].Count, Is.EqualTo(slower), "the last bar gathers every bucket past the window");

            int total = 0;
            foreach (CourseTimeHistogramBar bar in bars)
                total += bar.Count;
            int expectedTotal = 0;
            foreach (int count in counts)
                expectedTotal += count;
            Assert.That(total, Is.EqualTo(expectedTotal), "no player is dropped from the chart");
        }

        [Test]
        public void OnlyTheEndBarsAreOpen()
        {
            IReadOnlyList<CourseTimeHistogramBar> bars = CourseTimeHistogram.Bars(NumberedCounts());

            Assert.That(bars[0].IsOpenEnd, Is.True);
            Assert.That(bars[0].LowerEdgePermille, Is.Zero);
            Assert.That(bars[0].UpperEdgePermille, Is.EqualTo(980));

            for (int ndx = 1; ndx < CourseTimeHistogram.BarCount - 1; ndx++)
            {
                Assert.That(bars[ndx].IsOpenEnd, Is.False);
                Assert.That(bars[ndx].LowerEdgePermille, Is.EqualTo(970 + ndx * 10));
                Assert.That(bars[ndx].UpperEdgePermille, Is.EqualTo(980 + ndx * 10));
            }

            CourseTimeHistogramBar last = bars[CourseTimeHistogram.BarCount - 1];
            Assert.That(last.IsOpenEnd, Is.True);
            Assert.That(last.LowerEdgePermille, Is.EqualTo(1140));
            Assert.That(last.UpperEdgePermille, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void AMissingOrMalformedArrayGivesEmptyBars()
        {
            foreach (int[] counts in new int[][] { null, new int[5], new int[CourseTimeBuckets.BucketCount + 1] })
            {
                IReadOnlyList<CourseTimeHistogramBar> bars = CourseTimeHistogram.Bars(counts);
                Assert.That(bars.Count, Is.EqualTo(CourseTimeHistogram.BarCount));
                foreach (CourseTimeHistogramBar bar in bars)
                    Assert.That(bar.Count, Is.Zero);
                Assert.That(CourseTimeHistogram.MaxCount(bars), Is.Zero);
            }
        }

        [Test]
        public void BarForPlacesTimesAgainstPar()
        {
            Assert.That(CourseTimeHistogram.BarFor(Par, Par), Is.EqualTo(3), "par is 1000 permille: (1000 - 980) / 10 + 1");
            Assert.That(CourseTimeHistogram.BarFor(Par / 2, Par), Is.EqualTo(0));
            Assert.That(CourseTimeHistogram.BarFor(Par * 1139 / 1000, Par), Is.EqualTo(16), "the last ranged bar ends at ×1.14");
            Assert.That(CourseTimeHistogram.BarFor(Par * 1140 / 1000, Par), Is.EqualTo(17));
            Assert.That(CourseTimeHistogram.BarFor(Par * 2, Par), Is.EqualTo(17));
            Assert.That(CourseTimeHistogram.BarFor(Par, 0), Is.EqualTo(-1), "no par, no bar");
        }

        [Test]
        public void TheMarkerSitsInsideItsBar()
        {
            int atPar = CourseTimeHistogram.MarkerPermille(Par, Par);
            Assert.That(atPar, Is.InRange(3 * 1000 / 18, 4 * 1000 / 18), "par is drawn inside bar 3");

            Assert.That(CourseTimeHistogram.MarkerPermille(Par / 2, Par), Is.EqualTo(1000 / 36), "the fast end has no scale, so the marker is at bar 0's centre");
            Assert.That(CourseTimeHistogram.MarkerPermille(Par * 2, Par), Is.EqualTo(35 * 1000 / 36), "and at the slow end, bar 17's centre");
            Assert.That(CourseTimeHistogram.MarkerPermille(Par, 0), Is.EqualTo(-1), "no par, no marker");
        }

        [Test]
        public void TheMarkerNeverMovesBackForASlowerTime()
        {
            int previous = -1;
            for (int permille = 400; permille <= 2500; permille++)
            {
                int marker = CourseTimeHistogram.MarkerPermille(Par * permille / 1000, Par);
                Assert.That(marker, Is.InRange(0, 1000));
                Assert.That(marker, Is.GreaterThanOrEqualTo(previous), $"at {permille} permille of par");
                previous = marker;
            }
        }

        [Test]
        public void MaxCountIsTheTallestBar()
        {
            int[] counts = new int[CourseTimeBuckets.BucketCount];
            counts[4] = 7;
            counts[20] = 3;
            counts[30] = 6;

            IReadOnlyList<CourseTimeHistogramBar> bars = CourseTimeHistogram.Bars(counts);

            Assert.That(CourseTimeHistogram.MaxCount(bars), Is.EqualTo(9), "the slow-end bar sums to 9 and outgrows bar 4");
        }
    }
}
