using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary> The start-of-turn package, the main phase, and the explicit end. </summary>
    [TestFixture]
    public class TurnFlowTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        MatchEngine Dealt(ulong seed)
        {
            MatchSetup  setup  = new MatchSetup(seed, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);
            engine.Mulligan(0);
            engine.Mulligan(1);
            return engine;
        }

        [Test]
        public void StartOfTurnRunsRampRefillDrawWakeInOrder()
        {

            // A critter that needs waking, so the last step of the package is observable rather than assumed.
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Board("PondFrog", sleepy: true);
            scenario.Seat(0).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.Clear();
            engine.PassTurn();

            List<string> order = new List<string>();
            foreach (MatchEvent ev in engine.Events)
            {
                switch (ev)
                {
                    case TurnStartedEvent _:  order.Add("turn"); break;
                    // The ramp and the refill are one event on purpose: they always happen together and a
                    // client has one number to animate, so there is nothing to interleave between them.
                    case ManaChangedEvent _:  order.Add("mana"); break;
                    case CardDrawnEvent _:    order.Add("draw"); break;
                    case CrittersWokeEvent _: order.Add("wake"); break;
                }
            }

            Assert.That(order, Is.EqualTo(new List<string> { "turn", "mana", "draw", "wake" }));
            Assert.That(engine.Rules.Seat(0).Board[0].IsSleepy, Is.False);
        }

        [Test]
        public void BothSeatsDrawOnTheirOwnFirstTurn()
        {
            MatchEngine        engine    = Dealt(seed: 21);
            int                first     = engine.Rules.SeatOnTurn;

            Assert.That(engine.LastEventOf<CardDrawnEvent>().Seat, Is.EqualTo(first));

            engine.Clear();
            engine.PassTurn();

            // Hearthstone parity: the second seat draws on its own first turn too.
            Assert.That(engine.LastEventOf<CardDrawnEvent>().Seat, Is.EqualTo(MatchSeats.Other(first)));
        }

        [Test]
        public void WakingClearsSleepyAndHasAttacked()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog", sleepy: true, hasAttacked: true);
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Critter(frog).IsSleepy, Is.False);
            Assert.That(engine.Critter(frog).HasAttackedThisTurn, Is.False);
        }

        [Test]
        public void ManyActionsInAnyOrderAreAccepted()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId mouse    = scenario.Seat(0).Hand("MeadowMouse");
            CardInstanceId beaver   = scenario.Seat(0).Hand("BusyBeaver");
            CardInstanceId attacker = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Play(0, mouse), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Attack(0, attacker, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.Play(0, beaver), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(engine.ActionCount, Is.EqualTo(3));
        }

        [Test]
        public void TurnDoesNotEndImplicitlyWhenNothingIsPlayable()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(0, 0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            // Nothing is playable and nothing can attack, so the only legal action left is ending the turn.
            Assert.That(engine.EnumerateLegalIntents(0).Count, Is.EqualTo(1));
            Assert.That(engine.EnumerateLegalIntents(0)[0], Is.InstanceOf<EndTurnIntent>());

            // Offering the engine something it will refuse must not end the turn for the seat either.
            Assert.That(engine.Attack(0, new CardInstanceId(0), EffectTargetRef.Den(1)),
                Is.EqualTo(MatchIntentResults.NotYourCritter));

            Assert.That(engine.HasEventOf<TurnEndedEvent>(), Is.False);
            Assert.That(engine.Rules.SeatOnTurn, Is.EqualTo(0));
            Assert.That(engine.Turn, Is.EqualTo(1));
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingIntent),
                "a seat with nothing to do still has to say it is finished");

            // And when it does say so, the turn moves.
            engine.EndTurn(0);
            Assert.That(engine.Rules.SeatOnTurn, Is.EqualTo(1));
        }

        [Test]
        public void OffTurnSeatIsRefusedNotYourTurn()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.EndTurn(1), Is.EqualTo(MatchIntentResults.NotYourTurn));
        }

        [Test]
        public void TurnNumberCountsBothSeatsTurns()
        {
            MatchEngine engine = Dealt(seed: 21);

            Assert.That(engine.Turn, Is.EqualTo(1));
            engine.PassTurn();
            Assert.That(engine.Turn, Is.EqualTo(2));
            engine.PassTurn();
            Assert.That(engine.Turn, Is.EqualTo(3));
            Assert.That(engine.Rules.SeatOnTurn, Is.EqualTo(engine.Rules.FirstSeat));
        }

        [Test]
        public void EndingATurnIsOneActionThatAlsoOpensTheNext()
        {
            Scenario       scenario = new Scenario(Config);
            CardInstanceId mouse    = scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, mouse);
            Assert.That((engine.Turn, engine.ActionCount), Is.EqualTo((1, 1)));

            engine.PassTurn();

            // The turn advance and the whole start-of-turn package run inside the action that ended the turn.
            Assert.That((engine.Turn, engine.ActionCount), Is.EqualTo((2, 2)));
            Assert.That(engine.Rules.ActedThisTurn, Is.False);
        }

        [Test]
        public void EndOfTurnTriggersResolveBeforeTheNextTurnStarts()
        {

            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Board("MossyYearling"); // TurnEnd: this critter gains +1/+1
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.EndTurn(0);

            int growth = engine.Events.FindIndex(ev => ev is StatsChangedEvent);
            int ended  = engine.Events.FindIndex(ev => ev is TurnEndedEvent);
            int started = engine.Events.FindIndex(ev => ev is TurnStartedEvent);

            Assert.That(growth, Is.GreaterThanOrEqualTo(0), "the end-of-turn effect resolved");
            Assert.That(ended, Is.GreaterThan(growth), "the turn is announced ended only once its effects have resolved");
            Assert.That(started, Is.GreaterThan(ended), "and the next turn starts after that");
        }
    }
}
