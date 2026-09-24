using Game.Logic;
using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The Heist's own rules, as pure functions: what a tier owes, what is left on the menu, what an absent
    /// winner takes, what one pick does to one collection, and what the table's next step is.
    /// <para>
    /// <c>MatchResultRecordTests</c> owns the <em>eligibility subtraction</em> and the outcome record's own
    /// shape; this fixture owns everything the phase decides on top of them. The split is deliberate: those
    /// eight eligibility rows are the rules half, and a rewrite that broke one of them should fail where
    /// they were written.
    /// </para>
    /// <para>
    /// There is no actor harness here and there is deliberately not going to be one, so the phase's ordering
    /// rules are reachable only because
    /// <see cref="MatchHeistPolicy.Resolve"/> is pure. Every case below is one the actor cannot decide for
    /// itself.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchHeistPolicyTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        GlobalConfig Global => Config.Global;

        static CardId Card(string id) => CardId.FromString(id);

        static HeistEligibleCard Row(string id, int rank) => new HeistEligibleCard(Card(id), rank);

        static List<HeistEligibleCard> Menu(params HeistEligibleCard[] rows) => new List<HeistEligibleCard>(rows);

        static List<CardId> Cards(params string[] ids)
        {
            List<CardId> cards = new List<CardId>();
            foreach (string id in ids)
                cards.Add(Card(id));
            return cards;
        }

        static MatchStakes Ranked(StakesTier tier)
            => new MatchStakes(isRanked: true, tier, new List<int> { 60, 60 },
                new List<List<CardId>> { new List<CardId>(), new List<CardId>() });

        static MatchOutcomeRecord Won(int seat)
            => new MatchOutcomeRecord(
                seat == 0 ? MatchOutcome.Seat0Wins : MatchOutcome.Seat1Wins,
                seat, finalTurn: 12, MatchEndCause.DenAtZero,
                new List<bool> { true, true }, wasRanked: true, decidedAt: MetaTime.Now);

        // ---------------------------------------------------------------- the winner-relative tier

        [Test]
        public void TheSeatRelativeFramingReadsTheSameFactFromBothSeats()
        {
            // The tier is one shared field frozen at formation and both clients render the identical record;
            // only the copy's framing differs per local seat. This is that table, one row per case.
            Assert.That(MatchHeistPolicy.SeatIsTheFavourite(StakesTier.Favourite, 0), Is.True);
            Assert.That(MatchHeistPolicy.SeatIsTheFavourite(StakesTier.Favourite, 1), Is.False);
            Assert.That(MatchHeistPolicy.SeatIsTheFavourite(StakesTier.Underdog, 0), Is.False);
            Assert.That(MatchHeistPolicy.SeatIsTheFavourite(StakesTier.Underdog, 1), Is.True);

            // The three tiers that name no favourite name none from either seat.
            foreach (StakesTier tier in new[] { StakesTier.Even, StakesTier.Practice, StakesTier.Shielded })
            {
                Assert.That(MatchHeistPolicy.SeatIsTheFavourite(tier, 0), Is.False, $"{tier} from seat 0");
                Assert.That(MatchHeistPolicy.SeatIsTheFavourite(tier, 1), Is.False, $"{tier} from seat 1");
            }
        }

        [Test]
        public void ThePayoutTableIsSixRowsAndPunchingDownPaysNothing()
        {
            // game-design.md's asymmetric-stakes table, read from the winner's side: even is one pick, the
            // favourite's win takes nothing at all, and the upset takes two.
            Assert.That(MatchHeistPolicy.PicksOwed(StakesTier.Even, winnerWasTheFormationFavourite: false), Is.EqualTo(1));
            Assert.That(MatchHeistPolicy.PicksOwed(StakesTier.Even, winnerWasTheFormationFavourite: true), Is.EqualTo(1),
                "an even matchup names no favourite, so the flag cannot change its payout");

            Assert.That(MatchHeistPolicy.PicksOwed(StakesTier.Favourite, winnerWasTheFormationFavourite: true), Is.EqualTo(0));
            Assert.That(MatchHeistPolicy.PicksOwed(StakesTier.Favourite, winnerWasTheFormationFavourite: false), Is.EqualTo(2));
            Assert.That(MatchHeistPolicy.PicksOwed(StakesTier.Underdog, winnerWasTheFormationFavourite: true), Is.EqualTo(0));
            Assert.That(MatchHeistPolicy.PicksOwed(StakesTier.Underdog, winnerWasTheFormationFavourite: false), Is.EqualTo(2));
        }

        [Test]
        public void PracticeAndTheShieldOweNothingWhoeverWins()
        {
            foreach (StakesTier tier in new[] { StakesTier.Practice, StakesTier.Shielded })
            {
                Assert.That(MatchHeistPolicy.PicksOwed(tier, winnerWasTheFormationFavourite: true), Is.EqualTo(0), $"{tier}");
                Assert.That(MatchHeistPolicy.PicksOwed(tier, winnerWasTheFormationFavourite: false), Is.EqualTo(0), $"{tier}");
            }
        }

        [Test]
        public void AMatchWithNoResultYetOwesNoPicks()
        {
            // The phase decision is asked at the moment the result lands, and the actor asks it again on every
            // pick — so a null result has to answer zero rather than throwing.
            Assert.That(MatchHeistPolicy.PicksOwedForMatch(Ranked(StakesTier.Even), null), Is.EqualTo(0));
            Assert.That(MatchHeistPolicy.PicksOwedForMatch(null, Won(0)), Is.EqualTo(0));
        }

        [Test]
        public void AnUnrankedMatchAndADrawOweNoPicks()
        {
            MatchStakes practice = MatchStakes.Practice(
                new List<int> { 25, 25 }, new List<List<CardId>> { new List<CardId>(), new List<CardId>() });

            Assert.That(MatchHeistPolicy.PicksOwedForMatch(practice, Won(0)), Is.EqualTo(0), "practice puts no ranks at stake");

            MatchOutcomeRecord drawn = new MatchOutcomeRecord(
                MatchOutcome.Draw, MatchSeats.None, 20, MatchEndCause.BothDensAtZero,
                new List<bool> { true, true }, wasRanked: true, decidedAt: MetaTime.Now);

            Assert.That(MatchHeistPolicy.PicksOwedForMatch(Ranked(StakesTier.Even), drawn), Is.EqualTo(0), "neither seat is the loser");
        }

        [Test]
        public void TheWinningSeatIsWhatTurnsAFrozenTierIntoAPayout()
        {
            // Favourite means SEAT 0 entered ahead. So seat 0 winning pays nothing and seat 1 winning is the
            // upset — which is the whole reason the tier is stored seat-relative rather than winner-relative.
            Assert.That(MatchHeistPolicy.PicksOwedForMatch(Ranked(StakesTier.Favourite), Won(0)), Is.EqualTo(0));
            Assert.That(MatchHeistPolicy.PicksOwedForMatch(Ranked(StakesTier.Favourite), Won(1)), Is.EqualTo(2));
            Assert.That(MatchHeistPolicy.PicksOwedForMatch(Ranked(StakesTier.Underdog), Won(1)), Is.EqualTo(0));
            Assert.That(MatchHeistPolicy.PicksOwedForMatch(Ranked(StakesTier.Underdog), Won(0)), Is.EqualTo(2));
        }

        [Test]
        public void APhaseRunsOnlyWhereAPickIsOwedAndThereIsSomethingToPick()
        {
            List<HeistEligibleCard> menu = Menu(Row("EmberKit", 3));

            Assert.That(MatchHeistPolicy.PhaseRuns(Ranked(StakesTier.Even), Won(0), menu), Is.True);
            Assert.That(MatchHeistPolicy.PhaseRuns(Ranked(StakesTier.Shielded), Won(0), menu), Is.False, "the shield moves nothing");
            Assert.That(MatchHeistPolicy.PhaseRuns(Ranked(StakesTier.Even), Won(0), new List<HeistEligibleCard>()), Is.False,
                "an empty menu is a screen with nothing to ask");
            Assert.That(MatchHeistPolicy.PhaseRuns(Ranked(StakesTier.Even), Won(0), null), Is.False);
        }

        // ---------------------------------------------------------------- the menu, by occurrence

        [Test]
        public void APickTakesOneOccurrenceAndNotEveryCopy()
        {
            // Duplicates deliberately survive the eligibility subtraction — a seat that played two copies
            // offers two — so taking one by card id would take both off the menu at once.
            List<HeistEligibleCard> menu = Menu(Row("EmberKit", 3), Row("AcornHoard", 2), Row("EmberKit", 3));

            List<HeistEligibleCard> left = MatchHeistPolicy.Remaining(menu, Cards("EmberKit"));

            Assert.That(left.Count, Is.EqualTo(2));
            Assert.That(left[0].Card, Is.EqualTo(Card("AcornHoard")));
            Assert.That(left[1].Card, Is.EqualTo(Card("EmberKit")), "the second copy is still on the menu");
        }

        [Test]
        public void APickOfSomethingNotOnTheMenuTakesNothingOffIt()
        {
            List<HeistEligibleCard> menu = Menu(Row("EmberKit", 3));

            Assert.That(MatchHeistPolicy.Remaining(menu, Cards("GreyOwl")).Count, Is.EqualTo(1));
            Assert.That(MatchHeistPolicy.Remaining(menu, null).Count, Is.EqualTo(1));
            Assert.That(MatchHeistPolicy.Remaining(null, Cards("EmberKit")), Is.Empty);
        }

        // ---------------------------------------------------------------- the auto-default

        [Test]
        public void TheDefaultTakesTheHighestRankedCard()
        {
            List<HeistEligibleCard> menu = Menu(Row("EmberKit", 2), Row("GardenSnail", 5), Row("GreyOwl", 4));

            Assert.That(MatchHeistPolicy.AutoDefault(menu, Config), Is.EqualTo(Card("GardenSnail")),
                "rank comes first, however cheap the card is");
        }

        [Test]
        public void RankTiesBreakOnRarityAndThenOnCost()
        {
            // AcornForager is Rare at cost 3; BubbleDrifter is Common at the same cost.
            Assert.That(
                MatchHeistPolicy.AutoDefault(Menu(Row("BubbleDrifter", 3), Row("AcornForager", 3)), Config),
                Is.EqualTo(Card("AcornForager")), "higher rarity first");

            // Both Common: GreyOwl costs 5 and EmberKit costs 1.
            Assert.That(
                MatchHeistPolicy.AutoDefault(Menu(Row("EmberKit", 3), Row("GreyOwl", 3)), Config),
                Is.EqualTo(Card("GreyOwl")), "then the more expensive body");
        }

        [Test]
        public void TheFinalTieBreakIsCanonicalCardOrderAndNotCatalogueOrder()
        {
            // SmokeBomb and MeadowMouse are both Common at cost 1, and their catalogue order is the opposite
            // of their ordinal order — so a switch to a config-iteration order would fail here rather than
            // pass by accident. The answer must also not depend on the order the menu happens to be in.
            Assert.That(MatchHeistPolicy.AutoDefault(Menu(Row("SmokeBomb", 3), Row("MeadowMouse", 3)), Config),
                Is.EqualTo(Card("MeadowMouse")));
            Assert.That(MatchHeistPolicy.AutoDefault(Menu(Row("MeadowMouse", 3), Row("SmokeBomb", 3)), Config),
                Is.EqualTo(Card("MeadowMouse")));
        }

        [Test]
        public void ASingleCandidateNeedsNoTieBreakAndAnEmptyMenuAnswersNothing()
        {
            Assert.That(MatchHeistPolicy.AutoDefault(Menu(Row("EmberKit", 1)), Config), Is.EqualTo(Card("EmberKit")));
            Assert.That(MatchHeistPolicy.AutoDefault(new List<HeistEligibleCard>(), Config), Is.Null);
            Assert.That(MatchHeistPolicy.AutoDefault(null, Config), Is.Null);
        }

        // ---------------------------------------------------------------- the transfer, one side at a time

        [Test]
        public void TheWinnerGainsARankBelowTheCeilingAndNothingAtIt()
        {
            HeistRankMove gained = MatchHeistPolicy.WinnerGain(owns: true, rank: 3, locked: false, Global);
            Assert.That(gained.Kind, Is.EqualTo(HeistMove.Moved));
            Assert.That(gained.From, Is.EqualTo(3));
            Assert.That(gained.To, Is.EqualTo(4));

            HeistRankMove capped = MatchHeistPolicy.WinnerGain(owns: true, rank: Global.RankMax, locked: false, Global);
            Assert.That(capped.Kind, Is.EqualTo(HeistMove.AtCeiling));
            Assert.That(capped.Moves, Is.False, "nothing above rank five, and nothing paid instead");
        }

        [Test]
        public void AWinnerWhoOwnsNoCopyIsGivenOneAtTheFloor()
        {
            HeistRankMove acquired = MatchHeistPolicy.WinnerGain(owns: false, rank: 0, locked: false, Global);

            Assert.That(acquired.Kind, Is.EqualTo(HeistMove.Acquired));
            Assert.That(acquired.From, Is.EqualTo(0));
            Assert.That(acquired.To, Is.EqualTo(Global.RankMin));
        }

        [Test]
        public void AFrozenCopyMovesOnNeitherSide()
        {
            // A lock runs both ways, and it is read live at the moment the transfer applies rather than off
            // the snapshot the pick was filtered against — which is what makes the promise hold on a retry
            // that lands after the owner locked the card.
            Assert.That(MatchHeistPolicy.WinnerGain(owns: true, rank: 2, locked: true, Global).Moves, Is.False);
            Assert.That(MatchHeistPolicy.LoserLoss(owns: true, rank: 4, locked: true, Global).Moves, Is.False);
            Assert.That(MatchHeistPolicy.LoserLoss(owns: true, rank: 4, locked: true, Global).Kind, Is.EqualTo(HeistMove.Frozen));
        }

        [Test]
        public void TheLoserLosesARankAboveTheFloorAndNothingAtIt()
        {
            HeistRankMove lost = MatchHeistPolicy.LoserLoss(owns: true, rank: 3, locked: false, Global);
            Assert.That(lost.Kind, Is.EqualTo(HeistMove.Moved));
            Assert.That(lost.To, Is.EqualTo(2));

            HeistRankMove floored = MatchHeistPolicy.LoserLoss(owns: true, rank: Global.RankMin, locked: false, Global);
            Assert.That(floored.Kind, Is.EqualTo(HeistMove.AtFloor));
            Assert.That(floored.To, Is.EqualTo(Global.RankMin), "cards are never removed from a collection");

            Assert.That(MatchHeistPolicy.LoserLoss(owns: false, rank: 0, locked: false, Global).Kind, Is.EqualTo(HeistMove.NotOwned));
        }

        // ---------------------------------------------------------------- the phase's next step

        static MatchHeistPosition Position(
            int picksOwed,
            List<HeistEligibleCard> menu,
            bool winnerIsPresent,
            List<CardId> picksSoFar = null,
            MatchTablePhase phase = MatchTablePhase.HeistPick,
            int winnerSeat = 0)
            => new MatchHeistPosition(phase, winnerSeat, picksOwed, menu, picksSoFar, winnerIsPresent);

        [Test]
        public void AWinnerWhoIsNotThereHasEverySlotDefaultedAndNoClockArmed()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4), Row("GreyOwl", 2)), winnerIsPresent: false),
                MatchSeats.None, picked: null, Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndEnd), "there is nothing for a clock to wait for");
            Assert.That(verdict.Picks, Is.EqualTo(Cards("EmberKit", "GreyOwl")), "highest rank first, then the rest");
            Assert.That(verdict.AnyAutoDefaulted, Is.True);
        }

        [Test]
        public void ALapsedClockDefaultsOneSlotAndClocksTheNextWhileTheWinnerIsPresent()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4), Row("GreyOwl", 2)), winnerIsPresent: true),
                MatchSeats.None, picked: null, Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndArm), "the second pick is a second decision");
            Assert.That(verdict.Picks, Is.EqualTo(Cards("EmberKit")));
            Assert.That(verdict.AnyAutoDefaulted, Is.True);
        }

        [Test]
        public void TheLastSlotLapsingEndsThePhaseRatherThanArmingAgain()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4), Row("GreyOwl", 2)), winnerIsPresent: true,
                    picksSoFar: Cards("EmberKit")),
                MatchSeats.None, picked: null, Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndEnd));
            Assert.That(verdict.Picks, Is.EqualTo(Cards("GreyOwl")));
        }

        [Test]
        public void AWinnerWhoLeavesBetweenPicksHasTheSecondDefaultedAtOnce()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4), Row("GreyOwl", 2)), winnerIsPresent: false,
                    picksSoFar: Cards("EmberKit")),
                MatchSeats.None, picked: null, Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndEnd));
            Assert.That(verdict.Picks, Is.EqualTo(Cards("GreyOwl")));
            Assert.That(verdict.AnyAutoDefaulted, Is.True);
        }

        [Test]
        public void APickThatLandsWithMoreOwedArmsTheNextClock()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4), Row("GreyOwl", 2)), winnerIsPresent: true),
                fromSeat: 0, picked: Card("GreyOwl"), Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndArm));
            Assert.That(verdict.Picks, Is.EqualTo(Cards("GreyOwl")), "the player's own choice, not the default");
            Assert.That(verdict.AnyAutoDefaulted, Is.False);
        }

        [Test]
        public void ThePickThatFillsTheLastSlotEndsThePhase()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 1, Menu(Row("EmberKit", 4)), winnerIsPresent: true),
                fromSeat: 0, picked: Card("EmberKit"), Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndEnd));
            Assert.That(verdict.Picks, Is.EqualTo(Cards("EmberKit")));
        }

        [Test]
        public void APickTheWinnerMakesAndThenVanishesOnStillFillsTheSecondSlot()
        {
            // The client that sent the pick is gone by the time it is resolved: the slot it filled stands and
            // the other one is defaulted rather than left hanging on a clock nobody is watching.
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4), Row("GreyOwl", 2)), winnerIsPresent: false),
                fromSeat: 0, picked: Card("GreyOwl"), Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndEnd));
            Assert.That(verdict.Picks, Is.EqualTo(Cards("GreyOwl", "EmberKit")));
            Assert.That(verdict.AnyAutoDefaulted, Is.True);
        }

        [Test]
        public void APickOutsideTheMenuIsRefusedAndMovesNothing()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 1, Menu(Row("EmberKit", 4)), winnerIsPresent: true),
                fromSeat: 0, picked: Card("GreyOwl"), Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.Refuse));
            Assert.That(verdict.Refusal, Is.EqualTo(MatchRefusalCode.NotEligible));
            Assert.That(verdict.Picks, Is.Empty);
        }

        [Test]
        public void ASecondPickOfTheSameSingleCopyIsRefused()
        {
            // The menu is subtracted by occurrence, so a card played once cannot be taken twice — and this is
            // the refusal a client that re-sent its pick would get rather than a second rank moving.
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4)), winnerIsPresent: true, picksSoFar: Cards("EmberKit")),
                fromSeat: 0, picked: Card("EmberKit"), Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.Refuse));
            Assert.That(verdict.Refusal, Is.EqualTo(MatchRefusalCode.NotEligible));
        }

        [Test]
        public void APickFromTheLosersSeatIsRefused()
        {
            // The loser owes nothing here at all: whatever they protected, they protected before they queued.
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 1, Menu(Row("EmberKit", 4)), winnerIsPresent: true, winnerSeat: 0),
                fromSeat: 1, picked: Card("EmberKit"), Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.Refuse));
            Assert.That(verdict.Refusal, Is.EqualTo(MatchRefusalCode.NotYourTurn));
        }

        [Test]
        public void APickOutsideThePhaseIsRefusedAndALapseOutsideItDoesNothing()
        {
            MatchHeistPosition ended = Position(picksOwed: 1, Menu(Row("EmberKit", 4)), winnerIsPresent: true,
                phase: MatchTablePhase.Ended);

            Assert.That(MatchHeistPolicy.Resolve(ended, fromSeat: 0, picked: Card("EmberKit"), Config).Step,
                Is.EqualTo(MatchHeistStep.Refuse));

            // A stamp that outlived its phase must be a no-op rather than a second resolution: the actor
            // re-arms from the model afterwards, and anything else here would spin.
            Assert.That(MatchHeistPolicy.Resolve(ended, MatchSeats.None, picked: null, Config).Step,
                Is.EqualTo(MatchHeistStep.Nothing));
        }

        [Test]
        public void AShortMenuPaysWhatIsThereAndTheSecondSlotPaysNothing()
        {
            MatchHeistVerdict verdict = MatchHeistPolicy.Resolve(
                Position(picksOwed: 2, Menu(Row("EmberKit", 4)), winnerIsPresent: false),
                MatchSeats.None, picked: null, Config);

            Assert.That(verdict.Step, Is.EqualTo(MatchHeistStep.TakeAndEnd));
            Assert.That(verdict.Picks, Is.EqualTo(Cards("EmberKit")), "nothing deadlocks on a card that does not exist");
        }

        [Test]
        public void ThePositionOffALiveTableReadsTheLosersMenu()
        {
            // The one place the phase's inputs are gathered, so the actor and a test read them the same way —
            // in particular that the menu is the seat that did NOT win.
            MatchModel match = new MatchModel
            {
                Phase  = MatchTablePhase.HeistPick,
                Stakes = Ranked(StakesTier.Even),
                Result = Won(1),
                HeistEligibility = new List<List<HeistEligibleCard>>
                {
                    Menu(Row("EmberKit", 3)),
                    Menu(Row("GreyOwl", 5)),
                },
            };

            MatchHeistPosition position = MatchHeistPosition.Of(match, winnerIsPresent: true);

            Assert.That(position.WinnerSeat, Is.EqualTo(1));
            Assert.That(position.PicksOwed, Is.EqualTo(1));
            Assert.That(position.Eligible.Count, Is.EqualTo(1));
            Assert.That(position.Eligible[0].Card, Is.EqualTo(Card("EmberKit")), "seat 1 won, so seat 0's list is the menu");
            Assert.That(position.PicksSoFar, Is.Empty);
        }
    }
}
