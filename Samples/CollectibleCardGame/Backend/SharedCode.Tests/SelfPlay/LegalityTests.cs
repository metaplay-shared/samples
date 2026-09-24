using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// L1–L5 of the invariant catalog, across every profile pairing including the two random-legal ones. This
    /// is the fixture that carries bots.md's first required property — a bot never takes an illegal action —
    /// and the fuzzer role at the same time: a random player walks into the legality holes a heuristic never
    /// would, and every action either of them takes is checked against the rules a second time before the
    /// engine sees it.
    /// </summary>
    [TestFixture]
    public class LegalityTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void NoProfileEverTakesAnIllegalAction()
        {
            SelfPlayHarness.RunBatch(
                "legality",
                SelfPlayStreams.Legality,
                SelfPlayRun.Games(standard: 80, deep: 5000),
                index => new SelfPlayGameSpec
                {
                    Config = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks = SelfPlayChecks.Legality,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                });
        }

        [Test]
        public void NoSeatEverTakesAnIllegalActionInAPacedGame()
        {
            // The same games, paced. The clock changes no rule, but it changes the *stamps*: at zero timings
            // every deadline is null, so a follower deriving one from (payload, public state) is never
            // actually put to the test. This batch is where it is — a follower that reached a different
            // stamp fails the mirror.
            SelfPlayHarness.RunBatch(
                "legality, paced",
                SelfPlayStreams.Legality,
                SelfPlayRun.Games(standard: 20, deep: 400),
                index => new SelfPlayGameSpec
                {
                    Config  = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks  = SelfPlayChecks.Legality,
                    Timings = MatchTimings.Default,
                    MirrorOnFollower = true,
                });

        }

        [Test]
        public void ADecisionIsAPureFunctionOfTheSeatViewAndTheSeed()
        {
            // L2, taken at every decision rather than sampled: the property the indistinguishability proof is
            // built on deserves one run where nothing is skipped.
            SelfPlayHarness.RunBatch(
                "decision determinism",
                SelfPlayStreams.DecisionDeterminism,
                SelfPlayRun.Games(standard: 20, deep: 800),
                index => new SelfPlayGameSpec
                {
                    Config            = Config,
                    Seat0             = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1             = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing           = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks            = SelfPlayChecks.Legality,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                    DeterminismStride = 1,
                });
        }

        [Test]
        public void TheBulkRunDealsEveryCollectibleCardAtMoreThanOneRank()
        {
            // A sweep is only as wide as the content it deals. Two fixed decks at rank 1 would leave ten of
            // the catalogue's collectibles never played and every rank track read at the one rank where it
            // does nothing — so the rotation is asserted rather than assumed, because a deck helper that
            // quietly narrowed would take the coverage with it and nothing would go red.
            List<CardId> covered = SelfPlayDecks.Covered(Config);
            List<CardId> missing = new List<CardId>();

            foreach (CardInfo card in Config.Cards.Values)
            {
                if (card.Collectible && !covered.Contains(card.CardId))
                    missing.Add(card.CardId);
            }

            missing.Sort(CardInfo.CompareCanonical);
            Assert.That(missing, Is.Empty, "the rotation never deals these");

            Assert.That(SelfPlayDecks.RanksCovered(), Does.Contain(Config.Global.RankMin));
            Assert.That(SelfPlayDecks.RanksCovered(), Does.Contain(Config.Global.RankMax));
            Assert.That(SelfPlayDecks.RanksCovered().Count, Is.GreaterThanOrEqualTo(4), "the rank tracks need more than their endpoints");
        }

        [Test]
        public void TheLegalityCheckCatchesAnAttackPastAGuard()
        {
            // The second opinion has to be able to disagree, or its agreement means nothing.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId attacker = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).Board("SunbeamRetriever");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            SeatView    view   = engine.BuildSeatView(0);
            MatchIntent illegal = new AttackIntent(attacker, EffectTargetRef.Den(1));

            Assert.That(LegalityChecks.Check(engine, view, 0, illegal), Does.StartWith("L1"), "it is not in the legal set either");
        }

        [Test]
        public void TheLegalityCheckCatchesATargetedSneakyCritter()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId attacker = scenario.Seat(0).Board("PondFrog");
            CardInstanceId sneak    = scenario.Seat(1).Board("MoonlitAlleycat");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchIntent illegal = new AttackIntent(attacker, EffectTargetRef.OnCritter(sneak));
            string      caught  = LegalityChecks.Check(engine, engine.BuildSeatView(0), 0, illegal);

            Assert.That(caught, Is.Not.Null);
            Assert.That(caught, Does.StartWith("L1"), "the enumeration refuses it, so membership catches it first");
        }

        [Test]
        public void TheSneakyRuleIsCheckedIndependentlyOfTheEnumeration()
        {
            // L5 has to hold even where L1 cannot speak: an attack the enumeration would have offered, aimed
            // at a critter that became Sneaky since. The membership check is skipped so the ruling itself is
            // the thing under test.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId attacker = scenario.Seat(0).Board("PondFrog");
            CardInstanceId sneak    = scenario.Seat(1).Board("MoonlitAlleycat");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            SeatView withTheAttackOffered = SeatViews.Offering(engine, 0, new AttackIntent(attacker, EffectTargetRef.OnCritter(sneak)));

            string caught = LegalityChecks.Check(engine, withTheAttackOffered, 0, withTheAttackOffered.LegalActions[0]);
            Assert.That(caught, Does.StartWith("L5"));
        }

        [Test]
        public void TheLegalityCheckCatchesASleepyAttacker()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId sleepy   = scenario.Seat(0).Board("PondFrog", sleepy: true);
            scenario.Seat(1).Board("MeadowMouse");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            SeatView offered = SeatViews.Offering(engine, 0, new AttackIntent(sleepy, EffectTargetRef.Den(1)));

            Assert.That(LegalityChecks.Check(engine, offered, 0, offered.LegalActions[0]), Does.StartWith("L4"));
        }
    }
}
