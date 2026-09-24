using System.Collections.Generic;

namespace Game.Logic
{
    /// <summary>
    /// Everything the Heist decides, as pure functions: what a tier owes the winner, which cards are still on
    /// the menu, what an absent winner would have picked, what one pick does to one collection, and what the
    /// table's next step is.
    /// <para>
    /// <b>It is shared rather than server-side, and that is the point.</b> The Heist screen states the payout
    /// in words before the player commits to it, and the client cannot see the server assembly — so a
    /// server-only policy would mean the screen carrying a second copy of the rule whose whole job is to be
    /// honest about what a pick costs. One implementation, called by the actor and by the screen.
    /// </para>
    /// <para>
    /// Nothing here reads <see cref="MatchModel.Secret"/> and nothing here writes a model: every input is
    /// public on the replicated match or on the reader's own account.
    /// </para>
    /// </summary>
    public static class MatchHeistPolicy
    {
        // ---------------------------------------------------------------- the menu

        /// <summary>
        /// The cards a seat played from its own starting deck, minus the ones <b>its own owner</b> had locked
        /// at enqueue. A seat's cards are protected by its own owner's locks.
        /// <para>
        /// Played order is kept and duplicates survive: a seat that played two copies offers two. A null
        /// locked set means nothing was locked.
        /// </para>
        /// <para>
        /// The rows carry the rank their owner brought the card at, and the subtraction is by <em>card</em>: a
        /// lock protects a card whatever rank it is held at, and the rank travels with the row because a pick
        /// moves a card at a rank.
        /// </para>
        /// <para>
        /// It composes, and the Heist menu uses that: the loser's list has the <em>winner's</em> frozen locks
        /// subtracted as well, because a card the winner has frozen can neither gain nor lose a rank, so
        /// offering it would be a pick that takes a rank off the loser and pays nothing.
        /// </para>
        /// </summary>
        public static List<HeistEligibleCard> Eligible(IReadOnlyList<HeistEligibleCard> played, IReadOnlyList<CardId> locked)
        {
            if (played == null)
                return new List<HeistEligibleCard>();

            HashSet<CardId> frozen = new HashSet<CardId>();
            if (locked != null)
            {
                foreach (CardId cardId in locked)
                    frozen.Add(cardId);
            }

            List<HeistEligibleCard> eligible = new List<HeistEligibleCard>(played.Count);
            foreach (HeistEligibleCard row in played)
            {
                if (!frozen.Contains(row.Card))
                    eligible.Add(row);
            }

            return eligible;
        }

        /// <summary>
        /// Per seat, what a finished table owes the Heist screen: the cards that seat played from its own
        /// starting deck, minus the locks that protect them.
        /// <para>
        /// <b>The loser's row is the menu, and it takes two subtractions rather than one.</b> Its owner's
        /// frozen set protects it, and so does the <em>winner's</em>: a lock runs both ways, so a card the
        /// winner has frozen can neither gain a rank nor lose one, and offering it would be a pick that takes a
        /// rank off the loser and pays the winner nothing, so such a pick never reaches the menu. The winner's
        /// own row gets one pass, because nothing picks from it.
        /// </para>
        /// <para>
        /// It is here rather than composed at the call site because a composition inside a private actor method
        /// is a rule only a copy of itself can test — and the second pass is exactly the kind of line a later
        /// refactor drops silently.
        /// </para>
        /// </summary>
        public static List<List<HeistEligibleCard>> Eligibility(MatchResult result, MatchStakes stakes)
        {
            List<List<HeistEligibleCard>> eligibility = new List<List<HeistEligibleCard>>(MatchSeats.Count);
            int                           winnerSeat  = result != null ? result.WinnerSeat : MatchSeats.None;

            for (int seat = 0; seat < MatchSeats.Count; seat++)
            {
                List<HeistEligibleCard> eligible = Eligible(result?.PlayedBy(seat), LockedBy(stakes, seat));

                if (MatchSeats.IsValid(winnerSeat) && seat != winnerSeat)
                    eligible = Eligible(eligible, LockedBy(stakes, winnerSeat));

                eligibility.Add(eligible);
            }

            return eligibility;
        }

        /// <summary> One seat's frozen set, or null where a table carries none. </summary>
        static IReadOnlyList<CardId> LockedBy(MatchStakes stakes, int seat)
            => stakes?.LockedCards != null && seat >= 0 && seat < stakes.LockedCards.Count ? stakes.LockedCards[seat] : null;

