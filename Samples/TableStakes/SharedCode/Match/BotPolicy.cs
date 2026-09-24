using Metaplay.Core;
using System;
using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// A bot's move for one turn: the card, the play index it was decided at, and how long the host waits before
    /// submitting it.
    /// <para>
    /// The host submits the move after a timer, and by then the seat may have been reclaimed or the table may have
    /// ended. The engine refuses a move whose play index is not the current one, so a stale move is rejected
    /// without other checks (see <c>docs/match.md</c>, "The play index").
    /// </para>
    /// </summary>
    public readonly struct BotDecision
    {
        public readonly int  Seat;
        public readonly int  PlayIndex;
        public readonly Card Card;

        /// <summary>
        /// The minimum time the host waits before submitting the move. The policy does not wait itself. The host
        /// arms a timer for this duration. Zero when the host's timings are zero and when a table is played out.
        /// </summary>
        public readonly MetaDuration ThinkDelay;

        public BotDecision(int seat, int playIndex, Card card, MetaDuration thinkDelay)
        {
            Seat       = seat;
            PlayIndex  = playIndex;
            Card       = card;
            ThinkDelay = thinkDelay;
        }

        public override string ToString() => $"seat {Seat} plays {Card} at index {PlayIndex} after {ThinkDelay}";
    }

    /// <summary>
    /// Chooses the card a bot plays. Every decision is a pure function of the seat's own view and a seed.
    /// <para>
    /// A hand is only five cards, so the policy does not search ahead. If the seat can win the current trick it plays
    /// the cheapest winning card, otherwise the cheapest card to lose (<c>docs/bots.md</c>). Every path picks from
    /// <see cref="MatchRules.GetLegalPlays"/>, so a bot cannot play an illegal card.
    /// </para>
    /// </summary>
    public static class BotPolicy
    {
        // Separate salts give the card choice and the think delay independent random streams, so changing how
        // one draws does not change the other. CreateRng also mixes in the seat and play index, so one seed per
        // table gives a different draw at every move.
        const ulong CardSalt  = 0x2545F4914F6CDD1DUL;
        const ulong DelaySalt = 0x9E3779B97F4A7C15UL;

        #region Deciding a card

        /// <summary>
        /// Decide a card and a think delay for a bot seat.
        /// </summary>
        /// <param name="view">The deciding seat's own view, which does not contain other seats' cards.</param>
        /// <param name="profile">The seat's strength profile, drawn when the table formed.</param>
        /// <param name="timings">The host's durations. All-zero timings give a zero delay.</param>
        /// <param name="seed">The host's bot seed. It differs from the deal seed and is never sent to a client.</param>
        public static BotDecision Decide(MatchSeatView view, BotProfile profile, MatchTimings timings, ulong seed)
        {
            Card card = ChooseCard(view, profile, seed);
            return new BotDecision(view.Seat, view.Board.PlayIndex, card, DrawThinkDelay(view, profile, timings, seed));
        }

        /// <summary>
        /// The card this seat plays, as a pure function of <paramref name="view"/>, <paramref name="profile"/> and
        /// <paramref name="seed"/>. Throws <see cref="InvalidOperationException"/> when the seat has no legal play.
        /// </summary>
        public static Card ChooseCard(MatchSeatView view, BotProfile profile, ulong seed)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            List<Card> legal = view.GetLegalPlays();
            if (legal.Count == 0)
                throw new InvalidOperationException($"Seat {view.Seat} was asked for a card while it is not on turn");

            RandomPCG rng = CreateRng(view, seed, CardSalt);

            if (profile.Mode == BotDecisionMode.RandomLegal)
                return rng.Choice(legal);

            if (profile.Mode == BotDecisionMode.LowestLegal)
            {
                List<Card> ordered = new List<Card>(legal);
                ordered.Sort();
                return ordered[0];
            }

            List<Card> ranked = RankLegalPlays(view);

            // A deliberate mistake plays the second-best card, never a worse one, so the error stays plausible
            // for a human player.
            if (ranked.Count >= 2 && profile.MistakeChancePercent > 0 && rng.NextInt(100) < profile.MistakeChancePercent)
                return ranked[1];

            return ranked[0];
        }

        /// <summary>
        /// The legal cards in the heuristic's preference order, best first. <see cref="ChooseCard"/> plays the
        /// first entry, or the second on a deliberate mistake. Public so tests can check the ranking.
        /// </summary>
        public static List<Card> RankLegalPlays(MatchSeatView view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            List<Card> legal = view.GetLegalPlays();
            if (legal.Count <= 1)
                return legal;

            List<PlayRecord> trickPlays = view.Board.GetCurrentTrickPlays();
            if (trickPlays.Count == 0)
                return RankLead(view, legal);
            return RankFollow(view, legal, trickPlays);
        }

        /// <summary>
        /// Ranks the cards for a seat that leads a trick. First come trumps that no unseen trump outranks, which
        /// are sure to win. Next come non-trump cards that no unseen card of their suit outranks, which win unless
        /// someone trumps. The remaining cards follow in <see cref="SortByDiscardCost"/> order.
        /// </summary>
        static List<Card> RankLead(MatchSeatView view, List<Card> legal)
        {
            Suit          trumpSuit = view.TrumpSuit;
            HashSet<Card> seen      = GetSeenCards(view);

            List<Card> masterTrumps    = new List<Card>();
            List<Card> masterNonTrumps = new List<Card>();
            List<Card> rest            = new List<Card>();

            foreach (Card card in legal)
            {
                if (!IsHighestUnseenOfItsSuit(card, seen))
                    rest.Add(card);
                else if (card.Suit == trumpSuit)
                    masterTrumps.Add(card);
                else
                    masterNonTrumps.Add(card);
            }

            masterTrumps.Sort(CompareByRankThenSuit);
            masterNonTrumps.Sort(CompareByRankThenSuit);
            SortByDiscardCost(rest, view);

            List<Card> ranked = new List<Card>(legal.Count);
            ranked.AddRange(masterTrumps);
            ranked.AddRange(masterNonTrumps);
            ranked.AddRange(rest);
            return ranked;
        }

        /// <summary>
        /// Highest rank first, then lowest suit ordinal. The suit key orders two cards of equal rank, such as two
        /// aces. Without it, <see cref="List{T}.Sort(Comparison{T})"/> is unstable and their order would not be
        /// a pure function of the view.
        /// </summary>
        static int CompareByRankThenSuit(Card a, Card b)
        {
            int byRank = ((int)b.Rank).CompareTo((int)a.Rank);
            if (byRank != 0)
                return byRank;
            return ((int)a.Suit).CompareTo((int)b.Suit);
        }

        /// <summary>
        /// Ranks the cards for a seat that follows into a trick with cards in it. Cards that beat the current best
        /// play come first, cheapest first, so the seat keeps its strong cards for later tricks. Cards that cannot
        /// win follow in <see cref="SortByDiscardCost"/> order, which puts trumps last.
        /// </summary>
        static List<Card> RankFollow(MatchSeatView view, List<Card> legal, List<PlayRecord> trickPlays)
        {
            Suit trumpSuit  = view.TrumpSuit;
            Suit ledSuit    = trickPlays[0].Card.Suit;
            Card cardToBeat = trickPlays[MatchRules.GetBestPlayNdx(trickPlays, trumpSuit)].Card;

            List<Card> winners = new List<Card>();
            List<Card> losers  = new List<Card>();
            foreach (Card card in legal)
            {
                if (MatchRules.Beats(card, cardToBeat, ledSuit, trumpSuit))
                    winners.Add(card);
                else
                    losers.Add(card);
            }

            winners.Sort((Card a, Card b) => CompareWinCost(a, b, trumpSuit));
            SortByDiscardCost(losers, view);

            List<Card> ranked = new List<Card>(legal.Count);
            ranked.AddRange(winners);
            ranked.AddRange(losers);
            return ranked;
        }

        /// <summary>Orders winning cards cheapest first: non-trump before trump, then lower rank, then lower suit ordinal.</summary>
        static int CompareWinCost(Card a, Card b, Suit trumpSuit)
        {
            bool aTrump = a.Suit == trumpSuit;
            bool bTrump = b.Suit == trumpSuit;
            if (aTrump != bTrump)
                return aTrump ? 1 : -1;
            if (a.Rank != b.Rank)
                return ((int)a.Rank).CompareTo((int)b.Rank);
            return ((int)a.Suit).CompareTo((int)b.Suit);
        }

        /// <summary>
        /// Orders cards cheapest to lose first: non-trump before trump, then lower rank, then the suit the seat holds
        /// fewer cards of, so longer suits stay intact for later tricks. The suit ordinal breaks remaining ties so
        /// the order is deterministic.
        /// </summary>
        static void SortByDiscardCost(List<Card> cards, MatchSeatView view)
        {
            Suit trumpSuit = view.TrumpSuit;
            int[] suitLength = new int[4];
            foreach (Card card in view.Hand)
                suitLength[(int)card.Suit]++;

            cards.Sort((Card a, Card b) =>
            {
                bool aTrump = a.Suit == trumpSuit;
                bool bTrump = b.Suit == trumpSuit;
                if (aTrump != bTrump)
                    return aTrump ? 1 : -1;
                if (a.Rank != b.Rank)
                    return ((int)a.Rank).CompareTo((int)b.Rank);
                if (suitLength[(int)a.Suit] != suitLength[(int)b.Suit])
                    return suitLength[(int)a.Suit].CompareTo(suitLength[(int)b.Suit]);
                return ((int)a.Suit).CompareTo((int)b.Suit);
            });
        }

        /// <summary>
        /// The cards this seat can see: its own hand, every card played, and the revealed trump card. Any other card
        /// is in another hand or undealt, and the seat's view does not say which.
        /// </summary>
        static HashSet<Card> GetSeenCards(MatchSeatView view)
        {
            HashSet<Card> seen = new HashSet<Card>();
            foreach (Card card in view.Hand)
                seen.Add(card);
            foreach (PlayRecord play in view.Board.Plays)
                seen.Add(play.Card);
            seen.Add(view.Board.TrumpCard);
            return seen;
        }

        static bool IsHighestUnseenOfItsSuit(Card card, HashSet<Card> seen)
        {
            for (int rank = (int)card.Rank + 1; rank <= (int)Rank.Ace; rank++)
            {
                if (!seen.Contains(new Card(card.Suit, (Rank)rank)))
                    return false;
            }
            return true;
        }

        #endregion

        #region Playing on behalf of an absent human

        // These methods take no profile and always use BotProfiles.Strongest. A bot seated at formation may use a
        // weaker profile, but a bot playing a human's cards must not make deliberate mistakes with them.

        /// <summary>
        /// The card for a connected seat whose move deadline passed. The delay is zero because the player already
        /// used the whole deadline.
        /// </summary>
        public static BotDecision DecideAutoPlay(MatchSeatView view, ulong seed) => DecideImmediately(view, seed);

        /// <summary>
        /// The card for a seat a bot is covering. When the owner is connected, the move waits
        /// <see cref="MatchTimings.CoveredSeatReclaimDelay"/>, which gives the owner time to take the seat back by
        /// playing a card. When the owner is disconnected, the move uses the normal think delay.
        /// </summary>
        public static BotDecision DecideCover(MatchSeatView view, MatchTimings timings, ulong seed, bool ownerConnected)
        {
            Card         card  = ChooseCard(view, BotProfiles.Strongest, seed);
            MetaDuration delay = ownerConnected
                ? timings.CoveredSeatReclaimDelay
                : DrawThinkDelay(view, BotProfiles.Strongest, timings, seed);
            return new BotDecision(view.Seat, view.Board.PlayIndex, card, delay);
        }

        /// <summary>
        /// The card for a seat at a table that is played out because no human is left. The delay is zero so the
        /// remaining tricks resolve in one call.
        /// </summary>
        public static BotDecision DecidePlayOut(MatchSeatView view, ulong seed) => DecideImmediately(view, seed);

        /// <summary>
        /// The <see cref="BotProfiles.Strongest"/> card with zero delay. Shared by <see cref="DecideAutoPlay"/> and
        /// <see cref="DecidePlayOut"/>.
        /// </summary>
        static BotDecision DecideImmediately(MatchSeatView view, ulong seed)
        {
            Card card = ChooseCard(view, BotProfiles.Strongest, seed);
            return new BotDecision(view.Seat, view.Board.PlayIndex, card, MetaDuration.Zero);
        }

        /// <summary>
        /// Whether <paramref name="profile"/> may play a seat for an absent human: only a heuristic profile with
        /// zero mistake chance qualifies. The rule is in code rather than a profile field so that editing the
        /// profiles in game config cannot let a weak profile play a human's cards.
        /// </summary>
        public static bool MayPlayForAnAbsentHuman(BotProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));
            return profile.Mode == BotDecisionMode.Heuristic && profile.MistakeChancePercent == 0;
        }

        #endregion

        #region Pacing

        /// <summary>
        /// How long the host waits before submitting this move. Drawn per move from the host's timing ranges, so
        /// bots at one table do not all answer after the same delay. Zero when the ranges are zero.
        /// </summary>
        public static MetaDuration DrawThinkDelay(MatchSeatView view, BotProfile profile, MatchTimings timings, ulong seed)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            RandomPCG rng = CreateRng(view, seed, DelaySalt);

            bool longThink = profile.LongThinkChancePercent > 0 && rng.NextInt(100) < profile.LongThinkChancePercent;
            if (longThink)
                return DrawUniform(rng, timings.BotThinkDelayMax, timings.BotThinkDelayOccasionalMax);
            return DrawUniform(rng, timings.BotThinkDelayMin, timings.BotThinkDelayMax);
        }

        static MetaDuration DrawUniform(RandomPCG rng, MetaDuration min, MetaDuration max)
        {
            long lo = min.Milliseconds;
            long hi = max.Milliseconds;
            if (hi <= lo)
                return MetaDuration.FromMilliseconds(lo);
            return MetaDuration.FromMilliseconds(lo + rng.NextLong(hi - lo + 1));
        }

        #endregion

        #region Helpers

        /// <summary>
        /// The random stream for one decision, derived from the seed, the salt, the seat and the play index. It does
        /// not use the engine's deal RNG, whose state must stay secret from the seats.
        /// </summary>
        static RandomPCG CreateRng(MatchSeatView view, ulong seed, ulong salt)
        {
            ulong mixed = unchecked(seed ^ salt);
            mixed = unchecked(mixed * 0x9E3779B97F4A7C15UL + (ulong)(uint)view.Seat);
            mixed = unchecked(mixed * 0xBF58476D1CE4E5B9UL + (ulong)(uint)view.Board.PlayIndex);
            return RandomPCG.CreateFromSeed(mixed);
        }

        #endregion
    }
}
