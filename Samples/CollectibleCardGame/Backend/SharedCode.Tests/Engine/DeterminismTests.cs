using Metaplay.Core;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Determinism as a suite, not an assertion. Every discipline the engine keeps — one stream, one
    /// consumption order, no clock, no unordered iteration, no floating point — is invisible in a single run
    /// and only shows up over thousands of seeds.
    /// <para>
    /// The standard run is sized for CI. Set <c>STICKYPAWS_DETERMINISM_DEEP=1</c> for the long one.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DeterminismTests
    {
        SharedGameConfig Config => TestGameConfig.Shared;

        const int StandardSeeds = 2000;
        const int DeepSeeds     = 25000;
        /// <summary> How many of the seeds also get the full per-step invariant walk, which is the slow part. </summary>
        const int StandardInvariantSeeds = 200;
        const int DeepInvariantSeeds     = 2000;
        const int StandardPacingSeeds    = 40;
        const int DeepPacingSeeds        = 400;

        /// <summary>
        /// Every suite here scales with the deep switch, not only the replay one. The deep run is what a
        /// change to an iteration order or to the deal is supposed to be checked against, and a suite that
        /// stayed at its CI size would be sitting that check out.
        /// </summary>
        static bool IsDeepRun => Environment.GetEnvironmentVariable("STICKYPAWS_DETERMINISM_DEEP") == "1";

        static int SeedCount      => IsDeepRun ? DeepSeeds : StandardSeeds;
        static int InvariantSeeds => IsDeepRun ? DeepInvariantSeeds : StandardInvariantSeeds;
        static int PacingSeeds    => IsDeepRun ? DeepPacingSeeds : StandardPacingSeeds;

        MatchSetup Setup(ulong seed, MatchTimings timings)
            => new MatchSetup(seed, Config, timings, TestDecks.Standard(Config), TestDecks.Alternate(Config));

        // ------------------------------------------------------------------ 16.1

        [Test]
        public void SameSeedAndScriptProduceIdenticalFinalState()
        {
            int       seeds     = SeedCount;
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                ScriptRecorder recorder = new ScriptRecorder(seed);
                MatchEngine    original = MatchEngine.Create(Setup(seed, MatchTimings.Instant));
                Drive(original, recorder);

                // Replaying the recorded intents rather than re-running the policy is deliberate: it proves
                // the engine is deterministic rather than proving the policy is.
                MatchEngine        replay       = MatchEngine.Create(Setup(seed, MatchTimings.Instant));
                Replay(replay, recorder.Script);

                Assert.That(replay.ComputeRulesHash(), Is.EqualTo(original.ComputeRulesHash()), $"seed {seed}");
                Assert.That(replay.Result.Outcome, Is.EqualTo(original.Result.Outcome), $"seed {seed}");
                Assert.That(EventBytes(replay), Is.EqualTo(EventBytes(original)), $"seed {seed}: event streams differ");
            }

            stopwatch.Stop();
            TestContext.Out.WriteLine($"{seeds} seeds replayed in {stopwatch.ElapsedMilliseconds} ms");
        }

        // ------------------------------------------------------------------ 16.2

        [Test]
        public void TimingsDoNotAffectTheGame()
        {
            int seeds = PacingSeeds;
            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                ScriptRecorder recorder = new ScriptRecorder(seed);
                MatchEngine    instant  = MatchEngine.Create(Setup(seed, MatchTimings.Instant));
                Drive(instant, recorder);

                MatchEngine paced = MatchEngine.Create(Setup(seed, PacedTimings));
                Replay(paced, recorder.Script);

                Assert.That(paced.ComputeRulesHash(), Is.EqualTo(instant.ComputeRulesHash()), $"seed {seed}: pacing changed the game");
            }

            // …and the stamps really did differ, or the assertion above would be proving nothing.
            MatchEngine paced0 = MatchEngine.Create(Setup(1, PacedTimings));
            Assert.That(paced0.Pending.DeadlineAt, Is.Not.Null);
            Assert.That(MatchEngine.Create(Setup(1, MatchTimings.Instant)).Pending.DeadlineAt, Is.Null);
        }

        /// <summary>
        /// Real deadlines. The reserve bank is left at zero because how much of it a seat has spent is game
        /// state and is inside the hash, so a run that started with a different bank would be a different
        /// game rather than the same one paced differently.
        /// </summary>
        static readonly MatchTimings PacedTimings = new MatchTimings(
            mulliganDeadline:     MetaDuration.FromSeconds(30),
            turnDeadline:         MetaDuration.FromSeconds(60),
            turnReserveBank:      MetaDuration.Zero,
            turnReserveExtension: MetaDuration.Zero,
            effectChoiceDeadline: MetaDuration.FromSeconds(20));

        // ------------------------------------------------------------------ 16.3

        [Test]
        public void GoldenGame_Seed12345()
        {
            // A pinned seed, a pinned script and a pinned hash: the tripwire for a rules change nobody meant
            // to make. Updating these three numbers is a deliberate, reviewable diff.
            const ulong Seed = 12345;

            ScriptRecorder recorder = new ScriptRecorder(Seed);
            MatchEngine    engine   = MatchEngine.Create(Setup(Seed, MatchTimings.Instant));

            // The deal's own event is not one of the counted 538 and never was: the driver reads the model's
            // history, which the deal writes into, where the old harness handed the deal a sink that threw it
            // away. Clearing here keeps the number counting exactly the events it has always counted, so it
            // stays a tripwire rather than becoming a new number for a new reason.
            engine.Clear();
            Drive(engine, recorder);

            TestContext.Out.WriteLine($"golden: hash={engine.ComputeRulesHash()} actions={recorder.Script.Count} events={engine.Events.Count} outcome={engine.Result}");

            Assert.That(engine.Result.Outcome, Is.EqualTo(MatchOutcome.Seat1Wins));
            Assert.That(engine.Result.Cause, Is.EqualTo(MatchEndCause.DenAtZero));
            Assert.That(engine.Result.FinalTurn, Is.EqualTo(GoldenFinalTurn));
            Assert.That(recorder.Script.Count, Is.EqualTo(GoldenActionCount));
            Assert.That(engine.ActionCount, Is.EqualTo(GoldenActionCount), "every accepted intent is one counted action");
            Assert.That(engine.Events.Count, Is.EqualTo(GoldenEventCount));
            Assert.That(engine.ComputeRulesHash(), Is.EqualTo(GoldenHash));
        }

        [Test]
        public void TheGoldenGameIsPublicEqualOnAFollower()
        {
            // The replacement for the golden game under a failing publisher, and it exercises the same thing
            // that one did — the whole game through the other path — for a path that now exists. Every action
            // of the pinned game runs against a network-masked clone and the two are compared after each one,
            // so a divergence names the action it came from rather than a hash at the end.
            const ulong Seed = 12345;

            ScriptRecorder recorder = new ScriptRecorder(Seed);
            MatchEngine    engine   = MatchEngine.Create(Setup(Seed, MatchTimings.Instant));

            engine.Mirror = FollowerMirror.Of(engine.Model, Config);
            engine.Clear();

            Assert.DoesNotThrow(() => Drive(engine, recorder), "the golden game must be public-equal on a follower, action by action");

            Assert.That(engine.Result.Outcome, Is.EqualTo(MatchOutcome.Seat1Wins), "and it must still be the same game");
            Assert.That(engine.ComputeRulesHash(), Is.EqualTo(GoldenHash));
        }

        // \note Regenerate these deliberately, never to make a red test green: the point of the golden game is
        //       that a rules change nobody meant to make shows up as a diff right here.
        const int  GoldenFinalTurn   = 47;
        const int  GoldenActionCount = 169;
        const int  GoldenEventCount  = 766;
        // The hash covers the public rules state, so a change to its shape moves the hash alone. If the outcome,
        // cause, final turn, action count or event count move too, the game itself changed: regenerate only
        // when that is intended.
        const uint GoldenHash        = 3902393807u;

        [Test]
        public void EveryStepOfEveryGameHoldsTheInvariants()
        {
            int seeds = InvariantSeeds;
            for (ulong seed = 1; seed <= (ulong)seeds; seed++)
            {
                MatchEngine    engine   = MatchEngine.Create(Setup(seed, MatchTimings.Instant));
                ScriptRecorder recorder = new ScriptRecorder(seed);

                int  previous = engine.ActionCount;
                uint lastHash = engine.ComputeRulesHash();

                // The catalog itself is self-play's, walked here over the engine's own random-legal games.
                // Two implementations of "what must always be true of a match" would disagree eventually, and
                // the one that disagreed silently would be the one nobody was reading.
                InvariantWalk walk = new InvariantWalk(Config, SelfPlayChecks.All, dealtFromFullDecks: true);
                walk.Capture(engine);

                DriveWithCheck(engine, recorder, (accepted, stepEvents) =>
                {
                    Assert.That(engine.ActionCount, Is.GreaterThanOrEqualTo(previous), $"seed {seed}: the action count moved backwards");
                    previous = engine.ActionCount;

                    Assert.That(walk.Check(engine, stepEvents), Is.Null, $"seed {seed}");
                    walk.Capture(engine);

                    if (accepted)
                    {
                        uint hash = engine.ComputeRulesHash();
                        Assert.That(hash, Is.Not.EqualTo(lastHash), $"seed {seed}: an accepted action changed nothing");
                        lastHash = hash;
                    }
                });

                Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete), $"seed {seed}: the game did not terminate");
            }
        }

        // ------------------------------------------------------------------ driving

        void Drive(MatchEngine engine, ScriptRecorder recorder)
            => DriveWithCheck(engine, recorder, null);

        /// <summary> What one call recorded, without disturbing the whole game's record. </summary>
        static IReadOnlyList<MatchEvent> Since(MatchEngine engine, int from)
            => engine.Events.GetRange(from, engine.Events.Count - from);

        /// <summary>
        /// Play a whole game with a seeded random-legal policy, recording every intent the engine accepted.
        /// The host here never lets a deadline lapse, so the script is a pure list of player actions.
        /// </summary>
        void DriveWithCheck(MatchEngine engine, ScriptRecorder recorder, Action<bool, IReadOnlyList<MatchEvent>> afterStep)
        {
            const int MaxSteps = 20000;

            for (int step = 0; step < MaxSteps; step++)
            {
                if (engine.Phase == MatchPhase.Complete)
                    return;

                // The invariant walk reconstructs a step's deltas from that step's own events, so each call
                // into the engine has to be handed the slice it produced rather than the whole game's.
                int published = engine.Events.Count;

                switch (engine.Pending.Kind)
                {

                    case MatchPendingKind.AwaitingEffectChoice:
                    {
                        PendingEffectChoice pending = engine.Rules.PendingChoice;
                        List<int> keep = recorder.ChooseKept(pending);
                        MatchIntent intent = new EffectChoiceIntent(engine.ChoiceId, keep);
                        recorder.Record(pending.Seat, intent);
                        engine.Submit(pending.Seat, intent);
                        afterStep?.Invoke(true, Since(engine, published));
                        continue;
                    }

                    case MatchPendingKind.AwaitingMulligan:
                    {
                        for (int seat = 0; seat < MatchSeats.Count; seat++)
                        {
                            if (engine.Rules.Seat(seat).HasMulliganed)
                                continue;

                            MatchIntent intent = recorder.ChooseMulligan(engine, seat);
                            recorder.Record(seat, intent);
                            engine.Submit(seat, intent);
                        }
                        afterStep?.Invoke(true, Since(engine, published));
                        continue;
                    }

                    default:
                    {
                        int         seat   = engine.Rules.SeatOnTurn;
                        MatchIntent intent = recorder.ChooseAction(engine.BuildSeatView(seat), seat);
                        recorder.Record(seat, intent);

                        MatchIntentResult result = engine.Submit(seat, intent);
                        Assert.That(MatchIntentResults.All, Does.Contain(result), "an unenumerated refusal reason");
                        Assert.That(result, Is.EqualTo(MatchIntentResults.Success), "the policy chose an illegal action");
                        afterStep?.Invoke(true, Since(engine, published));
                        continue;
                    }
                }
            }

            Assert.Fail("a game did not terminate within the step bound");
        }

        /// <summary>
        /// Replay a recorded script against an independently constructed engine. Every intent must be accepted
        /// and the script must be consumed exactly — a replay that quietly had one refused would otherwise
        /// still "match" by running a shorter game and hashing the same way for the wrong reason.
        /// </summary>
        void Replay(MatchEngine engine, List<(int Seat, MatchIntent Intent)> script)
        {
            int next = 0;
            const int MaxSteps = 20000;

            for (int step = 0; step < MaxSteps; step++)
            {
                if (engine.Phase == MatchPhase.Complete)
                    break;


                Assert.That(next, Is.LessThan(script.Count), "the replay ran out of script before the game ended");
                (int seat, MatchIntent intent) = script[next++];
                MatchIntentResult result = engine.Submit(seat, intent);
                Assert.That(result, Is.EqualTo(MatchIntentResults.Success), $"replayed intent {next - 1} ({intent.GetType().Name}) was refused: {result}");
            }

            Assert.That(engine.Phase, Is.EqualTo(MatchPhase.Complete), "a replayed game did not terminate");
            Assert.That(next, Is.EqualTo(script.Count), "the replay finished without consuming the whole script");
        }

        static byte[] EventBytes(MatchEngine engine)
        {
            List<MatchEvent> events = engine.Events;
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            {
                foreach (MatchEvent ev in events)
                {
                    byte[] bytes = Metaplay.Core.Serialization.MetaSerialization.SerializeTagged(ev, Metaplay.Core.Serialization.MetaSerializationFlags.IncludeAll, logicVersion: null);
                    stream.Write(bytes, 0, bytes.Length);
                }
                return stream.ToArray();
            }
        }
    }

    /// <summary>
    /// A seeded policy that picks uniformly from the legal set, and records what it picked. Its own generator
    /// is separate from the engine's: nothing outside the engine ever draws from the engine's stream.
    /// </summary>
    public sealed class ScriptRecorder : IActionSource
    {
        readonly RandomPCG _rng;

        public List<(int Seat, MatchIntent Intent)> Script { get; } = new List<(int, MatchIntent)>();

        public ScriptRecorder(ulong policySeed)
        {
            _rng = RandomPCG.CreateFromSeed(policySeed ^ 0x5DEECE66Dul);
        }

        public void Record(int seat, MatchIntent intent) => Script.Add((seat, intent));

        public MatchIntent ChooseAction(SeatView view, int seat)
        {
            IReadOnlyList<MatchIntent> legal = view.LegalActions;
            if (legal.Count == 0)
                return new EndTurnIntent();

            // Ending the turn is always the last entry; weight against it so games actually get played.
            int ndx = _rng.NextInt(legal.Count + (legal.Count > 1 ? legal.Count - 1 : 0));
            if (ndx >= legal.Count)
                ndx = ndx - legal.Count;

            return legal[ndx];
        }

        public MatchIntent ChooseMulligan(MatchEngine engine, int seat)
        {
            List<CardInstanceId> replace = new List<CardInstanceId>();
            foreach (CardInstanceId id in engine.SecretHand(seat))
            {
                if (engine.Rules.Instance(id).FromStartingDeck && _rng.NextBool())
                    replace.Add(id);
            }

            return new MulliganIntent(replace);
        }

        public List<int> ChooseKept(PendingEffectChoice pending)
        {
            List<int> keep = new List<int>();
            for (int ndx = 0; ndx < pending.RevealedCount && keep.Count < pending.KeepCount; ndx++)
            {
                if (_rng.NextBool())
                    keep.Add(ndx);
            }
            return keep;
        }
    }
}
