using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using NUnit.Framework;

namespace Game.Server.Tests
{
    /// <summary>
    /// What a seat is, and what changing who occupies one does — and does not — move.
    /// <para>
    /// A player identity appears in exactly one place, the seat roster, which is what makes covering a seat a
    /// one-field change. The properties below are the ones the rest of the actor reads to decide whether a
    /// policy plays a seat, whether the plaque carries a mark, whether the table has lost everyone, and
    /// whether a lapsed turn deadline has cost its owner the seat.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchSeatPolicyTests
    {
        static readonly EntityId Owner = EntityId.Create(EntityKindCore.Player, 42);

        static MatchSeat Human() => new MatchSeat(Owner, "Moppet", SeatOccupancy.Human, BotProfileId.Strongest) { IsConnected = true };

        static MatchSeat Bot() => new MatchSeat(EntityId.None, "Clockwork Cub", SeatOccupancy.Bot, BotProfileId.Casual);

        [Test]
        public void AConnectedHumanIsNeitherBotDrivenNorMarked()
        {
            MatchSeat seat = Human();

            Assert.That(seat.IsBotDriven, Is.False);
            Assert.That(seat.IsConnectedHuman, Is.True);
            Assert.That(seat.ShowsComputerMark, Is.False);
        }

        [Test]
        public void ADisconnectedHumanIsStillNotBotDriven()
        {
            // A connected seat is governed by the turn deadline and a disconnected one by grace, never both.
            // Dropping does not itself hand the seat to a policy.
            MatchSeat seat = Human();
            seat.IsConnected = false;

            Assert.That(seat.IsBotDriven, Is.False);
            Assert.That(seat.IsConnectedHuman, Is.False);
        }

        [Test]
        public void CoveringASeatKeepsItsOwnerAndAddsTheMark()
        {
            // The human's identity is retained so they can reclaim it, and the plaque keeps their name and
            // gains the computer-player mark: a bot playing anonymously under a human's name would break the
            // table's honesty in the most common bot case there is.
            //
            // The flip goes through MatchSeatPolicy, which is the same call the actor makes. Setting Occupancy
            // in the test body and then asserting the derived properties tests nothing but the properties: the
            // rule about which occupancies may be covered, and about what survives the change, would not be
            // exercised at all.
            MatchSeat seat = Human();
            seat.IsConnected = false;

            MatchSeatPolicy.Cover(seat);

            Assert.That(seat.PlayerId, Is.EqualTo(Owner));
            Assert.That(seat.DisplayName, Is.EqualTo("Moppet"));
            Assert.That(seat.IsBotDriven, Is.True);
            Assert.That(seat.ShowsComputerMark, Is.True);
            Assert.That(seat.IsConnectedHuman, Is.False);
        }

        [Test]
        public void ASeatThatIsAlreadyCoveredIsNotCoveredAgain()
        {
            // A bot is already playing it, and its owner takes it back by coming back rather than by a timer.
            MatchSeat seat = Human();
            MatchSeatPolicy.Cover(seat);
            MatchSeatPolicy.Cover(seat);

            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.HumanCoveredByBot));
            Assert.That(MatchSeatPolicy.CanCover(seat), Is.False);
        }

        [Test]
        public void ABotSeatIsNeverCovered()
        {
            Assert.That(MatchSeatPolicy.CanCover(Bot()), Is.False);
        }

        [Test]
        public void TheOwnerComingBackMarksTheSeatAndLeavesTheBotPlayingIt()
        {
            // The bot keeps the seat until the next turn boundary, so the owner never races it mid-turn.
            MatchSeat seat = Human();
            MatchSeatPolicy.Cover(seat);
            seat.Strikes = 2;

            Assert.That(MatchSeatPolicy.RequestReclaim(seat, matchHasDecided: false), Is.True);

            Assert.That(seat.ReclaimPending, Is.True);
            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.HumanCoveredByBot));
            Assert.That(seat.IsBotDriven, Is.True);
            Assert.That(MatchSeatPolicy.RefusesIntents(seat, matchHasDecided: false), Is.True, "the owner's intents are refused until the boundary");
            Assert.That(MatchSeatPolicy.RefusesIntents(seat, matchHasDecided: true), Is.False, "a decided game leaves refusing to the rules");
            // Strikes are consecutive by definition, and a player who returned has broken the run.
            Assert.That(seat.Strikes, Is.Zero);

            Assert.That(MatchSeatPolicy.RequestReclaim(seat, matchHasDecided: false), Is.False, "a second ask changes nothing");
        }

        [Test]
        public void AMarkedSeatIsHandedBackAtTheTurnBoundaryWithTheOwnerIntact()
        {
            MatchSeat seat = Human();
            MatchSeatPolicy.Cover(seat);
            MatchSeatPolicy.RequestReclaim(seat, matchHasDecided: false);

            Assert.That(MatchSeatPolicy.ReclaimsAtTurnBoundary(seat, matchHasDecided: false), Is.True);
            MatchSeatPolicy.Reclaim(seat, matchHasDecided: false);

            Assert.That(seat.PlayerId, Is.EqualTo(Owner));
            Assert.That(seat.IsBotDriven, Is.False);
            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.Human));
            Assert.That(seat.ReclaimPending, Is.False);
            Assert.That(MatchSeatPolicy.RefusesIntents(seat, matchHasDecided: false), Is.False);
        }

        [Test]
        public void AnUnmarkedCoveredSeatStaysCoveredAcrossATurnBoundary()
        {
            MatchSeat seat = Human();
            MatchSeatPolicy.Cover(seat);

            Assert.That(MatchSeatPolicy.ReclaimsAtTurnBoundary(seat, matchHasDecided: false), Is.False);
            MatchSeatPolicy.Reclaim(seat, matchHasDecided: false);
            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.HumanCoveredByBot));
        }

        [Test]
        public void AnOwnerWhoGoesAgainBeforeTheBoundaryLeavesTheSeatCovered()
        {
            MatchSeat seat = Human();
            MatchSeatPolicy.Cover(seat);
            MatchSeatPolicy.RequestReclaim(seat, matchHasDecided: false);

            MatchSeatPolicy.CancelReclaim(seat);

            Assert.That(MatchSeatPolicy.ReclaimsAtTurnBoundary(seat, matchHasDecided: false), Is.False);
            MatchSeatPolicy.Reclaim(seat, matchHasDecided: false);
            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.HumanCoveredByBot));
        }

        [Test]
        public void CoveringASeatAgainForgetsAnOldRequest()
        {
            // A seat handed back and then covered again by a fresh run of lapses starts unmarked.
            MatchSeat seat = Human();
            seat.ReclaimPending = true;
            MatchSeatPolicy.Cover(seat);

            Assert.That(seat.ReclaimPending, Is.False);
        }

        [Test]
        public void ADecidedMatchCannotBeReclaimedInto()
        {
            // There is no game left to play.
            MatchSeat seat = Human();
            MatchSeatPolicy.Cover(seat);

            Assert.That(MatchSeatPolicy.RequestReclaim(seat, matchHasDecided: true), Is.False);
            Assert.That(seat.ReclaimPending, Is.False);

            seat.ReclaimPending = true;
            MatchSeatPolicy.Reclaim(seat, matchHasDecided: true);
            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.HumanCoveredByBot));
        }

        [Test]
        public void AnUncoveredSeatHasNothingToReclaim()
        {
            Assert.That(MatchSeatPolicy.CanReclaim(Human(), matchHasDecided: false), Is.False);
            Assert.That(MatchSeatPolicy.CanReclaim(Bot(), matchHasDecided: false), Is.False);
            Assert.That(MatchSeatPolicy.RequestReclaim(Human(), matchHasDecided: false), Is.False);
            Assert.That(MatchSeatPolicy.RefusesIntents(Human(), matchHasDecided: false), Is.False);
        }

        // ---------------------------------------------------------------- who the turn deadline governs

        [Test]
        public void TheTurnDeadlineGovernsAConnectedHumanAndEveryBotDrivenSeat()
        {
            MatchSeat covered = Human();
            MatchSeatPolicy.Cover(covered);

            // A lapse on either of the bot-driven cases auto-plays with the strongest profile, which is what
            // that seat's own driver would have done anyway.
            Assert.That(MatchSeatPolicy.TurnDeadlineGoverns(Human()), Is.True);
            Assert.That(MatchSeatPolicy.TurnDeadlineGoverns(Bot()), Is.True);
            Assert.That(MatchSeatPolicy.TurnDeadlineGoverns(covered), Is.True);
        }

        [Test]
        public void TheTurnDeadlineDoesNotGovernAHumanWhoIsNotHere()
        {
            // The exclusivity rule: a connected seat is governed by the deadline and a seat whose player has
            // gone is governed by grace, never both. The lapse is where the strike is recorded, so a seat that
            // fell through this guard would collect strikes for turns its owner could not take — and two of
            // them would hand away a seat grace was already holding.
            MatchSeat away = Human();
            away.IsConnected = false;

            Assert.That(MatchSeatPolicy.TurnDeadlineGoverns(away), Is.False);
            Assert.That(MatchSeatPolicy.IsAwayHuman(away), Is.True);
        }

        [Test]
        public void ACoveredSeatIsNotAnAwayHumanEvenThoughItsOwnerIsAway()
        {
            // Covering a seat spends its grace: from then on a bot has it, and the deadline is what governs.
            MatchSeat covered = Human();
            covered.IsConnected = false;
            MatchSeatPolicy.Cover(covered);

            Assert.That(MatchSeatPolicy.IsAwayHuman(covered), Is.False);
            Assert.That(MatchSeatPolicy.TurnDeadlineGoverns(covered), Is.True);
        }

        // ---------------------------------------------------------------- who the Heist clock is armed for

        [Test]
        public void TheHeistClockIsArmedOnlyForAPersonWhoIsHere()
        {
            MatchSeat away = Human();
            away.IsConnected = false;

            Assert.That(Human().IsConnectedHuman, Is.True);
            Assert.That(away.IsConnectedHuman, Is.False);
            Assert.That(Bot().IsConnectedHuman, Is.False, "a bot seat never wins a Heist pick to make");
        }

        [Test]
        public void ACoveredSeatCarriesNoHeistClockEvenThoughItCarriesATurnDeadline()
        {
            // The Heist clock reads a covered seat the opposite way to TurnDeadlineGoverns: a covered turn is
            // worth a clock because a bot will take it, and a covered pick is the bot's default already.
            MatchSeat covered = Human();
            covered.IsConnected = false;
            MatchSeatPolicy.Cover(covered);

            Assert.That(MatchSeatPolicy.TurnDeadlineGoverns(covered), Is.True);
            Assert.That(covered.IsConnectedHuman, Is.False);
        }

        [Test]
        public void ACoveredSeatWhoseOwnerIsConnectedStillCarriesNoHeistClock()
        {
            // The surprising row, and the one the Heist inherits rather than re-derives: a seat struck out
            // while its owner sat watching is covered with IsConnected still true. A bot has the seat, so the
            // pick is the bot's answer and there is nothing to clock.
            MatchSeat covered = Human();
            MatchSeatPolicy.Cover(covered);

            Assert.That(covered.IsConnected, Is.True, "the owner never dropped");
            Assert.That(covered.IsConnectedHuman, Is.False);
        }

        // ---------------------------------------------------------------- strikes, and the cover they reach

        /// <summary> The shipped budget, so a case reads the boundary rather than restating a literal. </summary>
        static int ShippedLimit => new MatchOptions().StrikesBeforeCover;

        [Test]
        public void AFirstLapseTakesAStrikeAndLeavesTheSeatWhereItIs()
        {
            // One lapse is being slow. The turn is played out either way — that is not this predicate's
            // business — and the seat stays its owner's.
            MatchSeat seat = Human();

            Assert.That(MatchSeatPolicy.LapseCountsAsAStrike(seat, ShippedLimit), Is.True);
            seat.Strikes++;

            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(seat, ShippedLimit, matchHasDecided: false), Is.False);
            Assert.That(seat.Occupancy, Is.EqualTo(SeatOccupancy.Human));
        }

        [Test]
        public void ASecondConsecutiveLapseHandsTheSeatToABot()
        {
            // Two is being gone. The count is consecutive, so the second lapse is only the second if nothing
            // from the seat came between them — which is what the reset below is for.
            MatchSeat seat = Human();
            seat.Strikes = 2;

            Assert.That(ShippedLimit, Is.EqualTo(2), "the shipped budget is what match.md's timing table quotes");
            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(seat, ShippedLimit, matchHasDecided: false), Is.True);
        }

        [Test]
        public void ClearingTheCountMakesTheNextLapseAFirstAgain()
        {
            // The arithmetic of "consecutive", and **only** that: this drives the predicates directly, so it
            // says nothing about whether anything calls them. That an intent is what clears the count — the
            // actor's own presence step, refused intents included — is asserted where it happens, in
            // Client.Tests/MatchStrikeTests.AnIntentBetweenTwoLapsesKeepsTheSeat, because Server.Tests has no
            // actor to ask.
            MatchSeat seat = Human();
            seat.Strikes = 1;

            MatchSeatPolicy.ClearStrikes(seat);
            Assert.That(seat.Strikes, Is.Zero);

            seat.Strikes++;
            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(seat, ShippedLimit, matchHasDecided: false), Is.False);
        }

        [Test]
        public void ADisconnectedSeatOnGraceAccruesNothing()
        {
            // The exclusivity rule from the strike's own side: a disconnected seat is governed by grace, and a
            // player must not accumulate strikes for turns they could not take. Belt and braces, because the
            // deadline is also held out of the attention fold while that seat is away.
            MatchSeat away = Human();
            away.IsConnected = false;

            Assert.That(MatchSeatPolicy.LapseCountsAsAStrike(away, ShippedLimit), Is.False);
            Assert.That(MatchSeatPolicy.TurnDeadlineGoverns(away), Is.False);
        }

        [Test]
        public void ABotSeatAndAnAlreadyCoveredSeatAccrueNothing()
        {
            // Both are bot-driven, so the turn deadline governs them and a lapse auto-plays — but a bot being
            // slow is not a person failing to turn up, and a covered seat's lapse belongs to the bot playing
            // it. Neither may be covered again, whatever the count on it says.
            MatchSeat covered = Human();
            MatchSeatPolicy.Cover(covered);
            covered.Strikes = 9;

            Assert.That(MatchSeatPolicy.LapseCountsAsAStrike(Bot(), ShippedLimit), Is.False);
            Assert.That(MatchSeatPolicy.LapseCountsAsAStrike(covered, ShippedLimit), Is.False);
            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(covered, ShippedLimit, matchHasDecided: false), Is.False);
            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(Bot(), ShippedLimit, matchHasDecided: false), Is.False);
        }

        [Test]
        public void ADecidedGameIsNotCoveredInto()
        {
            // The play-out a lapse triggers can finish the game, and then there is nothing to cover: keyed on
            // the same fact CanReclaim is, so the escalation cannot produce a covered seat whose owner has no
            // way of taking it back.
            MatchSeat seat = Human();
            seat.Strikes = 2;

            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(seat, ShippedLimit, matchHasDecided: true), Is.False);
        }

        [Test]
        public void TheBudgetAtOneCoversOnTheFirstLapse()
        {
            MatchSeat seat = Human();

            Assert.That(MatchSeatPolicy.LapseCountsAsAStrike(seat, 1), Is.True);
            seat.Strikes++;

            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(seat, 1, matchHasDecided: false), Is.True);
        }

        [Test]
        public void TheBudgetAtZeroTurnsTheCountOffEntirely()
        {
            // Zero means the count is not in force, the way a zero turn deadline means no deadline is in force
            // rather than one that has already lapsed. A lapse then only auto-plays the rest of the turn,
            // which is the behaviour the escalation was added on top of — so that behaviour stays reachable by
            // configuration rather than by deleting code.
            MatchSeat seat = Human();

            Assert.That(MatchSeatPolicy.LapseCountsAsAStrike(seat, 0), Is.False);

            seat.Strikes = 5;
            Assert.That(MatchSeatPolicy.ShouldCoverAfterStrike(seat, 0, matchHasDecided: false), Is.False);
        }

        [Test]
        public void ABotSeatNeverGainsAnOwner()
        {
            MatchSeat seat = Bot();

            Assert.That(seat.PlayerId, Is.EqualTo(EntityId.None));
            Assert.That(seat.PlayerId.IsValid, Is.False);
            Assert.That(seat.IsBotDriven, Is.True);
            Assert.That(seat.ShowsComputerMark, Is.True);
            Assert.That(seat.IsConnectedHuman, Is.False);
        }

        [Test]
        public void APersonalityProfileResolvesToItselfAndTheStrongestOneToNoMistakes()
        {
            // A seat seated as a bot is an opponent and may have a personality; a seat played on behalf of an
            // absent human is a service to that human, and a seeded mistake with their cards could cost them a
            // rank. The strongest profile never constructs its mistake substream at all.
            Assert.That(BotProfiles.Resolve(BotProfileId.Casual).Name, Is.EqualTo(BotProfile.Casual.Name));
            Assert.That(BotProfiles.Resolve(BotProfileId.Casual).MakesMistakes, Is.True);

            Assert.That(BotProfiles.Resolve(BotProfileId.Strongest).MakesMistakes, Is.False);
            Assert.That(BotProfiles.Resolve(BotProfileId.StrictlyDeterministic).MakesMistakes, Is.False);
        }

        [Test]
        public void EveryProfileIdNamesARealProfile()
        {
            // The id is the wire form of a profile, so an id with no profile behind it would be a seat nothing
            // could play. It resolves to the strongest one rather than to nothing, which is the safe
            // direction, and this asserts that fallback is never actually needed.
            foreach (BotProfileId id in System.Enum.GetValues<BotProfileId>())
                Assert.That(BotProfiles.Resolve(id).Name, Is.EqualTo(id.ToString()), $"{id} has no profile of its own");
        }
    }
}
