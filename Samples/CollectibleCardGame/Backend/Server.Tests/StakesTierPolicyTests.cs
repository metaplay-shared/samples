using Game.Logic;
using Game.Server.Matchmaking;
using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>
    /// The wager, decided at formation. Every case here is a row of game-design.md's stakes table or one of the two
    /// tiers that come from who is in the seats rather than from what is in the decks.
    /// </summary>
    [TestFixture]
    public class StakesTierPolicyTests
    {
        static GlobalConfig Global => TestGameConfig.Shared.Global;

        /// <summary> Both seats well past the shield, so only the gap decides. </summary>
        const int Veteran = 500;

        static StakesTier Tier(
            int powerScore0, int powerScore1, int played0 = Veteran, int played1 = Veteran,
            bool waived0 = false, bool waived1 = false)
            => StakesTierPolicy.ComputeTier(
                new StakesSeatFacts(powerScore0, played0, waived0),
                new StakesSeatFacts(powerScore1, played1, waived1),
                Global);

        [Test]
        public void AGapInsideTheThresholdIsEven()
        {
            Assert.That(Tier(60, 60), Is.EqualTo(StakesTier.Even));
        }

        [Test]
        public void TheThresholdItselfIsStillEven_InBothDirections()
        {
            // Inclusive on the even side, and symmetric: the design calls a matchup even when the gap is
            // *within* ±15, so the boundary must not be the first asymmetric row.
            int threshold = Global.PowerScoreGapThreshold;

            Assert.That(Tier(60 + threshold, 60), Is.EqualTo(StakesTier.Even));
            Assert.That(Tier(60, 60 + threshold), Is.EqualTo(StakesTier.Even));
        }

        [Test]
        public void OnePointPastTheThresholdIsAsymmetric_FramedRelativeToSeatZero()
        {
            int past = Global.PowerScoreGapThreshold + 1;

            Assert.That(Tier(60 + past, 60), Is.EqualTo(StakesTier.Favourite), "seat 0 ahead");
            Assert.That(Tier(60, 60 + past), Is.EqualTo(StakesTier.Underdog), "seat 0 behind");
        }

        // The seat-relative framing — which seat a Favourite or Underdog tier names — is read by the
        // pre-match screen and by the Heist, so it lives in shared code and is pinned beside the payout it
        // feeds, in MatchHeistPolicyTests.

        [Test]
        public void EitherSeatInsideTheShieldShieldsTheMatch()
        {
            // A shield on the other side changes what this player stands to win just as much as a shield on
            // their own changes what they stand to lose, so it is never a private fact about one collection.
            Assert.That(Tier(60, 60, played0: 0, played1: Veteran), Is.EqualTo(StakesTier.Shielded));
            Assert.That(Tier(60, 60, played0: Veteran, played1: 0), Is.EqualTo(StakesTier.Shielded));
        }

        [Test]
        public void TheShieldBeatsAGapThatWouldOtherwiseBeAsymmetric()
        {
            // A shielded match's whole point is that no rank moves in either direction, which has to override
            // what the gap alone would have implied rather than compose with it.
            Assert.That(Tier(120, 25, played0: 0), Is.EqualTo(StakesTier.Shielded));
            Assert.That(Tier(25, 120, played1: 0), Is.EqualTo(StakesTier.Shielded));
        }

        [Test]
        public void TheLastShieldedMatchIsTheOneBeforeTheThreshold()
        {
            // "First N ranked matches" counts matches already played, so the account with N behind it is out.
            int shield = Global.NewcomerShieldMatches;

            Assert.That(Tier(60, 60, played0: shield - 1, played1: Veteran), Is.EqualTo(StakesTier.Shielded));
            Assert.That(Tier(60, 60, played0: shield, played1: Veteran), Is.EqualTo(StakesTier.Even));
        }

        [Test]
        public void AWaivedNewcomerAgainstAVeteranLetsTheGapDecide()
        {
            // The waiver's whole purpose: a fresh playtest account that can see the asymmetric tiers without
            // playing through the newcomer matches first. It lifts the shield and changes nothing else, so the gap
            // decides exactly as it would between two veterans.
            int past = Global.PowerScoreGapThreshold + 1;

            Assert.That(Tier(60, 60, played0: 0, waived0: true), Is.EqualTo(StakesTier.Even));
            Assert.That(Tier(60 + past, 60, played0: 0, waived0: true), Is.EqualTo(StakesTier.Favourite));
            Assert.That(Tier(60, 60 + past, played1: 0, waived1: true), Is.EqualTo(StakesTier.Underdog));
        }

        [Test]
        public void AWaivedNewcomerAgainstAnUnwaivedOneIsStillShielded()
        {
            // A waiver lifts one account's own shield, never the match's: what one player stands to win is
            // what the other stands to lose, so the unwaived newcomer across the table still shields both.
            Assert.That(Tier(60, 60, played0: 0, played1: 0, waived0: true), Is.EqualTo(StakesTier.Shielded));
            Assert.That(Tier(60, 60, played0: 0, played1: 0, waived1: true), Is.EqualTo(StakesTier.Shielded));
        }

        [Test]
        public void TwoWaivedNewcomersLetTheGapDecide()
        {
            // Which is why a two-human playtest has to waive both accounts.
            int past = Global.PowerScoreGapThreshold + 1;

            Assert.That(Tier(60, 60, played0: 0, played1: 0, waived0: true, waived1: true), Is.EqualTo(StakesTier.Even));
            Assert.That(Tier(60 + past, 60, played0: 0, played1: 0, waived0: true, waived1: true),
                Is.EqualTo(StakesTier.Favourite));
        }

        [Test]
        public void AWaiverOnAVeteranChangesNothing()
        {
            // Idempotent against an account that was never shielded, so the dashboard control is safe to
            // leave on rather than something that has to be taken back at the tenth match.
            Assert.That(Tier(60, 60, waived0: true, waived1: true), Is.EqualTo(StakesTier.Even));
        }
    }
}
