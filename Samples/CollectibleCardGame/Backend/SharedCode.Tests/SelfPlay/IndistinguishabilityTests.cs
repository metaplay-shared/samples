using NUnit.Framework;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The statistical half of the secrecy proof. The structural half is discharged once by the seat view's
    /// type; this is the half that catches a leak which survives being encoded in a field the type does allow
    /// — a count in the wrong place, an ordering that should have been a multiset, an identity minted in draw
    /// order (<c>Docs/bots.md</c>, "The secrecy proof").
    /// <para>
    /// The mechanism is a replay. Self-play reaches a decision point in the ordinary way; a second copy of the
    /// authoritative state is built that differs only within the equivalence class
    /// <c>Docs/hidden-information.md</c> defines; the seat's view is derived from each and both are
    /// asserted byte-identical, and the policy's decision from each identical too.
    /// </para>
    /// </summary>
    [TestFixture]
    public class IndistinguishabilityTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        [Test]
        public void ASeatCannotTellTwoDealsOfTheSameGameApart()
        {
            SweepCoverage coverage = new SweepCoverage();
            int           games    = SelfPlayRun.Games(standard: 40, deep: 800);

            SelfPlayHarness.RunBatch(
                "indistinguishability",
                SelfPlayStreams.Indistinguishability,
                games,
                index =>
                {
                    // The sample points move from game to game, so the sweep is not forty copies of "turn
                    // three, before anything interesting has happened".
                    int   stride = 2 + index % 5;
                    int   seen   = 0;
                    ulong seed   = SelfPlayRun.GameSeed(SelfPlayStreams.Indistinguishability, index);

                    return new SelfPlayGameSpec
                    {
                        Config            = Config,
                        Seat0             = SelfPlaySeats.Strongest,
                        Seat1             = SelfPlaySeats.Profile(BotProfile.Casual),
                        Checks            = SelfPlayChecks.None,
                    // Every action of this game also runs against a follower, and the two models are
                    // compared after each one. This is the refactor's proof, and it is on for every batch.
                    MirrorOnFollower = true,
                        DeterminismStride = 0,
                        OnDecisionPoint   = (engine, seat) =>
                        {
                            if (++seen % stride != 0)
                                return;

                            CheckOnePoint(engine, seat, seed, coverage);
                        },
                    };
                });

            TestContext.Out.WriteLine(coverage.Summary());

            Assert.That(coverage.Compared, Is.GreaterThan(games), "the sweep has to reach more than one position per game");
            Assert.That(coverage.WithSneaky, Is.GreaterThan(0), "a turn with a Sneaky critter in play was never sampled");
            Assert.That(coverage.WithGraveyard, Is.GreaterThan(0), "a position after something died was never sampled");
            Assert.That(coverage.WithMintedCards, Is.GreaterThan(0), "a position after a token or a copy was minted was never sampled");
            Assert.That(coverage.LateTurns, Is.GreaterThan(0), "only opening turns were sampled");
        }

        /// <summary>
        /// One position, one mutation, both assertions. Returns quietly when the position has nothing hidden
        /// left to move: a seat that can already see everything is a position this check has nothing to say
        /// about, not a failure.
        /// </summary>
        static void CheckOnePoint(MatchEngine engine, int seat, ulong gameSeed, SweepCoverage coverage)
        {
            if (!IndistinguishablePair.TryBuild(engine, seat, gameSeed, out IndistinguishablePair pair))
                return;

            coverage.Note(engine, seat);

            byte[] left  = HiddenState.SeatPayload(pair.Left, seat);
            byte[] right = HiddenState.SeatPayload(pair.Right, seat);

            if (!SelfPlayBytes.Equal(left, right))
            {
                Assert.Fail(
                    $"a seat could tell two indistinguishable deals apart at turn {engine.Turn}, action {engine.ActionCount}: "
                    + $"the payload is {left.Length} bytes one way and {right.Length} the other\n{pair.Describe(seat)}");
            }

            // The decision has to follow, and it does not follow for free: it is the property that turns "the
            // payload matched" into "nothing the seat could act on moved".
            BotPolicy   policy = new BotPolicy(TestGameConfig.Shared, BotProfile.Casual, pair.Seed);
            MatchIntent one    = policy.ChooseAction(pair.Left.BuildSeatView(seat), seat);
            MatchIntent other  = policy.ChooseAction(pair.Right.BuildSeatView(seat), seat);

            if (!SameDecision(one, other))
            {
                Assert.Fail(
                    $"a seat decided differently across two indistinguishable deals at turn {engine.Turn}, action {engine.ActionCount}: "
                    + $"{SelfPlayHarness.Describe(one)} against {SelfPlayHarness.Describe(other)}\n{pair.Describe(seat)}");
            }
        }

        static bool SameDecision(MatchIntent a, MatchIntent b)
        {
            if (a == null || b == null)
                return ReferenceEquals(a, b);
            if (a.GetType() != b.GetType())
                return false;

            return SelfPlayBytes.Equal(SelfPlayBytes.Of<MatchIntent>(a), SelfPlayBytes.Of<MatchIntent>(b));
        }

        /// <summary>
        /// What the sweep actually reached. A secrecy check that only ever looked at turn one would be a
        /// hundred assertions about the deal and none about the game.
        /// </summary>
        sealed class SweepCoverage
        {
            public int Compared;
            public int WithSneaky;
            public int WithGraveyard;
            public int WithMintedCards;
            public int LateTurns;

            public void Note(MatchEngine engine, int seat)
            {
                Compared++;

                // One increment per position, not per critter and not per seat: these are counts of positions
                // the sweep reached, and the printed line says so.
                bool sneaky    = false;
                bool graveyard = false;

                for (int side = 0; side < MatchSeats.Count; side++)
                {
                    SeatState state = engine.Rules.Seat(side);

                    foreach (BoardCritter critter in state.Board)
                        sneaky |= (critter.Keywords & KeywordFlags.Sneaky) != 0;

                    graveyard |= state.Graveyard.Count > 0;
                }

                if (sneaky)
                    WithSneaky++;
                if (graveyard)
                    WithGraveyard++;

                // Tokens and graveyard copies only. The second player's compensation card is minted at the
                // deal too, so counting it would make this true of every position and say nothing.
                CardId compensation = TestGameConfig.Shared.Global.SecondPlayerBonusCard?.Ref.CardId;
                foreach (CardInstance instance in engine.Rules.Instances)
                {
                    if (!instance.FromStartingDeck && AuthorityZones.Of(engine.Model, instance) != AuthorityZone.Limbo && CardLookup.CardId(engine.Model, instance.Id) != compensation)
                    {
                        WithMintedCards++;
                        break;
                    }
                }

                if (engine.Turn > 10)
                    LateTurns++;
            }

            public string Summary()
                => $"indistinguishability sweep: {Compared} positions compared, {WithSneaky} with a Sneaky critter, "
                   + $"{WithGraveyard} with a graveyard, {WithMintedCards} with a minted card, {LateTurns} past turn 10";
        }
    }

    /// <summary>
    /// Two authoritative states that one seat is supposed to be unable to tell apart: the same game, with
    /// every deck order reshuffled and the opponent's unseen pool re-split between their deck and their hand.
    /// </summary>
    public sealed class IndistinguishablePair
    {
        public MatchEngine Left  { get; private set; }
        public MatchEngine Right { get; private set; }
        public ulong       Seed  { get; private set; }

        /// <summary>
        /// Build a pair whose hidden halves genuinely differ. A mutation that happened to produce the same
        /// partition would make the comparison pass for the wrong reason, so this keeps re-rolling until the
        /// opponent really is holding something else, and gives up rather than pretending.
        /// </summary>
        public static bool TryBuild(MatchEngine engine, int seat, ulong gameSeed, out IndistinguishablePair pair)
        {
            pair = null;

            SharedGameConfig config    = engine.Config;
            int              enemySeat = MatchSeats.Other(seat);
            // A seed the failure message can name, and one that moves with the position so two samples in the
            // same game do not re-deal the same way.
            ulong            baseSeed  = gameSeed
                                         ^ ((ulong)(uint)engine.Turn << 20)
                                         ^ ((ulong)(uint)engine.ActionCount << 4)
                                         ^ (ulong)(uint)seat;

            for (int attempt = 1; attempt <= 8; attempt++)
            {
                MatchModel left  = HiddenState.Clone(engine.State, config);
                MatchModel right = HiddenState.Clone(engine.State, config);

                if (!HiddenState.Redeal(right, seat, baseSeed + (ulong)attempt))
                    return false;

                if (!HiddenState.EnemyHandDiffers(left, right, enemySeat))
                    continue;
                if (!HiddenState.EnemyDeckDiffers(left, right, enemySeat))
                    continue;

                pair = new IndistinguishablePair
                {
                    Left  = HiddenState.Wrap(left),
                    Right = HiddenState.Wrap(right),
                    Seed  = baseSeed + (ulong)attempt,
                };
                return true;
            }

            return false;
        }

        public string Describe(int seat)
            => $"  seat {seat}, mutation seed {Seed}\n"
               + $"  left  enemy hand {Left.SecretHand(MatchSeats.Other(seat)).Count}, deck {Left.SecretDeck(MatchSeats.Other(seat)).Count}\n"
               + $"  right enemy hand {Right.SecretHand(MatchSeats.Other(seat)).Count}, deck {Right.SecretDeck(MatchSeats.Other(seat)).Count}";
    }
}
