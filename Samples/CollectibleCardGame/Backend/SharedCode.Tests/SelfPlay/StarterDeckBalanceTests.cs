using Metaplay.Core;
using NUnit.Framework;
using System.Collections.Generic;
using System.Text;

namespace Game.Logic.Tests
{
    /// <summary>
    /// How the six config-authored starter decks do against each other. <b>Content measurement, not an
    /// arena</b>, and the distinction is the whole reason this file is separate from everything else in
    /// <c>SelfPlay/</c>: self-play deliberately does not grade bots by win rate
    /// (<c>Docs/bots.md</c>), because a bare win rate with no confidence interval and no rotated
    /// schedule reports the first-player advantage as strength. What is being measured here is the
    /// <em>decks</em>, with the player held constant.
    /// <para>
    /// Both seats play <see cref="SelfPlaySeats.Strongest"/>, which never reads the seed
    /// (<c>ProfileTests.TheStrongestProfileNeverReadsTheSeed</c>) and plays the same game as
    /// <c>StrictlyDeterministic</c> (<c>ProfileTests.TheStrictlyDeterministicProfilePlaysTheStrongestProfilesGame</c>) —
    /// so the only randomness in the sweep is the deal seed, which is what makes the matrix a statement about
    /// the cards.
    /// </para>
    /// <para>
    /// <b>Both seat orders, the same seeds.</b> Each unordered pair plays every seed twice, once from each
    /// side, so each deck sits first in exactly half of the pair's games and the ~59 % first-seat effect
    /// cancels out of the pair statistic instead of being attributed to a deck. The six mirror cells are
    /// omitted: two identical lists differ only by the deal, so the cell would measure the seat effect and
    /// nothing else, which <c>StateSanityTests</c> already reports.
    /// </para>
    /// <para>
    /// Every deck is held at <see cref="GlobalConfig.RankMin"/>. A starter deck has no rank dimension — it is
    /// a card list — and a fixed rank is what keeps the signal about the cards. This is also why the sweep
    /// does not replace <see cref="SelfPlayDecks"/>' rotation, which exists to read the rank tracks.
    /// </para>
    /// </summary>
    [TestFixture]
    public class StarterDeckBalanceTests
    {
        static SharedGameConfig Config => TestGameConfig.Shared;

        /// <summary>
        /// The tolerance a deck's whole row has to sit inside: no deck is hopeless. It is wide at the standard
        /// size because the sample is small there, and narrower at the deep size where it is not.
        /// </summary>
        static int RowTolerancePercentagePoints => SelfPlayRun.IsDeep ? 25 : 35;

        /// <summary>
        /// The tolerance one cell has to sit inside, asserted only at the deep size: a pair at the standard size
        /// is 24 games, whose three standard deviations are wider than any useful pin.
        /// </summary>
        const int CellTolerancePercentagePoints = 35;

        /// <summary> Below this many seeds per pair, a cell is too small to assert anything about. </summary>
        const int MinimumSeedsForACellAssertion = 50;

        [Test]
        public void NoStarterDeckIsRunawayOrHopeless()
        {
            int      seeds  = SelfPlayRun.Games(standard: 12, deep: 200);
            Matrix   matrix = Play(seeds);

            TestContext.Out.WriteLine(matrix.Render(seeds));

            // A run that drew its way to fifty per cent must not pass. A deep self-play run recorded not
            // one draw in twenty thousand games, so this is a tripwire on the harness rather than a bound.
            Assert.That(matrix.Decided, Is.GreaterThanOrEqualTo(matrix.Games * 9 / 10),
                $"only {matrix.Decided} of {matrix.Games} games were decided");

            for (int deck = 0; deck < matrix.DeckCount; deck++)
            {
                int rate = matrix.RowWinPercent(deck);
                Assert.That(System.Math.Abs(rate - 50), Is.LessThanOrEqualTo(RowTolerancePercentagePoints),
                    $"{matrix.Name(deck)} won {rate}% across its five opponents");
            }

            if (seeds < MinimumSeedsForACellAssertion)
                return;

            for (int row = 0; row < matrix.DeckCount; row++)
            {
                for (int column = 0; column < matrix.DeckCount; column++)
                {
                    if (row == column)
                        continue;

                    int rate = matrix.CellWinPercent(row, column);
                    Assert.That(System.Math.Abs(rate - 50), Is.LessThanOrEqualTo(CellTolerancePercentagePoints),
                        $"{matrix.Name(row)} won {rate}% against {matrix.Name(column)}");
                }
            }
        }

