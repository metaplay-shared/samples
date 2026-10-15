using Metaplay.Core.Model;

namespace Game.Logic
{
    /// <summary>
    /// Where one accepted run left the player standing on its course: the run's time and the field of best
    /// times it was placed in. Computed by the course's ghost pool on the server from the spread of times it
    /// keeps, and handed to the client with the accepted result so the score screen can draw it.
    /// <para>
    /// It is a snapshot from the moment the run was accepted, not a live rank: it does not change when other
    /// players run afterwards, and it is not a leaderboard position.
    /// </para>
    /// </summary>
    [MetaSerializable]
    [MetaBlockedMembers(3, 4)]
    public class TimeTrialStanding
    {
        [MetaMember(1)] public CourseId CourseId     { get; private set; }
        [MetaMember(2)] public int      FinishTimeMs { get; private set; }

        /// <summary>The par time the course's bands are measured against, in milliseconds.</summary>
        [MetaMember(5)] public int ParTimeMs { get; private set; }

        /// <summary>
        /// One entry per player at their best, in the buckets of <see cref="CourseTimeBuckets"/>, so the score
        /// screen can draw the field the run was placed in. A copy of the pool's own counts taken when the run
        /// was accepted, the player's new best already among them: it sums to the number of players with a best
        /// time on the course, and does not move when other players run afterwards.
        /// <para>
        /// Null when the standing was computed without a distribution to copy, and the score screen then draws
        /// no chart. <see cref="CourseTimeHistogram"/> condenses it into the bars the screen shows.
        /// </para>
        /// </summary>
        [MetaMember(6)] public int[] PlayerBestCounts { get; private set; }

        public TimeTrialStanding() { }

        public TimeTrialStanding(CourseId courseId, int finishTimeMs, int parTimeMs, int[] playerBestCounts = null)
        {
            CourseId         = courseId;
            FinishTimeMs     = finishTimeMs;
            ParTimeMs        = parTimeMs;
            PlayerBestCounts = playerBestCounts;
        }

        public override string ToString() => $"{FinishTimeMs} ms on {CourseId} against par {ParTimeMs} ms";
    }
}
