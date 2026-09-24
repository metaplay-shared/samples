using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary> The zones, the caps, and the unseen pool that the two of them have to keep honest. </summary>
    [TestFixture]
    public class ZoneTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void HandIsCappedAtNine()
        {
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxHandSize; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).DeckOf(4, "BusyBeaver");
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.SecretHand(0).Count, Is.EqualTo(Config.Global.MaxHandSize));
        }

        [Test]
        public void OverflowingDrawGoesToTheBottomOfTheDeck()
        {
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxHandSize; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            CardInstanceId top = scenario.Seat(0).Deck("BusyBeaver");
            scenario.Seat(0).Deck("PondFrog");
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            SeatState state = engine.Rules.Seat(0);
            Assert.That(engine.SecretDeck(0).Count, Is.EqualTo(2), "the card was never burned");
            Assert.That(engine.SecretDeck(0)[engine.SecretDeck(0).Count - 1], Is.EqualTo(top), "it went to the bottom");
            Assert.That(engine.ZoneOf(top), Is.EqualTo(AuthorityZone.Deck));
        }

        [Test]
        public void BounceIntoAFullHandGoesToTheDeckBottom()
        {

            // Riptide returns every critter and draws nothing, so the second one genuinely has no seat left
            // in a hand that Riptide's own departure only just made room in.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId riptide  = scenario.Seat(0).Hand("Riptide");
            for (int ndx = 0; ndx < Config.Global.MaxHandSize - 1; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            CardInstanceId firstBack  = scenario.Seat(0).Board("PondFrog");
            CardInstanceId secondBack = scenario.Seat(0).Board("StrayGoat");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            int deckBefore = engine.SecretDeck(0).Count;

            engine.Play(0, riptide, EffectTargetRef.None);

            SeatState state = engine.Rules.Seat(0);
            Assert.That(state.Board.Count, Is.EqualTo(0));
            Assert.That(engine.SecretHand(0).Count, Is.EqualTo(Config.Global.MaxHandSize));
            Assert.That(engine.ZoneOf(firstBack), Is.EqualTo(AuthorityZone.Hand), "the first one fitted");
            Assert.That(engine.ZoneOf(secondBack), Is.EqualTo(AuthorityZone.Deck), "the second one did not");
            Assert.That(engine.SecretDeck(0)[engine.SecretDeck(0).Count - 1], Is.EqualTo(secondBack), "and went to the bottom, never burned");
            Assert.That(engine.SecretDeck(0).Count, Is.EqualTo(deckBefore + 1));
            Assert.That(engine.HasEventOf<DrawOverflowedEvent>(), Is.True, "an overflow says so, whatever caused it");
        }

        [Test]
        public void BoardIsCappedAtSixPerSide()
        {
            // Driven through the engine rather than written into the fixture: a cap asserted against a board
            // the test built itself proves only that the test can count.
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxBoardCritters + 1; ndx++)
                scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(20, 20).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            for (int ndx = 0; ndx < Config.Global.MaxBoardCritters; ndx++)
                Assert.That(engine.Play(0, engine.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.Success), $"critter {ndx}");

            Assert.That(engine.Rules.Seat(0).Board.Count, Is.EqualTo(Config.Global.MaxBoardCritters));
            Assert.That(engine.Play(0, engine.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.BoardFull), "and no further");
        }

        [Test]
        public void PlayingACritterIntoAFullBoardIsRefusedBoardFull()
        {
            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxBoardCritters; ndx++)
                scenario.Seat(0).Board("MeadowMouse");
            CardInstanceId extra = scenario.Seat(0).Hand("BusyBeaver");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Play(0, extra), Is.EqualTo(MatchIntentResults.BoardFull));
        }

        [Test]
        public void SummonIntoAFullBoardFizzlesWithAnEvent()
        {

            Scenario scenario = new Scenario(Config);
            for (int ndx = 0; ndx < Config.Global.MaxBoardCritters - 1; ndx++)
                scenario.Seat(0).Board("MeadowMouse");
            CardInstanceId shepherd = scenario.Seat(0).Hand("SheepdogShepherd"); // Hello: summon two Lambs
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, shepherd, EffectTargetRef.None);

            Assert.That(engine.Rules.Seat(0).Board.Count, Is.EqualTo(Config.Global.MaxBoardCritters));

            SummonFizzledEvent fizzled = engine.LastEventOf<SummonFizzledEvent>();
            Assert.That(fizzled, Is.Not.Null, "an effect summon onto a full board fizzles with an event, it does not refuse the play");
            Assert.That(fizzled.Count, Is.EqualTo(2));
            Assert.That(fizzled.Reason, Is.EqualTo(SummonFizzleReason.BoardFull));
        }

        [Test]
        public void GraveyardIsPublicInArrivalOrder()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId notes    = scenario.Seat(0).Hand("FieldNotes");
            CardInstanceId snack    = scenario.Seat(0).Hand("BerrySnack");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, notes);
            engine.Play(0, snack, EffectTargetRef.Den(0));

            Assert.That(engine.Rules.Seat(0).Graveyard, Is.EqualTo(new List<CardInstanceId> { notes, snack }));
            // The graveyard is instance ids and the cards come off the public registry — which is the whole
            // point of the split: an id says nothing, and what it is is a separate lookup.
            List<CardId> asCards = new List<CardId>();
            foreach (CardInstanceId id in engine.Rules.Seat(0).Graveyard)
                asCards.Add(CardLookup.CardId(engine.Model, id));

            Assert.That(asCards, Is.EqualTo(new List<CardId> { CardId.FromString("FieldNotes"), CardId.FromString("BerrySnack") }));
        }

        [Test]
        public void PlayingACardRemovesItFromTheUnseenPool()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Hand("PondFrog");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Rules.Seat(0).UnseenPool, Does.Contain(CardId.FromString("PondFrog")));
            engine.Play(0, frog);
            Assert.That(engine.Rules.Seat(0).UnseenPool, Does.Not.Contain(CardId.FromString("PondFrog")));
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(0)));
        }

        [Test]
        public void DrawingDoesNotChangeTheUnseenPool()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(6, "BusyBeaver");
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            List<CardId> before = new List<CardId>(engine.Rules.Seat(0).UnseenPool);
            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(before), "a card moving between deck and hand is not becoming public");
        }

        [Test]
        public void BouncedCardStaysOutOfTheUnseenPool()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId undertow = scenario.Seat(0).Hand("Undertow");
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, undertow, EffectTargetRef.OnCritter(frog));

            Assert.That(engine.ZoneOf(frog), Is.EqualTo(AuthorityZone.Hand));
            Assert.That(engine.Rules.Seat(0).UnseenPool, Does.Not.Contain(CardId.FromString("PondFrog")),
                "the pool shrinks monotonically: a card that has been seen does not become unseen");
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(0)));
        }

        [Test]
        public void UnseenPoolMatchesDerivationAfterEveryStep()
        {
            MatchSetup  setup  = new MatchSetup(606, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);
            engine.Mulligan(0);
            engine.Mulligan(1);

            for (int step = 0; step < 4000 && engine.Phase != MatchPhase.Complete; step++)
            {
                for (int seat = 0; seat < MatchSeats.Count; seat++)
                    Assert.That(engine.Rules.Seat(seat).UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(seat)), $"seat {seat} at step {step}");

                if (engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice)
                {
                    engine.ExpireDeadline(MatchDeadlineKind.EffectChoice, null);
                    continue;
                }

                int         onTurn = engine.Rules.SeatOnTurn;
                MatchIntent intent = TestBots.Strongest.ChooseAction(engine.BuildSeatView(onTurn), onTurn)
                                     ?? new EndTurnIntent();
                engine.Submit(onTurn, intent);
            }

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
        }

        [Test]
        public void DeckAndHandCountsArePublishedSeparately()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Hand("BusyBeaver");
            scenario.Seat(0).DeckOf(7);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            SeatState view = engine.Rules.Seat(0);
            Assert.That(view.HandCount, Is.EqualTo(2));
            Assert.That(view.DeckCount, Is.EqualTo(7));
            Assert.That(view.UnseenPool.Count, Is.EqualTo(9), "the pool is the two combined, and the counts are separate");
        }

        [Test]
        public void RevealToOwnerLeavesTheUnseenPoolAlone()
        {
            Scenario       scenario  = new Scenario(Config);
            CardInstanceId collector = scenario.Seat(0).Hand("PebbleCollector"); // Hello: look at 3, keep 1
            scenario.Seat(0).Deck("StrayGoat");
            scenario.Seat(0).Deck("GreyOwl");
            scenario.Seat(0).Deck("OldBadger");
            scenario.Seat(0).DeckOf(3);
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            List<CardId> before = new List<CardId>(engine.Rules.Seat(0).UnseenPool);
            engine.Play(0, collector);

            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));

            before.Remove(CardId.FromString("PebbleCollector"));
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(before),
                "cards revealed to one player moved nowhere and are not public");
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(0)));
            Assert.That(engine.Rules.Seat(0).UnseenPool, Is.EqualTo(before));
        }
    }
}
