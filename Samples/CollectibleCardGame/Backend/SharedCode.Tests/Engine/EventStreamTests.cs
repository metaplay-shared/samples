using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The events are the engine's real product, and every one of them is public-safe by construction. This is
    /// the suite that keeps that true as events are added, because "not included" is a promise about every
    /// future field nobody has written yet.
    /// </summary>
    [TestFixture]
    public class EventStreamTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void NoEventNamesANonPublicCard()
        {
            MatchSetup         setup     = new MatchSetup(1234, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine        engine    = MatchEngine.Create(setup);

            engine.Mulligan(0);
            engine.Mulligan(1);

            for (int step = 0; step < 4000 && engine.Phase != MatchPhase.Complete; step++)
            {
                engine.Clear();

                if (engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice)
                    engine.ExpireDeadline(MatchDeadlineKind.EffectChoice, null);
                else
                {
                    int         seat   = engine.Rules.SeatOnTurn;
                    MatchIntent intent = TestBots.Strongest.ChooseAction(engine.BuildSeatView(seat), seat)
                                         ?? new EndTurnIntent();
                    engine.Submit(seat, intent);
                }

                foreach (MatchEvent ev in engine.Events)
                {
                    string leak = FindLeak(engine, ev);
                    Assert.That(leak, Is.Null, $"step {step}: {leak}");
                }
            }

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
        }

        [Test]
        public void NoEventNamesANonPublicCard_NegativeControl()
        {
            // "Nothing leaked" is also what a check that walked nothing would report. These are the cases that
            // must fail.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId secret   = scenario.Seat(0).Hand("MooseWanderer");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchEvent named = new CardPlayedEvent(0, secret, CardId.FromString("MooseWanderer"), 1, EffectTargetRef.None, 0);
            Assert.That(FindLeak(engine, named), Is.Not.Null, "an identity named directly on the event");

            // …and the one the walk used to miss entirely: an identity buried inside a struct field. Most
            // events name their target through one, so a walk that stopped at the struct boundary was
            // checking almost nothing and reporting that everything was fine.
            MatchEvent buried = new DamageDealtEvent(CardInstanceId.None, EffectTargetRef.OnCritter(secret), 3, false);
            Assert.That(FindLeak(engine, buried), Is.Not.Null, "an identity inside an EffectTargetRef");
        }

        [Test]
        public void TheLeakWalkReachesIdentitiesInsideStructFields()
        {
            // The guard on the guard: prove the walk actually descends into EffectTargetRef rather than
            // happening to pass because nothing ever leaked.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId critter  = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).DeckOf(4);
            scenario.Seat(1).DeckOf(4);
            scenario.OnTurn(0).Build();

            MatchEvent ev = new DamageDealtEvent(CardInstanceId.None, EffectTargetRef.OnCritter(critter), 2, false);

            Assert.That(CollectInstanceIds(ev), Does.Contain(critter));
        }

        /// <summary>
        /// Reflect over everything an event reaches and report the first identity it names that is not public.
        /// Doing it by reflection rather than per event type is the point: an event added later is covered
        /// without anybody remembering to extend this.
        /// <para>
        /// It walks <em>into</em> structs, which is not a detail — most events name their target through an
        /// <see cref="EffectTargetRef"/>, and a walk that stopped at the struct would have been checking
        /// almost nothing while reporting that everything was fine.
        /// </para>
        /// </summary>
        static string FindLeak(MatchEngine engine, MatchEvent ev)
        {
            foreach (CardInstanceId id in CollectInstanceIds(ev))
            {
                CardInstance instance = engine.Rules.TryGetInstance(id);
                if (instance == null)
                    return $"{ev.GetType().Name} names instance {id}, which does not exist";
                if (!instance.IsPublic)
                    return $"{ev.GetType().Name} names {CardLookup.CardId(engine.Model, instance.Id)}, which is not public";
            }

            foreach (CardId card in CollectCardIds(ev))
            {
                if (IsSecretCard(engine, card))
                    return $"{ev.GetType().Name} names {card}, which is still in somebody's unseen pool";
            }

            return null;
        }

        /// <summary>
        /// A catalogue identity leaks when it is still in a seat's unseen pool and nothing public is that
        /// card. Both halves matter: a token that was never in a deck is in no pool and is safe to name, and
        /// a card one seat has played is safe to name even while the other seat holds a copy of it.
        /// </summary>
        static bool IsSecretCard(MatchEngine engine, CardId card)
        {
            bool inSomePool = false;
            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                if (engine.Rules.Seat(seat).UnseenPool.Contains(card))
                    inSomePool = true;
            }

            return inSomePool && !NamesAPublicCard(engine, card);
        }

        static bool NamesAPublicCard(MatchEngine engine, CardId card)
        {
            foreach (CardInstance instance in engine.Rules.Instances)
            {
                if (instance.IsPublic && instance.CardId == card)
                    return true;
            }

            return false;
        }

        [Test]
        public void ADebugFormatterDoesNotPrintAHiddenCard()
        {
            // A log line or a debugger watch is the last place a hidden hand should be readable, and this is
            // the only formatter in the engine that could have printed one.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId inHand   = scenario.Seat(0).Hand("MooseWanderer");
            CardInstanceId inDeck   = scenario.Seat(0).Deck("GreyOwl");
            CardInstanceId onBoard  = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(1).DeckOf(4);
            MatchEngine engine = scenario.OnTurn(0).Build();

            Assert.That(engine.Rules.Instance(inHand).ToString(), Does.Not.Contain("MooseWanderer"));
            Assert.That(engine.Rules.Instance(inDeck).ToString(), Does.Not.Contain("GreyOwl"));
            Assert.That(engine.Rules.Instance(onBoard).ToString(), Does.Contain("PondFrog"), "a public card is not redacted");
        }

        [Test]
        public void CardDrawnCarriesNoIdentity()
        {
            foreach (PropertyInfo property in typeof(CardDrawnEvent).GetProperties())
                Assert.That(property.PropertyType, Is.EqualTo(typeof(int)), $"{property.Name} could name the drawn card");
        }

        [Test]
        public void DrawOverflowedCarriesNoIdentity()
        {
            foreach (PropertyInfo property in typeof(DrawOverflowedEvent).GetProperties())
                Assert.That(property.PropertyType, Is.EqualTo(typeof(int)), $"{property.Name} could name the overflowed card");
        }

        [Test]
        public void ThePublicRulesStateNamesEveryFieldExplicitly()
        {
            // A tripwire rather than a proof: adding a member to the public rules state has to be a
            // deliberate edit here too, which is what stops one arriving by accident. It matters more than it
            // did — the public half is on the replicated model now, so a new member is a new thing both
            // clients hold rather than a new field in a projection somebody built.
            Assert.That(typeof(MatchRulesState).GetProperties().Length, Is.EqualTo(15));
            Assert.That(typeof(SeatState).GetProperties().Length, Is.EqualTo(13));
            Assert.That(typeof(BoardCritter).GetProperties().Length, Is.EqualTo(14), "eight stored members and six derived");

            foreach (PropertyInfo property in typeof(SeatState).GetProperties())
            {
                Assert.That(property.Name, Is.Not.EqualTo("Hand"), "the public seat state has no hand: it has a count");
                Assert.That(property.Name, Is.Not.EqualTo("Deck"), "the public seat state has no deck order: it has a count");
            }
        }

        [Test]
        public void SeatViewHasNoFieldForTheOpponentHandOrDeckOrder()
        {
            MatchSetup  setup  = new MatchSetup(4242, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);
            engine.Mulligan(0);
            engine.Mulligan(1);

            SeatView view = engine.BuildSeatView(0);

            // Structural: the only hand on the type is one seat's own, and the view says whose.
            Assert.That(view.Seat, Is.EqualTo(0));

            // An exact allow-list, name and type, rather than a refusal of one type that would obviously be
            // wrong. The weaker form let a review add `public readonly MatchModel Root;` — the model root,
            // which carries the secret — with both shape tests still green. What the doc promises is that a
            // field is a deliberate edit; this is what makes that true, because a new field fails here until
            // somebody writes it down.
            List<string> members = new List<string>();
            foreach (FieldInfo field in typeof(SeatView).GetFields())
                members.Add($"{field.Name}: {field.FieldType.Name}");

            members.Sort();
            Assert.That(members, Is.EqualTo(new List<string>
            {
                "Hand: IReadOnlyList`1",
                "LegalActions: IReadOnlyList`1",
                "Pacing: MatchPacing",
                "Pending: MatchPendingWork",
                "PendingChoice: PendingChoiceView",
                "Rules: MatchRulesState",
                "Seat: Int32",
                "Seats: IReadOnlyList`1",
                "Stakes: MatchStakes",
                "Timings: MatchTimings",
            }), "the seat view's members are an allow-list: a new one is a secrecy decision, not a convenience");

            // …and behavioural, which is the half that catches an identity leaking through some other member.
            //
            // What it can claim changed with the model's shape, and the change is worth stating. Instance
            // IDENTIFIERS are public now: the registry is dense and replicated, so the opponent's hidden
            // cards do have entries in it, at their own indices. What those entries must not carry is what
            // the card IS — so the claim is about identities rather than about ids, which is exactly the
            // line CardPlace.Unseen draws.
            foreach (CardInstance instance in view.Rules.Instances)
            {
                if (instance.IsPublic)
                    continue;

                Assert.That(instance.IsKnown, Is.False, $"{instance.Id} is hidden but its entry names a card");
                Assert.That(instance.Place, Is.EqualTo(CardPlace.Unseen), $"{instance.Id} says which hidden zone it is in");
            }

            // And the one hand the view does carry is this seat's own.
            HashSet<CardInstanceId> ownHand = new HashSet<CardInstanceId>();
            foreach (HandCard card in view.Hand)
                ownHand.Add(card.Instance);

            foreach (CardInstanceId id in engine.SecretHand(1))
                Assert.That(ownHand, Does.Not.Contain(id), "the delivered hand is one seat's own");
        }

        static List<CardInstanceId> CollectInstanceIds(object root)
        {
            List<CardInstanceId> found = new List<CardInstanceId>();
            Walk(root, found, null, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            return found;
        }

        static List<CardId> CollectCardIds(object root)
        {
            List<CardId> found = new List<CardId>();
            Walk(root, null, found, new HashSet<object>(ReferenceEqualityComparer.Instance), 0);
            return found;
        }

        /// <summary>
        /// Walk an object graph collecting the identities it names. Structs are walked like anything else,
        /// which is the whole reason this exists rather than a switch over an event's own properties.
        /// </summary>
        static void Walk(object value, List<CardInstanceId> instances, List<CardId> cards, HashSet<object> visited, int depth)
        {
            if (value == null || depth > 8)
                return;

            if (value is CardInstanceId id)
            {
                if (id.IsValid)
                    instances?.Add(id);
                return;
            }

            if (value is CardId card)
            {
                cards?.Add(card);
                return;
            }

            if (value is string || value.GetType().IsPrimitive || value.GetType().IsEnum)
                return;

            if (value is IEnumerable list)
            {
                foreach (object item in list)
                    Walk(item, instances, cards, visited, depth + 1);
                return;
            }

            if (!value.GetType().IsValueType && !visited.Add(value))
                return;

            foreach (PropertyInfo property in value.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length == 0)
                    Walk(property.GetValue(value), instances, cards, visited, depth + 1);
            }

            foreach (FieldInfo field in value.GetType().GetFields())
                Walk(field.GetValue(value), instances, cards, visited, depth + 1);
        }

        [Test]
        public void EveryAcceptedIntentPublishesAStep()
        {

            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            CardInstanceId mouse    = engine.SecretHand(0)[0];
            CardInstanceId attacker = engine.Rules.Seat(0).Board[0].Id;

            engine.Play(0, mouse, EffectTargetRef.None);
            Assert.That(engine.Actions.Count, Is.EqualTo(1));
            Assert.That(engine.LastEvents.Count, Is.GreaterThan(0));

            engine.Attack(0, attacker, EffectTargetRef.Den(1));
            Assert.That(engine.Actions.Count, Is.EqualTo(2));

            engine.EndTurn(0);
            Assert.That(engine.Actions.Count, Is.EqualTo(3));
        }

        [Test]
        public void TheHandRevisionSaysWhoseHandChanged()
        {

            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, engine.SecretHand(0)[0], EffectTargetRef.None);

            Assert.That(engine.HandChangedInLastCall(0), Is.True, "the host knows whose correction to send");
            Assert.That(engine.HandChangedInLastCall(1), Is.False);
        }

        [Test]
        public void TheStartOfTurnDrawMarksTheDrawingSeatsHand()
        {
            // The most frequent correction of all. The handover runs inside the action that ended the turn,
            // so the draw and its correction arrive on that one step.

            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build(MatchTimings.Default);

            engine.Clear();
            engine.EndTurn(0);

            Assert.That(engine.Actions.Count, Is.EqualTo(1));
            Assert.That(engine.HandChangedInLastCall(1), Is.True, "seat 1 drew");
            Assert.That(engine.HandChangedInLastCall(0), Is.False);
        }

        [Test]
        public void TheMulliganMarksBothHands()
        {
            MatchSetup         setup     = new MatchSetup(77, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine        engine    = MatchEngine.Create(setup);

            // Each seat asks for a card that actually came out of its own deck, which is the only kind the
            // mulligan may name — and at the deal the whole of both hands is that kind.
            Assert.That(engine.Mulligan(0, FirstReplaceable(engine, 0)), Is.EqualTo(MatchIntentResults.Success));
            engine.Clear();
            Assert.That(engine.Mulligan(1, FirstReplaceable(engine, 1)), Is.EqualTo(MatchIntentResults.Success));

            Assert.That(engine.Actions.Count, Is.EqualTo(1));
            Assert.That(engine.HandChangedInLastCall(0), Is.True, "both hands were rewritten server-side with no play");
            Assert.That(engine.HandChangedInLastCall(1), Is.True);
        }

        static CardInstanceId FirstReplaceable(MatchEngine engine, int seat)
        {
            foreach (CardInstanceId id in engine.SecretHand(seat))
            {
                if (engine.Rules.Instance(id).FromStartingDeck)
                    return id;
            }

            throw new AssertionException($"seat {seat} was dealt nothing it may replace");
        }

        [Test]
        public void ABounceMarksTheOwnersHandNotTheActingSeats()
        {
            // Seat 0 acts; seat 1's hand is the one that changed. A flag that followed the actor would send
            // the correction to the wrong client.

            Scenario       scenario = new Scenario(Config);
            CardInstanceId undertow = scenario.Seat(0).Hand("Undertow");
            CardInstanceId victim   = scenario.Seat(1).Board("PondFrog");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, undertow, EffectTargetRef.OnCritter(victim));

            Assert.That(engine.ZoneOf(victim), Is.EqualTo(AuthorityZone.Hand));
            Assert.That(engine.Rules.Instance(victim).Owner, Is.EqualTo(1));
            Assert.That(engine.HandChangedInLastCall(1), Is.True, "the bounced critter went to its owner's hand");
            Assert.That(engine.HandChangedInLastCall(0), Is.True, "and the acting seat played a card and drew one");
        }
    }
}
