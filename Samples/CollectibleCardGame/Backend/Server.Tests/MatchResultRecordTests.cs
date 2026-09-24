using Game.Logic;
using Game.Server.Match;
using Metaplay.Core;
using Metaplay.Core.Model;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Server.Tests
{
    /// <summary>
    /// The outcome record and the Heist eligibility lists: what a finished table owes its two accounts.
    /// <para>
    /// The eligibility list has to outlive the board — it is read during the Heist and again in the result
    /// payload, after the game state has stopped meaning anything — which is why it is kept explicitly rather
    /// than folded out of a history whose shape will keep changing as cards are added.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchResultRecordTests
    {
        static CardId Card(string id) => CardId.FromString(id);

        /// <summary>
        /// The policy's own subtraction, not a copy of it, so subtracting the wrong seat's locks fails here.
        /// </summary>
        static List<CardId> Eligible(IReadOnlyList<CardId> played, IReadOnlyList<CardId> locked)
            => Cards(MatchHeistPolicy.Eligible(Rows(played), locked));

        /// <summary>
        /// A played list as the Heist rows it is now: the card plus the rank its owner brought it at. These
        /// tests are about which <em>cards</em> survive the subtraction, so the rank is a fixed 1 and the
        /// rows are unwrapped again on the way out.
        /// </summary>
        static List<HeistEligibleCard> Rows(IReadOnlyList<CardId> cards)
        {
            List<HeistEligibleCard> rows = new List<HeistEligibleCard>(cards.Count);
            foreach (CardId card in cards)
                rows.Add(new HeistEligibleCard(card, 1));
            return rows;
        }

        static List<CardId> Cards(IReadOnlyList<HeistEligibleCard> rows)
        {
            List<CardId> cards = new List<CardId>(rows.Count);
            foreach (HeistEligibleCard row in rows)
                cards.Add(row.Card);
            return cards;
        }

        [Test]
        public void EligibilityIsPlayedMinusTheSeatsOwnLockedSet()
        {
            List<CardId> played = new List<CardId> { Card("EmberKit"), Card("WarmBiscuit"), Card("AcornHoard") };
            List<CardId> locked = new List<CardId> { Card("WarmBiscuit") };

            List<CardId> eligible = Eligible(played, locked);

            Assert.That(eligible, Is.EqualTo(new List<CardId> { Card("EmberKit"), Card("AcornHoard") }));
        }

        [Test]
        public void ALockOnACardThatWasNeverPlayedChangesNothing()
        {
            List<CardId> played = new List<CardId> { Card("EmberKit") };
            List<CardId> locked = new List<CardId> { Card("BrambleBoar") };

            Assert.That(Eligible(played, locked), Is.EqualTo(played));
        }

        [Test]
        public void ASeatThatPlayedNothingLeavesNothingToPick()
        {
            // A short eligible list takes what is there, and an empty one never enters the phase at all.
            // Nothing deadlocks on a card that does not exist.
            Assert.That(Eligible(new List<CardId>(), new List<CardId>()), Is.Empty);
        }

        [Test]
        public void ASeatWhosePlayedCardsWereAllLockedLeavesNothingToPick()
        {
            List<CardId> played = new List<CardId> { Card("EmberKit"), Card("WarmBiscuit") };

            Assert.That(Eligible(played, played), Is.Empty);
        }

        [Test]
        public void TheSubtractionIsAgainstTheSeatsOwnLocksAndNotTheOtherSeats()
        {
            // The one substitution these tests exist to catch, asserted directly.
            List<CardId> played    = new List<CardId> { Card("EmberKit"), Card("WarmBiscuit") };
            List<CardId> ownLocks  = new List<CardId> { Card("EmberKit") };
            List<CardId> theirLocks = new List<CardId> { Card("WarmBiscuit") };

            Assert.That(Cards(MatchHeistPolicy.Eligible(Rows(played), ownLocks)), Is.EqualTo(new List<CardId> { Card("WarmBiscuit") }));
            Assert.That(Cards(MatchHeistPolicy.Eligible(Rows(played), theirLocks)), Is.EqualTo(new List<CardId> { Card("EmberKit") }),
                "swapping which seat's locks are subtracted changes the answer, so a test that could not tell them apart proved nothing");
        }

        [Test]
        public void TheLosersMenuAlsoSubtractsTheWinnersOwnFrozenSet()
        {
            // The rule the table actually applies, asked of the function the table calls — not of a copy of
            // its composition, which is what a private actor method makes untestable. A lock runs both ways,
            // so a card the WINNER froze would cost the loser a rank and pay the winner nothing; the owner's
            // ruling is that such a pick never reaches the menu at all (2026-09-09).
            MatchResult result = new MatchResult(
                MatchOutcome.Seat0Wins,
                winnerSeat: 0,
                playedBySeat0: Rows(new List<CardId> { Card("GreyOwl"), Card("EmberKit") }),
                playedBySeat1: Rows(new List<CardId> { Card("EmberKit"), Card("WarmBiscuit"), Card("AcornHoard") }),
                finalTurn: 14,
                cause: MatchEndCause.DenAtZero);

            MatchStakes stakes = new MatchStakes(
                isRanked: true,
                StakesTier.Even,
                new List<int> { 60, 60 },
                new List<List<CardId>>
                {
                    new List<CardId> { Card("WarmBiscuit") },   // seat 0, the winner
                    new List<CardId> { Card("EmberKit") },      // seat 1, the loser
                });

            List<List<HeistEligibleCard>> eligibility = MatchHeistPolicy.Eligibility(result, stakes);

            Assert.That(Cards(eligibility[1]), Is.EqualTo(new List<CardId> { Card("AcornHoard") }),
                "the loser's menu loses their own frozen card AND the one the winner froze");

            Assert.That(Cards(eligibility[0]), Is.EqualTo(new List<CardId> { Card("GreyOwl"), Card("EmberKit") }),
                "the winner's own row takes one pass only — its owner's — so the loser's lock on EmberKit does not reach across the table");
        }

        [Test]
        public void ADrawSubtractsEachSeatsOwnLocksAndNothingElse()
        {
            // No winner, so there is no second frozen set to apply — and the rows still have to be built,
            // because the lists outlive the board whether or not a phase runs.
            MatchResult drawn = new MatchResult(
                MatchOutcome.Draw,
                winnerSeat: MatchSeats.None,
                playedBySeat0: Rows(new List<CardId> { Card("EmberKit") }),
                playedBySeat1: Rows(new List<CardId> { Card("EmberKit") }),
                finalTurn: 20,
                cause: MatchEndCause.BothDensAtZero);

            MatchStakes stakes = new MatchStakes(
                isRanked: true, StakesTier.Even, new List<int> { 60, 60 },
                new List<List<CardId>> { new List<CardId> { Card("EmberKit") }, new List<CardId>() });

            List<List<HeistEligibleCard>> eligibility = MatchHeistPolicy.Eligibility(drawn, stakes);

            Assert.That(eligibility[0], Is.Empty, "seat 0 froze the one card it played");
            Assert.That(Cards(eligibility[1]), Is.EqualTo(new List<CardId> { Card("EmberKit") }),
                "and seat 0's lock does not reach across the table");
        }

        [Test]
        public void ANullLockedSetMeansNothingWasLocked()
        {
            List<CardId> played = new List<CardId> { Card("EmberKit") };

            Assert.That(Cards(MatchHeistPolicy.Eligible(Rows(played), null)), Is.EqualTo(played));
        }

        [Test]
        public void DuplicatePlaysSurviveAsDuplicates()
        {
            // A seat that played two copies offers two, and played order is kept: the Heist draws this list.
            List<CardId> played = new List<CardId> { Card("EmberKit"), Card("AcornHoard"), Card("EmberKit") };

            Assert.That(Cards(MatchHeistPolicy.Eligible(Rows(played), new List<CardId>())), Is.EqualTo(played));
        }

        [Test]
        public void TheEngineOnlyOffersCardsFromASeatsOwnStartingDeck()
        {
            // Tokens, summons and copies out of a graveyard are excluded at the source: the engine records
            // PlayedFromOwnDeck, which is the list the eligibility subtraction runs over. A card the loser
            // never owned cannot be taken from them.
            MatchResult result = new MatchResult(
                MatchOutcome.Seat0Wins,
                winnerSeat: 0,
                playedBySeat0: Rows(new List<CardId> { Card("EmberKit") }),
                playedBySeat1: Rows(new List<CardId> { Card("WarmBiscuit") }),
                finalTurn: 14,
                cause: MatchEndCause.DenAtZero);

            Assert.That(Cards(result.PlayedBy(0)), Is.EqualTo(new List<CardId> { Card("EmberKit") }));
            Assert.That(Cards(result.PlayedBy(1)), Is.EqualTo(new List<CardId> { Card("WarmBiscuit") }));
        }

        [Test]
        public void ADrawNamesNoWinner()
        {
            MatchOutcomeRecord record = new MatchOutcomeRecord(
                MatchOutcome.Draw, MatchSeats.None, 20, MatchEndCause.BothDensAtZero, new List<bool> { true, true }, wasRanked: true, decidedAt: MetaTime.Now);

            Assert.That(record.IsDraw, Is.True);
            Assert.That(record.WinnerSeat, Is.EqualTo(MatchSeats.None));
            Assert.That(record.OutcomeForSeat(0), Is.EqualTo(MatchAccountOutcome.Draw));
            Assert.That(record.OutcomeForSeat(1), Is.EqualTo(MatchAccountOutcome.Draw));
        }

        [Test]
        public void TheRecordTranslatesASeatIntoWhatThatAccountDid()
        {
            // The table speaks in seats and the record speaks in wins, so the translation happens once, where
            // the seat is still in scope.
            MatchOutcomeRecord record = new MatchOutcomeRecord(
                MatchOutcome.Seat1Wins, winnerSeat: 1, 12, MatchEndCause.DenAtZero, new List<bool> { false, true }, wasRanked: false, decidedAt: MetaTime.Now);

            Assert.That(record.OutcomeForSeat(1), Is.EqualTo(MatchAccountOutcome.Win));
            Assert.That(record.OutcomeForSeat(0), Is.EqualTo(MatchAccountOutcome.Loss));
        }

        [Test]
        public void WhoWasStillAtTheTableIsRecordedRatherThanCounted()
        {
            // Leaving costs exactly what staying would, so presence at the finish changes nothing about the
            // result — it is simply on the record.
            MatchOutcomeRecord record = new MatchOutcomeRecord(
                MatchOutcome.Seat0Wins, winnerSeat: 0, 9, MatchEndCause.DenAtZero, new List<bool> { true, false }, wasRanked: false, decidedAt: MetaTime.Now);

            Assert.That(record.WasPresentAtFinish, Is.EqualTo(new List<bool> { true, false }));
            Assert.That(record.OutcomeForSeat(0), Is.EqualTo(MatchAccountOutcome.Win), "the absent seat still lost a real game");
        }

        [Test]
        public void APracticeMatchPutsNoRanksAtStake()
        {
            // And therefore runs no Heist: the tier and the ranked flag are the two facts the phase decision
            // reads, and whether a phase ran is answered by the record afterwards rather than by the wager.
            MatchStakes stakes = MatchStakes.Practice(new List<int> { 25, 25 }, new List<List<CardId>> { new List<CardId>(), new List<CardId>() });

            Assert.That(stakes.IsRanked, Is.False);
            Assert.That(stakes.Tier, Is.EqualTo(StakesTier.Practice));
            Assert.That(MatchHeistPolicy.PicksOwedForMatch(stakes, null), Is.Zero);
        }

        [Test]
        public void TheRecordSurvivesARoundTrip()
        {
            // It is persisted with the model and it rides an internal ask, so it has to come back whole from
            // both — a table restored from the database has to know exactly what it still owes.
            MatchOutcomeRecord record = new MatchOutcomeRecord(
                MatchOutcome.Seat0Wins, winnerSeat: 0, 17, MatchEndCause.DenAtZero, new List<bool> { true, false }, wasRanked: true, decidedAt: MetaTime.Now);

            MatchOutcomeRecord copy = MetaSerialization.CloneTagged(record, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

            Assert.That(copy.Outcome, Is.EqualTo(record.Outcome));
            Assert.That(copy.WinnerSeat, Is.EqualTo(record.WinnerSeat));
            Assert.That(copy.FinalTurn, Is.EqualTo(record.FinalTurn));
            Assert.That(copy.Cause, Is.EqualTo(record.Cause));
            Assert.That(copy.WasPresentAtFinish, Is.EqualTo(record.WasPresentAtFinish));
            Assert.That(copy.WasRanked, Is.True);
            Assert.That(copy.Heist, Is.Null, "a tier that moves no ranks carries no Heist record at all");
        }

        [Test]
        public void TheHeistRecordCarriesBothPicksAndTheDefaultedFlag()
        {
            // It rides the delivery ask and both clients render it, so the picks and the one flag the screen
            // draws have to come back whole.
            MatchOutcomeRecord record = new MatchOutcomeRecord(
                MatchOutcome.Seat1Wins, winnerSeat: 1, 11, MatchEndCause.DenAtZero, new List<bool> { false, true }, wasRanked: true, decidedAt: MetaTime.Now)
            {
                Heist = new HeistResult(new List<CardId> { Card("EmberKit"), Card("WarmBiscuit") }, anyPickAutoDefaulted: true),
            };

            MatchOutcomeRecord copy = MetaSerialization.CloneTagged(record, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null);

            Assert.That(copy.Heist, Is.Not.Null);
            Assert.That(copy.Heist.Picks, Is.EqualTo(new List<CardId> { Card("EmberKit"), Card("WarmBiscuit") }));
            Assert.That(copy.Heist.AnyPickAutoDefaulted, Is.True);
        }

        [Test]
        public void AThirdPickCannotRideTheWire()
        {
            // The underdog tier is the only one that takes two, so two is the bound — and a bound on a
            // payload-borne list is enforced the same way MatchModel.History's 4096 is, which is the thing
            // worth pinning rather than assuming.
            HeistResult overfull = new HeistResult(
                new List<CardId> { Card("EmberKit"), Card("WarmBiscuit"), Card("AcornHoard") }, anyPickAutoDefaulted: false);

            Assert.That(() => MetaSerialization.CloneTagged(overfull, MetaSerializationFlags.IncludeAll, logicVersion: null, resolver: null),
                Throws.Exception, "MaxCollectionSize(2) has to be a refusal rather than a comment");
        }
    }
}
