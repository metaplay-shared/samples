using Game.Logic;
using Metaplay.Core.Model;

namespace Game.Server.CourseGhosts
{
    /// <summary>
    /// How the times run on one course are spread out. Two histograms over the same buckets: every accepted
    /// run, and one entry per player holding their best. The player histogram is the field the score screen
    /// draws a run against; the run histogram is what an operator looks at to see whether a course is too
    /// hard or too easy.
    /// <para>
    /// The best times start with the course's house entries (<see cref="TimeTrialHouseField"/>): the best times
    /// known for the course when its pool is seeded, counted like anyone else's best, so a standing means
    /// something on a course few players have run. They are never moved afterwards and are outnumbered as
    /// players arrive.
    /// </para>
    /// <para>
    /// The buckets are the par-relative <see cref="CourseTimeBuckets"/> the score screen draws with. The cost
    /// is fixed: two integer arrays of <see cref="CourseTimeBuckets.BucketCount"/> entries per course, whatever
    /// the number of runs. Nothing about an individual run is kept here.
    /// </para>
    /// </summary>
    [MetaSerializable]
    public class CourseTimeDistribution
    {
        /// <summary>One entry per accepted run, including repeat runs by the same player.</summary>
        [MetaMember(1)] public int[] RunCounts { get; private set; } = new int[CourseTimeBuckets.BucketCount];

        /// <summary>One entry per player, in the bucket of their best accepted time, and one per house entry.</summary>
        [MetaMember(2)] public int[] PlayerBestCounts { get; private set; } = new int[CourseTimeBuckets.BucketCount];

        [MetaMember(3)] public int RunCount { get; private set; }

        /// <summary>How many best times the course holds: one per player with a time on it, and the house entries.</summary>
        [MetaMember(4)] public int PlayerCount { get; private set; }

        /// <summary>
        /// How many of <see cref="PlayerCount"/> are house entries, counted when the pool was seeded. Zero means
        /// the course has not been given them yet.
        /// </summary>
        [MetaMember(5)] public int HouseCount { get; private set; }

        /// <summary>Count one accepted run.</summary>
        public void AddRun(int timeMs, int parTimeMs)
        {
            int bucket = CourseTimeBuckets.BucketFor(timeMs, parTimeMs);
            if (bucket < 0)
                return;

            RunCounts[bucket]++;
            RunCount++;
        }

        /// <summary>
        /// Count one house entry's best time, exactly as a player's first best is counted. It is not an accepted
        /// run, so <see cref="RunCounts"/> is left alone.
        /// </summary>
        public void AddHouse(int timeMs, int parTimeMs)
        {
            if (CourseTimeBuckets.BucketFor(timeMs, parTimeMs) < 0)
                return;

            MovePlayerBest(0, timeMs, parTimeMs);
            HouseCount++;
        }

        /// <summary>
        /// Move a player from the bucket of their previous best to the bucket of a new one. Pass 0 for
        /// <paramref name="previousBestMs"/> when the player had no time on this course, which is what adds
        /// them to the player count.
        /// </summary>
        public void MovePlayerBest(int previousBestMs, int newBestMs, int parTimeMs)
        {
            int newBucket = CourseTimeBuckets.BucketFor(newBestMs, parTimeMs);
            if (newBucket < 0)
                return;

            int previousBucket = CourseTimeBuckets.BucketFor(previousBestMs, parTimeMs);
            if (previousBucket >= 0 && PlayerBestCounts[previousBucket] > 0)
                PlayerBestCounts[previousBucket]--;
            else if (previousBucket < 0)
                PlayerCount++;

            PlayerBestCounts[newBucket]++;
        }
    }
}
