using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The contract between the engine and whatever interprets an effect. Several of these need an
    /// interpreter that real content cannot produce, which is why the boundary is an interface.
    /// </summary>
    [TestFixture]
    public class EffectBoundaryTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        Scenario Fresh()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Mana(9, 9).DeckOf(8);
            scenario.Seat(1).Mana(9, 9).DeckOf(8);
            return scenario.OnTurn(0);
        }

        [Test]
        public void EffectQueueResolvesFirstInFirstOut()
        {

            Scenario       scenario = Fresh();
            CardInstanceId undertow = scenario.Seat(0).Hand("Undertow"); // BounceChosen, then DrawOne
            CardInstanceId victim   = scenario.Seat(0).Board("MeadowMouse");
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, undertow, EffectTargetRef.OnCritter(victim));

            List<EffectResolvedEvent> resolved = engine.EventsOf<EffectResolvedEvent>();
            Assert.That(resolved.Count, Is.EqualTo(2));
            Assert.That(resolved[0].Step, Is.EqualTo(EffectStepId.FromString("BounceChosen")));
            Assert.That(resolved[1].Step, Is.EqualTo(EffectStepId.FromString("DrawOne")), "steps of one card resolve in authored order");
        }

        [Test]
        public void TriggersRaisedDuringResolutionGoToTheTail()
        {

            Scenario       scenario = Fresh();
            CardInstanceId storm    = scenario.Seat(0).Hand("CinderStorm"); // 5 damage to every enemy critter
            scenario.Seat(1).Board("SizzleWhisker", attack: 5, maxHealth: 5); // Goodbye: 5 to the enemy Den
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, storm, EffectTargetRef.None);

            List<EffectResolvedEvent> resolved = engine.EventsOf<EffectResolvedEvent>();
            Assert.That(resolved[0].Step, Is.EqualTo(EffectStepId.FromString("SweepEnemies1")));
            Assert.That(resolved[1].Step, Is.EqualTo(EffectStepId.FromString("BurnDen1")), "what a step caused goes to the tail");
        }

        [Test]
        public void NoIntentIsAcceptedMidResolution()
        {
            Scenario       scenario  = Fresh();
            CardInstanceId collector = scenario.Seat(0).Hand("PebbleCollector"); // holds the queue for a choice
            CardInstanceId other     = scenario.Seat(0).Hand("MeadowMouse");
            MatchEngine    engine    = scenario.Build();

            engine.Play(0, collector);
            Assert.That(engine.Rules.PendingChoice, Is.Not.Null);

            Assert.That(engine.Play(0, other), Is.EqualTo(MatchIntentResults.ResolutionHeld));
            Assert.That(engine.EndTurn(0), Is.EqualTo(MatchIntentResults.ResolutionHeld));
            Assert.That(engine.EndTurn(1), Is.EqualTo(MatchIntentResults.ResolutionHeld), "exclusivity survives the pause");
        }

        [Test]
        public void EngineRemovesDeadCrittersBetweenQueueItems()
        {

            Scenario       scenario = Fresh();
            CardInstanceId storm    = scenario.Seat(0).Hand("CinderStorm");
            CardInstanceId whisker  = scenario.Seat(1).Board("SizzleWhisker", attack: 5, maxHealth: 5);
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, storm, EffectTargetRef.None);

            int died     = engine.Events.FindIndex(ev => ev is CritterDiedEvent);
            int goodbye  = engine.Events.FindIndex(ev => ev is EffectResolvedEvent resolved && resolved.Step == EffectStepId.FromString("BurnDen1"));

            Assert.That(died, Is.GreaterThanOrEqualTo(0));
            Assert.That(goodbye, Is.GreaterThan(died), "the sweep happens between items, so the Goodbye reads a board without its own critter");
            Assert.That(engine.Critter(whisker), Is.Null);
        }

        [Test]
        public void InterpreterSeesAStableDeadSource()
        {
            SourceRecordingInterpreter recorder = new SourceRecordingInterpreter();

            Scenario       scenario = Fresh();
            CardInstanceId storm    = scenario.Seat(0).Hand("CinderStorm");
            CardInstanceId whisker  = scenario.Seat(1).Board("SizzleWhisker", attack: 15, maxHealth: 5);
            MatchEngine    engine   = scenario.Build(MatchTimings.Instant).WithInterpreter(recorder);

            engine.Play(0, storm);

            CardInstanceSnapshot goodbyeSource = recorder.SourceFor(EffectStepId.FromString("BurnDen1"));
            Assert.That(goodbyeSource.Id, Is.EqualTo(whisker));
            Assert.That(goodbyeSource.WasOnBoard, Is.True);
            Assert.That(goodbyeSource.Attack, Is.EqualTo(15), "read as it was at the moment it died");
            Assert.That(engine.Critter(whisker), Is.Null, "…even though the board no longer has it");
        }

        [Test]
        public void MutationsGoThroughZoneOpsAndRespectCaps()
        {
            HandFloodingInterpreter flooder = new HandFloodingInterpreter(20);

            Scenario       scenario = Fresh();
            CardInstanceId scholar  = scenario.Seat(0).Hand("TideScholar");
            MatchEngine    engine   = scenario.Build(MatchTimings.Instant).WithInterpreter(flooder);

            int deckBefore = engine.SecretDeck(0).Count;
            engine.Play(0, scholar);

            SeatState state = engine.Rules.Seat(0);
            Assert.That(engine.SecretHand(0).Count, Is.LessThanOrEqualTo(Config.Global.MaxHandSize), "the cap applies no matter which effect asked");
            Assert.That(engine.SecretDeck(0).Count, Is.GreaterThan(deckBefore), "the overflow went to the bottom, never burned");
            Assert.That(state.UnseenPool, Is.EqualTo(engine.DeriveUnseenPool(0)), "and the pool is still its derivation");
        }

        [Test]
        public void EffectContextExposesNoMutableState()
        {
            foreach (PropertyInfo property in typeof(IEffectContext).GetProperties())
                Assert.That(property.CanWrite, Is.False, $"{property.Name} is writable");

            foreach (MethodInfo method in typeof(IEffectContext).GetMethods())
            {
                if (method.IsSpecialName)
                    continue;

                Assert.That(method.ReturnType, Is.Not.EqualTo(typeof(void)), $"{method.Name} returns nothing, so it can only be a mutation");
                Assert.That(method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(List<>), Is.False,
                    $"{method.Name} hands back a writable collection");
            }
        }

        [Test]
        public void MultiTargetEffectResolvesInCanonicalOrder()
        {

            Scenario       scenario = Fresh();
            CardInstanceId storm    = scenario.Seat(0).Hand("CinderStorm");
            CardInstanceId first    = scenario.Seat(1).Board("WiseTortoise");
            CardInstanceId second   = scenario.Seat(1).Board("GreyOwl");
            CardInstanceId third    = scenario.Seat(1).Board("HillPony");
            MatchEngine    engine   = scenario.Build();

            engine.Play(0, storm, EffectTargetRef.None);

            List<DamageDealtEvent> damage = engine.EventsOf<DamageDealtEvent>();
            Assert.That(damage.Count, Is.EqualTo(3));
            Assert.That(damage[0].Target.Critter, Is.EqualTo(first));
            Assert.That(damage[1].Target.Critter, Is.EqualTo(second));
            Assert.That(damage[2].Target.Critter, Is.EqualTo(third), "entered-play order, never a lookup's order");
        }

        [Test]
        public void SelfReenqueueingEffectHitsTheDrainCapAndFails()
        {
            RunawayInterpreter runaway = new RunawayInterpreter();

            Scenario       scenario = Fresh();
            CardInstanceId scholar  = scenario.Seat(0).Hand("TideScholar");
            MatchEngine    engine   = scenario.Build(MatchTimings.Instant).WithInterpreter(runaway);

            // A failure, not a policy: the engine refuses to keep going rather than quietly capping the game.
            MatchEngineException failure = Assert.Throws<MatchEngineException>(() => engine.Play(0, scholar));
            Assert.That(failure.Message, Does.Contain("drain cap"));
        }

        [Test]
        public void DamageThatLandedOnNothingFeedsNoSnacktime()
        {
            // A step whose target has already died resolves as a no-op. A no-op is not damage the critter
            // dealt, so it must not heal a Snacktime Den or reveal a Sneaky critter.
            VanishedTargetInterpreter interpreter = new VanishedTargetInterpreter();

            Scenario scenario = Fresh();
            scenario.Seat(0).Board("DawnShepherd", keywords: KeywordFlags.Snacktime | KeywordFlags.Sneaky); // TurnStart trigger
            scenario.Seat(0).Den(100);
            MatchEngine engine = scenario.Build(MatchTimings.Instant).WithInterpreter(interpreter);

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(100), "nothing was damaged, so nothing was healed");
            Assert.That(engine.Rules.Seat(0).Board[0].HasSneaky, Is.True, "and nothing was revealed");
        }

        [Test]
        public void ARankTrackScalesOnlyTheStepsPrimaryAmount()
        {
            // Pebble Collector looks at three and keeps one. A track that scaled the second amount too would
            // quietly turn "restore one more" into "keep one more" on every card that has both.
            SecondAmountRecordingInterpreter interpreter = new SecondAmountRecordingInterpreter();

            Scenario scenario = Fresh();
            scenario.Seat(0).Hand("PebbleCollector", rank: 5);
            scenario.Seat(0).DeckOf(8, "GreyOwl");
            MatchEngine engine = scenario.Build(MatchTimings.Instant).WithInterpreter(interpreter);

            engine.Play(0, engine.SecretHand(0)[0]);

            Assert.That(interpreter.LookCount, Is.EqualTo(3));
            Assert.That(interpreter.KeepCount, Is.EqualTo(1));
        }

        [Test]
        public void APlayedThisMatchCounterCountsTheHistoryAndExcludesItself()
        {
            // Nine-Tail Matriarch burns the enemy Den for each *other* Kitsune card its owner has played. An
            // off-by-one here would otherwise only ever show up as a changed golden hash.
            Scenario scenario = Fresh();
            scenario.Seat(0).Played("EmberKit").Played("Foxfire").Played("MeadowMouse"); // two Kitsune, one Wanderer
            CardInstanceId matriarch = scenario.Seat(0).Hand("NineTailMatriarch");
            MatchEngine    engine    = scenario.Build();

            engine.Play(0, matriarch);

            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(Config.Global.DenStartingHp - 10),
                "two other Kitsune cards: the Wanderer does not count and neither does the Matriarch itself");
        }

        [Test]
        public void APlayedThisMatchCounterFollowsTheHistoryAsItGrows()
        {
            Scenario       scenario  = Fresh();
            CardInstanceId kit       = scenario.Seat(0).Hand("EmberKit");
            CardInstanceId matriarch = scenario.Seat(0).Hand("NineTailMatriarch");
            MatchEngine    engine    = scenario.Build();

            engine.Play(0, matriarch);
            Assert.That(engine.Rules.Seat(1).DenHp, Is.EqualTo(Config.Global.DenStartingHp), "nothing played yet, nothing counted");

            engine.Play(0, kit);
            Assert.That(engine.Rules.Seat(0).PlayedThisMatch.Count, Is.EqualTo(2));
        }

        [Test]
        public void AFriendlyCritterCounterCountsTheBoardAtResolutionTime()
        {
            // Dawn Shepherd heals its owner's Den by one for each friendly critter, at the start of its
            // owner's turn — so the count includes itself and everything beside it.
            Scenario scenario = Fresh();
            scenario.Seat(0).Board("DawnShepherd");
            scenario.Seat(0).Board("MeadowMouse");
            scenario.Seat(0).Board("BusyBeaver");
            scenario.Seat(1).Board("GreyOwl");
            scenario.Seat(0).Den(75);
            MatchEngine engine = scenario.Build();

            engine.PassTurn();
            engine.PassTurn();

            Assert.That(engine.Rules.Seat(0).DenHp, Is.EqualTo(90), "three friendly critters, and the enemy's does not count");
        }

        [Test]
        public void WeatherAndGoodbyeInterleavePerDeath()
        {
            // "The sky acts before the critters" is scoped to one event. A sweep that kills three critters is
            // three deaths, so the queue reads Weather-then-Goodbye three times rather than three Weathers
            // followed by three Goodbyes.

            Scenario scenario = new Scenario(Config, weatherId: "PicnicDay"); // a critter died: heal its owner
            CardInstanceId storm = scenario.Seat(0).Hand("CinderStorm");
            scenario.Seat(1).Board("SizzleWhisker", attack: 5, maxHealth: 5);  // Goodbye: 5 to the enemy Den
            scenario.Seat(1).Board("SizzleWhisker", attack: 5, maxHealth: 5);
            scenario.Seat(0).Mana(9, 9).DeckOf(8).Den(100);
            scenario.Seat(1).Mana(9, 9).DeckOf(8).Den(100);
            MatchEngine engine = scenario.OnTurn(0).Build();

            engine.Play(0, storm, EffectTargetRef.None);

            List<EffectStepId> order = new List<EffectStepId>();
            foreach (EffectResolvedEvent ev in engine.EventsOf<EffectResolvedEvent>())
                order.Add(ev.Step);

            Assert.That(order, Is.EqualTo(new List<EffectStepId>
            {
                EffectStepId.FromString("SweepEnemies1"),
                EffectStepId.FromString("HealOwnerDen1"),  // the sky, for the first death
                EffectStepId.FromString("BurnDen1"),       // then that critter's own Goodbye
                EffectStepId.FromString("HealOwnerDen1"),  // and again for the second
                EffectStepId.FromString("BurnDen1"),
            }));
        }

        [Test]
        public void QueueIsDrainedAtEveryDefinedResolutionPointAndNoOther()
        {
            MatchSetup  setup  = new MatchSetup(515, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);
            engine.Mulligan(0);
            engine.Mulligan(1);

            for (int step = 0; step < 4000 && engine.Phase != MatchPhase.Complete; step++)
            {
                if (engine.Pending.Kind == MatchPendingKind.AwaitingEffectChoice)
                {
                    Assert.That(engine.Rules.EffectQueue, Is.Not.Null, "the queue is held, not abandoned");
                    engine.ExpireDeadline(MatchDeadlineKind.EffectChoice, null);
                    continue;
                }

                Assert.That(engine.Rules.EffectQueue.Count, Is.EqualTo(0),
                    "the queue is drained to empty at every resolution point and never left partly drained");

                int         seat   = engine.Rules.SeatOnTurn;
                MatchIntent intent = TestBots.Strongest.ChooseAction(engine.BuildSeatView(seat), seat)
                                     ?? new EndTurnIntent();
                engine.Submit(seat, intent);
            }

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
            Assert.That(engine.Rules.EffectQueue.Count, Is.EqualTo(0));
        }
    }

    /// <summary> Records the source snapshot each step was handed, then does what the launch interpreter does. </summary>
    public sealed class SourceRecordingInterpreter : IEffectInterpreter
    {
        readonly Dictionary<EffectStepId, CardInstanceSnapshot> _sources = new Dictionary<EffectStepId, CardInstanceSnapshot>();

        public CardInstanceSnapshot SourceFor(EffectStepId step) => _sources[step];

        public void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut)
        {
            _sources[item.Info.StepId] = item.Source;
            StepInterpreter.Instance.Resolve(item, ctx, mut);
        }
    }

    /// <summary> Pushes far more into a hand than it can hold, to prove the cap is the engine's and not the effect's. </summary>
    public sealed class HandFloodingInterpreter : IEffectInterpreter
    {
        readonly int _count;

        public HandFloodingInterpreter(int count)
        {
            _count = count;
        }

        public void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut)
        {
            CardInfo card = ctx.Config.Cards[CardId.FromString("MeadowMouse")];
            for (int ndx = 0; ndx < _count; ndx++)
                mut.AddCopyToHand(item.ResolvingSeat, card);
        }
    }

    /// <summary> Aims every step at an identity that names nothing, to prove a no-op stays a no-op. </summary>
    public sealed class VanishedTargetInterpreter : IEffectInterpreter
    {
        public void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut)
            => mut.DealDamage(EffectTargetRef.OnCritter(new CardInstanceId(9999)), 3);
    }

    /// <summary> Records the two amounts a Peek step resolved for, so the rank track's reach can be asserted. </summary>
    public sealed class SecondAmountRecordingInterpreter : IEffectInterpreter
    {
        public int LookCount { get; private set; } = -1;
        public int KeepCount { get; private set; } = -1;

        public void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut)
        {
            if (item.Info.Op != EffectOp.Peek)
                return;

            LookCount = -1;
            KeepCount = -1;
            StepInterpreter.Instance.Resolve(item, ctx, new Recorder(this, mut));
        }

        sealed class Recorder : IEffectMutations
        {
            readonly SecondAmountRecordingInterpreter _owner;
            readonly IEffectMutations                 _inner;

            public Recorder(SecondAmountRecordingInterpreter owner, IEffectMutations inner)
            {
                _owner = owner;
                _inner = inner;
            }

            public void PeekDeck(int seat, int lookCount, int keepCount)
            {
                _owner.LookCount = lookCount;
                _owner.KeepCount = keepCount;
                _inner.PeekDeck(seat, lookCount, keepCount);
            }

            public void DealDamage(EffectTargetRef target, int amount) => _inner.DealDamage(target, amount);
            public void Heal(EffectTargetRef target, int amount) => _inner.Heal(target, amount);
            public void Draw(int seat, int count) => _inner.Draw(seat, count);
            public void GainMana(int seat, int amount, ManaDuration duration) => _inner.GainMana(seat, amount, duration);
            public void Buff(CardInstanceId critter, int attackDelta, int maxHealthDelta) => _inner.Buff(critter, attackDelta, maxHealthDelta);
            public void GrantKeywords(CardInstanceId critter, KeywordFlags keywords) => _inner.GrantKeywords(critter, keywords);
            public void Summon(int seat, CardInfo card, int count) => _inner.Summon(seat, card, count);
            public void Bounce(CardInstanceId critter) => _inner.Bounce(critter);
            public void AddCopyToHand(int seat, CardInfo card) => _inner.AddCopyToHand(seat, card);
        }
    }

    /// <summary>
    /// An interpreter that never lets the queue finish: every step summons a critter with a Goodbye and kills
    /// it, so each item enqueues another. No config can compose this, which is exactly why the drain cap is
    /// tested through the boundary rather than through content.
    /// </summary>
    public sealed class RunawayInterpreter : IEffectInterpreter
    {
        public void Resolve(EffectQueueItem item, IEffectContext ctx, IEffectMutations mut)
        {
            CardInfo bomb = ctx.Config.Cards[CardId.FromString("SizzleWhisker")];
            mut.Summon(item.ResolvingSeat, bomb, 1);

            IReadOnlyList<BoardCritter> board = ctx.Board(item.ResolvingSeat);
            if (board.Count > 0)
                mut.Buff(board[board.Count - 1].Id, 0, -99);
        }
    }
}