        /// <summary>
        /// The menu with the picks already taken removed, <b>by occurrence rather than by card</b>. Duplicates
        /// are deliberately kept on the eligible list — a seat that played two copies offers two — so a
        /// subtraction by card id would let one pick take both of them off the menu at once.
        /// </summary>
        public static List<HeistEligibleCard> Remaining(IReadOnlyList<HeistEligibleCard> eligible, IReadOnlyList<CardId> picksSoFar)
        {
            List<HeistEligibleCard> remaining = new List<HeistEligibleCard>();
            if (eligible != null)
                remaining.AddRange(eligible);

            if (picksSoFar == null)
                return remaining;

            foreach (CardId picked in picksSoFar)
                RemoveOneOccurrence(remaining, picked);

            return remaining;
        }

        static void RemoveOneOccurrence(List<HeistEligibleCard> rows, CardId card)
        {
            for (int ndx = 0; ndx < rows.Count; ndx++)
            {
                if (rows[ndx].Card == card)
                {
                    rows.RemoveAt(ndx);
                    return;
                }
            }
        }

        static bool Holds(IReadOnlyList<HeistEligibleCard> rows, CardId card)
        {
            foreach (HeistEligibleCard row in rows)
            {
                if (row.Card == card)
                    return true;
            }

            return false;
        }

        // ---------------------------------------------------------------- what the tier owes

        /// <summary>
        /// How many picks the winner is owed, from the frozen tier and whether the winner is the seat that
        /// entered ahead on Power Score. <c>Docs/game-design.md</c>'s payout table is phrased in terms of who wins,
        /// which is not knowable at formation, so the tier records seat 0's side of it and this is where the
        /// two become one answer.
        /// <para>
        /// Punching down pays nothing and an upset pays double, which is the whole of the asymmetric-stakes
        /// design: the favourite's win takes no rank, and the underdog's takes two.
        /// </para>
        /// </summary>
        public static int PicksOwed(StakesTier tier, bool winnerWasTheFormationFavourite)
        {
            switch (tier)
            {
                case StakesTier.Even:
                    return 1;

                case StakesTier.Favourite:
                case StakesTier.Underdog:
                    return winnerWasTheFormationFavourite ? 0 : 2;

                default:
                    // Practice puts no ranks at stake at all, and the newcomer shield is its own tier for
                    // exactly this reason: no rank moves in either direction.
                    return 0;
            }
        }

        /// <summary>
        /// Whether the seat that entered as the Power-Score favourite is <paramref name="seat"/>, for a tier
        /// that names one. Both clients render the identical stakes record and only the copy's framing differs
        /// per local seat — a tier that applied to one player and was visible to only that player would be
        /// exactly the surprise the pre-match screen exists to prevent.
        /// </summary>
        public static bool SeatIsTheFavourite(StakesTier tier, int seat)
        {
            if (tier == StakesTier.Favourite)
                return seat == 0;
            if (tier == StakesTier.Underdog)
                return seat == 1;

            return false;
        }

        /// <summary>
        /// How many picks this match owes, off the two public facts the table carries. Zero for anything
        /// unranked, for a draw — neither seat is the loser — and for the two tiers that move nothing.
        /// </summary>
        public static int PicksOwedForMatch(MatchStakes stakes, MatchOutcomeRecord result)
        {
            if (stakes == null || !stakes.IsRanked || result == null || result.IsDraw || !MatchSeats.IsValid(result.WinnerSeat))
                return 0;

            return PicksOwed(stakes.Tier, SeatIsTheFavourite(stakes.Tier, result.WinnerSeat));
        }

        /// <summary>
        /// Whether the table runs a <see cref="MatchTablePhase.HeistPick"/> phase at all: a pick is owed and
        /// there is something on the menu to pick. A short menu takes what is there and an empty one never
        /// enters the phase — nothing deadlocks on a card that does not exist, and a phase with an empty stage
        /// would be a screen that asks for a decision nobody can make.
        /// </summary>
        public static bool PhaseRuns(MatchStakes stakes, MatchOutcomeRecord result, IReadOnlyList<HeistEligibleCard> loserEligible)
            => PicksOwedForMatch(stakes, result) > 0 && loserEligible != null && loserEligible.Count > 0;

        // ---------------------------------------------------------------- the absent winner's pick

