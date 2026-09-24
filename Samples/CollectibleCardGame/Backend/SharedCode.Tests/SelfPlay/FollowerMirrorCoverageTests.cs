using Metaplay.Core;
using Metaplay.Core.Model;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// <b>Every action reaches a follower, and this is what says so.</b>
    /// <para>
    /// The bulk sweep mirrors the actions a game produces when the seats play it, which is most of them and
    /// reads like all of them. It is not all of them: the actions that only exist under a clock — a released
    /// beat, a lapsed mulligan deadline, a reserve extension — never occur at zero timings, and the table's
    /// own actions are the actor's rather than a seat's, so no seat-driven harness issues one. The first
    /// version of the refactor mirrored five of the block and the proof statement said "every action".
    /// </para>
    /// <para>
    /// So the coverage is asserted rather than described. <see cref="FollowerMirror"/> records every action
    /// type any mirror in the run has executed against a follower, and
    /// <see cref="EveryMatchActionInTheBlockHasBeenRunAgainstAFollower"/> requires the whole 5100–5149 block
    /// to appear in it. A new action cannot join the registry without a mirrored path that runs it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FollowerMirrorCoverageTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        // ---------------------------------------------------------------- the deadline-driven rule actions

        /// <summary>
        /// A mulligan deadline that lapses with nobody having submitted, which is the only way
        /// <see cref="MatchMulliganResolve"/> exists as an action of its own: when both seats submit, the
        /// resolution happens inside the second <see cref="MatchMulliganSubmit"/>.
        /// </summary>
        [Test]
        public void ALapsedMulliganDeadlineIsMirrored()
        {
            MatchEngine engine = DealtAtDefaultTimings(seed: 40_001ul);

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Mulligan));
            Assert.That(engine.ExpireDeadline(MatchDeadlineKind.Mulligan, null), Is.EqualTo(MatchDeadlineOutcome.Applied),
                "the mulligan deadline must actually lapse, or this covers nothing");
            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Playing));

            Assert.That(FollowerMirror.HasApplied(typeof(MatchMulliganResolve)), Is.True);
        }

        /// <summary>
        /// A turn deadline that lapses for a seat which has already acted and still has reserve, which is the
        /// only way <see cref="MatchExtendReserve"/> exists at all: the extension is gated on both.
        /// </summary>
        [Test]
        public void AReserveExtensionIsMirrored()
        {
            MatchEngine engine = PlayingAtDefaultTimings(seed: 40_002ul);

            // Acting first is the rule, not the fixture's convenience: only a seat that has already spent
            // part of its turn may draw on the bank (Docs/rules.md, "Two kinds of number").
            engine.Play(0, engine.SecretHand(0)[0]);

            Assert.That(engine.ExpireDeadline(MatchDeadlineKind.Turn, TestBots.Strongest),
                Is.EqualTo(MatchDeadlineOutcome.ExtendedFromReserve));

            Assert.That(FollowerMirror.HasApplied(typeof(MatchExtendReserve)), Is.True);
        }

        /// <summary>
        /// The clock push a returning seat is owed. It is the action a reviewer's planted secret-into-public
        /// leak once sat inside undetected, because no mirrored path ran it — and it is the one action kept
        /// against a delete list, so it is the one that most needs a mirrored path of its own.
        /// </summary>
        [Test]
        public void TheClockPushIsMirrored()
        {
            // A deal walked into its first turn, because an armed deadline is what the push has to move and a
            // Scenario builds a state rather than walking into one.
            MatchEngine engine = DealtAtDefaultTimings(seed: 40_003ul);
            Assert.That(engine.ExpireDeadline(MatchDeadlineKind.Mulligan, null), Is.EqualTo(MatchDeadlineOutcome.Applied));

            MetaTime? before = engine.Pacing.DeadlineAt;
            Assert.That(before, Is.Not.Null, "the fixture needs an armed deadline for the push to move");

            engine.PushClocksForward(MetaDuration.FromSeconds(90));

            Assert.That(engine.Pacing.DeadlineAt, Is.EqualTo(before.Value + MetaDuration.FromSeconds(90)));
            Assert.That(FollowerMirror.HasApplied(typeof(MatchPushClocks)), Is.True);
        }

        // ---------------------------------------------------------------- the table's own actions

        /// <summary>
        /// The seven actions the actor issues about the table rather than about the game. They are public in
        /// full and every one of them is checksummed, so a follower runs each of them too — and until this
        /// fixture existed, none of them had ever been run against one.
        /// </summary>
        [Test]
        public void EveryTableActionIsMirrored()
        {
            MatchEngine engine = DealtAtDefaultTimings(seed: 40_004ul);

            foreach (MatchHostAction action in TableActions())
                Assert.That(engine.SubmitTableAction(action), Is.EqualTo(MatchIntentResults.Success), $"{action.GetType().Name} was refused");

            Assert.That(engine.Model.Seats[0].DisplayName, Is.EqualTo("Mirrored"));
            Assert.That(engine.Model.Phase, Is.EqualTo(MatchTablePhase.Ended));
            Assert.That(engine.Model.Result, Is.Not.Null);
            Assert.That(engine.Model.ResultAcked[0], Is.True);

            foreach (MatchAction action in TableActions())
                Assert.That(FollowerMirror.HasApplied(action.GetType()), Is.True, $"{action.GetType().Name} never reached a follower");
        }

        [Test]
        public void DeveloperShortcutIsMirrored()
        {
            MatchEngine engine = MatchEngine.Create(new MatchSetup(40009, Config, MatchTimings.Default,
                TestDecks.Standard(Config), TestDecks.Alternate(Config)));
            engine.Model.Stakes = MatchStakes.Practice(new List<int> { 25, 25 },
                new List<List<CardId>> { new List<CardId>(), new List<CardId>() });
            engine = Mirrored(engine);
            Assert.That(engine.SubmitTableAction(new MatchDebugWin(0)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(FollowerMirror.HasApplied(typeof(MatchDebugWin)), Is.True);
        }

        // ---------------------------------------------------------------- the ratchet

        /// <summary>
        /// The closing statement of the proof: <b>every</b> action in the match block has been
        /// executed against a follower and compared, not merely most of them.
        /// <para>
        /// It drives its own coverage first rather than relying on the rest of the assembly having run, so it
        /// says the same thing under a filter as it does in a full run. What it adds over the fixtures above
        /// is that it is written against the <em>registry</em>: a new action is discovered by reflection and
        /// fails here until somebody gives it a mirrored path.
        /// </para>
        /// </summary>
        [Test]
        public void EveryMatchActionInTheBlockHasBeenRunAgainstAFollower()
        {
            ALapsedMulliganDeadlineIsMirrored();
            AReserveExtensionIsMirrored();
            TheClockPushIsMirrored();
            EveryTableActionIsMirrored();
            DeveloperShortcutIsMirrored();
            EveryActionASeatCanTakeIsMirrored();

            List<string> missing = new List<string>();
            foreach (Type type in MatchActionTypesInTheBlock())
            {
                if (!FollowerMirror.HasApplied(type))
                    missing.Add(type.Name);
            }

            missing.Sort();
            Assert.That(missing, Is.Empty,
                "these actions have never been executed against a follower, so nothing checks that they are public-equal: "
                + string.Join(", ", missing));
        }

        /// <summary>
        /// The five actions a seat produces by playing, each in a state built for it rather than in a game
        /// that happens to reach it. The bulk sweep runs these thousands of times over real games; what a
        /// played game cannot promise is that <em>this</em> run reached an attack or a peek, and a coverage
        /// ratchet that depends on a seed's luck is a ratchet that fails on somebody else's Tuesday.
        /// </summary>
        [Test]
        public void EveryActionASeatCanTakeIsMirrored()
        {
            // A mulligan submitted by one seat, which is also the deal's first submittable action.
            MatchEngine dealt = DealtAtDefaultTimings(seed: 40_005ul);
            Assert.That(dealt.Mulligan(0, dealt.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.Success));

            // A card played, a critter attacking, a turn ended. The attacker is on the board from the start
            // and awake, because a critter played this turn is asleep — which is a rule, not a fixture detail.
            MatchEngine playing = PlayingAtDefaultTimings(seed: 40_006ul);
            CardInstanceId attacker = playing.Rules.Seat(0).Board[0].Id;
            Assert.That(playing.Play(0, playing.SecretHand(0)[0]), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(playing.Attack(0, attacker, EffectTargetRef.Den(1)), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(playing.EndTurn(0), Is.EqualTo(MatchIntentResults.Success));

            // A held peek answered by its owner. The one interactive resolution, and the only card that
            // pauses one (Docs/effects.md).
            MatchEngine peeking = PeekingAtDefaultTimings(seed: 40_007ul);
            Assert.That(peeking.Play(0, peeking.SecretHand(0)[0], EffectTargetRef.None), Is.EqualTo(MatchIntentResults.Success));
            Assert.That(peeking.Rules.PendingChoice, Is.Not.Null, "the fixture has to reach the pause");
            Assert.That(peeking.ChooseKept(0, peeking.Revealed(0)[0]), Is.EqualTo(MatchIntentResults.Success));

            foreach (Type type in new[]
            {
                typeof(MatchMulliganSubmit), typeof(MatchPlayCard), typeof(MatchAttack),
                typeof(MatchEndTurn), typeof(MatchEffectChoice),
            })
                Assert.That(FollowerMirror.HasApplied(type), Is.True, $"{type.Name} never reached a follower");
        }

        // ---------------------------------------------------------------- fixtures

        static IEnumerable<Type> MatchActionTypesInTheBlock()
        {
            foreach (Type type in typeof(MatchAction).Assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(MatchAction).IsAssignableFrom(type))
                    continue;

                int code = ModelActionRepository.Instance.SpecFromType[type].TypeCode;
                if (code >= 5100 && code <= 5149)
                    yield return type;
            }
        }

        static IEnumerable<MatchHostAction> TableActions()
        {
            yield return new MatchSetSeats(new List<MatchSeat>
            {
                new MatchSeat(EntityId.None, "Mirrored", SeatOccupancy.Bot, BotProfileId.Strongest),
                new MatchSeat(EntityId.None, "Mirrored", SeatOccupancy.Bot, BotProfileId.Strongest),
            });
            yield return new MatchSetGrace(0, MetaDuration.FromSeconds(30));
            yield return new MatchArmHeistDeadline(0, MetaDuration.FromSeconds(45));
            yield return new MatchPushClocks(MetaDuration.FromSeconds(15));
            yield return new MatchSetResult(
                new MatchOutcomeRecord(MatchOutcome.Seat0Wins, winnerSeat: 0, finalTurn: 4, MatchEndCause.DenAtZero,
                    new List<bool> { true, true }, wasRanked: false, decidedAt: MetaTime.Epoch),
                new List<List<HeistEligibleCard>> { new List<HeistEligibleCard>(), new List<HeistEligibleCard>() });
            yield return new MatchSetPhase(MatchTablePhase.Ended);
            yield return new MatchSetResultAck(0);
        }

        static MatchEngine Mirrored(MatchEngine engine)
        {
            engine.Mirror = FollowerMirror.Of(engine.Model, Config);
            engine.Clear();
            return engine;
        }

        static MatchEngine DealtAtDefaultTimings(ulong seed)
            => Mirrored(MatchEngine.Create(
                new MatchSetup(seed, Config, MatchTimings.Default, TestDecks.Standard(Config), TestDecks.Alternate(Config))));

        /// <summary> Seat 0 holding the one card that pauses a resolution, over a deck whose top three are named. </summary>
        static MatchEngine PeekingAtDefaultTimings(ulong seed)
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seed(seed);
            scenario.Seat(0).Hand("PebbleCollector");   // Hello: look at 3, keep 1
            scenario.Seat(0).Deck("MeadowMouse");
            scenario.Seat(0).Deck("MooseWanderer");
            scenario.Seat(0).Deck("BusyBeaver");
            scenario.Seat(0).DeckOf(4, "GreyOwl");
            scenario.Seat(0).Mana(9, 9);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return Mirrored(scenario.OnTurn(0).Build(MatchTimings.Default));
        }

        /// <summary> A seat on turn with mana and a cheap card, at shipped timings. </summary>
        static MatchEngine PlayingAtDefaultTimings(ulong seed)
        {
            Scenario scenario = new Scenario(Config);
            scenario.Seed(seed);
            scenario.Seat(0).Hand("MeadowMouse");
            scenario.Seat(0).Hand("GardenSnail");
            scenario.Seat(0).Board("TrailRabbit");
            scenario.Seat(0).Mana(9, 9).DeckOf(6);
            scenario.Seat(1).Mana(9, 9).DeckOf(6);
            return Mirrored(scenario.OnTurn(0).Build(MatchTimings.Default));
        }
    }
}
