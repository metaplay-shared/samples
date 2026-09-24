using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Plays many seeded bot-vs-bot games and asserts the game invariants from <see cref="SelfPlayHarness"/>. Each check
    /// has a negative control that must fail, because a check that inspected nothing would also report no
    /// violations. Every failure message names the seeds, so a failing game can be replayed.
    /// </summary>
    [TestFixture]
    public class SelfPlayTests
    {
        const ulong DealSeedBase = 0x5EED0000UL;
        const ulong BotSeedBase  = 0xB07B07UL;
        const int   NumGames     = 300;

        static ulong DealSeed(int gameNdx) => DealSeedBase + (ulong)gameNdx;
        static ulong BotSeed(int gameNdx)  => BotSeedBase + (ulong)gameNdx * 7919UL;

        /// <summary>A valid game that the negative controls clone and corrupt.</summary>
        static SelfPlayHarness.GameLog PlayReferenceGame()
        {
            SelfPlayHarness.GameLog log = SelfPlayHarness.PlayGame(DealSeed(0), BotSeed(0), SelfPlayHarness.DrawProfiles(DealSeed(0)), MatchTimings.Instant);
            Assert.That(SelfPlayHarness.FindGameInvariantViolations(log), Is.Empty, $"reference game is not clean: {log}");
            return log;
        }

        #region The games

        [Test]
        public void SelfPlay_BulkGamesHoldEveryGameInvariant()
        {
            for (int gameNdx = 0; gameNdx < NumGames; gameNdx++)
            {
                ulong dealSeed = DealSeed(gameNdx);
                ulong botSeed  = BotSeed(gameNdx);

                SelfPlayHarness.GameLog log        = SelfPlayHarness.PlayGame(dealSeed, botSeed, SelfPlayHarness.DrawProfiles(dealSeed), MatchTimings.Default);
                List<string>            violations = SelfPlayHarness.FindGameInvariantViolations(log);

                Assert.That(violations, Is.Empty, $"deal seed {dealSeed}, bot seed {botSeed}: {string.Join("; ", violations)}");
            }
        }

        [Test]
        public void SelfPlay_HoldsTheInvariantsForEveryProfileAtEverySeat()
        {
            foreach (BotProfile profile in TestBotConfig.All)
            {
                for (int gameNdx = 0; gameNdx < 40; gameNdx++)
                {
                    ulong            dealSeed   = DealSeed(gameNdx);
                    ulong            botSeed    = BotSeed(gameNdx);
                    SelfPlayHarness.GameLog log        = SelfPlayHarness.PlayGame(dealSeed, botSeed, SelfPlayHarness.SameProfileAtEverySeat(profile), MatchTimings.Default);
                    List<string>            violations = SelfPlayHarness.FindGameInvariantViolations(log);

                    Assert.That(violations, Is.Empty, $"profile {profile}, deal seed {dealSeed}, bot seed {botSeed}: {string.Join("; ", violations)}");
                }
            }
        }

        [Test]
        public void SelfPlay_GamesAreReproducibleFromTheirSeeds()
        {
            for (int gameNdx = 0; gameNdx < 20; gameNdx++)
            {
                ulong dealSeed = DealSeed(gameNdx);
                ulong botSeed  = BotSeed(gameNdx);

                SelfPlayHarness.GameLog first  = SelfPlayHarness.PlayGame(dealSeed, botSeed, SelfPlayHarness.DrawProfiles(dealSeed), MatchTimings.Default);
                SelfPlayHarness.GameLog second = SelfPlayHarness.PlayGame(dealSeed, botSeed, SelfPlayHarness.DrawProfiles(dealSeed), MatchTimings.Default);

                Assert.That(second.Plays, Is.EqualTo(first.Plays), $"deal seed {dealSeed}, bot seed {botSeed}");
                Assert.That(second.TrickWinnerSeats, Is.EqualTo(first.TrickWinnerSeats), $"deal seed {dealSeed}, bot seed {botSeed}");
                Assert.That(second.ThinkDelays, Is.EqualTo(first.ThinkDelays), $"deal seed {dealSeed}, bot seed {botSeed}");
            }
        }

        [Test]
        public void SelfPlay_DifferentBotSeedsPlayTheSameDealDifferently()
        {
            // Negative control for SelfPlay_GamesAreReproducibleFromTheirSeeds: if the bot seed were ignored,
            // different seeds would produce identical games and that test would still pass.
            ulong dealSeed = DealSeed(3);

            SelfPlayHarness.GameLog a = SelfPlayHarness.PlayGame(dealSeed, 1UL, SelfPlayHarness.SameProfileAtEverySeat(TestBotConfig.TestRandom), MatchTimings.Default);
            SelfPlayHarness.GameLog b = SelfPlayHarness.PlayGame(dealSeed, 2UL, SelfPlayHarness.SameProfileAtEverySeat(TestBotConfig.TestRandom), MatchTimings.Default);

            Assert.That(b.Plays, Is.Not.EqualTo(a.Plays), $"deal seed {dealSeed}: bot seeds 1 and 2 produced the same game");
        }

        [Test]
        public void SelfPlay_APlayedOutTableUsesNoDelayAtAll()
        {
            // A table with no humans left plays out its remaining tricks with MatchTimings.Instant, so every
            // think delay must be zero.
            for (int gameNdx = 0; gameNdx < 20; gameNdx++)
            {
                ulong                   dealSeed = DealSeed(gameNdx);
                SelfPlayHarness.GameLog log      = SelfPlayHarness.PlayGame(dealSeed, BotSeed(gameNdx), SelfPlayHarness.DrawProfiles(dealSeed), MatchTimings.Instant);

                Assert.That(SelfPlayHarness.FindGameInvariantViolations(log), Is.Empty, $"deal seed {dealSeed}");
                Assert.That(log.ThinkDelays, Has.Count.EqualTo(MatchRules.NumPlays), $"deal seed {dealSeed}");
                foreach (MetaDuration delay in log.ThinkDelays)
                    Assert.That(delay, Is.EqualTo(MetaDuration.Zero), $"deal seed {dealSeed}");
            }
        }

        [Test]
        public void SelfPlay_OrdinaryTimingsPaceEveryMoveInsideTheBudget()
        {
            MatchTimings timings = MatchTimings.Default;

            for (int gameNdx = 0; gameNdx < 40; gameNdx++)
            {
                ulong                   dealSeed = DealSeed(gameNdx);
                SelfPlayHarness.GameLog log      = SelfPlayHarness.PlayGame(dealSeed, BotSeed(gameNdx), SelfPlayHarness.DrawProfiles(dealSeed), timings);

                foreach (MetaDuration delay in log.ThinkDelays)
                {
                    Assert.That(delay, Is.GreaterThanOrEqualTo(timings.BotThinkDelayMin), $"deal seed {dealSeed}");
                    Assert.That(delay, Is.LessThanOrEqualTo(timings.BotThinkDelayOccasionalMax), $"deal seed {dealSeed}");
                }
            }
        }

        #endregion

        #region Negative controls

        [Test]
        public void NegativeControl_ACheatingChooserIsCaughtAndRefused()
        {
            // The illegal-card check is only meaningful if it catches an illegal card. This chooser plays an
            // illegal card from its hand whenever it holds one.
            int cheatsOffered = 0;

            SelfPlayHarness.GameLog log = SelfPlayHarness.PlayGame(DealSeed(11), BotSeed(11), SelfPlayHarness.DrawProfiles(DealSeed(11)), MatchTimings.Instant, (MatchSeatView view) =>
            {
                List<Card> legal = view.GetLegalPlays();
                foreach (Card card in view.Hand)
                {
                    if (!SelfPlayHarness.Contains(legal, card))
                    {
                        cheatsOffered++;
                        return card;
                    }
                }
                return legal[0];
            });

            Assert.That(cheatsOffered, Is.GreaterThan(0), $"deal seed {DealSeed(11)}: the position never offered an illegal card, so the control proves nothing");
            Assert.That(log.IllegalOffers, Is.Not.Empty, $"deal seed {DealSeed(11)}: the harness did not notice an illegal card");
            Assert.That(log.Refusals, Does.Contain(MoveRefusalReason.MustFollowSuit), $"deal seed {DealSeed(11)}: the engine did not refuse an illegal card");
            Assert.That(SelfPlayHarness.FindGameInvariantViolations(log), Is.Not.Empty, $"deal seed {DealSeed(11)}: the invariant check passed a cheating game");
        }

        [Test]
        public void NegativeControl_AGameMissingAPlayFailsTheCardCount()
        {
            SelfPlayHarness.GameLog broken = PlayReferenceGame().Clone();
            broken.Plays.RemoveAt(broken.Plays.Count - 1);

            Assert.That(SelfPlayHarness.FindGameInvariantViolations(broken), Is.Not.Empty, $"{broken}: a nineteen-card game passed");
        }

        [Test]
        public void NegativeControl_AGameWithACardPlayedTwiceFails()
        {
            SelfPlayHarness.GameLog broken = PlayReferenceGame().Clone();
            broken.Plays[19] = new PlayRecord(broken.Plays[19].Seat, broken.Plays[0].Card);

            Assert.That(SelfPlayHarness.FindGameInvariantViolations(broken), Is.Not.Empty, $"{broken}: a game playing one card twice passed");
        }

        [Test]
        public void NegativeControl_TricksThatDoNotSumToFiveFail()
        {
            SelfPlayHarness.GameLog broken = PlayReferenceGame().Clone();
            broken.TrickWinnerSeats.RemoveAt(broken.TrickWinnerSeats.Count - 1);

            Assert.That(SelfPlayHarness.FindGameInvariantViolations(broken), Is.Not.Empty, $"{broken}: four tricks passed as five");
        }

        [Test]
        public void NegativeControl_ATrickCreditedToTheWrongSeatFails()
        {
            SelfPlayHarness.GameLog reference = PlayReferenceGame();
            SelfPlayHarness.GameLog broken    = reference.Clone();
            broken.TrickWinnerSeats[0] = (reference.TrickWinnerSeats[0] + 1) % MatchRules.NumSeats;

            Assert.That(SelfPlayHarness.FindGameInvariantViolations(broken), Is.Not.Empty, $"{broken}: a misattributed trick passed");
        }

        [Test]
        public void NegativeControl_StandingsThatAreNotATotalOrderFail()
        {
            SelfPlayHarness.GameLog reference = PlayReferenceGame();

            // The same seat appears in two positions.
            SelfPlayHarness.GameLog duplicatedSeat = reference.Clone();
            duplicatedSeat.Standings[1] = new SeatStanding(1, reference.Standings[0].Seat, reference.Standings[0].TricksWon, reference.Standings[0].LastTrickWonIndex, reference.Standings[0].SeparatedFromNextBy);
            Assert.That(SelfPlayHarness.FindGameInvariantViolations(duplicatedSeat), Is.Not.Empty, $"{duplicatedSeat}: standings naming one seat twice passed");

            // Reversed standings put the worse seat ahead, which the ordering check must reject.
            SelfPlayHarness.GameLog reversed = reference.Clone();
            reversed.Standings.Reverse();
            for (int ndx = 0; ndx < reversed.Standings.Count; ndx++)
            {
                SeatStanding standing = reversed.Standings[ndx];
                reversed.Standings[ndx] = new SeatStanding(ndx, standing.Seat, standing.TricksWon, standing.LastTrickWonIndex, standing.SeparatedFromNextBy);
            }
            Assert.That(SelfPlayHarness.FindGameInvariantViolations(reversed), Is.Not.Empty, $"{reversed}: reversed standings passed");

            // A seat credited tricks it did not win.
            SelfPlayHarness.GameLog miscounted = reference.Clone();
            SeatStanding            winner     = reference.Standings[0];
            miscounted.Standings[0] = new SeatStanding(0, winner.Seat, winner.TricksWon + 1, winner.LastTrickWonIndex, winner.SeparatedFromNextBy);
            Assert.That(SelfPlayHarness.FindGameInvariantViolations(miscounted), Is.Not.Empty, $"{miscounted}: miscounted standings passed");
        }

        [Test]
        public void NegativeControl_AStandingThatNamesTheWrongReasonFails()
        {
            // The results screen shows SeparatedFromNextBy as stored and does not recompute it, so this check is
            // the only thing that catches a wrong value. The reference game's winner is ahead on tricks (asserted
            // below), so claiming it is ahead on the more recent trick is wrong.
            SelfPlayHarness.GameLog reference = PlayReferenceGame();
            SelfPlayHarness.GameLog broken    = reference.Clone();
            SeatStanding            winner    = reference.Standings[0];

            Assert.That(winner.SeparatedFromNextBy, Is.EqualTo(StandingSeparation.MoreTricks),
                "the reference game's winner is not ahead on tricks, so this control tests nothing");

            broken.Standings[0] = new SeatStanding(0, winner.Seat, winner.TricksWon, winner.LastTrickWonIndex, StandingSeparation.MoreRecentTrick);
            Assert.That(SelfPlayHarness.FindGameInvariantViolations(broken), Is.Not.Empty, $"{broken}: a standing naming the wrong reason passed");

            // The last position has no position below it, so any separation on it is wrong.
            SelfPlayHarness.GameLog trailing = reference.Clone();
            SeatStanding            last     = reference.Standings[MatchRules.NumSeats - 1];
            trailing.Standings[MatchRules.NumSeats - 1] = new SeatStanding(last.Position, last.Seat, last.TricksWon, last.LastTrickWonIndex, StandingSeparation.MoreTricks);
            Assert.That(SelfPlayHarness.FindGameInvariantViolations(trailing), Is.Not.Empty, $"{trailing}: a separation on the last seat passed");
        }

        [Test]
        public void NegativeControl_APlayFromTheWrongSeatFails()
        {
            SelfPlayHarness.GameLog reference = PlayReferenceGame();
            SelfPlayHarness.GameLog broken    = reference.Clone();
            broken.Plays[1] = new PlayRecord((reference.Plays[1].Seat + 1) % MatchRules.NumSeats, reference.Plays[1].Card);

            Assert.That(SelfPlayHarness.FindGameInvariantViolations(broken), Is.Not.Empty, $"{broken}: a card played out of turn order passed");
        }

        #endregion
    }
}