        /// <summary>
        /// What the strongest bot profile would take: the highest-ranked card on the menu, ties broken by
        /// rarity, then by mana cost, then by canonical card order. Null for an empty menu.
        /// <para>
        /// It is a deterministic function of <em>public</em> state — a played card's rank is on the board the
        /// moment it is played — so a defaulted pick is one the other player could have predicted rather than
        /// a secret decision made on somebody's behalf (<c>Docs/match.md</c>, "The deadline
        /// defaults in the absent player's own interest"). The final tie-break is
        /// <see cref="CardInfo.CompareCanonical"/>, the same order the graveyard selectors and the peek's own
        /// default use, so nothing in the game ever breaks a tie by iterating the config library.
        /// </para>
        /// </summary>
        public static CardId AutoDefault(IReadOnlyList<HeistEligibleCard> menu, SharedGameConfig config)
        {
            if (menu == null || menu.Count == 0)
                return null;

            HeistEligibleCard best = menu[0];
            for (int ndx = 1; ndx < menu.Count; ndx++)
            {
                if (IsWorthMore(menu[ndx], best, config))
                    best = menu[ndx];
            }

            return best.Card;
        }

        static bool IsWorthMore(HeistEligibleCard candidate, HeistEligibleCard incumbent, SharedGameConfig config)
        {
            if (candidate.OwnedRank != incumbent.OwnedRank)
                return candidate.OwnedRank > incumbent.OwnedRank;

            CardRarity candidateRarity = RarityOf(candidate.Card, config);
            CardRarity incumbentRarity = RarityOf(incumbent.Card, config);
            if (candidateRarity != incumbentRarity)
                return candidateRarity > incumbentRarity;

            int candidateCost = CostOf(candidate.Card, config);
            int incumbentCost = CostOf(incumbent.Card, config);
            if (candidateCost != incumbentCost)
                return candidateCost > incumbentCost;

            return CardInfo.CompareCanonical(candidate.Card, incumbent.Card) < 0;
        }

        static CardRarity RarityOf(CardId card, SharedGameConfig config)
            => Info(card, config) is CardInfo info ? info.Rarity : CardRarity.Common;

        static int CostOf(CardId card, SharedGameConfig config)
            => Info(card, config) is CardInfo info ? info.Cost : 0;

        static CardInfo Info(CardId card, SharedGameConfig config)
            => config != null && card != null && config.Cards.TryGetValue(card, out CardInfo info) ? info : null;

        // ---------------------------------------------------------------- the transfer, one side at a time

        /// <summary>
        /// What one pick does to the <b>winner's</b> copy, from that account's own live state. A locked card
        /// can neither lose a rank nor gain one, so a winner who has frozen their own copy gains nothing —
        /// which is why the menu subtracts their frozen set too, and why this still refuses: the lock is
        /// re-read at the moment the transfer applies, and an account may have locked the card in the seconds
        /// since the pick.
        /// </summary>
        public static HeistRankMove WinnerGain(bool owns, int rank, bool locked, GlobalConfig global)
        {
            if (!owns)
                return new HeistRankMove(HeistMove.Acquired, 0, global.RankMin);

            if (locked)
                return new HeistRankMove(HeistMove.Frozen, rank, rank);

            if (rank >= global.RankMax)
                return new HeistRankMove(HeistMove.AtCeiling, rank, rank);

            return new HeistRankMove(HeistMove.Moved, rank, rank + 1);
        }

        /// <summary>
        /// What one pick does to the <b>loser's</b> copy: one rank off, floored. <b>Cards are never removed
        /// from a collection</b> — the floor is safe — and a card this account has since locked moves not at
        /// all, which is the promise the live re-read exists to keep on the retry path as well as the first
        /// one.
        /// </summary>
        public static HeistRankMove LoserLoss(bool owns, int rank, bool locked, GlobalConfig global)
        {
            if (!owns)
                return new HeistRankMove(HeistMove.NotOwned, 0, 0);

            if (locked)
                return new HeistRankMove(HeistMove.Frozen, rank, rank);

            if (rank <= global.RankMin)
                return new HeistRankMove(HeistMove.AtFloor, rank, rank);

            return new HeistRankMove(HeistMove.Moved, rank, rank - 1);
        }

        // ---------------------------------------------------------------- the table's next step

