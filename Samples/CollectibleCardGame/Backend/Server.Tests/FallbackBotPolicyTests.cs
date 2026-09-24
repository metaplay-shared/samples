using Game.Logic;
using Game.Server.Matchmaking;
using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The seat the fill wait hands out. The deck half of it is <c>BotDecks</c> and is pinned by
    /// <c>BotDeckTests</c> — legality, determinism, clan disjointness, Power-Score proximity and rank
    /// clamping — so what is left here is the profile bucketing and the one stakes pairing the shipped helper
    /// cannot express.
    /// </summary>
    [TestFixture]
    public class FallbackBotPolicyTests
    {
        static GlobalConfig Global => TestGameConfig.Shared.Global;

        static MatchmakingBandSchedule Schedule => MatchmakingPolicy.ScheduleWithFillWait(MetaDuration.FromSeconds(45));

        static BotProfileId ProfileFor(int rating) => FallbackBotPolicy.ProfileFor(rating, Global, Schedule);

        [Test]
        public void ThreeBucketsSplitSymmetricallyAroundTheRatingSeed()
        {
            int seed = Global.InitialRating;

            Assert.That(ProfileFor(seed), Is.EqualTo(BotProfileId.Casual), "a fresh account meets the middle bucket");
            Assert.That(ProfileFor(seed - 1000), Is.EqualTo(BotProfileId.Sloppy));
            Assert.That(ProfileFor(seed + 1000), Is.EqualTo(BotProfileId.Practiced));
        }

        [Test]
        public void TheBucketBoundariesAreTheSchedulesOwnWidestFiniteRatingBand()
        {
            // One set of rating thresholds in the codebase rather than two independently tuned ones: the
            // buckets are the schedule's widest bounded band, either side of the seed.
            int seed = Global.InitialRating;
            int band = 250;

            Assert.That(ProfileFor(seed - band), Is.EqualTo(BotProfileId.Casual), "the boundary itself is inside the middle");
            Assert.That(ProfileFor(seed - band - 1), Is.EqualTo(BotProfileId.Sloppy));
            Assert.That(ProfileFor(seed + band), Is.EqualTo(BotProfileId.Casual));
            Assert.That(ProfileFor(seed + band + 1), Is.EqualTo(BotProfileId.Practiced));
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