        [Test]
        public void EveryStarterDeckCostsTheSame()
        {
            // The one authored invariant the six decks share: the same total cost, so the matrix measures what
            // the cards do rather than what they cost. Clan halves are not equal, so each deck drops the two
            // Wanderers that cancel its own clan pair's skew.
            List<int> costs = new List<int>();
            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
            {
                int cost = 0;
                foreach (MetaRef<CardInfo> card in deck.Cards)
                    cost += card.Ref.Cost;
                costs.Add(cost);
            }

            Assert.That(costs, Has.All.EqualTo(costs[0]), string.Join(", ", costs));
        }

        // The 2026-09-09 baseline (N=200, 6,000 games, row sd about 1.1 pp) reads FireAndFoam 46 %, SunlitThicket
        // 57 %, AlleySparks 37 %, PorchlightPack 34 %, RiverbankPatience 64 % and EmberAndOak 59 %; the widest
        // cells are RiverbankPatience 74 % over AlleySparks and EmberAndOak 73 % over PorchlightPack (cell sd
        // 2.5 pp). Three rows sit twelve to sixteen points off even. That is content, not noise: the pool is
        // authored to cover the effect vocabulary rather than to be balanced (Docs/game-design.md), so the
        // tolerances above only say no deck is hopeless — tighten them when a balance pass lands. The largest
        // legal edit to StarterDecks.csv moves a row by about five points, so no deck edit alone can fail the
        // guardrail: diffing a fresh print against the previous one is what catches a deck-authoring regression.
        [Test, Explicit("Prints the matrix; ~6,000 games. Run it after any content edit and diff the table "
            + "against the previous print — a deck edit cannot fail the guardrail, so this print is "
            + "what catches one.")]
        public void StarterDeckWinRateMatrix_Printed()
        {
            Matrix matrix = Play(seeds: 200);

            TestContext.Out.WriteLine(matrix.Render(200));
        }

        // ---------------------------------------------------------------- the sweep

        static Matrix Play(int seeds)
        {
            List<StarterDeckInfo> decks = new List<StarterDeckInfo>();
            foreach (StarterDeckInfo deck in Config.StarterDecks.Values)
                decks.Add(deck);

            Matrix matrix = new Matrix(decks);
            int    index  = 0;

            for (int a = 0; a < decks.Count; a++)
            {
                for (int b = a + 1; b < decks.Count; b++)
                {
                    for (int seed = 0; seed < seeds; seed++)
                    {
                        ulong gameSeed = SelfPlayRun.GameSeed(SelfPlayStreams.StarterDeckBalance, index++);

                        // The same seed from both sides, so the seat effect cancels within the pair.
                        matrix.Record(a, b, RunOne(matrix.Cards(a), matrix.Cards(b), gameSeed));
                        matrix.Record(b, a, RunOne(matrix.Cards(b), matrix.Cards(a), gameSeed));
                    }
                }
            }

            return matrix;
        }

        /// <summary>
        /// One game, with everything that is cost rather than coverage switched off: the invariant walk, the
        /// re-derivation stride, the follower mirror and the pacing. Every one of those is asserted over the
        /// same engine by the invariant sweeps, so paying for them here would double the cost of a run whose
        /// whole output is a win count.
        /// </summary>
        static SelfPlayGameRecord RunOne(List<MatchDeckCard> seat0, List<MatchDeckCard> seat1, ulong seed)
            => SelfPlayHarness.RunGame(new SelfPlayGameSpec
            {
                Config            = Config,
                Seed              = seed,
                Seat0             = SelfPlaySeats.Strongest,
                Seat1             = SelfPlaySeats.Strongest,
                Deck0             = seat0,
                Deck1             = seat1,
                Checks            = SelfPlayChecks.None,
                DeterminismStride = 0,
                MirrorOnFollower  = false,
                Timings           = MatchTimings.Instant,
            });

        // ---------------------------------------------------------------- the table