        /// <summary>
        /// What the table does next, as one verdict per call. Every branch of the phase goes through here — a
        /// pick arriving, a lapsed clock, a winner who has gone — so the ordering rules can be tested without
        /// an actor, which <c>Server.Tests</c> cannot stand up.
        /// <para>
        /// <paramref name="picked"/> null means "no pick": a lapsed clock while the winner is still present
        /// defaults <b>one</b> slot and the next is clocked again, and a winner who is not there has every
        /// remaining slot defaulted at once rather than the loser's collection waiting on somebody who closed
        /// their tab.
        /// </para>
        /// </summary>
        public static MatchHeistVerdict Resolve(MatchHeistPosition position, int fromSeat, CardId picked, SharedGameConfig config)
        {
            bool isAPick = picked != null;

            if (position.Phase != MatchTablePhase.HeistPick)
                return isAPick ? MatchHeistVerdict.Refuse(MatchRefusalCode.NotYourTurn) : MatchHeistVerdict.Nothing;

            // The pick belongs to the winner and to nobody else. The loser owes nothing here at all, so an
            // intent from that seat is a client offering a decision it does not have.
            if (isAPick && fromSeat != position.WinnerSeat)
                return MatchHeistVerdict.Refuse(MatchRefusalCode.NotYourTurn);

            int                     owedNow   = position.PicksOwed - position.PicksSoFar.Count;
            List<HeistEligibleCard> remaining = Remaining(position.Eligible, position.PicksSoFar);

            if (owedNow <= 0 || remaining.Count == 0)
            {
                // Nothing left to take: a pick is refused, and a clock or a departure simply ends the phase.
                return isAPick
                    ? MatchHeistVerdict.Refuse(MatchRefusalCode.NotEligible)
                    : MatchHeistVerdict.Take(new List<CardId>(), anyAutoDefaulted: false, thenEnd: true);
            }

            if (isAPick)
            {
                if (!Holds(remaining, picked))
                    return MatchHeistVerdict.Refuse(MatchRefusalCode.NotEligible);

                List<CardId> taken = new List<CardId> { picked };

                if (owedNow == 1)
                    return MatchHeistVerdict.Take(taken, anyAutoDefaulted: false, thenEnd: true);

                if (position.WinnerIsPresent)
                    return MatchHeistVerdict.Take(taken, anyAutoDefaulted: false, thenEnd: false);

                // The winner has gone between their two picks. The second is defaulted rather than clocked.
                AppendDefaults(taken, Remaining(remaining, taken), owedNow - 1, config);
                return MatchHeistVerdict.Take(taken, anyAutoDefaulted: true, thenEnd: true);
            }

            List<CardId> defaults = new List<CardId>();
            AppendDefaults(defaults, remaining, position.WinnerIsPresent ? 1 : owedNow, config);

            bool moreToTake = owedNow - defaults.Count > 0 && Remaining(remaining, defaults).Count > 0;

            return MatchHeistVerdict.Take(
                defaults,
                anyAutoDefaulted: defaults.Count > 0,
                thenEnd: !(position.WinnerIsPresent && moreToTake));
        }

        static void AppendDefaults(List<CardId> into, IReadOnlyList<HeistEligibleCard> menu, int count, SharedGameConfig config)
        {
            List<HeistEligibleCard> left = new List<HeistEligibleCard>();
            left.AddRange(menu);

            for (int taken = 0; taken < count && left.Count > 0; taken++)
            {
                CardId next = AutoDefault(left, config);
                into.Add(next);
                RemoveOneOccurrence(left, next);
            }
        }
    }

    /// <summary> What one pick does to one account's copy of the card. </summary>
    public enum HeistMove
    {
        /// <summary> A rank moved: up on the winner's side, down on the loser's. </summary>
        Moved     = 0,
        /// <summary> The winner owned no copy, so one arrives at the rank floor. </summary>
        Acquired  = 1,
        /// <summary> Nothing: the winner's copy is already at the ceiling. </summary>
        AtCeiling = 2,
        /// <summary> Nothing: this account has frozen its own copy, and a lock runs both ways. </summary>
        Frozen    = 3,
        /// <summary> Nothing: the loser's copy is already at the floor, and no card is ever removed. </summary>
        AtFloor   = 4,
        /// <summary> Nothing: the loser does not own a copy at all. </summary>
        NotOwned  = 5,
    }

    /// <summary>
    /// One account's half of the transfer: which way it goes, and the two ranks the screen spells out. The
    /// two ranks are equal exactly when nothing moves, so <see cref="Moves"/> is derived rather than a second
    /// answer that could disagree with them.
    /// </summary>
    public readonly struct HeistRankMove
    {
        public readonly HeistMove Kind;
        /// <summary> The rank held before, or zero when the account owns no copy. </summary>
        public readonly int       From;
        /// <summary> The rank held after. </summary>
        public readonly int       To;

        public HeistRankMove(HeistMove kind, int from, int to)
        {
            Kind = kind;
            From = from;
            To   = to;
        }

        public bool Moves => To != From;

        public override string ToString() => $"{Kind} {From}→{To}";
    }

