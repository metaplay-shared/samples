using Game.Logic;
using Game.Logic.Tests;
using Game.Server.Matchmaking;
using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The seat the fill wait hands out. The deck half of it is <c>BotDecks</c> and is pinned by
    /// <c>BotDeckTests</c> — clan disjointness, Power-Score proximity and rank
    /// clamping — so what is left here is the profile bucketing and the one stakes pairing the shipped helper
    /// cannot express.
    /// </summary>
    [TestFixture]
    public class FallbackBotPolicyTests
    {
        static GlobalConfig Global => TestGameConfig.Shared.Global;

        static MatchmakingBandSchedule Schedule => MatchmakingPolicy.ScheduleWithFillWait(MetaDuration.FromSeconds(45));

        static BotProfileId ProfileFor(int rating) => FallbackBotPolicy.ProfileFor(rating, Global, Schedule);

        /// <summary>
        /// Three buckets either side of the rating seed, and one set of rating thresholds in the codebase rather
        /// than two independently tuned ones: the boundaries are the schedule's widest bounded band (250), and the
        /// boundary itself is inside the middle bucket.
        /// </summary>
        [TestCase(-1000, BotProfileId.Sloppy)]
        [TestCase(-251, BotProfileId.Sloppy)]
        [TestCase(-250, BotProfileId.Casual)]
        [TestCase(0, BotProfileId.Casual)]
        [TestCase(250, BotProfileId.Casual)]
        [TestCase(251, BotProfileId.Practiced)]
        [TestCase(1000, BotProfileId.Practiced)]
        public void TheBucketsSplitAroundTheRatingSeedAtTheSchedulesWidestBand(int offsetFromSeed, BotProfileId expected)
        {
            Assert.That(ProfileFor(Global.InitialRating + offsetFromSeed), Is.EqualTo(expected));
        }

        [Test]
        public void EveryBucketNamesAProfileThatActuallyMakesMistakes()
        {
            // A fill-wait seat is an opponent and may have a personality — unlike a seat played on behalf of
            // an absent human, which always takes the strongest profile. A bucket that resolved to Strongest
            // would quietly turn the fallback into that.
            foreach (int rating in new[] { 0, 500, 1000, 1500, 5000 })
            {
                BotProfile profile = BotProfiles.Resolve(ProfileFor(rating));
                Assert.That(profile.MakesMistakes, Is.True, $"a waiter at {rating} met {profile.Name}");
            }
        }
    }
}
