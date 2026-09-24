using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// The one place the bot policy consumes randomness. Two properties are asked of it: that a decision's
    /// stream depends on every one of the decision's coordinates, so no two decisions share a draw, and that
    /// the derivation <b>loses information about the seed</b>, so a recovered key is not the deal.
    /// </summary>
    [TestFixture]
    public class BotSubstreamTests
    {
        const int Somewhere = 42;

        [Test]
        public void EveryCoordinateSeparatesTheStream()
        {
            // A coordinate that did not reach the key would silently share a stream with its neighbour, which
            // is the collision the key exists to prevent — and the peek's is the one that matters most, since
            // a held choice is answered at the same action count as the decision that would follow it.
            List<string> streams = new List<string>();

            Add(streams, BotSubstreams.For(7, 0, BotDecisionKind.MainPhase, Somewhere), "seat 0 main");
            Add(streams, BotSubstreams.For(7, 1, BotDecisionKind.MainPhase, Somewhere), "seat 1 main");
            Add(streams, BotSubstreams.For(7, 0, BotDecisionKind.Mulligan, Somewhere), "seat 0 mulligan");
            Add(streams, BotSubstreams.For(7, 0, BotDecisionKind.EffectChoice, Somewhere), "seat 0 peek");
            Add(streams, BotSubstreams.For(7, 0, BotDecisionKind.MainPhase, Somewhere + 1), "next action");
            Add(streams, BotSubstreams.For(8, 0, BotDecisionKind.MainPhase, Somewhere), "another match");

            Assert.That(streams.Count, Is.EqualTo(new HashSet<string>(streams).Count), "two decisions share a stream");
        }

        [Test]
        public void TheSameCoordinatesAlwaysGiveTheSameStream()
        {
            Assert.That(
                First(BotSubstreams.For(12345, 1, BotDecisionKind.MainPhase, Somewhere)),
                Is.EqualTo(First(BotSubstreams.For(12345, 1, BotDecisionKind.MainPhase, Somewhere))));
        }

        [Test]
        public void TheDerivationLosesTheSeed()
        {
            // The whole point of folding the key in half. Every input to a substream key except the seed is
            // public, so an invertible derivation would make one recovered key name one deal seed — and the
            // deal seed is the game's single real secret (Docs/hidden-information.md).
            //
            // Non-invertibility is shown the only way it can be shown cheaply: by finding two seeds that land
            // on the same stream. A bijection has none, so one collision is a proof. Over a folded 32-bit key
            // the birthday bound puts the first at a few tens of thousands of samples.
            Dictionary<ulong, ulong> seen = new Dictionary<ulong, ulong>();

            for (ulong seed = 1; seed <= 400000; seed++)
            {
                ulong stream = First(BotSubstreams.For(seed, 0, BotDecisionKind.MainPhase, Somewhere));
                if (seen.TryGetValue(stream, out ulong earlier))
                {
                    TestContext.Out.WriteLine($"seeds {earlier} and {seed} derive the same substream");
                    Assert.Pass();
                }

                seen[stream] = seed;
            }

            Assert.Fail("no two seeds collided in 400,000, which is what an invertible derivation looks like");
        }

        static void Add(List<string> streams, RandomPCG stream, string what) => streams.Add($"{what}={First(stream)}");

        /// <summary> A stream's identity, read without disturbing the one the caller holds. </summary>
        static ulong First(RandomPCG stream) => new RandomPCG(stream).NextULong();
    }
}
