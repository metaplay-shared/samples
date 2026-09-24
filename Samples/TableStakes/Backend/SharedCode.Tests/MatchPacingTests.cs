using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Measures game length against the timing budget in <c>docs/match.md</c>.
    /// <para>
    /// Each game runs through <see cref="MatchHost.RunTable"/> on a virtual clock that jumps to
    /// <see cref="MatchHost.GetNextWakeAt"/>, so it measures the designed pacing only.
    /// <c>WebClient.Tests/LiveServerPacingTests</c> measures network and browser time end to end. The human time
    /// per card is a parameter, so a change to the delays the game adds fails an assertion.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MatchPacingTests
    {
        /// <summary>The number of deals each measurement runs. Deal n uses the seed <c>SeedBase + n</c>.</summary>
        const int   NumGames  = 300;
        const ulong SeedBase  = 0x5EED_F1E5UL;

        static readonly MetaTime T0 = MatchTestDeals.T0;

        /// <summary>The minimum, mean and maximum game length over <see cref="NumGames"/> deals.</summary>
        readonly struct Measurement
        {
            public readonly MetaDuration Min;
            public readonly MetaDuration Mean;
            public readonly MetaDuration Max;

            public Measurement(MetaDuration min, MetaDuration mean, MetaDuration max)
            {
                Min  = min;
                Mean = mean;
                Max  = max;
            }

            public override string ToString()
                => $"min {Seconds(Min)} s, mean {Seconds(Mean)} s, max {Seconds(Max)} s";

            static string Seconds(MetaDuration duration) => (duration.Milliseconds / 1000.0).ToString("F1");
        }

        /// <summary>
        /// Plays one game on a virtual clock and returns the time from the deal to the end of the game. Every
        /// human seat plays <paramref name="humanThinkTime"/> after its turn starts.
        /// </summary>
        static MetaDuration MeasureOneGame(ulong seed, int numHumanSeats, MetaDuration humanThinkTime)
        {
            MatchModel model = MatchTestDeals.NewTable(seed, MatchTimings.Default, numHumanSeats, hasArrived: true);

            MatchPendingBotMove pendingBotMove = new MatchPendingBotMove();
            TestMatchHost   host  = new TestMatchHost(model, seed);

            for (int seat = 0; seat < numHumanSeats; seat++)
                MatchHost.NoteSeatPresent(model, seat, T0, host);

            MetaTime now = T0;
            MatchHost.RunTable(model, pendingBotMove, now, host);

            // The time the current turn started, from which a human's think time is measured. A turn starts
            // when the play index changes, because every turn advances the play index.
            int      onTurnAtPlayIndex = -1;
            MetaTime onTurnSince       = now;

            for (int step = 0; step < 400 && model.Phase == MatchPhase.Playing; step++)
            {
                int seatOnTurn = model.Board.SeatOnTurn;
                if (seatOnTurn >= 0 && model.Board.PlayIndex != onTurnAtPlayIndex)
                {
                    onTurnAtPlayIndex = model.Board.PlayIndex;
                    onTurnSince       = now;
                }

                MetaTime wakeAt = MatchHost.GetNextWakeAt(model, pendingBotMove);
                Assert.That(wakeAt, Is.GreaterThan(MetaTime.Epoch), $"seed {seed}: a playing table with nothing to wait for");

                bool     humanOnTurn = seatOnTurn >= 0 && seatOnTurn < numHumanSeats;
                MetaTime playAt      = onTurnSince + humanThinkTime;

                if (humanOnTurn && playAt <= wakeAt)
                {
                    // The human plays before the table's next wake time.
                    now = playAt > now ? playAt : now;

                    List<Card>        legal   = MatchRules.GetLegalPlays(model.Engine.GetHand(seatOnTurn), model.Board.LedSuit);
                    MoveRefusalReason refusal = MatchHost.TrySubmitMove(
                        model, MatchTestDeals.Player(seatOnTurn), seatOnTurn, model.Board.PlayIndex, legal[0], now, host);
                    Assert.That(refusal, Is.EqualTo(MoveRefusalReason.None), $"seed {seed}: a legal card was refused");
                }
                else
                {
                    now = wakeAt > now ? wakeAt : now;
                }

                MatchHost.RunTable(model, pendingBotMove, now, host);
            }

            Assert.That(model.Phase, Is.EqualTo(MatchPhase.Ended), $"seed {seed}: the table did not finish");
            return now - T0;
        }

        static Measurement Measure(int numHumanSeats, MetaDuration humanThinkTime)
        {
            long total = 0;
            long min   = long.MaxValue;
            long max   = long.MinValue;

            for (int game = 0; game < NumGames; game++)
            {
                long milliseconds = MeasureOneGame(SeedBase + (ulong)game, numHumanSeats, humanThinkTime).Milliseconds;
                total += milliseconds;
                if (milliseconds < min) min = milliseconds;
                if (milliseconds > max) max = milliseconds;
            }

            return new Measurement(
                MetaDuration.FromMilliseconds(min),
                MetaDuration.FromMilliseconds(total / NumGames),
                MetaDuration.FromMilliseconds(max));
        }

        /// <summary>
        /// Measures the delays the game adds at a table of one human and three bots, with the human playing
        /// instantly: one bot think delay per bot play and one resolve pause per trick.
        /// </summary>
        [Test]
        public void ABotFilledTablesOwnPacingIsUnderThirtySeconds()
        {
            Measurement measured = Measure(numHumanSeats: 1, MetaDuration.Zero);
            TestContext.Out.WriteLine($"Bot-filled table, host-driven pacing only ({NumGames} deals): {measured}");

            // The expected range is computed from the bot think delays and resolve pause in MatchTimings.Default
            // and the bot profiles in TestBotConfig.Opponents. Update it when those change.
            Assert.That(measured.Mean.Milliseconds, Is.InRange(24_000, 32_000), "the bot pacing has drifted from the timing budget");
            Assert.That(measured.Max.Milliseconds, Is.LessThan(45_000), "the slowest bot-driven game must still leave room for a person to play in");
        }

        /// <summary>
        /// Measures the delays the game adds at a table of four humans playing instantly. The game adds no delay
        /// to a human's turn, so only the resolve pauses remain.
        /// </summary>
        [Test]
        public void AFourHumanTablesOwnPacingIsTheResolvePausesAlone()
        {
            Measurement measured = Measure(numHumanSeats: MatchRules.NumSeats, MetaDuration.Zero);
            TestContext.Out.WriteLine($"Four humans, host-driven pacing only ({NumGames} deals): {measured}");

            MetaDuration pauses = MatchTimings.Default.ResolvePause * MatchRules.NumTricks;
            Assert.That(measured.Min, Is.EqualTo(pauses));
            Assert.That(measured.Max, Is.EqualTo(pauses), "nothing but the resolve pauses may pace a table of four people");
        }

        /// <summary>
        /// Checks the budget's figures for a table of one human and three bots (about 45 seconds) and a table of
        /// four humans (about 90 seconds), with a fixed human think time per card. The assertion is on the total,
        /// so a change to bot pacing that does not also update the budget fails here. With four humans every play
        /// is a human's, so that game is longer than a bot-filled one even though the game adds only the resolve
        /// pauses.
        /// </summary>
        [TestCase(1, 3, 38_000, 52_000)]
        [TestCase(4, 4, 78_000, 102_000)]
        public void AGameLandsOnTheBudgetsFigure(int numHumanSeats, int humanThinkSeconds, int minMeanMs, int maxMeanMs)
        {
            MetaDuration humanThink = MetaDuration.FromSeconds(humanThinkSeconds);
            Measurement  measured   = Measure(numHumanSeats, humanThink);
            TestContext.Out.WriteLine($"{numHumanSeats} human seats at {humanThink} a card ({NumGames} deals): {measured}");

            Assert.That(measured.Mean.Milliseconds, Is.InRange(minMeanMs, maxMeanMs), "the game length has drifted from the timing budget");
        }

        /// <summary>
        /// Measures the budget's worst case: four humans who each use almost the whole move deadline on every
        /// card. No game in progress can be slower, because the host plays a card for a seat that misses the
        /// deadline.
        /// </summary>
        [Test]
        public void TheWorstCaseIsEverySeatSpendingItsWholeDeadline()
        {
            // Just inside the deadline, so the human plays the card instead of the host auto-playing it.
            MetaDuration humanThink = MatchTimings.Default.MoveDeadline - MetaDuration.FromMilliseconds(100);
            Measurement  measured   = Measure(numHumanSeats: MatchRules.NumSeats, humanThink);
            TestContext.Out.WriteLine($"Four humans at the move deadline ({NumGames} deals): {measured}");

            MetaDuration expected = humanThink * MatchRules.NumPlays + MatchTimings.Default.ResolvePause * MatchRules.NumTricks;
            Assert.That(measured.Max, Is.EqualTo(expected));
            Assert.That(measured.Max.Milliseconds, Is.LessThan(420_000), "a game nobody abandons still has to end inside a sitting");
        }
    }
}