        /// <summary> Wins and games per ordered cell, plus the rendering. </summary>
        sealed class Matrix
        {
            readonly List<StarterDeckInfo>    _decks;
            readonly List<List<MatchDeckCard>> _cards;
            readonly int[,]                   _seat0Wins;
            readonly int[,]                   _decided;

            public int Games   { get; private set; }
            public int Decided { get; private set; }
            public int FirstSeatWins { get; private set; }

            public int DeckCount => _decks.Count;

            public Matrix(List<StarterDeckInfo> decks)
            {
                _decks     = decks;
                _cards     = new List<List<MatchDeckCard>>();
                _seat0Wins = new int[decks.Count, decks.Count];
                _decided   = new int[decks.Count, decks.Count];

                int rankMin = TestGameConfig.Shared.Global.RankMin;
                foreach (StarterDeckInfo deck in decks)
                {
                    List<MatchDeckCard> cards = new List<MatchDeckCard>();
                    foreach (CardId cardId in deck.ToCardIds())
                        cards.Add(new MatchDeckCard(cardId, rankMin));
                    _cards.Add(cards);
                }
            }

            public string Name(int deck) => _decks[deck].StarterDeckId.Value;

            /// <summary> A fresh list per game: the engine takes ownership of what it is handed. </summary>
            public List<MatchDeckCard> Cards(int deck) => new List<MatchDeckCard>(_cards[deck]);

            /// <summary> One game in which <paramref name="seat0"/>'s deck moved into seat 0. </summary>
            public void Record(int seat0, int seat1, SelfPlayGameRecord record)
            {
                Games++;
                if (record.IsDraw)
                    return;

                Decided++;
                _decided[seat0, seat1]++;

                if (record.WinnerSeat == 0)
                    _seat0Wins[seat0, seat1]++;

                if (record.WinnerSeat == record.FirstSeat)
                    FirstSeatWins++;
            }

            /// <summary> How often <paramref name="row"/> beat <paramref name="column"/>, from both sides. </summary>
            public int CellWinPercent(int row, int column)
            {
                int wins    = _seat0Wins[row, column] + (_decided[column, row] - _seat0Wins[column, row]);
                int decided = _decided[row, column] + _decided[column, row];

                return decided == 0 ? 50 : wins * 100 / decided;
            }

            /// <summary> How often <paramref name="row"/> won, over all five of its opponents. </summary>
            public int RowWinPercent(int row)
            {
                int wins    = 0;
                int decided = 0;

                for (int column = 0; column < DeckCount; column++)
                {
                    if (column == row)
                        continue;

                    wins    += _seat0Wins[row, column] + (_decided[column, row] - _seat0Wins[column, row]);
                    decided += _decided[row, column] + _decided[column, row];
                }

                return decided == 0 ? 50 : wins * 100 / decided;
            }

            public string Render(int seeds)
            {
                StringBuilder text = new StringBuilder();
                text.Append("starter deck win-rate matrix, N=").Append(seeds)
                    .Append(" seeds per pair, both seat orders, strongest vs strongest\n");

                text.Append("".PadRight(20));
                for (int column = 0; column < DeckCount; column++)
                    text.Append(Name(column).PadLeft(20));
                text.Append("overall".PadLeft(10)).Append('\n');

                for (int row = 0; row < DeckCount; row++)
                {
                    text.Append(Name(row).PadRight(20));
                    for (int column = 0; column < DeckCount; column++)
                        text.Append((row == column ? "—" : CellWinPercent(row, column) + "%").PadLeft(20));
                    text.Append((RowWinPercent(row) + "%").PadLeft(10)).Append('\n');
                }

                // The first-seat line is the sanity check that the schedule really did rotate: a number far
                // from the recorded ~59 % means the rotation or the seeding is wrong, not that the content
                // changed. It is never asserted — it is content evidence rather than a property.
                text.Append(Games).Append(" games, ").Append(Games - Decided).Append(" draws, first seat won ")
                    .Append(FirstSeatWins).Append(" of ").Append(Decided);
                if (Decided > 0)
                    text.Append(" (").Append(FirstSeatWins * 100 / Decided).Append("%)");

                return text.ToString();
            }
        }
    }
}
