namespace Game.Logic
{
    /// <summary>
    /// The buckets a course's spread of times is counted in, shared by the server that counts and the score
    /// screen that draws the counts.
    /// <para>
    /// Buckets are relative to the course's par time rather than absolute milliseconds, so the same bucket
    /// means the same thing on every course however long it is, and a course needs no distribution tuning of
    /// its own. Par is the time the sharpest scripted runner sets (see
    /// <see cref="TimeTrialBotSkill.Sharp"/>).
    /// </para>
    /// </summary>
    public static class CourseTimeBuckets
    {
        /// <summary>Where the ranged buckets start, as a permille of par. Anything faster lands in bucket 0.</summary>
        public const int FirstRangePermille = 980;

        /// <summary>How wide one ranged bucket is, in permille of par.</summary>
        public const int RangeWidthPermille = 10;

        /// <summary>
        /// How many ranged buckets there are; together they cover par ×0.98 to ×1.30, which is the range real
        /// times fall in on a course that is mostly running. Everything outside goes into the open ends.
        /// </summary>
        public const int RangeCount = 32;

        /// <summary>Ranged buckets plus the two open-ended ones at either end.</summary>
        public const int BucketCount = RangeCount + 2;

        /// <summary>The permille of par the lower edge of <paramref name="bucket"/> sits at. Bucket 0 has no lower edge.</summary>
        public static int LowerEdgePermille(int bucket)
            => bucket <= 0 ? 0 : FirstRangePermille + (bucket - 1) * RangeWidthPermille;

        /// <summary>The permille of par the upper edge of <paramref name="bucket"/> sits at. The last bucket has no upper edge.</summary>
        public static int UpperEdgePermille(int bucket)
            => bucket >= BucketCount - 1 ? int.MaxValue : FirstRangePermille + bucket * RangeWidthPermille;

        /// <summary>
        /// Which bucket a time falls in, or -1 if there is no par to measure it against. Times faster than the
        /// first edge and slower than the last both have a bucket of their own, so nothing is ever dropped.
        /// </summary>
        public static int BucketFor(int timeMs, int parTimeMs)
        {
            if (parTimeMs <= 0 || timeMs <= 0)
                return -1;

            int permille = (int)((long)timeMs * 1000 / parTimeMs);
            if (permille < FirstRangePermille)
                return 0;

            int bucket = (permille - FirstRangePermille) / RangeWidthPermille + 1;
            return bucket >= BucketCount ? BucketCount - 1 : bucket;
        }
    }
}
