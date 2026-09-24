using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// C1–C5 of the invariant catalog: nothing is created or lost outside the rules that may create or lose
    /// it. Cards are in exactly one zone and the starting-deck census never moves; mana across a step is
    /// exactly the costs paid, the ramp, and whatever a resolved step granted; a critter's damage is exactly
    /// what the step said happened to it, and a Den holds exactly what the last event about it announced.
    /// </summary>
    [TestFixture]
    public class ConservationTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void NothingIsCreatedOrLostAcrossAnyStepOfAnyGame()
        {
            SelfPlayHarness.RunBatch(
                "conservation",
                SelfPlayStreams.Conservation,
                SelfPlayRun.Games(standard: 60, deep: 4000),
                index => new SelfPlayGameSpec
                {
                    Config = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks = SelfPlayChecks.Conservation,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                });
        }

        [Test]
        public void EveryStepOfEveryPacedGameConservesEverything()
        {
            // The same games, paced. The clock changes no rule, but it changes the *stamps*: at zero timings
            // every deadline is null, so a follower deriving one from (payload, public state) is never
            // actually put to the test. This batch is where it is — a follower that reached a different
            // stamp fails the mirror.
            SelfPlayHarness.RunBatch(
                "conservation, paced",
                SelfPlayStreams.Conservation,
                SelfPlayRun.Games(standard: 20, deep: 400),
                index => new SelfPlayGameSpec
                {
                    Config  = Config,
                    Seat0   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat0,
                    Seat1   = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Seat1,
                    Pairing = SelfPlaySeats.Mix[index % SelfPlaySeats.Mix.Length].Name,
                    Checks  = SelfPlayChecks.Conservation,
                    Timings = MatchTimings.Default,
                    MirrorOnFollower = true,
                });

        }

        [Test]
        public void TheWalkCatchesACardInTwoPlacesAtOnce()
        {
            // The count check alone would pass a state where one card was duplicated and another lost, which
            // is why C2 walks membership as well. Plant exactly that.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId mouse    = scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            List<CardInstanceId> deck = engine.SecretDeck(0);
            deck[0] = mouse;

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Conservation);
            walk.Capture(engine);

            Assert.That(walk.Check(engine, NoEvents), Does.StartWith("C2"));
        }

        [Test]
        public void TheWalkCatchesManaThatTheStepDoesNotExplain()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Mana(5, 5).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Conservation);
            walk.Capture(engine);

            Assert.That(engine.Play(0, engine.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.Success));

            // The mana really did move, but a step whose events say nothing about it is exactly what an
            // unexplained drain looks like from here.
            Assert.That(walk.Check(engine, NoEvents), Does.StartWith("C3"));
        }

        [Test]
        public void TheWalkCatchesADenThatDisagreesWithItsOwnRecord()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).Den(100).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Conservation);
            walk.Capture(engine);

            // A Den is only ever written by the two calls that announce the number they wrote, so a Den that
            // does not hold what the last event said is a Den something else moved.
            MatchEvent[] events = { new DenDamagedEvent(1, 5, 15) };
            Assert.That(walk.Check(engine, events), Does.StartWith("C5"));
        }

        [Test]
        public void TheWalkCatchesDamageNobodyDealt()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Conservation);
            walk.Capture(engine);

            // The critter took nothing, so a step claiming it did means the two have come apart.
            MatchEvent[] events = { new DamageDealtEvent(CardInstanceId.None, EffectTargetRef.OnCritter(frog), 2, false) };
            Assert.That(walk.Check(engine, events), Does.StartWith("C5"));
        }

        [Test]
        public void TheStartingDeckCensusCatchesACardThatStoppedBeingOne()
        {
            // The census only applies to a game that started from a real deal, so it needs its own control:
            // a flag that quietly turned a check off would look exactly like a check that always passed.
            MatchEngine engine = MatchEngine.Create(
                new MatchSetup(918273, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config)));

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Conservation, dealtFromFullDecks: true);
            walk.Capture(engine);
            Assert.That(walk.Check(engine, NoEvents), Is.Null, "a freshly dealt game accounts for both decks");

            // Same card, same zone, same count — it simply stopped claiming to have come out of a deck.
            CardInstance real = engine.Rules.Instance(engine.SecretDeck(0)[0]);
            engine.Rules.Instances[real.Id.Value] = new CardInstance(real.Id, real.Owner, real.Place, real.IsPublic, fromStartingDeck: false);

            walk.Capture(engine);
            Assert.That(walk.Check(engine, NoEvents), Does.StartWith("C1"));
        }

        [Test]
        public void TheWalkCatchesACardThatMovedWithNothingAnnouncingIt()
        {
            // C6, and the reason it is not redundant with C1 and C2: a card slid from a deck into a hand with
            // no draw on the record leaves the state agreeing with itself perfectly. The client rebuilds its
            // board from the event stream, so a move nobody announced is a move the client never makes.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.Conservation);
            walk.Capture(engine);

            // The plant is a card that slid from the deck into the hand with nothing on the record. It goes in
            // through the secret lists directly, which is exactly the move no rule makes: every real one
            // pairs the move with the counts and an event. The counts move with it, so C1b passes and C6 is
            // the check that has to catch it.
            SeatState      state  = engine.Rules.Seat(0);
            SeatSecrets    secret = engine.Model.SecretSeat(0);
            CardInstanceId top    = secret.Deck[0];
            secret.Deck.RemoveAt(0);
            secret.Hand.Add(top);
            state.SetCounts(state.HandCount + 1, state.DeckCount - 1);

            Assert.That(walk.Check(engine, NoEvents), Does.StartWith("C6"));
        }

        [Test]
        public void TheWalkCatchesAnUnseenPoolThatDriftedFromItsDerivation()
        {
            // The pool is the one public field the engine maintains incrementally rather than computing, so it
            // is the one that can silently come apart from what it is supposed to be — and a pool that has
            // drifted is a pool naming a card that is no longer where the seat thinks it is.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.StateSanity);
            walk.Capture(engine);
            Assert.That(walk.Check(engine, NoEvents), Is.Null, "the pool starts out agreeing with its derivation");

            engine.Rules.Seat(0).UnseenPool.Add(CardId.FromString("MooseWanderer"));

            Assert.That(walk.Check(engine, NoEvents), Does.StartWith("S8"));
        }

        static readonly MatchEvent[] NoEvents = new MatchEvent[0];
    }
}
