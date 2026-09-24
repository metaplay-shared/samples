using System.Collections.Generic;
using System.Text;

namespace Game.Logic.Tests
{
    /// <summary>
    /// What one self-play game produced. The invariants are asserted as the game runs, so this is not how a
    /// failure is reported — it is the per-game record a balance harness would aggregate
    /// (<c>Docs/bots.md</c>, "Balance-harness hooks").
    /// <para>
    /// Self-play owes that harness only the shape: which cards each side played, which clans each deck drew
    /// from, how the game ended and how many turns it took. Producing it costs nothing here because the game
    /// already has all of it in hand, and leaving it out would mean re-deriving the same machinery later.
    /// </para>
    /// </summary>
    public sealed class SelfPlayGameRecord
    {
        public ulong         Seed;
        public int           RunIndex;
        public MatchOutcome  Outcome;
        public int           WinnerSeat;
        public MatchEndCause Cause;
        /// <summary> Turns counting both seats', which is what the engine's position counts. </summary>
        public int           FinalTurn;
        public int           FirstSeat;
        public WeatherId     Weather;
        /// <summary> Which profile pairing played it, so a batch's numbers can be read per pairing. </summary>
        public string        Pairing;
        /// <summary> Player actions accepted across the whole game, both seats. </summary>
        public int           ActionCount;
        /// <summary> Per seat, the cards played from that seat's own starting deck with their owned ranks: the Heist-eligible shape. </summary>
        public List<HeistEligibleCard> PlayedBySeat0 = new List<HeistEligibleCard>();
        public List<HeistEligibleCard> PlayedBySeat1 = new List<HeistEligibleCard>();
        public List<ClanId>  ClansSeat0    = new List<ClanId>();
        public List<ClanId>  ClansSeat1    = new List<ClanId>();

        public bool IsDraw => Outcome == MatchOutcome.Draw;

        public override string ToString() => $"seed {Seed}: {Outcome} on turn {FinalTurn} ({Cause}), {ActionCount} actions";
    }

    /// <summary> A whole batch's worth of records, plus the questions a batch is usually asked. </summary>
    public sealed class SelfPlayBatch
    {
        public readonly List<SelfPlayGameRecord> Games = new List<SelfPlayGameRecord>();

        public int Count => Games.Count;

        public int Wins(int seat)
        {
            int wins = 0;
            foreach (SelfPlayGameRecord game in Games)
            {
                if (!game.IsDraw && game.WinnerSeat == seat)
                    wins++;
            }
            return wins;
        }

        public int Draws()
        {
            int draws = 0;
            foreach (SelfPlayGameRecord game in Games)
            {
                if (game.IsDraw)
                    draws++;
            }
            return draws;
        }

        public int LongestGame()
        {
            int longest = 0;
            foreach (SelfPlayGameRecord game in Games)
            {
                if (game.FinalTurn > longest)
                    longest = game.FinalTurn;
            }
            return longest;
        }

        /// <summary> Mean turns, in tenths, so the summary line carries a decimal without a float in the suite. </summary>
        public int AverageTurnsTenths()
        {
            if (Games.Count == 0)
                return 0;

            int total = 0;
            foreach (SelfPlayGameRecord game in Games)
                total += game.FinalTurn;

            return total * 10 / Games.Count;
        }

        public int EndedBy(MatchEndCause cause)
        {
            int count = 0;
            foreach (SelfPlayGameRecord game in Games)
            {
                if (game.Cause == cause)
                    count++;
            }
            return count;
        }

        /// <summary> Games the seat that moved first went on to win, and how many it played. </summary>
        public int FirstSeatWins()
        {
            int wins = 0;
            foreach (SelfPlayGameRecord game in Games)
            {
                if (!game.IsDraw && game.WinnerSeat == game.FirstSeat)
                    wins++;
            }
            return wins;
        }

        public int Decided()
        {
            int decided = 0;
            foreach (SelfPlayGameRecord game in Games)
            {
                if (!game.IsDraw)
                    decided++;
            }
            return decided;
        }

        /// <summary>
        /// The line every bulk fixture writes, so a run's shape is visible in CI output.
        /// <para>
        /// The per-pairing and first-seat breakdowns matter more than they look. A batch that rotates through
        /// uneven pairings has no reason to come out even overall, so a 50/50 aggregate over one is a
        /// coincidence rather than a finding — and reading it as one is how a first-player advantage gets
        /// reported as balance. Split by the two things that actually explain a win and neither claim is
        /// available to be made by accident.
        /// </para>
        /// </summary>
        public string Summary(string label)
        {
            StringBuilder text = new StringBuilder();
            text.Append(label).Append(": ").Append(Count).Append(" games, ");
            text.Append("seat0 ").Append(Wins(0)).Append(" / seat1 ").Append(Wins(1)).Append(" / draws ").Append(Draws());
            text.Append(", first seat won ").Append(FirstSeatWins()).Append(" of ").Append(Decided());
            text.Append(", turns avg ").Append(AverageTurnsTenths() / 10).Append('.').Append(AverageTurnsTenths() % 10);
            text.Append(" max ").Append(LongestGame());
            text.Append(", ended on a Den ").Append(EndedBy(MatchEndCause.DenAtZero));
            text.Append(" / both Dens ").Append(EndedBy(MatchEndCause.BothDensAtZero));

            foreach (string pairing in Pairings())
            {
                SelfPlayBatch slice = Where(pairing);
                text.Append("\n    ").Append(pairing).Append(": ").Append(slice.Count).Append(" games, ");
                text.Append("seat0 ").Append(slice.Wins(0)).Append(" / seat1 ").Append(slice.Wins(1));
                text.Append(", first seat ").Append(slice.FirstSeatWins()).Append(" of ").Append(slice.Decided());
                text.Append(", turns avg ").Append(slice.AverageTurnsTenths() / 10).Append('.').Append(slice.AverageTurnsTenths() % 10);
            }

            return text.ToString();
        }

        List<string> Pairings()
        {
            List<string> names = new List<string>();
            foreach (SelfPlayGameRecord game in Games)
            {
                if (game.Pairing != null && !names.Contains(game.Pairing))
                    names.Add(game.Pairing);
            }
            return names;
        }

        SelfPlayBatch Where(string pairing)
        {
            SelfPlayBatch slice = new SelfPlayBatch();
            foreach (SelfPlayGameRecord game in Games)
            {
                if (game.Pairing == pairing)
                    slice.Games.Add(game);
            }
            return slice;
        }
    }
}
