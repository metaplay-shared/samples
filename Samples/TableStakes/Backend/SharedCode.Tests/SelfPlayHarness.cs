using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic.Tests
{
    /// <summary>
    /// Self-play harness: plays seeded bot-vs-bot games and checks the invariants a finished game must satisfy.
    /// <para>
    /// The checks <b>return violations instead of asserting</b>, so a negative-control test can run each check
    /// against a deliberately broken game and confirm it reports a violation. Without that control, a check that
    /// inspected nothing would also report no violations.
    /// </para>
    /// </summary>
    public static class SelfPlayHarness
    {
        /// <summary>
        /// The record of one self-played game. The fields are writable so a negative-control test can corrupt a
        /// real game and confirm the checks report it.
        /// </summary>
        public sealed class GameLog
        {
            public ulong DealSeed;
            public ulong BotSeed;
            public Suit  TrumpSuit;
            public int   StartingLeaderSeat;

            public List<PlayRecord>   Plays            = new List<PlayRecord>();
            public List<int>          TrickWinnerSeats = new List<int>();
            public List<SeatStanding> Standings        = new List<SeatStanding>();
            public List<MetaDuration> ThinkDelays      = new List<MetaDuration>();

            /// <summary>
            /// Cards the chooser offered that are not legal plays from the seat's authoritative hand.
            /// </summary>
            public List<string> IllegalOffers = new List<string>();

            /// <summary>Refusals the engine returned. A self-played game should produce none.</summary>
            public List<MoveRefusalReason> Refusals = new List<MoveRefusalReason>();

            public override string ToString() => $"deal seed {DealSeed}, bot seed {BotSeed}";

            public GameLog Clone()
            {
                GameLog copy = new GameLog
                {
                    DealSeed           = DealSeed,
                    BotSeed            = BotSeed,
                    TrumpSuit          = TrumpSuit,
                    StartingLeaderSeat = StartingLeaderSeat,
                    Plays              = new List<PlayRecord>(Plays),
                    TrickWinnerSeats   = new List<int>(TrickWinnerSeats),
                    Standings          = new List<SeatStanding>(Standings),
                    ThinkDelays        = new List<MetaDuration>(ThinkDelays),
                    IllegalOffers      = new List<string>(IllegalOffers),
                    Refusals           = new List<MoveRefusalReason>(Refusals),
                };
                return copy;
            }
        }

        /// <summary>
        /// Play one whole game with a bot in every seat.
        /// <para>
        /// Each seat chooses from its own <see cref="MatchSeatView"/>, and the chosen card is checked against the
        /// authoritative hand with <see cref="MatchRules.IsLegalPlay"/> before it is submitted. When the engine
        /// refuses a card, the refusal is recorded and the first legal card is played instead, so the game always
        /// finishes and a broken chooser shows up in the log instead of stalling the loop.
        /// </para>
        /// </summary>
        /// <param name="chooserOverride">Replaces <see cref="BotPolicy.Decide"/> when set. Negative-control tests use it to play illegal cards.</param>
        public static GameLog PlayGame(ulong dealSeed, ulong botSeed, IReadOnlyList<BotProfile> profiles, MatchTimings timings, Func<MatchSeatView, Card> chooserOverride = null)
        {
            MetaTime    now    = MatchTestDeals.T0;
            MatchEngine engine = MatchEngine.Create(dealSeed, timings, now);

            GameLog log = new GameLog
            {
                DealSeed           = dealSeed,
                BotSeed            = botSeed,
                TrumpSuit          = engine.TrumpSuit,
                StartingLeaderSeat = engine.StartingLeaderSeat,
            };

            int loopIterations = 0;
            while (!engine.IsFinished)
            {
                if (++loopIterations > 200)
                    throw new InvalidOperationException($"Game did not finish ({log})");

                if (engine.TurnPhase == MatchTurnPhase.ResolvingTrick)
                {
                    now = engine.ResolvePauseEndsAt;
                    engine.Advance(now);
                    continue;
                }

                int           seat = engine.SeatOnTurn;
                MatchSeatView view = MatchSeatView.ForSeat(engine, seat);

                Card card;
                if (chooserOverride != null)
                {
                    card = chooserOverride(view);
                }
                else
                {
                    BotDecision decision = BotPolicy.Decide(view, profiles[seat], timings, botSeed);
                    if (decision.Seat != seat || decision.PlayIndex != engine.PlayIndex)
                        throw new InvalidOperationException($"Decision addressed seat {decision.Seat} at index {decision.PlayIndex}, expected seat {seat} at index {engine.PlayIndex}");
                    log.ThinkDelays.Add(decision.ThinkDelay);
                    card = decision.Card;
                }

                // Check against the authoritative hand, not the chooser's view, so a chooser that used wrong or
                // hidden information is caught.
                IReadOnlyList<Card> hand = engine.GetHand(seat);
                if (!Contains(hand, card) || !MatchRules.IsLegalPlay(hand, engine.LedSuit, card))
                    log.IllegalOffers.Add($"seat {seat} offered {card} at index {engine.PlayIndex} (led {engine.LedSuit}, trump {engine.TrumpSuit})");

                MoveResult result = engine.PlayCard(seat, engine.PlayIndex, card, now);
                if (!result.Accepted)
                {
                    log.Refusals.Add(result.Refusal);
                    engine.PlayCard(seat, engine.PlayIndex, engine.GetLegalPlays(seat)[0], now);
                }
            }

            log.Plays.AddRange(engine.Plays);
            log.TrickWinnerSeats.AddRange(engine.TrickWinnerSeats);
            log.Standings.AddRange(engine.ComputeStandings());
            return log;
        }

        /// <summary>
        /// Check every invariant a finished game must satisfy. Returns one message per violation, or an empty list
        /// when the game is valid.
        /// </summary>
        public static List<string> FindGameInvariantViolations(GameLog log)
        {
            List<string> violations = new List<string>();

            foreach (string offer in log.IllegalOffers)
                violations.Add($"illegal card offered: {offer}");
            foreach (MoveRefusalReason refusal in log.Refusals)
                violations.Add($"move refused: {refusal}");

            // Every card is played once, and each seat plays its whole hand.
            if (log.Plays.Count != MatchRules.NumPlays)
                violations.Add($"{log.Plays.Count} cards were played, expected {MatchRules.NumPlays}");

            HashSet<Card> playedCards = new HashSet<Card>();
            int[]         playsBySeat = new int[MatchRules.NumSeats];
            foreach (PlayRecord play in log.Plays)
            {
                if (!playedCards.Add(play.Card))
                    violations.Add($"{play.Card} was played twice");
                if (play.Seat < 0 || play.Seat >= MatchRules.NumSeats)
                    violations.Add($"a card was played from seat {play.Seat}");
                else
                    playsBySeat[play.Seat]++;
            }
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
            {
                if (playsBySeat[seat] != MatchRules.CardsPerSeat)
                    violations.Add($"seat {seat} played {playsBySeat[seat]} cards, expected {MatchRules.CardsPerSeat}");
            }

            // The game resolves every trick, and the tricks won add up to the number of tricks.
            if (log.TrickWinnerSeats.Count != MatchRules.NumTricks)
                violations.Add($"{log.TrickWinnerSeats.Count} tricks were resolved, expected {MatchRules.NumTricks}");

            int[] tricksWon = new int[MatchRules.NumSeats];
            foreach (int winnerSeat in log.TrickWinnerSeats)
            {
                if (winnerSeat < 0 || winnerSeat >= MatchRules.NumSeats)
                    violations.Add($"trick won by seat {winnerSeat}");
                else
                    tricksWon[winnerSeat]++;
            }

            int totalTricks = 0;
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                totalTricks += tricksWon[seat];
            if (totalTricks != MatchRules.NumTricks)
                violations.Add($"tricks won sum to {totalTricks}, expected {MatchRules.NumTricks}");

            // Each trick is credited to the seat MatchRules.ResolveTrick picks, the winner leads the next trick,
            // and play goes round the table in seat order.
            for (int trickNdx = 0; trickNdx < log.TrickWinnerSeats.Count; trickNdx++)
            {
                int firstPlayNdx = trickNdx * MatchRules.CardsPerTrick;
                if (firstPlayNdx + MatchRules.CardsPerTrick > log.Plays.Count)
                    break;

                List<PlayRecord> trick = log.Plays.GetRange(firstPlayNdx, MatchRules.CardsPerTrick);
                int expectedWinner = MatchRules.ResolveTrick(trick, log.TrumpSuit);
                if (expectedWinner != log.TrickWinnerSeats[trickNdx])
                    violations.Add($"trick {trickNdx} was credited to seat {log.TrickWinnerSeats[trickNdx]}, but {expectedWinner} won it");

                int expectedLeader = trickNdx == 0 ? log.StartingLeaderSeat : log.TrickWinnerSeats[trickNdx - 1];
                if (trick[0].Seat != expectedLeader)
                    violations.Add($"trick {trickNdx} was led by seat {trick[0].Seat}, expected {expectedLeader}");
                for (int posNdx = 1; posNdx < trick.Count; posNdx++)
                {
                    int expectedSeat = (expectedLeader + posNdx) % MatchRules.NumSeats;
                    if (trick[posNdx].Seat != expectedSeat)
                        violations.Add($"trick {trickNdx} position {posNdx} was played by seat {trick[posNdx].Seat}, expected {expectedSeat}");
                }
            }

            violations.AddRange(FindStandingsOrderViolations(log, tricksWon));
            return violations;
        }

        /// <summary>
        /// Check that the standings are a strict total order over the seats: one position per seat, ranks in
        /// position order, strictly ordered by <see cref="Compare"/>, and each position's
        /// <see cref="SeatStanding.SeparatedFromNextBy"/> naming the rule that put it ahead of the next position.
        /// <para>
        /// The separation is checked across self-played games, not only in hand-built cases, because the results
        /// screen shows it as text. A wrong separation, such as "won on the more recent trick" between seats with
        /// different trick counts, passes every ordering check.
        /// </para>
        /// </summary>
        public static List<string> FindStandingsOrderViolations(GameLog log, int[] tricksWon)
        {
            List<string> violations = new List<string>();

            if (log.Standings.Count != MatchRules.NumSeats)
            {
                violations.Add($"standings hold {log.Standings.Count} positions, expected {MatchRules.NumSeats}");
                return violations;
            }

            HashSet<int> seatsSeen = new HashSet<int>();
            for (int ndx = 0; ndx < log.Standings.Count; ndx++)
            {
                SeatStanding standing = log.Standings[ndx];
                if (standing.Position != ndx)
                    violations.Add($"position {ndx} carries position {standing.Position}");
                if (!seatsSeen.Add(standing.Seat))
                    violations.Add($"seat {standing.Seat} appears twice in the standings");
                if (standing.Seat >= 0 && standing.Seat < MatchRules.NumSeats && standing.TricksWon != tricksWon[standing.Seat])
                    violations.Add($"seat {standing.Seat} is credited {standing.TricksWon} tricks, but won {tricksWon[standing.Seat]}");
            }
            if (seatsSeen.Count != MatchRules.NumSeats)
                violations.Add("the standings are not a permutation of the four seats");

            for (int ndx = 1; ndx < log.Standings.Count; ndx++)
            {
                SeatStanding better = log.Standings[ndx - 1];
                SeatStanding worse  = log.Standings[ndx];
                if (Compare(better, worse) >= 0)
                    violations.Add($"standings are not strictly ordered: {better} is not ahead of {worse}");

                // Two seats can tie on both tricks won and the last trick won only when neither won a trick.
                if (better.TricksWon == worse.TricksWon && better.LastTrickWonIndex == worse.LastTrickWonIndex && better.TricksWon != 0)
                    violations.Add($"{better} and {worse} are tied on tricks and recency while holding tricks");

                StandingSeparation expected = SeparationBetween(better, worse);
                if (better.SeparatedFromNextBy != expected)
                    violations.Add($"{better} claims to be ahead of {worse} by {better.SeparatedFromNextBy}, but it is {expected}");
            }

            // The last position has no position below it, so it must carry no separation.
            SeatStanding last = log.Standings[log.Standings.Count - 1];
            if (last.SeparatedFromNextBy != StandingSeparation.None)
                violations.Add($"the last seat {last} carries a separation, but there is nothing below it");

            return violations;
        }

        /// <summary>
        /// The rule of <see cref="Compare"/> that separates two adjacent standings, computed from their fields
        /// rather than read from <see cref="SeatStanding.SeparatedFromNextBy"/>. It checks the same rules in the
        /// same order as <see cref="Compare"/>.
        /// </summary>
        public static StandingSeparation SeparationBetween(SeatStanding better, SeatStanding worse)
        {
            if (better.TricksWon != worse.TricksWon)
                return StandingSeparation.MoreTricks;
            if (better.LastTrickWonIndex != worse.LastTrickWonIndex)
                return StandingSeparation.MoreRecentTrick;
            return StandingSeparation.NoTricksEither;
        }

        /// <summary>
        /// The standings order: more tricks won first, then the seat that won a trick more recently, then the lower
        /// seat index. The seat index only decides between seats that won no tricks.
        /// </summary>
        public static int Compare(SeatStanding a, SeatStanding b)
        {
            if (a.TricksWon != b.TricksWon)
                return b.TricksWon.CompareTo(a.TricksWon);
            if (a.LastTrickWonIndex != b.LastTrickWonIndex)
                return b.LastTrickWonIndex.CompareTo(a.LastTrickWonIndex);
            return a.Seat.CompareTo(b.Seat);
        }

        public static bool Contains(IReadOnlyList<Card> cards, Card card)
        {
            foreach (Card candidate in cards)
            {
                if (candidate == card)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// A profile per seat drawn from <see cref="TestBotConfig.Opponents"/> with the table's seed.
        /// </summary>
        public static List<BotProfile> DrawProfiles(ulong seed)
        {
            List<BotProfile> profiles = new List<BotProfile>(MatchRules.NumSeats);
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                profiles.Add(BotProfiles.DrawForSeat(TestBotConfig.Opponents, seed, seat));
            return profiles;
        }

        public static List<BotProfile> SameProfileAtEverySeat(BotProfile profile)
        {
            List<BotProfile> profiles = new List<BotProfile>(MatchRules.NumSeats);
            for (int seat = 0; seat < MatchRules.NumSeats; seat++)
                profiles.Add(profile);
            return profiles;
        }
    }
}
