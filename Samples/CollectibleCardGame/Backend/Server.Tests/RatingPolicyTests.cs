using Game.Logic;
using Game.Server.Matchmaking;
using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>
    /// How one ranked result moves one rating. The formula is a placeholder that may be replaced outright,
    /// so what is pinned here is the shape the queue depends on rather than the arithmetic: symmetry, the
    /// direction of an upset, and what a fallback-bot match is worth.
    /// </summary>
    [TestFixture]
    public class RatingPolicyTests
    {
        static int K => TestGameConfig.Shared.Global.RatingKFactor;

        static int Delta(int own, int opponent, MatchAccountOutcome outcome) => RatingPolicy.Delta(own, opponent, outcome, K);

        [Test]
        public void EvenlyRatedPlayersSplitTheKFactor()
        {
            Assert.That(Delta(1000, 1000, MatchAccountOutcome.Win), Is.EqualTo(K / 2));
            Assert.That(Delta(1000, 1000, MatchAccountOutcome.Loss), Is.EqualTo(-K / 2));
            Assert.That(Delta(1000, 1000, MatchAccountOutcome.Draw), Is.Zero);
        }

        [Test]
        public void TheMoveIsSymmetricBetweenTheTwoAccounts()
        {
            // What one side gains the other loses, so the pool's total rating is conserved — which is what
            // makes the number an ordering rather than a currency.
            Assert.That(Delta(1200, 1000, MatchAccountOutcome.Win), Is.EqualTo(-Delta(1000, 1200, MatchAccountOutcome.Loss)));
            Assert.That(Delta(1000, 1200, MatchAccountOutcome.Win), Is.EqualTo(-Delta(1200, 1000, MatchAccountOutcome.Loss)));
        }

        [Test]
        public void AnUpsetPaysMoreThanAnExpectedWin()
        {
            int expectedWin = Delta(1400, 1000, MatchAccountOutcome.Win);
            int upsetWin    = Delta(1000, 1400, MatchAccountOutcome.Win);

            Assert.That(upsetWin, Is.GreaterThan(expectedWin));
            Assert.That(expectedWin, Is.GreaterThan(0), "a win never costs rating");
            Assert.That(Delta(1000, 1400, MatchAccountOutcome.Loss), Is.LessThan(0), "and a loss never pays");
        }

        [Test]
        public void ADrawMovesTheFavouriteDownAndTheUnderdogUp()
        {
            // game-design.md's "reduced rating movement" for a draw falls out of the expected score rather than
            // being a second rule beside the formula.
            Assert.That(Delta(1400, 1000, MatchAccountOutcome.Draw), Is.LessThan(0));
            Assert.That(Delta(1000, 1400, MatchAccountOutcome.Draw), Is.GreaterThan(0));
        }

        [Test]
        public void ARealMovementIsNeverRoundedAwayToNothing()
        {
            // Rounded away from zero, so a rating that the formula says should move by a fraction of a point
            // moves by one rather than standing still forever.
            Assert.That(Delta(1400, 1000, MatchAccountOutcome.Draw), Is.LessThanOrEqualTo(-1));
        }

        [Test]
        public void TheFallbackBotCountsAsTheWaitersOwnRating()
        {
            // Rating-neutral in expectation, which is the honest reading of "the queue could not find you an
            // opponent": a win against the fallback pays half the K-factor and a loss costs the same.
            int waiter = 1337;

            Assert.That(Delta(waiter, waiter, MatchAccountOutcome.Win), Is.EqualTo(K / 2));
            Assert.That(Delta(waiter, waiter, MatchAccountOutcome.Loss), Is.EqualTo(-K / 2));
        }

        [Test]
        public void AnAccountAtZeroIsStillOrderedAgainstTheSeededOnes()
        {
            // An account persisted before PlayerRecord.Rating existed reads zero rather than the seed, which
            // is a low rating rather than an unloadable row — so the formula has to answer for it.
            Assert.That(Delta(0, 1000, MatchAccountOutcome.Win), Is.GreaterThan(0));
            Assert.That(Delta(0, 1000, MatchAccountOutcome.Win), Is.LessThanOrEqualTo(K));
        }
    }
}
