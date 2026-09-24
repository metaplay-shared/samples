using Metaplay.Core;
using Metaplay.Core.Serialization;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Game.Logic.Tests
{
    /// <summary> How one seat is played. Built per game so a profile's mistakes are keyed on that game's seed. </summary>
    public delegate IActionSource SelfPlaySeat(SharedGameConfig config, ulong gameSeed);

    /// <summary> The seat sources the suites choose between. </summary>
    public static class SelfPlaySeats
    {
        public static SelfPlaySeat Profile(BotProfile profile)
            => (config, seed) => new BotPolicy(config, profile, seed);

        /// <summary>
        /// Uniform over the legal set. The fuzzer: a legality hole or an effect interaction a sensible
        /// heuristic politely never walks into, this walks into by lunchtime
        /// (<c>Docs/bots.md</c>, "Personalities").
        /// </summary>
        public static SelfPlaySeat RandomLegal()
            => (config, seed) => new RandomLegalActionSource(config, seed ^ 0x1234ABCD5678EF01ul);

        public static readonly SelfPlaySeat Strongest = Profile(BotProfile.Strongest);

        /// <summary>
        /// The pairings every bulk sweep rotates through. A suite that only ever played the strongest profile
        /// against itself would exercise one narrow corridor of the rules; the random-legal seats are what
        /// reach the interactions a sensible heuristic politely never walks into.
        /// </summary>
        public static readonly SelfPlayPairing[] Mix =
        {
            new SelfPlayPairing("strongest vs strongest", Strongest, Strongest),
            new SelfPlayPairing("strongest vs casual", Strongest, Profile(BotProfile.Casual)),
            new SelfPlayPairing("sloppy vs practiced", Profile(BotProfile.Sloppy), Profile(BotProfile.Practiced)),
            new SelfPlayPairing("random-legal vs random-legal", RandomLegal(), RandomLegal()),
            new SelfPlayPairing("strongest vs random-legal", Strongest, RandomLegal()),
        };
    }

    /// <summary> Two seats, named. </summary>
    public sealed class SelfPlayPairing
    {
        public readonly string       Name;
        public readonly SelfPlaySeat Seat0;
        public readonly SelfPlaySeat Seat1;

        public SelfPlayPairing(string name, SelfPlaySeat seat0, SelfPlaySeat seat1)
        {
            Name  = name;
            Seat0 = seat0;
            Seat1 = seat1;
        }
    }

    /// <summary> Everything one self-play game needs to know about itself. </summary>
    public sealed class SelfPlayGameSpec
    {
        public SharedGameConfig Config;
        public ulong            Seed;
        public int              RunIndex;
        public SelfPlaySeat     Seat0;
        public SelfPlaySeat     Seat1;
        public SelfPlayChecks   Checks = SelfPlayChecks.All;
        /// <summary> What to call this pairing in the batch summary. </summary>
        public string           Pairing;

        /// <summary>
        /// The host durations this game is dealt with. Zero by default, which is what a suite wants: a test
        /// that slept through a beat would be testing the test runner.
        /// <para>
        /// A <b>paced</b> batch — anything non-zero — is not a slower version of the same coverage, it is
        /// different coverage, and the mirror is why. At zero timings no deadline is ever armed, so every
        /// pacing stamp is null and a follower deriving one from (payload, public state) is never put to the
        /// test. The same game paced arms a real stamp at every turn boundary. Any difference between the two
        /// runs of one seed is the timings, because nothing else changed.
        /// </para>
        /// </summary>
        public MatchTimings     Timings = MatchTimings.Instant;

        /// <summary>
        /// Called with the live engine and the seat about to act, at every decision point. What the
        /// indistinguishability sweep hangs off: it needs a real position reached by real play rather than a
        /// hand-built one.
        /// </summary>
        public Action<MatchEngine, int> OnDecisionPoint;

        /// <summary> How often to re-derive a decision and check it came out the same (L2). </summary>
        public int DeterminismStride = 8;

        /// <summary>
        /// When set, every action of the game is also run against a <see cref="FollowerMirror"/> and the two
        /// models are compared. This is the refactor's enforcement mechanism: it turns "every action mutates
        /// public state identically on a follower" from a discipline into a property of twenty thousand
        /// games (<c>Docs/bots.md</c>).
        /// </summary>
        public bool MirrorOnFollower;

        /// <summary>
        /// The seats' decks, when a suite chooses them rather than taking <see cref="SelfPlayDecks"/>'
        /// rotation. Null on every existing suite, which is what keeps the rotation's coverage argument
        /// intact: it deals every collectible and reads every rank track at more than the one rank where it
        /// does nothing, and a starter deck has no rank dimension at all.
        /// </summary>
        public List<MatchDeckCard> Deck0;

        public List<MatchDeckCard> Deck1;
    }

    /// <summary>
    /// An invariant violation, with everything needed to look at it again. The engine is pure and every
    /// substream is derived from the game seed alone, so re-running the harness for that one seed reproduces
    /// the identical game — the dumped script exists so nobody has to reach for a debugger just to see what
    /// happened first.
    /// </summary>
    public sealed class SelfPlayFailure : Exception
    {
        public SelfPlayFailure(string report) : base(report) { }
    }

    /// <summary>
    /// The bulk-play runner: seeded games end to end, the invariant catalog after every step, and a failure
    /// artifact that replays in one command (<c>Docs/bots.md</c>).
    /// </summary>
    public static class SelfPlayHarness
    {
        /// <summary> A game that reaches this many engine calls has stopped being a game. </summary>
        const int MaxSteps = 20000;

        // ---------------------------------------------------------------- a batch

        /// <summary>
        /// Play <paramref name="games"/> games and report the batch. A violation surfaces as an NUnit failure
        /// carrying the seed, the position and the script, so CI output alone is enough to reproduce it.
        /// </summary>
        public static SelfPlayBatch RunBatch(string label, int stream, int games, Func<int, SelfPlayGameSpec> spec)
        {
            SelfPlayBatch batch     = new SelfPlayBatch();
            Stopwatch     stopwatch = Stopwatch.StartNew();

            for (int index = 0; index < games; index++)
            {
                SelfPlayGameSpec game = spec(index);
                game.RunIndex = index;
                game.Seed     = SelfPlayRun.GameSeed(stream, index);

                try
                {
                    batch.Games.Add(RunGame(game));
                }
                catch (SelfPlayFailure failure)
                {
                    TestContext.Out.WriteLine(failure.Message);
                    Assert.Fail($"{label}: {failure.Message.Split('\n')[0]}");
                }
            }

            stopwatch.Stop();
            TestContext.Out.WriteLine($"{batch.Summary(label)}, {stopwatch.ElapsedMilliseconds} ms");
            return batch;
        }

        // ---------------------------------------------------------------- one game

        public static SelfPlayGameRecord RunGame(SelfPlayGameSpec spec)
        {
            SharedGameConfig    config = spec.Config;
            // Rotated by index, so a bulk run deals every collectible and reads the rank tracks at more than
            // the one rank where they do nothing (SelfPlayDecks) — unless the suite named the decks itself,
            // which is what a content measurement over fixed lists needs.
            List<MatchDeckCard> deck0  = spec.Deck0 ?? SelfPlayDecks.For(config, spec.RunIndex, 0);
            List<MatchDeckCard> deck1  = spec.Deck1 ?? SelfPlayDecks.For(config, spec.RunIndex, 1);

            MatchEngine        engine    = MatchEngine.Create(
                new MatchSetup(spec.Seed, config, spec.Timings, deck0, deck1));

            // The clone is made once, here, from the model a subscriber would have been handed — which is the
            // honest simulation, because a real follower subscribes once and replays everything after that.
            // Re-cloning per action would test nothing about accumulated state, which is exactly where a
            // forgotten stored count shows up.
            if (spec.MirrorOnFollower)
                engine.Mirror = FollowerMirror.Of(engine.Model, config);

            IActionSource[] sources = { spec.Seat0(config, spec.Seed), spec.Seat1(config, spec.Seed) };
            InvariantWalk   walk    = new InvariantWalk(config, spec.Checks, dealtFromFullDecks: true);
            List<ScriptLine> script = new List<ScriptLine>();

            script.Add(ScriptLine.Deal(engine.Turn, engine.ActionCount, engine.Rules.FirstSeat));

            // The deal publishes its own step from inside Create, so there is no "before" to have captured.
            // Capturing the dealt state and checking against it makes the conservation half a no-op here and
            // leaves the sanity half doing the work, which is exactly what there is to say about a deal.
            walk.Capture(engine);
            string dealt = walk.Check(engine, engine.Events);
            if (dealt != null)
                throw Report(spec, engine, script, dealt);

            int decisions = 0;

            for (int step = 0; step < MaxSteps && engine.Phase != MatchPhase.Complete; step++)
            {
                walk.Capture(engine);
                engine.Clear();

                try
                {
                    switch (engine.Pending.Kind)
                    {
                        case MatchPendingKind.AwaitingEffectChoice:
                            Act(spec, engine, sources, script, engine.Rules.PendingChoice.Seat, ref decisions);
                            break;

                        case MatchPendingKind.AwaitingMulligan:
                            for (int seat = 0; seat < MatchSeats.Count; seat++)
                            {
                                if (!engine.Rules.Seat(seat).HasMulliganed)
                                    Act(spec, engine, sources, script, seat, ref decisions);
                            }
                            break;

                        default:
                            spec.OnDecisionPoint?.Invoke(engine, engine.Rules.SeatOnTurn);
                            Act(spec, engine, sources, script, engine.Rules.SeatOnTurn, ref decisions);
                            break;
                    }
                }
                catch (FollowerDivergence divergence)
                {
                    // A divergence is an invariant violation like any other, and it gets the same artifact:
                    // the seed, the position and the replayable script, so it is reproducible from CI output
                    // alone rather than from a debugger.
                    throw Report(spec, engine, script, divergence.Message);
                }

                string violation = walk.Check(engine, engine.Events);
                if (violation != null)
                    throw Report(spec, engine, script, violation);
            }

            if ((spec.Checks & SelfPlayChecks.Termination) != 0)
            {
                string terminal = InvariantWalk.CheckTerminalState(engine);
                if (terminal != null)
                    throw Report(spec, engine, script, terminal);
            }
            else if (engine.Phase != MatchPhase.Complete)
                throw Report(spec, engine, script, "the game did not terminate");

            return Record(spec, engine, deck0, deck1, script.Count);
        }

        /// <summary>
        /// Ask one seat for one action, check what came back against the rules a second time, and submit it.
        /// The re-check is deliberately a second opinion rather than a call back into
        /// <see cref="Legality"/> — a bug shared between the enumeration and the validation would otherwise
        /// hide from both.
        /// </summary>
        static void Act(SelfPlayGameSpec spec, MatchEngine engine, IActionSource[] sources, List<ScriptLine> script, int seat, ref int decisions)
        {
            SeatView    view   = engine.BuildSeatView(seat);
            MatchIntent intent = sources[seat].ChooseAction(view, seat) ?? new EndTurnIntent();

            decisions++;

            // L2. The property the indistinguishability proof rests on: the same seat view and the same seed
            // give the same action. Sampled rather than taken every time, because it doubles the policy's cost.
            if (spec.DeterminismStride > 0 && decisions % spec.DeterminismStride == 0)
            {
                MatchIntent again = sources[seat].ChooseAction(engine.BuildSeatView(seat), seat) ?? new EndTurnIntent();
                if (!SameIntent(intent, again))
                    throw Report(spec, engine, script, $"L2: seat {seat} decided {Describe(intent)} and then {Describe(again)} from the same view");
            }

            if ((spec.Checks & SelfPlayChecks.Legality) != 0)
            {
                string illegal = LegalityChecks.Check(engine, view, seat, intent);
                if (illegal != null)
                    throw Report(spec, engine, script, illegal);
            }

            script.Add(ScriptLine.Of(engine.Turn, engine.ActionCount, seat, intent));

            MatchIntentResult result = engine.Submit(seat, intent);
            if (!result.IsSuccess)
                throw Report(spec, engine, script, $"L1: seat {seat}'s {Describe(intent)} was refused with {result}");
        }

        static SelfPlayGameRecord Record(SelfPlayGameSpec spec, MatchEngine engine, List<MatchDeckCard> deck0, List<MatchDeckCard> deck1, int actions)
        {
            MatchResult result = engine.Result;

            SelfPlayGameRecord record = new SelfPlayGameRecord
            {
                Seed        = spec.Seed,
                RunIndex    = spec.RunIndex,
                Outcome     = result.Outcome,
                WinnerSeat  = result.WinnerSeat,
                Cause       = result.Cause,
                FinalTurn   = result.FinalTurn,
                FirstSeat   = engine.Rules.FirstSeat,
                Weather     = engine.Weather?.WeatherId,
                Pairing     = spec.Pairing,
                ActionCount = actions,
            };

            record.PlayedBySeat0.AddRange(result.PlayedBySeat0);
            record.PlayedBySeat1.AddRange(result.PlayedBySeat1);
            AddClans(spec.Config, deck0, record.ClansSeat0);
            AddClans(spec.Config, deck1, record.ClansSeat1);

            return record;
        }

        static void AddClans(SharedGameConfig config, List<MatchDeckCard> deck, List<ClanId> clans)
        {
            foreach (MatchDeckCard card in deck)
            {
                ClanId clan = config.Cards[card.Card].Clan.Ref.ClanId;
                if (!clans.Contains(clan))
                    clans.Add(clan);
            }
        }

        // ---------------------------------------------------------------- the failure artifact

        static SelfPlayFailure Report(SelfPlayGameSpec spec, MatchEngine engine, List<ScriptLine> script, string violation)
        {
            StringBuilder text = new StringBuilder();
            text.Append("self-play failed: ").Append(violation).Append('\n');
            text.Append($"  seed {spec.Seed} (run index {spec.RunIndex}) at turn {engine.Turn}, action {engine.ActionCount}\n");
            text.Append($"  weather {engine.Weather?.WeatherId}, first seat {engine.Rules.FirstSeat}\n");
            text.Append("  script:\n");

            foreach (ScriptLine line in script)
                text.Append("    ").Append(line.Format()).Append('\n');

            return new SelfPlayFailure(text.ToString());
        }

        /// <summary> Whether two decisions are the same decision, compared as the payloads they would be. </summary>
        internal static bool SameIntent(MatchIntent a, MatchIntent b)
        {
            if (a == null || b == null)
                return ReferenceEquals(a, b);
            if (a.GetType() != b.GetType())
                return false;

            return SelfPlayBytes.Equal(SelfPlayBytes.Of<MatchIntent>(a), SelfPlayBytes.Of<MatchIntent>(b));
        }

        internal static string Describe(MatchIntent intent)
        {
            switch (intent)
            {
                case MulliganIntent mulligan:  return $"mulligan x{mulligan.Replace.Count}";
                case PlayCardIntent play:      return $"play {play.Card} at {play.Target}";
                case AttackIntent attack:      return $"attack {attack.Attacker} into {attack.Target}";
                case EffectChoiceIntent keep:  return $"keep x{keep.Keep.Count}";
                case EndTurnIntent _:          return "end turn";
                default:                       return intent?.GetType().Name ?? "nothing";
            }
        }

        /// <summary> One line of the replayable script: what happened, on which turn and action, to whom. </summary>
        readonly struct ScriptLine
        {
            readonly int    _turn;
            readonly int    _actionCount;
            readonly int    _seat;
            readonly string _what;

            ScriptLine(int turn, int actionCount, int seat, string what)
            {
                _turn        = turn;
                _actionCount = actionCount;
                _seat        = seat;
                _what        = what;
            }

            public static ScriptLine Deal(int turn, int actionCount, int firstSeat)
                => new ScriptLine(turn, actionCount, firstSeat, "deal");

            public static ScriptLine Of(int turn, int actionCount, int seat, MatchIntent intent)
                => new ScriptLine(turn, actionCount, seat, Describe(intent));

            public string Format() => $"turn {_turn} action {_actionCount} seat {_seat}: {_what}";
        }
    }

    /// <summary>
    /// Serializing a payload so two of them can be compared as bytes rather than as fields. Byte equality is
    /// the stronger claim <c>Docs/hidden-information.md</c> makes and the only one that catches a leak
    /// nobody thought to write a field-by-field comparison for.
    /// </summary>
    public static class SelfPlayBytes
    {
        /// <summary>
        /// Serialized at the static type, so a polymorphic payload carries its type tag and two payloads of
        /// different kinds cannot compare equal by coincidence.
        /// </summary>
        public static byte[] Of<T>(T value)
            => MetaSerialization.SerializeTagged(value, MetaSerializationFlags.IncludeAll, logicVersion: null);

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;

            for (int ndx = 0; ndx < a.Length; ndx++)
            {
                if (a[ndx] != b[ndx])
                    return false;
            }

            return true;
        }
    }
}
