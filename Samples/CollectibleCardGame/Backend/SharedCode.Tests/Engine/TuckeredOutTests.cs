using Metaplay.Core;
using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The clock. Uncapped mana has no natural end, so the game needs an external one: a draw you cannot take
    /// costs you, and a full hand is a way of not taking one.
    /// </summary>
    [TestFixture]
    public class TuckeredOutTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void TuckeredOutDamageEscalatesByTheIncrementEachTick()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Den(125);   // empty deck
            scenario.Seat(1).Den(125).DeckOf(20);
            MatchEngine engine = scenario.OnTurn(0).Build();

            // The schedule is a health quantity, so it scaled with the domain: 5, then 10, then 15, which is
            // the same number of ticks to a dead Den as 1, 2, 3 was against 25.
            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(120), "5");

            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(110), "then 10");

            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(95), "then 15");
        }

        [Test]
        public void TuckeredOutCountersAreIndependentPerSeat()
        {
            // Seat 0 has one card left, so its first turn draws successfully and only the turns after that
            // tick. A counter that ticked on every turn regardless would look the same without this.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Den(125).DeckOf(1);
            scenario.Seat(1).Den(125).DeckOf(20);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();
            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(0), "the draw it could take cost it nothing");
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(125));

            for (int turn = 0; turn < 4; turn++)
                engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(2), "and the two after it did");
            Assert.That(engine.Rules.Seat(1).TuckeredOutTicks, Is.EqualTo(0));
        }

        [Test]
        public void TuckeredOutCounterNeverResets()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Den(125).Tuckered(3);
            scenario.Seat(1).Den(125).DeckOf(20);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(4), "the counter is monotonic across the match");
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(105), "and the damage picks up where it left off");
        }

        [Test]
        public void DrawEffectOnAnEmptyDeckTicksTuckeredOut()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId notes    = scenario.Seat(0).Hand("FieldNotes"); // Draw a card
            scenario.Seat(0).Mana(9, 9).Den(125);                            // empty deck
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, notes);

            // Every unsatisfiable draw, not only the turn draw.
            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(1));
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(120));
        }

        [Test]
        public void DrawIntoAFullHandTicksTuckeredOut()
        {
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxHandSize; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Den(125).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            // Without this a seat holding nine cards never empties its deck and the game has
            // no clock at all.
            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(1));
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(120));
        }

        [Test]
        public void OverflowedCardStillReachesTheDeckBottom()
        {
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxHandSize; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            CardInstanceId top = scenario.Seat(0).Deck("StrayGoat");
            scenario.Seat(0).DeckOf(3);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            SeatState state = engine.Rules.Seat(0);
            Assert.That(engine.SecretDeck(0).Count, Is.EqualTo(4), "never burned");
            Assert.That(engine.SecretDeck(0)[engine.SecretDeck(0).Count - 1], Is.EqualTo(top));
        }

        [Test]
        public void BounceIntoAFullHandDoesNotTickTuckeredOut()
        {
            // The clock is about draws a seat could not take, and a bounce is not a draw. Ticking here
            // would let an opponent's Undertow advance your clock for you.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId riptide  = scenario.Seat(0).Hand("Riptide");
            for (int ndx = 0; ndx < Config.Global.MaxHandSize - 1; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Board("StrayGoat");
            scenario.Seat(0).Mana(9, 9).Den(125).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, riptide);

            Assert.That(engine.SecretHand(0).Count, Is.EqualTo(Config.Global.MaxHandSize), "the hand really is full");
            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(0));
            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(125));
        }

        [Test]
        public void EmptyDeckAndFullHandTicksExactlyOnce()
        {
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxHandSize; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Den(125);   // and no deck at all
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(1), "one refused draw is one tick, however many ways it was refused");
        }

        [Test]
        public void TuckeredOutTicksArePublic()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Tuckered(4).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Rules.Seat(0).TuckeredOutTicks, Is.EqualTo(4),
                "settled as public, and the public deck count makes it unavoidable anyway");
        }

        [Test]
        public void TuckeredOutCanKillTheSeatOnTurn()
        {

            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Den(5).Tuckered(0);   // empty deck
            scenario.Seat(1).Den(125).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.EndTurn(0);
            engine.PassTurn();

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete), "you can lose on your own turn");
            Assert.That(engine.Result.Outcome, Is.EqualTo(MatchOutcome.Seat1Wins));
            Assert.That(engine.HasEventOf<TuckeredOutEvent>(), Is.True);
        }

        [Test]
        public void TwoPassingSeatsStillReachAResult()
        {
            // The whole point of closing C3: two players content to hold a full hand and pass must not play
            // forever.
            MatchSetup  setup  = new MatchSetup(31, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);
            engine.Mulligan(0);
            engine.Mulligan(1);

            for (int turn = 0; turn < 500 && engine.Phase != MatchPhase.Complete; turn++)
                engine.EndTurn(engine.Rules.SeatOnTurn);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
            Assert.That(engine.Result.Cause, Is.AnyOf(MatchEndCause.DenAtZero, MatchEndCause.BothDensAtZero));
        }
    }
}