    /// <summary> What the table's Heist phase does next. </summary>
    public enum MatchHeistStep
    {
        /// <summary> Nothing at all: the phase is not running. </summary>
        Nothing    = 0,
        /// <summary> The pick is refused and no state moves. </summary>
        Refuse     = 1,
        /// <summary> These picks land, and the phase waits on another one with a fresh clock. </summary>
        TakeAndArm = 2,
        /// <summary> These picks land and the phase is over. </summary>
        TakeAndEnd = 3,
    }

    /// <summary> One step of the Heist phase: what is taken, whether it was a choice, and what happens next. </summary>
    public readonly struct MatchHeistVerdict
    {
        public readonly MatchHeistStep   Step;
        /// <summary> The cards this step adds to the record, in the order they are taken. Never null. </summary>
        public readonly List<CardId>     Picks;
        /// <summary> Whether any pick in this step was the deterministic default rather than a choice. </summary>
        public readonly bool             AnyAutoDefaulted;
        /// <summary> Meaningful only for <see cref="MatchHeistStep.Refuse"/>. </summary>
        public readonly MatchRefusalCode Refusal;

        MatchHeistVerdict(MatchHeistStep step, List<CardId> picks, bool anyAutoDefaulted, MatchRefusalCode refusal)
        {
            Step             = step;
            Picks            = picks ?? new List<CardId>();
            AnyAutoDefaulted = anyAutoDefaulted;
            Refusal          = refusal;
        }

        public static MatchHeistVerdict Nothing
            => new MatchHeistVerdict(MatchHeistStep.Nothing, null, false, MatchRefusalCode.Stale);

        public static MatchHeistVerdict Refuse(MatchRefusalCode code)
            => new MatchHeistVerdict(MatchHeistStep.Refuse, null, false, code);

        public static MatchHeistVerdict Take(List<CardId> picks, bool anyAutoDefaulted, bool thenEnd)
            => new MatchHeistVerdict(thenEnd ? MatchHeistStep.TakeAndEnd : MatchHeistStep.TakeAndArm, picks, anyAutoDefaulted, MatchRefusalCode.Stale);

        public override string ToString() => $"{Step} [{string.Join(", ", Picks)}]{(AnyAutoDefaulted ? " defaulted" : "")}";
    }

    /// <summary>
    /// Everything the next step is decided from, all of it public on the replicated match. Bundled so that a
    /// caller cannot transpose two same-typed arguments, and so that the actor and a test read the position
    /// the same way.
    /// </summary>
    public readonly struct MatchHeistPosition
    {
        public readonly MatchTablePhase                  Phase;
        /// <summary> The seat that owes the picks. </summary>
        public readonly int                              WinnerSeat;
        public readonly int                              PicksOwed;
        /// <summary> The loser's menu as it was frozen when the result was recorded. </summary>
        public readonly IReadOnlyList<HeistEligibleCard> Eligible;
        /// <summary> What has been taken already, so the menu can be subtracted by occurrence. </summary>
        public readonly IReadOnlyList<CardId>            PicksSoFar;
        /// <summary>
        /// Whether the winner is a person who is here. A covered seat is <b>not</b> present: the auto-default
        /// is what a bot would pick anyway, so there is nothing for a clock to wait for.
        /// </summary>
        public readonly bool                             WinnerIsPresent;

        public MatchHeistPosition(
            MatchTablePhase phase,
            int winnerSeat,
            int picksOwed,
            IReadOnlyList<HeistEligibleCard> eligible,
            IReadOnlyList<CardId> picksSoFar,
            bool winnerIsPresent)
        {
            Phase           = phase;
            WinnerSeat      = winnerSeat;
            PicksOwed       = picksOwed;
            Eligible        = eligible ?? new List<HeistEligibleCard>();
            PicksSoFar      = picksSoFar ?? new List<CardId>();
            WinnerIsPresent = winnerIsPresent;
        }

        /// <summary>
        /// The position off a live table. <paramref name="winnerIsPresent"/> is the host's own answer —
        /// <c>MatchSeatPolicy.HeistDeadlineArmed</c> — because occupancy is the actor's business and not the
        /// game's.
        /// </summary>
        public static MatchHeistPosition Of(MatchModel match, bool winnerIsPresent)
        {
            MatchOutcomeRecord result = match?.Result;
            int                winner = result != null ? result.WinnerSeat : MatchSeats.None;

            return new MatchHeistPosition(
                match?.Phase ?? MatchTablePhase.Playing,
                winner,
                MatchHeistPolicy.PicksOwedForMatch(match?.Stakes, result),
                MatchSeats.IsValid(winner) && match.HeistEligibility != null
                    ? match.HeistEligibility[MatchSeats.Other(winner)]
                    : null,
                result?.Heist?.Picks,
                winnerIsPresent);
        }
    }
}
