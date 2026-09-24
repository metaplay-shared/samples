using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What the rules promise the host that drives them: stamps they never evaluate, a reserve rule the host
    /// routes on rather than decides, and a game the host can finish in one call.
    /// <para>
    /// There is no publisher contract left to test. There is one state and the action <em>is</em> the commit,
    /// so publish-before-commit dissolved rather than being preserved — and with it the eleven cases about a
    /// host that refused a step, the rollback that put one back, and the clone that made a rollback possible.
    /// What survives is the half that matters more now: a host cannot get a stamp wrong without the follower
    /// disagreeing about it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class HostDrivingTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary>
        /// Seat 0 on turn with one card and mana to spend. Restored states carry no armed deadline, so this
        /// walks two turn boundaries first: the engine arms the turn deadline itself, which is the state a
        /// host would ever actually see.
        /// </summary>
        MatchEngine Simple(MatchTimings timings = default(MatchTimings))
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);

            MatchEngine engine = scenario.OnTurn(0).Build(timings);
            engine.PassTurn();
            engine.PassTurn();
            return engine;
        }






        [Test]
        public void TheRulesNeverCompareTwoTimestamps()
        {
            // A reflection guard rather than a reading of the source: no pure rules query may take a time at
            // all, and the rules half of the state holds no absolute stamp for one to be compared against.
            Type[] pureRules = { typeof(Legality), typeof(ManaRules), typeof(KeywordRules), typeof(WeatherModifiers), typeof(CombatRules) };

            foreach (Type type in pureRules)
            {
                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(MetaTime)), $"{type.Name}.{method.Name} takes a time");
                        Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(MetaDuration)), $"{type.Name}.{method.Name} takes a duration");
                    }
                }
            }

            foreach (PropertyInfo property in typeof(MatchRulesState).GetProperties())
            {
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(MetaTime)), $"{property.Name} is an absolute stamp in the rules half");
                Assert.That(property.PropertyType, Is.Not.EqualTo(typeof(MetaTime?)), $"{property.Name} is an absolute stamp in the rules half");
            }
        }

        [Test]
        public void ADeadlineIsStampedFromTheModelsClock()
        {
            // No action carries a time: the stamp is the model's own clock at the tick the action executes,
            // plus the duration off the model's timings.
            MatchEngine engine = Simple(MatchTimings.Default);

            engine.AdvanceTime(MetaDuration.FromSeconds(100));
            Assert.That(engine.Now, Is.EqualTo(MetaTime.Epoch + MetaDuration.FromSeconds(100)));

            engine.EndTurn(0);

            Assert.That(engine.Pacing.DeadlineAt, Is.EqualTo(engine.Now + MatchTimings.Default.TurnDeadline));
        }

        [Test]
        public void ALapseHappensAtItsStamp()
        {
            // The host dispatches a lapse once the model's clock has reached the stamp, so the next deadline
            // is armed from the lapsed one rather than from whenever the test happened to call.
            MatchEngine engine = MatchEngine.Create(
                new MatchSetup(4712, Config, MatchTimings.Default, TestDecks.Standard(Config), TestDecks.Alternate(Config)));
            MetaTime mulliganDeadline = engine.Pacing.DeadlineAt!.Value;

            Assert.That(engine.ExpireDeadline(MatchDeadlineKind.Mulligan, null), Is.EqualTo(MatchDeadlineOutcome.Applied));

            Assert.That(engine.Now, Is.EqualTo(mulliganDeadline));
            Assert.That(engine.Pacing.DeadlineAt, Is.EqualTo(mulliganDeadline + MatchTimings.Default.TurnDeadline));
        }

        [Test]
        public void ZeroTurnDeadlineArmsNoDeadline()
        {
            MatchEngine engine = Simple();

            Assert.That(engine.Pending.DeadlineAt, Is.Null, "zero means no deadline in force, not one already lapsed");
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingIntent));
        }

        [Test]
        public void AWakePreservesDeadlineStamps()
        {
            MatchEngine engine = Simple(MatchTimings.Default);
            MetaTime?   armed  = engine.Pending.DeadlineAt;
            Assert.That(armed, Is.Not.Null);

            MatchEngine restored = MatchEngine.Wrap(engine.State);
            Assert.That(restored.Pending.DeadlineAt, Is.EqualTo(armed));
            Assert.That(restored.Pending.DeadlineKind, Is.EqualTo(MatchDeadlineKind.Turn));
        }

        [Test]
        public void LapsedTurnDeadlinePlaysTheRestOfTheTurnAndEndsIt()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Hand("BusyBeaver");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build();

            MatchDeadlineOutcome outcome = engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest);

            Assert.That(outcome, Is.EqualTo(MatchDeadlineOutcome.Applied));
            Assert.That(engine.Rules.SeatOnTurn, Is.EqualTo(1), "the whole turn was played out and then ended");
            Assert.That(engine.Rules.Seat(0).Board.Count, Is.EqualTo(2), "not a single auto-played card: the rest of the turn");
        }

        [Test]
        public void ReserveExtensionRefusedBeforeTheSeatHasActed()
        {
            MatchEngine engine = Simple(MatchTimings.Default);

            Assert.That(engine.Rules.ActedThisTurn, Is.False);
            MatchDeadlineOutcome outcome = engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest);

            Assert.That(outcome, Is.EqualTo(MatchDeadlineOutcome.Applied), "a seat that has sat still takes its lapse immediately");
            Assert.That(engine.Rules.TurnReserveSpent[0], Is.EqualTo(MetaDuration.Zero));
        }

        [Test]
        public void ReserveIsDrawnOnlyByASeatThatHasActedThisTurn()
        {
            MatchEngine engine = Simple(MatchTimings.Default);

            engine.Play(0, engine.SecretHand(0)[0]);
            MetaTime? before = engine.Pending.DeadlineAt;

            MatchDeadlineOutcome outcome = engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest);

            Assert.That(outcome, Is.EqualTo(MatchDeadlineOutcome.ExtendedFromReserve));
            Assert.That(engine.Rules.SeatOnTurn, Is.EqualTo(0), "the turn is not over");
            Assert.That(engine.Rules.TurnReserveSpent[0], Is.EqualTo(MatchTimings.Default.TurnReserveExtension));
            Assert.That(engine.Pending.DeadlineAt, Is.EqualTo(before + MatchTimings.Default.TurnReserveExtension));
        }


        [Test]
        public void ExhaustedReserveLetsTheDeadlineExpire()
        {
            MatchEngine engine = Simple(MatchTimings.Default);
            engine.Play(0, engine.SecretHand(0)[0]);

            int extensions = 0;
            while (engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest) == MatchDeadlineOutcome.ExtendedFromReserve)
            {
                extensions++;
                Assert.That(extensions, Is.LessThan(100), "the bank is not being spent");
            }

            Assert.That(extensions, Is.EqualTo(4), "sixty seconds of bank in fifteen-second extensions");
            Assert.That(engine.ReserveRemaining(0), Is.EqualTo(MetaDuration.Zero));
            Assert.That(engine.Rules.SeatOnTurn, Is.EqualTo(1), "the turn ended once the bank ran out");
        }

        [Test]
        public void SpentReserveChangesTheRulesHash()
        {
            MatchEngine engine = Simple(MatchTimings.Default);
            engine.Play(0, engine.SecretHand(0)[0]);

            uint before = engine.ComputeRulesHash();
            engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest);

            Assert.That(engine.ComputeRulesHash(), Is.Not.EqualTo(before),
                "how much of the bank a seat has drawn is a fact about the game, not a stamp outside it");
        }



        [Test]
        public void AStaleTurnDeadlineIsIgnoredWhileAResolutionIsHeld()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector"); // Hello: look at 3, keep 1 — holds the resolution
            scenario.Seat(0).Mana(9, 9).DeckOf(8, "GreyOwl");
            scenario.Seat(1).DeckOf(8);
            MatchEngine engine = scenario.OnTurn(0).Build(MatchTimings.Default);

            engine.PassTurn();
            engine.PassTurn();
            engine.Play(0, engine.SecretHand(0)[0]);

            Assert.That(engine.Pending.DeadlineKind, Is.EqualTo(MatchDeadlineKind.EffectChoice));

            // The turn timer the host armed before the peek is still in flight. It must not end the turn out
            // from under the resolution that replaced it.
            MatchDeadlineOutcome outcome = engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest);

            Assert.That(outcome, Is.EqualTo(MatchDeadlineOutcome.Ignored));
            Assert.That(engine.Rules.PendingChoice, Is.Not.Null, "the choice is still waiting for its own deadline");
            Assert.That(engine.Rules.SeatOnTurn, Is.EqualTo(0));
        }



        [Test]
        public void IntentsAfterTheMatchEndAreWrongPhase()
        {
            // A finished table reopens for nobody, and says so rather than looking merely busy.
            Scenario       scenario = new Scenario(Config);
            CardInstanceId frog     = scenario.Seat(0).Board("PondFrog");
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).Den(15).DeckOf(6);
            MatchEngine engine = scenario.OnTurn(0).Build(MatchTimings.Default);

            engine.Attack(0, frog, EffectTargetRef.Den(1));

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.Complete));
            Assert.That(engine.EndTurn(0), Is.EqualTo(MatchIntentResults.WrongPhase));
            Assert.That(engine.Play(0, engine.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.WrongPhase));
        }

        [Test]
        public void PlayOutRemainderReachesATerminalResult()
        {
            MatchSetup  setup  = new MatchSetup(4711, Config, MatchTimings.Instant, TestDecks.Standard(Config), TestDecks.Alternate(Config));
            MatchEngine engine = MatchEngine.Create(setup);

            engine.PlayOutRemainder(TestBots.Strongest);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete));
            Assert.That(engine.Result, Is.Not.Null);
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.Complete));
        }

        [Test]
        public void PlayOutRemainderWorksFromAnyPointIncludingTheMulligan()
        {
            for (ulong seed = 100; seed < 110; seed++)
            {
                MatchSetup  setup  = new MatchSetup(seed, Config, MatchTimings.Default, TestDecks.Standard(Config), TestDecks.Alternate(Config));
                MatchEngine engine = MatchEngine.Create(setup);

                engine.PlayOutRemainder(TestBots.Strongest);
                Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete), $"seed {seed}");
            }
        }


        [Test]
        public void PlayOutRemainderStartsFromAHeldPeek()
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seat(0).Hand("PebbleCollector");
            scenario.Seat(0).Mana(9, 9).DeckOf(8, "GreyOwl");
            scenario.Seat(1).DeckOf(8);
            MatchEngine engine = scenario.OnTurn(0).Build(MatchTimings.Default);

            engine.PassTurn();
            engine.PassTurn();
            engine.Play(0, engine.SecretHand(0)[0]);
            Assert.That(engine.Pending.Kind, Is.EqualTo(MatchPendingKind.AwaitingEffectChoice));

            engine.PlayOutRemainder(TestBots.Strongest);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete), "the pause must never block a play-out");
        }

        // ---------------------------------------------------------------- the outage push

        [Test]
        public void PushingTheClocksForwardMovesEveryStampByTheSameAmount()
        {
            // What a host does when the table was not there for a while. A deadline is a promise about how
            // long a seat gets to decide, and time in which the table could not be reached is not time a seat
            // spent — so an outage moves the stamps rather than consuming them.
            MatchEngine engine = Simple(MatchTimings.Default);

            MetaTime? deadlineBefore = engine.Pacing.DeadlineAt;
            Assert.That(deadlineBefore, Is.Not.Null, "the fixture walks two turn boundaries so a deadline is armed");

            MetaDuration outage = MetaDuration.FromSeconds(90);
            engine.PushClocksForward(outage);

            Assert.That(engine.Pacing.DeadlineAt, Is.EqualTo(deadlineBefore!.Value + outage));
        }

        [Test]
        public void PushingTheClocksForwardChangesNothingAboutTheGame()
        {
            // It moves stamps and only stamps: no step is published, no rule runs, and the game is exactly
            // where it was. The rules hash is what says so.
            MatchEngine engine = Simple(MatchTimings.Default);
            uint        before = engine.ComputeRulesHash();

            engine.PushClocksForward(MetaDuration.FromMinutes(5));

            Assert.That(engine.ComputeRulesHash(), Is.EqualTo(before));
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));
        }

        [Test]
        public void PushingByNothingIsANoOp()
        {
            MatchEngine engine = Simple(MatchTimings.Default);
            MetaTime?   before = engine.Pacing.DeadlineAt;

            engine.PushClocksForward(MetaDuration.Zero);
            engine.PushClocksForward(MetaDuration.FromSeconds(-30));

            Assert.That(engine.Pacing.DeadlineAt, Is.EqualTo(before));
        }

        [Test]
        public void AnOutagePushIsWhatKeepsALapsedDeadlineFromFiringOnTheWakeFrame()
        {
            // The failure the push exists to prevent, stated as a property: after a restore whose outage
            // exceeded the remaining deadline, the deadline is in the future again rather than in the past —
            // so a host that folds it into its wake schedule arms a real timer instead of one the SDK clamps
            // to "now" and dispatches immediately.
            MatchEngine engine = Simple(MatchTimings.Default);
            MetaTime    wokeAt = engine.Pacing.DeadlineAt!.Value + MetaDuration.FromSeconds(30);

            MatchModel  state   = engine.State;
            MatchEngine resumed = MatchEngine.Wrap(state);

            Assert.That(resumed.Pacing.DeadlineAt!.Value, Is.LessThan(wokeAt), "the stamp lapsed while the table was away");

            resumed.PushClocksForward(MetaDuration.FromSeconds(90));

            Assert.That(resumed.Pacing.DeadlineAt!.Value, Is.GreaterThan(wokeAt), "and the returning seat gets its time back");
        }
    }
}
